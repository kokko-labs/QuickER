namespace QuickER.Provider;

/// <summary>
/// 方言間の型変換にのみ使う中立の正規型の種別。ファイルには保存されない。
/// </summary>
public enum CanonicalTypeKind
{
    /// <summary>真偽値</summary>
    Boolean,

    /// <summary>8 ビット整数</summary>
    TinyInt,

    /// <summary>16 ビット整数</summary>
    SmallInt,

    /// <summary>32 ビット整数</summary>
    Int32,

    /// <summary>64 ビット整数</summary>
    Int64,

    /// <summary>固定小数点数（<see cref="CanonicalType.Precision"/> / <see cref="CanonicalType.Scale"/> を使用）</summary>
    Decimal,

    /// <summary>単精度浮動小数点数</summary>
    Float32,

    /// <summary>倍精度浮動小数点数</summary>
    Float64,

    /// <summary>通貨型</summary>
    Money,

    /// <summary>Unicode 可変長文字列（<see cref="CanonicalType.Length"/> = -1 は max）</summary>
    String,

    /// <summary>非 Unicode 可変長文字列（<see cref="CanonicalType.Length"/> = -1 は max）</summary>
    AnsiString,

    /// <summary>Unicode 固定長文字列</summary>
    FixedString,

    /// <summary>非 Unicode 固定長文字列</summary>
    AnsiFixedString,

    /// <summary>可変長バイナリ（<see cref="CanonicalType.Length"/> = -1 は max）</summary>
    Binary,

    /// <summary>固定長バイナリ</summary>
    FixedBinary,

    /// <summary>日付</summary>
    Date,

    /// <summary>時刻（<see cref="CanonicalType.Precision"/> は小数秒桁を使用）</summary>
    Time,

    /// <summary>日時（<see cref="CanonicalType.Precision"/> は小数秒桁を使用）</summary>
    DateTime,

    /// <summary>タイムゾーン付き日時（<see cref="CanonicalType.Precision"/> は小数秒桁を使用）</summary>
    DateTimeOffset,

    /// <summary>GUID</summary>
    Guid,

    /// <summary>XML</summary>
    Xml,

    /// <summary>JSON</summary>
    Json,

    /// <summary>
    /// 行バージョン（SQL Server の <c>rowversion</c> / <c>timestamp</c>）。DB が採番する 8 バイトのバイナリ。
    /// </summary>
    /// <remarks>
    /// 「DB が採番する」という意味はこの種別を持つ方言（SQL Server）でしか再現できないため、
    /// 他方言の <see cref="ITypeCatalog.TryFormat"/> は「ただのバイナリ列」へ落とすか、対応がなければ <c>false</c> を返す
    /// （SQLite は BLOB へ落とす＝サーバー版のミラー置き場・PostgreSQL / MySQL / Oracle は変換不能のまま）。
    /// 落とした先では版ガードが働かないため、変換は非可逆（BLOB を SQL Server へ戻しても <c>varbinary</c> にしかならない）。
    /// </remarks>
    RowVersion,
}

/// <summary>
/// 方言間の型変換にのみ使う中立の正規型。ファイルには保存されない。
/// </summary>
/// <param name="Kind">正規型の種別</param>
/// <param name="Length">文字列・バイナリ系の長さ。<c>-1</c> は max（可変長の上限なし）を表す</param>
/// <param name="Precision">数値の精度、または時刻・日時系の小数秒桁</param>
/// <param name="Scale">数値のスケール（小数点以下桁数）</param>
public sealed record CanonicalType(
    CanonicalTypeKind Kind,
    int? Length = null,
    int? Precision = null,
    int? Scale = null
);

/// <summary>
/// 方言のネイティブ型と正規型の相互変換、および UI 型候補を提供するカタログ（DB 方言ごとに実装）。
/// </summary>
public interface ITypeCatalog
{
    /// <summary>この DBMS で選択可能なデータ型の一覧（UI の型候補・検証に使用）</summary>
    IReadOnlyList<string> DataTypes { get; }

    /// <summary>新規カラム追加時に用いる既定のデータ型（例: SQL Server は <c>int</c>）</summary>
    string DefaultDataType { get; }

    /// <summary>ネイティブ型文字列を正規型へ解析する。方言固有で対応不能な型は <c>false</c> を返す</summary>
    /// <param name="nativeType">解析対象のネイティブ型文字列（例: <c>nvarchar(100)</c>）</param>
    /// <param name="canonical">解析に成功した場合の正規型</param>
    bool TryParse(string nativeType, out CanonicalType canonical);

    /// <summary>正規型からこの方言のネイティブ型文字列を生成する</summary>
    /// <param name="canonical">変換元の正規型</param>
    /// <param name="nativeType">生成に成功した場合のネイティブ型文字列</param>
    bool TryFormat(CanonicalType canonical, out string nativeType);
}
