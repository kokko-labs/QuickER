namespace QuickER.CodeGen.CSharp;

/// <summary>
/// SQL Server 型から解決された C# 型の情報
/// </summary>
/// <remarks>
/// <para><see cref="SqlServerCSharpTypeMapper"/> が生成し、Nullable 注釈の付与や [MaxLength] 属性の判定に使う</para>
/// <para>
/// init-only の不変データキャリアであり、参照同一性に依存する箇所は無いため <c>record</c> とする。
/// 一部の項目だけを差し替えた複製（<see cref="MultiDialectTypeReconciler"/> の <c>[SqlColumnType]</c> 補完・
/// <c>CanonicalTypeTokenAttacher</c> のトークン付加）は <c>with</c> 式で作ること。
/// 全項目を列挙して <c>new</c> し直す書き方は、プロパティが増えたときに写し漏れても
/// コンパイルが通ってしまい、実際に <see cref="CanonicalTypeToken"/> が黙って消える不具合を起こした。
/// </para>
/// </remarks>
public sealed record CSharpTypeInfo
{
    /// <summary>C# の型名（例: int, string, byte[]）。Nullable 注釈 "?" は含まない</summary>
    public required string TypeName { get; init; }

    /// <summary>参照型かどうか。false の場合は値型で、NULL 許容時に "?" を付けて Nullable&lt;T&gt; にする</summary>
    public required bool IsReferenceType { get; init; }

    /// <summary>文字列型の最大長。varchar(max) や長さ指定なしの場合は null で、[MaxLength] 属性の生成可否を決める</summary>
    public int? MaxLength { get; init; }

    /// <summary>decimal/numeric の精度（全体桁数 p）。指定なしや非 decimal 型は null。値オブジェクトの桁数検証に使う</summary>
    public int? Precision { get; init; }

    /// <summary>decimal/numeric のスケール（小数桁数 s）。指定なしや非 decimal 型は null。値オブジェクトの桁数検証に使う</summary>
    public int? Scale { get; init; }

    /// <summary>
    /// SQL パラメータの型明示化に使う <c>System.Data.SqlDbType</c> の列挙名（例: "VarChar", "NVarChar", "Decimal", "Int"）。
    /// 未知の型・型を特定できない場合は null で、生成物側は AddWithValue にフォールバックする。
    /// </summary>
    /// <remarks>
    /// Generator は DB 非依存を保つため <c>SqlDbType</c> 型そのものは扱わず、列挙名を文字列で運ぶ。
    /// SqlDbType 化は生成物（テンプレート）内でのみ行う。
    /// </remarks>
    public string? SqlDbTypeName { get; init; }

    /// <summary>
    /// SQL パラメータ <c>SqlParameter.Size</c> に使う「宣言長」。文字列/バイナリの char(n)/varbinary(n) は n、
    /// <c>(max)</c> は -1、長さ指定なし（および Size を持たない型）は 0。<see cref="MaxLength"/> は <c>(max)</c> と
    /// 無指定をどちらも null にするため区別できないので、Size 判定用にこの三値を別途保持する。
    /// </summary>
    public int SqlDeclaredLength { get; init; }

    /// <summary>
    /// 行バージョン（SQL Server の <c>rowversion</c> / <c>timestamp</c> 等）かどうか。
    /// EF Core の Fluent 構成で <c>IsRowVersion()</c>（並行性トークン）を出すかの判定に使う。
    /// 生成器は DB 非依存のため、この判定はプロバイダ（型マッパー）が行って渡す
    /// </summary>
    public bool IsRowVersion { get; init; }

    /// <summary>
    /// 無制限バイナリ型（<c>varbinary(max)</c> / <c>image</c> / 長さ宣言なし BLOB / <c>bytea</c> 等）かどうか。
    /// </summary>
    /// <remarks>
    /// カラム定義（宣言）ベースの静的判定。<c>ExcludeUnboundedBinaryColumns</c> オプション有効時に
    /// 生成 Repository の SELECT / UPDATE 対象から除外する列の識別に使う（判定はプロバイダの型マッパーの責務）。
    /// <c>rowversion</c> / <c>timestamp</c> / <c>binary(n)</c> / <c>varbinary(n)</c> など有界のバイナリは対象外。
    /// </remarks>
    public bool IsUnboundedBinary { get; init; }

    /// <summary>
    /// DB 定義メタ属性（<c>[DbColumnMeta]</c>）へ刻む方言中立の型トークン（例 <c>"string(50)"</c> / <c>"decimal(10,2)"</c> / <c>"int32"</c>）。
    /// 型カタログで解析できない自由記述型は <c>null</c> で、属性を付与しない（黙って誤った型を刻まない）。
    /// </summary>
    /// <remarks>
    /// 値は図の方言の <c>ITypeCatalog.TryParse</c> → <c>CanonicalTypeToken.Format</c> で解決した中立トークンで、
    /// Generator は DB 非依存を保つためプロバイダ層（<c>DiagramCodeGenerator</c> の後処理）が付加する。
    /// canonical 由来のため、可搬図では各方言の型表記から同一トークンが得られる（EF Core 単独出力の方言可搬性を保つ）。
    /// </remarks>
    public string? CanonicalTypeToken { get; init; }
}
