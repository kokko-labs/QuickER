namespace QuickER.Tests.TestSupport;

/// <summary>
/// 名前の総当たり（<see cref="NameFuzz"/>）の既知の失敗の一覧（厳密: 一覧に無い失敗も、一覧に載っているのに起きない失敗も赤）
/// </summary>
/// <remarks>
/// <para>
/// 各項目は、対応する指摘（三巡目レビューの summary の番号・記録の番号、22a で見つけたものは NA の番号）と、
/// 一覧から外す区切りを持つ。修正の区切りは該当する項目を消してから修正し、総当たりが緑になることで直ったことを確かめる。
/// </para>
/// <para>
/// キーの 3 つめは、重複の判定の失敗（duplicate-accepted / distinct-rejected）では名前の組の違い
/// （<see cref="NameDifference"/>）、受け付けた後の性質（lookup-wrong-target・stored-form-differs・upsert-rejected など）では
/// 組のどちらかが前後に空白を持つか（Plain / WithWhitespace）。
/// </para>
/// </remarks>
public static class NameFuzzKnownFailures
{
    private const string Fix22 = "区切り 22";

    private const string Undecided = "未定（ユーザー判断）";

    /// <summary>一覧</summary>
    public static readonly IReadOnlyList<KnownFuzzFailure> All =
    [
        // 44 / M1: テーブル名の重複の照合が空白を除く前の名前で行われ、setter が空白を除くので完全に同名のテーブルができる。
        // 照合が空白を除かないので、前後に空白のある名前では受け付けたテーブルを以後どのツールからも引けない
        .. Each(
            "44 / M1",
            [
                "mcp-tool:add_entity",
                "mcp-tool:set_entity_property",
                "gui-tool:add_entity",
                "gui-tool:set_entity_property",
            ],
            "duplicate-accepted",
            "Identical",
            "Whitespace",
            "WhitespaceAndCase"
        ),
        .. Each(
            "44 / M1",
            ["mcp-tool:add_entity", "mcp-tool:set_entity_property", "gui-tool:add_entity"],
            "lookup-wrong-target",
            "WithWhitespace"
        ),
        // M2（3r-mcp #3）: set_unique_constraint が既存の制約名との衝突を検査しない
        .. Each(
            "M2 / 3r-mcp #3",
            ["mcp-tool:set_unique_constraint", "gui-tool:set_unique_constraint"],
            "duplicate-accepted",
            "Identical",
            "Case"
        ),
        // N7（3r-mcp #5）: set_query が query_name を空白を除かずに照合・保存し、上書きのつもりの呼び出しが重複で断られる
        .. Each("N7 / 3r-mcp #5", ["query-tool:query_name"], "upsert-rejected", "WithWhitespace"),
        .. Each(
            "N7 / 3r-mcp #5",
            ["query-tool:query_name"],
            "stored-form-differs",
            "WithWhitespace"
        ),
        // 32 / C2: 射影のフィールド名の大小・空白だけの違いを受け付ける
        .. Each("32 / C2", ["query-tool:field", "codegen:field"], "duplicate-accepted", "Case"),
        .. Each(
            "32 / C2",
            ["query-dialog:field"],
            "duplicate-accepted",
            "Case",
            "WhitespaceAndCase"
        ),
        // NA1（22a で検出）: プロパティパネルの名前の書き換え（VM の setter）が重複の判定をしない（完全に同じ名前も受け付ける）
        .. Each(
            "NA1",
            ["gui-vm:EntityViewModel.TableName", "gui-vm:ColumnViewModel.Name"],
            "duplicate-accepted",
            "Identical",
            "Whitespace",
            "Case",
            "WhitespaceAndCase"
        ),
        // NA2（22a で検出）: DBML・Mermaid・Excel 定義書の取込が同じテーブル内の重複した列名を受け付ける（完全に同じ名前も）
        .. Each(
            "NA2",
            ["import:dbml:column", "import:excel:column"],
            "duplicate-accepted",
            "Identical",
            "Whitespace",
            "Case",
            "WhitespaceAndCase"
        ),
        .. Each(
            "NA2",
            ["import:mermaid:column"],
            "duplicate-accepted",
            "Identical",
            "Case",
            "WhitespaceAndCase"
        ),
        // NA3（22a で検出）: SQLite の DB 取込で、前後の空白だけ違うテーブル名が衝突の判定をすり抜け、
        // 空白を除いた同じ名前のテーブルが 2 つできる（警告も出ない）
        .. Each(
            "NA3",
            ["db-import:sqlite:table"],
            "duplicate-accepted",
            "Whitespace",
            "WhitespaceAndCase"
        ),
        // NA4（22a で検出）: C# 生成が、規則上は同じ名前（非 ASCII の大小違い・空白違い）のテーブル・列を診断せずに生成する
        .. Each(
            "NA4",
            ["codegen:table", "codegen:column"],
            "duplicate-accepted",
            "Case",
            "WhitespaceAndCase"
        ),
        // NA5（2026-10-08 の方針の決定＝列名・制約名も前後の空白を除いて保存し、空白を除いて照合する）: 列名・制約名の入口が
        // 空白を除かずに照合・保存する
        .. Each(
            "NA5",
            [
                "mcp-tool:add_column",
                "gui-tool:add_column",
                "mcp-tool:set_unique_constraint",
                "gui-tool:set_unique_constraint",
                "db-import:sqlite:column",
            ],
            "duplicate-accepted",
            "Whitespace",
            "WhitespaceAndCase"
        ),
        .. Each(
            "NA5",
            [
                "mcp-tool:add_column",
                "gui-tool:add_column",
                "mcp-tool:set_unique_constraint",
                "gui-tool:set_unique_constraint",
                "gui-vm:ColumnViewModel.Name",
                "import:dbml:column",
                "db-import:sqlite:column",
            ],
            "stored-form-differs",
            "WithWhitespace"
        ),
        // NA5 の DDL 側: MySQL は末尾が空白の識別子を断る（Incorrect column name / index name）。列名・一意制約名が空白を
        // 除かずに保存されるので、その図の DDL が適用できない
        .. Each(
            "NA5",
            ["ddl:mysql:Column", "ddl:mysql:UniqueConstraint"],
            "apply-failed",
            "Whitespace"
        ),
        // NA7（22a で検出・識別子の長さの上限）: 図は名前の長さを検査しないので、方言の上限を超える名前（と、名前から作る
        // 既定の制約名 PK_{テーブル} など）が DDL の適用で断られる（SQL Server・MySQL・Oracle）か、黙って切り詰められて
        // 取り込み直すと別の名前になる（PostgreSQL の 63 バイト）。上限の後ろでだけ違う 2 つの名前は切り詰めで同じ名前になり、
        // 2 つめの CREATE が既存として断られる（PostgreSQL は表名そのもの・マルチバイト・既定の主キー名、MySQL は既定の主キー名）
        .. EachIn(
            "NA7",
            [
                "ddl:sqlserver:Table",
                "ddl:sqlserver:Column",
                "ddl:sqlserver:UniqueConstraint",
                "ddl:sqlserver:ForeignKey",
                "ddl:mysql:Table",
                "ddl:mysql:Column",
                "ddl:mysql:UniqueConstraint",
                "ddl:mysql:ForeignKey",
                "ddl:oracle:Table",
                "ddl:oracle:Column",
                "ddl:oracle:UniqueConstraint",
                "ddl:oracle:ForeignKey",
            ],
            "apply-failed",
            Undecided,
            "Long"
        ),
        .. EachIn("NA7", ["ddl:postgresql:Table"], "diff:AddTable", Undecided, "Long"),
        .. EachIn("NA7", ["ddl:postgresql:Table"], "diff:DropTable", Undecided, "Long"),
        .. EachIn(
            "NA7",
            ["ddl:postgresql:Table", "ddl:postgresql:ForeignKey"],
            "diff:AddForeignKey",
            Undecided,
            "Long"
        ),
        .. EachIn(
            "NA7",
            ["ddl:postgresql:Table", "ddl:postgresql:ForeignKey"],
            "diff:DropForeignKey",
            Undecided,
            "Long"
        ),
        .. EachIn("NA7", ["ddl:postgresql:Column"], "diff:AddColumn", Undecided, "Long"),
        .. EachIn("NA7", ["ddl:postgresql:Column"], "diff:DropColumn", Undecided, "Long"),
        .. EachIn("NA7", ["ddl:postgresql:Column"], "diff:AlterForeignKey", Undecided, "Long"),
        .. EachIn(
            "NA7",
            ["ddl:postgresql:LongPair"],
            "apply-failed",
            Undecided,
            "DerivedConstraintName",
            "TableName",
            "MultibyteTableName"
        ),
        .. EachIn(
            "NA7",
            ["ddl:mysql:LongPair"],
            "apply-failed",
            Undecided,
            "DerivedConstraintName",
            "TableName"
        ),
        // CLAUDE.md の既知の残件（取込の忠実性の節の末尾）: 同じテーブル内で大文字小文字だけ違う列名は未対応。
        // SQLite は非 ASCII の大小を畳まないので Äpfel と äPFEL の列が共存し、そのまま取り込まれる。
        // 方針（2026-10-08）で重複として拒否する側へそろえる（取込はテーブル名と同じく後着を捨てて警告するのが第一案）
        .. Each(
            "既知の残件（CLAUDE.md）",
            ["db-import:sqlite:column"],
            "duplicate-accepted",
            "Case"
        ),
        // NA6（22a で検出・名前に依らない）: SQLite の QuickER 版 Repository の構成で、DbColumnMeta の XmlDoc が出力されない
        // SqlColumnTypeAttribute を cref で参照し、GenerateDocumentationFile の利用者ビルドで CS1574 の警告になる
        new(
            new("compile:SqlitePlain", "compile-warning", "CS1574"),
            "NA6",
            "区切り 23（生成コードの警告）"
        ),
    ];

    private static IEnumerable<KnownFuzzFailure> Each(
        string finding,
        string[] entrances,
        string property,
        params string[] differences
    ) => EachIn(finding, entrances, property, Fix22, differences);

    private static IEnumerable<KnownFuzzFailure> EachIn(
        string finding,
        string[] entrances,
        string property,
        string fixedIn,
        params string[] differences
    ) =>
        entrances.SelectMany(entrance =>
            differences.Select(difference => new KnownFuzzFailure(
                new FuzzFailureKey(entrance, property, difference),
                finding,
                fixedIn
            ))
        );
}
