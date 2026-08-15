using System.Globalization;
using System.Text.RegularExpressions;
using QuickER.Provider;

namespace QuickER.Sqlite;

/// <summary>
/// SQLite 向けの <see cref="ITypeCatalog"/> 実装。
/// ネイティブ（宣言型）文字列（<c>NVARCHAR(100)</c> / <c>DECIMAL(18,2)</c> / <c>DATETIME2</c> 等）と
/// 正規型 <see cref="CanonicalType"/> を相互変換する。
/// </summary>
/// <remarks>
/// <para>
/// SQLite は宣言型文字列を verbatim に保存し読み戻せる（型親和性で実行時の格納クラスは決まるが、
/// 宣言型そのものは保持される）。この性質を活かし、本カタログは SQL Server 風のリッチな宣言型を
/// 採用して <see cref="CanonicalTypeKind"/> の全種別を双方向にカバーする。狙いは SQL Server ⇄ SQLite の
/// スキーマ往復をほぼ無損失にすること。
/// </para>
/// <para>
/// 解析（<see cref="TryParse"/>）は SQL Server 表記に加え、SQLite の伝統的な型親和性キーワード
/// （<c>INTEGER</c> / <c>TEXT</c> / <c>BLOB</c> 等）や別名（<c>NUMERIC</c> / <c>BOOLEAN</c> / <c>DATETIME</c> 等）も
/// 受け付ける。生成（<see cref="TryFormat"/>）は往復無損失を優先し、SQL Server と同じ代表宣言型を出力する。
/// </para>
/// <para>
/// 唯一の非可逆な種別は <see cref="CanonicalTypeKind.RowVersion"/>（SQL Server の <c>rowversion</c>）で、
/// SQLite には「DB が採番する行バージョン」に当たる概念が無いため <c>BLOB</c>（ただのバイナリ列）へ落とす。
/// 落とした列はサーバー側の版を写して持つミラー置き場として使う想定で、SQLite 側で版ガードは働かない。
/// 逆向き（<see cref="TryParse"/>）は <c>BLOB</c> を <see cref="CanonicalTypeKind.Binary"/> として読むため、
/// SQL Server へ戻しても <c>varbinary(max)</c> にしかならない（往復では rowversion に戻らない）。
/// </para>
/// </remarks>
public sealed partial class SqliteTypeCatalog : ITypeCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<string> DataTypes => SqliteDataTypes.All;

    /// <inheritdoc />
    public string DefaultDataType => "INT";

    // 型名は英字・アンダースコアで始まり英数字・アンダースコアを許容する。
    // 末尾の括弧内には長さ（数値または MAX）／精度・スケールを取る。
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

        var name = NormalizeAlias(match.Groups["name"].Value.ToLowerInvariant());
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
                return TryParsePrecisionScale(arg1, arg2, CanonicalTypeKind.Decimal, out canonical);

            case "real":
                canonical = new CanonicalType(CanonicalTypeKind.Float32);
                return true;

            case "float":
                canonical = new CanonicalType(CanonicalTypeKind.Float64);
                return true;

            case "money":
                canonical = new CanonicalType(CanonicalTypeKind.Money);
                return true;

            case "nvarchar":
                return TryParseLength(arg1, CanonicalTypeKind.String, out canonical);

            case "varchar":
                return TryParseLength(arg1, CanonicalTypeKind.AnsiString, out canonical);

            // SQLite の型親和性キーワード TEXT は Unicode 可変長（max）として扱う
            case "text":
                canonical = new CanonicalType(CanonicalTypeKind.String, Length: -1);
                return true;

            case "nchar":
                return TryParseLength(arg1, CanonicalTypeKind.FixedString, out canonical);

            case "char":
                return TryParseLength(arg1, CanonicalTypeKind.AnsiFixedString, out canonical);

            case "varbinary":
                return TryParseLength(arg1, CanonicalTypeKind.Binary, out canonical);

            // SQLite の型親和性キーワード BLOB は可変長バイナリ（max）として扱う
            case "blob":
                canonical = new CanonicalType(CanonicalTypeKind.Binary, Length: -1);
                return true;

            case "binary":
                return TryParseLength(arg1, CanonicalTypeKind.FixedBinary, out canonical);

            case "date":
                canonical = new CanonicalType(CanonicalTypeKind.Date);
                return true;

            case "time":
                return TryParsePrecisionOnly(arg1, CanonicalTypeKind.Time, out canonical);

            case "datetime2":
                return TryParsePrecisionOnly(arg1, CanonicalTypeKind.DateTime, out canonical);

            // datetime は精度概念を持たない代表日時型として扱う
            case "datetime":
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

            case "json":
                canonical = new CanonicalType(CanonicalTypeKind.Json);
                return true;

            default:
                // 未知の型は変換不能として扱う
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
                nativeType = "BIT";
                return true;

            case CanonicalTypeKind.TinyInt:
                nativeType = "TINYINT";
                return true;

            case CanonicalTypeKind.SmallInt:
                nativeType = "SMALLINT";
                return true;

            case CanonicalTypeKind.Int32:
                nativeType = "INT";
                return true;

            case CanonicalTypeKind.Int64:
                nativeType = "BIGINT";
                return true;

            case CanonicalTypeKind.Decimal:
                nativeType = FormatPrecisionScale("DECIMAL", canonical.Precision, canonical.Scale);
                return true;

            case CanonicalTypeKind.Float32:
                nativeType = "REAL";
                return true;

            case CanonicalTypeKind.Float64:
                nativeType = "FLOAT";
                return true;

            case CanonicalTypeKind.Money:
                nativeType = "MONEY";
                return true;

            case CanonicalTypeKind.String:
                nativeType = FormatLength("NVARCHAR", canonical.Length);
                return true;

            case CanonicalTypeKind.AnsiString:
                nativeType = FormatLength("VARCHAR", canonical.Length);
                return true;

            case CanonicalTypeKind.FixedString:
                nativeType = FormatLength("NCHAR", canonical.Length);
                return true;

            case CanonicalTypeKind.AnsiFixedString:
                nativeType = FormatLength("CHAR", canonical.Length);
                return true;

            case CanonicalTypeKind.Binary:
                nativeType = FormatLength("VARBINARY", canonical.Length);
                return true;

            case CanonicalTypeKind.FixedBinary:
                nativeType = FormatLength("BINARY", canonical.Length);
                return true;

            case CanonicalTypeKind.Date:
                nativeType = "DATE";
                return true;

            case CanonicalTypeKind.Time:
                nativeType = FormatPrecisionOnly("TIME", canonical.Precision);
                return true;

            case CanonicalTypeKind.DateTime:
                nativeType = FormatPrecisionOnly("DATETIME2", canonical.Precision);
                return true;

            case CanonicalTypeKind.DateTimeOffset:
                nativeType = FormatPrecisionOnly("DATETIMEOFFSET", canonical.Precision);
                return true;

            case CanonicalTypeKind.Guid:
                nativeType = "UNIQUEIDENTIFIER";
                return true;

            case CanonicalTypeKind.Xml:
                nativeType = "XML";
                return true;

            case CanonicalTypeKind.Json:
                nativeType = "JSON";
                return true;

            case CanonicalTypeKind.RowVersion:
                // SQLite に行バージョンの概念は無いため、値だけを写せる BLOB（ミラー列）へ落とす。
                // VARBINARY ではなく BLOB を出すのは「SQL Server の varbinary から来た列」と読み分けられるようにするため
                nativeType = "BLOB";
                return true;

            default:
                return false;
        }
    }

    /// <summary>SQLite / 他方言由来の型別名を代表表記へ解決する（例: <c>numeric</c> → <c>decimal</c>）</summary>
    /// <remarks>
    /// SQLite の型親和性は名称の部分一致で決まるが、本カタログは宣言型を厳密名で扱うため、
    /// 代表的な別名のみを明示的に正規化する。SQLite の伝統キーワード（<c>integer</c> / <c>boolean</c> 等）や
    /// 他方言由来の別名（<c>numeric</c> / <c>character varying</c> 等）を SQL Server 風の代表名へ寄せる。
    /// </remarks>
    private static string NormalizeAlias(string name) =>
        name switch
        {
            // SQLite の INTEGER は最大 8 バイト格納（EF Core も long 対応）のため Int64 として扱う
            "integer" => "bigint",
            "int4" => "int",
            "int8" => "bigint",
            "int2" => "smallint",
            "boolean" or "bool" => "bit",
            "numeric" => "decimal",
            "double" or "double precision" => "float",
            "character varying" => "varchar",
            "character" => "char",
            "guid" => "uniqueidentifier",
            "timestamp" => "datetime2",
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

        return length == -1 ? $"{name}(MAX)" : $"{name}({length})";
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
