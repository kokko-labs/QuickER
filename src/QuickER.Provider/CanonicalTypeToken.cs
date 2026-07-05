using System.Globalization;
using System.Text.RegularExpressions;

namespace QuickER.Provider;

/// <summary>
/// 正規型 <see cref="CanonicalType"/> と、DB 定義メタ属性へ刻む中立トークン文字列を相互変換する。
/// </summary>
/// <remarks>
/// <para>
/// トークンは「小文字の種別名＋（あれば）括弧引数」で構成する方言非依存の中立表記で、生成 Entity の
/// <c>[DbColumnMeta("...")]</c> 属性へ刻む。将来の C#→ErDiagram リバース（段階2）で、この文字列を
/// <see cref="TryParse"/> で正規型へ復元し、任意方言の <see cref="ITypeCatalog.TryFormat"/> でネイティブ型へ戻す。
/// </para>
/// <para>
/// 引数規則は <see cref="CanonicalType"/> のフィールドに対応する:
/// <list type="bullet">
///   <item>文字列・バイナリ系: <c>Length</c>。<c>-1</c> は <c>max</c>、<c>null</c> は括弧なし（例 <c>string(50)</c> / <c>string(max)</c> / <c>string</c>）</item>
///   <item>decimal: <c>Precision</c> と <c>Scale</c>（例 <c>decimal(10,2)</c> / <c>decimal(10)</c> / <c>decimal</c>）</item>
///   <item>time / datetime / datetimeoffset: <c>Precision</c>（小数秒桁。例 <c>datetime(7)</c> / <c>datetime</c>）</item>
///   <item>その他の種別（整数・bool・guid など）は引数を持たない</item>
/// </list>
/// </para>
/// </remarks>
public static partial class CanonicalTypeToken
{
    /// <summary>正規型の種別 → トークンの種別名（小文字）。全 <see cref="CanonicalTypeKind"/> を双方向にカバーする</summary>
    private static readonly IReadOnlyDictionary<CanonicalTypeKind, string> KindNames =
        new Dictionary<CanonicalTypeKind, string>
        {
            [CanonicalTypeKind.Boolean] = "boolean",
            [CanonicalTypeKind.TinyInt] = "tinyint",
            [CanonicalTypeKind.SmallInt] = "smallint",
            [CanonicalTypeKind.Int32] = "int32",
            [CanonicalTypeKind.Int64] = "int64",
            [CanonicalTypeKind.Decimal] = "decimal",
            [CanonicalTypeKind.Float32] = "float32",
            [CanonicalTypeKind.Float64] = "float64",
            [CanonicalTypeKind.Money] = "money",
            [CanonicalTypeKind.String] = "string",
            [CanonicalTypeKind.AnsiString] = "ansistring",
            [CanonicalTypeKind.FixedString] = "fixedstring",
            [CanonicalTypeKind.AnsiFixedString] = "ansifixedstring",
            [CanonicalTypeKind.Binary] = "binary",
            [CanonicalTypeKind.FixedBinary] = "fixedbinary",
            [CanonicalTypeKind.Date] = "date",
            [CanonicalTypeKind.Time] = "time",
            [CanonicalTypeKind.DateTime] = "datetime",
            [CanonicalTypeKind.DateTimeOffset] = "datetimeoffset",
            [CanonicalTypeKind.Guid] = "guid",
            [CanonicalTypeKind.Xml] = "xml",
            [CanonicalTypeKind.Json] = "json",
        };

    /// <summary>トークンの種別名（小文字）→ 正規型の種別。<see cref="KindNames"/> の逆引き</summary>
    private static readonly IReadOnlyDictionary<string, CanonicalTypeKind> KindsByName =
        KindNames.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>長さ引数（<c>Length</c>）を持つ文字列・バイナリ系の種別</summary>
    private static readonly IReadOnlySet<CanonicalTypeKind> LengthKinds =
        new HashSet<CanonicalTypeKind>
        {
            CanonicalTypeKind.String,
            CanonicalTypeKind.AnsiString,
            CanonicalTypeKind.FixedString,
            CanonicalTypeKind.AnsiFixedString,
            CanonicalTypeKind.Binary,
            CanonicalTypeKind.FixedBinary,
        };

    /// <summary>小数秒桁（<c>Precision</c> のみ）を持つ時刻・日時系の種別</summary>
    private static readonly IReadOnlySet<CanonicalTypeKind> PrecisionOnlyKinds =
        new HashSet<CanonicalTypeKind>
        {
            CanonicalTypeKind.Time,
            CanonicalTypeKind.DateTime,
            CanonicalTypeKind.DateTimeOffset,
        };

    [GeneratedRegex(
        @"^\s*(?<name>[a-z0-9]+)\s*(\(\s*(?<arg1>max|-?\d+)\s*(,\s*(?<arg2>-?\d+)\s*)?\))?\s*$"
    )]
    private static partial Regex TokenPattern();

    /// <summary>正規型を中立トークン文字列へ整形する（例 <c>string(50)</c> / <c>decimal(10,2)</c> / <c>int32</c>）</summary>
    /// <param name="canonical">整形対象の正規型</param>
    /// <returns>トークン文字列</returns>
    public static string Format(CanonicalType canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);

        var name = KindNames[canonical.Kind];

        if (LengthKinds.Contains(canonical.Kind))
        {
            return FormatLength(name, canonical.Length);
        }

        if (canonical.Kind == CanonicalTypeKind.Decimal)
        {
            return FormatPrecisionScale(name, canonical.Precision, canonical.Scale);
        }

        if (PrecisionOnlyKinds.Contains(canonical.Kind))
        {
            return FormatPrecisionOnly(name, canonical.Precision);
        }

        // 引数を持たない種別（整数・bool・guid・xml・json・money・date・float 系）は種別名のみ
        return name;
    }

    /// <summary>中立トークン文字列を正規型へ解析する。書式不正・未知の種別名は <c>false</c> を返す</summary>
    /// <param name="token">解析対象のトークン文字列（例 <c>string(50)</c>）</param>
    /// <param name="canonical">解析に成功した場合の正規型</param>
    public static bool TryParse(string token, out CanonicalType canonical)
    {
        canonical = null!;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var match = TokenPattern().Match(token);

        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups["name"].Value;

        if (!KindsByName.TryGetValue(name, out var kind))
        {
            return false;
        }

        string? arg1 = match.Groups["arg1"].Success ? match.Groups["arg1"].Value : null;
        string? arg2 = match.Groups["arg2"].Success ? match.Groups["arg2"].Value : null;

        if (LengthKinds.Contains(kind))
        {
            // 文字列・バイナリ系は Length 引数のみ（arg2 は無効）
            if (arg2 is not null)
            {
                return false;
            }

            return TryParseLength(kind, arg1, out canonical);
        }

        if (kind == CanonicalTypeKind.Decimal)
        {
            return TryParsePrecisionScale(kind, arg1, arg2, out canonical);
        }

        if (PrecisionOnlyKinds.Contains(kind))
        {
            // 時刻・日時系は Precision 引数のみ（arg2 は無効）
            if (arg2 is not null)
            {
                return false;
            }

            return TryParsePrecisionOnly(kind, arg1, out canonical);
        }

        // 引数を持たない種別に括弧が付いていたら書式不正
        if (arg1 is not null)
        {
            return false;
        }

        canonical = new CanonicalType(kind);
        return true;
    }

    private static string FormatLength(string name, int? length)
    {
        if (length is null)
        {
            return name;
        }

        return length == -1
            ? $"{name}(max)"
            : $"{name}({length.Value.ToString(CultureInfo.InvariantCulture)})";
    }

    private static string FormatPrecisionScale(string name, int? precision, int? scale)
    {
        if (precision is null)
        {
            return name;
        }

        var p = precision.Value.ToString(CultureInfo.InvariantCulture);

        return scale is null
            ? $"{name}({p})"
            : $"{name}({p},{scale.Value.ToString(CultureInfo.InvariantCulture)})";
    }

    private static string FormatPrecisionOnly(string name, int? precision) =>
        precision is null
            ? name
            : $"{name}({precision.Value.ToString(CultureInfo.InvariantCulture)})";

    private static bool TryParseLength(
        CanonicalTypeKind kind,
        string? arg1,
        out CanonicalType canonical
    )
    {
        if (arg1 is null)
        {
            canonical = new CanonicalType(kind);
            return true;
        }

        if (string.Equals(arg1, "max", StringComparison.Ordinal))
        {
            canonical = new CanonicalType(kind, Length: -1);
            return true;
        }

        if (!TryParseArg(arg1, out var length))
        {
            canonical = null!;
            return false;
        }

        canonical = new CanonicalType(kind, Length: length);
        return true;
    }

    private static bool TryParsePrecisionScale(
        CanonicalTypeKind kind,
        string? arg1,
        string? arg2,
        out CanonicalType canonical
    )
    {
        if (arg1 is null)
        {
            canonical = new CanonicalType(kind);
            return true;
        }

        // max はスケール・精度の引数として無効
        if (!TryParseArg(arg1, out var precision))
        {
            canonical = null!;
            return false;
        }

        int? scale = null;

        if (arg2 is not null)
        {
            if (!TryParseArg(arg2, out var parsedScale))
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
        CanonicalTypeKind kind,
        string? arg1,
        out CanonicalType canonical
    )
    {
        if (arg1 is null)
        {
            canonical = new CanonicalType(kind);
            return true;
        }

        if (!TryParseArg(arg1, out var precision))
        {
            canonical = null!;
            return false;
        }

        canonical = new CanonicalType(kind, Precision: precision);
        return true;
    }

    /// <summary>数値引数を解析する。<c>max</c>・負数・範囲外は失敗として扱う（長さの <c>max</c> は呼び出し側で先に処理する）</summary>
    private static bool TryParseArg(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
