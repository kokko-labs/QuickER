using System.Globalization;
using System.Text.RegularExpressions;
using QuickER.Provider;

namespace QuickER.Provider.MySql;

/// <summary>
/// MySQL 8.0 以上向けの <see cref="ITypeCatalog"/> 実装。
/// ネイティブ型文字列（<c>varchar(255)</c> / <c>tinyint(1)</c> / <c>int unsigned</c> 等）と正規型 <see cref="CanonicalType"/> を相互変換する。
/// </summary>
/// <remarks>
/// <para>
/// MySQL は <c>unsigned</c> / <c>zerofill</c> といった末尾修飾子や、<c>tinyint(1)</c>（真偽値慣習）・<c>bit(1)</c> 等の
/// 特殊表記を持つため、型名・型引数・末尾修飾子を分離して判定する。
/// <c>double precision</c> / <c>real</c> のような複数語型名も畳み込んで解釈する。
/// </para>
/// <para>
/// 対象は MySQL 8.0 以上（MariaDB は対象外）。MySQL 8 の既定文字セットは <c>utf8mb4</c>（Unicode）のため、
/// <c>varchar</c> / <c>text</c> 系は <see cref="CanonicalTypeKind.String"/>（Unicode）として扱う。
/// </para>
/// <para>
/// 次の変換は Format 専用（解釈では生まれない）である点に注意:
/// <list type="bullet">
///   <item><see cref="CanonicalTypeKind.Money"/> → <c>decimal(19,4)</c></item>
///   <item><see cref="CanonicalTypeKind.Guid"/> → <c>char(36)</c>（MySQL に UUID 型は無い）</item>
///   <item><see cref="CanonicalTypeKind.Xml"/> → <c>longtext</c>（MySQL に XML 型は無い）</item>
/// </list>
/// </para>
/// <para>
/// <c>timestamp</c> は MySQL では内部的に UTC で格納されるため <see cref="CanonicalTypeKind.DateTimeOffset"/> に
/// 最も近い等価として対応付ける。ただし MySQL の <c>timestamp</c> は 2038-01-19 が上限という既知の制約がある。
/// </para>
/// <para>
/// 変換不能（<see cref="TryParse"/> が <c>false</c>）: <c>enum(...)</c> / <c>set(...)</c> / <c>year</c> /
/// <c>bit(n)</c>（n&gt;1）/ 幾何型（<c>geometry</c> 等）・未知の型。型引数の負数・<c>int</c> 範囲外も <c>false</c>。
/// </para>
/// </remarks>
public sealed partial class MySqlTypeCatalog : ITypeCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<string> DataTypes => MySqlDataTypes.All;

    /// <inheritdoc />
    public string DefaultDataType => "int";

    // 型名は英字・アンダースコアと空白を許容する（"double precision" 等の複数語型名に対応）。
    // 括弧内には長さ / 精度 / スケール、または enum/set の値リストを取る（値リストは弾くため貪欲に飲み込む）。
    // 末尾に unsigned / zerofill / signed の修飾子を任意個許容する。
    [GeneratedRegex(
        @"^\s*(?<name>[a-zA-Z_][a-zA-Z0-9_ ]*?)\s*(\((?<args>.*)\))?\s*(?<mods>(\s*(unsigned|zerofill|signed))*)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
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
        var argsRaw = match.Groups["args"].Success ? match.Groups["args"].Value.Trim() : null;
        var isUnsigned = match
            .Groups["mods"]
            .Value.Contains("unsigned", StringComparison.OrdinalIgnoreCase);

        // enum / set は値リストを括弧に取る。値リストのパースには踏み込まず変換不能とする
        if (name is "enum" or "set")
        {
            return false;
        }

        switch (name)
        {
            case "boolean":
                // bool / boolean は tinyint(1) の別名。真偽値として解釈する
                canonical = new CanonicalType(CanonicalTypeKind.Boolean);
                return true;

            case "tinyint":
                return TryParseTinyInt(argsRaw, isUnsigned, out canonical);

            // 符号の有無で値の範囲が変わるので、範囲を収められる正規型を選ぶ（unsigned は 1 段広い型）。
            // 正規型は符号なし整数を持たないため、元の綴りは verbatim 併記（NativeType）が運ぶ
            case "smallint":
                canonical = new CanonicalType(
                    isUnsigned ? CanonicalTypeKind.Int32 : CanonicalTypeKind.SmallInt
                );
                return true;

            case "int":
                canonical = new CanonicalType(
                    isUnsigned ? CanonicalTypeKind.Int64 : CanonicalTypeKind.Int32
                );
                return true;

            // mediumint は 24 ビット（符号つき ±8,388,607・符号なし 0〜16,777,215）で、どちらも Int32 に収まる
            case "mediumint":
                canonical = new CanonicalType(CanonicalTypeKind.Int32);
                return true;

            // year（1901〜2155）は整数として読む。ドライバが返す CLR 型（int）・C# の型マッパーと同じ Int32
            case "year":
                canonical = new CanonicalType(CanonicalTypeKind.Int32);
                return true;

            // bigint unsigned（0〜18,446,744,073,709,551,615＝20 桁）は Int64 に収まらないので decimal(20,0)
            case "bigint":
                canonical = isUnsigned
                    ? new CanonicalType(CanonicalTypeKind.Decimal, Precision: 20, Scale: 0)
                    : new CanonicalType(CanonicalTypeKind.Int64);
                return true;

            case "decimal":
                return TryParsePrecisionScale(argsRaw, out canonical);

            case "float":
                canonical = new CanonicalType(CanonicalTypeKind.Float32);
                return true;

            case "double":
                canonical = new CanonicalType(CanonicalTypeKind.Float64);
                return true;

            case "varchar":
                // MySQL 8 の既定 utf8mb4 は Unicode。正規型は String として扱う
                return TryParseLength(argsRaw, CanonicalTypeKind.String, out canonical);

            case "tinytext":
            case "text":
            case "mediumtext":
            case "longtext":
                canonical = new CanonicalType(CanonicalTypeKind.String, Length: -1);
                return true;

            case "char":
                return TryParseLength(argsRaw, CanonicalTypeKind.FixedString, out canonical);

            case "varbinary":
                return TryParseLength(argsRaw, CanonicalTypeKind.Binary, out canonical);

            case "tinyblob":
            case "blob":
            case "mediumblob":
            case "longblob":
                canonical = new CanonicalType(CanonicalTypeKind.Binary, Length: -1);
                return true;

            case "binary":
                return TryParseLength(argsRaw, CanonicalTypeKind.FixedBinary, out canonical);

            case "date":
                canonical = new CanonicalType(CanonicalTypeKind.Date);
                return true;

            case "time":
                return TryParsePrecisionOnly(argsRaw, CanonicalTypeKind.Time, out canonical);

            case "datetime":
                return TryParsePrecisionOnly(argsRaw, CanonicalTypeKind.DateTime, out canonical);

            case "timestamp":
                // MySQL の timestamp は UTC 格納のため DateTimeOffset に最も近い等価とみなす
                return TryParsePrecisionOnly(
                    argsRaw,
                    CanonicalTypeKind.DateTimeOffset,
                    out canonical
                );

            case "bit":
                // bit(1) は Boolean として解釈する（解釈のみ）。bit(n>1) は変換不能
                return TryParseBit(argsRaw, out canonical);

            case "json":
                canonical = new CanonicalType(CanonicalTypeKind.Json);
                return true;

            default:
                // enum / set は上で除外済み。幾何型 / 未知の型は変換不能
                return false;
        }
    }

    /// <inheritdoc />
    public bool TryFormat(CanonicalType canonical, out string nativeType)
    {
        nativeType = string.Empty;

        // MySQL の上限を超える長さ・精度・スケールは書き出さない（DDL の適用で必ず失敗するため、切替の時点で変換不能一覧に載せる）
        if (canonical is not null && !IsWithinLimits(canonical))
        {
            return false;
        }

        return TryFormatParameterType(canonical!, out nativeType);
    }

    /// <inheritdoc />
    /// <remarks>列の上限（<see cref="IsWithinLimits"/>）だけを当てない。そのほかの規則は <see cref="TryFormat"/> と同じ。</remarks>
    public bool TryFormatParameterType(CanonicalType canonical, out string nativeType)
    {
        nativeType = string.Empty;

        if (canonical is null)
        {
            return false;
        }

        // 長さを要する文字列・バイナリ種別で長さが無い正規型は書き出さない（変換不能一覧に載せる）。
        // MySQL は長さを省いた varchar / varbinary を構文エラーとして拒否し（`ERROR 1064`）、
        // char / binary は黙って長さ 1 として作る。
        // 負のスケールと同じ「実行できない／意味の違う DDL を黙って作らない」線引き
        if (CanonicalTypeToken.HasLengthArgument(canonical.Kind) && canonical.Length is null)
        {
            return false;
        }

        switch (canonical.Kind)
        {
            case CanonicalTypeKind.Boolean:
                // MySQL の真偽値慣習は tinyint(1)
                nativeType = "tinyint(1)";
                return true;

            case CanonicalTypeKind.TinyInt:
                nativeType = "tinyint unsigned";
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
                // 負のスケール（PostgreSQL / Oracle の「100 の倍数へ丸める」宣言）は書き出せない。
                // MySQL は負のスケールを受け付けない（`ERROR 1064` = 構文エラー）。
                // ここで true を返すと、対象 DBMS の切替が実行できない DDL を黙って作る
                if (canonical.Scale is < 0)
                {
                    return false;
                }

                // 精度の無い Decimal も書き出さない。MySQL の素の decimal は decimal(10,0) で、Oracle の NUMBER・
                // PostgreSQL の無制約 numeric から切り替えた列が小数部を黙って丸め、整数部も 10 桁で溢れる
                if (canonical.Precision is null)
                {
                    return false;
                }

                nativeType = FormatPrecisionScale("decimal", canonical.Precision, canonical.Scale);
                return true;

            case CanonicalTypeKind.Float32:
                nativeType = "float";
                return true;

            case CanonicalTypeKind.Float64:
                nativeType = "double";
                return true;

            case CanonicalTypeKind.Money:
                // MySQL に通貨型は無いため decimal(19,4) で受ける（Format 専用）
                nativeType = "decimal(19,4)";
                return true;

            // varchar は Unicode（utf8mb4）可変長。max（-1）は longtext
            case CanonicalTypeKind.String:
            case CanonicalTypeKind.AnsiString:
                nativeType = FormatVarcharOrText(canonical.Length);
                return true;

            // 固定長は char。max（-1）は longtext
            case CanonicalTypeKind.FixedString:
            case CanonicalTypeKind.AnsiFixedString:
                nativeType = FormatCharOrText(canonical.Length);
                return true;

            // 可変長バイナリは varbinary。max（-1）は longblob
            case CanonicalTypeKind.Binary:
                nativeType = FormatVarbinaryOrBlob(canonical.Length);
                return true;

            // 固定長バイナリは binary。max（-1）は longblob
            case CanonicalTypeKind.FixedBinary:
                nativeType = FormatBinaryOrBlob(canonical.Length);
                return true;

            case CanonicalTypeKind.Date:
                nativeType = "date";
                return true;

            case CanonicalTypeKind.Time:
                nativeType = FormatPrecisionOnly("time", canonical.Precision);
                return true;

            case CanonicalTypeKind.DateTime:
                nativeType = FormatPrecisionOnly("datetime", canonical.Precision);
                return true;

            case CanonicalTypeKind.DateTimeOffset:
                // MySQL の timestamp は UTC 格納。2038 年上限の既知の制約がある
                nativeType = FormatPrecisionOnly("timestamp", canonical.Precision);
                return true;

            case CanonicalTypeKind.Guid:
                // MySQL に UUID 型は無いため char(36) で受ける（Format 専用）
                nativeType = "char(36)";
                return true;

            case CanonicalTypeKind.Xml:
                // MySQL に XML 型は無いため longtext で受ける（Format 専用）
                nativeType = "longtext";
                return true;

            case CanonicalTypeKind.Json:
                nativeType = "json";
                return true;

            default:
                return false;
        }
    }

    /// <summary>MySQL の型別名を正規表記へ解決する（例: <c>integer</c> → <c>int</c>）</summary>
    private static string NormalizeAlias(string name) =>
        name switch
        {
            "integer" => "int",
            "bool" => "boolean",
            "numeric" or "dec" or "fixed" => "decimal",
            "double precision" or "real" => "double",
            _ => name,
        };

    /// <summary>
    /// <c>tinyint</c> を解釈する。<c>tinyint(1)</c> は真偽値慣習として Boolean、
    /// <c>tinyint unsigned</c> は TinyInt（0〜255）、符号つきの <c>tinyint</c>（長さ 1 以外）は値の範囲を収められる SmallInt として扱う。
    /// </summary>
    private static bool TryParseTinyInt(
        string? argsRaw,
        bool isUnsigned,
        out CanonicalType canonical
    )
    {
        // tinyint(1)（unsigned なし）は真偽値慣習として Boolean と解釈する
        if (!isUnsigned && argsRaw == "1")
        {
            canonical = new CanonicalType(CanonicalTypeKind.Boolean);
            return true;
        }

        // 表示幅指定（例: tinyint(4)）は int 範囲内なら無視し、負数・範囲外は変換不能
        if (argsRaw is not null && !TryParseTypeArg(argsRaw, out _))
        {
            canonical = null!;
            return false;
        }

        // 正規型の TinyInt は 0〜255（SQL Server の tinyint）。符号つきの tinyint（-128〜127）は収まらないので SmallInt
        canonical = new CanonicalType(
            isUnsigned ? CanonicalTypeKind.TinyInt : CanonicalTypeKind.SmallInt
        );
        return true;
    }

    /// <summary><c>bit(1)</c> のみ Boolean として解釈する（解釈専用）。<c>bit(n&gt;1)</c> は変換不能</summary>
    private static bool TryParseBit(string? argsRaw, out CanonicalType canonical)
    {
        // bit（引数なし）は bit(1) と同義。Boolean として解釈する
        if (argsRaw is null || argsRaw == "1")
        {
            canonical = new CanonicalType(CanonicalTypeKind.Boolean);
            return true;
        }

        // bit(n>1) は複数ビットフィールドで正規型に対応が無いため変換不能
        canonical = null!;
        return false;
    }

    private static bool TryParseLength(
        string? argsRaw,
        CanonicalTypeKind kind,
        out CanonicalType canonical
    )
    {
        if (argsRaw is null)
        {
            canonical = new CanonicalType(kind);
            return true;
        }

        // 負数・int 範囲外は変換不能として扱う（例外にしない）
        if (!TryParseTypeArg(argsRaw, out var length))
        {
            canonical = null!;
            return false;
        }

        canonical = new CanonicalType(kind, Length: length);
        return true;
    }

    private static bool TryParsePrecisionScale(string? argsRaw, out CanonicalType canonical)
    {
        // 素の decimal は decimal(10,0)（MySQL の既定の精度・スケール）。精度なし（＝任意の小数）と読むと、
        // 書き出せない型として C# リバースが偽の警告を出し、他方言への切替も意味の違う型になる
        if (argsRaw is null)
        {
            canonical = new CanonicalType(
                CanonicalTypeKind.Decimal,
                Precision: DefaultDecimalPrecision,
                Scale: 0
            );
            return true;
        }

        var parts = argsRaw.Split(',');

        if (!TryParseTypeArg(parts[0], out var precision))
        {
            canonical = null!;
            return false;
        }

        int? scale = null;

        if (parts.Length > 1)
        {
            if (!TryParseTypeArg(parts[1], out var parsedScale))
            {
                canonical = null!;
                return false;
            }

            scale = parsedScale;
        }

        canonical = new CanonicalType(
            CanonicalTypeKind.Decimal,
            Precision: precision,
            Scale: scale
        );
        return true;
    }

    private static bool TryParsePrecisionOnly(
        string? argsRaw,
        CanonicalTypeKind kind,
        out CanonicalType canonical
    )
    {
        if (argsRaw is null)
        {
            canonical = new CanonicalType(kind);
            return true;
        }

        if (!TryParseTypeArg(argsRaw, out var precision))
        {
            canonical = null!;
            return false;
        }

        canonical = new CanonicalType(kind, Precision: precision);
        return true;
    }

    /// <summary>型引数の数値を解析する。負数・int 範囲外・非数値は失敗（変換不能）として扱う</summary>
    private static bool TryParseTypeArg(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>可変長文字列を varchar(n) または longtext（max）へ整形する</summary>
    private static string FormatVarcharOrText(int? length)
    {
        if (length is null)
        {
            return "varchar";
        }

        return length == -1 ? "longtext" : $"varchar({length})";
    }

    /// <summary>固定長文字列を char(n) または longtext（max）へ整形する</summary>
    private static string FormatCharOrText(int? length)
    {
        if (length is null)
        {
            return "char";
        }

        return length == -1 ? "longtext" : $"char({length})";
    }

    /// <summary>可変長バイナリを varbinary(n) または longblob（max）へ整形する</summary>
    private static string FormatVarbinaryOrBlob(int? length)
    {
        if (length is null)
        {
            return "varbinary";
        }

        return length == -1 ? "longblob" : $"varbinary({length})";
    }

    /// <summary>固定長バイナリを binary(n) または longblob（max）へ整形する</summary>
    private static string FormatBinaryOrBlob(int? length)
    {
        if (length is null)
        {
            return "binary";
        }

        return length == -1 ? "longblob" : $"binary({length})";
    }

    /// <summary>精度を省いた decimal の既定の精度</summary>
    private const int DefaultDecimalPrecision = 10;

    /// <summary>decimal の最大精度</summary>
    private const int MaxDecimalPrecision = 65;

    /// <summary>decimal の最大スケール</summary>
    private const int MaxDecimalScale = 30;

    /// <summary>time / datetime / timestamp の小数秒の最大桁数</summary>
    private const int MaxFractionalSecondsPrecision = 6;

    /// <summary>char / binary の最大長（文字数 / バイト数）</summary>
    private const int MaxFixedLength = 255;

    /// <summary>varchar の最大長（文字数）。QuickER の DDL は文字集合を書かないので、MySQL 8 の既定 utf8mb4（4 バイト / 文字）の列の上限</summary>
    private const int MaxVarcharLength = 16383;

    /// <summary>varbinary の最大長（バイト数）。行の上限 65,535 バイトから長さの 2 バイトと NULL の印の分を引いた、1 列だけの表で作れる長さ</summary>
    private const int MaxVarbinaryLength = 65532;

    /// <summary>正規型の長さ・精度・スケールが MySQL で宣言できる範囲に収まるか</summary>
    /// <remarks>
    /// 範囲外は MySQL が CREATE / ALTER の時点で拒否する値（<c>datetime(7)</c> は <c>Too-big precision 7</c>、
    /// <c>decimal(66,2)</c> と <c>decimal(10,31)</c> も拒否、<c>char(256)</c> / <c>binary(256)</c> と utf8mb4 の
    /// <c>varchar(16384)</c> は <c>Column length too big</c>）。SQL Server の <c>datetime2(7)</c> を手で書いた図などから切り替えると出る。
    /// 行全体の上限（65,535 バイト）はほかの列しだいで決まるので、型 1 つでは判定しない（超えれば MySQL が適用時に
    /// <c>Row size too large</c> で断る）。長さ <c>-1</c>（無制限）は longtext / longblob へ書くので上限の外。
    /// </remarks>
    private static bool IsWithinLimits(CanonicalType canonical) =>
        canonical.Kind switch
        {
            CanonicalTypeKind.String or CanonicalTypeKind.AnsiString => canonical.Length
                is null
                    or -1
                    or <= MaxVarcharLength,
            CanonicalTypeKind.FixedString
            or CanonicalTypeKind.AnsiFixedString
            or CanonicalTypeKind.FixedBinary => canonical.Length is null or -1 or <= MaxFixedLength,
            CanonicalTypeKind.Binary => canonical.Length is null or -1 or <= MaxVarbinaryLength,
            CanonicalTypeKind.Decimal => canonical.Precision
                is null
                    or (>= 1 and <= MaxDecimalPrecision)
                && canonical.Scale is null or (>= 0 and <= MaxDecimalScale)
                && (
                    canonical.Precision is null
                    || canonical.Scale is null
                    || canonical.Scale <= canonical.Precision
                ),
            CanonicalTypeKind.Time
            or CanonicalTypeKind.DateTime
            or CanonicalTypeKind.DateTimeOffset => canonical.Precision
                is null
                    or (>= 0 and <= MaxFractionalSecondsPrecision),
            _ => true,
        };

    private static string FormatPrecisionScale(string name, int? precision, int? scale)
    {
        if (precision is null)
        {
            return name;
        }

        // 負のスケールは書式のカルチャによって負号が変わる（sv-SE は U+2212）ので、インバリアントで書く
        return scale is null
            ? string.Create(CultureInfo.InvariantCulture, $"{name}({precision})")
            : string.Create(CultureInfo.InvariantCulture, $"{name}({precision},{scale})");
    }

    private static string FormatPrecisionOnly(string name, int? precision) =>
        precision is null ? name : $"{name}({precision})";
}
