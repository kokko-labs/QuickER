using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 同一のツール呼び出し列を (a) GUI 側 <see cref="ErDiagramDynamicTools"/>＋<see cref="MainViewModel"/> と
/// (b) <see cref="DocumentErDiagramToolHost"/>＋一時ファイル の両経路へ流し、結果の意味モデル
/// （エンティティ名・列の名前/型/PK/NULL/説明・一意制約の名前/構成列・リレーションの端点/種別）と各呼び出しの成否が
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

    /// <summary>比較用の一意制約スナップショット（名前と構成列名を宣言順で保持する）</summary>
    private sealed record UniqueSnap(string Name, List<string> Columns);

    /// <summary>比較用のエンティティスナップショット</summary>
    private sealed record EntitySnap(
        string TableName,
        string Description,
        List<ColumnSnap> Columns,
        List<UniqueSnap> UniqueConstraints
    );

    /// <summary>比較用のリレーションスナップショット（端点は名前・種別は文字列・列ペアは「親列→子列」の宣言順）</summary>
    private sealed record RelSnap(
        string Source,
        string Target,
        string Type,
        string ConstraintName,
        List<string> ColumnPairs
    );

    [Fact(
        DisplayName = "GUI 実行ホストとファイル実行ホストは同一シナリオで同じ意味モデル・成否になる"
    )]
    public void GuiAndDocumentHosts_ProduceEquivalentModel()
    {
        // ER 図操作 11 ツール全部を通る代表シナリオ（末尾に失敗系も含め成否パリティも確認する）
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
            // 一意制約（追加・列集合一致での upsert・列集合での削除・失敗系）
            ("set_unique_constraint", new { table_name = "Customer", columns = new[] { "Name" } }),
            (
                "set_unique_constraint",
                new
                {
                    table_name = "Order",
                    columns = new[] { "OrderId", "CustomerId" },
                    name = "UQ_Order_Pair",
                }
            ),
            // 同じ列集合を順序違い・大小違いで再定義（upsert＝件数は増えず列順と名前が更新される）
            (
                "set_unique_constraint",
                new { table_name = "Order", columns = new[] { "customerid", "orderid" } }
            ),
            (
                "set_unique_constraint",
                new { table_name = "OrderLine", columns = new[] { "OrderId" } }
            ),
            // 失敗系（存在しない列・列集合が一致しない削除）
            (
                "set_unique_constraint",
                new { table_name = "Customer", columns = new[] { "NoSuchColumn" } }
            ),
            (
                "remove_unique_constraint",
                new { table_name = "Customer", columns = new[] { "CustomerId" } }
            ),
            // 列削除（Order→OrderLine の FK 列 OrderId を消し、参照クリアも通す）
            // OrderLine.OrderId の一意制約は制約ごと消える（両ホスト共通のカスケード）
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
                    source_columns = new[] { "CustomerId" },
                    target_table = "Order",
                    target_columns = new[] { "NoSuchColumn" },
                    relationship_type = "OneToMany",
                }
            ),
            // 複合外部キー（親 PK 2 列）— 明示指定・自動ペア化・失敗系を通す
            ("add_entity", new { table_name = "TenantRegion" }),
            (
                "add_column",
                new
                {
                    table_name = "TenantRegion",
                    column_name = "TenantId",
                    data_type = "int",
                    is_primary_key = true,
                    is_nullable = false,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = "TenantRegion",
                    column_name = "RegionCode",
                    data_type = "nvarchar(10)",
                    is_primary_key = true,
                    is_nullable = false,
                }
            ),
            ("add_entity", new { table_name = "TenantUser" }),
            (
                "add_column",
                new
                {
                    table_name = "TenantUser",
                    column_name = "TenantUserId",
                    data_type = "int",
                    is_primary_key = true,
                    is_nullable = false,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = "TenantUser",
                    column_name = "TenantId",
                    data_type = "int",
                    is_primary_key = false,
                    is_nullable = false,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = "TenantUser",
                    column_name = "RegionCode",
                    data_type = "nvarchar(10)",
                    is_primary_key = false,
                    is_nullable = false,
                }
            ),
            // 列省略＝親 PK 全列の自動ペア化（両ホストで同じ 2 組になる）
            (
                "add_relationship",
                new
                {
                    source_table = "TenantRegion",
                    target_table = "TenantUser",
                    relationship_type = "OneToMany",
                }
            ),
            ("add_entity", new { table_name = "TenantAudit" }),
            (
                "add_column",
                new
                {
                    table_name = "TenantAudit",
                    column_name = "TenantAuditId",
                    data_type = "int",
                    is_primary_key = true,
                    is_nullable = false,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = "TenantAudit",
                    column_name = "TenantRef",
                    data_type = "int",
                    is_primary_key = false,
                    is_nullable = false,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = "TenantAudit",
                    column_name = "RegionRef",
                    data_type = "nvarchar(10)",
                    is_primary_key = false,
                    is_nullable = false,
                }
            ),
            // 明示指定の複合外部キー（宣言順どおりの 2 組）
            (
                "add_relationship",
                new
                {
                    source_table = "TenantRegion",
                    source_columns = new[] { "TenantId", "RegionCode" },
                    target_table = "TenantAudit",
                    target_columns = new[] { "TenantRef", "RegionRef" },
                    relationship_type = "OneToMany",
                }
            ),
            // 多対多は列を明示しても列ペアを持たない（両ホストで同じ扱い）
            (
                "add_relationship",
                new
                {
                    source_table = "TenantRegion",
                    source_columns = new[] { "TenantId" },
                    target_table = "TenantUser",
                    target_columns = new[] { "TenantId" },
                    relationship_type = "ManyToMany",
                }
            ),
            // 失敗系（配列の長さ不一致・片側のみ指定・複数一致の削除）
            (
                "add_relationship",
                new
                {
                    source_table = "TenantRegion",
                    source_columns = new[] { "TenantId", "RegionCode" },
                    target_table = "TenantAudit",
                    target_columns = new[] { "TenantRef" },
                    relationship_type = "OneToMany",
                }
            ),
            (
                "add_relationship",
                new
                {
                    source_table = "TenantRegion",
                    source_columns = new[] { "TenantId" },
                    target_table = "TenantAudit",
                    relationship_type = "OneToMany",
                }
            ),
            (
                "remove_relationship",
                new { source_table = "TenantRegion", target_table = "TenantUser" }
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
                    .ToList(),
                e.UniqueConstraints.Select(u => new UniqueSnap(
                        u.Name,
                        u.ColumnIds.Select(id => e.Columns.First(c => c.Id == id).Name).ToList()
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
                    .ToList(),
                e.UniqueConstraints.Select(u => new UniqueSnap(
                        // VM 側は未設定名を空文字で持つため、比較のため空文字へ揃える
                        u.Name
                            ?? string.Empty,
                        u.ColumnIds.Select(id => e.Columns.First(c => c.Id == id).Name).ToList()
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
                r.Type.ToString(),
                r.ConstraintName ?? string.Empty,
                r.ColumnPairs.Select(pair =>
                        $"{r.Source.Columns.First(c => c.Id == pair.SourceColumnId).Name}→{r.Target.Columns.First(c => c.Id == pair.TargetColumnId).Name}"
                    )
                    .ToList()
            ))
            .ToList();

    /// <summary>意味モデルからリレーションスナップショットを作る（端点は ID→テーブル名で解決）</summary>
    private static List<RelSnap> SnapshotRelationships(ErDiagram schema) =>
        schema
            .Relationships.Select(r =>
            {
                var source = schema.Entities.First(e => e.Id == r.SourceEntityId);
                var target = schema.Entities.First(e => e.Id == r.TargetEntityId);

                return new RelSnap(
                    source.TableName,
                    target.TableName,
                    r.Type.ToString(),
                    r.ConstraintName ?? string.Empty,
                    r.ColumnPairs.Select(pair =>
                            $"{source.Columns.First(c => c.Id == pair.SourceColumnId).Name}→{target.Columns.First(c => c.Id == pair.TargetColumnId).Name}"
                        )
                        .ToList()
                );
            })
            .ToList();
}
