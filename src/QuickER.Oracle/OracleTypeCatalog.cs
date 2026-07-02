using System.Globalization;
using System.Text.RegularExpressions;
using QuickER.Provider;

namespace QuickER.Oracle;

/// <summary>
/// Oracle 向けの <see cref="ITypeCatalog"/> 実装。
/// ネイティブ型文字列（<c>NUMBER(10)</c> / <c>TIMESTAMP(6) WITH TIME ZONE</c> 等）と正規型 <see cref="CanonicalType"/> を相互変換する。
/// </summary>
/// <remarks>
/// <para>
/// Oracle は複数語からなる型名（<c>TIMESTAMP WITH TIME ZONE</c> / <c>BINARY_DOUBLE</c> 等）を持つため、
/// 型名の解析では空白を含む名称を正規化してから判定する。
/// </para>
/// <para>
/// <c>NUMBER</c> は精度・スケールで正規型を振り分けるのが特徴的である。
/// <c>NUMBER(1)</c>=Boolean / <c>NUMBER(3)</c>=TinyInt / <c>NUMBER(5)</c>=SmallInt / <c>NUMBER(10)</c>=Int32 /
/// <c>NUMBER(19)</c>=Int64 を整数型として扱い、スケール付き（s&gt;0）や上記以外の精度は <c>Decimal(p,s)</c> として扱う。
/// </para>
/// <para>
/// いくつかの変換は非対称である。
/// <c>DATE</c> は取込では時刻を含むため <see cref="CanonicalTypeKind.DateTime"/> へ寄せるが、
/// 正規型 <see cref="CanonicalTypeKind.Date"/> は <c>DATE</c> へ書き戻す。
/// <see cref="CanonicalTypeKind.Guid"/> は <c>RAW(16)</c> として書き出すのみで、解釈から Guid は生まれない（format-only）。
/// <see cref="CanonicalTypeKind.Time"/> は Oracle に TIME 型が無いため <see cref="TryFormat"/> できず、
/// 方言切替時は変換不能一覧に載る。
/// <c>XMLTYPE</c> は <see cref="CanonicalTypeKind.Xml"/> と相互変換するが、
/// <see cref="CanonicalTypeKind.Json"/> は 19c に JSON 型が無いため <c>CLOB</c> として書き出すのみ（format-only）。
/// </para>
/// <para>
/// <c>LONG</c> / <c>LONG RAW</c> / <c>ROWID</c> / <c>UROWID</c> / <c>BFILE</c> / <c>INTERVAL</c> 系 /
/// <c>SDO_GEOMETRY</c>・未知の型は正規型に対応する概念が無いため変換不能として扱う（<see cref="TryParse"/> が <c>false</c> を返す）。
/// </para>
/// </remarks>
public sealed partial class OracleTypeCatalog : ITypeCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<string> DataTypes => OracleDataTypes.All;

    /// <inheritdoc />
    public string DefaultDataType => "NUMBER(10)";

    // 型名は英字・アンダースコアと空白を許容する（"TIMESTAMP WITH TIME ZONE" 等の複数語型名に対応）。
    // 括弧内には長さ / 精度 / スケールを取る。TIMESTAMP は "TIMESTAMP(6) WITH TIME ZONE" のように
    // 括弧が名称の途中に入るため、suffix（括弧後の語）は括弧グループの内側にのみ現れるよう定義する。
    // こうすることで、括弧が無い単語型（"BINARY_FLOAT" 等）は name が全体を取り込む。
    [GeneratedRegex(
        @"^\s*(?<name>[a-zA-Z_][a-zA-Z0-9_ ]*?)\s*(\(\s*(?<arg1>-?\d+)\s*(,\s*(?<arg2>-?\d+)\s*)?\)\s*(?<suffix>[a-zA-Z][a-zA-Z0-9_ ]*?)?)?\s*$",
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

        // 複数語型名（"TIMESTAMP WITH TIME ZONE" 等）は括弧の前後を連結し、空白を 1 個へ畳み込み小文字化する
        var namePart = match.Groups["name"].Value.Trim();
        var suffixPart = match.Groups["suffix"].Success ? match.Groups["suffix"].Value.Trim() : "";
        var rawName = WhitespacePattern()
            .Replace((namePart + " " + suffixPart).Trim(), " ")
            .ToLowerInvariant();
        string? arg1 = match.Groups["arg1"].Success ? match.Groups["arg1"].Value : null;
        string? arg2 = match.Groups["arg2"].Success ? match.Groups["arg2"].Value : null;

        switch (rawName)
        {
            case "number":
                return TryParseNumber(arg1, arg2, out canonical);

            case "binary_float":
                canonical = new CanonicalType(CanonicalTypeKind.Float32);
                return true;

            case "binary_double":
                canonical = new CanonicalType(CanonicalTypeKind.Float64);
                return true;

            // FLOAT(b) は 2 進精度指定だが、正規型では Float64 として解釈する（解釈のみ）
            case "float":
                canonical = new CanonicalType(CanonicalTypeKind.Float64);
                return true;

            case "nvarchar2":
                return TryParseLength(arg1, CanonicalTypeKind.String, out canonical);

            case "nclob":
                canonical = new CanonicalType(CanonicalTypeKind.String, Length: -1);
                return true;

            case "varchar2":
                return TryParseLength(arg1, CanonicalTypeKind.AnsiString, out canonical);

            case "clob":
                canonical = new CanonicalType(CanonicalTypeKind.AnsiString, Length: -1);
                return true;

            case "nchar":
                return TryParseLength(arg1, CanonicalTypeKind.FixedString, out canonical);

            case "char":
                return TryParseLength(arg1, CanonicalTypeKind.AnsiFixedString, out canonical);

            case "raw":
                return TryParseLength(arg1, CanonicalTypeKind.Binary, out canonical);

            case "blob":
                canonical = new CanonicalType(CanonicalTypeKind.Binary, Length: -1);
                return true;

            // Oracle の DATE は時刻を含むため、取込では DateTime として解釈する（TryFormat は Date→DATE と非対称）
            case "date":
                canonical = new CanonicalType(CanonicalTypeKind.DateTime);
                return true;

            case "timestamp":
                return TryParsePrecisionOnly(arg1, CanonicalTypeKind.DateTime, out canonical);

            case "timestamp with time zone":
                return TryParsePrecisionOnly(
                    arg1,
                    CanonicalTypeKind.DateTimeOffset,
                    out canonical
                );

            // WITH LOCAL TIME ZONE も DateTimeOffset として解釈する（解釈のみ・TryFormat では扱わない）
            case "timestamp with local time zone":
                return TryParsePrecisionOnly(
                    arg1,
                    CanonicalTypeKind.DateTimeOffset,
                    out canonical
                );

            case "xmltype":
                canonical = new CanonicalType(CanonicalTypeKind.Xml);
                return true;

            default:
                // LONG / LONG RAW / ROWID / UROWID / BFILE / INTERVAL 系 / SDO_GEOMETRY / 未知の型は変換不能
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
                nativeType = "NUMBER(1)";
                return true;

            case CanonicalTypeKind.TinyInt:
                nativeType = "NUMBER(3)";
                return true;

            case CanonicalTypeKind.SmallInt:
                nativeType = "NUMBER(5)";
                return true;

            case CanonicalTypeKind.Int32:
                nativeType = "NUMBER(10)";
                return true;

            case CanonicalTypeKind.Int64:
                nativeType = "NUMBER(19)";
                return true;

            case CanonicalTypeKind.Decimal:
                nativeType = FormatNumber(canonical.Precision, canonical.Scale);
                return true;

            case CanonicalTypeKind.Float32:
                nativeType = "BINARY_FLOAT";
                return true;

            case CanonicalTypeKind.Float64:
                nativeType = "BINARY_DOUBLE";
                return true;

            // 通貨は NUMBER(19,4) で受ける
            case CanonicalTypeKind.Money:
                nativeType = "NUMBER(19,4)";
                return true;

            // Unicode 可変長は NVARCHAR2。max（-1）は NCLOB
            case CanonicalTypeKind.String:
                nativeType = FormatVarcharOrLob(canonical.Length, "NVARCHAR2", "NCLOB");
                return true;

            // 非 Unicode 可変長は VARCHAR2。max（-1）は CLOB
            case CanonicalTypeKind.AnsiString:
                nativeType = FormatVarcharOrLob(canonical.Length, "VARCHAR2", "CLOB");
                return true;

            // Unicode 固定長は NCHAR
            case CanonicalTypeKind.FixedString:
                nativeType = FormatFixedChar(canonical.Length, "NCHAR");
                return true;

            // 非 Unicode 固定長は CHAR
            case CanonicalTypeKind.AnsiFixedString:
                nativeType = FormatFixedChar(canonical.Length, "CHAR");
                return true;

            // 可変長・固定長バイナリはともに RAW(n)。max（-1）は BLOB
            case CanonicalTypeKind.Binary:
            case CanonicalTypeKind.FixedBinary:
                nativeType = FormatRawOrBlob(canonical.Length);
                return true;

            case CanonicalTypeKind.Date:
                nativeType = "DATE";
                return true;

            // Oracle に TIME 型は無いため書き出せない（方言切替時は変換不能一覧に載る）
            case CanonicalTypeKind.Time:
                return false;

            case CanonicalTypeKind.DateTime:
                nativeType = FormatTimestamp(canonical.Precision, withTimeZone: false);
                return true;

            case CanonicalTypeKind.DateTimeOffset:
                nativeType = FormatTimestamp(canonical.Precision, withTimeZone: true);
                return true;

            // 解釈で Guid は生まれないが、書き出しは RAW(16) として表現する（format-only）
            case CanonicalTypeKind.Guid:
                nativeType = "RAW(16)";
                return true;

            case CanonicalTypeKind.Xml:
                nativeType = "XMLTYPE";
                return true;

            // 19c に JSON 型が無いため CLOB で表現する（format-only）
            case CanonicalTypeKind.Json:
                nativeType = "CLOB";
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// <c>NUMBER</c> の精度・スケールから正規型を振り分ける。
    /// スケール未指定（=0 扱い）の代表的な精度は整数型へ、それ以外は <see cref="CanonicalTypeKind.Decimal"/> へ寄せる。
    /// </summary>
    private static bool TryParseNumber(string? arg1, string? arg2, out CanonicalType canonical)
    {
        // NUMBER（無引数）は精度・スケール不定の Decimal
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

        // スケールが指定され 0 超なら固定小数点（Decimal(p,s)）として扱う
        if (scale is > 0)
        {
            canonical = new CanonicalType(
                CanonicalTypeKind.Decimal,
                Precision: precision,
                Scale: scale
            );
            return true;
        }

        // スケール未指定または 0 の場合、代表的な精度は整数型へ振り分ける
        canonical = precision switch
        {
            1 => new CanonicalType(CanonicalTypeKind.Boolean),
            3 => new CanonicalType(CanonicalTypeKind.TinyInt),
            5 => new CanonicalType(CanonicalTypeKind.SmallInt),
            10 => new CanonicalType(CanonicalTypeKind.Int32),
            19 => new CanonicalType(CanonicalTypeKind.Int64),
            // 上記以外の精度は Decimal(p,0) として扱う
            _ => new CanonicalType(CanonicalTypeKind.Decimal, Precision: precision, Scale: 0),
        };
        return true;
    }

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

    /// <summary>Decimal を <c>NUMBER</c> / <c>NUMBER(p)</c> / <c>NUMBER(p,s)</c> へ整形する</summary>
    private static string FormatNumber(int? precision, int? scale)
    {
        if (precision is null)
        {
            return "NUMBER";
        }

        return scale is null ? $"NUMBER({precision})" : $"NUMBER({precision},{scale})";
    }

    /// <summary>可変長文字列を <c>型(n)</c> または LOB（max）へ整形する</summary>
    private static string FormatVarcharOrLob(int? length, string varName, string lobName)
    {
        if (length is null)
        {
            return varName;
        }

        return length == -1 ? lobName : $"{varName}({length})";
    }

    /// <summary>固定長文字列を <c>型(n)</c> へ整形する（長さ未指定は型名のみ）</summary>
    private static string FormatFixedChar(int? length, string name)
    {
        if (length is null)
        {
            return name;
        }

        // 固定長で max（-1）は Oracle に対応が無いため型名のみで表現する
        return length == -1 ? name : $"{name}({length})";
    }

    /// <summary>バイナリを <c>RAW(n)</c> または <c>BLOB</c>（max）へ整形する</summary>
    private static string FormatRawOrBlob(int? length)
    {
        if (length is null)
        {
            return "RAW";
        }

        return length == -1 ? "BLOB" : $"RAW({length})";
    }

    /// <summary><c>TIMESTAMP</c> / <c>TIMESTAMP(p)</c>（＋ WITH TIME ZONE）へ整形する</summary>
    private static string FormatTimestamp(int? precision, bool withTimeZone)
    {
        var head = precision is null ? "TIMESTAMP" : $"TIMESTAMP({precision})";
        return withTimeZone ? head + " WITH TIME ZONE" : head;
    }
}
