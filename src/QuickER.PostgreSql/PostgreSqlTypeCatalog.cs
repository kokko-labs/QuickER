using System.Globalization;
using System.Text.RegularExpressions;
using QuickER.Provider;

namespace QuickER.PostgreSql;

/// <summary>
/// PostgreSQL 向けの <see cref="ITypeCatalog"/> 実装。
/// ネイティブ型文字列（<c>varchar(100)</c> / <c>timestamp with time zone</c> 等）と正規型 <see cref="CanonicalType"/> を相互変換する。
/// </summary>
/// <remarks>
/// PostgreSQL は複数語からなる型名（<c>double precision</c> / <c>timestamp with time zone</c> 等）を持つため、
/// 型名の解析では空白を含む名称を正規化してから判定する。
/// <c>serial</c> / <c>bigserial</c> / <c>smallserial</c>・配列型（<c>型[]</c>）・<c>inet</c> / <c>cidr</c> / <c>macaddr</c>・
/// <c>interval</c>・幾何型（<c>point</c> 等）・<c>tsvector</c>・未知の型は正規型に対応する概念が無いため変換不能として扱う
/// （<see cref="TryParse"/> が <c>false</c> を返す）。
/// <c>numeric</c> / <c>json</c> / <c>char</c>（<c>character</c> / <c>bpchar</c>）・<c>timestamp without time zone</c> 等の別名は
/// 解析時に正規化して解釈し、<see cref="TryFormat"/> では代表型（<c>jsonb</c> 等）を出力する。
/// </remarks>
public sealed partial class PostgreSqlTypeCatalog : ITypeCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<string> DataTypes => PostgreSqlDataTypes.All;

    /// <inheritdoc />
    public string DefaultDataType => "integer";

    // 型名は英字・アンダースコアと空白を許容する（"double precision" 等の複数語型名に対応）。
    // 末尾の括弧内には長さ / 精度 / スケールを取る。配列型を弾くため型名側に "[" "]" は含めない。
    [GeneratedRegex(
        @"^\s*(?<name>[a-zA-Z_][a-zA-Z0-9_ ]*?)\s*(\(\s*(?<arg1>-?\d+)\s*(,\s*(?<arg2>-?\d+)\s*)?\))?\s*$",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex TypePattern();

    // 複数連続する空白を 1 個へ畳み込むための正規表現
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

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

        // 複数語型名（"double precision" 等）は空白を 1 個へ畳み込み、小文字化して別名解決する
        var rawName = WhitespacePattern()
            .Replace(match.Groups["name"].Value.Trim(), " ")
            .ToLowerInvariant();
        var name = NormalizeAlias(rawName);
        string? arg1 = match.Groups["arg1"].Success ? match.Groups["arg1"].Value : null;
        string? arg2 = match.Groups["arg2"].Success ? match.Groups["arg2"].Value : null;

        switch (name)
        {
            case "boolean":
                canonical = new CanonicalType(CanonicalTypeKind.Boolean);
                return true;

            case "smallint":
                canonical = new CanonicalType(CanonicalTypeKind.SmallInt);
                return true;

            case "integer":
                canonical = new CanonicalType(CanonicalTypeKind.Int32);
                return true;

            case "bigint":
                canonical = new CanonicalType(CanonicalTypeKind.Int64);
                return true;

            case "numeric":
                return TryParsePrecisionScale(arg1, arg2, out canonical);

            case "real":
                canonical = new CanonicalType(CanonicalTypeKind.Float32);
                return true;

            case "double precision":
                canonical = new CanonicalType(CanonicalTypeKind.Float64);
                return true;

            case "money":
                canonical = new CanonicalType(CanonicalTypeKind.Money);
                return true;

            case "varchar":
                // PostgreSQL の varchar は Unicode。正規型は String として扱う
                return TryParseLength(arg1, CanonicalTypeKind.String, out canonical);

            case "text":
                canonical = new CanonicalType(CanonicalTypeKind.String, Length: -1);
                return true;

            case "char":
                return TryParseLength(arg1, CanonicalTypeKind.FixedString, out canonical);

            case "bytea":
                canonical = new CanonicalType(CanonicalTypeKind.Binary, Length: -1);
                return true;

            case "date":
                canonical = new CanonicalType(CanonicalTypeKind.Date);
                return true;

            case "time":
                return TryParsePrecisionOnly(arg1, CanonicalTypeKind.Time, out canonical);

            case "timestamp":
                return TryParsePrecisionOnly(arg1, CanonicalTypeKind.DateTime, out canonical);

            case "timestamptz":
                return TryParsePrecisionOnly(arg1, CanonicalTypeKind.DateTimeOffset, out canonical);

            case "uuid":
                canonical = new CanonicalType(CanonicalTypeKind.Guid);
                return true;

            case "xml":
                canonical = new CanonicalType(CanonicalTypeKind.Xml);
                return true;

            case "json":
            case "jsonb":
                canonical = new CanonicalType(CanonicalTypeKind.Json);
                return true;

            default:
                // serial / 配列型 / inet / cidr / macaddr / interval / 幾何型 / tsvector / 未知の型は変換不能
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
                nativeType = "boolean";
                return true;

            // PostgreSQL に tinyint は無いため smallint で受ける
            case CanonicalTypeKind.TinyInt:
            case CanonicalTypeKind.SmallInt:
                nativeType = "smallint";
                return true;

            case CanonicalTypeKind.Int32:
                nativeType = "integer";
                return true;

            case CanonicalTypeKind.Int64:
                nativeType = "bigint";
                return true;

            case CanonicalTypeKind.Decimal:
                nativeType = FormatPrecisionScale("numeric", canonical.Precision, canonical.Scale);
                return true;

            case CanonicalTypeKind.Float32:
                nativeType = "real";
                return true;

            case CanonicalTypeKind.Float64:
                nativeType = "double precision";
                return true;

            case CanonicalTypeKind.Money:
                nativeType = "money";
                return true;

            // varchar は Unicode 可変長。max（-1）は text
            case CanonicalTypeKind.String:
            case CanonicalTypeKind.AnsiString:
                nativeType = FormatVarcharOrText(canonical.Length);
                return true;

            // 固定長は char。max（-1）は text
            case CanonicalTypeKind.FixedString:
            case CanonicalTypeKind.AnsiFixedString:
                nativeType = FormatCharOrText(canonical.Length);
                return true;

            // PostgreSQL のバイナリは長さの概念を持たない bytea 単一型
            case CanonicalTypeKind.Binary:
            case CanonicalTypeKind.FixedBinary:
                nativeType = "bytea";
                return true;

            case CanonicalTypeKind.Date:
                nativeType = "date";
                return true;

            case CanonicalTypeKind.Time:
                nativeType = FormatPrecisionOnly("time", canonical.Precision);
                return true;

            case CanonicalTypeKind.DateTime:
                nativeType = FormatPrecisionOnly("timestamp", canonical.Precision);
                return true;

            case CanonicalTypeKind.DateTimeOffset:
                nativeType = FormatPrecisionOnly("timestamptz", canonical.Precision);
                return true;

            case CanonicalTypeKind.Guid:
                nativeType = "uuid";
                return true;

            case CanonicalTypeKind.Xml:
                nativeType = "xml";
                return true;

            case CanonicalTypeKind.Json:
                nativeType = "jsonb";
                return true;

            default:
                return false;
        }
    }

    /// <summary>PostgreSQL の型別名を正規表記へ解決する（例: <c>character varying</c> → <c>varchar</c>）</summary>
    private static string NormalizeAlias(string name) =>
        name switch
        {
            "character varying" => "varchar",
            "character" => "char",
            "bpchar" => "char",
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
            // time with time zone / timetz は Time として解釈する（TryFormat では扱わない）
            "time with time zone" or "timetz" => "time",
            _ => name,
        };

    private static bool TryParseLength(
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
        string? arg1,
        string? arg2,
        out CanonicalType canonical
    )
    {
        if (arg1 is null)
        {
            canonical = new CanonicalType(CanonicalTypeKind.Decimal);
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

        canonical = new CanonicalType(CanonicalTypeKind.Decimal, Precision: precision, Scale: scale);
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

    /// <summary>可変長文字列を varchar(n) または text（max）へ整形する</summary>
    private static string FormatVarcharOrText(int? length)
    {
        if (length is null)
        {
            return "varchar";
        }

        return length == -1 ? "text" : $"varchar({length})";
    }

    /// <summary>固定長文字列を char(n) または text（max）へ整形する</summary>
    private static string FormatCharOrText(int? length)
    {
        if (length is null)
        {
            return "char";
        }

        return length == -1 ? "text" : $"char({length})";
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
