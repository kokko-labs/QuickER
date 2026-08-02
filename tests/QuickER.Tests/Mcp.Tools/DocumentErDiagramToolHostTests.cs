using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// <see cref="DocumentErDiagramToolHost"/> の各ツールの正常系・エラー系を、一時ファイルベースで検証する。
/// </summary>
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
        Exec(DocumentErDiagramToolHost.CreateDiagramToolName, file, new { target_dbms = "sqlite" })
            .Success.Should()
            .BeTrue();

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

    [Fact(DisplayName = "create_diagram は大文字混じりの target_dbms を正規化して受け付ける")]
    public void CreateDiagram_NormalizesDbmsCase()
    {
        var file = PathFor("cased.json");

        var (_, success) = Exec(
            DocumentErDiagramToolHost.CreateDiagramToolName,
            file,
            new { target_dbms = "SqlServer" }
        );

        success.Should().BeTrue();
        JsonStorageService.Load(file).Schema.TargetDbms.Should().Be("sqlserver");
    }

    [Fact(DisplayName = "create_diagram は既存ファイルを上書きせずエラーにする")]
    public void CreateDiagram_ExistingFile_Fails()
    {
        var file = PathFor("exists.json");
        File.WriteAllText(file, "original");

        var (result, success) = Exec(
            DocumentErDiagramToolHost.CreateDiagramToolName,
            file,
            new { target_dbms = "sqlite" }
        );

        success.Should().BeFalse();
        result.Should().Contain("already exists");
        File.ReadAllText(file).Should().Be("original");
    }

    [Fact(DisplayName = "create_diagram は親ディレクトリが無ければエラーにする（掘らない）")]
    public void CreateDiagram_MissingParentDirectory_Fails()
    {
        var file = PathFor(Path.Combine("nope", "child.json"));

        var (result, success) = Exec(
            DocumentErDiagramToolHost.CreateDiagramToolName,
            file,
            new { target_dbms = "sqlite" }
        );

        success.Should().BeFalse();
        result.Should().Contain("does not exist");
        File.Exists(file).Should().BeFalse();
    }

    [Fact(DisplayName = "create_diagram は不正な target_dbms をエラーにする")]
    public void CreateDiagram_InvalidDbms_Fails()
    {
        var file = PathFor("bad.json");

        var (result, success) = Exec(
            DocumentErDiagramToolHost.CreateDiagramToolName,
            file,
            new { target_dbms = "db2" }
        );

        success.Should().BeFalse();
        result.Should().Contain("db2");
        File.Exists(file).Should().BeFalse();
    }

    [Fact(DisplayName = "create_diagram は target_dbms 未指定をエラーにする")]
    public void CreateDiagram_MissingDbms_Fails()
    {
        var file = PathFor("nodbms.json");

        var (result, success) = Exec(
            DocumentErDiagramToolHost.CreateDiagramToolName,
            file,
            new { }
        );

        success.Should().BeFalse();
        result.Should().Contain("target_dbms");
        File.Exists(file).Should().BeFalse();
    }

    // ---------------- guards ----------------

    [Fact(DisplayName = "変更系ツールは存在しないファイルをエラーにする")]
    public void Mutating_FileNotFound_ReturnsError()
    {
        var file = PathFor("missing.json");

        var (result, success) = Exec("add_entity", file, new { table_name = "X" });

        success.Should().BeFalse();
        result.Should().Contain("not found");
        result.Should().Contain(DocumentErDiagramToolHost.CreateDiagramToolName);
    }

    [Fact(DisplayName = "変更系ツールは DiagramDocument でない JSON を上書きせずエラーにする")]
    public void Mutating_NonDiagramJson_ReturnsErrorAndDoesNotOverwrite()
    {
        var file = PathFor("unrelated.json");
        File.WriteAllText(file, "{\"foo\":1}");

        var (result, success) = Exec("add_entity", file, new { table_name = "X" });

        success.Should().BeFalse();
        result.Should().Contain("not a DiagramDocument");
        File.ReadAllText(file).Should().Be("{\"foo\":1}");
    }

    [Fact(DisplayName = "変更系ツールは不正な JSON をエラーにする")]
    public void Mutating_InvalidJson_ReturnsError()
    {
        var file = PathFor("broken.json");
        File.WriteAllText(file, "this is not json");

        var (result, success) = Exec("add_entity", file, new { table_name = "X" });

        success.Should().BeFalse();
        result.Should().Contain("not valid JSON");
    }

    [Fact(DisplayName = "変更系ツールは新しいフォーマットの文書を拒否する")]
    public void Mutating_NewerFormat_Rejected()
    {
        var file = PathFor("newer.json");
        SaveRaw(file, new DiagramDocument { Version = DiagramDocument.CurrentVersion + 5 });

        var (result, success) = Exec("add_entity", file, new { table_name = "X" });

        success.Should().BeFalse();
        result.Should().Contain("refusing to modify");
    }

    [Fact(DisplayName = "get_diagram_summary は新しいフォーマットでも警告付きで続行する")]
    public void Summary_NewerFormat_WarnsAndContinues()
    {
        var file = PathFor("newer-summary.json");
        var document = new DiagramDocument { Version = DiagramDocument.CurrentVersion + 5 };
        document.Schema.Entities.Add(new Entity { TableName = "Book" });
        SaveRaw(file, document);

        var (result, success) = Exec("get_diagram_summary", file, new { });

        success.Should().BeTrue();
        result.Should().Contain("Warning");
        result.Should().Contain("Book");
    }

    // ---------------- entity / column operations ----------------

    [Fact(DisplayName = "add_entity はテーブルを追加し列は自動生成しない")]
    public void AddEntity_AddsTableWithoutColumns()
    {
        var file = PathFor("d.json");
        Exec(DocumentErDiagramToolHost.CreateDiagramToolName, file, new { target_dbms = "sqlite" });

        var (result, success) = Exec(
            "add_entity",
            file,
            new { table_name = "Book", description = "books" }
        );

        success.Should().BeTrue();
        result.Should().Contain("Book");
        var entity = JsonStorageService.Load(file).Schema.Entities.Single();
        entity.TableName.Should().Be("Book");
        entity.Description.Should().Be("books");
        entity.Columns.Should().BeEmpty();
    }

    [Fact(DisplayName = "add_column は主キー・NULL 許容フラグを指定どおり設定する")]
    public void AddColumn_SetsFlags()
    {
        var file = PathFor("d.json");
        Exec(DocumentErDiagramToolHost.CreateDiagramToolName, file, new { target_dbms = "sqlite" });
        Exec("add_entity", file, new { table_name = "Book" });

        Exec(
            "add_column",
            file,
            new
            {
                table_name = "Book",
                column_name = "BookId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );

        var column = JsonStorageService.Load(file).Schema.Entities.Single().Columns.Single();
        column.Name.Should().Be("BookId");
        column.IsPrimaryKey.Should().BeTrue();
        column.IsNullable.Should().BeFalse();
    }

    [Fact(DisplayName = "add_column は存在しないテーブルをエラーにする")]
    public void AddColumn_UnknownTable_ReturnsError()
    {
        var file = PathFor("d.json");
        Exec(DocumentErDiagramToolHost.CreateDiagramToolName, file, new { target_dbms = "sqlite" });

        var (_, success) = Exec(
            "add_column",
            file,
            new
            {
                table_name = "NoSuch",
                column_name = "X",
                data_type = "int",
            }
        );

        success.Should().BeFalse();
    }

    [Fact(DisplayName = "set_entity_property は指定プロパティのみ更新する")]
    public void SetEntityProperty_UpdatesSpecifiedOnly()
    {
        var file = PathFor("d.json");
        Exec(DocumentErDiagramToolHost.CreateDiagramToolName, file, new { target_dbms = "sqlite" });
        Exec("add_entity", file, new { table_name = "Book" });

        var (_, success) = Exec(
            "set_entity_property",
            file,
            new
            {
                table_name = "Book",
                new_table_name = "Books",
                description = "renamed",
            }
        );

        success.Should().BeTrue();
        var entity = JsonStorageService.Load(file).Schema.Entities.Single();
        entity.TableName.Should().Be("Books");
        entity.Description.Should().Be("renamed");
        entity.Memo.Should().BeEmpty();
    }

    [Fact(DisplayName = "set_entity_property は変更対象未指定をエラーにする")]
    public void SetEntityProperty_NoChange_ReturnsError()
    {
        var file = PathFor("d.json");
        Exec(DocumentErDiagramToolHost.CreateDiagramToolName, file, new { target_dbms = "sqlite" });
        Exec("add_entity", file, new { table_name = "Book" });

        var (_, success) = Exec("set_entity_property", file, new { table_name = "Book" });

        success.Should().BeFalse();
    }

    [Fact(DisplayName = "set_column_property はデータ型と NULL 許容を更新する")]
    public void SetColumnProperty_UpdatesDataTypeAndNullable()
    {
        var file = PathFor("d.json");
        Exec(DocumentErDiagramToolHost.CreateDiagramToolName, file, new { target_dbms = "sqlite" });
        Exec("add_entity", file, new { table_name = "Book" });
        Exec(
            "add_column",
            file,
            new
            {
                table_name = "Book",
                column_name = "Title",
                data_type = "nvarchar(100)",
                is_nullable = false,
            }
        );

        var (_, success) = Exec(
            "set_column_property",
            file,
            new
            {
                table_name = "Book",
                column_name = "Title",
                data_type = "nvarchar(500)",
                is_nullable = true,
            }
        );

        success.Should().BeTrue();
        var column = JsonStorageService
            .Load(file)
            .Schema.Entities.Single()
            .Columns.Single(c => c.Name == "Title");
        column.DataType.Should().Be("nvarchar(500)");
        column.IsNullable.Should().BeTrue();
    }

    // ---------------- relationship operations ----------------

    [Fact(DisplayName = "add_relationship はカラム省略時に命名規則で FK 列を解決する")]
    public void AddRelationship_ResolvesTargetColumnByConvention()
    {
        var file = CreateParentChildFile();

        var (_, success) = Exec(
            "add_relationship",
            file,
            new
            {
                source_table = "Customer",
                target_table = "Order",
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeTrue();
        var schema = JsonStorageService.Load(file).Schema;
        var order = schema.Entities.Single(e => e.TableName == "Order");
        var customerIdColumn = order.Columns.Single(c => c.Name == "CustomerId");
        var relationship = schema.Relationships.Single();
        relationship.TargetColumnId.Should().Be(customerIdColumn.Id);
        // 参照先の FK 列へ FK フラグが付与される
        customerIdColumn.IsForeignKey.Should().BeTrue();
    }

    [Fact(DisplayName = "add_relationship は存在しない target_column をエラーにする")]
    public void AddRelationship_UnknownTargetColumn_ReturnsError()
    {
        var file = CreateParentChildFile();

        var (result, success) = Exec(
            "add_relationship",
            file,
            new
            {
                source_table = "Customer",
                target_table = "Order",
                target_column = "NoSuchColumn",
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("NoSuchColumn");
        JsonStorageService.Load(file).Schema.Relationships.Should().BeEmpty();
    }

    [Fact(DisplayName = "remove_relationship は指定した端点間のリレーションを削除する")]
    public void RemoveRelationship_RemovesRelationship()
    {
        var file = CreateParentChildFile();
        Exec("add_relationship", file, new { source_table = "Customer", target_table = "Order" });

        var (_, success) = Exec(
            "remove_relationship",
            file,
            new { source_table = "Customer", target_table = "Order" }
        );

        success.Should().BeTrue();
        JsonStorageService.Load(file).Schema.Relationships.Should().BeEmpty();
    }

    [Fact(DisplayName = "remove_column は FK 列削除時にリレーションの参照をクリアする")]
    public void RemoveColumn_ClearsRelationshipReference()
    {
        var file = CreateParentChildFile();
        Exec("add_relationship", file, new { source_table = "Customer", target_table = "Order" });

        var (_, success) = Exec(
            "remove_column",
            file,
            new { table_name = "Order", column_name = "CustomerId" }
        );

        success.Should().BeTrue();
        var schema = JsonStorageService.Load(file).Schema;
        var order = schema.Entities.Single(e => e.TableName == "Order");
        order.Columns.Should().NotContain(c => c.Name == "CustomerId");
        schema.Relationships.Single().TargetColumnId.Should().BeNull();
    }

    [Fact(DisplayName = "remove_entity は接続リレーションと孤児レイアウトも削除する")]
    public void RemoveEntity_RemovesRelationshipsAndOrphanLayout()
    {
        var file = CreateParentChildFile();
        Exec("add_relationship", file, new { source_table = "Customer", target_table = "Order" });

        // Order にレイアウトエントリを付与しておく（削除で孤児になるべきもの）
        var document = JsonStorageService.Load(file);
        var order = document.Schema.Entities.Single(e => e.TableName == "Order");
        var orderId = order.Id;
        document.Layout ??= new();
        document.Layout[orderId] = new EntityLayout { X = 10, Y = 20 };
        JsonStorageService.Save(file, document);

        var (_, success) = Exec("remove_entity", file, new { table_name = "Order" });

        success.Should().BeTrue();
        var reloaded = JsonStorageService.Load(file);
        reloaded.Schema.Entities.Should().NotContain(e => e.TableName == "Order");
        reloaded.Schema.Relationships.Should().BeEmpty();
        reloaded.Layout.Should().NotContainKey(orderId);
    }

    // ---------------- preservation ----------------

    [Fact(DisplayName = "変更しても既存のレイアウトは温存される")]
    public void Mutation_PreservesExistingLayout()
    {
        var file = PathFor("layout.json");
        Exec(DocumentErDiagramToolHost.CreateDiagramToolName, file, new { target_dbms = "sqlite" });
        Exec("add_entity", file, new { table_name = "Book" });

        var document = JsonStorageService.Load(file);
        var book = document.Schema.Entities.Single();
        document.Layout ??= new();
        document.Layout[book.Id] = new EntityLayout { X = 111, Y = 222 };
        JsonStorageService.Save(file, document);

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
        Exec(DocumentErDiagramToolHost.CreateDiagramToolName, file, new { target_dbms = "sqlite" });
        Exec("add_entity", file, new { table_name = "Book" });

        var document = JsonStorageService.Load(file);
        var book = document.Schema.Entities.Single();
        document.Schema.Queries.Add(
            new QueryDefinition { Name = "GetAllBooks", EntityId = book.Id }
        );
        JsonStorageService.Save(file, document);

        Exec("add_entity", file, new { table_name = "Author" });

        var reloaded = JsonStorageService.Load(file);
        reloaded.Schema.Queries.Should().ContainSingle(q => q.Name == "GetAllBooks");
    }

    /// <summary>JsonStorageService をそのまま使い、任意バージョンの文書をファイルへ保存する</summary>
    private static void SaveRaw(string file, DiagramDocument document) =>
        JsonStorageService.Save(file, document);
}
