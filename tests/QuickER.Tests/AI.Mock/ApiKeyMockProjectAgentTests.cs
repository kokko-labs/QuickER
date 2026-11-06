using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Tests.AI.Mock.Agents;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="ApiKeyMockProjectAgent"/> の固定パイプライン（共通部→各画面→中間ビルド→修正 1 回）・emit_file の実書き込みと
/// パス保護・プロンプト構成（スキーマ/画面 HTML/既提出一覧の引き継ぎ）・emit なしターン・中断を、フェイクエンジン／
/// ビルド検証器で検証するテストクラス。
/// </summary>
public class ApiKeyMockProjectAgentTests
{
    private const string Project = "MockApp";

    private static string ScreenHtml(string marker) => ApiKeyMockMaterials.ScreenHtml(marker);

    /// <summary>emit_file の引数 JSON を組み立てる</summary>
    private static (string Tool, string Args) Emit(string path, string content = "<x/>") =>
        ApiKeyMockMaterials.Emit(path, content);

    /// <summary>スキャフォールド相当の素材（design/mock/ の mock.json・画面 HTML・style.css＋Generated/ の契約）を用意する</summary>
    private static string SetupScaffold(
        string work,
        params (string File, string Name, string Marker)[] screens
    ) => ApiKeyMockMaterials.Setup(work, Project, screens);

    private static string NewWork()
    {
        var work = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        return work;
    }

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static MockProjectAgentRequest Request(string work) =>
        new(
            WorkingDirectory: work,
            ProjectName: Project,
            AdditionalInstructions: null,
            Model: "gpt-4o",
            Profile: MockProjectTargetProfile.Wpf
        );

    private static MockProjectAgentRequest BlazorRequest(string work) =>
        new(
            WorkingDirectory: work,
            ProjectName: Project,
            AdditionalInstructions: null,
            Model: "gpt-4o",
            Profile: MockProjectTargetProfile.Blazor
        );

    /// <summary>
    /// フェイクエンジンを生成し、生成時に scriptedTurns をキューへ積むファクトリを組み立てる。
    /// エンジンは RunAsync 内で生成されるため、生成時点でスクリプトを仕込む（プロファイルも捕捉する）。
    /// </summary>
    private static (
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine> Factory,
        FakeChatEngine[] EngineBox,
        ErChatProfile[] ProfileBox
    ) BuildFactory(
        IReadOnlyList<IReadOnlyList<(string Tool, string Args)>> scriptedTurns,
        IReadOnlyList<ErChatTurnDiagnostics?>? diagnostics = null
    )
    {
        var engineBox = new FakeChatEngine[1];
        var profileBox = new ErChatProfile[1];

        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine> factory = (profile, toolHost) =>
        {
            profileBox[0] = profile;
            var engine = new FakeChatEngine(toolHost);

            foreach (var batch in scriptedTurns)
            {
                engine.ScriptedTurns.Enqueue(batch);
            }

            foreach (var item in diagnostics ?? [])
            {
                engine.ScriptedDiagnostics.Enqueue(item);
            }

            engineBox[0] = engine;
            return engine;
        };

        return (factory, engineBox, profileBox);
    }

    /// <summary>成功パス: 共通部 1 + 画面 1 のリクエストで emit が実書き込みされ、中間ビルド成功で Outcome 成功になることを検証する</summary>
    [Fact(DisplayName = "共通部＋画面のパイプラインで emit 実書き込み・ビルド成功で成功")]
    public async Task RunAsync_SuccessPipeline()
    {
        var work = NewWork();
        SetupScaffold(work, ("OrderList.html", "Order List", "MARKER_S1"));

        var (factory, engineBox, profileBox) = BuildFactory(
            new IReadOnlyList<(string, string)>[]
            {
                new[] { Emit("MockApp/App.xaml"), Emit("MockApp/MainWindow.xaml") },
                new[]
                {
                    Emit("MockApp/Views/OrderListView.xaml"),
                    Emit("MockApp/ViewModels/OrderListViewModel.cs"),
                },
            }
        );
        var build = new QueueBuildRunner();
        build.Results.Enqueue(true);

        var agent = new ApiKeyMockProjectAgent(factory, build);
        var progress = new List<string>();

        try
        {
            var outcome = await agent.RunAsync(
                Request(work),
                progress.Add,
                TestContext.Current.CancellationToken
            );

            outcome.Success.Should().BeTrue();
            outcome.Error.Should().BeNull();
            outcome.NotLoggedIn.Should().BeFalse();

            // リクエスト回数 = 1（共通部）＋画面数（1）
            engineBox[0].SentPrompts.Should().HaveCount(2);

            // 共通部プロンプトにスキーマ・契約要約・画面一覧が入る
            var commonPrompt = engineBox[0].SentPrompts[0];
            commonPrompt.Should().Contain("SCHEMA-TEXT: Customer");
            commonPrompt.Should().Contain("ICustomerRepository");
            commonPrompt.Should().Contain("GetByIdAsync");
            commonPrompt.Should().Contain("OrderList.html");
            commonPrompt.Should().Contain("CSS-MARKER");

            // 画面プロンプトに該当 HTML と既提出一覧が引き継がれる
            var screenPrompt = engineBox[0].SentPrompts[1];
            screenPrompt.Should().Contain("MARKER_S1");
            screenPrompt.Should().Contain("MockApp/App.xaml");

            // emit_file がファイルを実書き込みする
            File.Exists(Path.Combine(work, "MockApp", "App.xaml")).Should().BeTrue();
            File.Exists(Path.Combine(work, "MockApp", "MainWindow.xaml")).Should().BeTrue();
            File.Exists(Path.Combine(work, "MockApp", "Views", "OrderListView.xaml"))
                .Should()
                .BeTrue();

            build.BuildCallCount.Should().Be(1);

            // プロファイルは API キー用システムプロンプト・emit_file ツール
            profileBox[0].BuildSystemPrompt().Should().Contain("emit_file");
            profileBox[0].Tools.Should().ContainSingle(t => t.Name == "emit_file");

            // 生成コードの必須使用・XAML View 必須（コード組み立て禁止）の規約が入る
            profileBox[0].BuildSystemPrompt().Should().Contain("XAML view");
            profileBox[0].BuildSystemPrompt().Should().Contain("hard-coded list");

            // 主キーのアプリ側採番（GuidKey の例外込み）とパッケージソース設定の禁止が入る
            profileBox[0]
                .BuildSystemPrompt()
                .Should()
                .Contain("assign the primary key in the application");
            profileBox[0].BuildSystemPrompt().Should().Contain("GuidKey");
            profileBox[0].BuildSystemPrompt().Should().Contain("NuGet.Config");
        }
        finally
        {
            Cleanup(work);
        }
    }

    /// <summary>パス保護: Generated/ への emit はツール失敗結果になり、ファイルが書かれないことを検証する</summary>
    [Fact(DisplayName = "保護パスへの emit はツール失敗・未書き込み")]
    public async Task RunAsync_RejectsProtectedEmitPath()
    {
        var work = NewWork();
        SetupScaffold(work);

        var (factory, engineBox, _) = BuildFactory(
            new IReadOnlyList<(string, string)>[]
            {
                new[] { Emit("MockApp/Generated/Evil.cs", "public class Evil {}") },
            }
        );
        var build = new QueueBuildRunner();
        build.Results.Enqueue(true);

        var agent = new ApiKeyMockProjectAgent(factory, build);
        var progress = new List<string>();

        try
        {
            await agent.RunAsync(
                Request(work),
                progress.Add,
                TestContext.Current.CancellationToken
            );

            engineBox[0].LastToolResult!.Value.Success.Should().BeFalse();
            // 拒否理由（英語・カルチャ非依存）がツール結果として返る
            engineBox[0].LastToolResult!.Value.Result.Should().Contain("protected 'Generated/'");
            File.Exists(Path.Combine(work, "MockApp", "Generated", "Evil.cs")).Should().BeFalse();
            string.Concat(progress).Should().Contain("protected 'Generated/'");
        }
        finally
        {
            Cleanup(work);
        }
    }

    /// <summary>
    /// パス保護: 許可拡張子でない提出（MSBuild が自動 import する Directory.Build.props）はツール失敗結果になり、
    /// ファイルが書かれず、run 自体は継続して成功することを検証する。
    /// </summary>
    [Fact(DisplayName = "非許可拡張子への emit はツール失敗・未書き込み・run は継続")]
    public async Task RunAsync_RejectsDisallowedExtensionAndContinues()
    {
        var work = NewWork();
        SetupScaffold(work, ("OrderList.html", "Order list", "ORDER-LIST"));

        var (factory, engineBox, _) = BuildFactory(
            new IReadOnlyList<(string, string)>[]
            {
                new[]
                {
                    Emit("Directory.Build.props", "<Project><Target Name=\"Evil\" /></Project>"),
                    Emit("MockApp/App.xaml"),
                },
                new[] { Emit("MockApp/Views/OrderListView.xaml") },
            }
        );
        var build = new QueueBuildRunner();
        build.Results.Enqueue(true);

        var agent = new ApiKeyMockProjectAgent(factory, build);
        var progress = new List<string>();

        try
        {
            var outcome = await agent.RunAsync(
                Request(work),
                progress.Add,
                TestContext.Current.CancellationToken
            );

            // 拒否されたのは 1 件だけで、同ターンの正当な提出は書かれ、run は成功のまま続く
            File.Exists(Path.Combine(work, "Directory.Build.props")).Should().BeFalse();
            File.Exists(Path.Combine(work, "MockApp", "App.xaml")).Should().BeTrue();
            File.Exists(Path.Combine(work, "MockApp", "Views", "OrderListView.xaml"))
                .Should()
                .BeTrue();
            outcome.Success.Should().BeTrue();

            // 拒否理由（許可拡張子の列挙）が進捗ログに残る
            string.Concat(progress).Should().Contain("only .cs, .xaml files may be submitted");
            engineBox[0].LastToolResult.Should().NotBeNull();
        }
        finally
        {
            Cleanup(work);
        }
    }

    /// <summary>中間ビルド失敗→修正ターン 1 回（エラー全文含有）→再ビルド成功で Outcome 成功になることを検証する</summary>
    [Fact(DisplayName = "ビルド失敗→修正 1 回→再ビルド成功で成功")]
    public async Task RunAsync_BuildFailThenFixThenSuccess()
    {
        var work = NewWork();
        SetupScaffold(work, ("OrderList.html", "Order List", "MARKER_S1"));

        var (factory, engineBox, _) = BuildFactory(
            new IReadOnlyList<(string, string)>[]
            {
                new[] { Emit("MockApp/App.xaml") },
                new[] { Emit("MockApp/Views/OrderListView.xaml") },
                new[] { Emit("MockApp/Views/OrderListView.xaml", "<fixed/>") },
            }
        );
        var build = new QueueBuildRunner();
        build.Results.Enqueue(false); // 中間ビルド失敗
        build.Results.Enqueue(true); // 修正後は成功

        var agent = new ApiKeyMockProjectAgent(factory, build);

        try
        {
            var outcome = await agent.RunAsync(
                Request(work),
                _ => { },
                TestContext.Current.CancellationToken
            );

            outcome.Success.Should().BeTrue();
            build.BuildCallCount.Should().Be(2);

            // 修正ターンのプロンプトにビルド出力（全文）と既提出一覧が入る
            engineBox[0].SentPrompts.Should().HaveCount(3);
            var fixPrompt = engineBox[0].SentPrompts[2];
            fixPrompt.Should().Contain("BUILD_OUTPUT_CS1002");
            fixPrompt.Should().Contain("MockApp/App.xaml");
        }
        finally
        {
            Cleanup(work);
        }
    }

    /// <summary>修正ターン後も再ビルドが失敗したら Outcome 失敗（ビルド失敗理由）になることを検証する</summary>
    [Fact(DisplayName = "修正後も失敗なら Outcome 失敗")]
    public async Task RunAsync_BuildFailThenFixThenStillFails()
    {
        var work = NewWork();
        SetupScaffold(work, ("OrderList.html", "Order List", "MARKER_S1"));

        var (factory, _, _) = BuildFactory(
            new IReadOnlyList<(string, string)>[]
            {
                new[] { Emit("MockApp/App.xaml") },
                new[] { Emit("MockApp/Views/OrderListView.xaml") },
                new[] { Emit("MockApp/Views/OrderListView.xaml", "<still-broken/>") },
            }
        );
        var build = new QueueBuildRunner();
        build.Results.Enqueue(false);
        build.Results.Enqueue(false);

        var agent = new ApiKeyMockProjectAgent(factory, build);

        try
        {
            var outcome = await agent.RunAsync(
                Request(work),
                _ => { },
                TestContext.Current.CancellationToken
            );

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Be(MockStrings.Mock_ApiRun_ErrorBuildFailed);
            build.BuildCallCount.Should().Be(2);
        }
        finally
        {
            Cleanup(work);
        }
    }

    /// <summary>
    /// emit が 1 度も無い画面のターンはログ（NoEmit）に記録して残りの画面と中間ビルドを続け、
    /// 実行の最後に失敗にしてそのターンを名指しすることを検証する（やり直しはしない）
    /// </summary>
    [Fact(DisplayName = "emit なしの画面のターンは続行し、最後に失敗にして名指しする")]
    public async Task RunAsync_EmitlessTurn_ContinuesThenFails()
    {
        var work = NewWork();
        SetupScaffold(
            work,
            ("OrderList.html", "Order List", "MARKER_S1"),
            ("OrderEdit.html", "Order Edit", "MARKER_S2")
        );

        // 共通部と 2 つ目の画面だけ emit・1 つ目の画面は emit なし
        var (factory, engineBox, _) = BuildFactory(
            new IReadOnlyList<(string, string)>[]
            {
                new[] { Emit("MockApp/App.xaml") },
                Array.Empty<(string, string)>(),
                new[] { Emit("MockApp/Views/OrderEditView.xaml") },
            }
        );
        var build = new QueueBuildRunner();
        build.Results.Enqueue(true);

        var agent = new ApiKeyMockProjectAgent(factory, build);
        var progress = new List<string>();

        try
        {
            var outcome = await agent.RunAsync(
                Request(work),
                progress.Add,
                TestContext.Current.CancellationToken
            );

            outcome.Success.Should().BeFalse();
            outcome
                .Error.Should()
                .Be(
                    string.Format(
                        MockStrings.Mock_ApiRun_ErrorTurnsWithoutFilesFormat,
                        string.Format(MockStrings.Mock_ApiRun_ScreenTurnLabelFormat, "Order List")
                    )
                );

            // 残りの画面と中間ビルドは続ける（やり直しのターンは無い）
            engineBox[0].SentPrompts.Should().HaveCount(3);
            build.BuildCallCount.Should().Be(1);
            string.Concat(progress).Should().Contain(MockStrings.Mock_ApiRun_NoEmit);
        }
        finally
        {
            Cleanup(work);
        }
    }

    /// <summary>
    /// ターンごとに、番号・種類（共通部・画面・修正）・応答の回数・終了理由・出力のトークン数・捨てたブロックの種類を
    /// 1 行ログに残し、分からない値は「不明」、診断の無いターンは行を出さないことを検証する。
    /// 診断の行にプロンプトの本文（スキーマ・CSS・画面の HTML の目印）と接続情報が入らないことも確かめる。
    /// </summary>
    [Fact(
        DisplayName = "ターンごとの診断の行は終了理由とトークン数だけを出し、本文と接続情報を出さない"
    )]
    public async Task RunAsync_LogsTurnDiagnostics()
    {
        var work = NewWork();
        SetupScaffold(
            work,
            ("OrderList.html", "Order List", "MARKER_S1"),
            ("OrderEdit.html", "Order Edit", "MARKER_S2")
        );

        var (factory, _, _) = BuildFactory(
            new IReadOnlyList<(string, string)>[]
            {
                new[] { Emit("MockApp/App.xaml") },
                new[] { Emit("MockApp/Views/OrderListView.xaml") },
                new[] { Emit("MockApp/Views/OrderEditView.xaml") },
                new[] { Emit("MockApp/Views/OrderListView.xaml", "<fixed/>") },
            },
            new ErChatTurnDiagnostics?[]
            {
                new(1, "end_turn", 100, [], 64_000),
                new(2, null, null, []),
                null,
                new(1, "max_tokens", 8192, ["thinking", "redacted_thinking"], 8_192),
            }
        );
        var build = new QueueBuildRunner();
        build.Results.Enqueue(false);
        build.Results.Enqueue(true);
        var progress = new List<string>();
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(
            "en"
        );

        try
        {
            var outcome = await new ApiKeyMockProjectAgent(factory, build).RunAsync(
                Request(work),
                progress.Add,
                TestContext.Current.CancellationToken
            );

            outcome.Success.Should().BeTrue();
            var lines = string.Concat(progress)
                .Split('\n')
                .Where(line => line.StartsWith("Turn ", StringComparison.Ordinal))
                .ToList();

            lines
                .Should()
                .Equal(
                    "Turn 1 (shared parts): 1 response(s), stop reason end_turn, 100 output tokens (limit 64000), omitted blocks: none",
                    "Turn 2 (screen): 2 response(s), stop reason unknown, unknown output tokens (limit unknown), omitted blocks: none",
                    "Turn 4 (fix): 1 response(s), stop reason max_tokens, 8192 output tokens (limit 8192), omitted blocks: thinking, redacted_thinking"
                );

            foreach (
                var marker in new[]
                {
                    "SCHEMA-TEXT",
                    "CSS-MARKER",
                    "MARKER_S1",
                    "MARKER_S2",
                    "gpt-4o",
                    "了解",
                }
            )
            {
                lines.Should().NotContain(line => line.Contains(marker, StringComparison.Ordinal));
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
            Cleanup(work);
        }
    }

    /// <summary>
    /// 内容が空の提出（出力の上限で切れたツール呼び出しはこの形で届く）は、AI へ失敗を返すだけでなくログにも残すことを検証する
    /// </summary>
    [Fact(DisplayName = "内容が空の提出の拒否はログに残す")]
    public async Task RunAsync_EmptyEmit_IsLogged()
    {
        var work = NewWork();
        SetupScaffold(work, ("OrderList.html", "Order List", "MARKER_S1"));

        var (factory, engineBox, _) = BuildFactory(
            new IReadOnlyList<(string, string)>[]
            {
                new[] { Emit("MockApp/App.xaml", string.Empty) },
                new[] { Emit("MockApp/Views/OrderListView.xaml") },
            }
        );
        var build = new QueueBuildRunner();
        build.Results.Enqueue(true);
        var progress = new List<string>();

        try
        {
            await new ApiKeyMockProjectAgent(factory, build).RunAsync(
                Request(work),
                progress.Add,
                TestContext.Current.CancellationToken
            );

            engineBox[0].LastToolResult.Should().NotBeNull();
            string.Concat(progress)
                .Should()
                .Contain(
                    string.Format(
                        MockStrings.Mock_ApiRun_EmitRejectedFormat,
                        "content is empty. Submit the complete file content."
                    )
                );
            File.Exists(Path.Combine(work, "MockApp", "App.xaml")).Should().BeFalse();
        }
        finally
        {
            Cleanup(work);
        }
    }

    /// <summary>
    /// AI の応答の断片が改行で終わらなくても、次の「[2/…」の行と診断の行は新しい行から始まることを検証する
    /// （フェイクのエンジンは断片「了解しました。」を改行なしで流す）
    /// </summary>
    [Fact(DisplayName = "改行で終わらない応答の後の行は新しい行から始まる")]
    public async Task RunAsync_LinesAfterUnterminatedDelta_StartOnNewLine()
    {
        var work = NewWork();
        SetupScaffold(work, ("OrderList.html", "Order List", "MARKER_S1"));

        var (factory, _, _) = BuildFactory(
            new IReadOnlyList<(string, string)>[]
            {
                new[] { Emit("MockApp/App.xaml") },
                new[] { Emit("MockApp/Views/OrderListView.xaml") },
            },
            new ErChatTurnDiagnostics?[] { new(1, "end_turn", 10, []), null }
        );
        var build = new QueueBuildRunner();
        build.Results.Enqueue(true);
        var progress = new List<string>();

        try
        {
            await new ApiKeyMockProjectAgent(factory, build).RunAsync(
                Request(work),
                progress.Add,
                TestContext.Current.CancellationToken
            );

            var log = string.Concat(progress);

            // 「了解しました。」の直後に続く文字は改行だけ（行が AI の文章の末尾へ続かない）
            var index = 0;

            while ((index = log.IndexOf("了解しました。", index, StringComparison.Ordinal)) >= 0)
            {
                index += "了解しました。".Length;
                log[index].Should().Be('\n');
            }

            // 診断の行と「[2/2]」の行は、どちらも行の先頭から始まる
            log.Should().Contain("了解しました。\n").And.Contain("\n[2/2]");
        }
        finally
        {
            Cleanup(work);
        }
    }

    /// <summary>修正のターンでファイルを出さなくても、再ビルドが通れば失敗にしないことを検証する（修正のターンは数えない）</summary>
    [Fact(DisplayName = "修正のターンで emit が無くても、再ビルドが通れば成功")]
    public async Task RunAsync_EmitlessFixTurn_DoesNotFail()
    {
        var work = NewWork();
        SetupScaffold(work, ("OrderList.html", "Order List", "MARKER_S1"));

        var (factory, _, _) = BuildFactory(
            new IReadOnlyList<(string, string)>[]
            {
                new[] { Emit("MockApp/App.xaml") },
                new[] { Emit("MockApp/Views/OrderListView.xaml") },
                Array.Empty<(string, string)>(),
            }
        );
        var build = new QueueBuildRunner();
        build.Results.Enqueue(false);
        build.Results.Enqueue(true);

        try
        {
            var outcome = await new ApiKeyMockProjectAgent(factory, build).RunAsync(
                Request(work),
                _ => { },
                TestContext.Current.CancellationToken
            );

            outcome.Success.Should().BeTrue();
        }
        finally
        {
            Cleanup(work);
        }
    }

    /// <summary>共通部のターンでファイルを出さなかったときも、最後に失敗にして名指しすることを検証する</summary>
    [Fact(DisplayName = "共通部のターンで emit が無ければ最後に失敗にして名指しする")]
    public async Task RunAsync_EmitlessCommonTurn_Fails()
    {
        var work = NewWork();
        SetupScaffold(work, ("OrderList.html", "Order List", "MARKER_S1"));

        var (factory, _, _) = BuildFactory(
            new IReadOnlyList<(string, string)>[]
            {
                Array.Empty<(string, string)>(),
                new[] { Emit("MockApp/Views/OrderListView.xaml") },
            }
        );
        var build = new QueueBuildRunner();
        build.Results.Enqueue(true);

        try
        {
            var outcome = await new ApiKeyMockProjectAgent(factory, build).RunAsync(
                Request(work),
                _ => { },
                TestContext.Current.CancellationToken
            );

            outcome.Success.Should().BeFalse();
            outcome
                .Error.Should()
                .Be(
                    string.Format(
                        MockStrings.Mock_ApiRun_ErrorTurnsWithoutFilesFormat,
                        MockStrings.Mock_ApiRun_TurnKindCommon
                    )
                );
        }
        finally
        {
            Cleanup(work);
        }
    }
}
