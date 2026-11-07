using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogOp;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogState;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Dialog;

internal static partial class MockDialogRows
{
    /// <summary>モックフォルダの指定・使えないフォルダ・ターン中の拒否・画面全体のクリア</summary>
    private static IEnumerable<ScenarioRow<MockDialogCase>> FolderRows() =>
        [
            Row(
                "folder-required-to-start",
                "フォルダ未指定では会話を開始できず、空フォルダを指定すると開始できる",
                [(Idle, SetFolder)],
                h =>
                {
                    var vm = h.CreateApiKeyReady(setMockFolder: false);

                    vm.CanStartConversation.Should().BeFalse();
                    vm.StartConversationCommand.CanExecute(null).Should().BeFalse();

                    vm.MockFolder = h.MockFolder;
                    vm.CanStartConversation.Should().BeTrue();
                }
            ),
            Row(
                "folder-start-depends-on-diagram-emptiness",
                "空の図では会話を開始できず、空でなければ開始できる（フォルダ指定済み）",
                [(Ready, Converse)],
                h =>
                {
                    h.Diagram = new ErDiagram();
                    var emptyVm = h.CreateApiKeyReady();

                    emptyVm.IsDiagramEmpty.Should().BeTrue();
                    emptyVm.CanStartConversation.Should().BeFalse();

                    h.Diagram = MockDialogHarness.NonEmptyDiagram();
                    var vm = h.CreateApiKeyReady();

                    vm.IsDiagramEmpty.Should().BeFalse();
                    vm.CanStartConversation.Should().BeTrue();
                }
            ),
            Row(
                "folder-existing-mock-folder-populates-sidebar",
                "既存のモックフォルダを指定するだけで（会話の前でも）画面一覧に画面が並ぶ",
                [(Idle, SetFolder)],
                h =>
                {
                    var vm = h.CreateApiKeyReady(setMockFolder: false);
                    h.SeedMockFolder();

                    vm.MockFolder = h.MockFolder;

                    vm.Screens.Should().ContainSingle();
                    vm.Screens[0].File.Should().Be("OrderList.html");
                    vm.Screens[0].Name.Should().Be("注文一覧");
                    vm.HasScreens.Should().BeTrue();
                }
            ),
            Row(
                "folder-non-mock-folder-with-files-blocks-start",
                "モックフォルダでない中身のある既存フォルダは会話の開始を止めて案内する",
                [(Idle, SetFolder)],
                h =>
                {
                    var vm = h.CreateApiKeyReady(setMockFolder: false);
                    h.BlockFolder(vm, BlockedKind.NotAMockFolder);

                    vm.CanStartConversation.Should().BeFalse();
                    vm.StatusMessage.Should().Be(MockStrings.Mock_NotAMockFolder);
                }
            ),
            Row(
                "folder-corrupt-manifest-shows-error",
                "壊れた mock.json のフォルダは状態の文にエラーを出し、会話を開始できず画面も無い",
                [(Idle, SetFolder)],
                h =>
                {
                    var vm = h.CreateApiKeyReady(setMockFolder: false);
                    h.BlockFolder(vm, BlockedKind.CorruptManifest);

                    vm.CanStartConversation.Should().BeFalse();
                    vm.StatusMessage.Should().NotBeEmpty();
                    vm.Screens.Should().BeEmpty();
                }
            ),
            Row(
                "folder-invalid-screen-file-name-shows-error-and-blocks-start",
                "画面のファイル名が不正な（手編集で壊れた）mock.json も、壊れた mock.json と同じ経路でエラーを出して止める",
                [(Idle, SetFolder)],
                h =>
                {
                    var vm = h.CreateApiKeyReady(setMockFolder: false);
                    h.BlockFolder(vm, BlockedKind.InvalidScreenName);

                    vm.CanStartConversation.Should().BeFalse();
                    var fixedPart = MockStrings.Mock_MockFolderOpenFailedFormat.Split("{0}")[0];
                    vm.StatusMessage.Should().Contain(fixedPart);
                    vm.Screens.Should().BeEmpty();
                }
            ),
            .. new[]
            {
                ("relative", "quicker-relative-mock"),
                ("drive-relative", "C:quicker-relative-mock"),
            }.Select(c =>
                Row(
                    $"folder-not-fully-qualified-blocks-start-{c.Item1}",
                    $"フルパスでないモックフォルダ（{c.Item2}）は会話を開始できず案内を出し、フォルダを作らない。フルパスへ直せば始められる",
                    [(Idle, SetFolder), (Blocked, SetFolder)],
                    h =>
                    {
                        var vm = h.CreateApiKeyReady(setMockFolder: false);

                        vm.MockFolder = c.Item2;

                        vm.CanStartConversation.Should().BeFalse();
                        vm.StartConversationCommand.CanExecute(null).Should().BeFalse();
                        vm.StatusMessage.Should().Be(MockStrings.Mock_MockFolderNotFullyQualified);
                        Directory.Exists(Path.GetFullPath(c.Item2)).Should().BeFalse();

                        vm.MockFolder = h.MockFolder;
                        vm.CanStartConversation.Should().BeTrue();
                    }
                )
            ),
            Row(
                "folder-browse-applies-picked-folder",
                "参照ボタンで選んだフォルダがモックフォルダになり、キャンセルなら変わらない",
                [(Idle, SetFolder)],
                h =>
                {
                    var vm = h.CreateApiKeyReady(setMockFolder: false);

                    h.Files.FolderResult = null;
                    vm.BrowseMockFolderCommand.Execute(null);
                    vm.MockFolder.Should().BeEmpty();

                    h.Files.FolderResult = h.MockFolder;
                    vm.BrowseMockFolderCommand.Execute(null);
                    vm.MockFolder.Should().Be(h.MockFolder);
                    vm.CanStartConversation.Should().BeTrue();
                    h.Files.PickFolderCallCount.Should().Be(2);
                }
            ),
            Row(
                "folder-change-during-turn-is-refused-and-turn-still-completes",
                "ターンの実行中のモックフォルダの変更は断られ（値は変わらず束縛先へ読み直しの通知）、セッションを切り離さないのでターンの完了で実行中が解ける",
                [(Turn, SetFolder)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    var engine = await h.StartHeldTurnAsync(vm);
                    var notified = new List<string?>();
                    vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

                    // 同じフォルダの入れ直し（入力欄の確定など）は、ターン中でも断らずに何もしない
                    var statusBeforeSameValue = vm.StatusMessage;
                    vm.MockFolder = h.MockFolder;
                    vm.StatusMessage.Should()
                        .Be(statusBeforeSameValue, "同じ値の入れ直しは断らない");
                    notified
                        .Should()
                        .NotContain(
                            nameof(MockGenerationDialogViewModel.MockFolder),
                            "同じ値の入れ直しは変更として通知しない"
                        );

                    vm.MockFolder = h.PathOf("other");

                    vm.MockFolder.Should().Be(h.MockFolder);
                    vm.StatusMessage.Should().Be(MockStrings.Mock_MockFolderLockedDuringTurn);
                    notified
                        .Should()
                        .Contain(
                            nameof(MockGenerationDialogViewModel.MockFolder),
                            "束縛先へ元の値を読み直させる"
                        );
                    vm.DetachedDisposalForTests.Should().BeNull("セッションを切り離していない");
                    engine.DisposeCount.Should().Be(0);

                    engine.CompleteHeldTurn();

                    vm.IsTurnInProgress.Should().BeFalse();
                }
            ),
            Row(
                "folder-edit-disabled-during-turn",
                "ターンの実行中はモックフォルダの入力欄と参照ボタンが無効になり、完了で戻る",
                [(Ready, SetFolder), (Turn, SetFolder)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();

                    vm.CanEditMockFolder.Should().BeTrue();
                    vm.BrowseMockFolderCommand.CanExecute(null).Should().BeTrue();

                    var engine = await h.StartHeldTurnAsync(vm);

                    vm.CanEditMockFolder.Should().BeFalse();
                    vm.BrowseMockFolderCommand.CanExecute(null).Should().BeFalse();

                    engine.CompleteHeldTurn();

                    vm.CanEditMockFolder.Should().BeTrue();
                    vm.BrowseMockFolderCommand.CanExecute(null).Should().BeTrue();
                }
            ),
            Row(
                "folder-change-disposes-previous-session-engine",
                "フォルダの変更（会話のリセット）で旧セッションのエンジンが破棄される",
                [(Conversation, SetFolder)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);
                    var oldEngine = h.Engine;
                    oldEngine.DisposeCount.Should().Be(0, "会話中はまだ破棄されない");

                    vm.MockFolder = h.PathOf("mock2");

                    // 破棄はスレッドプールで走るため、VM が持つ破棄のタスクを待つ（短い上限のポーリングはプールの飽和で落ちる）
                    vm.DetachedDisposalForTests.Should()
                        .NotBeNull("切替で旧セッションの破棄が始まること");
                    await vm.DetachedDisposalForTests!;
                    oldEngine
                        .DisposeCount.Should()
                        .Be(1, "手放した旧セッションのエンジンは破棄されること");
                }
            ),
            Row(
                "reset-clears-everything-when-confirmed",
                "クリアは確認の後に会話・フォルダの選択・第 2 ステップの入力を初期状態へ戻し、ディスクのモックフォルダは残す",
                [(Ready, Reset)],
                h =>
                {
                    h.Dialogs.ConfirmResult = true;
                    var vm = h.CreateApiKeyReady(setMockFolder: false);
                    h.SeedMockFolder();

                    vm.MockFolder = h.MockFolder;
                    vm.Screens.Should().NotBeEmpty();
                    vm.UserInput = "入力途中のテキスト";
                    vm.OutputFolder = h.PathOf("out");
                    vm.ProjectName = "ShopMock";
                    vm.MockGenInstructions = "ダークテーマで";

                    vm.ClearCommand.Execute(null);

                    h.Dialogs.ConfirmMessages.Should().ContainSingle();
                    vm.MockFolder.Should().BeEmpty();
                    vm.Screens.Should().BeEmpty();
                    vm.HasScreens.Should().BeFalse();
                    vm.Messages.Should().BeEmpty();
                    vm.UserInput.Should().BeEmpty();
                    vm.OutputFolder.Should().BeEmpty();
                    vm.ProjectName.Should().Be("MockApp");
                    vm.MockGenInstructions.Should().BeEmpty();
                    MockFolderStore.IsMockFolder(h.MockFolder).Should().BeTrue();
                }
            ),
            Row(
                "reset-disabled-without-mock-folder",
                "モックフォルダ未選択ではクリアできず、選択で押せ、クリアで未選択へ戻ると再び押せない",
                [(Idle, Reset), (Ready, Reset)],
                h =>
                {
                    var vm = h.CreateApiKeyReady(setMockFolder: false);

                    vm.CanClear.Should().BeFalse();
                    vm.ClearCommand.CanExecute(null).Should().BeFalse();

                    vm.MockFolder = h.MockFolder;
                    vm.CanClear.Should().BeTrue();
                    vm.ClearCommand.CanExecute(null).Should().BeTrue();

                    vm.ClearCommand.Execute(null);
                    vm.CanClear.Should().BeFalse();
                    vm.ClearCommand.CanExecute(null).Should().BeFalse();
                }
            ),
            Row(
                "reset-does-nothing-when-cancelled",
                "クリアの確認をキャンセルすると何も変わらない",
                [(Ready, Reset)],
                h =>
                {
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.CreateApiKeyReady(setMockFolder: false);
                    h.SeedMockFolder();

                    vm.MockFolder = h.MockFolder;
                    vm.UserInput = "入力途中のテキスト";
                    vm.ProjectName = "ShopMock";

                    vm.ClearCommand.Execute(null);

                    h.Dialogs.ConfirmMessages.Should().ContainSingle();
                    vm.MockFolder.Should().Be(h.MockFolder);
                    vm.Screens.Should().NotBeEmpty();
                    vm.UserInput.Should().Be("入力途中のテキスト");
                    vm.ProjectName.Should().Be("ShopMock");
                }
            ),
            Row(
                "reset-restores-default-target",
                "クリア（確認 OK）で生成ターゲットが既定（Blazor）へ戻る",
                [(Ready, Reset)],
                h =>
                {
                    h.Dialogs.ConfirmResult = true;
                    var vm = h.Create();
                    h.SeedMockFolder();

                    vm.MockFolder = h.MockFolder;
                    vm.SelectedMockProjectTarget = MockProjectTarget.Wpf;

                    vm.ClearCommand.Execute(null);

                    vm.SelectedMockProjectTarget.Should().Be(MockProjectTarget.Blazor);
                }
            ),
            Row(
                "reset-during-conversation-detaches-session",
                "会話中のクリアは会話をリセットして旧セッションのエンジンを破棄し、送信できなくなる",
                [(Conversation, Reset)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);
                    vm.UserInput = "要望";
                    await vm.SendMessageCommand.ExecuteAsync(null);
                    var engine = h.Engine;

                    vm.ClearCommand.Execute(null);

                    vm.Messages.Should().BeEmpty();
                    vm.MockFolder.Should().BeEmpty();
                    vm.UserInput = "次の要望";
                    vm.CanSendMessage.Should().BeFalse();
                    vm.DetachedDisposalForTests.Should()
                        .NotBeNull("旧セッションは切り離して破棄する");
                    await vm.DetachedDisposalForTests!;
                    engine.DisposeCount.Should().Be(1);
                }
            ),
            Row(
                "blocked-backend-switch-keeps-start-blocked",
                "使えないフォルダのまま接続方式を切り替えても会話は開始できないまま（切替は止めない）",
                [(Blocked, Backend)],
                h =>
                {
                    var vm = h.CreateApiKeyReady(setMockFolder: false);
                    h.BlockFolder(vm, BlockedKind.NotAMockFolder);
                    vm.CanStartConversation.Should().BeFalse();

                    h.TryChangeBackend(vm, ErChatBackendKind.ClaudeCode).Should().BeTrue();
                    vm.ApplyClaudeCodeReadiness(
                        true,
                        "ログイン済み",
                        ConnectionHealth.Ready,
                        string.Empty
                    );

                    vm.IsBackendReady.Should().BeTrue();
                    vm.CanStartConversation.Should().BeFalse();
                    vm.MockFolder.Should().Be(h.MockFolder);
                }
            ),
            Row(
                "blocked-reset-clears-blocked-folder",
                "使えないフォルダのままクリアすると未選択へ戻り、状態の文の案内も消える",
                [(Blocked, Reset)],
                h =>
                {
                    var vm = h.CreateApiKeyReady(setMockFolder: false);
                    h.BlockFolder(vm, BlockedKind.Relative);
                    vm.StatusMessage.Should().Be(MockStrings.Mock_MockFolderNotFullyQualified);
                    vm.CanClear.Should().BeTrue();

                    vm.ClearCommand.Execute(null);

                    h.Dialogs.ConfirmMessages.Should().ContainSingle();
                    vm.MockFolder.Should().BeEmpty();
                    vm.StatusMessage.Should().BeEmpty();
                    vm.CanStartConversation.Should().BeFalse();

                    vm.MockFolder = h.MockFolder;
                    vm.CanStartConversation.Should().BeTrue();
                }
            ),
        ];
}
