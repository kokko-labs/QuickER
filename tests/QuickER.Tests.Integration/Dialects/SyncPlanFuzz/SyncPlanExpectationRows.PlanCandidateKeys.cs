using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using I = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationItems;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（候補キー：選んだ外部キーの追加の参照先が同期後の主キー・一意制約でなければ警告する・証明できない追加の並び・
/// 参照する列の集合が新しい主キーと違う付け直し）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    private const string AddForeignKeyNotOnCandidateKeyDescription =
        "候補キー: 選んだ外部キーの追加は、参照先が同期後の主キー・一意制約でなければ警告する";

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanCandidateKeyRows() =>
        [
            CandidateKeyRow(
                    Row(
                        "plan-candidate-key-add-fk-to-non-key-warns",
                        AddForeignKeyNotOnCandidateKeyDescription,
                        DialectNames.SqlServer
                    ),
                    w => [AddForeignKeyToCustomer(w, "code")]
                )
                .Expect(e => e.Warnings.Exact("ForeignKeyNotOnCandidateKey*")),
            CandidateKeyRow(
                    Row(
                        "plan-candidate-key-add-fk-to-unique-added-in-same-plan-no-warning",
                        AddForeignKeyNotOnCandidateKeyDescription,
                        DialectNames.SqlServer
                    ),
                    w =>
                        [
                            AddForeignKeyToCustomer(w, "code"),
                            I.Unique(
                                SchemaDiffKind.AddUniqueConstraint,
                                "customer",
                                null,
                                ["code"]
                            ),
                        ]
                )
                .Expect(e => e.Warnings.Empty()),
            CandidateKeyRow(
                    Row(
                        "plan-candidate-key-add-fk-to-pk-no-warning",
                        AddForeignKeyNotOnCandidateKeyDescription,
                        DialectNames.SqlServer
                    ),
                    w => [AddForeignKeyToCustomer(w, "id")]
                )
                .Expect(e => e.Warnings.Empty()),
            CandidateKeyRow(
                    Row(
                        "plan-candidate-key-add-fk-to-live-unique-no-warning",
                        AddForeignKeyNotOnCandidateKeyDescription,
                        DialectNames.SqlServer
                    ),
                    w => [AddForeignKeyToCustomer(w, "code")],
                    s => s.Unique("customer", "UQ_customer_code", "code")
                )
                .Expect(e => e.Warnings.Empty()),
            // 同じ計画で追加する表の一意制約も候補キーに数える（表の作成と一緒に作られる）
            .. new[] { false, true }.Select(withUnique =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    CandidateKeyRow(
                            Row(
                                $"plan-candidate-key-add-fk-to-new-table-{(withUnique ? "unique-no-warning" : "non-key-warns")}",
                                AddForeignKeyNotOnCandidateKeyDescription,
                                DialectNames.SqlServer
                            ),
                            w =>
                            {
                                var brandSchema = new ExpectationSchema();
                                var brand = brandSchema.Tbl(
                                    "brand",
                                    ("id", "INT", true),
                                    ("code", "INT", false)
                                );

                                if (withUnique)
                                {
                                    brandSchema.Unique("brand", "UQ_brand_code", "code");
                                }

                                return
                                [
                                    I.AddForeignKey(
                                        brand,
                                        w.Live["orders"],
                                        "FK_brand",
                                        new ForeignKeyColumnNamePair("code", "customer_code")
                                    ),
                                    new SchemaDiffItem
                                    {
                                        Kind = SchemaDiffKind.AddTable,
                                        TableName = "brand",
                                        Entity = brand,
                                        IsSelected = true,
                                    },
                                ];
                            }
                        )
                        .Expect(e =>
                        {
                            if (withUnique)
                            {
                                e.Warnings.Empty();
                            }
                            else
                            {
                                e.Warnings.Exact("ForeignKeyNotOnCandidateKey*");
                            }
                        })
            ),
            // 主キーを外す変更と一緒に選ぶと、主キーを参照する外部キーも候補キーを失う（差分と同じく、外部キーの
            // 参照先は図＝主キーを外した後の表）
            CandidateKeyRow(
                    Row(
                        "plan-candidate-key-add-fk-to-removed-pk-warns",
                        AddForeignKeyNotOnCandidateKeyDescription,
                        DialectNames.SqlServer
                    ),
                    w =>
                    {
                        var keyless = w.Live["customer"].Clone(preserveId: true);
                        keyless.Columns[0].IsPrimaryKey = false;
                        return
                        [
                            I.AddForeignKey(
                                keyless,
                                w.Live["orders"],
                                "FK_new",
                                new ForeignKeyColumnNamePair("id", "customer_code")
                            ),
                            I.AlterPk("customer", keyless),
                        ];
                    }
                )
                .Expect(e => e.Warnings.Contains("ForeignKeyNotOnCandidateKey*")),
            // DDL が暗黙コミットされる方言では、証明できない追加を外部キーの追加の最後へ回す（断られても、ほかの付け直しは
            // 済んでいる）。巻き戻る方言は選んだ順のまま
            Row(
                    "plan-candidate-key-unproven-fk-add-goes-last-where-ddl-commits",
                    AddForeignKeyNotOnCandidateKeyDescription,
                    DialectNames.MySql,
                    DialectNames.SqlServer
                )
                .Live(ExpectationScenarios.CustomerOrdersWithCodeAndKind)
                .Items(w =>
                    [
                        AddForeignKeyToCustomer(w, "code"),
                        I.AddForeignKey(
                            w.Live["customer"],
                            w.Live["orders"],
                            "FK_proven",
                            new ForeignKeyColumnNamePair("id", "customer_kind")
                        ),
                    ]
                )
                .Expect(e =>
                    e.For(
                            DialectNames.MySql,
                            x =>
                                x.Sections.Count("AddForeignKey: *", 1)
                                    .Sections.Single(
                                        "AddForeignKey: #1 AddForeignKey orders fk=FK_proven; #0 AddForeignKey orders fk=FK_new"
                                    )
                        )
                        .For(
                            DialectNames.SqlServer,
                            x =>
                                x.Sections.Count("AddForeignKey: *", 1)
                                    .Sections.Single(
                                        "AddForeignKey: #0 AddForeignKey orders fk=FK_new; #1 AddForeignKey orders fk=FK_proven"
                                    )
                        )
                ),
            // 同じ列集合の一意制約が 2 つあれば、1 つを外してももう 1 つが同期後の候補キーとして残る（照合は名前と列集合）
            CandidateKeyRow(
                    Row(
                        "plan-candidate-key-add-fk-to-twin-unique-one-dropped-no-warning",
                        AddForeignKeyNotOnCandidateKeyDescription,
                        DialectNames.SqlServer
                    ),
                    w =>
                        [
                            AddForeignKeyToCustomer(w, "code"),
                            I.Unique(
                                SchemaDiffKind.DropUniqueConstraint,
                                "customer",
                                "UQ1",
                                ["code"]
                            ),
                        ],
                    TwinCodeUniques
                )
                .Expect(e => e.Warnings.Empty()),
            CandidateKeyRow(
                    Row(
                        "plan-candidate-key-add-fk-to-twin-unique-both-dropped-warns",
                        AddForeignKeyNotOnCandidateKeyDescription,
                        DialectNames.SqlServer
                    ),
                    w =>
                        [
                            AddForeignKeyToCustomer(w, "code"),
                            I.Unique(
                                SchemaDiffKind.DropUniqueConstraint,
                                "customer",
                                "UQ1",
                                ["code"]
                            ),
                            I.Unique(
                                SchemaDiffKind.DropUniqueConstraint,
                                "customer",
                                "UQ2",
                                ["code"]
                            ),
                        ],
                    TwinCodeUniques
                )
                .Expect(e => e.Warnings.Contains("ForeignKeyNotOnCandidateKey*")),
            Row(
                    "plan-candidate-key-referenced-column-set-change-still-runs-where-ddl-commits",
                    "候補キー: 参照する列の集合が新しい主キーと違う付け直しは、MySQL でも警告して計画に入れる",
                    DialectNames.MySql
                )
                .Live(s => ExpectationScenarios.PairReference(s))
                // 主キー (a, b) を (a) へ縮める
                .Items(w =>
                {
                    var shrunk = w.Live["pair"].Clone(preserveId: true);
                    shrunk.Columns[1].IsPrimaryKey = false;
                    return [I.AlterPk("pair", shrunk)];
                })
                // 取り込んでいない索引で通る構成があるため止めない。付け直しは live の並びのまま
                .Expect(e =>
                    AddedForeignKeyPairs(
                        e.Sections.Contains("AlterPrimaryKey*: *")
                            .Warnings.Exact("ForeignKeyRebuildMayLoseCandidateKey*"),
                        "FK_ref",
                        "a>ra,b>rb"
                    )
                ),
        ];

    /// <summary>
    /// 候補キーの行に、DB 側の図（<see cref="ExpectationScenarios.CustomerOrdersWithCodeAndKind"/> に <paramref name="live"/> を
    /// 当てたもの）と手で組む項目を付ける
    /// </summary>
    private static ExpectationRow CandidateKeyRow(
        ExpectationRow row,
        Func<ExpectationWorld, IEnumerable<SchemaDiffItem>> items,
        Action<ExpectationSchema>? live = null
    ) =>
        row.Live(s =>
            {
                ExpectationScenarios.CustomerOrdersWithCodeAndKind(s);
                live?.Invoke(s);
            })
            .Items(items);

    /// <summary>customer の列 <paramref name="parentColumn"/> を orders.customer_code で参照する外部キー FK_new の追加</summary>
    private static SchemaDiffItem AddForeignKeyToCustomer(
        ExpectationWorld w,
        string parentColumn
    ) =>
        I.AddForeignKey(
            w.Live["customer"],
            w.Live["orders"],
            "FK_new",
            new ForeignKeyColumnNamePair(parentColumn, "customer_code")
        );

    /// <summary>customer.code に同じ列集合の一意制約 UQ1・UQ2 を足す</summary>
    private static void TwinCodeUniques(ExpectationSchema s)
    {
        s.Unique("customer", "UQ1", "code");
        s.Unique("customer", "UQ2", "code");
    }
}
