using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Moq;
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

    /// <summary>環境変数が非空ならそれを最優先する</summary>
    [Fact(DisplayName = "Resolve: 環境変数が非空なら環境変数を優先する")]
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

    /// <summary>環境変数がなければ定数フィード（本リポジトリの GitHub Releases）を採用する</summary>
    [Fact(DisplayName = "Resolve: 環境変数がなければ定数フィードを採用する")]
    public void Resolve_NoEnvironmentVariable_UsesConstantFeed()
    {
        var env = EnvFrom();

        // リテラルで固定する（フィードが誤ったリポジトリを指す退行を検知するため）
        UpdateFeed.Resolve(env).Should().Be("https://github.com/kokko-labs/QuickER");
    }

    // ---- UpdateService.CheckOnStartupAsync ----

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
            () => null // フィード未設定（環境変数・定数がともに空の構成）
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
        updater.Verify(u => u.ApplyAndRestart(), Times.Never);
    }

    /// <summary>更新あり＋承諾なら Download → ApplyAndRestart の順に呼ばれる</summary>
    [Fact(DisplayName = "CheckOnStartup: 承諾なら Download→ApplyAndRestart の順で実行する")]
    public async Task CheckOnStartup_UpdateAccepted_DownloadsThenApplies()
    {
        var dialog = new StubDialogService { ConfirmResult = true };
        var updater = new Mock<IAppUpdater>(MockBehavior.Strict);
        updater.SetupGet(u => u.IsInstalled).Returns(true);
        updater.Setup(u => u.CheckForUpdateAsync()).ReturnsAsync("2.0.0");

        var calls = new List<string>();
        updater
            .Setup(u => u.DownloadAsync())
            .Callback(() => calls.Add("download"))
            .Returns(Task.CompletedTask);
        updater.Setup(u => u.ApplyAndRestart()).Callback(() => calls.Add("apply"));

        var service = CreateService(dialog, updater.Object);

        await service.CheckOnStartupAsync();

        calls.Should().Equal("download", "apply");
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
        return new UpdateService(dialog, _ => updater, () => @"C:\feed\test");
    }
}
