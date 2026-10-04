using System.Text.RegularExpressions;

namespace QuickER.Provider.Oracle;

/// <summary>
/// Oracle の型表記を「その表記で宣言した列を取り込むと返る表記」へ寄せる。
/// </summary>
/// <remarks>
/// <para>
/// Oracle は宣言の綴りをそのまま保存しない。<c>user_tab_cols</c> から読めるのは型名と精度・スケール・長さで、
/// 取込（<see cref="OracleSchemaImporter.FormatDataType"/>）はそこから表記を組み立て直す。そのため同じ型の
/// 別の綴り（<c>TIMESTAMP</c> と <c>TIMESTAMP(6)</c>、<c>INTEGER</c> と <c>NUMBER(*,0)</c>）を図に書くと、
/// 取り込んだ表記と食い違い、適用しても消えない <c>ALTER COLUMN</c> が差分に出続ける。
/// </para>
/// <para>
/// 規則（既定値の補いと、DB が覚えていない別名の置き換えだけ。意味は変えない）:
/// <list type="bullet">
///   <item><c>TIMESTAMP</c>（<c>WITH TIME ZONE</c> / <c>WITH LOCAL TIME ZONE</c> を含む）の精度なし → <c>(6)</c>（既定の小数秒の桁数）</item>
///   <item><c>NUMBER(p,0)</c> → <c>NUMBER(p)</c>（スケール 0 は精度だけの宣言と区別して保存されない）</item>
///   <item><c>NUMBER(*)</c> → <c>NUMBER</c>（精度もスケールも持たない＝素の <c>NUMBER</c> と同じ）</item>
///   <item><c>INTEGER</c> / <c>INT</c> / <c>SMALLINT</c> → <c>NUMBER(*,0)</c>（Oracle はこれらを精度なし・スケール 0 の NUMBER として保存する）</item>
///   <item><c>FLOAT</c> → <c>FLOAT(126)</c>（既定の 2 進精度）</item>
/// </list>
/// 型名は大文字へ、空白は 1 つへそろえる（取込は大文字で返す）。当てはまらない表記は前後の空白を落とすだけで変えない。
/// </para>
/// <para>
/// 取込もこの関数を通す（<see cref="OracleSchemaImporter.FormatDataType"/>）ので、取込の出力は不動点になる。
/// 選択肢と <c>TryFormat</c> の全表記が取込と一致することは <c>OracleTypeNotationIntegrationTests</c> が実 DB で固定する。
/// </para>
/// </remarks>
public static partial class OracleTypeNotation
{
    /// <summary>型表記を取込が返す表記へ寄せる</summary>
    /// <param name="dataType">図・型の選択肢・<c>TryFormat</c>・取込のいずれかの型表記</param>
    /// <returns>取込が返す表記（当てはまる規則が無ければ前後の空白を除いただけの表記）</returns>
    public static string Normalize(string dataType)
    {
        var text = dataType.Trim();
        var timestamp = TimestampPattern().Match(text);

        if (timestamp.Success)
        {
            var precision = timestamp.Groups["p"].Success ? timestamp.Groups["p"].Value : "6";
            var zone = timestamp.Groups["zone"].Success
                ? " WITH " + (timestamp.Groups["local"].Success ? "LOCAL " : "") + "TIME ZONE"
                : "";
            return $"TIMESTAMP({precision}){zone}";
        }

        var number = NumberPattern().Match(text);

        if (number.Success)
        {
            var precision = number.Groups["p"].Value;
            var scale = number.Groups["s"].Success ? number.Groups["s"].Value : null;

            if (precision == "*")
            {
                return scale is null ? "NUMBER" : $"NUMBER(*,{scale})";
            }

            return scale is null or "0" ? $"NUMBER({precision})" : $"NUMBER({precision},{scale})";
        }

        if (IntegerAliasPattern().IsMatch(text))
        {
            return "NUMBER(*,0)";
        }

        if (FloatPattern().IsMatch(text))
        {
            return "FLOAT(126)";
        }

        return text;
    }

    // TIMESTAMP [(n)] [WITH [LOCAL] TIME ZONE]（大文字小文字・空白の量を問わない）
    [GeneratedRegex(
        @"^TIMESTAMP\s*(?:\(\s*(?<p>\d{1,9})\s*\))?(?<zone>\s+WITH\s+(?<local>LOCAL\s+)?TIME\s+ZONE)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex TimestampPattern();

    // NUMBER(p) / NUMBER(p,s) / NUMBER(*) / NUMBER(*,s)（素の NUMBER はそのまま）
    [GeneratedRegex(
        @"^NUMBER\s*\(\s*(?<p>\d{1,9}|\*)\s*(?:,\s*(?<s>-?\d{1,9})\s*)?\)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex NumberPattern();

    // Oracle が精度なし・スケール 0 の NUMBER として保存する整数の別名
    [GeneratedRegex(
        @"^(?:INTEGER|INT|SMALLINT)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex IntegerAliasPattern();

    // 精度を書かない FLOAT
    [GeneratedRegex(@"^FLOAT$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FloatPattern();
}
