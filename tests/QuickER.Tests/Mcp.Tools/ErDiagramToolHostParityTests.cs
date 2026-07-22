using System.IO;
using System.Text.Json;
using FluentAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 同一のツール呼び出し列を (a) GUI 側 <see cref="ErDiagramDynamicTools"/>＋<see cref="MainViewModel"/> と
/// (b) <see cref="DocumentErDiagramToolHost"/>＋一時ファイル の両経路へ流し、結果の意味モデル
/// （エンティティ名・列の名前/型/PK/NULL/説明・リレーションの端点/種別）と各呼び出しの成否が
/// 一致することを検証するパリティテスト。Guid は両経路で新規生成されるため突合には使わず、名前で対応付ける。
/// </summary>
public sealed class ErDiagramToolHostParityTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-mcp-parity-" + Guid.NewGuid().ToString("N")
    );

    public ErDiagramToolHostParityTests()
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
            // クリーンアップ失敗は無視
        }
    }

    /// <summary>比較用の列スナップショット（PK/NULL/説明を含む・FK フラグは比較対象外）</summary>
    private sealed record ColumnSnap(
        string Name,
        string DataType,
        bool IsPrimaryKey,
        bool IsNullable,
        string Description
    );

    /// <summary>比較用のエンティティスナップショット</summary>
    private sealed record EntitySnap(
        string TableName,
        string Description,
        List<ColumnSnap> Columns
    );

    /// <summary>比較用のリレーションスナップショット（端点は名前・種別は文字列）</summary>
    private sealed record RelSnap(string Source, string Target, string Type);

    [Fact(
        DisplayName = "GUI 実行ホストとファイル実行ホストは同一シナリオで同じ意味モデル・成否になる"
    )]
    public void GuiAndDocumentHosts_ProduceEquivalentModel()
    {
        // 9 ツール全部を通る代表シナリオ（末尾に失敗系も含め成否パリティも確認する）
        var scenario = new (string Tool, object Args)[]
        {
            ("add_entity", new { table_name = "Customer" }),
            (
                "add_column",
                new
                {
                    table_name = "Customer",
                    column_name = "CustomerId",
                    data_type = "int",
                    is_primary_key = true,
                    is_nullable = false,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = "Customer",
                    column_name = "Name",
                    data_type = "nvarchar(50)",
                    is_primary_key = false,
                    is_nullable = false,
                }
            ),
            ("add_entity", new { table_name = "Order" }),
            (
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "OrderId",
                    data_type = "int",
                    is_primary_key = true,
                    is_nullable = false,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "CustomerId",
                    data_type = "int",
                    is_primary_key = false,
                    is_nullable = false,
                }
            ),
            ("add_entity", new { table_name = "OrderLine" }),
            (
                "add_column",
                new
                {
                    table_name = "OrderLine",
                    column_name = "OrderLineId",
                    data_type = "int",
                    is_primary_key = true,
                    is_nullable = false,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = "OrderLine",
                    column_name = "OrderId",
                    data_type = "int",
                    is_primary_key = false,
                    is_nullable = false,
                }
            ),
            // リレーション 2 種（列は省略し名前規則で解決させる。異なる種別で作成）
            (
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    relationship_type = "OneToMany",
                }
            ),
            (
                "add_relationship",
                new
                {
                    source_table = "Order",
                    target_table = "OrderLine",
                    relationship_type = "OneToOne",
                }
            ),
            // プロパティ変更
            (
                "set_column_property",
                new
                {
                    table_name = "Order",
                    column_name = "CustomerId",
                    description = "Owning customer",
                }
            ),
            (
                "set_entity_property",
                new
                {
                    table_name = "OrderLine",
                    description = "Line items of an order",
                    memo = "note",
                }
            ),
            // 列削除（Order→OrderLine の FK 列 OrderId を消し、参照クリアも通す）
            ("remove_column", new { table_name = "OrderLine", column_name = "OrderId" }),
            // エンティティ削除（接続リレーションも巻き添え削除）
            ("remove_entity", new { table_name = "OrderLine" }),
            // 失敗系（存在しないテーブル・存在しない列）— 成否パリティを確認する
            ("remove_entity", new { table_name = "Ghost" }),
            (
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    target_column = "NoSuchColumn",
                    relationship_type = "OneToMany",
                }
            ),
        };

        var vm = new MainViewModel();
        var file = Path.Combine(_dir, "parity.json");
        DocumentErDiagramToolHost
            .Execute(
                DocumentErDiagramToolHost.CreateDiagramToolName,
                file,
                JsonSerializer.SerializeToElement(new { target_dbms = "sqlserver" })
            )
            .Success.Should()
            .BeTrue();

        for (var step = 0; step < scenario.Length; step++)
        {
            var (tool, args) = scenario[step];
            var element = JsonSerializer.SerializeToElement(args);

            var guiResult = ErDiagramDynamicTools.Execute(tool, element, vm);
            var hostResult = DocumentErDiagramToolHost.Execute(tool, file, element);

            hostResult
                .Success.Should()
                .Be(
                    guiResult.Success,
                    $"step {step} ({tool}) success flag must match (GUI: '{guiResult.Result}', host: '{hostResult.Result}')"
                );
        }

        // 最終的な意味モデルが両経路で一致すること
        var guiEntities = SnapshotEntities(vm);
        var guiRelationships = SnapshotRelationships(vm);

        var schema = JsonStorageService.Load(file).Schema;
        var hostEntities = SnapshotEntities(schema);
        var hostRelationships = SnapshotRelationships(schema);

        hostEntities.Should().BeEquivalentTo(guiEntities, opts => opts.WithStrictOrdering());
        hostRelationships
            .Should()
            .BeEquivalentTo(guiRelationships, opts => opts.WithStrictOrdering());
    }

    /// <summary>ViewModel からエンティティスナップショットを作る</summary>
    private static List<EntitySnap> SnapshotEntities(MainViewModel vm) =>
        vm
            .Entities.Select(e => new EntitySnap(
                e.TableName,
                e.Description,
                e.Columns.Select(c => new ColumnSnap(
                        c.Name,
                        c.DataType,
                        c.IsPrimaryKey,
                        c.IsNullable,
                        c.Description
                    ))
                    .ToList()
            ))
            .ToList();

    /// <summary>意味モデルからエンティティスナップショットを作る</summary>
    private static List<EntitySnap> SnapshotEntities(ErDiagram schema) =>
        schema
            .Entities.Select(e => new EntitySnap(
                e.TableName,
                e.Description,
                e.Columns.Select(c => new ColumnSnap(
                        c.Name,
                        c.DataType,
                        c.IsPrimaryKey,
                        c.IsNullable,
                        c.Description
                    ))
                    .ToList()
            ))
            .ToList();

    /// <summary>ViewModel からリレーションスナップショットを作る</summary>
    private static List<RelSnap> SnapshotRelationships(MainViewModel vm) =>
        vm
            .Relationships.Select(r => new RelSnap(
                r.Source.TableName,
                r.Target.TableName,
                r.Type.ToString()
            ))
            .ToList();

    /// <summary>意味モデルからリレーションスナップショットを作る（端点は ID→テーブル名で解決）</summary>
    private static List<RelSnap> SnapshotRelationships(ErDiagram schema) =>
        schema
            .Relationships.Select(r => new RelSnap(
                schema.Entities.First(e => e.Id == r.SourceEntityId).TableName,
                schema.Entities.First(e => e.Id == r.TargetEntityId).TableName,
                r.Type.ToString()
            ))
            .ToList();
}
