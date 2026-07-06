using System.Globalization;
using System.Text.RegularExpressions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Oracle;

/// <summary>
/// Oracle のデータ型表記を C# 型へ変換するマッパー
/// </summary>
/// <remarks>
/// 対応規則（Oracle 型 → C# 型）:
/// <list type="bullet">
/// <item><description>NUMBER(1) → bool、NUMBER(3) → byte、NUMBER(5) → short、NUMBER(10) → int、NUMBER(19) → long</description></item>
/// <item><description>NUMBER(p,s)／その他の NUMBER(p)／NUMBER → decimal（精度・スケールを保持）</description></item>
/// <item><description>BINARY_FLOAT → float、BINARY_DOUBLE / FLOAT → double</description></item>
/// <item><description>NVARCHAR2 / VARCHAR2 / NCHAR / CHAR / NCLOB / CLOB / XMLTYPE → string（長さ指定があれば MaxLength として保持）</description></item>
/// <item><description>RAW / BLOB → byte[]（参照型）</description></item>
/// <item><description>DATE / TIMESTAMP → DateTime、TIMESTAMP WITH TIME ZONE → DateTimeOffset</description></item>
/// <item><description>未知の型 → string（生成失敗を避けるための安全側フォールバック）</description></item>
/// </list>
/// 型名は大文字小文字を区別せず、"NUMBER(10,2)" のような指定付き表記や "TIMESTAMP WITH TIME ZONE" 等の複数語型名を受け付ける
/// </remarks>
public sealed partial class OracleCSharpTypeMapper : IColumnTypeMapper
{
    /// <summary><see cref="IColumnTypeMapper"/> 実装。静的 <see cref="ResolveColumnTypes"/> へ委譲する</summary>
    IReadOnlyDictionary<Guid, CSharpTypeInfo> IColumnTypeMapper.ResolveColumnTypes(
        ErDiagram diagram
    ) => ResolveColumnTypes(diagram);

    /// <summary>
    /// ER 図の全カラムの Oracle 型を解決し、カラム ID → C# 型情報の対応表を構築する。
    /// </summary>
    /// <remarks>
    /// コード生成器（<see cref="QuickER.CodeGen.CSharp.CSharpCodeGenerationService" />）は DB 非依存で、
    /// 解決済みの型情報を入力として受け取る。型解決という Oracle 固有の責務はこのライブラリが担う。
    /// </remarks>
    public static IReadOnlyDictionary<Guid, CSharpTypeInfo> ResolveColumnTypes(ErDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        var mapper = new OracleCSharpTypeMapper();
        var result = new Dictionary<Guid, CSharpTypeInfo>();
        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                result[column.Id] = mapper.Map(column.DataType);
            }
        }

        return result;
    }

    /// <summary>
    /// Oracle のデータ型表記を C# 型情報へ変換する
    /// </summary>
    /// <param name="dataType">Oracle のデータ型表記（例: "NUMBER(10)", "VARCHAR2(50)", "TIMESTAMP WITH TIME ZONE"）</param>
    /// <returns>C# 型名・参照型区分・最大長を持つ型情報。未知の型は string にフォールバックする</returns>
    public CSharpTypeInfo Map(string dataType)
    {
        var normalized = Normalize(dataType);
        var baseType = GetBaseType(normalized);
        var maxLength = TryGetLength(normalized);
        var (precision, scale) = TryGetPrecisionScale(normalized);

        return baseType switch
        {
            "number" => MapNumber(precision, scale),
            "binary_float" => Value("float"),
            "binary_double" or "float" => Value("double"),
            // 日付時刻: WITH TIME ZONE のみ DateTimeOffset、それ以外は DateTime
            "timestamp with time zone" or "timestamp with local time zone" => Value(
                "DateTimeOffset"
            ),
            "date" or "timestamp" => Value("DateTime"),
            "raw" or "blob" or "long raw" => Reference("byte[]"),
            // 文字列系のみ MaxLength を保持し、[MaxLength] 属性の生成に使う
            "nvarchar2"
            or "varchar2"
            or "nchar"
            or "char"
            or "nclob"
            or "clob"
            or "xmltype"
            or "long" => Reference("string", maxLength),
            // 未知の型は string として扱い、生成自体は継続させる
            _ => Reference("string"),
        };
    }

    /// <summary>NUMBER の精度で C# 整数型へ振り分ける（型カタログと同じ規則）。該当しなければ decimal</summary>
    private static CSharpTypeInfo MapNumber(int? precision, int? scale)
    {
        // スケール付き（0 超）は固定小数点として decimal
        if (scale is > 0)
        {
            return Decimal(precision, scale);
        }

        return precision switch
        {
            1 => Value("bool"),
            3 => Value("byte"),
            5 => Value("short"),
            10 => Value("int"),
            19 => Value("long"),
            _ => Decimal(precision, scale),
        };
    }

    /// <summary>値型の型情報を作成する</summary>
    private static CSharpTypeInfo Value(string typeName) =>
        new() { TypeName = typeName, IsReferenceType = false };

    /// <summary>decimal 型の型情報を作成する（精度・スケールを保持し、値オブジェクトの桁数検証に使う）</summary>
    private static CSharpTypeInfo Decimal(int? precision, int? scale) =>
        new()
        {
            TypeName = "decimal",
            IsReferenceType = false,
            Precision = precision,
            Scale = scale,
        };

    /// <summary>参照型の型情報を作成する</summary>
    /// <param name="maxLength">文字列型の最大長。長さ指定なしの場合は null</param>
    private static CSharpTypeInfo Reference(string typeName, int? maxLength = null) =>
        new()
        {
            TypeName = typeName,
            IsReferenceType = true,
            MaxLength = maxLength,
        };

    /// <summary>データ型表記を前後空白除去・小文字化・空白畳み込みで正規化する</summary>
    private static string Normalize(string dataType) =>
        WhitespaceRegex().Replace(dataType.Trim().ToLowerInvariant(), " ");

    /// <summary>長さ・精度指定の括弧を除いた基本型名を取り出す（例: "varchar2(50)" → "varchar2"、"timestamp(6) with time zone" → "timestamp with time zone"）</summary>
    private static string GetBaseType(string normalizedDataType)
    {
        var open = normalizedDataType.IndexOf('(', StringComparison.Ordinal);

        if (open < 0)
        {
            return normalizedDataType;
        }

        var close = normalizedDataType.IndexOf(')', open);
        var head = normalizedDataType[..open].Trim();

        // 括弧の後ろに語が続く場合（TIMESTAMP(6) WITH TIME ZONE 等）は連結して基本型名とする
        if (close >= 0 && close + 1 < normalizedDataType.Length)
        {
            var tail = normalizedDataType[(close + 1)..].Trim();

            if (tail.Length > 0)
            {
                return (head + " " + tail).Trim();
            }
        }

        return head;
    }

    /// <summary>
    /// 長さ指定から最大長を抽出する
    /// </summary>
    /// <returns>数値の長さ指定があればその値、長さ指定なしの場合は null</returns>
    private static int? TryGetLength(string normalizedDataType)
    {
        var match = LengthRegex().Match(normalizedDataType);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(
            match.Groups[1].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var length
        )
            ? length
            : null;
    }

    /// <summary>
    /// NUMBER の精度・スケールを抽出する（例: "number(18,2)" → (18, 2)、"number(10)" → (10, null)）
    /// </summary>
    /// <returns>精度・スケール。指定が無い場合はそれぞれ null</returns>
    private static (int? Precision, int? Scale) TryGetPrecisionScale(string normalizedDataType)
    {
        var match = PrecisionScaleRegex().Match(normalizedDataType);
        if (!match.Success)
        {
            return (null, null);
        }

        int? precision = int.TryParse(
            match.Groups[1].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var p
        )
            ? p
            : null;
        int? scale =
            match.Groups[2].Success
            && int.TryParse(
                match.Groups[2].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var s
            )
                ? s
                : null;
        return (precision, scale);
    }

    /// <summary>連続する空白を検出する正規表現（複数語型名の畳み込みに使う）</summary>
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    /// <summary>長さ指定 "(数値)" を検出する正規表現</summary>
    [GeneratedRegex(@"\((\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex LengthRegex();

    /// <summary>NUMBER の "(精度)" または "(精度,スケール)" を検出する正規表現</summary>
    [GeneratedRegex(@"\(\s*(\d+)\s*(?:,\s*(\d+)\s*)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex PrecisionScaleRegex();
}
