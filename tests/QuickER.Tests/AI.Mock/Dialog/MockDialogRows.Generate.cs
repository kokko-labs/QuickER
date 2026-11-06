using System.Globalization;
using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Platform;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogOp;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogState;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Dialog;

internal static partial class MockDialogRows
{
    /// <summary>モックプロジェクトの生成（第 2 ステップ）：可否と理由・確認・実行・進捗・結果の分類・フォルダを開く</summary>
    private static IEnumerable<ScenarioRow<MockDialogCase>> GenerateRows() =>
        [
            Row(
                "generate-requires-all-conditions",
                "画面・接続・CLI・入力が揃うと生成でき、画面が無い・出力フォルダが空のときは理由が出る",
                [(Ready, Generate), (Conversation, Generate)],
                async h =>
                {
                    var vm = h.Create();
                    await vm.RefreshMockGenAvailabilityAsync();

                    vm.CanGenerateMockProject.Should().BeFalse();
                    vm.MockGenDisabledReason.Should().Be(MockStrings.Mock_DisabledReason_NoScreens);

                    await h.SaveScreenOnClaudeCodeAsync(vm);
                    vm.OutputFolder = h.PathOf("out");
                    vm.ProjectName = "AcmeMock";

                    vm.CanGenerateMockProject.Should().BeTrue();

                    vm.OutputFolder = string.Empty;
                    vm.CanGenerateMockProject.Should().BeFalse();
                    vm.MockGenDisabledReason.Should()
                        .Be(MockStrings.Mock_DisabledReason_OutputFolder);
                }
            ),
            .. new[]
            {
                ("parent-backslash", "..\\outside", PathSegmentViolation.Separator),
                ("parent-slash", "../outside", PathSegmentViolation.Separator),
                ("sub-folder", "sub/dir", PathSegmentViolation.Separator),
                ("colon", "bad:name", PathSegmentViolation.InvalidCharacter),
                ("device-name", "CON", PathSegmentViolation.ReservedDeviceName),
            }.Select(c =>
                Row(
                    $"generate-invalid-project-name-is-refused-{c.Item1}",
                    $"不正なプロジェクト名（{c.Item2}）では生成できず、当たった規則を名指しする専用の理由が出る",
                    [(Conversation, Generate)],
                    async h =>
                    {
                        var vm = h.Create();
                        await h.PrepareGenerationAsync(vm);
                        vm.CanGenerateMockProject.Should().BeTrue();

                        vm.ProjectName = c.Item2;

                        vm.CanGenerateMockProject.Should().BeFalse();
                        vm.MockGenDisabledReason.Should()
                            .Be(
                                string.Format(
                                    MockStrings.Mock_DisabledReason_ProjectNameInvalid,
                                    PathSegments.Describe(c.Item3, CultureInfo.CurrentUICulture)
                                )
                            );
                    }
                )
            ),
            .. new[] { "Design", "Bin", "Obj", "Generated" }.Select(reserved =>
                Row(
                    $"generate-reserved-project-name-is-refused-{reserved.ToLowerInvariant()}",
                    $"予約されたフォルダ名（{reserved}）のプロジェクト名では生成できず、前後の空白は取り除いて判定する",
                    [(Conversation, Generate)],
                    async h =>
                    {
                        var vm = h.Create();
                        await h.PrepareGenerationAsync(vm);

                        vm.ProjectName = reserved;
                        vm.CanGenerateMockProject.Should().BeFalse();
                        vm.MockGenDisabledReason.Should()
                            .Be(
                                string.Format(
                                    MockStrings.Mock_DisabledReason_ProjectNameReserved,
                                    reserved,
                                    string.Join(", ", MockProjectEmitTools.ProtectedFolderNames)
                                )
                            );

                        vm.ProjectName = " AcmeMock ";
                        vm.CanGenerateMockProject.Should().BeTrue();
                        vm.MockGenDisabledReason.Should().BeEmpty();
                    }
                )
            ),
            Row(
                "generate-refused-when-claude-missing",
                "claude が見つからなければ生成できず、理由に claude が出る",
                [(Conversation, Generate)],
                async h =>
                {
                    h.Generator.ClaudeAvailable = false;
                    var vm = h.Create();

                    await h.PrepareGenerationAsync(vm);

                    vm.CanGenerateMockProject.Should().BeFalse();
                    vm.MockGenDisabledReason.Should().Contain("claude");
                }
            ),
            Row(
                "generate-wires-build-confirmation",
                "最終ビルドの確認を生成器へ配線し、呼ぶと一覧を畳まない警告の確認になり、取り消しは false で返る",
                [(Conversation, Generate)],
                async h =>
                {
                    h.Dialogs.ConfirmResult = true;
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Generator.CapturedConfirmBuild.Should().NotBeNull();
                    h.Generator.CapturedConfirmBuild!([
                            "global.json",
                            "AcmeMock/AcmeMock.csproj.user",
                        ])
                        .Should()
                        .BeTrue();
                    var (message, details, _) = h.Dialogs.WarningConfirmDetailsMessages[^1];
                    message.Should().Be(MockStrings.Mock_BuildConfirm_Message);
                    details.Should().Contain("global.json").And.Contain("AcmeMock.csproj.user");

                    h.Dialogs.ConfirmResult = false;
                    h.Generator.CapturedConfirmBuild!(["global.json"]).Should().BeFalse();
                }
            ),
            Row(
                "generate-forwards-arguments-and-progress-and-shows-open-folder",
                "生成は出力フォルダ・名前・モックフォルダ・追加の指示を渡し、進捗をログへ写し、成功でフォルダを開くボタンを出す",
                [(Conversation, Generate)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    vm.MockGenInstructions = "ダークテーマで実装して";

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Generator.GenerateCallCount.Should().Be(1);
                    h.Generator.CapturedOutputFolder.Should().Be(h.PathOf("out"));
                    h.Generator.CapturedProjectName.Should().Be("AcmeMock");
                    h.Generator.CapturedMockFolder.Should().Be(h.MockFolder);
                    h.Generator.CapturedInstructions.Should().Be("ダークテーマで実装して");
                    vm.MockGenLog.Should().Contain("進捗: 生成中");
                    vm.IsMockGenInProgress.Should().BeFalse();
                    vm.MockGenCompleted.Should().BeTrue();
                    vm.MockGenSucceeded.Should().BeTrue();
                    vm.ShowOpenFolder.Should().BeTrue();
                }
            ),
            Row(
                "generate-existing-artifacts-confirm-overwrite",
                "既存の生成物がある出力フォルダでは上書きされるものを一覧で見せて確認し、OK なら生成へ進む",
                [(Conversation, Generate)],
                async h =>
                {
                    h.Dialogs.ConfirmResult = true;
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    var outFolder = h.PathOf("out");
                    Directory.CreateDirectory(Path.Combine(outFolder, "AcmeMock"));
                    File.WriteAllText(Path.Combine(outFolder, "AcmeMock.sln"), "old");

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Dialogs.WarningConfirmDetailsMessages.Should().ContainSingle();
                    var (message, details, _) = h.Dialogs.WarningConfirmDetailsMessages[0];
                    message.Should().Be(MockStrings.Mock_OverwriteConfirm_Message);
                    details.Should().Contain("AcmeMock.sln");
                    details.Should().Contain("AcmeMock" + Path.DirectorySeparatorChar);
                    h.Generator.GenerateCallCount.Should().Be(1);
                }
            ),
            Row(
                "generate-overwrite-cancelled-does-not-generate",
                "上書きの確認をキャンセルすると生成せず、出力フォルダも変えない",
                [(Conversation, Generate)],
                async h =>
                {
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    var outFolder = h.PathOf("out");
                    Directory.CreateDirectory(outFolder);
                    File.WriteAllText(Path.Combine(outFolder, "AcmeMock.sln"), "old");

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Dialogs.WarningConfirmDetailsMessages.Should().ContainSingle();
                    h.Generator.GenerateCallCount.Should().Be(0);
                    Directory
                        .EnumerateFileSystemEntries(outFolder)
                        .Select(Path.GetFileName)
                        .Should()
                        .Equal("AcmeMock.sln");
                    File.ReadAllText(Path.Combine(outFolder, "AcmeMock.sln")).Should().Be("old");
                    vm.IsMockGenInProgress.Should().BeFalse();
                    vm.MockGenCompleted.Should().BeFalse();
                }
            ),
            Row(
                "generate-overwrite-notice-survives-log-reset",
                "上書きの案内はログのクリアより後に出て、生成の後もログに残る",
                [(Conversation, Generate)],
                async h =>
                {
                    h.Dialogs.ConfirmResult = true;
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    Directory.CreateDirectory(h.PathOf("out", "AcmeMock"));

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    vm.MockGenLog.Should().Contain(MockStrings.Mock_OutputFolderNotEmpty.Trim());
                }
            ),
            Row(
                "generate-many-deltas-coalesce-notifications-and-flush-all",
                "差分を 1000 個流しても進捗ログの変更通知は差分ごとに出ず（反映の予約は 1 件）、完了後のログは差分の連結と一致する",
                [(Conversation, Generate)],
                async h =>
                {
                    var scheduled = new List<Action>();
                    h.ScheduleLogFlush = scheduled.Add;
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    var deltas = Enumerable.Range(0, 1000).Select(i => $"delta {i}\n").ToArray();
                    h.Generator.ProgressScript = onProgress =>
                    {
                        foreach (var delta in deltas)
                        {
                            onProgress(delta);
                        }
                    };
                    var logNotifications = 0;
                    vm.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(MockGenerationDialogViewModel.MockGenLog))
                        {
                            logNotifications++;
                        }
                    };

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    logNotifications.Should().BeLessThan(10);
                    scheduled.Should().HaveCount(1);
                    vm.MockGenLog.Should().Be(string.Concat(deltas));
                }
            ),
            Row(
                "generate-flush-during-run-reschedules-later-deltas",
                "予約した反映が実行されると途中までの全量が見え、その後の追記は新しい予約を積み、完了時に全量が揃う",
                [(Conversation, Generate)],
                async h =>
                {
                    var scheduled = new List<Action>();
                    h.ScheduleLogFlush = scheduled.Add;
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    string? logAfterFirstFlush = null;
                    h.Generator.ProgressScript = onProgress =>
                    {
                        onProgress("a");
                        onProgress("b");
                        scheduled.Single()();
                        logAfterFirstFlush = vm.MockGenLog;
                        onProgress("c");
                    };

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    logAfterFirstFlush.Should().Be("ab");
                    scheduled.Should().HaveCount(2);
                    vm.MockGenLog.Should().Be("abc");
                }
            ),
            Row(
                "generate-exception-flushes-deltas-and-error",
                "生成が例外で終わっても、それまでの差分と失敗の記録が完了時に全量反映され、実行中が解け、ダイアログは出ない",
                [(Conversation, Generate)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    h.Generator.ProgressScript = onProgress =>
                    {
                        onProgress("before failure\n");
                        throw new InvalidOperationException("boom");
                    };

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    vm.MockGenLog.Should()
                        .Be(
                            "before failure\n"
                                + string.Format(MockStrings.Mock_GenerationErrorLogFormat, "boom")
                        );
                    vm.IsMockGenInProgress.Should().BeFalse();
                    h.Dialogs.InformationMessages.Should().BeEmpty();
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "generate-failure-hides-open-folder",
                "生成の失敗ではフォルダを開くボタンを出さず完了だけが立ち、追加の指示が空なら null を渡す",
                [(Conversation, Generate)],
                async h =>
                {
                    h.Generator.ResultSuccess = false;
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    vm.MockGenCompleted.Should().BeTrue();
                    vm.MockGenSucceeded.Should().BeFalse();
                    vm.ShowOpenFolder.Should().BeFalse();
                    h.Generator.CapturedInstructions.Should().BeNull();
                }
            ),
            Row(
                "generate-success-shows-information-dialog",
                "生成の成功は出力フォルダ入りの情報ダイアログを 1 回出し、エラーは出さない",
                [(Conversation, Generate)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Dialogs.InformationMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Contain(h.PathOf("out"));
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            .. new[] { ("success", true), ("failure", false) }.Select(c =>
                Row(
                    $"generate-{c.Item1}-dialog-shown-after-progress-reset",
                    $"完了のモーダル（{c.Item1}）は実行中の印を外し完了を確定した後に出す",
                    [(Conversation, Generate)],
                    async h =>
                    {
                        h.Generator.ResultSuccess = c.Item2;
                        var vm = h.Create();
                        await h.PrepareGenerationAsync(vm);
                        bool? inProgressWhenShown = null;
                        bool? completedWhenShown = null;
                        h.Dialogs.OnBeforeShow = () =>
                        {
                            inProgressWhenShown = vm.IsMockGenInProgress;
                            completedWhenShown = vm.MockGenCompleted;
                        };

                        await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                        inProgressWhenShown.Should().NotBeNull("モーダルは実際に出ている");
                        inProgressWhenShown.Should().BeFalse();
                        completedWhenShown.Should().BeTrue();
                    }
                )
            ),
            Row(
                "generate-failure-shows-error-dialog-with-log-path",
                "生成の失敗はログのパス入りのエラーを 1 回出し、情報は出さない",
                [(Conversation, Generate)],
                async h =>
                {
                    h.Generator.ResultSuccess = false;
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Dialogs.ErrorMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Contain("quickr-mock-generation.log");
                    h.Dialogs.InformationMessages.Should().BeEmpty();
                }
            ),
            Row(
                "generate-build-declined-shows-information-not-error",
                "最終ビルドの確認を取り消して終わったときはエラーでなく、ログのパス入りの情報で案内する",
                [(Conversation, Generate)],
                async h =>
                {
                    h.Generator.ResultSuccess = false;
                    h.Generator.ResultBuildDeclined = true;
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                    h.Dialogs.InformationMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Contain("quickr-mock-generation.log");
                }
            ),
            Row(
                "generate-interrupted-shows-no-dialog",
                "利用者自身の中断で終わったときは情報もエラーも出さない",
                [(Conversation, Generate)],
                async h =>
                {
                    h.Generator.ResultSuccess = false;
                    h.Generator.ResultInterrupted = true;
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Dialogs.InformationMessages.Should().BeEmpty();
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "generate-api-key-backend-needs-key-then-allows",
                "API キー接続はキーの未入力で生成できず（理由と一発生成の注記）、キー任意のローカル LLM なら生成できる",
                [(Conversation, Backend)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    vm.CanGenerateMockProject.Should().BeTrue();

                    h.TryChangeBackend(vm, ErChatBackendKind.ApiKey);
                    vm.IsApiKeyMockGenBackend.Should().BeTrue();
                    vm.CanGenerateMockProject.Should().BeFalse();
                    vm.MockGenDisabledReason.Should()
                        .Be(MockStrings.Mock_DisabledReason_ApiKeyNotReady);

                    vm.Connection.ApiProvider = AiProvider.LocalLlm;
                    vm.CanGenerateMockProject.Should().BeTrue();
                }
            ),
            Row(
                "generate-api-key-backend-passes-backend-and-model",
                "API キー接続で生成すると backend=ApiKey とモデルを渡し、プロバイダーは渡さない",
                [(Conversation, Generate)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    vm.Connection.SelectedBackend = ErChatBackendKind.ApiKey;
                    vm.Connection.ApiProvider = AiProvider.LocalLlm;
                    vm.Connection.ApiModel = "llama3";
                    vm.CanGenerateMockProject.Should().BeTrue();

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Generator.CapturedBackend.Should().Be(ErChatBackendKind.ApiKey);
                    h.Generator.CapturedModel.Should().Be("llama3");
                    h.Generator.CapturedModelProvider.Should().BeEmpty();
                }
            ),
            Row(
                "generate-codex-ready-passes-model-and-provider",
                "Codex は認証の確認が通ると生成でき、Codex のモデルとプロバイダーを渡す",
                [(Conversation, Generate)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    vm.Connection.SelectedBackend = ErChatBackendKind.Codex;
                    vm.ApplyCodexReadiness(true, "ログイン済み", ConnectionHealth.Ready);
                    vm.Connection.CodexModelProvider = "openai";
                    vm.Connection.CodexModel = "gpt-5-codex";
                    vm.CanGenerateMockProject.Should().BeTrue();

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Generator.CapturedBackend.Should().Be(ErChatBackendKind.Codex);
                    h.Generator.CapturedModel.Should().Be("gpt-5-codex");
                    h.Generator.CapturedModelProvider.Should().Be("openai");
                }
            ),
            Row(
                "generate-codex-not-ready-shows-reason",
                "Codex は認証の確認が通っていなければ生成できず、理由が Codex の未接続になる",
                [(Conversation, Backend)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);

                    vm.Connection.SelectedBackend = ErChatBackendKind.Codex;

                    vm.CanGenerateMockProject.Should().BeFalse();
                    vm.MockGenDisabledReason.Should()
                        .Be(MockStrings.Mock_DisabledReason_CodexNotReady);
                }
            ),
            Row(
                "generate-copilot-passes-copilot-model",
                "Copilot は未接続なら Copilot 用の理由が出て、接続後は Copilot のモデルで生成し、プロバイダーは空",
                [(Conversation, Generate)],
                async h =>
                {
                    var vm = h.Create();
                    await h.SaveScreenOnClaudeCodeAsync(vm);
                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    vm.Connection.CopilotModel = "claude-sonnet-4.5";
                    vm.IsDotnetAvailable = true;
                    vm.OutputFolder = h.PathOf("out");

                    vm.CanGenerateMockProject.Should().BeFalse();
                    vm.MockGenDisabledReason.Should()
                        .Be(MockStrings.Mock_DisabledReason_CopilotNotReady);

                    vm.ApplyCopilotReadiness(
                        true,
                        "ログイン済み",
                        ConnectionHealth.Ready,
                        string.Empty,
                        []
                    );

                    vm.CanGenerateMockProject.Should().BeTrue();
                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Generator.CapturedBackend.Should().Be(ErChatBackendKind.Copilot);
                    h.Generator.CapturedModel.Should().Be("claude-sonnet-4.5");
                    h.Generator.CapturedModelProvider.Should().BeEmpty();
                }
            ),
            Row(
                "generate-targets-default-to-blazor-with-two-candidates",
                "生成ターゲットの候補は Blazor と WPF の 2 つで、既定は Blazor",
                [(Idle, Generate)],
                h =>
                {
                    var vm = h.Create();

                    vm.MockProjectTargets.Should()
                        .Equal(MockProjectTarget.Blazor, MockProjectTarget.Wpf);
                    vm.SelectedMockProjectTarget.Should().Be(MockProjectTarget.Blazor);
                }
            ),
            Row(
                "generate-passes-selected-target",
                "選んだターゲットを生成器へ渡す（既定は Blazor・選んだ後は WPF）",
                [(Conversation, Generate), (GenDone, Generate)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);
                    h.Generator.CapturedTarget.Should().Be(MockProjectTarget.Blazor);

                    vm.SelectedMockProjectTarget = MockProjectTarget.Wpf;
                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);
                    h.Generator.CapturedTarget.Should().Be(MockProjectTarget.Wpf);
                }
            ),
            Row(
                "generate-reason-is-turn-in-progress-during-turn",
                "会話のターンの実行中は第 2 ステップの無効の理由に応答の実行中を出し、理由の変化を通知する",
                [(Turn, Generate)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    vm.MockGenDisabledReason.Should().BeEmpty();
                    var notified = new List<string?>();
                    vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
                    h.Engine.HoldTurnCompletion = true;

                    vm.UserInput = "列を減らして";
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    vm.IsTurnInProgress.Should().BeTrue();
                    vm.CanGenerateMockProject.Should().BeFalse();
                    vm.MockGenDisabledReason.Should()
                        .Be(MockStrings.Mock_DisabledReason_TurnInProgress);
                    notified
                        .Should()
                        .Contain(nameof(MockGenerationDialogViewModel.MockGenDisabledReason));
                }
            ),
            Row(
                "generate-open-folder-opens-last-generated-folder",
                "フォルダを開くは、出力フォルダの欄を書き換えた後も最後に生成したフォルダを開く",
                [(GenDone, Generate)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    var generated = h.PathOf("out1");
                    var edited = h.PathOf("out2");
                    Directory.CreateDirectory(generated);
                    Directory.CreateDirectory(edited);
                    vm.OutputFolder = generated;
                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);
                    vm.ShowOpenFolder.Should().BeTrue();

                    vm.OutputFolder = edited;
                    vm.OpenOutputFolderCommand.Execute(null);

                    h.Opened.Should().Equal(generated);
                }
            ),
            Row(
                "generate-schema-mismatch-cancelled-does-not-generate",
                "設計を作った後で図のテーブルが入れ替わっていれば内訳つきで確認し、キャンセルなら生成せず出力フォルダも作らない",
                [(Conversation, Generate)],
                async h =>
                {
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    h.Diagram.Entities[0].TableName = "Product";

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Generator.GenerateCallCount.Should().Be(0);
                    var (message, details, _) = h
                        .Dialogs.WarningConfirmDetailsMessages.Should()
                        .ContainSingle()
                        .Subject;
                    message.Should().Be(MockStrings.Mock_SchemaMismatch_Message);
                    details.Should().Contain("Customer").And.Contain("Product");
                    Directory.Exists(h.PathOf("out")).Should().BeFalse();
                }
            ),
            Row(
                "generate-schema-mismatch-confirmed-generates-and-logs",
                "設計と図の食い違いを確認して続けたら生成し、続けたことと内訳をログに残す",
                [(Conversation, Generate)],
                async h =>
                {
                    h.Dialogs.ConfirmResult = true;
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    h.Diagram.Entities[0].TableName = "Product";

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Generator.GenerateCallCount.Should().Be(1);
                    vm.MockGenLog.Should().Contain(MockStrings.Mock_SchemaMismatch_ContinuedLog);
                    vm.MockGenLog.Should()
                        .Contain(
                            string.Format(
                                MockStrings.Mock_SchemaMismatch_TablesOnlyInDesignFormat,
                                "Customer"
                            )
                        );
                }
            ),
            Row(
                "generate-content-only-mismatch-does-not-confirm",
                "列や説明だけの違いでは確認を出さず、内容の違いをログに残して生成する",
                [(Conversation, Generate)],
                async h =>
                {
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    h.Diagram.Entities[0].Description = "得意先";

                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);

                    h.Dialogs.WarningConfirmDetailsMessages.Should().BeEmpty();
                    h.Generator.GenerateCallCount.Should().Be(1);
                    vm.MockGenLog.Should()
                        .Contain(MockStrings.Mock_SchemaMismatch_ContentChangedLog);
                    vm.MockGenLog.Should()
                        .Contain(
                            string.Format(
                                MockStrings.Mock_SchemaMismatch_ChangedTablesFormat,
                                "Customer"
                            )
                        );
                }
            ),
            Row(
                "generate-not-reentered-while-running",
                "生成の実行中はもう一度生成できず理由が生成中になり、進めると 1 回だけ生成して完了する",
                [(GenRun, Generate)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);

                    var run = h.StartHeldGeneration(vm);

                    vm.CanGenerateMockProject.Should().BeFalse();
                    vm.MockGenDisabledReason.Should()
                        .Be(MockStrings.Mock_DisabledReason_InProgress);
                    vm.GenerateMockProjectCommand.CanExecute(null).Should().BeFalse();
                    vm.CanEditMockGenInput.Should().BeFalse();

                    h.Generator.Release();
                    await run;

                    h.Generator.GenerateCallCount.Should().Be(1);
                    vm.IsMockGenInProgress.Should().BeFalse();
                    vm.MockGenCompleted.Should().BeTrue();
                }
            ),
            Row(
                "generate-interrupt-command-cancels-and-reaches-generator",
                "生成の中断は取り消しのトークンを取り消して生成器へ中断を渡し、中断の結果ではダイアログを出さない",
                [(GenRun, Generate)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    var run = h.StartHeldGeneration(vm);
                    vm.InterruptMockGenCommand.CanExecute(null).Should().BeTrue();

                    await vm.InterruptMockGenCommand.ExecuteAsync(null);

                    h.Generator.CapturedToken.IsCancellationRequested.Should().BeTrue();
                    h.Generator.InterruptCount.Should().Be(1);

                    h.Generator.ResultSuccess = false;
                    h.Generator.ResultInterrupted = true;
                    h.Generator.Release();
                    await run;

                    vm.IsMockGenInProgress.Should().BeFalse();
                    h.Dialogs.InformationMessages.Should().BeEmpty();
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "generate-done-reset-hides-open-folder",
                "生成の後に画面全体をクリアすると、フォルダを開くボタンと完了の表示が消え、フォルダを開けなくなる",
                [(GenDone, Reset)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    Directory.CreateDirectory(h.PathOf("out"));
                    await vm.GenerateMockProjectCommand.ExecuteAsync(null);
                    vm.ShowOpenFolder.Should().BeTrue();

                    vm.ClearCommand.Execute(null);

                    vm.ShowOpenFolder.Should().BeFalse();
                    vm.MockGenCompleted.Should().BeFalse();
                    vm.MockGenLog.Should().BeEmpty();
                    vm.OpenOutputFolderCommand.CanExecute(null).Should().BeFalse();
                    vm.OpenOutputFolderCommand.Execute(null);
                    h.Opened.Should().BeEmpty();
                }
            ),
        ];
}
