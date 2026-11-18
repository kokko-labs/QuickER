using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（項目を外さずに積む警告の並びと重ね方：規則どうしの積む順と、同じ値の警告を重ねて積む種別）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanWarningOrderRows() =>
        [
            // 周が止まったときの状態から決める SET NULL の警告は、同期後の状態の差として足す項目（列と一緒に消える一意制約）の
            // 警告より前に積む
            Row(
                    "plan-warning-order-set-null-add-before-unique-dropped-with-column",
                    "警告の並び: NOT NULL の子の列への SET NULL の外部キーの追加の警告は、列と一緒に消える一意制約の警告より前に並ぶ（SET NULL を作れる方言）",
                    DialectNames.PostgreSql
                )
                .Live(s =>
                {
                    s.Table("wo_p", S.Pk("id", "integer"));
                    s.Table(
                        "wo_c",
                        S.Pk("id", "integer"),
                        S.NotNull("p_id", "integer"),
                        S.Nullable("x", "integer"),
                        S.Nullable("y", "integer")
                    );
                    s.Unique("wo_c", "uq_wo_c_xy", "x", "y");
                })
                .Target(s =>
                {
                    s.Table("wo_p", S.Pk("id", "integer"));
                    s.Table(
                        "wo_c",
                        S.Pk("id", "integer"),
                        S.NotNull("p_id", "integer"),
                        S.Nullable("x", "integer")
                    );
                    s.Fk(
                        "wo_p",
                        "wo_c",
                        "fk_wo_c_p",
                        ForeignKeyReferentialAction.SetNull,
                        ("id", "p_id")
                    );
                })
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind is SchemaDiffKind.DropColumn or SchemaDiffKind.AddForeignKey;
                    }
                })
                .Expect(e =>
                    e.Warnings.Exact(
                            "SetNullOnNotNullColumn(wo_c): fk_wo_c_p (p_id)",
                            "UniqueConstraintDroppedWithColumn(wo_c): uq_wo_c_xy (x, y)"
                        )
                        .Excluded.Empty()
                ),
            // 一意制約の削除が外部キーを壊しうる警告は、同じ値（親の表・外部キーの名前）を重ねない。同じ外部キーを支えうる一意制約を
            // 2 つ外しても、その外部キーの警告は 1 件（規則は削除ごとに返し、種別で重ね方を分ける AssessmentRules.Add が 1 件にする）
            Row(
                    "plan-warning-unique-drop-may-break-fk-named-once-for-two-dropped-uniques",
                    "同じ外部キーを支えうる一意制約を 2 つ外しても、外部キーを壊しうる警告はその外部キーにつき 1 件",
                    DialectNames.SqlServer
                )
                .Live(s => TwinUniqueParentSchema(s, withUniques: true))
                .Target(s => TwinUniqueParentSchema(s, withUniques: false))
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected = item.Kind == SchemaDiffKind.DropUniqueConstraint;
                    }
                })
                .Expect(e =>
                    e.Warnings.Exact("UniqueConstraintDropMayBreakForeignKey(wo_tp): fk_wo_tc_tp")
                        .Excluded.Empty()
                ),
            // 主キーが置き換える一意制約の警告（持てない方言＝Oracle）は、DB にある SET NULL の外部キーの子の列の NOT NULL 化の
            // 警告より前に積む。後ろの 2 件は、断られうる文（NOT NULL 化）の前で外す制約の名指し
            Row(
                    "plan-warning-order-unique-replaced-by-pk-before-set-null-live-fk",
                    "警告の並び: 主キーが置き換える一意制約の警告は、DB にある SET NULL の外部キーの子の列の NOT NULL 化の警告より前に並ぶ（Oracle）",
                    DialectNames.Oracle
                )
                .Live(s => ReplacedUniqueAndSetNullChildSchema(s, moved: false))
                .Target(s => ReplacedUniqueAndSetNullChildSchema(s, moved: true))
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind
                                is SchemaDiffKind.AlterPrimaryKey
                                    or SchemaDiffKind.AlterColumn;
                    }
                })
                .Expect(e =>
                    e.Warnings.Exact(
                            "UniqueConstraintReplacedByPrimaryKey(wo_k): uq_wo_k_u (u)",
                            "SetNullOnNotNullColumn(wo_kc): fk_wo_kc_kp (p_id)",
                            "ConstraintRebuildAroundRowDependentChange(wo_k): uq_wo_k_u *",
                            "ConstraintRebuildAroundRowDependentChange(wo_k): PRIMARY KEY *"
                        )
                        .Excluded.Empty()
                ),
            // DB にある SET NULL の外部キーの子の列の NOT NULL 化の警告は、一意制約の削除が外部キーを壊しうる警告より前に積む
            Row(
                    "plan-warning-order-set-null-live-fk-before-unique-drop-breaking-fk",
                    "警告の並び: DB にある SET NULL の外部キーの子の列の NOT NULL 化の警告は、一意制約の削除が外部キーを壊しうる警告より前に並ぶ",
                    DialectNames.PostgreSql
                )
                .Live(s => SetNullChildAndReferencedUniqueSchema(s, changed: false))
                .Target(s => SetNullChildAndReferencedUniqueSchema(s, changed: true))
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind
                                is SchemaDiffKind.AlterColumn
                                    or SchemaDiffKind.DropUniqueConstraint;
                    }
                })
                .Expect(e =>
                    e.Warnings.Exact(
                            "SetNullOnNotNullColumn(wo_sc): fk_wo_sc_sp (p_id)",
                            "UniqueConstraintDropMayBreakForeignKey(wo_up): fk_wo_uc_up"
                        )
                        .Excluded.Empty()
                ),
            // 足す外部キーの参照先が候補キーでない警告は、SET DEFAULT と NOT NULL の子の列の警告（SQL Server）より前に積む
            Row(
                    "plan-warning-order-fk-not-on-candidate-key-before-set-default",
                    "警告の並び: 足す外部キーの参照先が候補キーでない警告は、SET DEFAULT と NOT NULL の子の列の警告より前に並ぶ（SQL Server）",
                    DialectNames.SqlServer
                )
                .Live(s => SetDefaultToNonKeySchema(s, withForeignKey: false))
                .Target(s => SetDefaultToNonKeySchema(s, withForeignKey: true))
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected = item.Kind == SchemaDiffKind.AddForeignKey;
                    }
                })
                .Expect(e =>
                    e.Warnings.Exact(
                            "ForeignKeyNotOnCandidateKey(wo_dc): fk_wo_dc_dp",
                            "SetDefaultOnNotNullColumn(wo_dc): fk_wo_dc_dp (p_code)"
                        )
                        .Excluded.Empty()
                ),
            // 付け直せない外部キー（SET NULL で、子の列が同期後に NOT NULL。作れない方言）は外し直さないので、参照先が候補キーで
            // なくなっても、付け直しの候補キーの警告は出さない（SET NULL の名指しだけ）
            Row(
                    "plan-warning-set-null-blocked-rebuild-is-not-warned-for-candidate-key",
                    "付け直せない SET NULL の外部キーは、参照先の主キーが移って候補キーでなくなっても、候補キーの警告を出さない（SQL Server）",
                    DialectNames.SqlServer
                )
                .Live(s => SetNullChildOfMovedPrimaryKeySchema(s, changed: false))
                .Target(s => SetNullChildOfMovedPrimaryKeySchema(s, changed: true))
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind
                                is SchemaDiffKind.AlterPrimaryKey
                                    or SchemaDiffKind.AlterColumn;
                    }
                })
                .Expect(e =>
                    e.Warnings.Exact("SetNullOnNotNullColumn(wo_bc): fk_wo_bc_bp (p_id)")
                        .Excluded.Empty()
                ),
            // 既定値のある列の型の種類の変更の警告は、説明の表せない文字の警告（MySQL）より前に積む
            Row(
                    "plan-warning-order-default-type-kind-change-before-description-characters",
                    "警告の並び: 既定値のある列の型の種類の変更の警告は、説明の表せない文字の警告より前に並ぶ（MySQL）",
                    DialectNames.MySql
                )
                .Live(s => s.Table("wo_m", S.Pk("id", "int"), S.NotNull("v", "varchar(20)")))
                .Target(s =>
                    s.Table("wo_m", S.Pk("id", "int"), S.NotNull("v", "int")).Description =
                        "a\U0001F600b"
                )
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind
                                is SchemaDiffKind.AlterColumn
                                    or SchemaDiffKind.SetTableDescription;
                    }
                })
                .Context(w => new SyncPlanContext
                {
                    LiveEntities = w.Live.Entities,
                    LiveRelationships = w.Live.Relationships,
                    ColumnDefaults = [new SchemaColumnDefault("wo_m", "v", "'abc'")],
                })
                .Expect(e =>
                    e.Warnings.Exact(
                            "DefaultOnColumnTypeKindChange(wo_m): v (varchar(20) \u2192 int)",
                            "DescriptionCharactersReplaced(wo_m): wo_m"
                        )
                        .Excluded.Empty()
                ),
        ];

    /// <summary>
    /// 表 wo_k(id, u) の列 u に一意制約を持ち、主キーを id から u へ移す図と、SET NULL の外部キーの子の列 wo_kc.p_id を NOT NULL にする図
    /// （<paramref name="moved"/> が真で変えた後。一意制約は図に残す＝主キーと同じ列集合の一意制約を持てない方言は比べない）
    /// </summary>
    private static void ReplacedUniqueAndSetNullChildSchema(S s, bool moved)
    {
        var id = moved ? S.NotNull("id", "NUMBER(10)") : S.Pk("id", "NUMBER(10)");
        var u = moved ? S.Pk("u", "NUMBER(10)") : S.NotNull("u", "NUMBER(10)");
        s.Table("wo_k", id, u);
        s.Unique("wo_k", "uq_wo_k_u", "u");
        s.Table("wo_kp", S.Pk("id", "NUMBER(10)"));
        s.Table(
            "wo_kc",
            S.Pk("id", "NUMBER(10)"),
            moved ? S.NotNull("p_id", "NUMBER(10)") : S.Nullable("p_id", "NUMBER(10)")
        );
        s.Fk("wo_kp", "wo_kc", "fk_wo_kc_kp", ForeignKeyReferentialAction.SetNull, ("id", "p_id"));
    }

    /// <summary>
    /// SET NULL の外部キーの子の列 wo_sc.p_id を NOT NULL にし、外部キー fk_wo_uc_up が参照する一意制約 uq_wo_up_u を外す図
    /// （<paramref name="changed"/> が真で変えた後）
    /// </summary>
    private static void SetNullChildAndReferencedUniqueSchema(S s, bool changed)
    {
        s.Table("wo_sp", S.Pk("id", "integer"));
        s.Table(
            "wo_sc",
            S.Pk("id", "integer"),
            changed ? S.NotNull("p_id", "integer") : S.Nullable("p_id", "integer")
        );
        s.Fk("wo_sp", "wo_sc", "fk_wo_sc_sp", ForeignKeyReferentialAction.SetNull, ("id", "p_id"));
        s.Table("wo_up", S.Pk("id", "integer"), S.NotNull("u", "integer"));
        s.Table("wo_uc", S.Pk("id", "integer"), S.Nullable("p_u", "integer"));

        if (!changed)
        {
            s.Unique("wo_up", "uq_wo_up_u", "u");
        }

        s.Fk("wo_up", "wo_uc", "fk_wo_uc_up", ("u", "p_u"));
    }

    /// <summary>
    /// 親 wo_bp の主キー id を SET NULL の外部キーで参照する子 wo_bc を持ち、親の主キーを k へ移して子の列 p_id を NOT NULL にする図
    /// （<paramref name="changed"/> が真で変えた後。外部キーは変えない）
    /// </summary>
    private static void SetNullChildOfMovedPrimaryKeySchema(S s, bool changed)
    {
        s.Table(
            "wo_bp",
            changed ? S.NotNull("id", "int") : S.Pk("id", "int"),
            changed ? S.Pk("k", "int") : S.NotNull("k", "int")
        );
        s.Table(
            "wo_bc",
            S.Pk("id", "int"),
            changed ? S.NotNull("p_id", "int") : S.Nullable("p_id", "int")
        );
        s.Fk("wo_bp", "wo_bc", "fk_wo_bc_bp", ForeignKeyReferentialAction.SetNull, ("id", "p_id"));
    }

    /// <summary>
    /// 親 wo_dp の候補キーでない列 code を、子 wo_dc の NOT NULL の列 p_code が ON DELETE SET DEFAULT で参照する外部キーを持つ図
    /// （<paramref name="withForeignKey"/> が偽なら外部キーを持たない）
    /// </summary>
    private static void SetDefaultToNonKeySchema(S s, bool withForeignKey)
    {
        s.Table("wo_dp", S.Pk("id", "int"), S.NotNull("code", "int"));
        s.Table("wo_dc", S.Pk("id", "int"), S.NotNull("p_code", "int"));

        if (withForeignKey)
        {
            s.Fk(
                "wo_dp",
                "wo_dc",
                "fk_wo_dc_dp",
                ForeignKeyReferentialAction.SetDefault,
                ("code", "p_code")
            );
        }
    }

    /// <summary>
    /// 親 wo_tp の列 u に同じ列の一意制約を 2 つ持ち（<paramref name="withUniques"/> が偽なら持たない）、子 wo_tc がその列を参照する図
    /// </summary>
    private static void TwinUniqueParentSchema(S s, bool withUniques)
    {
        s.Table("wo_tp", S.Pk("id", "int"), S.NotNull("u", "int"));
        s.Table("wo_tc", S.Pk("id", "int"), S.Nullable("p_u", "int"));

        if (withUniques)
        {
            s.Unique("wo_tp", "uq_wo_tp_u1", "u");
            s.Unique("wo_tp", "uq_wo_tp_u2", "u");
        }

        s.Fk("wo_tp", "wo_tc", "fk_wo_tc_tp", ("u", "p_u"));
    }
}
