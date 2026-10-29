using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>決定的な行の入力（流す方言と、方言からケースを作る関数）</summary>
/// <param name="Dialect">流す方言（<see cref="DialectNames"/> の値）</param>
/// <param name="Create">方言の部品からケースを作る</param>
/// <param name="RegularRun">
/// 実 DB の総当たりで普段も流すか（偽なら網と夜間の実行だけで流す＝形の行の 2 因子の網羅に要らない行）
/// </param>
internal sealed record SyncPlanScenarioInput(
    string Dialect,
    Func<SyncPlanFuzzDialect, ISyncPlanFuzzCase> Create,
    bool RegularRun = true
);

/// <summary>
/// 同期計画の総当たりの決定的な行（乱数の種で見つけた形を、生成器の乱数の列に依らない形へ書き写したもの）
/// </summary>
/// <remarks>
/// <para>
/// 各方言の総当たり（<see cref="SyncPlanFuzzTestsBase"/>）が、乱数の種の前に自分の方言の行を流す（種を 1 つだけ流すときは
/// 流さない）。行は乱数の種と同じランナー（<see cref="SyncPlanFuzzRunner.RunCaseAsync"/>）と同じ表明を通るので、違反の文は
/// 乱数の種と同じ形になる。既知の失敗は行ごとの決定的な項目（<see cref="SyncPlanFuzzKnownFailures"/>・範囲は
/// 方言名＋<see cref="ScopeSuffix"/>）で、どの種の数で流しても「起きない」を数える。
/// </para>
/// <para>
/// 行は種の形を最小にしたもので、表・列の名前は種と違う。種の番号は行のコメントに 1 行ずつ書く（乱数の生成器を変えると
/// 種の番号の形は変わるが、行は変わらない）。
/// </para>
/// </remarks>
internal static class SyncPlanScenarios
{
    /// <summary>行の失敗の範囲（方言名の後ろに付ける）</summary>
    public const string ScopeSuffix = "/scenarios";

    /// <summary>
    /// 実 DB の総当たりで夜間にだけ流す行（<see cref="SyncPlanScenarioInput.RegularRun"/> が偽）の失敗の範囲（範囲の後ろに付ける）。
    /// 実 DB の総当たりは夜間の実行でだけこの範囲を受け持つ（普段は流さない行の項目を「起きない」に数えない）。網は全行を流すので
    /// 常に受け持つ
    /// </summary>
    public const string NightlyScopeSuffix = "/nightly";

    /// <summary>行の失敗の範囲</summary>
    /// <param name="dialect">方言名</param>
    public static string ScopeOf(string dialect) => dialect + ScopeSuffix;

    /// <summary>行の失敗の範囲（夜間にだけ流す行は <see cref="NightlyScopeSuffix"/> を付ける）</summary>
    /// <param name="dialect">方言名</param>
    /// <param name="scenarioId">行の Id</param>
    public static string ScopeOf(string dialect, string scenarioId) =>
        Table[scenarioId].Input.RegularRun
            ? ScopeOf(dialect)
            : ScopeOf(dialect) + NightlyScopeSuffix;

    /// <summary>行の表</summary>
    public static ScenarioTable<SyncPlanScenarioInput> Table { get; } =
        new(
            EngineIds.SyncPlan,
            [
                // ---- 4m-p6-doomed-rebuild（MySQL・修正 A）：計画が失敗を前もって分かっている（ForeignKeyColumnTypeMismatch）のに、
                // 選んだ作り直し・主キーの変更の DROP が失敗の文より前に走り、付け直しが走らずに消える ----
                // 実 DB の種 44：自己参照の外部キーの ON DELETE の作り直し・参照される表の主キーの解除・別の表の主キーの片側だけの型の変更
                // → 外し直しを止めたまま主キーの解除が「needed in a foreign key constraint」で失敗 → 作り直した外部キーが消える
                Row(
                    "p6-alter-fk",
                    "4m-p6-doomed-rebuild",
                    "選んだ自己参照の外部キーの作り直しが、失敗が決まった主キーの解除より前に外れて消える（実 DB の種 44）",
                    DialectNames.MySql,
                    live =>
                    {
                        var a = live.Table("a_codes");
                        live.VarChar(a, "code", 20, pk: true);
                        var b = live.Table("b_items");
                        live.Int(b, "id", pk: true);
                        live.Ref(b, a, "a_ref", "fk_b_a");
                        var c = live.Table("c_nodes");
                        live.Int(c, "id", pk: true);
                        var d = live.Table("d_notes");
                        live.Int(d, "id", pk: true);
                        live.Ref(
                            d,
                            c,
                            "c_ref",
                            "fk_d_c",
                            ForeignKeyReferentialAction.SetNull,
                            nullable: true
                        );
                        var e = live.Table("e_items");
                        live.Int(e, "id", pk: true);
                        live.Ref(e, e, "parent_id", "fk_e_e");
                    },
                    target =>
                    {
                        target.RemovePrimaryKey("a_codes");
                        target.SetType("c_nodes", "id", FuzzTypeKind.BigInt);
                        target.SetOnDelete(
                            target.ForeignKey("e_items", "parent_id"),
                            ForeignKeyReferentialAction.Cascade
                        );
                    }
                ),
                // 実 DB の種 19：一意制約の構成列の変更（AlterUniqueConstraint）と、別の表から参照される主キーの片側だけの型の変更
                // → 外部キーの両端が incompatible で列の変更が失敗 → 作り直した一意制約が消える
                Row(
                    "p6-alter-unique",
                    "4m-p6-doomed-rebuild",
                    "選んだ一意制約の作り直しが、失敗が決まった列の変更より前に外れて消える（実 DB の種 19）",
                    DialectNames.MySql,
                    live =>
                    {
                        var t = live.Table("t_items");
                        live.Int(t, "id", pk: true);
                        var n = live.Int(t, "n", value: r => Int(r * 10));
                        live.Unique(t, "UQ_t_items_n", n);
                        var r = live.Table("r_codes");
                        live.Int(r, "id", pk: true);
                        live.Ref(r, t, "t_ref", "fk_r_t");
                    },
                    target =>
                    {
                        target.ChangeUnique("t_items", "UQ_t_items_n", "id");
                        target.SetType("t_items", "id", FuzzTypeKind.BigInt);
                    }
                ),
                // 実 DB の種 107：主キーの付け替え・外部キーの片側の列の型の変更・一意制約の構成列の変更（失敗は自己参照の外部キー）
                // → 外部キーの両端が incompatible で失敗 → 作り直した一意制約が消える
                Row(
                    "p6-alter-unique-self-reference",
                    "4m-p6-doomed-rebuild",
                    "選んだ一意制約の作り直しが、自己参照の外部キーで失敗が決まった列の変更より前に外れて消える（実 DB の種 107）",
                    DialectNames.MySql,
                    live =>
                    {
                        var n = live.Table("n_notes");
                        live.Int(n, "id", pk: true);
                        live.Ref(n, n, "parent_id", "fk_n_n");
                        var o = live.Table("o_orders");
                        live.Int(o, "id", pk: true);
                        var n0 = live.Int(o, "n0", value: r => Int(r * 10));
                        live.Unique(o, "UQ_o_orders_n0", n0);
                    },
                    target =>
                    {
                        target.SetType("n_notes", "id", FuzzTypeKind.BigInt);
                        target.ChangeUnique("o_orders", "UQ_o_orders_n0", "id");
                    }
                ),
                // 実 DB の種 447：複合主キーへ列を足す（id,k → id,k,n0）と、自己参照の外部キーの子の側だけの型の変更を選ぶ
                // → 列の変更が incompatible で失敗 → 外した主キーが付かずに消える
                // 実 DB の種 463：文字列の主キーへ列を足す（code → code,n1）と外部キーの削除（同じ形・主キーの列が 1 つ）
                // 実 DB の種 1355：主キーへ列を足す・一意制約の構成列の変更・外部キーの削除（主キーと作り直した一意制約の両方が消える＝
                // この行と p6-alter-unique の合成）
                Row(
                    "p6-pk-extend",
                    "4m-p6-doomed-rebuild",
                    "選んだ主キーの変更（列を足す）の解除が、失敗が決まった列の変更より前に走り、主キーが消える（実 DB の種 447・463・1355）",
                    DialectNames.MySql,
                    live =>
                    {
                        var n = live.Table("n_notes");
                        live.Int(n, "id", pk: true);
                        live.Ref(n, n, "parent_id", "fk_n_n");
                        var k = live.Table("k_codes");
                        live.Int(k, "id", pk: true);
                        live.Int(k, "k", pk: true, value: _ => "1");
                        live.Int(k, "n0", nullable: true, value: r => Int(r * 10));
                    },
                    target =>
                    {
                        target.SetType("n_notes", "parent_id", FuzzTypeKind.BigInt);
                        target.SetPrimaryKey("k_codes", "id", "k", "n0");
                    },
                    item =>
                        item.Kind == SchemaDiffKind.AlterPrimaryKey
                        || (
                            item.Kind == SchemaDiffKind.AlterColumn
                            && item.TableName.Equals("n_notes", StringComparison.OrdinalIgnoreCase)
                        )
                ),
                // 実 DB の種 797：複合主キーを縮める（id,k → id）と、外部キーの両端の型の変更（同じ親を参照する別の外部キーは変えない）
                // → 別の外部キーが incompatible で失敗 → 主キーが消える
                Row(
                    "p6-pk-shrink",
                    "4m-p6-doomed-rebuild",
                    "選んだ主キーの変更（列を外す）の解除が、兄弟の外部キーで失敗が決まった列の変更より前に走り、主キーが消える（実 DB の種 797）",
                    DialectNames.MySql,
                    live =>
                    {
                        var p = live.Table("p_tags");
                        live.Int(p, "id", pk: true);
                        var a = live.Table("a_codes");
                        live.Int(a, "id", pk: true);
                        live.Ref(a, p, "p_ref", "fk_a_p");
                        var b = live.Table("b_nodes");
                        live.Int(b, "id", pk: true);
                        live.Ref(b, p, "p_ref", "fk_b_p");
                        var k = live.Table("k_nodes");
                        live.Int(k, "id", pk: true);
                        live.Int(k, "k", pk: true, value: _ => "1");
                    },
                    target =>
                    {
                        target.SetType("p_tags", "id", FuzzTypeKind.BigInt);
                        target.SetType("a_codes", "p_ref", FuzzTypeKind.BigInt);
                        target.SetPrimaryKey("k_nodes", "id");
                    }
                ),
                // ---- nightly-mysql-1191（MySQL・P2）：候補キーを証明できない付け直しが「Missing unique key」で失敗し、
                // 同じ計画で先に外した制約が消えたまま残る ----
                // 実 DB の種 1191：参照される表の主キーを外す（付け直しが失敗）と、別の外部キーの子列の一意制約の削除（MySQL は外部キーを
                // 外して付け直す）→ 選んでいない外部キーが消える
                Row(
                    "mysql-1191-remove-pk",
                    "candidate-key-re-add-collateral",
                    "参照される表の主キーの解除で付け直しが失敗し、一意制約の削除に伴って外した別の外部キーが消える（実 DB の種 1191）",
                    DialectNames.MySql,
                    live =>
                    {
                        var a = live.Table("a_items");
                        live.Int(a, "id", pk: true);
                        var b = live.Table("b_orders");
                        live.Int(b, "id", pk: true);
                        live.Ref(b, a, "a_ref", "fk_b_a");
                        var c = live.Table("c_groups");
                        live.Int(c, "id", pk: true);
                        var d = live.Table("d_tags");
                        live.Int(d, "id", pk: true);
                        var cRef = live.Ref(
                            d,
                            c,
                            "c_ref",
                            "fk_d_c",
                            ForeignKeyReferentialAction.SetNull,
                            nullable: true
                        );
                        live.Unique(d, "UQ_d_tags_c_ref", cRef);
                    },
                    target =>
                    {
                        target.RemovePrimaryKey("a_items");
                        target.DropUnique("d_tags", "UQ_d_tags_c_ref");
                    }
                ),
                // 実 DB の種 358：主キーの付け替え（参照される文字列の主キーを別の列へ）と、その主キーを参照する外部キー・自己参照の
                // 外部キーの ON DELETE の作り直しを選ぶ → 付け替えた先を参照しない付け直しが失敗 → 先に外した、選んだ自己参照の
                // 作り直しが消える
                Row(
                    "mysql-358-selected-rebuild",
                    "candidate-key-re-add-collateral",
                    "候補キーを失う付け直しの失敗で、先に外した選んだ自己参照の作り直しが消える（実 DB の種 358）",
                    DialectNames.MySql,
                    live =>
                    {
                        var a = live.Table("a_items");
                        live.VarChar(a, "code", 20, pk: true);
                        live.VarChar(a, "s0", 20);
                        var b = live.Table("b_notes");
                        live.Int(b, "id", pk: true);
                        live.Ref(
                            b,
                            a,
                            "a_ref",
                            "fk_b_a",
                            ForeignKeyReferentialAction.SetNull,
                            nullable: true
                        );
                        var s = live.Table("s_groups");
                        live.Int(s, "id", pk: true);
                        live.Ref(s, s, "parent_id", "fk_s_s");
                    },
                    target =>
                    {
                        target.SetPrimaryKey("a_items", "s0");
                        target.SetOnDelete(
                            target.ForeignKey("b_notes", "a_ref"),
                            ForeignKeyReferentialAction.Cascade
                        );
                        target.SetOnDelete(
                            target.ForeignKey("s_groups", "parent_id"),
                            ForeignKeyReferentialAction.Cascade
                        );
                    }
                ),
                // 同じ形の Oracle 版（P2-4 の能力の組の網の Oracle の種 1551・1570・2660・3017・3279・3656・4094 で見つけた形の確かめ）
                Row(
                    "oracle-358-selected-rebuild",
                    "candidate-key-re-add-collateral",
                    "候補キーを失う付け直しの失敗で、先に外した選んだ自己参照の作り直しが消える（Oracle）",
                    DialectNames.Oracle,
                    live =>
                    {
                        var a = live.Table("a_items");
                        live.VarChar(a, "code", 20, pk: true);
                        live.VarChar(a, "s0", 20);
                        var b = live.Table("b_notes");
                        live.Int(b, "id", pk: true);
                        live.Ref(
                            b,
                            a,
                            "a_ref",
                            "fk_b_a",
                            ForeignKeyReferentialAction.SetNull,
                            nullable: true
                        );
                        var s = live.Table("s_groups");
                        live.Int(s, "id", pk: true);
                        live.Ref(s, s, "parent_id", "fk_s_s");
                    },
                    target =>
                    {
                        target.SetPrimaryKey("a_items", "s0");
                        target.SetOnDelete(
                            target.ForeignKey("b_notes", "a_ref"),
                            ForeignKeyReferentialAction.Cascade
                        );
                        target.SetOnDelete(
                            target.ForeignKey("s_groups", "parent_id"),
                            ForeignKeyReferentialAction.Cascade
                        );
                    }
                ),
                // ---- warned-removal-not-named（PostgreSQL・修正 A。旧名 nightly-postgresql-512）----
                // 実 DB の種 512：自己参照の外部キーの参照アクションの変更（AlterForeignKey）と参照先の主キー列の型の変更を一緒に選ぶ
                // → 型の食い違いの警告で作り直しを外すが、外した作り直しを名指ししない（選んだ項目が同期の後も残る）
                Row(
                    "postgresql-512-self-reference",
                    "warned-removal-not-named",
                    "自己参照の外部キーの作り直しと参照先の型の変更で、作り直しが名指しされずに残る（実 DB の種 512）",
                    DialectNames.PostgreSql,
                    live =>
                    {
                        var n = live.Table("n_notes");
                        live.Int(n, "id", pk: true);
                        live.Ref(n, n, "parent_id", "fk_n_n");
                    },
                    target =>
                    {
                        target.SetType("n_notes", "id", FuzzTypeKind.BigInt);
                        target.SetOnDelete(
                            target.ForeignKey("n_notes", "parent_id"),
                            ForeignKeyReferentialAction.Cascade
                        );
                    }
                ),
                // ---- nightly-oracle-100-111（Oracle・P2）----
                // 旧い実 DB の種 100：一意制約のある列へ主キーを付け替える（同じ種で一意制約の削除・表の削除も作るが選ばない）
                // 実 DB の種 111：一意制約のある列へ主キーを付け替える（ほかの変更は選ばない）
                // → ORA-02261 で失敗（警告なし）・主キーが消えたまま残る
                Row(
                    "oracle-100-111-move-pk-onto-unique",
                    "nightly-oracle-100-111",
                    "一意制約のある列へ主キーを付け替えると ORA-02261 で失敗し主キーが消える（旧い実 DB の種 100・111）",
                    DialectNames.Oracle,
                    live =>
                    {
                        var t = live.Table("t_items");
                        live.Int(t, "id", pk: true);
                        var s0 = live.VarChar(t, "s0", 20);
                        live.Unique(t, null, s0);
                    },
                    target => target.SetPrimaryKey("t_items", "s0"),
                    item => item.Kind == SchemaDiffKind.AlterPrimaryKey
                ),
                // ---- 4m-P1（Oracle・修正 A）：主キーの変更で、型が食い違う外部キーだけを残し兄弟の外部キーは外す → 主キーの解除が
                // ORA-02273 で失敗し、外した兄弟の外部キーが消えたまま残る（再現パッチ Repro4mOracleTests.P1） ----
                Row(
                    "4m-p1-sibling-foreign-key",
                    "4m-p1",
                    "主キーの変更（候補キーを保つ）と兄弟の外部キーの片側だけの型の変更で、兄弟の外部キーが外れて消える",
                    DialectNames.Oracle,
                    live =>
                    {
                        var customer = live.Table("r_customer");
                        live.Int(customer, "id", pk: true);
                        live.Int(customer, "code", value: r => Int(r * 10));
                        var orders = live.Table("r_orders");
                        live.Int(orders, "id", pk: true);
                        live.Ref(orders, customer, "customer_id", "fk_orders");
                        var invoices = live.Table("r_invoices");
                        live.Int(invoices, "id", pk: true);
                        live.Ref(invoices, customer, "customer_id", "fk_invoices");
                    },
                    target =>
                    {
                        target.SetPrimaryKey("r_customer", "code");
                        target.AddUnique("r_customer", "UQ_r_customer_id", "id");
                        target.SetType("r_orders", "customer_id", FuzzTypeKind.VarChar, 20);
                    }
                ),
                // ---- 4m-Q6（SQL Server・修正 A）：同じ列集合の一意制約 2 つの片方を明示的に外し、その列の型を変えると、もう片方が
                // 外されず Msg 5074（再現パッチ Repro4mSqlServerTests.Q6） ----
                Row(
                    "4m-q6-duplicate-unique",
                    "4m-q6",
                    "同じ列集合の一意制約 2 つの片方の削除と列の型の変更で、もう片方が列の変更を止める",
                    DialectNames.SqlServer,
                    live =>
                    {
                        var item = live.Table("r_item");
                        live.Int(item, "id", pk: true);
                        var a = live.Int(item, "a", value: r => Int(r * 10));
                        live.Unique(item, "UQ1", a);
                        live.Unique(item, "UQ2", a);
                    },
                    target =>
                    {
                        target.DropUnique("r_item", "UQ1");
                        target.SetType("r_item", "a", FuzzTypeKind.BigInt);
                    }
                ),
                // ---- 合格側の行（製品が意図して「警告して止まる」形。SyncPlannerTests の「型の食い違い:」の形）：
                // 両端の型が食い違う外部キーがあると、計画はどの外部キーも外さず ForeignKeyColumnTypeMismatch を警告し、
                // 列の変更（か主キーの解除）で何も変えずに失敗する。静的な規則も実 DB の表明も違反にしない ----
                // 型の食い違い: 片側だけの型変更では外部キーを外し直さず警告する
                Row(
                    "pass-fk-type-mismatch-one-side",
                    null,
                    "外部キーの子の側だけの型の変更は、外部キーを外さずに警告して止まる",
                    DialectNames.MySql,
                    live =>
                    {
                        var customer = live.Table("customer");
                        live.Int(customer, "id", pk: true);
                        var orders = live.Table("orders");
                        live.Int(orders, "id", pk: true);
                        live.Ref(orders, customer, "customer_id", "FK_orders_customer");
                    },
                    target => target.SetType("orders", "customer_id", FuzzTypeKind.BigInt)
                ),
                // 型の食い違い: 同じ列の別の外部キーが食い違うなら、食い違わない外部キーも外さない
                Row(
                    "pass-fk-type-mismatch-same-column",
                    null,
                    "同じ列の外部キー 2 本の片方だけが食い違うと、どちらも外さずに警告して止まる",
                    DialectNames.MySql,
                    live =>
                    {
                        var customer = live.Table("customer");
                        live.Int(customer, "id", pk: true);
                        var other = live.Table("other");
                        live.Int(other, "id", pk: true);
                        var orders = live.Table("orders");
                        live.Int(orders, "id", pk: true);
                        var column = live.Ref(orders, customer, "customer_id", "FK_a");
                        live.ForeignKey(orders, column, other, "FK_b");
                    },
                    target =>
                    {
                        target.SetType("orders", "customer_id", FuzzTypeKind.BigInt);
                        target.SetType("customer", "id", FuzzTypeKind.BigInt);
                    }
                ),
                // 型の食い違い: 食い違う外部キーがあれば、別の表の外部キーも外し直さない
                Row(
                    "pass-fk-type-mismatch-other-table",
                    null,
                    "食い違う外部キーがあると、別の表の外部キーも外さずに警告して止まる",
                    DialectNames.MySql,
                    live =>
                    {
                        var customer = live.Table("customer");
                        live.Int(customer, "id", pk: true);
                        var other = live.Table("other");
                        live.Int(other, "id", pk: true);
                        var orders = live.Table("orders");
                        live.Int(orders, "id", pk: true);
                        var column = live.Ref(orders, customer, "customer_id", "FK_a");
                        live.ForeignKey(orders, column, other, "FK_b");
                        var items = live.Table("items");
                        live.Int(items, "id", pk: true);
                        live.Ref(items, customer, "customer_ref", "FK_c");
                    },
                    target =>
                    {
                        target.SetType("orders", "customer_id", FuzzTypeKind.BigInt);
                        target.SetType("customer", "id", FuzzTypeKind.BigInt);
                        target.SetType("items", "customer_ref", FuzzTypeKind.BigInt);
                    }
                ),
                // 型の食い違い: 主キーの変更で付け直す外部キーも、型が食い違うなら外さずに警告する（4m-p1 の兄弟の外部キーが無い形）
                Row(
                    "pass-pk-change-type-mismatch",
                    null,
                    "主キーの変更で付け直す外部キーの型が食い違うと、外さずに警告して止まる（Oracle）",
                    DialectNames.Oracle,
                    live =>
                    {
                        var customer = live.Table("customer");
                        live.Int(customer, "id", pk: true);
                        live.Int(customer, "code", value: r => Int(r * 10));
                        var orders = live.Table("orders");
                        live.Int(orders, "id", pk: true);
                        live.Ref(orders, customer, "customer_id", "FK_orders_customer");
                    },
                    target =>
                    {
                        target.SetPrimaryKey("customer", "code");
                        target.SetType("orders", "customer_id", FuzzTypeKind.VarChar, 10);
                    }
                ),
                // 同じ形の MySQL 版
                Row(
                    "pass-pk-change-type-mismatch-mysql",
                    null,
                    "主キーの変更で付け直す外部キーの型が食い違うと、外さずに警告して止まる（MySQL）",
                    DialectNames.MySql,
                    live =>
                    {
                        var customer = live.Table("customer");
                        live.Int(customer, "id", pk: true);
                        live.Int(customer, "code", value: r => Int(r * 10));
                        var orders = live.Table("orders");
                        live.Int(orders, "id", pk: true);
                        live.Ref(orders, customer, "customer_id", "FK_orders_customer");
                    },
                    target =>
                    {
                        target.SetPrimaryKey("customer", "code");
                        target.SetType("orders", "customer_id", FuzzTypeKind.BigInt);
                    }
                ),
                // ---- nightly-sqlite-*（SQLite・P2）----
                // 実 DB の種 2301・2667・7088・7411・7898・15266・18203：表の列をすべて消す（主キーの解除と列の削除）→ 列が 0 本の再構築
                Row(
                    "sqlite-empty-rebuild",
                    "nightly-sqlite-empty-rebuild",
                    "表の列をすべて消すと、列が 0 本の再構築の表が構文エラーになる（実 DB の種 2301 ほか）",
                    DialectNames.Sqlite,
                    live =>
                    {
                        var t = live.Table("t_codes");
                        live.Int(t, "id", pk: true);
                        var s0 = live.VarChar(t, "s0", 20);
                        live.Unique(t, null, s0);
                    },
                    target =>
                    {
                        target.DropColumn("t_codes", "id");
                        target.DropColumn("t_codes", "s0");
                    }
                ),
                // 旧い実 DB の種 12503・18316：主キーへ列を足してから元の主キーの列を消す（残る主キーの列の値が一意でない）
                Row(
                    "sqlite-pk-nonunique",
                    "nightly-sqlite-pk-nonunique",
                    "値が一意でない列だけが残る主キーの付け替えが、再構築のデータの移送で UNIQUE 違反になり、巻き戻して何も変えない（旧い実 DB の種 12503・18316。DB がデータで断る受け入れの形）",
                    DialectNames.Sqlite,
                    live =>
                    {
                        var t = live.Table("t_nodes");
                        live.Int(t, "id", pk: true);
                        live.Int(t, "r", value: _ => "1");
                    },
                    target =>
                    {
                        target.SetPrimaryKey("t_nodes", "id", "r");
                        target.DropColumn("t_nodes", "id");
                    }
                ),
                // 旧い実 DB の種 16938：外部キーの付け替え先が、選んでいない新しい表（AddTable）
                Row(
                    "sqlite-alterfk-unselected-target",
                    "nightly-sqlite-alterfk-unselected-target",
                    "選んでいない新しい表への外部キーの付け替えが、名指しされたのに「選んだのに残った」と数えられる（旧い実 DB の種 16938）",
                    DialectNames.Sqlite,
                    live =>
                    {
                        var a = live.Table("a_tags");
                        live.Int(a, "id", pk: true);
                        var c = live.Table("c_items");
                        live.Int(c, "id", pk: true);
                        live.Ref(c, a, "a_ref", "FK_c_items_retarget", nullable: true);
                    },
                    target =>
                    {
                        target.AddTable("n_new");
                        target.SetParent(target.ForeignKey("c_items", "a_ref"), "n_new");
                        target.DropTable("a_tags");
                    },
                    item => item.Kind != SchemaDiffKind.AddTable
                ),
                // 旧い実 DB の種 17592：同じ列ペアの外部キー 2 本の参照アクションをどちらも変え、片方の作り直しだけを選ぶ
                Row(
                    "sqlite-twin-fk-rebuild",
                    "nightly-sqlite-twin-fk-rebuild",
                    "同じ列ペアの外部キー 2 本の片方だけの作り直しで、もう片方も作り直される（旧い実 DB の種 17592）",
                    DialectNames.Sqlite,
                    live =>
                    {
                        var p = live.Table("p_notes");
                        live.Int(p, "id", pk: true);
                        live.Int(p, "k", pk: true, value: _ => "1");
                        var c = live.Table("c_codes");
                        live.Int(c, "id", pk: true);
                        var first = live.Ref(c, p, "p_ref", "FK_c_codes_first");
                        live.TwinForeignKey(first, "FK_c_codes_second");
                    },
                    target =>
                    {
                        target.SetOnDelete(
                            target.ForeignKey("c_codes", "p_ref_0", 0),
                            ForeignKeyReferentialAction.Cascade
                        );
                        target.SetOnDelete(
                            target.ForeignKey("c_codes", "p_ref_0", 1),
                            ForeignKeyReferentialAction.Cascade
                        );
                    },
                    SelectFirst(SchemaDiffKind.AlterForeignKey)
                ),
                // ---- mysql-temporal-zero-precision-respelling（MySQL・修正 E）：小数秒の精度 0 を明示した綴り（datetime(0)・
                // time(0)・timestamp(0)）を MySqlTypeNotation が素の綴りへ寄せず、取込の綴り（datetime・time・timestamp）と食い違う
                // AlterColumn が出て、同期しても消えない（CLAUDE.md の取込の節 (12) の型の欠陥）。実 DB の種 77 で見つけた ----
                Row(
                    "mysql-77-respell-datetime",
                    "mysql-temporal-zero-precision-respelling",
                    "日時の型を小数秒の精度 0 を明示した綴りへ書き換えると、同期しても消えない AlterColumn が出る",
                    DialectNames.MySql,
                    live =>
                    {
                        var events = live.Table("r_events");
                        live.Int(events, "id", pk: true);
                        live.Typed(events, "at", "datetime", true, r => $"'2024-01-0{r} 10:00:00'");
                        live.Typed(events, "starts", "time", true, r => $"'1{r}:00:00'");
                        live.Typed(
                            events,
                            "stamped",
                            "timestamp",
                            true,
                            r => $"'2024-01-0{r} 10:00:00'"
                        );
                    },
                    target =>
                    {
                        target.SetDataType("r_events", "at", "datetime(0)");
                        target.SetDataType("r_events", "starts", "time(0)");
                        target.SetDataType("r_events", "stamped", "timestamp(0)");
                    }
                ),
                // ---- sqlite-rebuild-replace-all-columns（SQLite・修正 B）：表の元の列をすべて消して（主キーの解除と列の削除）新しい列を
                // 足すと、再構築に写す列が無く INSERT … SELECT が出ず、行がすべて消える（警告なし・コミット。逐次 DDL の方言では行が残る）。
                // nightly-sqlite-empty-rebuild（列が 0 本の再構築）の隣の形。実 DB の種 795 で見つけた ----
                Row(
                    "sqlite-795-replace-all-columns",
                    "sqlite-rebuild-replace-all-columns",
                    "表の元の列をすべて消して新しい列を足すと、再構築が行を写さずに消す",
                    DialectNames.Sqlite,
                    live =>
                    {
                        var nodes = live.Table("r_nodes");
                        live.Int(nodes, "id", pk: true);
                        var f0 = live.VarChar(nodes, "f0", 20);
                        live.Unique(nodes, null, f0);
                    },
                    target =>
                    {
                        target.RemovePrimaryKey("r_nodes");
                        target.DropColumn("r_nodes", "id");
                        target.DropColumn("r_nodes", "f0");
                        target.AddColumn("r_nodes", "added_a", FuzzTypeKind.VarChar, 30);
                        target.AddColumn("r_nodes", "added_b", FuzzTypeKind.Int);
                    }
                ),
                // ---- warned-removal-not-named（SQL Server・修正 A）：実 DB の種 753 の形＝親の列の型を変えずに（変更は選ばない）、
                // 子に型の違う列と外部キーを足す → ForeignKeyColumnTypeMismatch で外部キーの追加を外すが名指ししない ----
                Row(
                    "sqlserver-753-warned-type-mismatch",
                    "warned-removal-not-named",
                    "型の食い違いの警告で外した外部キーの追加が名指しされずに残る（実 DB の種 753）",
                    DialectNames.SqlServer,
                    live =>
                    {
                        var p = live.Table("w_parent");
                        live.Int(p, "id", pk: true);
                        var c = live.Table("w_child");
                        live.Int(c, "id", pk: true);
                    },
                    target =>
                    {
                        target.SetType("w_parent", "id", FuzzTypeKind.BigInt);
                        target.AddColumn("w_child", "parent_ref", FuzzTypeKind.BigInt);
                        target.AddForeignKey(
                            "w_child",
                            "parent_ref",
                            "w_parent",
                            "fk_w_child_parent"
                        );
                    },
                    item => item.Kind != SchemaDiffKind.AlterColumn
                ),
                // ---- warned-removal-not-named（MySQL・修正 A）：実 DB の種 997 の形＝NOT NULL の子の列の外部キーを SET NULL へ作り直し、
                // NULL 許容化は選ばない → SetNullOnNotNullColumn で作り直しを外すが名指ししない ----
                Row(
                    "mysql-997-warned-set-null",
                    "warned-removal-not-named",
                    "SET NULL の警告で外した外部キーの作り直しが名指しされずに残る（実 DB の種 997）",
                    DialectNames.MySql,
                    live =>
                    {
                        var p = live.Table("s_parent");
                        live.Int(p, "id", pk: true);
                        var c = live.Table("s_child");
                        live.Int(c, "id", pk: true);
                        live.Ref(c, p, "parent_ref", "fk_s_child_parent");
                    },
                    target =>
                    {
                        target.SetNullable("s_child", "parent_ref", true);
                        target.SetOnDelete(
                            target.ForeignKey("s_child", "parent_ref"),
                            ForeignKeyReferentialAction.SetNull
                        );
                    },
                    SelectFirst(SchemaDiffKind.AlterForeignKey)
                ),
                // ---- synthesized-unique-name-collision（MySQL・PostgreSQL・修正 E）：実 DB の種 386 の形＝名前付きの一意制約
                // UQ_u_orders_n0 の列を (n0)→(code) に変え、(n0) に名前の無い一意制約を足す → 合成名 UQ_u_orders_n0 が衝突する ----
                .. new[] { DialectNames.MySql, DialectNames.PostgreSql }.Select(dialect =>
                    Row(
                        $"{dialect}-386-synthesized-unique-name",
                        "synthesized-unique-name-collision",
                        "名前の無い一意制約の合成名が、列を変えた名前付きの一意制約の名前と衝突する（実 DB の種 386）",
                        dialect,
                        live =>
                        {
                            var t = live.Table("u_orders");
                            live.Int(t, "id", pk: true);
                            var n0 = live.Int(t, "n0", value: r => Int(r * 10));
                            live.Int(t, "code", value: r => Int(r * 100));
                            live.Unique(t, "UQ_u_orders_n0", n0);
                        },
                        target =>
                        {
                            target.ChangeUnique("u_orders", "UQ_u_orders_n0", "code");
                            target.AddUnique("u_orders", null, "n0");
                        }
                    )
                ),
                // ---- 4m-p6-doomed-rebuild の規則 4 の形（MySQL・修正 A）：実 DB の種 645・1014 の形＝一意制約の構成列の削除と、外部キーの
                // 子の列だけの型の変更（ForeignKeyColumnTypeMismatch＝doomed）を選び、一意制約の削除は選ばない → 計画が列と一緒に消える
                // 一意制約の DROP を足し（UniqueConstraintDroppedWithColumn）、それが失敗の文（列の変更）より前に走り、列の削除は失敗の
                // 後ろなので、一意制約だけが消える（規則 4。一意制約の削除を選ぶと規則 1 で消えてよい形になる） ----
                Row(
                    "p6-drop-before-failure-rule4",
                    "4m-p6-doomed-rebuild",
                    "doomed の計画で、後ろの列の削除が理由の一意制約の DROP が失敗の文より前に走り、一意制約だけが消える（実 DB の種 645・1014）",
                    DialectNames.MySql,
                    live =>
                    {
                        var p = live.Table("d_parent");
                        live.Int(p, "id", pk: true);
                        var c = live.Table("d_child");
                        live.Int(c, "id", pk: true);
                        live.Ref(c, p, "parent_ref", "fk_d_child_parent");
                        var f0 = live.Int(c, "f0", value: r => Int(r * 10));
                        live.Unique(c, "uq_d_child_f0", f0);
                    },
                    target =>
                    {
                        target.DropColumn("d_child", "f0");
                        target.SetType("d_child", "parent_ref", FuzzTypeKind.BigInt);
                    },
                    item => item.Kind != SchemaDiffKind.DropUniqueConstraint
                ),
                // ---- D4 の形の行（SyncPlanShapeRows）：主キーの変更 × 参照する外部キーの数 × 型の変更 × 明示の削除 × 一意制約の数 ----
                .. SyncPlanShapeRows.Rows,
            ]
        );

    /// <summary>合格側の行の Id の頭（製品が意図して「警告して止まる」形）</summary>
    public const string PassPrefix = "pass-";

    /// <summary>
    /// 合格側の行（指摘を持たず Id が <see cref="PassPrefix"/> で始まる行）の表明：規則が失敗を予言し（偽陽性の守りが空振りしていない）、
    /// 違反が 0。破れた行ごとに問題の文を返す（網と実 DB の総当たりの両方が使う）
    /// </summary>
    /// <param name="scenarioOutcomes">流した行の結果</param>
    public static IEnumerable<string> PassRowProblems(IEnumerable<FuzzSeedOutcome> scenarioOutcomes)
    {
        foreach (var outcome in scenarioOutcomes)
        {
            var row = Table[outcome.ScenarioId!];

            if (row.Finding is not null || !row.Id.StartsWith(PassPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (!outcome.Notes.Contains(SyncPlanFuzzRunner.StaticRulePredictedNote))
            {
                yield return $"pass row {row.Id}: the static rules must predict the failure that the plan warns about";
            }

            if (outcome.Violations.Count > 0)
            {
                yield return $"pass row {row.Id}: expected no violations, got: {string.Join(" / ", outcome.Violations)}";
            }
        }
    }

    /// <summary>方言の行</summary>
    /// <param name="dialect">方言名</param>
    public static IEnumerable<ScenarioRow<SyncPlanScenarioInput>> For(string dialect) =>
        Table.Where(row => string.Equals(row.Input.Dialect, dialect, StringComparison.Ordinal));

    /// <summary>行を作る</summary>
    private static ScenarioRow<SyncPlanScenarioInput> Row(
        string id,
        string? finding,
        string description,
        string dialect,
        Action<ScenarioLive> build,
        Action<ScenarioTarget> mutate,
        Func<SchemaDiffItem, bool>? select = null
    ) =>
        Row(
            id,
            finding,
            description,
            dialect,
            build,
            mutate,
            select is null ? null : (_, item) => select(item)
        );

    /// <summary>行を作る（選び方が項目の一覧全体を見る）</summary>
    private static ScenarioRow<SyncPlanScenarioInput> Row(
        string id,
        string? finding,
        string description,
        string dialect,
        Action<ScenarioLive> build,
        Action<ScenarioTarget> mutate,
        Func<IReadOnlyList<SchemaDiffItem>, SchemaDiffItem, bool>? select
    ) =>
        new(
            id,
            finding,
            description,
            new SyncPlanScenarioInput(
                dialect,
                fuzzDialect => new SyncPlanScenarioCase(fuzzDialect, build, mutate, select)
            )
        );

    /// <summary>その種類の選べる項目のうち最初の 1 つだけを選ぶ</summary>
    private static Func<IReadOnlyList<SchemaDiffItem>, SchemaDiffItem, bool> SelectFirst(
        SchemaDiffKind kind
    ) => (items, item) => ReferenceEquals(items.First(i => i.IsSelectable && i.Kind == kind), item);

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>決定的な行のケース（DB 側の図・目標の図への編集・選び方を手で書く）</summary>
/// <param name="dialect">方言の部品（型名・リテラル）</param>
/// <param name="build">DB 側の図を作る</param>
/// <param name="mutate">目標の図を編集する</param>
/// <param name="select">選ぶ項目（null なら選べる項目を全部選ぶ）</param>
internal sealed class SyncPlanScenarioCase(
    SyncPlanFuzzDialect dialect,
    Action<ScenarioLive> build,
    Action<ScenarioTarget> mutate,
    Func<IReadOnlyList<SchemaDiffItem>, SchemaDiffItem, bool>? select
) : ISyncPlanFuzzCase
{
    /// <inheritdoc/>
    public List<string> Mutations { get; } = [];

    /// <inheritdoc/>
    public FuzzLiveSchema BuildLive()
    {
        var live = new FuzzLiveSchema();
        build(new ScenarioLive(live, dialect));
        SyncPlanFuzzGenerator.BuildInserts(live, dialect);
        return live;
    }

    /// <inheritdoc/>
    public void Mutate(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    ) => mutate(new ScenarioTarget(entities, relationships, dialect, Mutations));

    /// <inheritdoc/>
    /// <remarks>選び方は行が決めるので、報告の表記は <see cref="FuzzSelection.All"/>（全部）か <see cref="FuzzSelection.ByKinds"/>（行の述語）</remarks>
    public FuzzSelection Select(IReadOnlyList<SchemaDiffItem> items)
    {
        foreach (var item in items.Where(i => i.IsSelectable))
        {
            item.IsSelected = select?.Invoke(items, item) ?? true;
        }

        return select is null ? FuzzSelection.All : FuzzSelection.ByKinds;
    }
}

/// <summary>決定的な行の DB 側の図を作る部品（列の値は生成器と同じ規則で作る）</summary>
internal sealed class ScenarioLive(FuzzLiveSchema live, SyncPlanFuzzDialect dialect)
{
    /// <summary>表を足す（行数は既定で 3）</summary>
    public Entity Table(string name, int rows = 3)
    {
        var entity = new Entity { TableName = name };
        live.Diagram.Entities.Add(entity);
        live.RowCounts[name] = rows;
        return entity;
    }

    /// <summary>整数の列を足す（値は既定で行番号）</summary>
    public Column Int(
        Entity table,
        string name,
        bool pk = false,
        bool nullable = false,
        Func<int, string?>? value = null
    ) =>
        Add(
            table,
            name,
            FuzzTypeKind.Int,
            0,
            pk,
            nullable,
            value ?? (r => r.ToString(CultureInfo.InvariantCulture))
        );

    /// <summary>可変長の文字列の列を足す（値は既定で「列名＋行番号」）</summary>
    public Column VarChar(
        Entity table,
        string name,
        int length,
        bool pk = false,
        bool nullable = false,
        Func<int, string?>? value = null
    ) =>
        Add(
            table,
            name,
            FuzzTypeKind.VarChar,
            length,
            pk,
            nullable,
            value ?? (r => dialect.StringLiteral($"{name}{r}"))
        );

    /// <summary>
    /// 親の主キーを参照する列と外部キーを足す（親の主キーが複数列なら列名に _0・_1 を付ける。値は親の行を指す。
    /// 自己参照は 1 行目を NULL にして前の行を指す）
    /// </summary>
    public Column Ref(
        Entity child,
        Entity parent,
        string name,
        string? constraintName,
        ForeignKeyReferentialAction onDelete = ForeignKeyReferentialAction.NoAction,
        bool nullable = false
    )
    {
        var key = parent.GetPrimaryKeyColumnsInOrder();
        var self = ReferenceEquals(child, parent);
        var parentRows = live.RowCounts[parent.TableName];
        var pairs = new List<RelationshipColumnPair>();
        Column? first = null;

        for (var p = 0; p < key.Count; p++)
        {
            var parentInfo = live.Find(parent.TableName, key[p].Name)!;
            var column = Add(
                child,
                key.Count == 1 ? name : $"{name}_{p}",
                parentInfo.Kind,
                parentInfo.Length,
                false,
                nullable || self,
                r =>
                    self ? (r == 1 ? null : parentInfo.Values[r - 2])
                    : parentRows == 0 ? null
                    : parentInfo.Values[(r - 1) % parentRows]
            );
            column.IsForeignKey = true;
            pairs.Add(new RelationshipColumnPair(key[p].Id, column.Id));
            first ??= column;
        }

        live.Diagram.Relationships.Add(
            new Relationship
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs = pairs,
                ConstraintName = constraintName,
                OnDelete = onDelete,
            }
        );
        return first!;
    }

    /// <summary>既にある子の列から、別の親の主キー（1 列）へ外部キーを張る（同じ列に外部キー 2 本の形）</summary>
    public void ForeignKey(Entity child, Column childColumn, Entity parent, string constraintName)
    {
        var key = parent.GetPrimaryKeyColumnsInOrder().Single();
        live.Diagram.Relationships.Add(
            new Relationship
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs = [new RelationshipColumnPair(key.Id, childColumn.Id)],
                ConstraintName = constraintName,
            }
        );
    }

    /// <summary>列の外部キーと同じ列ペア・同じ親の外部キーを、別の名前でもう 1 本張る</summary>
    public void TwinForeignKey(Column childColumn, string constraintName)
    {
        var source = live.Diagram.Relationships.Single(r =>
            r.ColumnPairs.Any(p => p.TargetColumnId == childColumn.Id)
        );
        live.Diagram.Relationships.Add(
            new Relationship
            {
                SourceEntityId = source.SourceEntityId,
                TargetEntityId = source.TargetEntityId,
                Type = RelationshipType.OneToMany,
                ColumnPairs = [.. source.ColumnPairs.Select(p => p.Clone())],
                ConstraintName = constraintName,
            }
        );
    }

    /// <summary>型の表記を書いた列を足す（生成器の型の種類に無い型。値は行番号から作る）</summary>
    public Column Typed(
        Entity table,
        string name,
        string dataType,
        bool nullable,
        Func<int, string?> value
    )
    {
        var column = Add(table, name, FuzzTypeKind.VarChar, 0, false, nullable, value);
        column.DataType = dataType;
        return column;
    }

    /// <summary>一意制約を張る（名前が null なら名前なし）</summary>
    public void Unique(Entity table, string? name, params Column[] columns) =>
        table.UniqueConstraints.Add(
            new UniqueConstraint { Name = name, ColumnIds = [.. columns.Select(c => c.Id)] }
        );

    private Column Add(
        Entity table,
        string name,
        FuzzTypeKind kind,
        int length,
        bool pk,
        bool nullable,
        Func<int, string?> value
    )
    {
        var column = new Column
        {
            Name = name,
            DataType = dialect.TypeName(kind, length),
            IsNullable = nullable && !pk,
            IsPrimaryKey = pk,
        };
        table.Columns.Add(column);

        if (pk)
        {
            table.PrimaryKeyColumnIds.Add(column.Id);
        }

        var info = new FuzzColumnInfo { Kind = kind, Length = length };

        for (var r = 1; r <= live.RowCounts[table.TableName]; r++)
        {
            info.Values.Add(value(r));
        }

        live.Columns[FuzzLiveSchema.Key(table.TableName, name)] = info;
        return column;
    }
}

/// <summary>決定的な行の目標の図を編集する部品（表・列・外部キーは名前で引く＝大文字小文字を無視）</summary>
internal sealed class ScenarioTarget(
    List<Entity> entities,
    List<Relationship> relationships,
    SyncPlanFuzzDialect dialect,
    List<string> mutations
)
{
    /// <summary>表を引く</summary>
    public Entity Table(string name) =>
        entities.Single(e => e.TableName.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>列を引く</summary>
    public Column Column(string table, string column) =>
        Table(table).Columns.Single(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));

    /// <summary>子の列を含む外部キーを引く（同じ列の外部キーが複数あれば並びの位置で選ぶ）</summary>
    public Relationship ForeignKey(string childTable, string childColumn, int index = 0)
    {
        var column = Column(childTable, childColumn);
        return relationships
            .Where(r => r.ColumnPairs.Any(p => p.TargetColumnId == column.Id))
            .ElementAt(index);
    }

    /// <summary>列の型を変える</summary>
    public void SetType(string table, string column, FuzzTypeKind kind, int length = 0)
    {
        var target = Column(table, column);
        target.DataType = dialect.TypeName(kind, length);
        mutations.Add($"alter type {table}.{column} -> {target.DataType}");
    }

    /// <summary>列の型の表記を書き換える（同じ型の別の綴りを含む）</summary>
    public void SetDataType(string table, string column, string dataType)
    {
        var target = Column(table, column);
        target.DataType = dataType;
        mutations.Add($"respell type {table}.{column} -> {dataType}");
    }

    /// <summary>NULL を許す列を足す</summary>
    public void AddColumn(string table, string column, FuzzTypeKind kind, int length = 0)
    {
        Table(table)
            .Columns.Add(
                new Column
                {
                    Name = column,
                    DataType = dialect.TypeName(kind, length),
                    IsNullable = true,
                }
            );
        mutations.Add($"add column {table}.{column}");
    }

    /// <summary>主キーを列の並びで置き換える</summary>
    public void SetPrimaryKey(string table, params string[] columns)
    {
        SyncPlanFuzzGenerator.SetPrimaryKey(
            Table(table),
            [.. columns.Select(c => Column(table, c))]
        );
        mutations.Add($"set pk {table} -> ({string.Join(",", columns)})");
    }

    /// <summary>主キーを外す</summary>
    public void RemovePrimaryKey(string table)
    {
        SyncPlanFuzzGenerator.SetPrimaryKey(Table(table), []);
        mutations.Add($"remove pk {table}");
    }

    /// <summary>外部キーの ON DELETE を変える</summary>
    public void SetOnDelete(Relationship relationship, ForeignKeyReferentialAction action)
    {
        relationship.OnDelete = action;
        mutations.Add($"fk action {Describe(relationship)} ON DELETE {action}");
    }

    /// <summary>外部キーの親を、別の表の主キー（整数 1 列）へ付け替える（子の列はそのまま）</summary>
    public void SetParent(Relationship relationship, string parentTable)
    {
        var parent = Table(parentTable);
        var key = parent.GetPrimaryKeyColumnsInOrder().Single();
        relationship.SourceEntityId = parent.Id;
        relationship.ColumnPairs =
        [
            new RelationshipColumnPair(key.Id, relationship.ColumnPairs[0].TargetColumnId),
        ];
        mutations.Add($"retarget fk {Describe(relationship)}");
    }

    /// <summary>名前付きの一意制約の構成列を変える</summary>
    public void ChangeUnique(string table, string name, params string[] columns)
    {
        var unique = Table(table)
            .UniqueConstraints.Single(u =>
                string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase)
            );
        unique.ColumnIds = [.. columns.Select(c => Column(table, c).Id)];
        mutations.Add($"change unique {table} {name} -> ({string.Join(",", columns)})");
    }

    /// <summary>一意制約を足す</summary>
    public void AddUnique(string table, string? name, params string[] columns)
    {
        Table(table)
            .UniqueConstraints.Add(
                new UniqueConstraint
                {
                    Name = name,
                    ColumnIds = [.. columns.Select(c => Column(table, c).Id)],
                }
            );
        mutations.Add(
            $"add unique {table}({string.Join(",", columns)}){(name is null ? "" : " named")}"
        );
    }

    /// <summary>NULL 許容を変える</summary>
    public void SetNullable(string table, string column, bool nullable)
    {
        Column(table, column).IsNullable = nullable;
        mutations.Add($"nullable {table}.{column} -> {(nullable ? "null" : "not null")}");
    }

    /// <summary>既にある子の列から、親の主キー（1 列）へ外部キーを足す</summary>
    public void AddForeignKey(
        string childTable,
        string childColumn,
        string parentTable,
        string name
    )
    {
        var parent = Table(parentTable);
        relationships.Add(
            new Relationship
            {
                SourceEntityId = parent.Id,
                TargetEntityId = Table(childTable).Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs =
                [
                    new RelationshipColumnPair(
                        parent.GetPrimaryKeyColumnsInOrder().Single().Id,
                        Column(childTable, childColumn).Id
                    ),
                ],
                ConstraintName = name,
            }
        );
        mutations.Add($"add fk {childTable}.{childColumn} -> {parentTable}");
    }

    /// <summary>外部キーを消す</summary>
    public void DropForeignKey(Relationship relationship)
    {
        mutations.Add($"drop fk {Describe(relationship)}");
        relationships.Remove(relationship);
    }

    /// <summary>名前付きの一意制約を消す</summary>
    public void DropUnique(string table, string name)
    {
        Table(table)
            .UniqueConstraints.RemoveAll(u =>
                string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase)
            );
        mutations.Add($"drop unique {table} {name}");
    }

    /// <summary>列を消す（その列を含む一意制約・外部キーも消す）</summary>
    public void DropColumn(string table, string column)
    {
        var entity = Table(table);
        var target = Column(table, column);
        entity.Columns.Remove(target);
        entity.UniqueConstraints.RemoveAll(u => u.ColumnIds.Contains(target.Id));
        relationships.RemoveAll(r =>
            r.ColumnPairs.Any(p => p.SourceColumnId == target.Id || p.TargetColumnId == target.Id)
        );
        mutations.Add($"drop column {table}.{column}");
    }

    /// <summary>表を消す（その表に関わる外部キーも消す）</summary>
    public void DropTable(string table)
    {
        var entity = Table(table);
        entities.Remove(entity);
        relationships.RemoveAll(r =>
            r.SourceEntityId == entity.Id || r.TargetEntityId == entity.Id
        );
        mutations.Add($"drop table {table}");
    }

    /// <summary>整数の主キー id と文字列 v の表を足す</summary>
    public Entity AddTable(string name)
    {
        var entity = new Entity { TableName = name };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = dialect.TypeName(FuzzTypeKind.Int, 0),
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.PrimaryKeyColumnIds.Add(entity.Columns[0].Id);
        entity.Columns.Add(
            new Column { Name = "v", DataType = dialect.TypeName(FuzzTypeKind.VarChar, 20) }
        );
        entities.Add(entity);
        mutations.Add($"add table {name}");
        return entity;
    }

    private string Describe(Relationship relationship) =>
        $"{entities.First(e => e.Id == relationship.TargetEntityId).TableName} -> "
        + $"{entities.First(e => e.Id == relationship.SourceEntityId).TableName} ({relationship.ConstraintName ?? "unnamed"})";
}
