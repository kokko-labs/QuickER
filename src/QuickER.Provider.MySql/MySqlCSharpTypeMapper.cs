using System.Globalization;
using System.Text.RegularExpressions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Provider.MySql;

/// <summary>
/// MySQL のデータ型表記を C# 型へ変換するマッパー
/// </summary>
/// <remarks>
/// 対応規則（MySQL 型 → C# 型）:
/// <list type="bullet">
/// <item><description>tinyint(1) / bool / boolean / bit(1) → bool、tinyint → sbyte、smallint → short、mediumint → int、int → int、bigint → long</description></item>
/// <item><description>符号なし整数（<c>unsigned</c>）→ tinyint は byte、smallint は ushort、mediumint / int は uint、bigint は ulong
/// （MySqlConnector・EF Core の <c>UseMySQL</c> とも同じ CLR 型を返す。実 mysql:8.4 で実測）。
/// <c>tinyint unsigned</c> には真偽値慣習を当てない（MySQL 8.4 は符号なし tinyint から表示幅を落とすため
/// <c>tinyint(1) unsigned</c> という表記が保たれず、ドライバも byte を返す。
/// <c>MySqlTypeCatalog.TryParseTinyInt</c> が Boolean 判定から unsigned を外しているのと同じ線引き）</description></item>
/// <item><description>bit(n)（n&gt;1）→ ulong、year → int（いずれも MySqlConnector が返す CLR 型に合わせる。実 mysql:8.4 で実測）</description></item>
/// <item><description>float → float、double → double</description></item>
/// <item><description>decimal / numeric → decimal</description></item>
/// <item><description>date / datetime → DateTime、time → TimeSpan、timestamp → DateTimeOffset</description></item>
/// <item><description>varbinary / binary / tinyblob / blob / mediumblob / longblob → byte[]（参照型）。blob/mediumblob/longblob は無制限バイナリ、tinyblob(255B)/binary(n)/varbinary(n) は有界</description></item>
/// <item><description>varchar / char / text 系 / json → string（長さ指定があれば MaxLength として保持）</description></item>
/// <item><description>未知の型 → string（生成失敗を避けるための安全側フォールバック。<c>CSharpTypeInfo.IsFallbackType</c> に true を刻む）</description></item>
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
        var (rawBaseType, isUnsigned) = GetBaseType(normalized);
        var baseType = ResolveAlias(rawBaseType);
        var maxLength = TryGetLength(normalized);
        var (precision, scale) = TryGetPrecisionScale(normalized);

        // tinyint(1) / bit(1) は真偽値慣習として bool へ寄せる
        // （MySqlConnector が bit(1) に返す CLR 型は ulong だが、1 ビットを真偽値として扱う慣習を優先する）。
        // 符号なしの tinyint は対象外＝MySQL 8.4 は tinyint unsigned から表示幅を落とすため
        // tinyint(1) unsigned という表記そのものが保たれず、ドライバも byte を返す
        if (
            (baseType == "tinyint" && !isUnsigned && maxLength == 1)
            || (baseType == "bit" && maxLength is null or 1)
        )
        {
            return Value("bool");
        }

        return baseType switch
        {
            "boolean" => Value("bool"),
            "tinyint" => Value(isUnsigned ? "byte" : "sbyte"),
            "smallint" => Value(isUnsigned ? "ushort" : "short"),
            "mediumint" => Value(isUnsigned ? "uint" : "int"),
            "int" => Value(isUnsigned ? "uint" : "int"),
            "bigint" => Value(isUnsigned ? "ulong" : "long"),
            // bit(n>1) は複数ビットのビットフィールド。MySqlConnector は幅に依らず ulong を返す
            // （bit(1) は上の真偽値慣習で先に bool へ寄せてある）
            "bit" => Value("ulong"),
            // year は 4 桁の年。MySqlConnector は int を返す
            "year" => Value("int"),
            "float" => Value("float"),
            "double" => Value("double"),
            "decimal" => Decimal(precision, scale),
            "date" or "datetime" => Value("DateTime"),
            "timestamp" => Value("DateTimeOffset"),
            "time" => Value("TimeSpan"),
            // tinyblob(255B) も byte[]。blob/mediumblob/longblob は上限不明の無制限バイナリ、tinyblob/binary(n)/varbinary(n) は有界
            "varbinary" or "binary" or "tinyblob" or "blob" or "mediumblob" or "longblob" =>
                Reference(
                    "byte[]",
                    isUnboundedBinary: baseType is "blob" or "mediumblob" or "longblob"
                ),
            // 文字列系のみ MaxLength を保持し、[MaxLength] 属性の生成に使う
            "varchar" or "char" or "text" or "mediumtext" or "longtext" or "json" => Reference(
                "string",
                maxLength
            ),
            // 未知の型は string として扱い、生成自体は継続させる
            _ => Reference("string", isFallbackType: true),
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
    /// <param name="isUnboundedBinary">無制限バイナリ（blob / mediumblob / longblob 等）かどうか</param>
    /// <param name="isFallbackType">型カタログが解析できず安全側の string フォールバックで決まったかどうか</param>
    private static CSharpTypeInfo Reference(
        string typeName,
        int? maxLength = null,
        bool isUnboundedBinary = false,
        bool isFallbackType = false
    ) =>
        new()
        {
            TypeName = typeName,
            IsReferenceType = true,
            MaxLength = maxLength,
            IsUnboundedBinary = isUnboundedBinary,
            IsFallbackType = isFallbackType,
        };

    /// <summary>データ型表記を前後空白除去・小文字化・空白畳み込みで正規化する</summary>
    private static string Normalize(string dataType) =>
        WhitespaceRegex().Replace(dataType.Trim().ToLowerInvariant(), " ");

    /// <summary>長さ指定の括弧・末尾修飾子を除いた基本型名と、符号なし修飾子の有無を取り出す</summary>
    /// <remarks>
    /// 例: <c>"int unsigned"</c> → <c>("int", true)</c>。符号は整数型の CLR 型（byte / ushort / uint / ulong）を
    /// 決めるため呼び出し側へ返す——落とすと符号付きの型で解決してしまい、範囲を超える値の読み出しが
    /// <c>OverflowException</c> になる。<c>zerofill</c> / <c>signed</c> は CLR 型に影響しないため従来どおり無視する。
    /// <c>unsigned</c> は<b>括弧の後ろ</b>に付く（<c>tinyint(1) unsigned</c>）ので、基本型名を取り出すための
    /// 「括弧以降の切り捨て」では判定できない。括弧の中身だけを落とした文字列から別に判定する
    /// （中身ごと落とすのは <c>enum('unsigned')</c> のような値リストを拾わないため）。
    /// </remarks>
    private static (string BaseType, bool IsUnsigned) GetBaseType(string normalizedDataType)
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

        var isUnsigned = UnsignedModifierRegex()
            .IsMatch(TypeArgumentsRegex().Replace(normalizedDataType, " "));

        return (name, isUnsigned);
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

    /// <summary>符号なし修飾子だけを検出する正規表現（<c>signed</c> に一致しないよう語境界で区切る）</summary>
    [GeneratedRegex(@"\bunsigned\b", RegexOptions.CultureInvariant)]
    private static partial Regex UnsignedModifierRegex();

    /// <summary>括弧の引数部（長さ・精度・enum / set の値リスト）を検出する正規表現</summary>
    [GeneratedRegex(@"\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex TypeArgumentsRegex();
}
