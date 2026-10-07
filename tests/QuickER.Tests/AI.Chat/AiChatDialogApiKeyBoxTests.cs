using System.IO;
using System.Windows;
using System.Windows.Controls;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// <see cref="AiChatDialog"/> の API キー欄が、打鍵ごとではなく欄から離れた時点でキーを保存することを、
/// 実 XAML の配線（PasswordBox の LostKeyboardFocus）で検証するテストクラス。
/// </summary>
/// <remarks>
/// 配線を外しても VM 単体テストは緑のままなので、実ダイアログを画面外へ表示してフォーカスを実際に移す。
/// フォーカスの移動に頼るので、ほかのテストのウィンドウにアクティブを奪われないよう並列に走らせない
/// （<see cref="WindowActivationTestCollection"/>）。
/// </remarks>
[Collection(WindowActivationTestCollection.Name)]
public class AiChatDialogApiKeyBoxTests
{
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    /// <summary>API キー欄へ打ち込んだキーは、欄から離れた時点で保存されることを検証する</summary>
    [Fact(DisplayName = "AI チャットの API キー欄は離れた時点でキーを保存する")]
    public void ApiKeyBox_SavesKeyWhenFocusLeaves()
    {
        var keyStore = new InMemoryApiKeyStore();
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var savedWhileTyping = true;
        string? savedAfterLeaving = null;

        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var vm = new AiChatDialogViewModel(
                host: null,
                dispatcher: new SyncUiDispatcher(),
                settingsStore: new AiSettingsStore(folder),
                codexClient: new FakeCodexAppServerClient(),
                apiKeyLoader: keyStore.Load,
                apiKeySaver: keyStore.Save
            );
            vm.Connection.ApiProvider = AiProvider.OpenAI;

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

                var box = (PasswordBox)dialog.FindName("ApiKeyBox");
                box.Focus();
                WpfApplicationTestSupport.DoEvents();
                box.IsKeyboardFocused.Should().BeTrue("API キー欄にフォーカスがある状態を作る");

                box.Password = "sk-typed";
                WpfApplicationTestSupport.DoEvents();
                vm.Connection.ApiKey.Should().Be("sk-typed");
                savedWhileTyping = keyStore.Entries.ContainsKey("OpenAiApiKey");

                ((TextBox)dialog.FindName("UserInputBox")).Focus();
                WpfApplicationTestSupport.DoEvents();
                savedAfterLeaving = keyStore.Entries.GetValueOrDefault("OpenAiApiKey");
            }
            finally
            {
                dialog.ForceClose();
            }
        });

        try
        {
            savedWhileTyping.Should().BeFalse("打鍵ごとには保存しない");
            savedAfterLeaving.Should().Be("sk-typed");
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
