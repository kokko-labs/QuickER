using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.AI.Mock.Agents;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Scaffold;

/// <summary>
/// モックプロジェクトのスキャフォールドの表（<see cref="MockProjectScaffoldService"/>）：構成物・方言 × ターゲット・名前空間の案内
/// （このファイル）と、プロジェクト名の入口・作り直し（MockScaffoldRows.Regeneration.cs）
/// </summary>
/// <remarks>
/// 行はターゲット × 方言 × スキャフォールドの前の出力フォルダの状態を宣言し、本体はその構成でスキャフォールドして表明する。
/// 名前空間の期待値は、書き出したファイルの本文の namespace 宣言から読む（製品の同じ関数を通さない）。
/// </remarks>
internal static partial class MockScaffoldRows
{
    private const string SqlServer = SqlServerProvider.ProviderName;

    private const string Sqlite = SqliteProvider.ProviderName;

    private const string PostgreSql = "postgresql";

    private static MockAgentTarget Wpf => MockAgentTarget.Wpf;

    private static MockAgentTarget Blazor => MockAgentTarget.Blazor;

    /// <summary>行の表</summary>
    public static ScenarioTable<MockScaffoldScenario> Table =>
        new(
            EngineIds.Ai,
            [
                .. Skeleton(),
                .. Dialects(),
                .. ReadmeNamespaces(),
                .. ApiKeyPromptNamespaces(),
                .. ProjectNames(),
                .. Regeneration(),
            ]
        );

    /// <summary>構成物（VS 標準の配置・sln の構文・プロジェクトの GUID）</summary>
    private static IEnumerable<ScenarioRow<MockScaffoldScenario>> Skeleton() =>
        [
            Row(
                "skeleton-writes-solution-project-readme-design-and-generated",
                "スキャフォールドは sln・csproj・README・design/mock/・Generated/ の土台一式を VS 標準の配置（sln は出力フォルダ直下・残りはプロジェクトフォルダ配下）で書き出す",
                Wpf,
                SqlServer,
                ScaffoldExisting.None,
                (c, s) =>
                {
                    var folder = c.NewPath("out");
                    var result = c.Scaffold(s, folder, c.SeedJapaneseMock(), withNameColumn: true);

                    var projectDirectory = Path.Combine(folder, MockScaffoldContext.ProjectName);
                    result.ProjectDirectory.Should().Be(projectDirectory);
                    result.SolutionFilePath.Should().Be(Path.Combine(folder, "AcmeMock.sln"));
                    File.Exists(result.SolutionFilePath).Should().BeTrue();
                    result.ProjectFilePath.Should().StartWith(projectDirectory);
                    result.ReadmePath.Should().StartWith(projectDirectory);
                    result.DesignFolderPath.Should().StartWith(projectDirectory);
                    result.GeneratedDirectory.Should().StartWith(projectDirectory);

                    // csproj は WPF・net10.0-windows・MVVM 依存を持つ
                    var csproj = File.ReadAllText(result.ProjectFilePath);
                    csproj.Should().Contain("<UseWPF>true</UseWPF>");
                    csproj.Should().Contain("net10.0-windows");
                    csproj.Should().Contain("CommunityToolkit.Mvvm");
                    csproj.Should().Contain("Microsoft.Extensions.DependencyInjection");

                    // README はデータ層読み取り専用・InMemory の DI 登録・実 DB への切替と、design/mock/ の複数画面前提を案内する
                    var readme = File.ReadAllText(result.ReadmePath);
                    readme.Should().Contain("Generated/");
                    readme.Should().Contain("AddGeneratedInMemoryRepositories");
                    readme.Should().Contain("I{Entity}Repository");
                    readme.Should().Contain("design/mock/mock.json");
                    readme.Should().Contain("style.css");

                    // design/mock/ にモックフォルダの内容（mock.json・複数 HTML・style.css）がフラットに同梱される
                    File.Exists(
                            Path.Combine(result.DesignFolderPath, MockManifest.ManifestFileName)
                        )
                        .Should()
                        .BeTrue();
                    File.Exists(
                            Path.Combine(result.DesignFolderPath, MockManifest.StylesheetFileName)
                        )
                        .Should()
                        .BeTrue();
                    File.Exists(Path.Combine(result.DesignFolderPath, "CustomerEdit.html"))
                        .Should()
                        .BeTrue();
                    File.ReadAllText(Path.Combine(result.DesignFolderPath, "CustomerList.html"))
                        .Should()
                        .Be(MockScaffoldContext.JapaneseScreenHtml);

                    // Generated/ 配下にデータ層のコードが分割して出力され、InMemory の登録と Entity が含まれる
                    Directory
                        .GetFiles(result.GeneratedDirectory, "*.g.cs", SearchOption.AllDirectories)
                        .Should()
                        .NotBeEmpty();
                    var allGenerated = MockScaffoldContext.ReadAllGenerated(
                        result.GeneratedDirectory
                    );
                    allGenerated.Should().Contain("AddGeneratedInMemoryRepositories");
                    allGenerated.Should().Contain("CustomerEntity");
                }
            ),
            Row(
                "solution-has-valid-syntax-and-project-reference",
                "生成した .sln は VS 標準の構文（Format Version 12.00・Project/EndProject・構成のセクション・CRLF）とプロジェクトフォルダ配下の csproj への参照を持つ",
                Wpf,
                SqlServer,
                ScaffoldExisting.None,
                (c, s) =>
                {
                    var result = c.Scaffold(
                        s,
                        c.NewPath("out"),
                        c.SeedJapaneseMock(),
                        withNameColumn: true
                    );
                    var sln = File.ReadAllText(result.SolutionFilePath);

                    sln.Should()
                        .Contain("Microsoft Visual Studio Solution File, Format Version 12.00");
                    sln.Should()
                        .Contain(
                            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"AcmeMock\""
                        );
                    sln.Should().Contain(@"AcmeMock\AcmeMock.csproj");
                    sln.Should().Contain("EndProject");
                    sln.Should()
                        .Contain("GlobalSection(SolutionConfigurationPlatforms) = preSolution");
                    sln.Should().Contain("Debug|Any CPU = Debug|Any CPU");
                    sln.Should().Contain("Release|Any CPU = Release|Any CPU");
                    sln.Should()
                        .Contain("GlobalSection(ProjectConfigurationPlatforms) = postSolution");
                    sln.Should().Contain(".Debug|Any CPU.ActiveCfg = Debug|Any CPU");
                    sln.Should().Contain(".Release|Any CPU.Build.0 = Release|Any CPU");
                    sln.Should().Contain("EndGlobal");
                    sln.Should().Contain("\r\n");
                }
            ),
            Row(
                "solution-project-guid-is-deterministic-from-name",
                "sln のプロジェクトの GUID はプロジェクト名から決定的に導く（同じ名前なら同じ GUID・別の名前なら別の GUID）",
                Wpf,
                SqlServer,
                ScaffoldExisting.None,
                (c, s) =>
                {
                    var mock = c.SeedJapaneseMock();
                    var diagram = MockScaffoldContext.Diagram(s.Dialect, withNameColumn: true);
                    var same1 = c.Service.Scaffold(
                        diagram,
                        c.NewPath("out1"),
                        "AcmeMock",
                        mock,
                        s.Target.Profile
                    );
                    var same2 = c.Service.Scaffold(
                        MockScaffoldContext.Diagram(s.Dialect, withNameColumn: true),
                        c.NewPath("out2"),
                        "AcmeMock",
                        mock,
                        s.Target.Profile
                    );
                    var other = c.Service.Scaffold(
                        MockScaffoldContext.Diagram(s.Dialect, withNameColumn: true),
                        c.NewPath("out3"),
                        "OtherMock",
                        mock,
                        s.Target.Profile
                    );

                    var guid1 = ExtractProjectGuid(File.ReadAllText(same1.SolutionFilePath));
                    guid1.Should().Be(ExtractProjectGuid(File.ReadAllText(same2.SolutionFilePath)));
                    guid1
                        .Should()
                        .NotBe(ExtractProjectGuid(File.ReadAllText(other.SolutionFilePath)));
                }
            ),
        ];

    /// <summary>方言 × ターゲット（QuickER 版 Repository と ADO の依存の出し分け・Blazor の csproj と README）</summary>
    private static IEnumerable<ScenarioRow<MockScaffoldScenario>> Dialects() =>
        [
            Row(
                "dialect-sqlserver-emits-repository-and-sqlclient-wpf",
                "SQL Server の方言の図では QuickER 版の SQL Server の Repository（DI は AddGeneratedSqlServerRepositories）と SqlClient の依存を出す",
                Wpf,
                SqlServer,
                ScaffoldExisting.None,
                (c, s) => SqlServerEmitsRepositoryAndSqlClient(c, s)
            ),
            Row(
                "dialect-sqlserver-emits-repository-and-sqlclient-blazor",
                "Blazor の SQL Server の方言でも、QuickER 版 Repository と ADO の依存の出し分けは WPF と同じく効く",
                Blazor,
                SqlServer,
                ScaffoldExisting.None,
                (c, s) => SqlServerEmitsRepositoryAndSqlClient(c, s)
            ),
            Row(
                "dialect-sqlite-emits-repository-and-sqlite-wpf",
                "SQLite の方言の図では QuickER 版の SQLite の Repository と Sqlite の依存を出す",
                Wpf,
                Sqlite,
                ScaffoldExisting.None,
                (c, s) =>
                {
                    var result = c.Scaffold(
                        s,
                        c.NewPath("out"),
                        c.SeedJapaneseMock(),
                        withNameColumn: true
                    );

                    result.RepositoryDialect.Should().Be("sqlite");
                    File.ReadAllText(result.ProjectFilePath)
                        .Should()
                        .Contain("Microsoft.Data.Sqlite");
                }
            ),
            Row(
                "dialect-sqlite-blazor-writes-web-csproj-and-readme",
                "Blazor は Blazor Web App の csproj（Microsoft.NET.Sdk.Web・net10.0・WPF の依存なし）と Blazor の規約の README を出す",
                Blazor,
                Sqlite,
                ScaffoldExisting.None,
                (c, s) =>
                {
                    var result = c.Scaffold(
                        s,
                        c.NewPath("out"),
                        c.SeedJapaneseMock(),
                        withNameColumn: true
                    );

                    var csproj = File.ReadAllText(result.ProjectFilePath);
                    csproj.Should().Contain("Microsoft.NET.Sdk.Web");
                    csproj.Should().Contain("<TargetFramework>net10.0</TargetFramework>");
                    csproj.Should().NotContain("UseWPF");
                    csproj.Should().NotContain("CommunityToolkit.Mvvm");
                    csproj.Should().NotContain("Microsoft.Extensions.DependencyInjection");
                    csproj.Should().Contain("Microsoft.Data.Sqlite");

                    // README は Blazor の実装規約（@page／InteractiveServer／wwwroot/style.css／dotnet run）を案内する
                    var readme = File.ReadAllText(result.ReadmePath);
                    readme.Should().Contain("Blazor Web App");
                    readme.Should().Contain("@page");
                    readme.Should().Contain("InteractiveServer");
                    readme.Should().Contain("wwwroot/style.css");
                    readme.Should().Contain("dotnet run");
                    readme.Should().Contain("AddGeneratedInMemoryRepositories");
                }
            ),
            Row(
                "dialect-unsupported-omits-repository-wpf",
                "QuickER 版 Repository に対応しない方言（PostgreSQL）の図では Repository も ADO の依存も出さず、InMemory は出す",
                Wpf,
                PostgreSql,
                ScaffoldExisting.None,
                (c, s) =>
                {
                    var result = c.Scaffold(
                        s,
                        c.NewPath("out"),
                        c.SeedJapaneseMock(),
                        withNameColumn: true
                    );

                    result.RepositoryDialect.Should().BeNull();
                    var csproj = File.ReadAllText(result.ProjectFilePath);
                    csproj.Should().NotContain("Microsoft.Data.SqlClient");
                    csproj.Should().NotContain("Microsoft.Data.Sqlite");
                    MockScaffoldContext
                        .ReadAllGenerated(result.GeneratedDirectory)
                        .Should()
                        .Contain("AddGeneratedInMemoryRepositories");
                }
            ),
            Row(
                "dialect-unsupported-blazor-omits-item-group",
                "Blazor で対応しない方言なら、ADO のパッケージも空の ItemGroup 要素も出さない",
                Blazor,
                PostgreSql,
                ScaffoldExisting.None,
                (c, s) =>
                {
                    var result = c.Scaffold(
                        s,
                        c.NewPath("out"),
                        c.SeedJapaneseMock(),
                        withNameColumn: true
                    );

                    result.RepositoryDialect.Should().BeNull();
                    var csproj = File.ReadAllText(result.ProjectFilePath);
                    csproj.Should().Contain("Microsoft.NET.Sdk.Web");
                    csproj.Should().NotContain("Microsoft.Data.SqlClient");
                    csproj.Should().NotContain("Microsoft.Data.Sqlite");
                    csproj.Should().NotContain("<ItemGroup>");
                }
            ),
            Row(
                "generated-is-identical-across-targets",
                "Generated/（データ層のコード）はターゲットに依らず同一（WPF と Blazor で相対パス → 内容が一致する）",
                Wpf,
                Sqlite,
                ScaffoldExisting.None,
                (c, _) =>
                {
                    var mock = c.SeedJapaneseMock();
                    var wpf = c.Service.Scaffold(
                        MockScaffoldContext.Diagram(Sqlite, withNameColumn: true),
                        c.NewPath("wpf"),
                        MockScaffoldContext.ProjectName,
                        mock,
                        MockProjectTargetProfile.Wpf
                    );
                    var blazor = c.Service.Scaffold(
                        MockScaffoldContext.Diagram(Sqlite, withNameColumn: true),
                        c.NewPath("blazor"),
                        MockScaffoldContext.ProjectName,
                        mock,
                        MockProjectTargetProfile.Blazor
                    );

                    ReadGeneratedRelative(blazor.GeneratedDirectory)
                        .Should()
                        .BeEquivalentTo(ReadGeneratedRelative(wpf.GeneratedDirectory));
                }
            ),
        ];

    /// <summary>README の名前空間の案内（ターゲット × 方言の全組）</summary>
    private static IEnumerable<ScenarioRow<MockScaffoldScenario>> ReadmeNamespaces() =>
        from target in MockAgentTarget.All
        from dialect in new (string Dialect, string? File)[]
        {
            (SqlServer, "Repositories.SqlServer.g.cs"),
            (Sqlite, "Repositories.Sqlite.g.cs"),
            (PostgreSql, null),
        }
        select Row(
            $"readme-namespaces-match-generated-files-{target.Name}-{dialect.Dialect}",
            "README の DI の節・実 DB への切替・名前空間の節が書き出したファイルの実際の名前空間を案内し、型を持たない {ルート}.Generated を登録の名前空間として案内しない",
            target,
            dialect.Dialect,
            ScaffoldExisting.None,
            (c, s) => ReadmeNamespacesMatchGeneratedFiles(c, s, dialect.File)
        );

    /// <summary>API キーの実行器が実際のスキャフォールドの上で組む共通部のリクエストの名前空間の案内（ターゲットごと）</summary>
    private static IEnumerable<ScenarioRow<MockScaffoldScenario>> ApiKeyPromptNamespaces() =>
        MockAgentTarget.All.Select(target =>
            Row(
                $"api-key-prompt-names-generated-namespaces-{target.Name}",
                "実際のスキャフォールドの上で API キーの実行器を走らせると、共通部のリクエストに書き出したファイルの実際の名前空間と登録メソッドの所在が入り、システムプロンプトにターゲットの流儀の using の規約が入る",
                target,
                SqlServer,
                ScaffoldExisting.None,
                ApiKeyPromptNamesGeneratedNamespacesAsync
            )
        );

    private static void SqlServerEmitsRepositoryAndSqlClient(
        MockScaffoldContext c,
        MockScaffoldScenario s
    )
    {
        var result = c.Scaffold(s, c.NewPath("out"), c.SeedJapaneseMock(), withNameColumn: true);

        result.RepositoryDialect.Should().Be("sqlserver");
        File.ReadAllText(result.ProjectFilePath).Should().Contain("Microsoft.Data.SqlClient");
        // DI 登録はエンジン別（AddGenerated{方言}Repositories）で統一。sqlserver の生成では方言別の名前が出る
        MockScaffoldContext
            .ReadAllGenerated(result.GeneratedDirectory)
            .Should()
            .Contain("AddGeneratedSqlServerRepositories");
    }

    private static void ReadmeNamespacesMatchGeneratedFiles(
        MockScaffoldContext c,
        MockScaffoldScenario s,
        string? dialectFile
    )
    {
        var result = c.Scaffold(s, c.NewPath("out"), c.SeedBareMock());

        var readme = File.ReadAllText(result.ReadmePath).ReplaceLineEndings("\n");
        var generated = result.GeneratedDirectory;
        var declared = GeneratedNamespaceDeclarations.Read(generated);
        var inMemory = GeneratedNamespaceDeclarations.OfFileContaining(
            generated,
            "IServiceCollection AddGeneratedInMemoryRepositories("
        );
        var entities = GeneratedNamespaceDeclarations.OfFileContaining(
            generated,
            "class CustomerEntity"
        );
        var contracts = GeneratedNamespaceDeclarations.OfFileContaining(
            generated,
            "interface ICustomerRepository"
        );

        // データ層は {ルート名前空間}.Generated の下に出る（MockProjectScaffoldService.BuildOptions の XmlDoc）。
        // README・プロンプトは実際の名前空間を読むので、ここで置き場所そのものを見ないと綴りの変化に気づけない
        declared
            .Values.Should()
            .OnlyContain(ns =>
                ns == $"{MockScaffoldContext.ProjectName}.Generated"
                || ns.StartsWith(
                    $"{MockScaffoldContext.ProjectName}.Generated.",
                    StringComparison.Ordinal
                )
            );

        readme.Should().Contain($"using {inMemory};");
        readme.Should().Contain($"is declared in the\n`{inMemory}` namespace.");

        if (s.Target == Blazor)
        {
            readme.Should().Contain($"@using {entities}\n@using {contracts}");
        }
        else
        {
            readme.Should().Contain($"`using {entities};`").And.Contain($"`using {contracts};`");
        }

        if (dialectFile is not null)
        {
            var database = declared[dialectFile];
            readme.Should().Contain($"`{database}` namespace (add `using {database};`)");
        }

        // 名前空間の節は書き出した全ファイルを、そのファイルの実際の名前空間で並べる
        foreach (var (file, declaredNamespace) in declared)
        {
            readme.Should().Contain($"- `Generated/{file}`: `{declaredNamespace}`");
        }

        // 型を持たない親の名前空間を、登録の名前空間として案内しない
        readme.Should().NotContain("from `AcmeMock.Generated`");
        readme.Should().NotContain("using AcmeMock.Generated;");
    }

    private static async Task ApiKeyPromptNamesGeneratedNamespacesAsync(
        MockScaffoldContext c,
        MockScaffoldScenario s
    )
    {
        const string project = "MockApp";
        var work = c.NewFolder("work");
        var mock = c.NewFolder("mock");
        var store = MockFolderStore.CreateNew(mock, project, "# schema");
        store.SaveScreen(
            "CustomerList.html",
            "Customer List",
            "role",
            ApiKeyMockMaterials.ScreenHtml("MARKER_S1"),
            Array.Empty<MockTransition>(),
            "init"
        );
        var scaffold = c.Service.Scaffold(
            MockScaffoldContext.Diagram(s.Dialect),
            work,
            project,
            mock,
            s.Target.Profile
        );

        FakeChatEngine? engine = null;
        ErChatProfile? profile = null;
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine> factory = (p, toolHost) =>
        {
            profile = p;
            engine = new FakeChatEngine(toolHost);
            engine.ScriptedTurns.Enqueue([
                ApiKeyMockMaterials.Emit("MockApp/Program.cs", "class P {}"),
            ]);
            engine.ScriptedTurns.Enqueue([
                ApiKeyMockMaterials.Emit("MockApp/CustomerList.cs", "class C {}"),
            ]);
            return engine;
        };
        var build = new QueueBuildRunner();
        build.Results.Enqueue(true);
        var request = new MockProjectAgentRequest(
            WorkingDirectory: work,
            ProjectName: project,
            AdditionalInstructions: null,
            Model: "gpt-4o",
            Profile: s.Target.Profile
        );

        var outcome = await new ApiKeyMockProjectAgent(factory, build).RunAsync(
            request,
            _ => { },
            TestContext.Current.CancellationToken
        );

        outcome.Success.Should().BeTrue();
        var generated = scaffold.GeneratedDirectory;
        var entities = GeneratedNamespaceDeclarations.OfFileContaining(
            generated,
            "class CustomerEntity"
        );
        var contracts = GeneratedNamespaceDeclarations.OfFileContaining(
            generated,
            "interface ICustomerRepository"
        );
        var inMemory = GeneratedNamespaceDeclarations.OfFileContaining(
            generated,
            "IServiceCollection AddGeneratedInMemoryRepositories("
        );

        var common = engine!.SentPrompts[0];
        common.Should().Contain($"- {entities}: Entities.g.cs");
        common.Should().Contain($"- {contracts}: Repositories.g.cs");
        common.Should().Contain($"- AddGeneratedInMemoryRepositories (namespace {inMemory})");

        var system = profile!.BuildSystemPrompt();
        system.Should().Contain("the parent namespace ending in .Generated declares no types");

        if (s.Target == Blazor)
        {
            system.Should().Contain("In Components/_Imports.razor, add `@using` lines");
        }
        else
        {
            system.Should().Contain("In App.xaml.cs, add a `using`");
        }
    }

    /// <summary>Generated/ 配下の *.g.cs を「相対パス → 内容」の辞書として読み出す（ターゲット間の同一性の比較用）</summary>
    private static Dictionary<string, string> ReadGeneratedRelative(string generatedDirectory) =>
        Directory
            .GetFiles(generatedDirectory, "*.g.cs", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(generatedDirectory, path), File.ReadAllText);

    /// <summary>.sln の本文から Project 行のプロジェクトの GUID（末尾の "{...}"）を取り出す</summary>
    private static string ExtractProjectGuid(string sln)
    {
        var projectLine = sln.Split('\n')
            .First(line => line.StartsWith("Project(", StringComparison.Ordinal));
        var lastBrace = projectLine.LastIndexOf('{');
        return projectLine[lastBrace..].Trim().TrimEnd('"');
    }

    /// <summary>行を作る（同期の本体）</summary>
    private static ScenarioRow<MockScaffoldScenario> Row(
        string id,
        string description,
        MockAgentTarget target,
        string dialect,
        ScaffoldExisting existing,
        Action<MockScaffoldContext, MockScaffoldScenario> body
    ) =>
        Row(
            id,
            description,
            target,
            dialect,
            existing,
            (c, s) =>
            {
                body(c, s);
                return Task.CompletedTask;
            }
        );

    /// <summary>行を作る</summary>
    /// <param name="id">行の Id（{まとまり}-{守っていること}）</param>
    /// <param name="description">説明</param>
    /// <param name="target">ターゲット</param>
    /// <param name="dialect">方言</param>
    /// <param name="existing">スキャフォールドの前の出力フォルダの状態</param>
    /// <param name="body">流して表明する本体（文脈と行の入力を受け取る）</param>
    private static ScenarioRow<MockScaffoldScenario> Row(
        string id,
        string description,
        MockAgentTarget target,
        string dialect,
        ScaffoldExisting existing,
        Func<MockScaffoldContext, MockScaffoldScenario, Task> body
    )
    {
        MockScaffoldScenario? scenario = null;
        scenario = new MockScaffoldScenario(target, dialect, existing, c => body(c, scenario!));
        return new(id, null, description, scenario);
    }
}
