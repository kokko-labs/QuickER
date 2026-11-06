using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockGenerationDialogViewModel"/> を実際の WPF のコントロールへ束縛して確かめるテストクラス。
/// </summary>
/// <remarks>
/// VM の操作の列（可否・会話・画面・出力・生成・接続・破棄）は <c>MockDialogTests</c> の表が持つ。
/// ここに残すのは、実のコントロールの束縛（入力欄の読み直し・ListBox の選択の押し戻し）が主題のものだけ。
/// </remarks>
public class MockGenerationDialogViewModelTests
{
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    private const string ValidHtml =
        "<!DOCTYPE html><html lang=\"ja\"><head><link rel=\"stylesheet\" href=\"style.css\">"
        + "<style>body{}</style></head><body><h1>顧客一覧</h1></body></html>";

    /// <summary>フェイク（API キー）エンジンを注入して VM を生成する（設定と mock は一時フォルダの下）</summary>
    private static (
        MockGenerationDialogViewModel vm,
        FakeChatEngine[] engineBox,
        string mockFolder
    ) CreateVm(TestTempFolder folder)
    {
        var mockFolder = Path.Combine(folder.Path, "mock");
        var engineBox = new FakeChatEngine[1];
        var keyStore = new InMemoryApiKeyStore();

        var vm = new MockGenerationDialogViewModel(
            new StubDiagramSource(
                new ErDiagram
                {
                    Entities =
                    {
                        new Entity { TableName = "Customer", Description = "顧客" },
                    },
                }
            ),
            new SyncUiDispatcher(),
            files: null,
            settingsStore: new AiSettingsStore(Path.Combine(folder.Path, "settings")),
            apiKeyEngineFactory: (_, toolHost) => engineBox[0] = new FakeChatEngine(toolHost),
            codexEngineFactory: null,
            claudeCodeEngineFactory: null,
            dialogService: new StubDialogService(),
            apiKeyLoader: keyStore.Load,
            apiKeySaver: keyStore.Save
        );
        vm.Connection.ApiProvider = AiProvider.LocalLlm;
        vm.MockFolder = mockFolder;

        return (vm, engineBox, mockFolder);
    }

    /// <summary>save_screen ツールを 1 回再生する（会話開始済み前提）</summary>
    private static async Task SaveScreenViaEngine(
        MockGenerationDialogViewModel vm,
        FakeChatEngine engine,
        string file,
        string name
    )
    {
        engine.ScriptedToolCall = (
            MockFolderDesignTools.SaveScreenToolName,
            JsonSerializer.Serialize(
                new Dictionary<string, object?>
                {
                    ["file"] = file,
                    ["name"] = name,
                    ["html"] = ValidHtml,
                }
            )
        );
        vm.UserInput = "追加";
        await vm.SendMessageCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// ターン実行中にモックフォルダの変更を拒んだとき、束縛している入力欄に打ち込んだ文字が残らない
    /// （元の値へ戻る）ことを、実際の WPF の束縛で検証する。
    /// </summary>
    /// <remarks>
    /// 画面では入力欄自体を無効化しているのでこの経路には来ないが、拒否が値を変えないまま
    /// 入力欄にだけ文字を残すと、画面と VM が食い違ったまま見えてしまう。
    /// </remarks>
    [Fact(DisplayName = "モックフォルダの変更を拒んだとき入力欄の文字は元の値へ戻る")]
    public async Task MockFolder_RejectedEdit_RevertsBoundTextBox()
    {
        using var folder = new TestTempFolder();
        var (vm, engineBox, mockFolder) = CreateVm(folder);
        var typed = Path.Combine(folder.Path, "typed");

        vm.StartConversationCommand.Execute(null);
        engineBox[0].HoldTurnCompletion = true;
        vm.UserInput = "要望";
        await vm.SendMessageCommand.ExecuteAsync(null);
        vm.IsTurnInProgress.Should().BeTrue("ターン完了を保留しているので実行中のまま");

        string? textAfterEdit = null;
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                var textBox = new TextBox();
                textBox.SetBinding(
                    TextBox.TextProperty,
                    new Binding(nameof(MockGenerationDialogViewModel.MockFolder))
                    {
                        Source = vm,
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    }
                );
                textBox.Text.Should().Be(mockFolder);

                textBox.Text = typed;
                textAfterEdit = textBox.Text;
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        captured.Should().BeNull();
        vm.MockFolder.Should().Be(mockFolder);
        textAfterEdit.Should().Be(mockFolder);
    }

    /// <summary>
    /// 実 ListBox を画面一覧へ束縛した状態で、表示していない画面が削除されても、選択とプレビューが動かないことを検証する。
    /// </summary>
    /// <remarks>
    /// ListBox は画面一覧の Clear で選択を null へ押し戻す。再構築が選び直さないと、
    /// 表示していない画面の削除だけで先頭の画面へ飛ぶ（画面に出る SelectedItem で表明する）。
    /// </remarks>
    [Fact(DisplayName = "表示していない画面を削除しても実 ListBox の選択は動かない")]
    public void ScreenRemoved_OtherScreen_KeepsListBoxSelection()
    {
        using var folder = new TestTempFolder();
        var (vm, engineBox, _) = CreateVm(folder);
        Exception? captured = null;
        string? selectedAfter = null;
        string? listBoxSelectedAfter = null;
        var previewsAfterRemoval = new List<string>();

        var thread = new Thread(() =>
        {
            try
            {
                var listBox = new ListBox { ItemsSource = vm.Screens };
                listBox.SetBinding(
                    System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                    new Binding(nameof(MockGenerationDialogViewModel.SelectedScreen))
                    {
                        Source = vm,
                        Mode = BindingMode.TwoWay,
                    }
                );

                vm.StartConversationCommand.Execute(null);
                SaveScreenViaEngine(vm, engineBox[0], "A.html", "画面A").GetAwaiter().GetResult();
                SaveScreenViaEngine(vm, engineBox[0], "B.html", "画面B").GetAwaiter().GetResult();
                SaveScreenViaEngine(vm, engineBox[0], "C.html", "画面C").GetAwaiter().GetResult();

                // 利用者が B を選んでいる
                listBox.SelectedItem = vm.Screens.Single(s => s.File == "B.html");
                vm.PreviewRequested += (_, r) =>
                    previewsAfterRemoval.Add(Path.GetFileName(r.FilePath));

                engineBox[0].ScriptedToolCall = (
                    MockFolderDesignTools.RemoveScreenToolName,
                    JsonSerializer.Serialize(
                        new Dictionary<string, object?> { ["file"] = "C.html" }
                    )
                );
                vm.UserInput = "実行";
                vm.SendMessageCommand.ExecuteAsync(null).GetAwaiter().GetResult();

                selectedAfter = vm.SelectedScreen?.File;
                listBoxSelectedAfter = (listBox.SelectedItem as MockScreenListItem)?.File;
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        captured.Should().BeNull();
        selectedAfter.Should().Be("B.html");
        listBoxSelectedAfter.Should().Be("B.html");
        previewsAfterRemoval.Should().BeEmpty();
        vm.Screens.Select(s => s.File).Should().Equal("A.html", "B.html");
    }
}
