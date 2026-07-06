using System.Globalization;
using System.Text.RegularExpressions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.SqlServer;

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
public sealed partial class SqlServerCSharpTypeMapper : IColumnTypeMapper
{
    /// <summary><see cref="IColumnTypeMapper"/> 実装。静的 <see cref="ResolveColumnTypes"/> へ委譲する</summary>
    IReadOnlyDictionary<Guid, CSharpTypeInfo> IColumnTypeMapper.ResolveColumnTypes(
        ErDiagram diagram
    ) => ResolveColumnTypes(diagram);

    /// <summary>
    /// ER 図の全カラムの SQL Server 型を解決し、カラム ID → C# 型情報の対応表を構築する。
    /// </summary>
    /// <remarks>
    /// コード生成器（<see cref="QuickER.CodeGen.CSharp.CSharpCodeGenerationService" />）は DB 非依存で、
    /// 解決済みの型情報を入力として受け取る。型解決という SQL Server 固有の責務はこのライブラリが担う。
    /// </remarks>
    public static IReadOnlyDictionary<Guid, CSharpTypeInfo> ResolveColumnTypes(ErDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        var mapper = new SqlServerCSharpTypeMapper();
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
        // SQL パラメータの型明示化に使う SqlDbType 列挙名を解決する（未知型は null でフォールバック）
        var sqlDbTypeName = ResolveSqlDbTypeName(baseType);

        return baseType switch
        {
            "bit" => Value("bool", sqlDbTypeName),
            "tinyint" => Value("byte", sqlDbTypeName),
            "smallint" => Value("short", sqlDbTypeName),
            "int" => Value("int", sqlDbTypeName),
            "bigint" => Value("long", sqlDbTypeName),
            // SQL Server の real は単精度、float は倍精度のため C# 側の対応が直感と逆になる点に注意
            "real" => Value("float", sqlDbTypeName),
            "float" => Value("double", sqlDbTypeName),
            "decimal" or "numeric" => Decimal(precision, scale, sqlDbTypeName),
            "money" or "smallmoney" => Value("decimal", sqlDbTypeName),
            "date" or "datetime" or "datetime2" or "smalldatetime" => Value(
                "DateTime",
                sqlDbTypeName
            ),
            "time" => Value("TimeSpan", sqlDbTypeName),
            "datetimeoffset" => Value("DateTimeOffset", sqlDbTypeName),
            "uniqueidentifier" => Value("Guid", sqlDbTypeName),
            "binary" or "varbinary" or "image" or "rowversion" or "timestamp" => Reference(
                "byte[]",
                sqlDbTypeName,
                maxLength: null,
                declaredLength: TryGetDeclaredLength(normalized),
                // rowversion / timestamp は行バージョン列。EF Core の IsRowVersion() 構成対象にする
                isRowVersion: baseType is "rowversion" or "timestamp"
            ),
            // 文字列系のみ MaxLength を保持し、[MaxLength] 属性の生成に使う
            "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "xml" => Reference(
                "string",
                sqlDbTypeName,
                maxLength,
                declaredLength: TryGetDeclaredLength(normalized)
            ),
            // 未知の型は string として扱い、生成自体は継続させる（SqlDbTypeName は null のまま＝AddWithValue フォールバック）
            _ => Reference("string", sqlDbTypeName: null),
        };
    }

    /// <summary>
    /// 基本型名を <c>System.Data.SqlDbType</c> の列挙名へ解決する（SQL パラメータの型明示化用）。
    /// </summary>
    /// <remarks>
    /// マッパー本体（<see cref="Map"/>）の分岐と対応させる。未知型は null を返し、生成物側で AddWithValue にフォールバックさせる。
    /// Generator は DB 非依存を保つため、ここでは <c>SqlDbType</c> 型そのものではなく列挙名を文字列で運ぶ。
    /// </remarks>
    private static string? ResolveSqlDbTypeName(string baseType) =>
        baseType switch
        {
            "char" => "Char",
            "varchar" => "VarChar",
            "nchar" => "NChar",
            "nvarchar" => "NVarChar",
            "text" => "Text",
            "ntext" => "NText",
            "xml" => "Xml",
            "decimal" or "numeric" => "Decimal",
            "money" => "Money",
            "smallmoney" => "SmallMoney",
            "bit" => "Bit",
            "tinyint" => "TinyInt",
            "smallint" => "SmallInt",
            "int" => "Int",
            "bigint" => "BigInt",
            "float" => "Float",
            "real" => "Real",
            "date" => "Date",
            "time" => "Time",
            "datetime" => "DateTime",
            "datetime2" => "DateTime2",
            "smalldatetime" => "SmallDateTime",
            "datetimeoffset" => "DateTimeOffset",
            "uniqueidentifier" => "UniqueIdentifier",
            "binary" => "Binary",
            "varbinary" => "VarBinary",
            "image" => "Image",
            "rowversion" or "timestamp" => "Timestamp",
            _ => null,
        };

    /// <summary>値型の型情報を作成する</summary>
    private static CSharpTypeInfo Value(string typeName, string? sqlDbTypeName) =>
        new()
        {
            TypeName = typeName,
            IsReferenceType = false,
            SqlDbTypeName = sqlDbTypeName,
        };

    /// <summary>decimal 型の型情報を作成する（精度・スケールを保持し、値オブジェクトの桁数検証に使う）</summary>
    private static CSharpTypeInfo Decimal(int? precision, int? scale, string? sqlDbTypeName) =>
        new()
        {
            TypeName = "decimal",
            IsReferenceType = false,
            Precision = precision,
            Scale = scale,
            SqlDbTypeName = sqlDbTypeName,
        };

    /// <summary>参照型の型情報を作成する</summary>
    /// <param name="sqlDbTypeName">SQL パラメータ型明示化用の SqlDbType 列挙名。未知型は null</param>
    /// <param name="maxLength">文字列型の最大長。長さ指定なし・max 指定の場合は null</param>
    /// <param name="declaredLength">SqlParameter.Size 用の宣言長（n / max=-1 / 無指定=0）</param>
    private static CSharpTypeInfo Reference(
        string typeName,
        string? sqlDbTypeName,
        int? maxLength = null,
        int declaredLength = 0,
        bool isRowVersion = false
    ) =>
        new()
        {
            TypeName = typeName,
            IsReferenceType = true,
            MaxLength = maxLength,
            SqlDbTypeName = sqlDbTypeName,
            SqlDeclaredLength = declaredLength,
            IsRowVersion = isRowVersion,
        };

    /// <summary>
    /// 文字列/バイナリの宣言長を三値で取り出す（SqlParameter.Size 用）。n → n、"(max)" → -1、長さ指定なし → 0。
    /// </summary>
    private static int TryGetDeclaredLength(string normalizedDataType)
    {
        var match = LengthRegex().Match(normalizedDataType);
        if (!match.Success)
        {
            return 0;
        }

        if (match.Groups[1].Value.Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        return int.TryParse(
            match.Groups[1].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var length
        )
            ? length
            : 0;
    }

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
