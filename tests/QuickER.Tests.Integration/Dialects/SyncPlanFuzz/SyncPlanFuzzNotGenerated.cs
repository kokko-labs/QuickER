using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の総当たりの生成器（<c>SyncPlanFuzzGenerator</c>）が作らない形の宣言。総当たりと能力の組の網が同じ生成器の入力を使う
/// （QuickER.Tests にもリンクで入るので、Integration の型を cref でも参照しない）
/// </summary>
/// <remarks>
/// 純粋なデータ（Engines の型と BCL だけを参照する）。QuickER.Tests にも Compile のリンクで入り、受け持つ守りが照合される。
/// </remarks>
internal static class SyncPlanFuzzNotGenerated
{
    private const string Expectations = "`table:SyncPlanExpectations`";

    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "SyncPlanFuzzGenerator",
            [
                ProtectionNames.EnginePart(EngineIds.SyncPlan, "Fuzz"),
                ProtectionNames.EnginePart(EngineIds.SyncPlan, "CapabilityNet"),
            ],
            [
                NotGenerated.Pending(
                    "descriptions",
                    "表・列の説明（空欄・保存する形・BMP の外の文字・選んでいない表の説明）",
                    "G1b",
                    "説明を一度も作らない。差分と計画は説明の空欄・保存する形・表せない文字で分岐する（SchemaDiffService・SyncPlanner の説明の扱い）",
                    Expectations
                        + " の行 `diff-blank-description-*` / `diff-description-stored-form-*` / `plan-description-characters-replaced-*`"
                ),
                NotGenerated.Pending(
                    "column-defaults",
                    "既定値のある列（型の種類の変更の警告）",
                    "G1b",
                    "既定値を一度も作らない。計画は既定値のある列の型の種類の変更を警告する（ChecksDefaultOnColumnTypeChange の宣言）",
                    Expectations
                        + " の行 `plan-default-*` / `realdb-column-type-kind-change-with-default-warns` / `realdb-pg-column-type-kind-change-with-default-warns` / `realdb-oracle-column-type-kind-change-with-default-warns`"
                ),
                NotGenerated.Accepted(
                    "identity-columns",
                    "採番列（DB が値を作る列）",
                    "差分（SchemaDiffService）と計画（SyncPlanner）は採番列で分岐しない。採番列で分岐するのは DDL・同期スクリプトの注記で、方言の DDL の表が受け持つ",
                    "`Dialect/DdlText`（行 `sync-*-identity-column-*`）"
                ),
                NotGenerated.Accepted(
                    "sqlserver-referential-actions",
                    "SQL Server の CASCADE・SET NULL・SET DEFAULT の外部キーと参照アクションの変更",
                    "SQL Server は循環・複数経路の連鎖を CREATE で断り、生成器は連鎖を避ける作り方を持たないので NO ACTION だけを使う（SyncPlanFuzzDialect.AllowCascade）。参照アクションの変更は、独立した親子に手で書いた網羅の行が選び方 5 通りで受け持つ",
                    "`SyncPlan/Fuzz`（手で書いた SQL Server の参照アクションの網羅の行）"
                ),
                NotGenerated.Pending(
                    "table-without-primary-key",
                    "主キーの無い DB の表",
                    "G1b",
                    "DB の表は必ず主キーを持つ。計画と差分は主キーの無い表で分岐する（主キーの同期の段・差分の主キーの比較）",
                    Expectations + " の行 `plan-without-pk-change-has-no-phase`"
                ),
                NotGenerated.Pending(
                    "multi-column-unique-constraints",
                    "複数列の一意制約・それを参照する外部キー・MySQL の先頭の列だけを参照する外部キー",
                    "G1b",
                    "DB 側の一意制約はいつも 1 列。計画は複数列の一意制約と、それを支えにする外部キー（MySQL は参照する列を先頭に並べるキー）で分岐する",
                    "`SyncPlan/ShapeRows`・"
                        + Expectations
                        + " の行 `plan-implicit-unique-drop-warns-for-fk-on-its-leading-columns-mysql-like`"
                ),
                NotGenerated.Pending(
                    "foreign-key-retarget",
                    "外部キーの参照先だけを変える（親の表・親の列・主キー参照と一意制約参照の入れ替え）・同じ名前で別の子表へ",
                    "G1b",
                    "外部キーの変更は子の列を変えるだけ。差分は参照先の変更で作り直しに分岐する",
                    Expectations
                        + " の行 `diff-fk-retargeted-without-persisted-names-is-alter` / `realdb-mysql-fk-alter-to-other-column`"
                ),
                NotGenerated.Pending(
                    "constraint-rename-only",
                    "制約名だけの変更（外部キー・一意制約）",
                    "G1b",
                    "制約名だけを変える手が無い。差分は名前の段と列集合の段で分岐する",
                    Expectations
                        + " の行 `diff-fk-different-names-stay-drop-and-add` / `diff-uq-different-names-are-not-paired`"
                ),
                NotGenerated.Pending(
                    "new-table-shapes",
                    "新しい表の形の幅（複合主キー・一意制約・主キーと同じ列の一意制約・自己参照・新しい表どうしの外部キー）",
                    "G1b",
                    "新しい表の形は固定。CREATE TABLE の制約行（TableConstraintLineBuilder）と新しい表の並びはこれらの形で分岐する",
                    "`Dialect/DdlText`（行 `sync-add-table-unique-on-primary-key-columns-omitted-where-unsupported`）・"
                        + Expectations
                        + " の行 `plan-rebuild-fk-to-unselected-new-table-*`"
                ),
                NotGenerated.Pending(
                    "unique-on-new-column",
                    "新しく足した列への一意制約",
                    "G1b",
                    "一意制約を足す列は DB 側の列から選ぶので、新しく足した列には張らない。計画は新しい列の一意制約を列の追加の後に並べる",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Pending(
                    "type-change-breadth",
                    "型の変更の幅（文字列から数値・整数から小数・スケールの変更・精度なしから精度あり・固定長と可変長の入れ替え）",
                    "G1b",
                    "型の変更の手は一部の組だけ。同じ型の判定と型の互換（SyncDialectCapabilities）はこれらの組で分岐する",
                    Expectations + " の行 `plan-type-mismatch-*`"
                ),
                NotGenerated.Accepted(
                    "oracle-kind-change-with-rows",
                    "Oracle の行のある列の型の種類の変更",
                    "Oracle は行のある列の型の種類を変えられない（ORA-01439）。生成器は変える列と揃える相手の値が全部 NULL か行 0 のときだけ種類を変える",
                    Expectations + " の行 `realdb-oracle-column-alter-non-empty-to-number-fails`"
                ),
                NotGenerated.Pending(
                    "two-readded-keys-in-unique-rebuild",
                    "一意制約の作り直しの組で付け直すキーが 2 つ以上",
                    "G1b",
                    "作り直す一意制約は最初の名前つきの 1 つだけ。前寄せの組は付け直すキーの数で分岐する（KeepOneReaddedUnique）",
                    "`SyncPlan/ShapeRows`・"
                        + Expectations
                        + " の行 `plan-move-first-key-drop-group-two-readded-keeps-one-rebuild-mysql-like`"
                ),
                NotGenerated.Accepted(
                    "name-forms",
                    "SQLite のドットを含む表名・前後の空白・非 ASCII の名前・上限ちょうどの列名と制約名",
                    "名前の形の網羅は名前の総当たり（DDL の往復）が受け持ち、利用者の付けた名前は縮めずに入口で断る（総当たりで作ると失敗が名前の入口の拒否だけになる）",
                    "`Name/DdlRoundTrip`・"
                        + Expectations
                        + " の行 `plan-dotted-table-name-warns-only-where-declared`"
                ),
                NotGenerated.Accepted(
                    "rename-table-keeping-unique-name",
                    "表の改名（大小以外）で古い表の一意制約の名前を引き継ぐ",
                    "セクションの順（表の削除を追加より前）だけが受け持つ 1 つの形で、決まった形の行で足りる",
                    Expectations
                        + " の行 `realdb-rename-table-keeping-unique-name-drops-old-table-first`"
                ),
                NotGenerated.Accepted(
                    "import-discarded-content",
                    "SQLite の取込が捨てる物・再構築で落ちる属性",
                    "DB は QuickER の DDL で作るので、取込が捨てる物を持たない。捨てた物のある表の再構築は決まった形の行が受け持つ",
                    Expectations + " の行 `realdb-rebuild-skipped-when-import-discarded-*`"
                ),
                NotGenerated.Accepted(
                    "legacy-history",
                    "旧版の来歴（PostgreSQL の切り詰め名）",
                    "乱数の DB はいつも今の DDL で作る。旧版の DDL が作った名前は来歴の行が受け持つ",
                    "`SyncPlan/HistoryRows`"
                ),
                NotGenerated.Accepted(
                    "duplicate-unique-column-sets",
                    "Oracle の同じ列集合の一意制約 2 つ・SQLite の同じ列集合の制約",
                    "目標の図を DB が持てない形にしない（Oracle は ORA-02261 で断る）。製品に除外の分岐が無いことは製品の課題で、生成器の水準では埋まらない",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Accepted(
                    "duplicate-unnamed-unique-constraints",
                    "同じ列の並びの名前の無い一意制約 2 つ",
                    "名前の無い 2 つは同じ合成名になり、製品の入口（GUI・ツール）はこの図を作らない。同じ並びにもう一意制約があれば名前を付ける",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Accepted(
                    "live-computed-to-plain",
                    "DB の計算列を図で普通の列に戻す",
                    "差分の 1 つの分岐で、決まった形の行で足りる",
                    Expectations + " の行 `diff-live-only-computed-column-*`"
                ),
                NotGenerated.Accepted(
                    "live-foreign-key-cycles",
                    "DB 側の表どうしの外部キーの循環",
                    "外部キーは前の表の主キーだけを参照する。計画に循環専用の分岐は無い",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Accepted(
                    "unique-column-kinds",
                    "一意制約の列が日時・バイト列、足す一意制約の列が小数",
                    "日時・バイト列を混ぜる目的は型の変更と綴りで、一意制約の形として足すものが無い。計画は一意制約の列の型で分岐しない",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Accepted(
                    "overlapping-column-changes",
                    "同じ列に主キーから外す・NULL 許容化・型の変更が重なる",
                    "乱数の偶然でだけ出る組で、依存の並びは決まった形の行が受け持つ",
                    Expectations + " の行 `plan-dependency-*`"
                ),
                NotGenerated.Accepted(
                    "mysql-long-varchar-with-key",
                    "MySQL の可変長の文字列の上限（varchar(16383)）の列",
                    "主キーの列と合わせると行の上限 65,535 バイトを超える（DB の事実）。上限の型は固定長の char(255) で確かめる",
                    NotGenerated.Unguarded
                ),
            ]
        );
}
