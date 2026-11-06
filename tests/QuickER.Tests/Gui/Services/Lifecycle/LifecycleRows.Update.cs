using AwesomeAssertions;
using Moq;
using QuickER.Gui.Abstractions;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Gui.Services.Lifecycle.LifecycleEntrance;
using static QuickER.Tests.Gui.Services.Lifecycle.LifecycleFault;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>
/// アプリの一生の表：更新（QuickER.Gui の <c>UpdateFeed.Resolve</c> と起動時の更新確認 <c>UpdateService</c>）。
/// UI・Velopack に依存せず、<c>IAppUpdater</c> はモックで差し替える
/// </summary>
internal static partial class LifecycleRows
{
    /// <summary>環境変数取得関数を、指定のキー→値の辞書から作る（未登録キーは null）</summary>
    private static Func<string, string?> EnvFrom(params (string Key, string? Value)[] entries)
    {
        var map = new Dictionary<string, string?>();

        foreach (var (key, value) in entries)
        {
            map[key] = value;
        }

        return name => map.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>フィードが必ず解決される固定のフィード解決関数と、指定の更新実行体を返すファクトリでサービスを作る</summary>
    private static UpdateService CreateService(StubDialogService dialog, IAppUpdater updater) =>
        new(dialog, _ => updater, () => @"C:\feed\test", new PostExitActions(), () => { });

    /// <summary>更新が見つかった状態の更新実行体のモックを作る（ダウンロードと適用を呼び出し順に記録する）</summary>
    private static Mock<IAppUpdater> CreateAvailableUpdater(List<string> calls)
    {
        var updater = new Mock<IAppUpdater>(MockBehavior.Strict);
        updater.SetupGet(u => u.IsInstalled).Returns(true);
        updater.Setup(u => u.CheckForUpdateAsync()).ReturnsAsync("2.0.0");
        updater
            .Setup(u => u.DownloadAsync())
            .Callback(() => calls.Add("download"))
            .Returns(Task.CompletedTask);
        updater
            .Setup(u => u.ApplyOnExit(It.IsAny<bool>()))
            .Callback<bool>(restart => calls.Add($"apply(restart: {restart})"));
        return updater;
    }

    /// <summary>1 回目（ダウンロード）・2 回目（今すぐ再起動）の確認へ順に答える確認ダイアログを作る</summary>
    private static Mock<IDialogService> CreateDialog(bool download, bool restartNow)
    {
        var dialog = new Mock<IDialogService>();
        dialog
            .SetupSequence(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(download)
            .Returns(restartNow);
        return dialog;
    }

    private static IEnumerable<ScenarioRow<LifecycleCase>> UpdateRows()
    {
#if DEBUG
        // Debug ビルドでは環境変数が非空ならそれを最優先する（E2E 検証用の上書き）
        yield return Row(
            "update-feed-environment-variable-takes-precedence-in-debug-feed-environment-set",
            "Resolve: Debug では環境変数が非空なら環境変数を優先する",
            UpdateFeedResolve,
            FeedEnvironmentSet,
            o =>
                o.Bag["feed"] = UpdateFeed.Resolve(
                    EnvFrom((UpdateFeed.FeedEnvironmentVariable, @"C:\feed\path"))
                ),
            o => o.Get<string?>("feed").Should().Be(@"C:\feed\path")
        );

        yield return Row(
            "update-feed-blank-environment-variable-falls-back-to-constant-feed-environment-blank",
            "Resolve: 環境変数が空白のみなら定数へフォールバックする",
            UpdateFeedResolve,
            FeedEnvironmentBlank,
            o =>
                o.Bag["feed"] = UpdateFeed.Resolve(
                    EnvFrom((UpdateFeed.FeedEnvironmentVariable, "   "))
                ),
            o => o.Get<string?>("feed").Should().Be(UpdateFeed.GitHubRepositoryUrl)
        );
#else
        // Release ビルドでは環境変数による上書きを受け付けない（配布物の更新元は定数フィードに固定）
        yield return Row(
            "update-feed-environment-variable-is-ignored-in-release-feed-environment-set",
            "Resolve: Release では環境変数を無視して定数フィードを使う",
            UpdateFeedResolve,
            FeedEnvironmentSet,
            o =>
                o.Bag["feed"] = UpdateFeed.Resolve(
                    EnvFrom((UpdateFeed.FeedEnvironmentVariable, @"C:\feed\path"))
                ),
            o => o.Get<string?>("feed").Should().Be(UpdateFeed.GitHubRepositoryUrl)
        );
#endif

        // 期待はリテラルで固定する（フィードが誤ったリポジトリを指す退行を検知するため）
        yield return Row(
            "update-feed-without-environment-variable-uses-constant-feed-none",
            "Resolve: 環境変数がなければ定数フィードを採用する",
            UpdateFeedResolve,
            None,
            o => o.Bag["feed"] = UpdateFeed.Resolve(EnvFrom()),
            o => o.Get<string?>("feed").Should().Be("https://github.com/kokko-labs/QuickER")
        );

        yield return AsyncRow(
            "update-startup-check-disabled-by-setting-does-nothing-setting-off",
            "CheckOnStartup: 設定でオフなら何もしない",
            CheckOnStartup,
            SettingOff,
            async o =>
            {
                var feedResolved = false;
                var factoryCalled = false;

                var service = new UpdateService(
                    o.Dialogs,
                    _ =>
                    {
                        factoryCalled = true;
                        return Mock.Of<IAppUpdater>();
                    },
                    () =>
                    {
                        feedResolved = true;
                        return @"C:\feed\test";
                    },
                    new PostExitActions(),
                    () => { },
                    () => false
                );

                await service.CheckOnStartupAsync();

                o.Bag["feedResolved"] = feedResolved;
                o.Bag["factoryCalled"] = factoryCalled;
            },
            o =>
            {
                // フィードの解決すら行わない＝更新元へ一切接続しないことを、解決関数の未呼び出しで示す
                o.Get<bool>("feedResolved").Should().BeFalse();
                o.Get<bool>("factoryCalled").Should().BeFalse();
                o.Dialogs.ConfirmMessages.Should().BeEmpty();
            }
        );

        yield return AsyncRow(
            "update-startup-check-enabled-by-setting-checks-for-update-none",
            "CheckOnStartup: 設定でオンなら従来どおり確認する",
            CheckOnStartup,
            None,
            async o =>
            {
                o.Dialogs.ConfirmResult = false;
                var updater = new Mock<IAppUpdater>();
                updater.SetupGet(u => u.IsInstalled).Returns(true);
                updater.Setup(u => u.CheckForUpdateAsync()).ReturnsAsync("1.2.3");
                o.Bag["updater"] = updater;

                var service = new UpdateService(
                    o.Dialogs,
                    _ => updater.Object,
                    () => @"C:\feed\test",
                    new PostExitActions(),
                    () => { },
                    () => true
                );

                await service.CheckOnStartupAsync();
            },
            o =>
            {
                o.Get<Mock<IAppUpdater>>("updater")
                    .Verify(u => u.CheckForUpdateAsync(), Times.Once);
                o.Dialogs.ConfirmMessages.Should().ContainSingle();
            }
        );

        yield return AsyncRow(
            "update-startup-check-without-feed-does-nothing-no-feed",
            "CheckOnStartup: フィード未設定なら何もしない",
            CheckOnStartup,
            NoFeed,
            async o =>
            {
                var factoryCalled = false;

                var service = new UpdateService(
                    o.Dialogs,
                    _ =>
                    {
                        factoryCalled = true;
                        return Mock.Of<IAppUpdater>();
                    },
                    () => null, // フィード未設定（環境変数・定数がともに空の構成）
                    new PostExitActions(),
                    () => { }
                );

                await service.CheckOnStartupAsync();

                o.Bag["factoryCalled"] = factoryCalled;
            },
            o =>
            {
                o.Get<bool>("factoryCalled").Should().BeFalse();
                o.Dialogs.ConfirmMessages.Should().BeEmpty();
            }
        );

        yield return AsyncRow(
            "update-startup-check-when-not-installed-does-nothing-not-installed",
            "CheckOnStartup: 非インストール実行なら何もしない",
            CheckOnStartup,
            NotInstalled,
            async o =>
            {
                var updater = new Mock<IAppUpdater>();
                updater.SetupGet(u => u.IsInstalled).Returns(false);
                o.Bag["updater"] = updater;

                await CreateService(o.Dialogs, updater.Object).CheckOnStartupAsync();
            },
            o =>
            {
                o.Get<Mock<IAppUpdater>>("updater")
                    .Verify(u => u.CheckForUpdateAsync(), Times.Never);
                o.Dialogs.ConfirmMessages.Should().BeEmpty();
            }
        );

        yield return AsyncRow(
            "update-startup-check-without-update-shows-no-dialog-no-update",
            "CheckOnStartup: 更新なしならダイアログを出さない",
            CheckOnStartup,
            NoUpdate,
            async o =>
            {
                var updater = new Mock<IAppUpdater>();
                updater.SetupGet(u => u.IsInstalled).Returns(true);
                updater.Setup(u => u.CheckForUpdateAsync()).ReturnsAsync((string?)null);
                o.Bag["updater"] = updater;

                await CreateService(o.Dialogs, updater.Object).CheckOnStartupAsync();
            },
            o =>
            {
                o.Dialogs.ConfirmMessages.Should().BeEmpty();
                o.Get<Mock<IAppUpdater>>("updater").Verify(u => u.DownloadAsync(), Times.Never);
            }
        );

        yield return AsyncRow(
            "update-startup-check-declined-update-is-not-downloaded-declined",
            "CheckOnStartup: 更新ありでも拒否ならダウンロードしない",
            CheckOnStartup,
            Declined,
            async o =>
            {
                o.Dialogs.ConfirmResult = false;
                var updater = new Mock<IAppUpdater>();
                updater.SetupGet(u => u.IsInstalled).Returns(true);
                updater.Setup(u => u.CheckForUpdateAsync()).ReturnsAsync("1.2.3");
                o.Bag["updater"] = updater;

                await CreateService(o.Dialogs, updater.Object).CheckOnStartupAsync();
            },
            o =>
            {
                var updater = o.Get<Mock<IAppUpdater>>("updater");

                o.Dialogs.ConfirmMessages.Should().ContainSingle();
                updater.Verify(u => u.DownloadAsync(), Times.Never);
                updater.Verify(u => u.ApplyOnExit(It.IsAny<bool>()), Times.Never);
            }
        );

        // 適用をその場でプロセスを終わらせる形で行うと、メインウィンドウの Closing
        // （自動保存・機能モジュールの終了連鎖）を飛ばす。適用が終了の最後まで来ないことの固定
        yield return AsyncRow(
            "update-startup-check-restart-now-applies-only-at-exit-restart-now",
            "CheckOnStartup: 今すぐ再起動を選ぶと終了を要求し、適用は正常終了の最後まで行わない",
            CheckOnStartup,
            RestartNow,
            async o =>
            {
                var calls = o.Calls;
                var updater = CreateAvailableUpdater(calls);
                var dialog = CreateDialog(download: true, restartNow: true);
                var postExit = new PostExitActions();
                o.Bag["dialog"] = dialog;

                var service = new UpdateService(
                    dialog.Object,
                    _ => updater.Object,
                    () => @"C:\feed\test",
                    postExit,
                    () => calls.Add("shutdown")
                );

                await service.CheckOnStartupAsync();

                o.Bag["callsBeforeExit"] = calls.ToList();

                // 正常終了の最後（App.OnExit）
                postExit.Run(o.Reporter);
            },
            o =>
            {
                o.Get<List<string>>("callsBeforeExit")
                    .Should()
                    .Equal(new[] { "download", "shutdown" }, "適用は終了の最後まで行わない");
                o.Get<Mock<IDialogService>>("dialog")
                    .Verify(
                        d =>
                            d.Confirm(
                                Strings.Update_RestartConfirmMessage,
                                Strings.Update_ConfirmTitle
                            ),
                        Times.Once
                    );
                o.Calls.Should().Equal("download", "shutdown", "apply(restart: True)");
            }
        );

        yield return AsyncRow(
            "update-startup-check-restart-later-applies-without-restart-at-next-exit-restart-later",
            "CheckOnStartup: 再起動を断ると終了を要求せず、次の正常終了で再起動なしに適用する",
            CheckOnStartup,
            RestartLater,
            async o =>
            {
                var calls = o.Calls;
                var updater = CreateAvailableUpdater(calls);
                var dialog = CreateDialog(download: true, restartNow: false);
                var postExit = new PostExitActions();

                var service = new UpdateService(
                    dialog.Object,
                    _ => updater.Object,
                    () => @"C:\feed\test",
                    postExit,
                    () => calls.Add("shutdown")
                );

                await service.CheckOnStartupAsync();

                o.Bag["callsBeforeExit"] = calls.ToList();

                postExit.Run(o.Reporter);
            },
            o =>
            {
                o.Get<List<string>>("callsBeforeExit")
                    .Should()
                    .Equal(new[] { "download" }, "終了を要求せず、適用もしない");
                o.Calls.Should().Equal("download", "apply(restart: False)");
            }
        );

        // 適用の実行は正常終了の最後だけが行う。終了がキャンセルされると PostExitActions.Run は呼ばれないので、
        // ここでは呼ばないことでキャンセルを表す
        yield return AsyncRow(
            "update-startup-check-cancelled-shutdown-never-applies-shutdown-cancelled",
            "CheckOnStartup: 終了がキャンセルされれば適用しない",
            CheckOnStartup,
            ShutdownCancelled,
            async o =>
            {
                var calls = o.Calls;
                var updater = CreateAvailableUpdater(calls);
                var dialog = CreateDialog(download: true, restartNow: true);
                o.Bag["updater"] = updater;

                var service = new UpdateService(
                    dialog.Object,
                    _ => updater.Object,
                    () => @"C:\feed\test",
                    new PostExitActions(),
                    () => calls.Add("shutdown")
                );

                await service.CheckOnStartupAsync();
            },
            o =>
            {
                o.Get<Mock<IAppUpdater>>("updater")
                    .Verify(u => u.ApplyOnExit(It.IsAny<bool>()), Times.Never);
                o.Calls.Should().Equal("download", "shutdown");
            }
        );

        yield return AsyncRow(
            "update-startup-check-download-failure-schedules-nothing-download-fails",
            "CheckOnStartup: ダウンロードに失敗したら適用を登録しない",
            CheckOnStartup,
            DownloadFails,
            async o =>
            {
                var updater = new Mock<IAppUpdater>();
                updater.SetupGet(u => u.IsInstalled).Returns(true);
                updater.Setup(u => u.CheckForUpdateAsync()).ReturnsAsync("2.0.0");
                updater
                    .Setup(u => u.DownloadAsync())
                    .ThrowsAsync(new InvalidOperationException("down"));
                var dialog = CreateDialog(download: true, restartNow: true);
                var postExit = new PostExitActions();
                var shutdownRequested = false;
                o.Bag["updater"] = updater;
                o.Bag["dialog"] = dialog;

                var service = new UpdateService(
                    dialog.Object,
                    _ => updater.Object,
                    () => @"C:\feed\test",
                    postExit,
                    () => shutdownRequested = true
                );

                await service.CheckOnStartupAsync();
                postExit.Run(o.Reporter);

                o.Bag["shutdownRequested"] = shutdownRequested;
            },
            o =>
            {
                o.Get<Mock<IDialogService>>("dialog")
                    .Verify(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
                o.Get<Mock<IAppUpdater>>("updater")
                    .Verify(u => u.ApplyOnExit(It.IsAny<bool>()), Times.Never);
                o.Get<bool>("shutdownRequested").Should().BeFalse();
            }
        );

        // CheckForUpdateAsync が例外を投げても外へ漏れない（起動を阻害しない）ことは、流す側の共通の表明が見る
        yield return AsyncRow(
            "update-startup-check-failure-does-not-leak-check-fails",
            "CheckOnStartup: 更新チェックの例外は外へ漏れない",
            CheckOnStartup,
            CheckFails,
            async o =>
            {
                var updater = new Mock<IAppUpdater>();
                updater.SetupGet(u => u.IsInstalled).Returns(true);
                updater
                    .Setup(u => u.CheckForUpdateAsync())
                    .ThrowsAsync(new InvalidOperationException("network down"));

                await CreateService(o.Dialogs, updater.Object).CheckOnStartupAsync();
            },
            o => o.Dialogs.ConfirmMessages.Should().BeEmpty()
        );
    }
}
