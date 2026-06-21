using System.Globalization;
using System.Text.RegularExpressions;

namespace ERDesigner.Generator;

/// <summary>
/// SQL Server のデータ型表記を C# 型へ変換するマッパー
/// </summary>
/// <remarks>
/// 対応規則（SQL Server 型 → C# 型）:
/// <list type="bullet">
/// <item><description>bit → bool、tinyint → byte、smallint → short、int → int、bigint → long</description></item>
/// <item><description>real → float、float → double（SQL の float は倍精度のため）</description></item>
/// <item><description>decimal / numeric / money / smallmoney → decimal</description></item>
/// <item><description>date / datetime / datetime2 / smalldatetime → DateTime、time → TimeSpan、datetimeoffset → DateTimeOffset</description></item>
/// <item><description>uniqueidentifier → Guid</description></item>
/// <item><description>binary / varbinary / image / rowversion / timestamp → byte[]（参照型）</description></item>
/// <item><description>char / varchar / nchar / nvarchar / text / ntext / xml → string（長さ指定があれば MaxLength として保持）</description></item>
/// <item><description>未知の型 → string（生成失敗を避けるための安全側フォールバック）</description></item>
/// </list>
/// 型名は大文字小文字を区別せず、"nvarchar(50)" のような長さ指定付き表記を受け付ける
/// </remarks>
internal sealed partial class SqlServerCSharpTypeMapper
{
    /// <summary>
    /// SQL Server のデータ型表記を C# 型情報へ変換する
    /// </summary>
    /// <param name="dataType">SQL Server のデータ型表記（例: "int", "nvarchar(50)", "varbinary(max)"）</param>
    /// <returns>C# 型名・参照型区分・最大長を持つ型情報。未知の型は string にフォールバックする</returns>
    public CSharpTypeInfo Map(string dataType)
    {
        var normalized = Normalize(dataType);
        var baseType = GetBaseType(normalized);
        var maxLength = TryGetLength(normalized);
        var (precision, scale) = TryGetPrecisionScale(normalized);

        return baseType switch
        {
            "bit" => Value("bool"),
            "tinyint" => Value("byte"),
            "smallint" => Value("short"),
            "int" => Value("int"),
            "bigint" => Value("long"),
            // SQL Server の real は単精度、float は倍精度のため C# 側の対応が直感と逆になる点に注意
            "real" => Value("float"),
            "float" => Value("double"),
            "decimal" or "numeric" => Decimal(precision, scale),
            "money" or "smallmoney" => Value("decimal"),
            "date" or "datetime" or "datetime2" or "smalldatetime" => Value("DateTime"),
            "time" => Value("TimeSpan"),
            "datetimeoffset" => Value("DateTimeOffset"),
            "uniqueidentifier" => Value("Guid"),
            "binary" or "varbinary" or "image" or "rowversion" or "timestamp" => Reference(
                "byte[]"
            ),
            // 文字列系のみ MaxLength を保持し、[MaxLength] 属性の生成に使う
            "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "xml" => Reference(
                "string",
                maxLength
            ),
            // 未知の型は string として扱い、生成自体は継続させる
            _ => Reference("string"),
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
    /// <param name="maxLength">文字列型の最大長。長さ指定なし・max 指定の場合は null</param>
    private static CSharpTypeInfo Reference(string typeName, int? maxLength = null) =>
        new()
        {
            TypeName = typeName,
            IsReferenceType = true,
            MaxLength = maxLength,
        };

    /// <summary>データ型表記を前後空白除去と小文字化で正規化する</summary>
    private static string Normalize(string dataType) => dataType.Trim().ToLowerInvariant();

    /// <summary>長さ指定の括弧を除いた基本型名を取り出す（例: "nvarchar(50)" → "nvarchar"）</summary>
    private static string GetBaseType(string normalizedDataType)
    {
        var parenIndex = normalizedDataType.IndexOf('(', StringComparison.Ordinal);
        return parenIndex < 0 ? normalizedDataType : normalizedDataType[..parenIndex].Trim();
    }

    /// <summary>
    /// 長さ指定から最大長を抽出する
    /// </summary>
    /// <returns>数値の長さ指定があればその値、"(max)" 指定や長さ指定なしの場合は null</returns>
    private static int? TryGetLength(string normalizedDataType)
    {
        var match = LengthRegex().Match(normalizedDataType);
        if (
            !match.Success
            || match.Groups[1].Value.Equals("max", StringComparison.OrdinalIgnoreCase)
        )
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
    /// decimal/numeric の精度・スケールを抽出する（例: "decimal(18,2)" → (18, 2)、"decimal(10)" → (10, null)）
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

    /// <summary>長さ指定 "(数値)" または "(max)" を検出する正規表現</summary>
    [GeneratedRegex(@"\((max|\d+)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LengthRegex();

    /// <summary>decimal/numeric の "(精度)" または "(精度,スケール)" を検出する正規表現</summary>
    [GeneratedRegex(@"\(\s*(\d+)\s*(?:,\s*(\d+)\s*)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex PrecisionScaleRegex();
}
