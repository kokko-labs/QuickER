using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Gui.Abstractions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogOp;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogState;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Dialog;

internal static partial class MockDialogRows
{
    /// <summary>
    /// 状態の列が薄かった組（使えないフォルダ・ターン／生成中・生成の後・設定の保存）の行。
    /// 出典は src/QuickER.AI.Mock/MockGenerationDialogViewModel.cs（以下 VM）の行
    /// </summary>
    private static IEnumerable<ScenarioRow<MockDialogCase>> StateGapRows() =>
        [
            // ── 会話の開始・送信 ──
            Row(
                "idle-converse-send-disabled-without-conversation",
                "モックフォルダが無い（会話を開始していない）ときは、入力があっても送信できず、直接実行してもエンジンを作らない",
                [(Idle, Converse)],
                async h =>
                {
                    // 根拠：CanSendMessage の XmlDoc「会話開始済み…入力あり」（VM:228-241）。本体の防御は `_session is null`（VM:1002）
                    var vm = h.CreateApiKeyReady(setMockFolder: false);

                    vm.UserInput = "要望";

                    vm.CanSendMessage.Should().BeFalse();
                    vm.SendMessageCommand.CanExecute(null).Should().BeFalse();

                    await vm.SendMessageCommand.ExecuteAsync(null);

                    h.Engines.Should().BeEmpty();
                    vm.Messages.Should().BeEmpty();
                }
            ),
            .. BlockedKinds.Select(kind =>
                Row(
                    $"blocked-converse-start-and-send-disabled-{Slug(kind)}",
                    $"使えないフォルダ（{kind}）では会話の開始も送信も押せず、エンジンを作らず、状態の文の案内が残る",
                    [(Blocked, Converse)],
                    h =>
                    {
                        // 根拠：CanStartConversation の `_folderBlocked`（VM:215-226）と CanSendMessage の `_conversationStarted`（VM:235）。
                        // 開始のコマンドの本体に防御が無い（VM:936-956）ので、直接実行はしない
                        var vm = h.CreateApiKeyReady(setMockFolder: false);

                        h.BlockFolder(vm, kind);
                        vm.UserInput = "要望";

                        vm.CanStartConversation.Should().BeFalse();
                        vm.StartConversationCommand.CanExecute(null).Should().BeFalse();
                        vm.CanSendMessage.Should().BeFalse();
                        vm.SendMessageCommand.CanExecute(null).Should().BeFalse();
                        h.Engines.Should().BeEmpty();
                        vm.Messages.Should().BeEmpty();
                        vm.StatusMessage.Should().StartWith(BlockedGuidance(kind));
                    }
                )
            ),
            Row(
                "generate-done-converse-sends-raw-feedback",
                "生成の後も送信でき、送るのは開始のプロンプトでなく生のフィードバックになる",
                [(GenDone, Converse)],
                async h =>
                {
                    // 根拠：CanSendMessage は生成の非実行中だけを見る（VM:235-241）。初回の済みは成功したターンでだけ立つ（src/QuickER.AI.Mock/CLAUDE.md「初回ターン」・不変条件 6）
                    var vm = h.Create();
                    await h.CompleteGenerationAsync(vm);
                    var sentBefore = h.Engine.SentPrompts.Count;

                    vm.UserInput = "列を減らして";

                    vm.CanSendMessage.Should().BeTrue();
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    h.Engine.SentPrompts.Should().HaveCount(sentBefore + 1);
                    h.Engine.SentPrompts[^1].Should().Be("列を減らして");
                    h.Engine.SentPrompts[^1]
                        .Should()
                        .NotContain(MockStrings.Mock_PromptUserInstructionsHeading);
                }
            ),
            // ── 添付 ──
            Row(
                "attach-before-folder-is-accepted",
                "モックフォルダを選ぶ前でも、対応する接続方式なら添付を足せる",
                [(Idle, Attach)],
                h =>
                {
                    // 根拠：AttachmentListViewModel.IsEnabled の XmlDoc「対応エンジン・ターン非実行中」（src/QuickER.AI.UI/AttachmentListViewModel.cs:100-101）。フォルダは条件に無い
                    var vm = h.Create();
                    vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;

                    vm.MockFolder.Should().BeEmpty();
                    vm.Attachments.IsEnabled.Should().BeTrue();
                    vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);

                    vm.Attachments.Items.Should().HaveCount(1);
                }
            ),
            .. BlockedKinds.Select(kind =>
                Row(
                    $"attach-survives-blocked-folder-{Slug(kind)}",
                    $"特性の固定（文書に定めが無い今の振る舞い）：使えないフォルダ（{kind}）へ変えても、送信待ちの添付は残り、添付は足せるまま",
                    [(Blocked, Attach)],
                    h =>
                    {
                        // 根拠：ResetConversation は Attachments を触らない（VM:2074-2083）。IsEnabled はフォルダを見ない（AttachmentListViewModel.cs:101）
                        var vm = h.Create();
                        vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
                        vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);
                        vm.Attachments.Items.Should().HaveCount(1);

                        h.BlockFolder(vm, kind);

                        vm.CanStartConversation.Should().BeFalse();
                        vm.Attachments.Items.Should().HaveCount(1);
                        vm.Attachments.IsEnabled.Should().BeTrue();
                        vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now.AddSeconds(1));
                        vm.Attachments.Items.Should().HaveCount(2);
                    }
                )
            ),
            Row(
                "attach-during-generation-kept-until-sent-after",
                "生成中も添付を足せ、生成が終わっても消えず、生成の後に送ればエンジンへ渡って消える",
                [(GenRun, Attach)],
                async h =>
                {
                    // 根拠：IsEnabled は生成を見ない（AttachmentListViewModel.cs:101）。消えるのはターンの成功だけ（VM:1912-1930 の ApplyTurnCompleted）
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    var sentBefore = h.Engine.SentAttachments.Count;
                    _ = h.StartHeldGeneration(vm);

                    vm.Attachments.IsEnabled.Should().BeTrue();
                    vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);
                    vm.Attachments.Items.Should().HaveCount(1);

                    await h.ReleaseHeldGenerationAsync();

                    vm.IsMockGenInProgress.Should().BeFalse();
                    vm.Attachments.Items.Should().HaveCount(1, "生成の終わりでは消えない");

                    vm.UserInput = "この画面イメージで直して";
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    h.Engine.SentAttachments.Should().HaveCount(sentBefore + 1);
                    h.Engine.SentAttachments[^1].Should().HaveCount(1);
                    vm.Attachments.Items.Should().BeEmpty();
                }
            ),
            Row(
                "attach-after-generation-passes-to-engine-and-clears",
                "生成の後に足した添付は、送ればエンジンへ渡って消える",
                [(GenDone, Attach)],
                async h =>
                {
                    // 根拠：IsEnabled は生成を見ない（AttachmentListViewModel.cs:101）。消えるのはターンの成功だけ（VM:1912-1930 の ApplyTurnCompleted）
                    var vm = h.Create();
                    await h.CompleteGenerationAsync(vm);
                    var sentBefore = h.Engine.SentAttachments.Count;

                    vm.Attachments.IsEnabled.Should().BeTrue();
                    vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);
                    vm.Attachments.Items.Should().HaveCount(1);

                    vm.UserInput = "この画面イメージで直して";
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    h.Engine.SentAttachments.Should().HaveCount(sentBefore + 1);
                    h.Engine.SentAttachments[^1].Should().HaveCount(1);
                    vm.Attachments.Items.Should().BeEmpty();
                }
            ),
            // ── 出力 ──
            .. BlockedKinds.Select(kind =>
                Row(
                    $"blocked-export-disabled-and-picks-nothing-{Slug(kind)}",
                    $"使えないフォルダ（{kind}）では単一 HTML も設計書も出力できず、実行しても保存先を聞かず何も書かない",
                    [(Blocked, Export)],
                    h =>
                    {
                        // 根拠：CanExportBundle＝HasScreens（VM:262-267）。本体の防御は `_store is null`（VM:1078・1134）
                        var outPath = h.PathOf("out.html");
                        h.Files.SaveResult = new FileDialogResult(outPath, 1);
                        var vm = h.CreateApiKeyReady(setMockFolder: false);

                        h.BlockFolder(vm, kind);

                        vm.CanExportBundle.Should().BeFalse();
                        vm.ExportBundleCommand.CanExecute(null).Should().BeFalse();
                        vm.ExportDesignDocCommand.CanExecute(null).Should().BeFalse();

                        vm.ExportBundleCommand.Execute(null);
                        vm.ExportDesignDocCommand.Execute(null);

                        h.Files.PickSaveFileCallCount.Should().Be(0);
                        File.Exists(outPath).Should().BeFalse();
                        File.Exists(Path.Combine(h.MockFolder, MockDesignDocExporter.FileName))
                            .Should()
                            .BeFalse();
                        h.Dialogs.InformationMessages.Should().BeEmpty();
                    }
                )
            ),
            Row(
                "conversation-export-keeps-conversation",
                "画面を作った会話中に単一 HTML と設計書を出せ、会話のエンジンは破棄されず、会話はそのまま残る",
                [(Conversation, Export)],
                async h =>
                {
                    // 根拠：CanExportBundle は画面の有無だけ（VM:262-267）。出力は図のストアを読むだけで会話（セッション・Messages）に触れない（VM:1075-1160 は _session を使わない）
                    var vm = h.Create();
                    await h.SaveScreenOnClaudeCodeAsync(vm);
                    var engine = h.Engine;
                    var messages = vm.Messages.Select(m => m.Content).ToList();
                    var outPath = h.PathOf("out.html");
                    h.Files.SaveResult = new FileDialogResult(outPath, 1);

                    vm.ExportBundleCommand.CanExecute(null).Should().BeTrue();
                    vm.ExportBundleCommand.Execute(null);
                    vm.ExportDesignDocCommand.CanExecute(null).Should().BeTrue();
                    vm.ExportDesignDocCommand.Execute(null);

                    File.ReadAllText(outPath).Should().Contain("顧客一覧");
                    File.Exists(Path.Combine(h.MockFolder, MockDesignDocExporter.FileName))
                        .Should()
                        .BeTrue();
                    engine.DisposeCount.Should().Be(0);
                    vm.DetachedDisposalForTests.Should().BeNull();
                    vm.Messages.Select(m => m.Content).Should().Equal(messages);
                }
            ),
            Row(
                "turn-export-bundle-does-not-interrupt-turn",
                "特性の固定（文書に定めが無い今の振る舞い）：画面のあるフォルダのターン中でも単一 HTML を出力でき、ターンは中断されず進めれば終わる",
                [(Turn, Export)],
                async h =>
                {
                    // 根拠：CanExportBundle＝HasScreens はターンを見ない（VM:262-267）。設計書はこの行で出さない（生成中の設計書と同じ論点が P5 に残る）
                    var outPath = h.PathOf("out.html");
                    h.SeedMockFolder();
                    h.Files.SaveResult = new FileDialogResult(outPath, 1);
                    var vm = h.CreateApiKeyReady();
                    var engine = await h.StartHeldTurnAsync(vm);

                    vm.CanExportBundle.Should().BeTrue();
                    vm.ExportBundleCommand.CanExecute(null).Should().BeTrue();
                    vm.ExportBundleCommand.Execute(null);

                    File.ReadAllText(outPath).Should().Contain("顧客一覧");
                    vm.IsTurnInProgress.Should().BeTrue();
                    engine.InterruptCount.Should().Be(0);

                    engine.CompleteHeldTurn();

                    vm.IsTurnInProgress.Should().BeFalse();
                }
            ),
            Row(
                "generate-done-export-bundle-and-design-doc",
                "生成の後も単一 HTML と設計書を出力できる",
                [(GenDone, Export)],
                async h =>
                {
                    // 根拠：CanExportBundle は画面の有無だけ（VM:262-267）で、生成はモックフォルダを読むだけ（VM:1256）
                    var vm = h.Create();
                    await h.CompleteGenerationAsync(vm);
                    var outPath = h.PathOf("out.html");
                    h.Files.SaveResult = new FileDialogResult(outPath, 1);

                    vm.ExportBundleCommand.CanExecute(null).Should().BeTrue();
                    vm.ExportBundleCommand.Execute(null);
                    vm.ExportDesignDocCommand.CanExecute(null).Should().BeTrue();
                    vm.ExportDesignDocCommand.Execute(null);

                    File.ReadAllText(outPath).Should().Contain("顧客一覧");
                    File.Exists(Path.Combine(h.MockFolder, MockDesignDocExporter.FileName))
                        .Should()
                        .BeTrue();
                }
            ),
            // ── 生成 ──
            .. BlockedKinds.Select(kind =>
                Row(
                    $"blocked-generate-disabled-after-ready-{Slug(kind)}",
                    $"生成できる状態から使えないフォルダ（{kind}）へ移すと、生成できず理由が画面なしになり、直接実行しても生成器を呼ばない",
                    [(Blocked, Generate)],
                    async h =>
                    {
                        // 根拠：MockGenDisabledReason の画面なし（VM:410-428）。本体の防御は `_store is null`（VM:1250）
                        var vm = h.Create();
                        await h.PrepareGenerationAsync(vm);
                        vm.CanGenerateMockProject.Should().BeTrue();

                        h.BlockFolder(vm, kind, h.PathOf("blocked-mock"));

                        vm.CanGenerateMockProject.Should().BeFalse();
                        vm.MockGenDisabledReason.Should()
                            .Be(MockStrings.Mock_DisabledReason_NoScreens);
                        vm.GenerateMockProjectCommand.CanExecute(null).Should().BeFalse();

                        await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                        h.Generator.GenerateCallCount.Should().Be(0);
                        vm.IsMockGenInProgress.Should().BeFalse();
                        vm.MockGenCompleted.Should().BeFalse();
                    }
                )
            ),
            // ── 設定の保存 ──
            .. new[] { Blocked, Ready, Turn, GenRun, GenDone }.Select(state =>
                Row(
                    $"persist-save-and-hide-keeps-running-work-{Slug(state)}",
                    $"{state} で × により設定を保存して隠しても、次の起動が接続方式を戻し、中断も破棄もせず、ターン・生成は隠した後も進めれば終わる",
                    [(state, Persist)],
                    async h =>
                    {
                        // 根拠：SaveSettings / SaveSettingsAndHide の XmlDoc（VM:1958-1996）と、× は隠して状態を保つ OnWindowClosing（src/QuickER.AI.Mock/MockGenerationDialog.xaml.cs:180-193）。
                        // 中断と破棄は ForceClose（アプリ終了）だけが行う
                        var (vm, resume) = await ReachPersistStateAsync(h, state);
                        var hidden = 0;

                        vm.SaveSettingsAndHide(() => hidden++);

                        hidden.Should().Be(1);
                        h.Dialogs.ErrorMessages.Should().BeEmpty();
                        vm.IsTurnInProgress.Should().Be(state == Turn);
                        vm.IsMockGenInProgress.Should().Be(state == GenRun);
                        h.Engines.Should()
                            .OnlyContain(e => e.InterruptCount == 0 && e.DisposeCount == 0);
                        h.Generator.InterruptCount.Should().Be(0);
                        h.Create()
                            .Connection.InitialBackend.Should()
                            .Be(ErChatBackendKind.ClaudeCode);

                        await resume();

                        vm.IsTurnInProgress.Should().BeFalse();
                        vm.IsMockGenInProgress.Should().BeFalse();
                        vm.MockGenCompleted.Should().Be(state is GenRun or GenDone);
                    }
                )
            ),
            // ── 終了 ──
            Row(
                "blocked-shutdown-on-fresh-dialog-touches-nothing",
                "使えないフォルダ（相対パス）を入れただけの新しいダイアログの終了の経路は、何にも触らず例外を投げない",
                [(Blocked, Shutdown)],
                h =>
                {
                    // 根拠：ShutdownEngines の remarks（セッションも切り離した破棄も無ければ何もしない。VM:1527-1536）
                    var vm = h.CreateApiKeyReady(setMockFolder: false);
                    h.BlockFolder(vm, BlockedKind.Relative);

                    var close = () =>
                    {
                        vm.RequestInterrupt();
                        h.ShutdownEngines(vm, TimeSpan.FromSeconds(5));
                    };

                    close.Should().NotThrow();
                    h.Generator.InterruptCount.Should().Be(0);
                    h.Engines.Should().BeEmpty();
                    vm.DetachedDisposalForTests.Should().BeNull();
                }
            ),
            Row(
                "blocked-shutdown-waits-for-detached-disposal-after-conversation",
                "会話から使えないフォルダへ移した直後に閉じても、切り離した旧セッションのエンジンをちょうど 1 回破棄してから戻る",
                [(Blocked, Shutdown)],
                h =>
                {
                    // 根拠：ShutdownEngines の remarks（切り離した破棄も待つ。VM:1515-1577）。フォルダの変更は会話をリセットして旧セッションを破棄する（VM:1749-1775）
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);
                    var engine = h.Engine;

                    h.BlockFolder(vm, BlockedKind.NotAMockFolder, h.PathOf("blocked-mock"));

                    vm.DetachedDisposalForTests.Should()
                        .NotBeNull("旧セッションの破棄が始まっている");

                    h.ShutdownEngines(vm, MockDialogHarness.Patience);

                    engine.DisposeCount.Should().Be(1);
                }
            ),
            Row(
                "generate-done-shutdown-disposes-session-without-interrupt",
                "生成の後の終了の経路は、実行中でないので生成器にもエンジンにも中断を届けず、会話のエンジンを 1 回破棄する",
                [(GenDone, Shutdown)],
                async h =>
                {
                    // 根拠：RequestInterrupt は実行中のものだけを中断する（VM:1493-1511）。ShutdownEngines の remarks（VM:1515-1577）
                    var vm = h.Create();
                    await h.CompleteGenerationAsync(vm);

                    vm.RequestInterrupt();

                    h.Generator.InterruptCount.Should().Be(0);
                    h.Engine.InterruptCount.Should().Be(0);

                    h.ShutdownEngines(vm, MockDialogHarness.Patience);

                    h.Engine.DisposeCount.Should().Be(1);
                }
            ),
            // ── クリア ──
            Row(
                "turn-reset-disabled-until-turn-ends",
                "ターンの実行中はクリアできず確認も出さず、終われば押せる",
                [(Turn, Reset)],
                async h =>
                {
                    // 根拠：CanClear の XmlDoc「ターン／モックプロジェクト生成の実行中は不可」（VM:1194-1196）。
                    // クリアの本体に防御が無い（VM:1203-1230）ので、直接実行はしない
                    var vm = h.CreateApiKeyReady();
                    var engine = await h.StartHeldTurnAsync(vm);

                    vm.CanClear.Should().BeFalse();
                    vm.ClearCommand.CanExecute(null).Should().BeFalse();
                    h.Dialogs.ConfirmMessages.Should().BeEmpty();

                    engine.CompleteHeldTurn();

                    vm.CanClear.Should().BeTrue();
                    vm.ClearCommand.CanExecute(null).Should().BeTrue();
                }
            ),
            Row(
                "generate-run-reset-disabled-until-generation-ends",
                "生成の実行中はクリアできず確認も出さず、終われば押せる",
                [(GenRun, Reset)],
                async h =>
                {
                    // 根拠：CanClear の XmlDoc「ターン／モックプロジェクト生成の実行中は不可」（VM:1194-1196）。
                    // クリアの本体に防御が無い（VM:1203-1230）ので、直接実行はしない
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    _ = h.StartHeldGeneration(vm);

                    vm.CanClear.Should().BeFalse();
                    vm.ClearCommand.CanExecute(null).Should().BeFalse();
                    h.Dialogs.ConfirmMessages.Should().BeEmpty();

                    await h.ReleaseHeldGenerationAsync();

                    vm.CanClear.Should().BeTrue();
                    vm.ClearCommand.CanExecute(null).Should().BeTrue();
                }
            ),
            // ── モックフォルダの指定 ──
            Row(
                "generate-run-set-folder-keeps-start-folder-and-completes",
                "特性の固定（文書に定めが無い今の振る舞い）：生成中もモックフォルダを変えられ、会話はリセットされるが、生成は開始時のモックフォルダで進み、進めれば完了が届く",
                [(GenRun, SetFolder)],
                async h =>
                {
                    // 根拠：MockFolder の setter が断るのはターン中だけ（VM:136-164）。生成は開始時に控えた `_store.Folder` で進み（VM:1256）、
                    // 完了は finally で実行中を解く（VM:1362-1368）
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    var oldEngine = h.Engine;
                    _ = h.StartHeldGeneration(vm);
                    var otherFolder = h.PathOf("other");

                    vm.MockFolder = otherFolder;

                    vm.MockFolder.Should().Be(otherFolder);
                    vm.Messages.Should().BeEmpty();
                    vm.Screens.Should().BeEmpty();
                    vm.IsMockGenInProgress.Should().BeTrue("生成は続いている");
                    vm.DetachedDisposalForTests.Should().NotBeNull();
                    await vm.DetachedDisposalForTests!;
                    oldEngine.DisposeCount.Should().Be(1);
                    h.Generator.CapturedMockFolder.Should().Be(h.MockFolder);

                    await h.ReleaseHeldGenerationAsync();

                    vm.IsMockGenInProgress.Should().BeFalse();
                    vm.MockGenCompleted.Should().BeTrue();
                    vm.ShowOpenFolder.Should().BeTrue();
                    vm.CanEditMockGenInput.Should().BeTrue();
                    h.Generator.GenerateCallCount.Should().Be(1);
                    h.Generator.CapturedMockFolder.Should().Be(h.MockFolder);

                    vm.MockFolder = h.MockFolder;

                    vm.Screens.Should().ContainSingle();
                    vm.CanGenerateMockProject.Should().BeTrue();
                }
            ),
            Row(
                "generate-done-set-folder-resets-conversation-keeps-open-folder",
                "生成の後にフォルダを変えると会話・画面はリセットされ、「フォルダを開く」は最後に生成したフォルダを開く（完了の表示が残ることは特性の固定＝文書に定めが無い今の振る舞い）",
                [(GenDone, SetFolder)],
                async h =>
                {
                    // 根拠：_lastGeneratedOutputDirectory の XmlDoc「破棄は生成の開始と画面の初期化だけ」（VM:1469-1475）。
                    // 表示が残るのは OnMockFolderChanged が生成の結果表示を触らないため（VM:716-785。クリアだけが戻す＝VM:1223-1228）
                    var vm = h.Create();
                    await h.CompleteGenerationAsync(vm);
                    var oldEngine = h.Engine;

                    vm.MockFolder = h.PathOf("other");

                    vm.Messages.Should().BeEmpty();
                    vm.Screens.Should().BeEmpty();
                    vm.HasScreens.Should().BeFalse();
                    vm.DetachedDisposalForTests.Should().NotBeNull();
                    await vm.DetachedDisposalForTests!;
                    oldEngine.DisposeCount.Should().Be(1);

                    vm.ShowOpenFolder.Should().BeTrue("完了の表示は残る（特性）");
                    vm.OpenOutputFolderCommand.Execute(null);

                    h.Opened.Should().Equal(h.PathOf("out"));
                }
            ),
            // ── 接続方式 ──
            Row(
                "generate-done-backend-switch-resets-conversation-and-gates-generation",
                "生成の後に接続方式を切り替えると、会話はリセットされて旧エンジンを破棄し、生成の可否は切替先の接続で決まる",
                [(GenDone, Backend)],
                async h =>
                {
                    // 根拠：TryChangeBackend / CanChangeBackend の XmlDoc「生成の中は切り替えてよい」（VM:253-258・2044-2065）。
                    // 切替は会話をリセットする（VM:2010-2015）。生成の可否は選択中の接続の準備で決まる（VM:395-407・435-437）
                    var vm = h.Create();
                    await h.CompleteGenerationAsync(vm);
                    var oldEngine = h.Engine;
                    vm.CanGenerateMockProject.Should().BeTrue();

                    h.TryChangeBackend(vm, ErChatBackendKind.Codex).Should().BeTrue();

                    vm.Messages.Should().BeEmpty();
                    vm.DetachedDisposalForTests.Should().NotBeNull();
                    await vm.DetachedDisposalForTests!;
                    oldEngine.DisposeCount.Should().Be(1);
                    vm.CanGenerateMockProject.Should().BeFalse();
                    vm.MockGenDisabledReason.Should()
                        .Be(MockStrings.Mock_DisabledReason_CodexNotReady);

                    vm.ApplyCodexReadiness(true, "ok", ConnectionHealth.Ready);

                    vm.CanGenerateMockProject.Should().BeTrue();
                    vm.MockGenDisabledReason.Should().BeEmpty();
                }
            ),
            // ── 画面 ──
            Row(
                "generate-done-screen-saved-updates-list-and-allows-generation",
                "生成の後の save_screen が画面一覧・選択・プレビューへ反映され、生成できる状態のまま",
                [(GenDone, Screen)],
                async h =>
                {
                    // 根拠：ApplyScreenSaved の XmlDoc「サイドバー再読込＋その画面をプレビュー表示」（VM:1838-1854）。生成の完了は画面の保存を止めない
                    var vm = h.Create();
                    await h.CompleteGenerationAsync(vm);
                    var requests = new List<MockPreviewRequest>();
                    vm.PreviewRequested += (_, request) => requests.Add(request);
                    vm.Screens.Should().ContainSingle();

                    await MockDialogHarness.SaveScreenViaEngine(
                        vm,
                        h.Engine,
                        "Detail.html",
                        "注文詳細"
                    );

                    vm.Screens.Select(s => s.File).Should().Equal("OrderList.html", "Detail.html");
                    vm.SelectedScreen.Should().NotBeNull();
                    vm.SelectedScreen!.File.Should().Be("Detail.html");
                    requests.Should().NotBeEmpty();
                    Path.GetFileName(requests[^1].FilePath).Should().Be("Detail.html");
                    vm.CanGenerateMockProject.Should().BeTrue();
                    vm.MockGenDisabledReason.Should().BeEmpty();
                }
            ),
        ];

    /// <summary>行の Id に使う、列挙値の小文字の名前</summary>
    private static string Slug(Enum value) => value.ToString().ToLowerInvariant();

    /// <summary>使えないフォルダの全種類</summary>
    private static IEnumerable<BlockedKind> BlockedKinds => Enum.GetValues<BlockedKind>();

    /// <summary>使えないフォルダの種類ごとに状態の文へ出る案内の先頭</summary>
    private static string BlockedGuidance(BlockedKind kind) =>
        kind switch
        {
            BlockedKind.Relative => MockStrings.Mock_MockFolderNotFullyQualified,
            BlockedKind.NotAMockFolder => MockStrings.Mock_NotAMockFolder,
            _ => FormatPrefix(MockStrings.Mock_MockFolderOpenFailedFormat),
        };

    /// <summary>
    /// 設定の保存の行の状態を作る。接続方式は Claude Code に替えてあり（保存が戻すものを見分けられるように）、
    /// 返す関数は走っているターン・生成を進めて終わらせる（走っていなければ何もしない）
    /// </summary>
    private static async Task<(
        MockGenerationDialogViewModel Vm,
        Func<Task> Resume
    )> ReachPersistStateAsync(MockDialogHarness h, MockDialogState state)
    {
        MockGenerationDialogViewModel vm;

        switch (state)
        {
            case Blocked:
                vm = h.CreateApiKeyReady(setMockFolder: false);
                vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
                h.BlockFolder(vm, BlockedKind.NotAMockFolder);
                return (vm, () => Task.CompletedTask);
            case Ready:
                vm = h.CreateApiKeyReady();
                vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
                return (vm, () => Task.CompletedTask);
            case Turn:
                vm = h.Create();
                vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
                vm.ApplyClaudeCodeReadiness(
                    true,
                    "ログイン済み",
                    ConnectionHealth.Ready,
                    string.Empty
                );
                vm.MockFolder = h.MockFolder;
                var engine = await h.StartHeldTurnAsync(vm);
                return (
                    vm,
                    () =>
                    {
                        engine.CompleteHeldTurn();
                        return Task.CompletedTask;
                    }
                );
            case GenRun:
                vm = h.Create();
                await h.PrepareGenerationAsync(vm);
                _ = h.StartHeldGeneration(vm);
                return (vm, h.ReleaseHeldGenerationAsync);
            case GenDone:
                vm = h.Create();
                await h.CompleteGenerationAsync(vm);
                return (vm, () => Task.CompletedTask);
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }
}
