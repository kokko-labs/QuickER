using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Moq;
using QuickER.Gui.Abstractions;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 起動時更新チェック（<see cref="UpdateService"/>）とフィード解決（<see cref="UpdateFeed"/>）を
/// 検証するテストクラス。UI・Velopack に依存せず、<see cref="IAppUpdater"/> はモックで差し替える。
/// </summary>
public class UpdateServiceTests
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

    // ---- UpdateFeed.Resolve ----

#if DEBUG
    /// <summary>Debug ビルドでは環境変数が非空ならそれを最優先する（E2E 検証用の上書き）</summary>
    [Fact(DisplayName = "Resolve: Debug では環境変数が非空なら環境変数を優先する")]
    public void Resolve_EnvironmentVariableNonEmpty_TakesPrecedence()
    {
        var env = EnvFrom((UpdateFeed.FeedEnvironmentVariable, @"C:\feed\path"));

        UpdateFeed.Resolve(env).Should().Be(@"C:\feed\path");
    }

    /// <summary>環境変数が空白のみなら定数フィードへフォールバックする</summary>
    [Fact(DisplayName = "Resolve: 環境変数が空白のみなら定数へフォールバックする")]
    public void Resolve_EnvironmentVariableWhitespace_FallsBackToConstant()
    {
        var env = EnvFrom((UpdateFeed.FeedEnvironmentVariable, "   "));

        UpdateFeed.Resolve(env).Should().Be(UpdateFeed.GitHubRepositoryUrl);
    }
#else
    /// <summary>
    /// Release ビルドでは環境変数による上書きを受け付けない（配布物の更新元は定数フィードに固定）。
    /// </summary>
    [Fact(DisplayName = "Resolve: Release では環境変数を無視して定数フィードを使う")]
    public void Resolve_EnvironmentVariableNonEmpty_IsIgnoredInRelease()
    {
        var env = EnvFrom((UpdateFeed.FeedEnvironmentVariable, @"C:\feed\path"));

        UpdateFeed.Resolve(env).Should().Be(UpdateFeed.GitHubRepositoryUrl);
    }
#endif

    /// <summary>環境変数がなければ定数フィード（本リポジトリの GitHub Releases）を採用する</summary>
    [Fact(DisplayName = "Resolve: 環境変数がなければ定数フィードを採用する")]
    public void Resolve_NoEnvironmentVariable_UsesConstantFeed()
    {
        var env = EnvFrom();

        // リテラルで固定する（フィードが誤ったリポジトリを指す退行を検知するため）
        UpdateFeed.Resolve(env).Should().Be("https://github.com/kokko-labs/QuickER");
    }

    // ---- UpdateService.CheckOnStartupAsync ----

    /// <summary>設定でオプトアウトされていれば、フィード解決もファクトリもダイアログも呼ばれない</summary>
    [Fact(DisplayName = "CheckOnStartup: 設定でオフなら何もしない")]
    public async Task CheckOnStartup_DisabledBySetting_DoesNothing()
    {
        var dialog = new StubDialogService();
        var feedResolved = false;
        var factoryCalled = false;

        var service = new UpdateService(
            dialog,
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

        // フィードの解決すら行わない＝更新元へ一切接続しないことを、解決関数の未呼び出しで示す
        feedResolved.Should().BeFalse();
        factoryCalled.Should().BeFalse();
        dialog.ConfirmMessages.Should().BeEmpty();
    }

    /// <summary>設定が有効なら従来どおり更新チェックへ進む（オプトアウトの既定が挙動を変えない）</summary>
    [Fact(DisplayName = "CheckOnStartup: 設定でオンなら従来どおり確認する")]
    public async Task CheckOnStartup_EnabledBySetting_ChecksForUpdate()
    {
        var dialog = new StubDialogService { ConfirmResult = false };
        var updater = new Mock<IAppUpdater>();
        updater.SetupGet(u => u.IsInstalled).Returns(true);
        updater.Setup(u => u.CheckForUpdateAsync()).ReturnsAsync("1.2.3");

        var service = new UpdateService(
            dialog,
            _ => updater.Object,
            () => @"C:\feed\test",
            new PostExitActions(),
            () => { },
            () => true
        );

        await service.CheckOnStartupAsync();

        updater.Verify(u => u.CheckForUpdateAsync(), Times.Once);
        dialog.ConfirmMessages.Should().ContainSingle();
    }

    /// <summary>フィード未設定ならファクトリもダイアログも呼ばれない</summary>
    [Fact(DisplayName = "CheckOnStartup: フィード未設定なら何もしない")]
    public async Task CheckOnStartup_NoFeed_DoesNothing()
    {
        var dialog = new StubDialogService();
        var factoryCalled = false;

        var service = new UpdateService(
            dialog,
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

        factoryCalled.Should().BeFalse();
        dialog.ConfirmMessages.Should().BeEmpty();
    }

    /// <summary>非インストール実行なら Check もダイアログも呼ばれない</summary>
    [Fact(DisplayName = "CheckOnStartup: 非インストール実行なら何もしない")]
    public async Task CheckOnStartup_NotInstalled_DoesNothing()
    {
        var dialog = new StubDialogService();
        var updater = new Mock<IAppUpdater>();
        updater.SetupGet(u => u.IsInstalled).Returns(false);

        var service = CreateService(dialog, updater.Object);

        await service.CheckOnStartupAsync();

        updater.Verify(u => u.CheckForUpdateAsync(), Times.Never);
        dialog.ConfirmMessages.Should().BeEmpty();
    }

    /// <summary>更新なし（null）ならダイアログは表示されない</summary>
    [Fact(DisplayName = "CheckOnStartup: 更新なしならダイアログを出さない")]
    public async Task CheckOnStartup_NoUpdate_ShowsNoDialog()
    {
        var dialog = new StubDialogService();
        var updater = new Mock<IAppUpdater>();
        updater.SetupGet(u => u.IsInstalled).Returns(true);
        updater.Setup(u => u.CheckForUpdateAsync()).ReturnsAsync((string?)null);

        var service = CreateService(dialog, updater.Object);

        await service.CheckOnStartupAsync();

        dialog.ConfirmMessages.Should().BeEmpty();
        updater.Verify(u => u.DownloadAsync(), Times.Never);
    }

    /// <summary>更新あり＋ユーザー拒否ならダウンロードしない</summary>
    [Fact(DisplayName = "CheckOnStartup: 更新ありでも拒否ならダウンロードしない")]
    public async Task CheckOnStartup_UpdateDeclined_DoesNotDownload()
    {
        var dialog = new StubDialogService { ConfirmResult = false };
        var updater = new Mock<IAppUpdater>();
        updater.SetupGet(u => u.IsInstalled).Returns(true);
        updater.Setup(u => u.CheckForUpdateAsync()).ReturnsAsync("1.2.3");

        var service = CreateService(dialog, updater.Object);

        await service.CheckOnStartupAsync();

        dialog.ConfirmMessages.Should().ContainSingle();
        updater.Verify(u => u.DownloadAsync(), Times.Never);
        updater.Verify(u => u.ApplyOnExit(It.IsAny<bool>()), Times.Never);
    }

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

    /// <summary>
    /// 承諾してダウンロードしても、正常終了の最後（<see cref="PostExitActions.Run"/>）まで適用しない。
    /// 今すぐ再起動を選ぶと終了を要求し、終了の最後に「適用して再起動」する。
    /// </summary>
    /// <remarks>
    /// 適用をその場でプロセスを終わらせる形で行うと、メインウィンドウの Closing
    /// （自動保存・機能モジュールの終了連鎖）を飛ばす。適用が終了の最後まで来ないことの固定。
    /// </remarks>
    [Fact(
        DisplayName = "CheckOnStartup: 今すぐ再起動を選ぶと終了を要求し、適用は正常終了の最後まで行わない"
    )]
    public async Task CheckOnStartup_RestartNow_RequestsShutdownAndAppliesOnlyAtExit()
    {
        var calls = new List<string>();
        var updater = CreateAvailableUpdater(calls);
        var dialog = CreateDialog(download: true, restartNow: true);
        var postExit = new PostExitActions();

        var service = new UpdateService(
            dialog.Object,
            _ => updater.Object,
            () => @"C:\feed\test",
            postExit,
            () => calls.Add("shutdown")
        );

        await service.CheckOnStartupAsync();

        calls.Should().Equal(new[] { "download", "shutdown" }, "適用は終了の最後まで行わない");
        dialog.Verify(
            d => d.Confirm(Strings.Update_RestartConfirmMessage, Strings.Update_ConfirmTitle),
            Times.Once
        );

        // 正常終了の最後（App.OnExit）
        postExit.Run(new RecordingShutdownFailureReporter());

        calls.Should().Equal("download", "shutdown", "apply(restart: True)");
    }

    /// <summary>今すぐ再起動を断ると終了を要求せず、次に閉じたときに再起動なしで適用する</summary>
    [Fact(
        DisplayName = "CheckOnStartup: 再起動を断ると終了を要求せず、次の正常終了で再起動なしに適用する"
    )]
    public async Task CheckOnStartup_RestartLater_AppliesWithoutRestartAtNextExit()
    {
        var calls = new List<string>();
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

        calls.Should().Equal(new[] { "download" }, "終了を要求せず、適用もしない");

        postExit.Run(new RecordingShutdownFailureReporter());

        calls.Should().Equal("download", "apply(restart: False)");
    }

    /// <summary>終了が起きなければ（キャンセルされた場合）、適用は一度も呼ばれない</summary>
    /// <remarks>
    /// 適用の実行は正常終了の最後（<c>App.OnExit</c>）だけが行う。終了がキャンセルされると
    /// <see cref="PostExitActions.Run"/> は呼ばれないので、ここでは呼ばないことでキャンセルを表す。
    /// </remarks>
    [Fact(DisplayName = "CheckOnStartup: 終了がキャンセルされれば適用しない")]
    public async Task CheckOnStartup_ShutdownCancelled_NeverApplies()
    {
        var calls = new List<string>();
        var updater = CreateAvailableUpdater(calls);
        var dialog = CreateDialog(download: true, restartNow: true);

        var service = new UpdateService(
            dialog.Object,
            _ => updater.Object,
            () => @"C:\feed\test",
            new PostExitActions(),
            () => calls.Add("shutdown")
        );

        await service.CheckOnStartupAsync();

        updater.Verify(u => u.ApplyOnExit(It.IsAny<bool>()), Times.Never);
        calls.Should().Equal("download", "shutdown");
    }

    /// <summary>ダウンロードに失敗したら、2 回目の確認も適用の登録も終了の要求もしない</summary>
    [Fact(DisplayName = "CheckOnStartup: ダウンロードに失敗したら適用を登録しない")]
    public async Task CheckOnStartup_DownloadFails_SchedulesNothing()
    {
        var updater = new Mock<IAppUpdater>();
        updater.SetupGet(u => u.IsInstalled).Returns(true);
        updater.Setup(u => u.CheckForUpdateAsync()).ReturnsAsync("2.0.0");
        updater.Setup(u => u.DownloadAsync()).ThrowsAsync(new InvalidOperationException("down"));
        var dialog = CreateDialog(download: true, restartNow: true);
        var postExit = new PostExitActions();
        var shutdownRequested = false;

        var service = new UpdateService(
            dialog.Object,
            _ => updater.Object,
            () => @"C:\feed\test",
            postExit,
            () => shutdownRequested = true
        );

        await service.CheckOnStartupAsync();
        postExit.Run(new RecordingShutdownFailureReporter());

        dialog.Verify(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        updater.Verify(u => u.ApplyOnExit(It.IsAny<bool>()), Times.Never);
        shutdownRequested.Should().BeFalse();
    }

    /// <summary>CheckForUpdateAsync が例外を投げても外へ漏れない（起動を阻害しない）</summary>
    [Fact(DisplayName = "CheckOnStartup: 更新チェックの例外は外へ漏れない")]
    public async Task CheckOnStartup_CheckThrows_SwallowsException()
    {
        var dialog = new StubDialogService();
        var updater = new Mock<IAppUpdater>();
        updater.SetupGet(u => u.IsInstalled).Returns(true);
        updater
            .Setup(u => u.CheckForUpdateAsync())
            .ThrowsAsync(new InvalidOperationException("network down"));

        var service = CreateService(dialog, updater.Object);

        var act = async () => await service.CheckOnStartupAsync();

        await act.Should().NotThrowAsync();
        dialog.ConfirmMessages.Should().BeEmpty();
    }

    /// <summary>
    /// フィードが必ず解決される固定のフィード解決関数を与え、指定の <see cref="IAppUpdater"/> を返す
    /// ファクトリで <see cref="UpdateService"/> を組み立てるヘルパ。
    /// </summary>
    private static UpdateService CreateService(StubDialogService dialog, IAppUpdater updater)
    {
        return new UpdateService(
            dialog,
            _ => updater,
            () => @"C:\feed\test",
            new PostExitActions(),
            () => { }
        );
    }
}
