using System.Globalization;
using System.Text.RegularExpressions;
using QuickER.Generator;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.PostgreSql;

/// <summary>
/// PostgreSQL のデータ型表記を C# 型へ変換するマッパー
/// </summary>
/// <remarks>
/// 対応規則（PostgreSQL 型 → C# 型）:
/// <list type="bullet">
/// <item><description>boolean → bool、smallint → short、integer → int、bigint → long</description></item>
/// <item><description>real → float、double precision → double</description></item>
/// <item><description>numeric / decimal / money → decimal</description></item>
/// <item><description>date / timestamp → DateTime、time → TimeSpan、timestamptz → DateTimeOffset</description></item>
/// <item><description>uuid → Guid</description></item>
/// <item><description>bytea → byte[]（参照型）</description></item>
/// <item><description>varchar / char / text / xml / json / jsonb → string（長さ指定があれば MaxLength として保持）</description></item>
/// <item><description>未知の型 → string（生成失敗を避けるための安全側フォールバック）</description></item>
/// </list>
/// 型名は大文字小文字を区別せず、"varchar(50)" のような長さ指定付き表記や "double precision" 等の複数語型名を受け付ける
/// </remarks>
public sealed partial class PostgreSqlCSharpTypeMapper : IColumnTypeMapper
{
    /// <summary><see cref="IColumnTypeMapper"/> 実装。静的 <see cref="ResolveColumnTypes"/> へ委譲する</summary>
    IReadOnlyDictionary<Guid, CSharpTypeInfo> IColumnTypeMapper.ResolveColumnTypes(
        ErDiagram diagram
    ) => ResolveColumnTypes(diagram);

    /// <summary>
    /// ER 図の全カラムの PostgreSQL 型を解決し、カラム ID → C# 型情報の対応表を構築する。
    /// </summary>
    /// <remarks>
    /// コード生成器（<see cref="QuickER.Generator.CSharpCodeGenerationService" />）は DB 非依存で、
    /// 解決済みの型情報を入力として受け取る。型解決という PostgreSQL 固有の責務はこのライブラリが担う。
    /// </remarks>
    public static IReadOnlyDictionary<Guid, CSharpTypeInfo> ResolveColumnTypes(ErDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        var mapper = new PostgreSqlCSharpTypeMapper();
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
    /// PostgreSQL のデータ型表記を C# 型情報へ変換する
    /// </summary>
    /// <param name="dataType">PostgreSQL のデータ型表記（例: "integer", "varchar(50)", "timestamptz"）</param>
    /// <returns>C# 型名・参照型区分・最大長を持つ型情報。未知の型は string にフォールバックする</returns>
    public CSharpTypeInfo Map(string dataType)
    {
        var normalized = Normalize(dataType);
        var baseType = ResolveAlias(GetBaseType(normalized));
        var maxLength = TryGetLength(normalized);
        var (precision, scale) = TryGetPrecisionScale(normalized);

        return baseType switch
        {
            "boolean" => Value("bool"),
            "smallint" => Value("short"),
            "integer" => Value("int"),
            "bigint" => Value("long"),
            "real" => Value("float"),
            "double precision" => Value("double"),
            "numeric" => Decimal(precision, scale),
            "money" => Value("decimal"),
            "date" or "timestamp" or "timestamptz" => baseType == "timestamptz"
                ? Value("DateTimeOffset")
                : Value("DateTime"),
            "time" => Value("TimeSpan"),
            "uuid" => Value("Guid"),
            "bytea" => Reference("byte[]"),
            // 文字列系のみ MaxLength を保持し、[MaxLength] 属性の生成に使う
            "varchar" or "char" or "text" or "xml" or "json" or "jsonb" => Reference(
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

    /// <summary>長さ指定の括弧を除いた基本型名を取り出す（例: "varchar(50)" → "varchar"）</summary>
    private static string GetBaseType(string normalizedDataType)
    {
        var parenIndex = normalizedDataType.IndexOf('(', StringComparison.Ordinal);
        return parenIndex < 0 ? normalizedDataType : normalizedDataType[..parenIndex].Trim();
    }

    /// <summary>PostgreSQL の型別名を代表表記へ解決する（例: <c>int4</c> → <c>integer</c>）</summary>
    private static string ResolveAlias(string baseType) =>
        baseType switch
        {
            "character varying" => "varchar",
            "character" or "bpchar" => "char",
            "int" or "int4" => "integer",
            "int2" => "smallint",
            "int8" => "bigint",
            "bool" => "boolean",
            "float4" => "real",
            "float8" => "double precision",
            "decimal" => "numeric",
            "timestamp without time zone" => "timestamp",
            "timestamp with time zone" => "timestamptz",
            "time without time zone" => "time",
            "time with time zone" or "timetz" => "time",
            _ => baseType,
        };

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
    /// numeric の精度・スケールを抽出する（例: "numeric(18,2)" → (18, 2)、"numeric(10)" → (10, null)）
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

    /// <summary>numeric の "(精度)" または "(精度,スケール)" を検出する正規表現</summary>
    [GeneratedRegex(@"\(\s*(\d+)\s*(?:,\s*(\d+)\s*)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex PrecisionScaleRegex();
}
