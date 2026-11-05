using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// <see cref="DocumentErDiagramToolHost"/> だけが持つ振る舞い（<c>create_diagram</c>・図ファイルの状態の扱い・保存・レイアウトとクエリの温存）を、
/// 一時ファイルベースで検証する。
/// </summary>
/// <remarks>
/// 両ホストで同じ表明をするツールの振る舞い（エンティティ・列・リレーション・要約）は <see cref="ToolScenarioRows"/> の行へ移した
/// </remarks>
public sealed class DocumentErDiagramToolHostTests : IDisposable
{
    /// <summary>各テスト専用の一時ディレクトリ</summary>
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-mcp-host-" + Guid.NewGuid().ToString("N")
    );

    public DocumentErDiagramToolHostTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // クリーンアップ失敗はテスト結果に影響させない
        }
    }

    /// <summary>ディレクトリ内のファイルパスを組み立てる</summary>
    private string PathFor(string name) => Path.Combine(_dir, name);

    /// <summary>引数オブジェクトを JsonElement 化してツールを実行する</summary>
    private static (string Result, bool Success) Exec(string tool, string file, object args) =>
        DocumentErDiagramToolHost.Execute(tool, file, JsonSerializer.SerializeToElement(args));

    /// <summary>テスト用に「Customer(PK) と Order(PK+CustomerId)」を作成した図ファイルを用意する</summary>
    private string CreateParentChildFile()
    {
        var file = PathFor("diagram.json");
        DiagramFiles.Create(file, "sqlite");

        Exec("add_entity", file, new { table_name = "Customer" });
        Exec(
            "add_column",
            file,
            new
            {
                table_name = "Customer",
                column_name = "CustomerId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );
        Exec("add_entity", file, new { table_name = "Order" });
        Exec(
            "add_column",
            file,
            new
            {
                table_name = "Order",
                column_name = "OrderId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );
        Exec(
            "add_column",
            file,
            new
            {
                table_name = "Order",
                column_name = "CustomerId",
                data_type = "int",
                is_primary_key = false,
                is_nullable = false,
            }
        );

        return file;
    }

    // ---------------- create_diagram ----------------

    [Fact(DisplayName = "create_diagram はスキーマのみ文書（layout キーなし）を作成する")]
    public void CreateDiagram_CreatesSchemaOnlyFile()
    {
        var file = PathFor("new.json");

        var (result, success) = Exec(
            DocumentErDiagramToolHost.CreateDiagramToolName,
            file,
            new { target_dbms = "postgresql" }
        );

        success.Should().BeTrue();
        result.Should().Contain("postgresql");
        File.Exists(file).Should().BeTrue();

        // layout キーが JSON へ出力されないこと（スキーマのみ文書）
        var json = File.ReadAllText(file);
        json.Should().NotContain("\"Layout\"");

        var document = JsonStorageService.Load(file);
        document.Schema.TargetDbms.Should().Be("postgresql");
        document.Schema.Entities.Should().BeEmpty();
        document.Version.Should().Be(DiagramDocument.CurrentVersion);
    }

    /// <summary>create_diagram の 1 ケース（呼び出しの形と期待）</summary>
    /// <param name="RelativePath">図ファイルのパス（一時ディレクトリからの相対）</param>
    /// <param name="Prepare">呼び出しの前にファイルを用意する（無ければ何もしない）</param>
    /// <param name="Args">引数</param>
    /// <param name="ExpectSuccess">期待する成否</param>
    /// <param name="Fragment">応答に含まれるはずの断片（応答を見ないなら null）</param>
    /// <param name="Verify">呼び出しの後のファイルの確認</param>
    private sealed record CreateDiagramCase(
        string RelativePath,
        Action<string>? Prepare,
        object Args,
        bool ExpectSuccess,
        string? Fragment,
        Action<string> Verify
    );

    /// <summary>create_diagram のケース（名前 → ケース）</summary>
    private static Dictionary<string, CreateDiagramCase> CreateDiagramCases { get; } =
        new()
        {
            ["normalizes-dbms-case"] = new(
                "cased.json",
                null,
                new { target_dbms = "SqlServer" },
                true,
                null,
                file => JsonStorageService.Load(file).Schema.TargetDbms.Should().Be("sqlserver")
            ),
            ["existing-file-is-not-overwritten"] = new(
                "exists.json",
                file => File.WriteAllText(file, "original"),
                new { target_dbms = "sqlite" },
                false,
                "already exists",
                file => File.ReadAllText(file).Should().Be("original")
            ),
            ["missing-parent-directory-is-not-created"] = new(
                Path.Combine("nope", "child.json"),
                null,
                new { target_dbms = "sqlite" },
                false,
                "does not exist",
                file => File.Exists(file).Should().BeFalse()
            ),
            ["invalid-dbms"] = new(
                "bad.json",
                null,
                new { target_dbms = "db2" },
                false,
                "db2",
                file => File.Exists(file).Should().BeFalse()
            ),
            ["missing-dbms"] = new(
                "nodbms.json",
                null,
                new { },
                false,
                "target_dbms",
                file => File.Exists(file).Should().BeFalse()
            ),
        };

    /// <summary>create_diagram のケース名</summary>
    public static TheoryData<string> CreateDiagramCaseNames
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var name in CreateDiagramCases.Keys)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory(
        DisplayName = "create_diagram は target_dbms の正規化・既存ファイル・親ディレクトリ・不正値・未指定を期待どおりに扱う"
    )]
    [MemberData(nameof(CreateDiagramCaseNames))]
    public void CreateDiagram_HandlesArgumentsAndExistingFile(string caseName)
    {
        var testCase = CreateDiagramCases[caseName];
        var file = PathFor(testCase.RelativePath);
        testCase.Prepare?.Invoke(file);

        var (result, success) = Exec(
            DocumentErDiagramToolHost.CreateDiagramToolName,
            file,
            testCase.Args
        );

        success.Should().Be(testCase.ExpectSuccess);

        if (testCase.Fragment is { } fragment)
        {
            result.Should().Contain(fragment);
        }

        testCase.Verify(file);
    }

    // ---------------- guards ----------------

    /// <summary>図ファイルの状態を替えてツールを呼ぶ 1 ケース</summary>
    /// <param name="FileName">図ファイルの名前</param>
    /// <param name="Prepare">呼び出しの前にファイルを用意する（無ければ何もしない）</param>
    /// <param name="Tool">ツール名</param>
    /// <param name="Args">引数</param>
    /// <param name="ExpectSuccess">期待する成否</param>
    /// <param name="Fragments">応答に含まれるはずの断片</param>
    /// <param name="Verify">呼び出しの後のファイルの確認（無ければ何もしない）</param>
    private sealed record FileStateCase(
        string FileName,
        Action<string>? Prepare,
        string Tool,
        object Args,
        bool ExpectSuccess,
        string[] Fragments,
        Action<string>? Verify
    );

    /// <summary>図ファイルの状態のケース（名前 → ケース）</summary>
    private static Dictionary<string, FileStateCase> FileStateCases { get; } =
        new()
        {
            ["mutating-file-not-found"] = new(
                "missing.json",
                null,
                "add_entity",
                new { table_name = "X" },
                false,
                ["not found", DocumentErDiagramToolHost.CreateDiagramToolName],
                null
            ),
            ["mutating-non-diagram-json-is-not-overwritten"] = new(
                "unrelated.json",
                file => File.WriteAllText(file, "{\"foo\":1}"),
                "add_entity",
                new { table_name = "X" },
                false,
                ["not a DiagramDocument"],
                file => File.ReadAllText(file).Should().Be("{\"foo\":1}")
            ),
            ["mutating-invalid-json"] = new(
                "broken.json",
                file => File.WriteAllText(file, "this is not json"),
                "add_entity",
                new { table_name = "X" },
                false,
                ["not valid JSON"],
                null
            ),
            ["mutating-newer-format-is-rejected"] = new(
                "newer.json",
                file =>
                    SaveRaw(
                        file,
                        new DiagramDocument { Version = DiagramDocument.CurrentVersion + 5 }
                    ),
                "add_entity",
                new { table_name = "X" },
                false,
                ["refusing to modify"],
                null
            ),
            ["summary-newer-format-warns-and-continues"] = new(
                "newer-summary.json",
                file =>
                {
                    var document = new DiagramDocument
                    {
                        Version = DiagramDocument.CurrentVersion + 5,
                    };
                    document.Schema.Entities.Add(new Entity { TableName = "Book" });
                    SaveRaw(file, document);
                },
                "get_diagram_summary",
                new { },
                true,
                ["Warning", "Book"],
                null
            ),
        };

    /// <summary>図ファイルの状態のケース名</summary>
    public static TheoryData<string> FileStateCaseNames
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var name in FileStateCases.Keys)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory(
        DisplayName = "ツールは図ファイルが存在しない・図でない JSON・不正な JSON・新しいフォーマットのとき期待どおりに断る・続行する"
    )]
    [MemberData(nameof(FileStateCaseNames))]
    public void Tool_HandlesFileState(string caseName)
    {
        var testCase = FileStateCases[caseName];
        var file = PathFor(testCase.FileName);
        testCase.Prepare?.Invoke(file);

        var (result, success) = Exec(testCase.Tool, file, testCase.Args);

        success.Should().Be(testCase.ExpectSuccess);

        foreach (var fragment in testCase.Fragments)
        {
            result.Should().Contain(fragment);
        }

        testCase.Verify?.Invoke(file);
    }

    /// <summary>
    /// 保存が原子的（一時ファイル経由の差し替え）でも、作成・変更の結果と後始末が正しく行われることを検証する。
    /// </summary>
    /// <remarks>
    /// ユーザーの実ファイルへ書き戻すため、書き込み途中の中断で既存の図が壊れないよう
    /// <see cref="JsonStorageService.SaveAtomic"/> を通す。差し替え後に <c>.tmp</c> が残ると
    /// 外部エージェントの作業フォルダにゴミが溜まるため、残らないことを見張る。
    /// </remarks>
    [Fact(DisplayName = "保存は一時ファイルを残さない（create_diagram / 変更系とも）")]
    public void Saving_LeavesNoTemporaryFile()
    {
        var file = PathFor("atomic.json");

        Exec(DocumentErDiagramToolHost.CreateDiagramToolName, file, new { target_dbms = "sqlite" })
            .Success.Should()
            .BeTrue();
        // 一時ファイル名は {path}.{GUID}.tmp のためワイルドカードで検出する
        Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty("作成直後に一時ファイルは残らない");

        Exec("add_entity", file, new { table_name = "Book" }).Success.Should().BeTrue();

        JsonStorageService.Load(file).Schema.Entities.Single().TableName.Should().Be("Book");
        Directory
            .GetFiles(_dir, "*.tmp")
            .Should()
            .BeEmpty("変更の保存後にも一時ファイルは残らない");
    }

    // ---------------- entity removal / preservation ----------------

    [Fact(DisplayName = "remove_entity は接続リレーションと孤児レイアウトも削除する")]
    public void RemoveEntity_RemovesRelationshipsAndOrphanLayout()
    {
        var file = CreateParentChildFile();
        Exec(
            "add_relationship",
            file,
            new
            {
                source_table = "Customer",
                target_table = "Order",
                relationship_type = "OneToMany",
            }
        );

        // Order にレイアウトエントリを付与しておく（削除で孤児になるべきもの）
        var document = JsonStorageService.Load(file);
        var order = document.Schema.Entities.Single(e => e.TableName == "Order");
        var orderId = order.Id;
        document.Layout ??= new();
        document.Layout[orderId] = new EntityLayout { X = 10, Y = 20 };
        JsonStorageService.SaveAtomic(file, document);

        var (_, success) = Exec("remove_entity", file, new { table_name = "Order" });

        success.Should().BeTrue();
        var reloaded = JsonStorageService.Load(file);
        reloaded.Schema.Entities.Should().NotContain(e => e.TableName == "Order");
        reloaded.Schema.Relationships.Should().BeEmpty();
        reloaded.Layout.Should().NotContainKey(orderId);
    }

    [Fact(DisplayName = "変更しても既存のレイアウトは温存される")]
    public void Mutation_PreservesExistingLayout()
    {
        var file = PathFor("layout.json");
        DiagramFiles.Create(file, "sqlite");
        Exec("add_entity", file, new { table_name = "Book" });

        var document = JsonStorageService.Load(file);
        var book = document.Schema.Entities.Single();
        document.Layout ??= new();
        document.Layout[book.Id] = new EntityLayout { X = 111, Y = 222 };
        JsonStorageService.SaveAtomic(file, document);

        Exec(
            "add_column",
            file,
            new
            {
                table_name = "Book",
                column_name = "BookId",
                data_type = "int",
            }
        );

        var reloaded = JsonStorageService.Load(file);
        reloaded.Layout.Should().ContainKey(book.Id);
        reloaded.Layout![book.Id].X.Should().Be(111);
        reloaded.Layout[book.Id].Y.Should().Be(222);
    }

    [Fact(DisplayName = "変更しても既存の名前付きクエリは温存される")]
    public void Mutation_PreservesQueries()
    {
        var file = PathFor("queries.json");
        DiagramFiles.Create(file, "sqlite");
        Exec("add_entity", file, new { table_name = "Book" });

        var document = JsonStorageService.Load(file);
        var book = document.Schema.Entities.Single();
        document.Schema.Queries.Add(
            new QueryDefinition { Name = "GetAllBooks", EntityId = book.Id }
        );
        JsonStorageService.SaveAtomic(file, document);

        Exec("add_entity", file, new { table_name = "Author" });

        var reloaded = JsonStorageService.Load(file);
        reloaded.Schema.Queries.Should().ContainSingle(q => q.Name == "GetAllBooks");
    }

    /// <summary>JsonStorageService をそのまま使い、任意バージョンの文書をファイルへ保存する</summary>
    private static void SaveRaw(string file, DiagramDocument document) =>
        JsonStorageService.SaveAtomic(file, document);
}
