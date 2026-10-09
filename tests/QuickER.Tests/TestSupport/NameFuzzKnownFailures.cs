namespace QuickER.Tests.TestSupport;

/// <summary>
/// 名前の総当たり（<see cref="NameFuzz"/>）の既知の失敗の一覧（厳密: 一覧に無い失敗も、一覧に載っているのに起きない失敗も赤）
/// </summary>
/// <remarks>
/// <para>
/// 各項目は、対応する指摘（三巡目レビューの summary の番号・記録の番号、22a で見つけたものは NA の番号）と、
/// 一覧から外す区切りを持つ。修正の区切りは該当する項目を消してから修正し、総当たりが緑になることで直ったことを確かめる。
/// 区切り 22 で名前の同一性の項目（44・M2・N7・32・NA1〜NA5・取込の既知の残件）は直して一覧から外した。残るのは別の区切りの項目だけ。
/// </para>
/// <para>
/// キーの 3 つめは、重複の判定の失敗（duplicate-accepted / distinct-rejected）では名前の組の違い
/// （<see cref="NameDifference"/>）、受け付けた後の性質（lookup-wrong-target・stored-form-differs・upsert-rejected など）では
/// 組のどちらかが前後に空白を持つか（Plain / WithWhitespace）。
/// </para>
/// </remarks>
public static class NameFuzzKnownFailures
{
    private const string Undecided = "未定（ユーザー判断）";

    /// <summary>一覧</summary>
    public static readonly IReadOnlyList<KnownFuzzFailure> All =
    [
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
        // NA6（22a で検出・名前に依らない）: SQLite の QuickER 版 Repository の構成で、DbColumnMeta の XmlDoc が出力されない
        // SqlColumnTypeAttribute を cref で参照し、GenerateDocumentationFile の利用者ビルドで CS1574 の警告になる
        new(
            new("compile:SqlitePlain", "compile-warning", "CS1574"),
            "NA6",
            "区切り 23（生成コードの警告）"
        ),
    ];

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
