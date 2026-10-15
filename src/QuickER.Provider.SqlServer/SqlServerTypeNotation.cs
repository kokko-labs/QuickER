using System.Globalization;
using System.Text.RegularExpressions;

namespace QuickER.Provider.SqlServer;

/// <summary>
/// SQL Server の型表記を「その表記で宣言した列を取り込むと返る表記」へ寄せる。
/// </summary>
/// <remarks>
/// <para>
/// SQL Server は宣言の綴りをそのまま保存しない。カタログから読めるのは型名と長さ・精度・スケールの数値だけで、
/// 取込（<see cref="SqlServerSchemaImporter.FormatDataType"/>）はそこから表記を組み立て直す。そのため
/// 同じ型を表す別の綴り（<c>numeric(18,0)</c> と <c>numeric(18)</c>、<c>datetime2(7)</c> と <c>datetime2</c>）を
/// 図に書くと、取り込んだ表記と食い違い、適用しても消えない <c>ALTER COLUMN</c> が差分に出続ける。
/// </para>
/// <para>
/// 規則（既定値の省略と、DB が覚えていない別名の置き換えだけ。意味は変えない）:
/// <list type="bullet">
///   <item><c>datetime2(7)</c> / <c>time(7)</c> / <c>datetimeoffset(7)</c> → 括弧なし（7 が既定の精度）</item>
///   <item>素の <c>decimal</c> / <c>numeric</c> → <c>(18)</c>（既定の精度 18・スケール 0）</item>
///   <item><c>decimal(p,0)</c> / <c>numeric(p,0)</c> → <c>(p)</c>（スケール 0 は既定）</item>
///   <item><c>rowversion</c> → <c>timestamp</c>（カタログは同義の旧名を返す）</item>
///   <item><c>float(1)</c>〜<c>float(24)</c> → <c>real</c>、<c>float(25)</c>〜<c>float(53)</c> → <c>float</c>
///   （SQL Server は float(n) の n を覚えず、24 以下を単精度の real、それより大きいものを倍精度の float として持つ）</item>
/// </list>
/// 当てはまらない表記は前後の空白を落とすだけで変えない。
/// </para>
/// <para>
/// 取込もこの関数を通す（<see cref="SqlServerSchemaImporter.FormatDataType"/>）ので、取込の出力は不動点になる。
/// 差分の型比較は <see cref="SyncDialectCapabilities.NormalizeTypeNotation"/> 経由で両側をここへ通す。
/// 選択肢と <c>TryFormat</c> の全表記が取込と一致することは <c>SqlServerTypeNotationIntegrationTests</c> が
/// 実 DB で固定する。
/// </para>
/// </remarks>
public static partial class SqlServerTypeNotation
{
    /// <summary>SQL Server が小数秒に付ける既定の精度</summary>
    private const int DefaultFractionalSecondsPrecision = 7;

    /// <summary>精度を省いた decimal / numeric の既定の精度</summary>
    private const int DefaultDecimalPrecision = 18;

    /// <summary>型表記を取込が返す表記へ寄せる</summary>
    /// <param name="dataType">図・型の選択肢・<c>TryFormat</c>・取込のいずれかの型表記</param>
    /// <returns>取込が返す表記（当てはまる規則が無ければ前後の空白を除いただけの表記）</returns>
    public static string Normalize(string dataType)
    {
        var text = dataType.Trim();

        var fractional = FractionalSecondsPattern().Match(text);

        if (fractional.Success)
        {
            var name = fractional.Groups["name"].Value.ToLowerInvariant();
            var precision = int.Parse(fractional.Groups["p"].Value, CultureInfo.InvariantCulture);
            return precision == DefaultFractionalSecondsPrecision ? name : $"{name}({precision})";
        }

        var numeric = DecimalPattern().Match(text);

        if (numeric.Success)
        {
            var name = numeric.Groups["name"].Value.ToLowerInvariant();

            if (!numeric.Groups["p"].Success)
            {
                return $"{name}({DefaultDecimalPrecision})";
            }

            var precision = int.Parse(numeric.Groups["p"].Value, CultureInfo.InvariantCulture);

            if (!numeric.Groups["s"].Success)
            {
                return $"{name}({precision})";
            }

            var scale = int.Parse(numeric.Groups["s"].Value, CultureInfo.InvariantCulture);
            return scale == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{name}({precision})")
                : string.Create(CultureInfo.InvariantCulture, $"{name}({precision},{scale})");
        }

        if (string.Equals(text, "rowversion", StringComparison.OrdinalIgnoreCase))
        {
            return "timestamp";
        }

        if (TryGetFloatMantissaBits(text) is >= 1 and <= MaxFloatMantissaBits)
        {
            // 単精度の境界の判定は型マッパー・型カタログと同じ 1 か所（IsSinglePrecisionFloat）に置く
            return IsSinglePrecisionFloat(text) ? "real" : "float";
        }

        return text;
    }

    /// <summary>float(n) の n のうち、SQL Server が単精度（real・4 バイト）として持つ上限</summary>
    private const int MaxSinglePrecisionMantissaBits = 24;

    /// <summary>float(n) の n の上限（倍精度・8 バイト）</summary>
    private const int MaxFloatMantissaBits = 53;

    /// <summary>
    /// 表記が単精度の float（<c>float(1)</c>〜<c>float(24)</c>＝SQL Server では real と同じ型）かどうか
    /// </summary>
    /// <remarks>
    /// 型マッパー（C# 型）と型カタログ（正規型）が、この表記を real と同じに扱うために使う。
    /// 素の <c>float</c> は <c>float(53)</c> と同じ倍精度。
    /// </remarks>
    internal static bool IsSinglePrecisionFloat(string dataType) =>
        TryGetFloatMantissaBits(dataType) is >= 1 and <= MaxSinglePrecisionMantissaBits;

    /// <summary><c>float(n)</c> の n を取り出す（float(n) の形でなければ null）</summary>
    private static int? TryGetFloatMantissaBits(string dataType)
    {
        var match = FloatPattern().Match(dataType.Trim());
        return match.Success
            ? int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture)
            : null;
    }

    // float(n)（括弧内の空白と大文字小文字を許す。桁数を 9 までに絞って int へ必ず収める）
    [GeneratedRegex(
        @"^float\s*\(\s*(?<n>\d{1,9})\s*\)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex FloatPattern();

    // datetime2(n) / time(n) / datetimeoffset(n)（括弧内の空白と大文字小文字を許す。桁数を 9 までに絞って int へ必ず収める）
    [GeneratedRegex(
        @"^(?<name>datetime2|time|datetimeoffset)\s*\(\s*(?<p>\d{1,9})\s*\)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex FractionalSecondsPattern();

    // decimal / numeric（引数なし・精度のみ・精度とスケール）
    [GeneratedRegex(
        @"^(?<name>decimal|numeric)(?:\s*\(\s*(?<p>\d{1,9})\s*(?:,\s*(?<s>\d{1,9})\s*)?\))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex DecimalPattern();
}
