using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Gui.Abstractions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogOp;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogState;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Dialog;

internal static partial class MockDialogRows
{
    private const string DesignDocSentinel = "SENTINEL-DESIGN-DOC";

    /// <summary>画面の保存・削除・共有 CSS の保存・プレビューの要求と、単一 HTML・設計書の出力</summary>
    private static IEnumerable<ScenarioRow<MockDialogCase>> ScreenRows() =>
        [
            Row(
                "screen-saved-updates-sidebar-and-requests-preview",
                "画面の保存で画面一覧が更新され、モックフォルダの実ファイルを指すプレビューの要求が出る",
                [(Conversation, Screen)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    MockPreviewRequest? request = null;
                    vm.PreviewRequested += (_, r) => request = r;

                    vm.StartConversationCommand.Execute(null);
                    h.Engine.ScriptedToolCall = (
                        MockFolderDesignTools.SaveScreenToolName,
                        MockDialogHarness.SaveScreenArgs()
                    );
                    vm.UserInput = "OrderList を作って";
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    vm.Screens.Should().ContainSingle();
                    vm.Screens[0].File.Should().Be("OrderList.html");
                    vm.SelectedScreen.Should().NotBeNull();
                    request.Should().NotBeNull();
                    request!.FilePath.Should().Be(Path.Combine(h.MockFolder, "OrderList.html"));
                    request.Folder.Should().Be(h.MockFolder);
                    File.Exists(Path.Combine(h.MockFolder, "OrderList.html")).Should().BeTrue();
                }
            ),
            Row(
                "screen-saved-during-held-turn-updates-sidebar-before-completion",
                "ターンの完了より前に届いた画面の保存も画面一覧へ反映され、その後の完了で実行中が解ける",
                [(Turn, Screen)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);
                    h.Engine.HoldTurnCompletion = true;
                    h.Engine.ScriptedToolCall = (
                        MockFolderDesignTools.SaveScreenToolName,
                        MockDialogHarness.SaveScreenArgs()
                    );
                    vm.UserInput = "OrderList を作って";

                    await vm.SendMessageCommand.ExecuteAsync(null);

                    vm.IsTurnInProgress.Should().BeTrue();
                    vm.Screens.Select(s => s.File).Should().Equal("OrderList.html");
                    vm.SelectedScreen!.File.Should().Be("OrderList.html");

                    h.Engine.CompleteHeldTurn();

                    vm.IsTurnInProgress.Should().BeFalse();
                    vm.Screens.Select(s => s.File).Should().Equal("OrderList.html");
                }
            ),
            Row(
                "screen-resaved-same-name-requests-preview-again",
                "表示中の画面を同じ名前で保存し直すと、選択が変わらなくてもプレビューを再要求する",
                [(Conversation, Screen)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);
                    await MockDialogHarness.SaveScreenViaEngine(
                        vm,
                        h.Engine,
                        "OrderList.html",
                        "注文一覧"
                    );
                    var requests = new List<string>();
                    vm.PreviewRequested += (_, r) => requests.Add(Path.GetFileName(r.FilePath));

                    await MockDialogHarness.SaveScreenViaEngine(
                        vm,
                        h.Engine,
                        "OrderList.html",
                        "注文一覧"
                    );

                    requests.Should().Equal("OrderList.html");
                    vm.SelectedScreen!.File.Should().Be("OrderList.html");
                }
            ),
            Row(
                "screen-sync-from-preview-selects-without-preview-request",
                "プレビューの中のリンクで移った画面へ選択を追従させ、プレビューは再要求しない",
                [(Conversation, Screen)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);
                    await MockDialogHarness.SaveScreenViaEngine(vm, h.Engine, "A.html", "画面A");
                    await MockDialogHarness.SaveScreenViaEngine(vm, h.Engine, "B.html", "画面B");
                    vm.SelectedScreen!.File.Should().Be("B.html");
                    var requests = new List<string>();
                    vm.PreviewRequested += (_, r) => requests.Add(Path.GetFileName(r.FilePath));

                    vm.SyncSelectionFromPreview("a.html");

                    vm.SelectedScreen!.File.Should().Be("A.html");
                    requests.Should().BeEmpty();
                }
            ),
            Row(
                "screen-saved-with-readme-regenerates-silently",
                "README.md がある状態で画面を保存すると、無音（ダイアログなし）で新しい画面を含む内容へ再生成する",
                [(Conversation, Screen)],
                async h =>
                {
                    var vm = h.Create();
                    await h.SaveScreenOnClaudeCodeAsync(vm);
                    var readmePath = Path.Combine(h.MockFolder, MockDesignDocExporter.FileName);
                    File.WriteAllText(readmePath, DesignDocSentinel);
                    var dialogsBefore = h.Dialogs.InformationMessages.Count;

                    await MockDialogHarness.SaveScreenViaEngine(
                        vm,
                        h.Engine,
                        "Detail.html",
                        "注文詳細"
                    );

                    var content = File.ReadAllText(readmePath);
                    content.Should().NotContain(DesignDocSentinel);
                    content.Should().Contain("注文詳細");
                    h.Dialogs.InformationMessages.Count.Should().Be(dialogsBefore);
                }
            ),
            Row(
                "screen-saved-without-readme-does-not-create-readme",
                "README.md が無い状態で画面を保存しても README.md を作らない（オプトインを保つ）",
                [(Conversation, Screen)],
                async h =>
                {
                    var vm = h.Create();
                    await h.SaveScreenOnClaudeCodeAsync(vm);
                    var readmePath = Path.Combine(h.MockFolder, MockDesignDocExporter.FileName);
                    File.Exists(readmePath).Should().BeFalse();

                    await MockDialogHarness.SaveScreenViaEngine(
                        vm,
                        h.Engine,
                        "Detail.html",
                        "注文詳細"
                    );

                    File.Exists(readmePath).Should().BeFalse();
                }
            ),
            Row(
                "screen-removed-with-readme-regenerates",
                "README.md がある状態で画面を削除すると追従して再生成する",
                [(Conversation, Screen)],
                async h =>
                {
                    var vm = h.Create();
                    await h.SaveScreenOnClaudeCodeAsync(vm);
                    await MockDialogHarness.SaveScreenViaEngine(
                        vm,
                        h.Engine,
                        "Detail.html",
                        "注文詳細"
                    );
                    var readmePath = Path.Combine(h.MockFolder, MockDesignDocExporter.FileName);
                    File.WriteAllText(readmePath, DesignDocSentinel);

                    await MockDialogHarness.RunToolViaEngine(
                        vm,
                        h.Engine,
                        MockFolderDesignTools.RemoveScreenToolName,
                        new Dictionary<string, object?> { ["file"] = "Detail.html" }
                    );

                    var content = File.ReadAllText(readmePath);
                    content.Should().NotContain(DesignDocSentinel);
                    content.Should().NotContain("注文詳細");
                }
            ),
            Row(
                "screen-stylesheet-saved-does-not-regenerate-readme",
                "共有のスタイルシートの保存では README.md を再生成しない",
                [(Conversation, Screen)],
                async h =>
                {
                    var vm = h.Create();
                    await h.SaveScreenOnClaudeCodeAsync(vm);
                    var readmePath = Path.Combine(h.MockFolder, MockDesignDocExporter.FileName);
                    File.WriteAllText(readmePath, DesignDocSentinel);

                    await MockDialogHarness.RunToolViaEngine(
                        vm,
                        h.Engine,
                        MockFolderDesignTools.SaveStylesheetToolName,
                        new Dictionary<string, object?>
                        {
                            ["css"] = "body{}",
                            ["revision_note"] = "配色",
                        }
                    );

                    File.ReadAllText(readmePath).Should().Be(DesignDocSentinel);
                }
            ),
            Row(
                "export-bundle-writes-combined-html-to-picked-path",
                "単一 HTML の出力は、既存のモックフォルダを開くだけで有効になり、選んだパスへ結合した HTML を書いて完了を知らせる",
                [(Ready, Export)],
                h =>
                {
                    var outPath = h.PathOf("out.html");
                    h.SeedMockFolder();
                    h.Files.SaveResult = new FileDialogResult(outPath, 1);
                    var vm = h.CreateApiKeyReady(setMockFolder: false);

                    vm.MockFolder = h.MockFolder;
                    vm.CanExportBundle.Should().BeTrue();

                    vm.ExportBundleCommand.Execute(null);

                    h.Files.LastInitialFileName.Should().Be("mock.html");
                    File.Exists(outPath).Should().BeTrue();
                    var written = File.ReadAllText(outPath);
                    written.Should().Contain("顧客一覧");
                    written.Should().Contain("data-screen");
                    h.Dialogs.InformationMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Contain(outPath);
                }
            ),
            Row(
                "export-bundle-failure-notifies-status-without-throwing",
                "単一 HTML の出力は結合（画面 HTML の読み取り）の失敗を状態の文へ出し、落ちず、保存先も聞かない",
                [(Ready, Export)],
                h =>
                {
                    var outPath = h.PathOf("out.html");
                    h.SeedMockFolder();
                    h.Files.SaveResult = new FileDialogResult(outPath, 1);
                    var vm = h.CreateApiKeyReady(setMockFolder: false);
                    vm.MockFolder = h.MockFolder;
                    vm.CanExportBundle.Should().BeTrue();

                    // 画面 HTML の実体を排他で開き、結合の読み取りを失敗させる
                    using (
                        new FileStream(
                            Path.Combine(h.MockFolder, "OrderList.html"),
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.None
                        )
                    )
                    {
                        var act = () => vm.ExportBundleCommand.Execute(null);

                        act.Should().NotThrow();
                    }

                    vm.StatusMessage.Should()
                        .Contain(MockStrings.Mock_HtmlSaveFailedFormat.Split("{0}")[0]);
                    h.Files.PickSaveFileCallCount.Should().Be(0);
                    File.Exists(outPath).Should().BeFalse();
                    h.Dialogs.InformationMessages.Should().BeEmpty();
                }
            ),
            Row(
                "export-disabled-without-screens",
                "画面が無いと単一 HTML も設計書も出力できない",
                [(Ready, Export)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();

                    vm.HasScreens.Should().BeFalse();
                    vm.CanExportBundle.Should().BeFalse();
                    vm.ExportBundleCommand.CanExecute(null).Should().BeFalse();
                    vm.ExportDesignDocCommand.CanExecute(null).Should().BeFalse();
                }
            ),
            Row(
                "export-disabled-without-folder-and-picks-nothing",
                "モックフォルダが無いと出力できず、出力を実行しても保存先を聞かず何も書かない",
                [(Idle, Export)],
                h =>
                {
                    h.Files.SaveResult = new FileDialogResult(h.PathOf("out.html"), 1);
                    var vm = h.CreateApiKeyReady(setMockFolder: false);

                    vm.ExportBundleCommand.CanExecute(null).Should().BeFalse();
                    vm.ExportDesignDocCommand.CanExecute(null).Should().BeFalse();

                    vm.ExportBundleCommand.Execute(null);
                    vm.ExportDesignDocCommand.Execute(null);

                    h.Files.PickSaveFileCallCount.Should().Be(0);
                    File.Exists(h.PathOf("out.html")).Should().BeFalse();
                    h.Dialogs.InformationMessages.Should().BeEmpty();
                }
            ),
            Row(
                "export-design-doc-writes-readme-and-shows-dialog",
                "設計書の出力は README.md を書き（決定的な出力と同じ内容）、保存先つきの情報ダイアログを出す",
                [(Ready, Export)],
                h =>
                {
                    var vm = h.CreateApiKeyReady(setMockFolder: false);
                    h.SeedMockFolder();
                    vm.MockFolder = h.MockFolder;
                    vm.ExportDesignDocCommand.CanExecute(null).Should().BeTrue();

                    vm.ExportDesignDocCommand.Execute(null);

                    var readmePath = Path.Combine(h.MockFolder, MockDesignDocExporter.FileName);
                    File.Exists(readmePath).Should().BeTrue();
                    var expected = MockDesignDocExporter.Export(MockFolderStore.Open(h.MockFolder));
                    File.ReadAllText(readmePath).Should().Be(expected);
                    h.Dialogs.InformationMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Contain(readmePath);
                }
            ),
            Row(
                "export-design-doc-failure-notifies-status-without-throwing",
                "設計書の出力は生成（画面 HTML の読み取り）の失敗を状態の文へ出し、落ちず、README.md を書かない",
                [(Ready, Export)],
                h =>
                {
                    var vm = h.CreateApiKeyReady(setMockFolder: false);
                    h.SeedMockFolder();
                    vm.MockFolder = h.MockFolder;
                    var readmePath = Path.Combine(h.MockFolder, MockDesignDocExporter.FileName);

                    using (
                        new FileStream(
                            Path.Combine(h.MockFolder, "OrderList.html"),
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.None
                        )
                    )
                    {
                        var act = () => vm.ExportDesignDocCommand.Execute(null);

                        act.Should().NotThrow();
                    }

                    vm.StatusMessage.Should()
                        .Contain(MockStrings.Mock_DesignDocSaveFailedFormat.Split("{0}")[0]);
                    File.Exists(readmePath).Should().BeFalse();
                    h.Dialogs.InformationMessages.Should().BeEmpty();
                }
            ),
        ];
}
