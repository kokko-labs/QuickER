using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 差分と計画の期待の行（同じテーブルの組に名前の無いリレーションが 2 本ある図で、差分が DDL と同じ一意な外部キー名を使う：
/// 既定名 FK_{子}_{親} だけでは 2 本が同じ名前になるので、2 本目に子の列名を足す。外部キーを作らない名前の無い多対多は名前を取らない）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>名前の無い 1 本目に DDL が付ける名前</summary>
    private const string UnnamedBillName = "FK_orders_customers";

    /// <summary>名前の無い 2 本目に DDL が付ける名前</summary>
    private const string UnnamedShipName = "FK_orders_customers_ship_customer_id";

    /// <summary>外部キーの名前を保存する 4 方言</summary>
    private static string[] PersistedForeignKeyNameDialects =>
        [DialectNames.SqlServer, DialectNames.PostgreSql, DialectNames.MySql, DialectNames.Oracle];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> UnnamedForeignKeyRows() =>
        [
            Row(
                    "diff-unnamed-fk-same-pair-twice-matches-live-named-by-ddl",
                    "名前の無い同じ組の外部キー 2 本の図は、DDL が付けた名前の live と外部キーの差分を出さない（名前を保存する 4 方言）",
                    PersistedForeignKeyNameDialects
                )
                // DB 側は DDL が付けた名前をそのまま持ち帰った図（取込の結果に当たる）
                .Live(s => UnnamedPairSchema(s, UnnamedBillName, UnnamedShipName))
                .Target(s => UnnamedPairSchema(s, null, null))
                .Expect(e =>
                    e.Diff.Absent("AddForeignKey *", "DropForeignKey *", "AlterForeignKey *")
                ),
            Row(
                    "diff-unnamed-fk-second-referential-action-change-replaces-under-same-name",
                    "名前の無い 2 本目の参照アクションを変えると、同じ名前で外して付け直す外部キー変更になる（名前を保存する 4 方言）",
                    PersistedForeignKeyNameDialects
                )
                .Live(s => UnnamedPairSchema(s, UnnamedBillName, UnnamedShipName))
                .Target(s =>
                    UnnamedPairSchema(s, null, null).OnDelete = ForeignKeyReferentialAction.Cascade
                )
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected = true;
                    }
                })
                // 外すのも付けるのも 2 本目の名前（既定名で付け直すと 1 本目と同じ名前になる）
                .Expect(e =>
                    e.Diff.Exact("AlterForeignKey *")
                        .Items.Count("DropForeignKey > *", 1)
                        .Items.Single($"DropForeignKey > * fk={UnnamedShipName} sel=*")
                        .Items.Count("AddForeignKey > *", 1)
                        .Items.Single($"AddForeignKey > * fk={UnnamedShipName} sel=*")
                ),
            Row(
                    "diff-unnamed-fk-many-to-many-first-matches-live-with-default-name",
                    "名前の無い多対多が先に並ぶ図は、既定名で外部キーを作った live と外部キーの差分を出さない（名前を保存する 4 方言）",
                    PersistedForeignKeyNameDialects
                )
                .Live(s => UnnamedPairSchema(s, UnnamedBillName, UnnamedShipName))
                .Target(s =>
                {
                    UnnamedPairSchema(s, null, null);

                    // DDL はこの関係に外部キーを作らないので、既定名を取らせてはいけない
                    s.Relationships.Insert(
                        0,
                        new Relationship
                        {
                            SourceEntityId = s["customers"].Id,
                            TargetEntityId = s["orders"].Id,
                            Type = RelationshipType.ManyToMany,
                        }
                    );
                })
                .Expect(e =>
                    e.Diff.Absent("AddForeignKey *", "DropForeignKey *", "AlterForeignKey *")
                ),
            .. LongDefaultForeignKeyNameRows(),
            .. AddedSecondForeignKeyWarningRows(),
        ];

    /// <summary>
    /// 計画の期待の行（名前の無い同じ組の 2 本目の外部キーを図で足し、計画が警告する形：警告は足す外部キーの名前＝スクリプトが使う名前
    /// <see cref="UnnamedShipName"/> を名指しする。DB にある 1 本目の名前 <see cref="UnnamedBillName"/> を名指ししない）
    /// </summary>
    /// <remarks>
    /// 警告が名前を規約名（FK_{子}_{親}）から作り直すと、2 本目は 1 本目と同じ名前になり、DB にある無関係な外部キーを名指しした
    /// （P5-2 の試運転の後に静的な規則 W1 が見つけた。型の食い違い・候補キーでない参照・SET NULL・SET DEFAULT の 4 つの警告）
    /// </remarks>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > AddedSecondForeignKeyWarningRows() =>
        [
            Row(
                    "plan-warning-names-added-fk-type-mismatch",
                    "型の食い違いで外す名前の無い 2 本目の外部キーの追加は、足す外部キーの名前で名指しする（名前を保存する 4 方言）",
                    PersistedForeignKeyNameDialects
                )
                .Live(s => AddedSecondForeignKeySchema(s, UnnamedBillName, "int", null))
                .Target(s => AddedSecondForeignKeySchema(s, null, "decimal(12,2)", ship => { }))
                .Select(SelectAll)
                .Expect(e =>
                    e.Warnings.Single($"ForeignKeyColumnTypeMismatch(orders): {UnnamedShipName}")
                        .Excluded.Single("AddForeignKey *")
                ),
            Row(
                    "plan-warning-names-added-fk-not-on-candidate-key",
                    "候補キーでない列を参照する名前の無い 2 本目の外部キーの追加は、足す外部キーの名前で名指しする（名前を保存する 4 方言）",
                    PersistedForeignKeyNameDialects
                )
                .Live(s => AddedSecondForeignKeySchema(s, UnnamedBillName, "int", null))
                .Target(s =>
                    AddedSecondForeignKeySchema(s, null, "int", ship => { }, parentColumn: "code")
                )
                .Select(SelectAll)
                .Expect(e =>
                    e.Warnings.Single($"ForeignKeyNotOnCandidateKey(orders): {UnnamedShipName}")
                ),
            Row(
                    "plan-warning-names-added-fk-set-null",
                    "NOT NULL の子の列への SET NULL の名前の無い 2 本目の外部キーの追加は、足す外部キーの名前で名指しする（名前を保存する 4 方言）",
                    PersistedForeignKeyNameDialects
                )
                .Live(s => AddedSecondForeignKeySchema(s, UnnamedBillName, "int", null))
                .Target(s =>
                    AddedSecondForeignKeySchema(
                        s,
                        null,
                        "int",
                        ship => ship.OnDelete = ForeignKeyReferentialAction.SetNull
                    )
                )
                .Select(SelectAll)
                .Expect(e =>
                    e.Warnings.Single(
                        $"SetNullOnNotNullColumn(orders): {UnnamedShipName} (ship_customer_id)"
                    )
                ),
            Row(
                    "plan-warning-names-added-fk-set-default",
                    "NOT NULL の子の列への SET DEFAULT の名前の無い 2 本目の外部キーの追加は、足す外部キーの名前で名指しする（SQL Server）",
                    DialectNames.SqlServer
                )
                .Live(s => AddedSecondForeignKeySchema(s, UnnamedBillName, "int", null))
                .Target(s =>
                    AddedSecondForeignKeySchema(
                        s,
                        null,
                        "int",
                        ship => ship.OnDelete = ForeignKeyReferentialAction.SetDefault
                    )
                )
                .Select(SelectAll)
                .Expect(e =>
                    e.Warnings.Single(
                        $"SetDefaultOnNotNullColumn(orders): {UnnamedShipName} (ship_customer_id)"
                    )
                ),
        ];

    /// <summary>
    /// 1 本目（bill）の外部キーを持ち、2 本目（ship）の外部キーを <paramref name="ship"/> があれば張る図（親の列は既定で主キー
    /// <c>id</c>。<paramref name="parentColumn"/> で候補キーでない列 <c>code</c> を参照させる）
    /// </summary>
    private static void AddedSecondForeignKeySchema(
        S s,
        string? billName,
        string shipType,
        System.Action<Relationship>? ship,
        string parentColumn = "id"
    )
    {
        s.Table("customers", S.Pk("id", "int"), S.NotNull("code", "int"));
        s.Table(
            "orders",
            S.Pk("id", "int"),
            S.NotNull("bill_customer_id", "int"),
            S.NotNull("ship_customer_id", shipType)
        );
        s.Fk("customers", "orders", billName, ("id", "bill_customer_id"));

        if (ship is not null)
        {
            ship(s.Fk("customers", "orders", null, (parentColumn, "ship_customer_id")));
        }
    }

    /// <summary>方言の上限（行の能力の宣言）</summary>
    /// <summary>
    /// 長い名前の図（<see cref="LongNameDiagrams.Build"/>）の表名・列名の先頭を大文字にしたもの（図の綴りと、小文字で返す DB の
    /// 綴りが違う形）
    /// </summary>
    private static ErDiagram MixedCaseLongNames(string capability)
    {
        var diagram = LongNameDiagrams.Build(LimitOf(capability), capability);

        foreach (var entity in diagram.Entities)
        {
            entity.TableName = char.ToUpperInvariant(entity.TableName[0]) + entity.TableName[1..];

            foreach (var column in entity.Columns)
            {
                column.Name = char.ToUpperInvariant(column.Name[0]) + column.Name[1..];
            }
        }

        return diagram;
    }

    private static IdentifierLengthLimit LimitOf(string capability) =>
        SyncPlanExpectationCapabilities.Get(capability).IdentifierLengthLimit;

    /// <summary>外部キーの既定名の解決（方言の上限に収めた名前と縮める前の名前）</summary>
    private static ResolvedForeignKeyName ForeignKeyNameOf(
        ErDiagram diagram,
        Relationship relationship,
        IdentifierLengthLimit limit
    ) =>
        ConstraintNames.ResolveForeignKeyNameDetails(
            diagram.Entities,
            diagram.Relationships,
            limit
        )[relationship];

    /// <summary>
    /// 差分の期待の行（既定の制約名が方言の上限を超える図＝<see cref="LongNameDiagrams"/>：DDL が縮めた名前で作った外部キーを図の名前の無い
    /// 外部キーと同じとみなす・PostgreSQL は旧版が 63 バイトで切り詰めた名前も同じとみなし、利用者が付けた名前はそうみなさない）。DB 側の図は
    /// 取込が返すのと同じ形（<see cref="ExpectationScenarios.AsImported"/>）
    /// </summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > LongDefaultForeignKeyNameRows() =>
        [
            Row(
                    "diff-long-default-fk-names-match-shortened-live",
                    "DDL が縮めた既定名で作った外部キー・一意制約の live と、名前の無い図の差分は空（上限のある 4 方言）",
                    DialectNames.SqlServer,
                    DialectNames.PostgreSql,
                    DialectNames.MySql,
                    DialectNames.Oracle
                )
                .LiveWith(w =>
                {
                    var diagram = LongNameDiagrams.Build(LimitOf(w.Capability), w.Capability);
                    ExpectationScenarios.AsImported(
                        w.Live,
                        diagram,
                        rel => ForeignKeyNameOf(diagram, rel, LimitOf(w.Capability)).Name
                    );
                })
                .TargetWith(w => RealCrossDialectScenarios.LongNames(w.Target, w.Capability))
                .Expect(e => e.Diff.Empty()),
            // 大文字小文字を畳む DB（MySQL の lower_case_table_names=1 など）は、図の綴りで DDL が作った表・列を小文字で返し、制約の
            // 名前は DDL が付けたまま返す。差分は名前の無い制約の既定名を図の綴りから求める（live の綴りへ揃えた複製から求め直すと、
            // 縮めた既定名のハッシュが変わり、偽の外部キーの追加と消えない削除が出る）
            Row(
                    "diff-long-default-fk-name-with-case-folded-live-matches-ddl",
                    "表名・列名を小文字で返す DB の、図の綴りで DDL が縮めた既定名を付けた外部キー・一意制約の live と、名前の無い大文字小文字混じりの図の差分は空（5 方言）",
                    AllDialects
                )
                .LiveWith(w =>
                {
                    var diagram = MixedCaseLongNames(w.Capability);
                    ExpectationScenarios.AsImported(
                        w.Live,
                        diagram,
                        rel => ForeignKeyNameOf(diagram, rel, LimitOf(w.Capability)).Name
                    );

                    foreach (var entity in w.Live.Entities)
                    {
                        entity.TableName = entity.TableName.ToLowerInvariant();

                        foreach (var column in entity.Columns)
                        {
                            column.Name = column.Name.ToLowerInvariant();
                        }
                    }
                })
                .TargetWith(w =>
                    RealCrossDialectScenarios.Adopt(w.Target, MixedCaseLongNames(w.Capability))
                )
                .Expect(e => e.Diff.Empty()),
            Row(
                    "diff-long-default-pg-legacy-truncated-fk-name-is-absorbed",
                    "PostgreSQL: 旧版が縮める前の既定名を 63 バイトで切り詰めて作った外部キーの live と、名前の無い図の差分は空（切り詰めるとみなさない宣言では別の外部キーに見える＝対照）",
                    DialectNames.PostgreSql,
                    SyncPlanExpectationCapabilities.PostgreSqlWithoutTruncation
                )
                .LiveWith(w =>
                {
                    var limit = LimitOf(DialectNames.PostgreSql);
                    var diagram = LongNameDiagrams.BuildSingleForeignKey(
                        limit.Max,
                        DialectNames.PostgreSql
                    );
                    ExpectationScenarios.AsImported(
                        w.Live,
                        diagram,
                        rel => limit.Truncate(ForeignKeyNameOf(diagram, rel, limit).UnshortenedName)
                    );
                })
                .TargetWith(w =>
                    RealCrossDialectScenarios.Adopt(
                        w.Target,
                        LongNameDiagrams.BuildSingleForeignKey(
                            LimitOf(DialectNames.PostgreSql).Max,
                            DialectNames.PostgreSql
                        )
                    )
                )
                .Expect(e =>
                    e.For(DialectNames.PostgreSql, x => x.Diff.Empty())
                        .For(
                            SyncPlanExpectationCapabilities.PostgreSqlWithoutTruncation,
                            x =>
                                x.Diff.Unordered(
                                    NotAbsorbedLines(
                                        LongNameDiagrams.BuildSingleForeignKey(
                                            LimitOf(DialectNames.PostgreSql).Max,
                                            DialectNames.PostgreSql
                                        ),
                                        "parent_id"
                                    )
                                )
                        )
                ),
            Row(
                    "diff-pg-user-named-fk-equal-to-shortened-default-is-not-absorbed",
                    "PostgreSQL: 利用者が付けた外部キーの名前は、旧版の切り詰めた名前の live と同じ外部キーとみなさない",
                    DialectNames.PostgreSql
                )
                .LiveWith(w =>
                {
                    var limit = LimitOf(DialectNames.PostgreSql);
                    var diagram = LongNameDiagrams.Build(limit, DialectNames.PostgreSql);
                    ExpectationScenarios.AsImported(
                        w.Live,
                        diagram,
                        rel => limit.Truncate(ForeignKeyNameOf(diagram, rel, limit).UnshortenedName)
                    );
                })
                .TargetWith(w =>
                {
                    var limit = LimitOf(DialectNames.PostgreSql);
                    var diagram = LongNameDiagrams.Build(limit, DialectNames.PostgreSql);
                    var names = diagram.Relationships.ToDictionary(
                        rel => rel,
                        rel => ForeignKeyNameOf(diagram, rel, limit).Name
                    );

                    // 縮めた既定名を、利用者が付けた名前として図に保存する（旧版の切り詰めた名前とは違う）
                    foreach (var relationship in diagram.Relationships)
                    {
                        relationship.ConstraintName = names[relationship];
                    }

                    RealCrossDialectScenarios.Adopt(w.Target, diagram);
                })
                .Expect(e =>
                    e.Diff.Unordered(
                        NotAbsorbedLines(
                            LongNameDiagrams.Build(
                                LimitOf(DialectNames.PostgreSql),
                                DialectNames.PostgreSql
                            ),
                            "bill_id",
                            "ship_id"
                        )
                    )
                ),
        ];

    /// <summary>
    /// PostgreSQL で旧版の切り詰めた名前の外部キーを吸収しないときの差分の行：外部キーごとに、縮めた名前での追加（選ぶ）と旧版の切り詰めた
    /// 名前の削除（選ばない）（<paramref name="childColumns"/> は図の外部キーの並びの子の列）
    /// </summary>
    private static string[] NotAbsorbedLines(ErDiagram diagram, params string[] childColumns)
    {
        var limit = LimitOf(DialectNames.PostgreSql);
        var child = diagram.Entities[1].TableName;

        return
        [
            .. diagram.Relationships.Select(
                (rel, i) =>
                    $"AddForeignKey {child}.{childColumns[i]} fk={ForeignKeyNameOf(diagram, rel, limit).Name} sel=1 * pairs=(id>{childColumns[i]}) *"
            ),
            .. diagram.Relationships.Select(
                (rel, i) =>
                    $"DropForeignKey {child} fk={limit.Truncate(ForeignKeyNameOf(diagram, rel, limit).UnshortenedName)} sel=0 * pairs=(id>{childColumns[i]}) *"
            ),
        ];
    }

    /// <summary>
    /// customers(id) と、customers を 2 回参照する orders(id, bill_customer_id, ship_customer_id) の図を足す（外部キーの名前は指定した
    /// もの）。2 本目の外部キー（ship_customer_id）を返す
    /// </summary>
    private static Relationship UnnamedPairSchema(S s, string? billName, string? shipName)
    {
        s.Table("customers", S.Pk("id", "int"));
        s.Table(
            "orders",
            S.Pk("id", "int"),
            S.NotNull("bill_customer_id", "int"),
            S.NotNull("ship_customer_id", "int")
        );
        s.Fk("customers", "orders", billName, ("id", "bill_customer_id"));
        return s.Fk("customers", "orders", shipName, ("id", "ship_customer_id"));
    }
}
