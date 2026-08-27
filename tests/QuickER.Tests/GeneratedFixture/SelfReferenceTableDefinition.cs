using QuickER.Model;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// グラフ取得糖衣（<c>IncludeGraph()</c>）の edge-skip を実行時に観測するための自己参照テーブル
/// （<c>nodes</c>）を、複数のフィクスチャへ<b>同一の形</b>で足すための共有ビルダー。
/// </summary>
/// <remarks>
/// <para>
/// 生成側の閉包はルートからのパス上に既に現れたテーブルへの辺を展開しない（自己参照は辿ると無限に深くなる）。
/// 「ツリーに現れない」ことは生成テキストの単体テストが固定できるが、「<b>子行が実在していても</b>コレクションが
/// 空のまま返る」は実行器を通さないと分からない。その 1 点のためだけの最小テーブル（親キー＋ラベル）である。
/// </para>
/// <para>
/// 置き場は 3 フィクスチャ——<c>MultiTargetPortableFixture</c>（QuickER 版 Repository の sqlserver / sqlite）・
/// <c>InMemoryFixture</c>（インメモリ・値オブジェクト無効）・<c>QueryFixture</c>（QuickER 版 sqlite ＋ EF Core ＋
/// インメモリ・値オブジェクト有効）。<c>QueryFixture</c> へ置けるのは、値オブジェクトの型解決が
/// 「子側の列は参照先の列の型を共有する」に統一されて、自己参照 FK（<c>parent_node_id</c>）が主キーと同じ
/// <c>NodeIdValue</c> になったため（それ以前は FK プロパティの型が主キーの型と一致せず、EF Core のモデル検証が
/// <c>DbContext</c> ごと落ちていた）。
/// </para>
/// <para>
/// 図の要素 ID は決定的でなければ再生成時に差分が出るため固定 GUID を用いる（両フィクスチャの既存 ID とは
/// 衝突しないプレフィックス）。自己参照 FK は <b>ON DELETE NO ACTION</b>（SQL Server は自己参照の連鎖削除を
/// 許さない）で、列型は方言可搬な <c>int</c> / <c>nvarchar(50)</c> だけを使う。
/// </para>
/// </remarks>
public static class SelfReferenceTableDefinition
{
    /// <summary>自己参照テーブルの物理名</summary>
    public const string TableName = "nodes";

    private static readonly Guid NodeId = new("f2000000-0000-0000-0000-000000000001");
    private static readonly Guid NodePkColId = new("f2000000-0000-0000-0000-000000000002");
    private static readonly Guid NodeParentFkColId = new("f2000000-0000-0000-0000-000000000003");
    private static readonly Guid NodeLabelColId = new("f2000000-0000-0000-0000-000000000004");
    private static readonly Guid RelNodeChildren = new("f3000000-0000-0000-0000-000000000002");

    /// <summary>指定の図へ自己参照テーブルと自己参照リレーションを追加する（既存要素には触れない）</summary>
    public static void AddTo(ErDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);

        diagram.Entities.Add(
            new Entity
            {
                Id = NodeId,
                TableName = TableName,
                Columns =
                {
                    new Column
                    {
                        Id = NodePkColId,
                        Name = "node_id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Id = NodeParentFkColId,
                        Name = "parent_node_id",
                        DataType = "int",
                        IsForeignKey = true,
                        IsNullable = true,
                    },
                    new Column
                    {
                        Id = NodeLabelColId,
                        Name = "label",
                        DataType = "nvarchar(50)",
                        IsNullable = false,
                    },
                },
            }
        );

        diagram.Relationships.Add(
            new Relationship
            {
                Id = RelNodeChildren,
                Type = RelationshipType.OneToMany,
                SourceEntityId = NodeId,
                TargetEntityId = NodeId,
                ColumnPairs = [new(NodePkColId, NodeParentFkColId)],
                ConstraintName = "FK_nodes_nodes",
                OnDelete = ForeignKeyReferentialAction.NoAction,
                OnUpdate = ForeignKeyReferentialAction.NoAction,
            }
        );
    }
}
