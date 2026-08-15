using System.Globalization;
using System.Text.RegularExpressions;
using QuickER.Provider;

namespace QuickER.SqlServer;

/// <summary>
/// SQL Server 向けの <see cref="ITypeCatalog"/> 実装。
/// ネイティブ型文字列（<c>nvarchar(100)</c> 等）と正規型 <see cref="CanonicalType"/> を相互変換する。
/// </summary>
/// <remarks>
/// <c>hierarchyid</c> / <c>geography</c> / <c>geometry</c> / <c>sql_variant</c> は
/// 正規型に対応する概念が無いため変換不能として扱う（<see cref="TryParse"/> が <c>false</c> を返す）。
/// <c>timestamp</c> / <c>rowversion</c> は <see cref="CanonicalTypeKind.RowVersion"/> として解析し、
/// <see cref="TryFormat"/> は代表表記 <c>rowversion</c> を出力する（<c>timestamp</c> は非推奨の別名のため）。
/// <c>numeric</c> / <c>ntext</c> / <c>text</c> / <c>image</c> / <c>datetime</c> / <c>smalldatetime</c> / <c>smallmoney</c> は
/// 解析のみ対応する「parse-only」型で、<see cref="TryFormat"/> では代表型（<c>decimal</c> / <c>nvarchar(max)</c> /
/// <c>varchar(max)</c> / <c>varbinary(max)</c> / <c>datetime2</c> / <c>money</c>）を出力する。
/// </remarks>
public sealed partial class SqlServerTypeCatalog : ITypeCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<string> DataTypes => SqlServerDataTypes.All;

    /// <inheritdoc />
    public string DefaultDataType => "int";

    [GeneratedRegex(
        @"^\s*(?<name>[a-zA-Z_][a-zA-Z0-9_]*)\s*(\(\s*(?<arg1>max|-?\d+)\s*(,\s*(?<arg2>-?\d+)\s*)?\))?\s*$",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex TypePattern();

    /// <inheritdoc />
    public bool TryParse(string nativeType, out CanonicalType canonical)
    {
        canonical = null!;

        if (string.IsNullOrWhiteSpace(nativeType))
        {
            return false;
        }

        var match = TypePattern().Match(nativeType);

        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups["name"].Value.ToLowerInvariant();
        string? arg1 = match.Groups["arg1"].Success ? match.Groups["arg1"].Value : null;
        string? arg2 = match.Groups["arg2"].Success ? match.Groups["arg2"].Value : null;

        switch (name)
        {
            case "bit":
                canonical = new CanonicalType(CanonicalTypeKind.Boolean);
                return true;

            case "tinyint":
                canonical = new CanonicalType(CanonicalTypeKind.TinyInt);
                return true;

            case "smallint":
                canonical = new CanonicalType(CanonicalTypeKind.SmallInt);
                return true;

            case "int":
                canonical = new CanonicalType(CanonicalTypeKind.Int32);
                return true;

            case "bigint":
                canonical = new CanonicalType(CanonicalTypeKind.Int64);
                return true;

            case "decimal":
            case "numeric":
                return TryParsePrecisionScale(
                    name,
                    arg1,
                    arg2,
                    CanonicalTypeKind.Decimal,
                    out canonical
                );

            case "real":
                canonical = new CanonicalType(CanonicalTypeKind.Float32);
                return true;

            case "float":
                canonical = new CanonicalType(CanonicalTypeKind.Float64);
                return true;

            case "money":
            case "smallmoney":
                canonical = new CanonicalType(CanonicalTypeKind.Money);
                return true;

            case "nvarchar":
                return TryParseLength(name, arg1, CanonicalTypeKind.String, out canonical);

            case "ntext":
                canonical = new CanonicalType(CanonicalTypeKind.String, Length: -1);
                return true;

            case "varchar":
                return TryParseLength(name, arg1, CanonicalTypeKind.AnsiString, out canonical);

            case "text":
                canonical = new CanonicalType(CanonicalTypeKind.AnsiString, Length: -1);
                return true;

            case "nchar":
                return TryParseLength(name, arg1, CanonicalTypeKind.FixedString, out canonical);

            case "char":
                return TryParseLength(name, arg1, CanonicalTypeKind.AnsiFixedString, out canonical);

            case "varbinary":
                return TryParseLength(name, arg1, CanonicalTypeKind.Binary, out canonical);

            case "image":
                canonical = new CanonicalType(CanonicalTypeKind.Binary, Length: -1);
                return true;

            case "binary":
                return TryParseLength(name, arg1, CanonicalTypeKind.FixedBinary, out canonical);

            case "date":
                canonical = new CanonicalType(CanonicalTypeKind.Date);
                return true;

            case "time":
                return TryParsePrecisionOnly(arg1, CanonicalTypeKind.Time, out canonical);

            case "datetime2":
                return TryParsePrecisionOnly(arg1, CanonicalTypeKind.DateTime, out canonical);

            case "datetime":
            case "smalldatetime":
                canonical = new CanonicalType(CanonicalTypeKind.DateTime);
                return true;

            case "datetimeoffset":
                return TryParsePrecisionOnly(arg1, CanonicalTypeKind.DateTimeOffset, out canonical);

            case "uniqueidentifier":
                canonical = new CanonicalType(CanonicalTypeKind.Guid);
                return true;

            case "xml":
                canonical = new CanonicalType(CanonicalTypeKind.Xml);
                return true;

            // rowversion（別名 timestamp）は DB が採番する行バージョン列。他方言には同じ概念が無いが、
            // 「ミラー用のバイナリ列として持ち出せる」ようにするため正規型として解析する（変換先が持てない方言は TryFormat が false）
            case "rowversion":
            case "timestamp":
                canonical = new CanonicalType(CanonicalTypeKind.RowVersion);
                return true;

            default:
                // hierarchyid / geography / geometry / sql_variant / 未知の型は変換不能
                return false;
        }
    }

    /// <inheritdoc />
    public bool TryFormat(CanonicalType canonical, out string nativeType)
    {
        nativeType = string.Empty;

        if (canonical is null)
        {
            return false;
        }

        switch (canonical.Kind)
        {
            case CanonicalTypeKind.Boolean:
                nativeType = "bit";
                return true;

            case CanonicalTypeKind.TinyInt:
                nativeType = "tinyint";
                return true;

            case CanonicalTypeKind.SmallInt:
                nativeType = "smallint";
                return true;

            case CanonicalTypeKind.Int32:
                nativeType = "int";
                return true;

            case CanonicalTypeKind.Int64:
                nativeType = "bigint";
                return true;

            case CanonicalTypeKind.Decimal:
                nativeType = FormatPrecisionScale("decimal", canonical.Precision, canonical.Scale);
                return true;

            case CanonicalTypeKind.Float32:
                nativeType = "real";
                return true;

            case CanonicalTypeKind.Float64:
                nativeType = "float";
                return true;

            case CanonicalTypeKind.Money:
                nativeType = "money";
                return true;

            case CanonicalTypeKind.String:
                nativeType = FormatLength("nvarchar", canonical.Length);
                return true;

            case CanonicalTypeKind.AnsiString:
                nativeType = FormatLength("varchar", canonical.Length);
                return true;

            case CanonicalTypeKind.FixedString:
                nativeType = FormatLength("nchar", canonical.Length);
                return true;

            case CanonicalTypeKind.AnsiFixedString:
                nativeType = FormatLength("char", canonical.Length);
                return true;

            case CanonicalTypeKind.Binary:
                nativeType = FormatLength("varbinary", canonical.Length);
                return true;

            case CanonicalTypeKind.FixedBinary:
                nativeType = FormatLength("binary", canonical.Length);
                return true;

            case CanonicalTypeKind.Date:
                nativeType = "date";
                return true;

            case CanonicalTypeKind.Time:
                nativeType = FormatPrecisionOnly("time", canonical.Precision);
                return true;

            case CanonicalTypeKind.DateTime:
                nativeType = FormatPrecisionOnly("datetime2", canonical.Precision);
                return true;

            case CanonicalTypeKind.DateTimeOffset:
                nativeType = FormatPrecisionOnly("datetimeoffset", canonical.Precision);
                return true;

            case CanonicalTypeKind.Guid:
                nativeType = "uniqueidentifier";
                return true;

            case CanonicalTypeKind.Xml:
                nativeType = "xml";
                return true;

            case CanonicalTypeKind.Json:
                // SqlServerDataTypes.All に json 型が無いため、代替として nvarchar(max) を出力する
                nativeType = "nvarchar(max)";
                return true;

            case CanonicalTypeKind.RowVersion:
                // timestamp は同義の非推奨別名のため、代表表記 rowversion を出力する
                nativeType = "rowversion";
                return true;

            default:
                return false;
        }
    }

    private static bool TryParseLength(
        string name,
        string? arg1,
        CanonicalTypeKind kind,
        out CanonicalType canonical
    )
    {
        if (arg1 is null)
        {
            canonical = new CanonicalType(kind);
            return true;
        }

        if (string.Equals(arg1, "max", StringComparison.OrdinalIgnoreCase))
        {
            canonical = new CanonicalType(kind, Length: -1);
            return true;
        }

        // 負数・int 範囲外は変換不能として扱う（例外にしない）
        if (!TryParseTypeArg(arg1, out var length))
        {
            canonical = null!;
            return false;
        }

        canonical = new CanonicalType(kind, Length: length);
        return true;
    }

    private static bool TryParsePrecisionScale(
        string name,
        string? arg1,
        string? arg2,
        CanonicalTypeKind kind,
        out CanonicalType canonical
    )
    {
        if (arg1 is null)
        {
            canonical = new CanonicalType(kind);
            return true;
        }

        if (!TryParseTypeArg(arg1, out var precision))
        {
            canonical = null!;
            return false;
        }

        int? scale = null;

        if (arg2 is not null)
        {
            if (!TryParseTypeArg(arg2, out var parsedScale))
            {
                canonical = null!;
                return false;
            }

            scale = parsedScale;
        }

        canonical = new CanonicalType(kind, Precision: precision, Scale: scale);
        return true;
    }

    private static bool TryParsePrecisionOnly(
        string? arg1,
        CanonicalTypeKind kind,
        out CanonicalType canonical
    )
    {
        if (arg1 is null)
        {
            canonical = new CanonicalType(kind);
            return true;
        }

        if (!TryParseTypeArg(arg1, out var precision))
        {
            canonical = null!;
            return false;
        }

        canonical = new CanonicalType(kind, Precision: precision);
        return true;
    }

    /// <summary>型引数の数値を解析する。負数・int 範囲外は失敗（変換不能）として扱う</summary>
    private static bool TryParseTypeArg(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static string FormatLength(string name, int? length)
    {
        if (length is null)
        {
            return name;
        }

        return length == -1 ? $"{name}(max)" : $"{name}({length})";
    }

    private static string FormatPrecisionScale(string name, int? precision, int? scale)
    {
        if (precision is null)
        {
            return name;
        }

        return scale is null ? $"{name}({precision})" : $"{name}({precision},{scale})";
    }

    private static string FormatPrecisionOnly(string name, int? precision) =>
        precision is null ? name : $"{name}({precision})";
}
