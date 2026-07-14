using System.Globalization;
using System.Text.RegularExpressions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Sqlite;

/// <summary>
/// SQLite の宣言型表記を C# 型へ変換するマッパー
/// </summary>
/// <remarks>
/// <para>
/// 本プロバイダは SQL Server 風のリッチな宣言型（<c>NVARCHAR(50)</c> / <c>DECIMAL(18,2)</c> / <c>DATETIME2</c> 等）を
/// 採用するため、<see cref="SqlServerCSharpTypeMapper"/> と同じ規則で C# 型へ寄せる。ただし SQLite は SQL Server の
/// <c>SqlDbType</c> を持たないため、<see cref="CSharpTypeInfo.SqlDbTypeName"/> は設定しない（PostgreSQL 版と同様）。
/// </para>
/// 対応規則（SQLite 宣言型 → C# 型）:
/// <list type="bullet">
/// <item><description>BIT → bool、TINYINT → byte、SMALLINT → short、INT → int、BIGINT/INTEGER → long（SQLite の INTEGER は 8 バイト格納のため）</description></item>
/// <item><description>REAL → float、FLOAT/DOUBLE → double</description></item>
/// <item><description>DECIMAL/NUMERIC/MONEY → decimal（精度・スケールを保持）</description></item>
/// <item><description>DATE/DATETIME/DATETIME2 → DateTime、TIME → TimeSpan、DATETIMEOFFSET → DateTimeOffset</description></item>
/// <item><description>UNIQUEIDENTIFIER → Guid</description></item>
/// <item><description>BINARY/VARBINARY/BLOB → byte[]（参照型）。長さ宣言なし（および (MAX)）は無制限バイナリ、BLOB(n) 等の長さ付きは有界</description></item>
/// <item><description>CHAR/VARCHAR/NCHAR/NVARCHAR/TEXT/XML/JSON → string（長さ指定があれば MaxLength として保持）</description></item>
/// <item><description>未知の型 → string（生成失敗を避けるための安全側フォールバック）</description></item>
/// </list>
/// 型名は大文字小文字を区別せず、"NVARCHAR(50)" のような長さ指定付き表記を受け付ける
/// </remarks>
public sealed partial class SqliteCSharpTypeMapper : IColumnTypeMapper
{
    /// <summary><see cref="IColumnTypeMapper"/> 実装。静的 <see cref="ResolveColumnTypes"/> へ委譲する</summary>
    IReadOnlyDictionary<Guid, CSharpTypeInfo> IColumnTypeMapper.ResolveColumnTypes(
        ErDiagram diagram
    ) => ResolveColumnTypes(diagram);

    /// <summary>
    /// ER 図の全カラムの SQLite 宣言型を解決し、カラム ID → C# 型情報の対応表を構築する。
    /// </summary>
    /// <remarks>
    /// コード生成器（<see cref="QuickER.CodeGen.CSharp.CSharpCodeGenerationService" />）は DB 非依存で、
    /// 解決済みの型情報を入力として受け取る。型解決という SQLite 固有の責務はこのライブラリが担う。
    /// </remarks>
    public static IReadOnlyDictionary<Guid, CSharpTypeInfo> ResolveColumnTypes(ErDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        var mapper = new SqliteCSharpTypeMapper();
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
    /// SQLite の宣言型表記を C# 型情報へ変換する
    /// </summary>
    /// <param name="dataType">SQLite の宣言型表記（例: "INT", "NVARCHAR(50)", "VARBINARY(MAX)"）</param>
    /// <returns>C# 型名・参照型区分・最大長を持つ型情報。未知の型は string にフォールバックする</returns>
    public CSharpTypeInfo Map(string dataType)
    {
        var normalized = Normalize(dataType);
        var baseType = ResolveAlias(GetBaseType(normalized));
        var maxLength = TryGetLength(normalized);
        var (precision, scale) = TryGetPrecisionScale(normalized);

        return baseType switch
        {
            "bit" => Value("bool"),
            "tinyint" => Value("byte"),
            "smallint" => Value("short"),
            "int" => Value("int"),
            "bigint" => Value("long"),
            "real" => Value("float"),
            "float" => Value("double"),
            "decimal" => Decimal(precision, scale),
            "money" => Value("decimal"),
            "date" or "datetime" or "datetime2" => Value("DateTime"),
            "time" => Value("TimeSpan"),
            "datetimeoffset" => Value("DateTimeOffset"),
            "uniqueidentifier" => Value("Guid"),
            // 長さ宣言なし（TryGetLength が null＝無指定・(MAX) の双方）は上限不明の無制限バイナリ、BLOB(n) 等は有界
            "binary" or "varbinary" or "blob" => Reference(
                "byte[]",
                isUnboundedBinary: maxLength is null
            ),
            // 文字列系のみ MaxLength を保持し、[MaxLength] 属性の生成に使う
            "char" or "varchar" or "nchar" or "nvarchar" or "text" or "xml" or "json" => Reference(
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
    /// <param name="maxLength">文字列型の最大長。長さ指定なし・MAX 指定の場合は null</param>
    /// <param name="isUnboundedBinary">無制限バイナリ（長さ宣言なし BLOB / varbinary(max) 等）かどうか</param>
    private static CSharpTypeInfo Reference(
        string typeName,
        int? maxLength = null,
        bool isUnboundedBinary = false
    ) =>
        new()
        {
            TypeName = typeName,
            IsReferenceType = true,
            MaxLength = maxLength,
            IsUnboundedBinary = isUnboundedBinary,
        };

    /// <summary>データ型表記を前後空白除去と小文字化で正規化する</summary>
    private static string Normalize(string dataType) => dataType.Trim().ToLowerInvariant();

    /// <summary>長さ指定の括弧を除いた基本型名を取り出す（例: "nvarchar(50)" → "nvarchar"）</summary>
    private static string GetBaseType(string normalizedDataType)
    {
        var parenIndex = normalizedDataType.IndexOf('(', StringComparison.Ordinal);
        return parenIndex < 0 ? normalizedDataType : normalizedDataType[..parenIndex].Trim();
    }

    /// <summary>SQLite / 他方言由来の型別名を代表表記へ解決する（例: <c>integer</c> → <c>int</c>）</summary>
    private static string ResolveAlias(string baseType) =>
        baseType switch
        {
            // SQLite の INTEGER は最大 8 バイト格納（EF Core も long 対応）のため Int64 として扱う
            "integer" or "int8" => "bigint",
            "int4" => "int",
            "int2" => "smallint",
            "boolean" or "bool" => "bit",
            "numeric" => "decimal",
            "double" or "double precision" => "float",
            "character varying" => "varchar",
            "character" => "char",
            "guid" => "uniqueidentifier",
            "timestamp" => "datetime2",
            _ => baseType,
        };

    /// <summary>
    /// 長さ指定から最大長を抽出する
    /// </summary>
    /// <returns>数値の長さ指定があればその値、"(MAX)" 指定や長さ指定なしの場合は null</returns>
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

    /// <summary>長さ指定 "(数値)" または "(MAX)" を検出する正規表現</summary>
    [GeneratedRegex(@"\((max|\d+)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LengthRegex();

    /// <summary>decimal/numeric の "(精度)" または "(精度,スケール)" を検出する正規表現</summary>
    [GeneratedRegex(@"\(\s*(\d+)\s*(?:,\s*(\d+)\s*)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex PrecisionScaleRegex();
}
