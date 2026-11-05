using System.Globalization;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.GeneratedFixture;
using QuickER.Tests.GeneratedPortableFixture;
using QuickER.Tests.GeneratedSyncFixture;
using E = QuickER.Tests.Provider.DialectDdlElements;
using F = QuickER.Tests.Provider.DialectDdlFacets;
using P = QuickER.Tests.Provider.DialectDdlPaths;

namespace QuickER.Tests.Provider;

/// <summary>
/// 書式のカルチャに依らない（DDL・同期スクリプト・生成日時の暦）。
/// カルチャの比較と生成日時の暦は全行にかかる自動の照合が行うので、行は入力だけを組み、
/// 例外を投げないことを期待に書く
/// </summary>
internal static partial class DialectDdlRows
{
    /// <summary>方言の型の選択肢に加えて流す型の表記（負のスケール・精度なし・単位語・配列など。元と同じ）</summary>
    private static string[] CuExtraTypeNotations =>
        [
            "numeric(10,-2)",
            "NUMBER(10,-2)",
            "NUMBER(*,2)",
            "decimal(18,4)",
            "decimal(38,10)",
            "numeric(18,0)",
            "timestamp(3)",
            "timestamp",
            "TIMESTAMP(9)",
            "VARCHAR2(50 CHAR)",
            "varchar(255)",
            "nvarchar(max)",
            "integer[]",
            "time(3) with time zone",
            "float(24)",
            "datetime2(3)",
            "bit(8)",
        ];

    /// <summary>図の対象 DBMS を方言に合わせる</summary>
    private static ErDiagram CuWithTarget(ErDiagram diagram, string dialect)
    {
        diagram.TargetDbms = DialectDdlAxis.Get(dialect).Provider.Name;
        return diagram;
    }

    /// <summary>方言の型の選択肢と追加の表記をすべて列に持つ図（説明・一意制約・外部キーつき）</summary>
    private static ErDiagram CuTypeChoices(string dialect)
    {
        var provider = DialectDdlAxis.Get(dialect).Provider;
        var parent = new Entity { TableName = "Items", Description = "Items İ ı I i" };
        var id = new Column
        {
            Name = "ID",
            DataType = provider.TypeCatalog.DefaultDataType,
            IsPrimaryKey = true,
            IsNullable = false,
        };
        parent.Columns.Add(id);

        foreach (
            var (type, index) in provider
                .TypeCatalog.DataTypes.Concat(CuExtraTypeNotations)
                .Select((type, index) => (type, index))
        )
        {
            parent.Columns.Add(
                new Column
                {
                    Name = "Column" + index.ToString(CultureInfo.InvariantCulture),
                    DataType = type,
                    IsNullable = index % 2 == 0,
                    Description = "Description " + index.ToString(CultureInfo.InvariantCulture),
                }
            );
        }

        parent.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [parent.Columns[1].Id, parent.Columns[2].Id] }
        );
        var child = new Entity { TableName = "ItemLines" };
        var childId = new Column
        {
            Name = "LineId",
            DataType = provider.TypeCatalog.DefaultDataType,
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var childFk = new Column
        {
            Name = "ItemId",
            DataType = provider.TypeCatalog.DefaultDataType,
            IsNullable = false,
        };
        child.Columns.Add(childId);
        child.Columns.Add(childFk);

        return new ErDiagram
        {
            TargetDbms = provider.Name,
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(id.Id, childFk.Id)],
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                },
            ],
        };
    }

    /// <summary>空の DB から図を作る同期の入力（全項目を選ぶ）</summary>
    private static DdlSyncDiff CuCreateDiff(ErDiagram diagram) =>
        new([], [], diagram.Entities, diagram.Relationships, CuSelectAll);

    /// <summary>図の全列（主キー以外）の型を方言の既定へ変える同期の入力（図の型を live、既定を target として差分を取る）</summary>
    private static DdlSyncDiff CuTypeChangeDiff(ErDiagram diagram, string dialect)
    {
        var provider = DialectDdlAxis.Get(dialect).Provider;
        var target = diagram.Entities.Select(e => e.Clone(preserveId: true)).ToList();

        foreach (var column in target.SelectMany(e => e.Columns).Where(c => !c.IsPrimaryKey))
        {
            column.DataType = provider.TypeCatalog.DefaultDataType;
        }

        return new DdlSyncDiff(
            diagram.Entities,
            diagram.Relationships,
            target,
            diagram.Relationships,
            CuSelectAll
        );
    }

    /// <summary>差分の全項目を選ぶ（選べない項目も含める）</summary>
    private static void CuSelectAll(IReadOnlyList<SchemaDiffItem> items)
    {
        foreach (var item in items)
        {
            item.IsSelected = true;
        }
    }

    /// <summary>流す図 4 つ（名前の部分は行の Id・図は方言を受けて作る）</summary>
    private static (string Name, string Description, Func<string, ErDiagram> Build)[] CuFigures =>
        [
            (
                "generated-fixture",
                "GeneratedFixture",
                d => CuWithTarget(GeneratedFixtureDefinition.Build(), d)
            ),
            (
                "portable-fixture",
                "PortableFixture",
                d => CuWithTarget(PortableFixtureDefinition.Build(), d)
            ),
            ("sync-fixture", "SyncFixture", d => CuWithTarget(SyncFixtureDefinition.Build(), d)),
            ("type-choices", "TypeChoices", CuTypeChoices),
        ];

    private static IEnumerable<DialectDdlRow> CultureRows()
    {
        foreach (var (name, description, build) in CuFigures)
        {
            var element = name == "type-choices" ? E.Type : E.Table;

            var ddl = DialectDdlRow.Ddl(
                $"ddl-{name}-culture-invariant",
                $"DDL は 5 方言とも、書式のカルチャに依らずインバリアントと同じ（{description}）",
                element,
                F.Culture,
                build
            );

            // 生成日時の暦（西暦・yyyy-MM-dd HH:mm:ss）は自動の照合が見る。その行は日時の行の存在も期待する
            yield return ddl.ExpectAll(e =>
            {
                e.NoException();

                if (name == "generated-fixture")
                {
                    e.Lines("generated-at");
                }
            });

            yield return DialectDdlRow
                .SyncDiff(
                    $"sync-add-table-{name}-culture-invariant",
                    $"同期スクリプト（空の DB から図を作る）は 5 方言とも、書式のカルチャに依らずインバリアントと同じ（{description}）",
                    P.SyncAddTable,
                    element,
                    F.Culture,
                    d => CuCreateDiff(build(d))
                )
                .ExpectAll(e => e.NoException());

            yield return DialectDdlRow
                .SyncDiff(
                    $"sync-other-{name}-type-change-culture-invariant",
                    $"同期スクリプト（全列の型を方言の既定へ変える）は 5 方言とも、書式のカルチャに依らずインバリアントと同じ（{description}）",
                    P.SyncOther,
                    E.Type,
                    F.Culture,
                    d => CuTypeChangeDiff(build(d), d)
                )
                .ExpectAll(e => e.NoException());
        }
    }
}
