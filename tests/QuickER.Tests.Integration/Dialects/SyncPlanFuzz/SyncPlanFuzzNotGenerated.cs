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
                NotGenerated.Accepted(
                    "sqlite-descriptions",
                    "SQLite の表と列の説明",
                    "SQLite は説明を保存しない（製品の事実＝SupportsDescriptions が偽）。説明の扱いは方言の DDL の表が受け持つ",
                    "`Dialect/DdlText`（行 `ddl-description-*`）"
                ),
                NotGenerated.Accepted(
                    "sqlite-column-defaults",
                    "SQLite の既定値のある列",
                    "SQLite は ALTER で既定値を足せず、図は既定値を持たない（生成器は DDL の後に生の SQL で既定値を足すので、SQLite では作れない）。再構築が既定値を落とすことは警告で告げる",
                    "`TableRebuildAttributeDetectorTests`・"
                        + Expectations
                        + " の行 `realdb-rebuild-*`"
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
                NotGenerated.Accepted(
                    "mysql-non-standard-key-foreign-key",
                    "MySQL の先頭の列だけを参照する外部キー（非標準キー）",
                    "MySQL 8.4 の既定（restrict_fk_on_non_standard_key=ON）は非標準キーへの外部キーを作らせない（DB の事実）。計画の分岐（CanSupportForeignKey の先頭の列）は mysql の能力で流す期待の行が受け持つ",
                    Expectations
                        + " の行 `plan-implicit-unique-drop-warns-for-fk-on-its-leading-columns-mysql-like` / `plan-implicit-fk-pk-change-onto-referenced-prefix-of-dropped-unique-rebuilds-fk-mysql-like`"
                ),
                NotGenerated.Accepted(
                    "oracle-kind-change-with-rows",
                    "Oracle の行のある列の型の種類の変更",
                    "Oracle は行のある列の型の種類を変えられない（ORA-01439）。生成器は変える列と揃える相手の値が全部 NULL か行 0 のときだけ種類を変える",
                    Expectations + " の行 `realdb-oracle-column-alter-non-empty-to-number-fails`"
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
                    "表の改名（大小以外）で古い表の一意制約の名前を引き継ぎ、古い表の削除と新しい表の追加を両方選ぶ",
                    "成否はセクションの順（表の削除を追加より前）だけで決まり、決まった形の行が 5 方言の実 DB で受け持つ。古い表の削除を選ばない形は計画が表の追加を外して名指しする分岐で、生成器の水準 new-table-takes-released-name と期待の行が受け持つ",
                    Expectations
                        + " の行 `realdb-rename-table-keeping-unique-name-drops-old-table-first` / `realdb-name-collision-new-table-keeps-unique-name-of-old-table-default-selection`"
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
