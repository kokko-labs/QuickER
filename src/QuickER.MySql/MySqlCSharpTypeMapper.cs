using System.Globalization;
using System.Text.RegularExpressions;
using QuickER.Generator;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.MySql;

/// <summary>
/// MySQL のデータ型表記を C# 型へ変換するマッパー
/// </summary>
/// <remarks>
/// 対応規則（MySQL 型 → C# 型）:
/// <list type="bullet">
/// <item><description>tinyint(1) / bool / boolean / bit(1) → bool、tinyint → sbyte、smallint → short、int → int、bigint → long</description></item>
/// <item><description>float → float、double → double</description></item>
/// <item><description>decimal / numeric → decimal</description></item>
/// <item><description>date / datetime → DateTime、time → TimeSpan、timestamp → DateTimeOffset</description></item>
/// <item><description>varbinary / binary / blob 系 → byte[]（参照型）</description></item>
/// <item><description>varchar / char / text 系 / json → string（長さ指定があれば MaxLength として保持）</description></item>
/// <item><description>未知の型 → string（生成失敗を避けるための安全側フォールバック）</description></item>
/// </list>
/// 型名は大文字小文字を区別せず、"varchar(255)" のような長さ指定付き表記や "double precision" 等の複数語型名、
/// "int unsigned" のような末尾修飾子を受け付ける
/// </remarks>
public sealed partial class MySqlCSharpTypeMapper : IColumnTypeMapper
{
    /// <summary><see cref="IColumnTypeMapper"/> 実装。静的 <see cref="ResolveColumnTypes"/> へ委譲する</summary>
    IReadOnlyDictionary<Guid, CSharpTypeInfo> IColumnTypeMapper.ResolveColumnTypes(
        ErDiagram diagram
    ) => ResolveColumnTypes(diagram);

    /// <summary>
    /// ER 図の全カラムの MySQL 型を解決し、カラム ID → C# 型情報の対応表を構築する。
    /// </summary>
    public static IReadOnlyDictionary<Guid, CSharpTypeInfo> ResolveColumnTypes(ErDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        var mapper = new MySqlCSharpTypeMapper();
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
    /// MySQL のデータ型表記を C# 型情報へ変換する
    /// </summary>
    /// <param name="dataType">MySQL のデータ型表記（例: "int", "varchar(255)", "tinyint(1)"）</param>
    /// <returns>C# 型名・参照型区分・最大長を持つ型情報。未知の型は string にフォールバックする</returns>
    public CSharpTypeInfo Map(string dataType)
    {
        var normalized = Normalize(dataType);
        var baseType = ResolveAlias(GetBaseType(normalized));
        var maxLength = TryGetLength(normalized);
        var (precision, scale) = TryGetPrecisionScale(normalized);

        // tinyint(1) / bit(1) は真偽値慣習として bool へ寄せる
        if (
            (baseType == "tinyint" && maxLength == 1)
            || (baseType == "bit" && maxLength is null or 1)
        )
        {
            return Value("bool");
        }

        return baseType switch
        {
            "boolean" => Value("bool"),
            "tinyint" => Value("sbyte"),
            "smallint" => Value("short"),
            "int" => Value("int"),
            "bigint" => Value("long"),
            "float" => Value("float"),
            "double" => Value("double"),
            "decimal" => Decimal(precision, scale),
            "date" or "datetime" => Value("DateTime"),
            "timestamp" => Value("DateTimeOffset"),
            "time" => Value("TimeSpan"),
            "varbinary" or "binary" or "blob" or "mediumblob" or "longblob" => Reference("byte[]"),
            // 文字列系のみ MaxLength を保持し、[MaxLength] 属性の生成に使う
            "varchar" or "char" or "text" or "mediumtext" or "longtext" or "json" => Reference(
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

    /// <summary>長さ指定の括弧・末尾修飾子を除いた基本型名を取り出す（例: "int unsigned" → "int"）</summary>
    private static string GetBaseType(string normalizedDataType)
    {
        var name = normalizedDataType;

        // 長さ / 精度の括弧以降を落とす（例: "varchar(255)" → "varchar"）
        var parenIndex = name.IndexOf('(', StringComparison.Ordinal);

        if (parenIndex >= 0)
        {
            name = name[..parenIndex].Trim();
        }

        // 末尾修飾子（unsigned / zerofill / signed）を除去する（例: "int unsigned" → "int"）
        name = ModifierRegex().Replace(name, "").Trim();

        return name;
    }

    /// <summary>MySQL の型別名を代表表記へ解決する（例: <c>integer</c> → <c>int</c>）</summary>
    private static string ResolveAlias(string baseType) =>
        baseType switch
        {
            "integer" => "int",
            "bool" => "boolean",
            "numeric" or "dec" or "fixed" => "decimal",
            "double precision" or "real" => "double",
            _ => baseType,
        };

    /// <summary>長さ指定から最大長を抽出する</summary>
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

    /// <summary>decimal の精度・スケールを抽出する（例: "decimal(18,2)" → (18, 2)）</summary>
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

    /// <summary>decimal の "(精度)" または "(精度,スケール)" を検出する正規表現</summary>
    [GeneratedRegex(@"\(\s*(\d+)\s*(?:,\s*(\d+)\s*)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex PrecisionScaleRegex();

    /// <summary>末尾修飾子（unsigned / zerofill / signed）を検出する正規表現</summary>
    [GeneratedRegex(@"\s*\b(unsigned|zerofill|signed)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ModifierRegex();
}
