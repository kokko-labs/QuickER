using QuickER.Dialects;
using QuickER.Model;
using static QuickER.Tests.Provider.DdlModel;
using E = QuickER.Tests.Provider.DialectDdlElements;
using F = QuickER.Tests.Provider.DialectDdlFacets;

namespace QuickER.Tests.Provider;

/// <summary>一意制約と主キーの制約行（5 方言の <c>*DdlGeneratorTests</c> の同形の組）</summary>
internal static partial class DialectDdlRows
{
    /// <summary>
    /// 一意制約の図（shops の id〔主キー〕・code・region。型は方言ごとの値）
    /// </summary>
    /// <param name="dialect">方言</param>
    /// <param name="withPrimaryKey">主キーの列を含めるか</param>
    /// <param name="constraint">一意制約を足す（表を受ける。null なら足さない）</param>
    private static ErDiagram Shops(
        string dialect,
        bool withPrimaryKey,
        Func<Entity, UniqueConstraint>? constraint
    )
    {
        var intType = ShopsIntType[dialect];
        var columns = new List<Column>();

        if (withPrimaryKey)
        {
            columns.Add(Pk("id", intType));
        }

        columns.Add(Col("code", VarcharType(dialect, 20), nullable: false));
        columns.Add(Col("region", VarcharType(dialect, 10), nullable: false));

        var entity = Table("shops", [.. columns]);

        if (constraint is not null)
        {
            entity.UniqueConstraints.Add(constraint(entity));
        }

        return Diagram(entity);
    }

    /// <summary>一意制約の図の主キーの型（SQLite は INT で、整数の表の値 INTEGER と違う）</summary>
    private static IReadOnlyDictionary<string, string> ShopsIntType =>
        new Dictionary<string, string>(IntType, StringComparer.Ordinal) { [Sl] = "INT" };

    private static IEnumerable<DialectDdlRow> UniqueRows() =>
        [
            DialectDdlRow
                .Ddl(
                    "ddl-unique-named-single-column-follows-pk",
                    "Build: 名前付き単一列 UNIQUE が PK の直後に出力される",
                    E.Unique,
                    F.StatementShape,
                    d =>
                        Shops(
                            d,
                            withPrimaryKey: true,
                            e => new UniqueConstraint
                            {
                                Name = "UQ_shops_code",
                                ColumnIds = [e.Columns[1].Id],
                            }
                        )
                )
                .ExpectAll(e =>
                    e.Lines("pk shops PK_shops (id),", "uq shops UQ_shops_code (code)*")
                        .NoLines("uq shops * (code),")
                        // 最後の制約行に余分なカンマが付かない（構造に写らない形でも見逃さない）
                        .NoText("*UNIQUE (*code*),")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-unique-unnamed-composite-synthesizes-name",
                    "Build: 名前なし複合 UNIQUE は UQ_テーブル_列… の合成名になる",
                    E.Unique,
                    F.ConstraintName,
                    d =>
                        Shops(
                            d,
                            withPrimaryKey: true,
                            // 宣言順は region → code（列定義順とは逆）
                            e => new UniqueConstraint
                            {
                                ColumnIds = [e.Columns[2].Id, e.Columns[1].Id],
                            }
                        )
                )
                .ExpectAll(e => e.Lines("uq shops UQ_shops_region_code (region, code)*")),
            DialectDdlRow
                .Ddl(
                    "ddl-unique-without-primary-key-keeps-comma",
                    "Build: PK なしでも UNIQUE 行の前の列にカンマが付く",
                    E.Unique,
                    F.StatementShape,
                    d =>
                        Shops(
                            d,
                            withPrimaryKey: false,
                            e => new UniqueConstraint { ColumnIds = [e.Columns[0].Id] }
                        )
                )
                .ExpectAll(e => e.NoText("*PRIMARY KEY*"))
                .ExpectWithDialect(
                    DialectNames.All,
                    (d, e) =>
                        e.Lines(
                            $"column shops.region {VarcharType(d, 10)} NOT NULL,",
                            "uq shops UQ_shops_code (code)*"
                        )
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-unique-none-emits-no-unique",
                    "Build: 一意制約が無ければ UNIQUE を出力しない",
                    E.Unique,
                    F.Omit,
                    d => Shops(d, withPrimaryKey: true, constraint: null)
                )
                .ExpectAll(e => e.NoText("*UNIQUE*")),
            DialectDdlRow
                .Ddl(
                    "ddl-pk-composite-follows-primary-key-column-ids",
                    "Build: 複合 PK は PrimaryKeyColumnIds の順で出力される",
                    E.PrimaryKey,
                    F.Order,
                    d =>
                    {
                        var a = Pk("a", IntType[d]);
                        var b = Pk("b", IntType[d]);
                        var entity = Table("pair", a, b);

                        // 列宣言順（a → b）とは逆の主キー順
                        entity.PrimaryKeyColumnIds = [b.Id, a.Id];
                        return Diagram(entity);
                    }
                )
                .ExpectAll(e => e.Lines("pk pair * (b, a)*")),
        ];
}
