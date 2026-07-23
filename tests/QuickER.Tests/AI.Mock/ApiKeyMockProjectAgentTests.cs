using System.IO;
using System.Text.Json;
using FluentAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
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

    /// <summary>スクリプト化したビルド結果（成否）をキューで返すフェイクビルド検証器</summary>
    private sealed class QueueBuildRunner : IBuildRunner
    {
        public Queue<bool> Results { get; } = new();
        public string Output { get; set; } = "BUILD_OUTPUT_CS1002";
        public int BuildCallCount { get; private set; }

        public Task<BuildRunResult> BuildAsync(
            string workingDirectory,
            CancellationToken cancellationToken = default
        )
        {
            BuildCallCount++;
            var success = Results.Count > 0 ? Results.Dequeue() : true;
            return Task.FromResult(new BuildRunResult(success, Output));
        }

        public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private static string ScreenHtml(string marker) =>
        "<!DOCTYPE html><html><head><link rel=\"stylesheet\" href=\"style.css\"></head>"
        + $"<body><h1>{marker}</h1></body></html>";

    /// <summary>emit_file の引数 JSON を組み立てる</summary>
    private static (string Tool, string Args) Emit(string path, string content = "<x/>") =>
        (MockProjectEmitTools.EmitFileToolName, JsonSerializer.Serialize(new { path, content }));

    /// <summary>スキャフォールド相当の素材（design/mock/ の mock.json・画面 HTML・style.css＋Generated/ の契約）を用意する</summary>
    private static string SetupScaffold(
        string work,
        params (string File, string Name, string Marker)[] screens
    )
    {
        var projectDir = Path.Combine(work, Project);
        var designDir = Path.Combine(projectDir, "design", "mock");
        Directory.CreateDirectory(designDir);

        var store = MockFolderStore.CreateNew(
            designDir,
            Project,
            sourceSchema: "SCHEMA-TEXT: Customer(Id, Name)"
        );

        foreach (var (file, name, marker) in screens)
        {
            store.SaveScreen(
                file,
                name,
                $"role of {name}",
                ScreenHtml(marker),
                Array.Empty<MockTransition>(),
                "init"
            );
        }

        store.SaveStylesheet("body { color: #111; } /* CSS-MARKER */", "css");

        // データ層（契約要約の抽出対象）
        var generatedDir = Path.Combine(projectDir, "Generated");
        Directory.CreateDirectory(generatedDir);
        File.WriteAllText(
            Path.Combine(generatedDir, "Repositories.cs"),
            "namespace Gen;\n"
                + "public interface ICustomerRepository\n{\n"
                + "    Task<Customer> GetByIdAsync(int id);\n"
                + "    Task<IReadOnlyList<Customer>> GetAllAsync();\n}\n"
                + "public sealed class Customer { public int Id { get; set; } }\n"
        );

        return work;
    }

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
            Model: "gpt-4o"
        );

    /// <summary>
    /// フェイクエンジンを生成し、生成時に scriptedTurns をキューへ積むファクトリを組み立てる。
    /// エンジンは RunAsync 内で生成されるため、生成時点でスクリプトを仕込む（プロファイルも捕捉する）。
    /// </summary>
    private static (
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine> Factory,
        FakeChatEngine[] EngineBox,
        ErChatProfile[] ProfileBox
    ) BuildFactory(IReadOnlyList<IReadOnlyList<(string Tool, string Args)>> scriptedTurns)
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
            profileBox[0].BuildSystemPrompt().Should().Contain("XAML の View");
            profileBox[0]
                .BuildSystemPrompt()
                .Should()
                .Contain("ハードコードされたリストで代用してはいけません");

            // 主キーのアプリ側採番（GuidKey の例外込み）とパッケージソース設定の禁止が入る
            profileBox[0].BuildSystemPrompt().Should().Contain("アプリ側で採番");
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
            engineBox[0].LastToolResult!.Value.Result.Should().Contain("scaffold-owned");
            File.Exists(Path.Combine(work, "MockApp", "Generated", "Evil.cs")).Should().BeFalse();
            string.Concat(progress).Should().Contain("scaffold-owned");
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

    /// <summary>emit が 1 度も無いターンはログ（NoEmit）に記録され、次へ進むことを検証する（最終判定はビルド）</summary>
    [Fact(DisplayName = "emit なしターンはログして続行")]
    public async Task RunAsync_EmitlessTurn_LogsAndContinues()
    {
        var work = NewWork();
        SetupScaffold(work, ("OrderList.html", "Order List", "MARKER_S1"));

        // 共通部だけ emit・画面ターンは batch 無し（emit なし）
        var (factory, engineBox, _) = BuildFactory(
            new IReadOnlyList<(string, string)>[] { new[] { Emit("MockApp/App.xaml") } }
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
            engineBox[0].SentPrompts.Should().HaveCount(2);
            string.Concat(progress).Should().Contain(MockStrings.Mock_ApiRun_NoEmit);
        }
        finally
        {
            Cleanup(work);
        }
    }

    /// <summary>キャンセルされたトークンでは OCE を伝播することを検証する</summary>
    [Fact(DisplayName = "キャンセルで OCE を伝播")]
    public async Task RunAsync_Cancellation_Throws()
    {
        var work = NewWork();
        SetupScaffold(work, ("OrderList.html", "Order List", "MARKER_S1"));

        var (factory, _, _) = BuildFactory(
            new IReadOnlyList<(string, string)>[] { new[] { Emit("MockApp/App.xaml") } }
        );
        var build = new QueueBuildRunner();
        build.Results.Enqueue(true);

        var agent = new ApiKeyMockProjectAgent(factory, build);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await agent.RunAsync(Request(work), _ => { }, cts.Token)
            );
        }
        finally
        {
            Cleanup(work);
        }
    }

    /// <summary>可用性は常に true・実行前の中断は無害（例外なし）であることを検証する</summary>
    [Fact(DisplayName = "IsAvailable=true・実行前中断は無害")]
    public async Task Availability_AndInterruptBeforeRun()
    {
        var (factory, _, _) = BuildFactory(Array.Empty<IReadOnlyList<(string, string)>>());
        var agent = new ApiKeyMockProjectAgent(factory, new QueueBuildRunner());

        agent.IsAvailable().Should().BeTrue();
        await agent.InterruptAsync();
    }
}
