using QuickER.Model;

namespace QuickER.Mcp.Tools;

/// <summary>
/// 名前付きクエリツール（set_query / list_queries / remove_query）の実行結果を表す構造化型。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="QueryToolCore"/> が返す「面（MCP / 内蔵チャット）に依らない結果」。表示文字列は一切含めず、
/// 成否・状態・エラー／警告の種別（<see cref="QueryToolDiagnostic"/>）・成功時の結果データだけを持つ。
/// 文字列化（英語 / 日本語）は面側のフォーマッタ（<see cref="QueryToolEnglishFormatter"/> 等）の責務。
/// </para>
/// <para>
/// DSL パーサ・生 SQL アナライザの診断メッセージは既にローカライズ済みの文字列なので、
/// <see cref="QueryToolDiagnostic.Detail"/> に「データとして」載せてそのまま面へ渡す。
/// </para>
/// </remarks>
public sealed class QueryToolOutcome
{
    /// <summary>操作全体の成否（成功時のみ呼び出し側は保存する）</summary>
    public bool Success => Status == QueryToolStatus.Success;

    /// <summary>結果状態（成功・各種失敗の区別）。フォーマッタが整形の型を選ぶ判別子</summary>
    public QueryToolStatus Status { get; init; }

    /// <summary>対象テーブル名（入力そのまま。set / remove の文脈で使用）</summary>
    public string? TableName { get; init; }

    /// <summary>対象クエリ名（入力そのまま。set / remove の文脈で使用）</summary>
    public string? QueryName { get; init; }

    /// <summary>set_query 成功時: 既存を置換したなら true（Updated）、新規追加なら false（Added）</summary>
    public bool WasUpdate { get; init; }

    /// <summary>set_query 成功時の戻り形（表示は面側で小文字化する）</summary>
    public QueryReturnShape? Returns { get; init; }

    /// <summary>set_query 成功時の実装方式（表示は面側で小文字化する）</summary>
    public QueryImplementationKind? Implementation { get; init; }

    /// <summary>
    /// 検証失敗（<see cref="QueryToolStatus.ValidationFailed"/>）時のエラー一覧。累積順を保持する。
    /// </summary>
    public IReadOnlyList<QueryToolDiagnostic> Errors { get; init; } = [];

    /// <summary>set_query 成功時の警告一覧（保存は継続するが定義ミスの兆候）。累積順を保持する。</summary>
    public IReadOnlyList<QueryToolDiagnostic> Warnings { get; init; } = [];

    /// <summary>list_queries 成功時の一覧データ（それ以外は null）</summary>
    public QueryListing? Listing { get; init; }
}

/// <summary>名前付きクエリツールの結果状態</summary>
public enum QueryToolStatus
{
    /// <summary>成功</summary>
    Success,

    /// <summary>必須引数（table_name / query_name）が欠落している</summary>
    MissingArgument,

    /// <summary>returns の指定が無い、または不正</summary>
    InvalidReturns,

    /// <summary>implementation の指定が不正</summary>
    InvalidImplementation,

    /// <summary>指定テーブルが見つからない</summary>
    TableNotFound,

    /// <summary>構造・DSL・生 SQL 検証に失敗した（<see cref="QueryToolOutcome.Errors"/> に詳細）</summary>
    ValidationFailed,

    /// <summary>remove_query で対象クエリが見つからない</summary>
    QueryNotFound,
}

/// <summary>
/// 名前付きクエリ検証の診断 1 件（エラー／警告共通）。表示文字列ではなく「種別＋プレースホルダ値」で保持する。
/// </summary>
/// <param name="Code">診断の種別</param>
/// <param name="Name">対象パラメータ名 / 射影フィールド名（該当時）</param>
/// <param name="Column">対象列名（source_column / order_by 列など。該当時）</param>
/// <param name="Table">対象テーブル名（該当時）</param>
/// <param name="Dialect">対象 SQL 方言名（該当時）</param>
/// <param name="Detail">
/// ローカライズ済み診断メッセージ等の付随文字列（DSL パーサ / 生 SQL アナライザの診断、または order_by 誤用時の
/// 入力 returns 値）。既に各言語化された「データ」として面へそのまま渡す。
/// </param>
public sealed record QueryToolDiagnostic(
    QueryToolDiagnosticCode Code,
    string? Name = null,
    string? Column = null,
    string? Table = null,
    string? Dialect = null,
    string? Detail = null
);

/// <summary>名前付きクエリ検証の診断種別</summary>
public enum QueryToolDiagnosticCode
{
    /// <summary>パラメータに必須の name が無い</summary>
    ParameterMissingName,

    /// <summary>パラメータの type / source_column がちょうど 1 つでない</summary>
    ParameterTypeSourceExclusive,

    /// <summary>パラメータの source_column が所属テーブルに無い</summary>
    ParameterSourceColumnNotFound,

    /// <summary>returns=scalar なのに scalar_type が無い</summary>
    ScalarRequiresScalarType,

    /// <summary>returns=projection なのに result_type_name が無い</summary>
    ProjectionRequiresResultTypeName,

    /// <summary>returns=projection なのに fields が空</summary>
    ProjectionRequiresFields,

    /// <summary>order_by が list / single / projection 以外で使われている（<see cref="QueryToolDiagnostic.Detail"/>＝入力 returns）</summary>
    OrderByInvalidForReturnShape,

    /// <summary>order_by エントリに必須の column が無い</summary>
    OrderByMissingColumn,

    /// <summary>order_by の列が所属テーブルに無い</summary>
    OrderByColumnNotFound,

    /// <summary>射影フィールドに必須の name が無い</summary>
    ProjectionFieldMissingName,

    /// <summary>射影フィールドの type / source_column がちょうど 1 つでない</summary>
    ProjectionFieldTypeSourceExclusive,

    /// <summary>射影フィールドの source_column が所属テーブルに無い</summary>
    ProjectionFieldSourceColumnNotFound,

    /// <summary>未知の SQL 方言名</summary>
    UnknownSqlDialect,

    /// <summary>SQL 方言の値が文字列でない</summary>
    SqlDialectNotString,

    /// <summary>DSL 条件の診断（<see cref="QueryToolDiagnostic.Detail"/>＝ローカライズ済みメッセージ）</summary>
    ConditionDiagnostic,

    /// <summary>生 SQL の診断（<see cref="QueryToolDiagnostic.Dialect"/>＋<see cref="QueryToolDiagnostic.Detail"/>＝ローカライズ済みメッセージ）</summary>
    RawSqlDiagnostic,

    /// <summary>DSL 条件で宣言済みパラメータが未使用（警告）</summary>
    ParameterUnusedInCondition,
}

/// <summary>list_queries の一覧データ（テーブル別グループ＋総件数）</summary>
/// <param name="TotalCount">図に含まれる名前付きクエリの総数</param>
/// <param name="Groups">テーブル別のクエリグループ（エンティティ順・末尾に不明エンティティのグループ）</param>
public sealed record QueryListing(int TotalCount, IReadOnlyList<QueryListingGroup> Groups);

/// <summary>list_queries の 1 グループ（1 テーブル分のクエリ）</summary>
/// <param name="TableName">テーブル名（null＝参照先エンティティが不明なダングリンググループ）</param>
/// <param name="Queries">そのテーブルのクエリ一覧</param>
public sealed record QueryListingGroup(string? TableName, IReadOnlyList<QueryListingItem> Queries);

/// <summary>list_queries のクエリ 1 件の要約（列参照は名前へ解決済み）</summary>
public sealed record QueryListingItem(
    string Name,
    QueryReturnShape Returns,
    QueryImplementationKind Implementation,
    string? Description,
    string? ScalarType,
    string? Condition,
    IReadOnlyList<string> SqlDialects,
    IReadOnlyList<QueryListingParameter> Parameters,
    IReadOnlyList<QueryListingOrder> OrderBy,
    bool HasPaging,
    string? ResultTypeName,
    IReadOnlyList<QueryListingField> Fields
);

/// <summary>list_queries のパラメータ要約</summary>
/// <param name="Name">パラメータ名</param>
/// <param name="Type">型トークン（列参照でない場合。無ければ null）</param>
/// <param name="IsColumnReference">列参照型付けか（true なら <see cref="ColumnName"/> を使う）</param>
/// <param name="ColumnName">参照列名（解決できなければ null）</param>
/// <param name="IsList">リスト（IN）パラメータか</param>
public sealed record QueryListingParameter(
    string Name,
    string? Type,
    bool IsColumnReference,
    string? ColumnName,
    bool IsList
);

/// <summary>list_queries の並び順要約</summary>
/// <param name="ColumnName">並び替え列名（解決できなければ null）</param>
/// <param name="Descending">降順か</param>
public sealed record QueryListingOrder(string? ColumnName, bool Descending);

/// <summary>list_queries の射影フィールド要約</summary>
/// <param name="Name">フィールド名</param>
/// <param name="Type">型トークン（列参照でない場合。無ければ null）</param>
/// <param name="IsColumnReference">列参照か（true なら <see cref="ColumnName"/> を使う）</param>
/// <param name="ColumnName">参照列名（解決できなければ null）</param>
public sealed record QueryListingField(
    string Name,
    string? Type,
    bool IsColumnReference,
    string? ColumnName
);
