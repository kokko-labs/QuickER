using System.IO;
using System.Windows;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.UI;
using QuickER.Tests.AI;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// <see cref="AiChatDialog"/> のウィンドウとしての振る舞い（× で閉じたときの設定保存・チャット欄の自動スクロール）を、
/// 実 XAML の配線で検証するテストクラス。
/// </summary>
/// <remarks>
/// どちらもコードビハインドの配線で、外しても VM 単体テストは緑のままなので、実ダイアログを画面外へ表示して確かめる。
/// </remarks>
public class AiChatDialogWindowTests
{
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    /// <summary>VM を組み立てる（設定の保存先と確認・エラー表示を差し替える）</summary>
    private static AiChatDialogViewModel CreateViewModel(
        string settingsFolder,
        StubDialogService dialogs
    )
    {
        var keyStore = new InMemoryApiKeyStore();

        return new AiChatDialogViewModel(
            host: null,
            dispatcher: new SyncUiDispatcher(),
            settingsStore: new AiSettingsStore(settingsFolder),
            codexClient: new FakeCodexAppServerClient(),
            claudeCodeClient: new DisposeTrackingClaudeCodeClient(),
            copilotClient: new FakeCopilotRuntimeClient(),
            dialogService: dialogs,
            apiKeyLoader: keyStore.Load,
            apiKeySaver: keyStore.Save
        );
    }

    /// <summary>ダイアログを画面外・非アクティブで表示して検証を実行し、最後に閉じる</summary>
    private static void WithShownDialog(AiChatDialogViewModel vm, Action<AiChatDialog> assert)
    {
        WpfApplicationTestSupport.EnsureApplicationResources();

        var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
            new AiChatDialog(vm, new RecordingShutdownFailureReporter())
        );
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = -4000;
        dialog.Top = -4000;
        dialog.ShowActivated = false;
        dialog.ShowInTaskbar = false;
        dialog.Show();

        try
        {
            dialog.UpdateLayout();
            WpfApplicationTestSupport.DoEvents();
            assert(dialog);
        }
        finally
        {
            dialog.ForceClose();
        }
    }

    /// <summary>
    /// × で閉じたときに設定の保存が失敗しても、例外を漏らさずウィンドウを隠し、
    /// 隠した後でエラーを 1 回知らせる（理由はステータスにも残る）ことを検証する。
    /// </summary>
    [Fact(DisplayName = "× で閉じたとき設定の保存が失敗しても隠れてエラーを知らせる")]
    public void CloseButton_WhenSaveFails_HidesAndReportsError()
    {
        // 保存先フォルダと同じパスにファイルを置く＝設定の書き込みが必ず失敗する
        var blockedFolder = Path.Combine(
            Path.GetTempPath(),
            $"QuickERTests-blocked-{Guid.NewGuid():N}"
        );
        File.WriteAllText(blockedFolder, "block");
        var dialogs = new StubDialogService();

        try
        {
            WpfApplicationTestSupport.RunSta(() =>
            {
                var vm = CreateViewModel(blockedFolder, dialogs);

                WithShownDialog(
                    vm,
                    dialog =>
                    {
                        var visibleWhenErrorShown = true;
                        dialogs.OnBeforeShow = () => visibleWhenErrorShown = dialog.IsVisible;

                        var close = dialog.Close;
                        close.Should().NotThrow();

                        dialog.IsVisible.Should().BeFalse("保存の失敗でも × で隠れる");
                        visibleWhenErrorShown.Should().BeFalse("エラーは隠した後に出す");
                        dialogs.ErrorMessages.Should().ContainSingle();
                        dialogs
                            .ErrorMessages[0]
                            .Should()
                            .StartWith(
                                ChatStrings.Chat_SettingsSaveFailedFormat[
                                    ..ChatStrings.Chat_SettingsSaveFailedFormat.IndexOf('{')
                                ]
                            );
                        vm.StatusMessage.Should().Be(dialogs.ErrorMessages[0]);
                    }
                );
            });
        }
        finally
        {
            File.Delete(blockedFolder);
        }
    }

    /// <summary>× で閉じたときに設定を保存できれば、エラーを出さずに隠れることを検証する</summary>
    [Fact(DisplayName = "× で閉じたとき設定を保存できればエラーを出さずに隠れる")]
    public void CloseButton_WhenSaveSucceeds_HidesWithoutError()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var dialogs = new StubDialogService();

        try
        {
            WpfApplicationTestSupport.RunSta(() =>
            {
                var vm = CreateViewModel(folder, dialogs);

                WithShownDialog(
                    vm,
                    dialog =>
                    {
                        dialog.Close();

                        dialog.IsVisible.Should().BeFalse();
                        dialogs.ErrorMessages.Should().BeEmpty();
                        File.Exists(new AiSettingsStore(folder).SettingsPath).Should().BeTrue();
                    }
                );
            });
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// ストリーミングで最後の吹き出しが伸びたとき、末尾を見ていればチャット欄が追従することを検証する
    /// （吹き出しの本文が伸びるだけではメッセージ一覧は変わらないため、一覧の変更だけでは追従しない）。
    /// </summary>
    [Fact(DisplayName = "ストリーミングで吹き出しが伸びると末尾を見ているチャット欄は追従する")]
    public void StreamingGrowth_WhenAtBottom_FollowsToEnd()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

        try
        {
            WpfApplicationTestSupport.RunSta(() =>
            {
                var vm = CreateViewModel(folder, new StubDialogService());

                WithShownDialog(
                    vm,
                    dialog =>
                    {
                        for (var i = 0; i < 30; i++)
                        {
                            vm.Messages.Add(
                                new ErChatMessage
                                {
                                    Role = ErChatMessageRole.Assistant,
                                    Content = $"message {i}",
                                }
                            );
                        }

                        WpfApplicationTestSupport.DoEvents();
                        var viewer = dialog.ChatScrollViewer;
                        var scrollableBefore = viewer.ScrollableHeight;
                        scrollableBefore.Should().BeGreaterThan(0, "末尾がある状態を作る");

                        // 新しい吹き出しの ScrollToEnd は位置を「無限大」として覚え、以後の伸びにも自然に付いていく。
                        // 追従が切れるのは利用者が手で動かした後なので、手で末尾まで戻した状態（有限の位置）を作る
                        viewer.ScrollToVerticalOffset(scrollableBefore);
                        WpfApplicationTestSupport.DoEvents();
                        viewer
                            .VerticalOffset.Should()
                            .Be(scrollableBefore, "末尾を見ている状態を作る");

                        var last = vm.Messages[^1];

                        for (var i = 0; i < 20; i++)
                        {
                            last.Content += "\nstreamed line " + i;
                        }

                        WpfApplicationTestSupport.DoEvents();

                        viewer.ScrollableHeight.Should().BeGreaterThan(scrollableBefore);
                        viewer.VerticalOffset.Should().Be(viewer.ScrollableHeight);
                    }
                );
            });
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
