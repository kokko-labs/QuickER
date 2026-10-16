using System.Text.RegularExpressions;

namespace QuickER.Provider.MySql;

/// <summary>
/// MySQL の型表記を「その表記で宣言した列を取り込むと返る表記」（<c>information_schema.COLUMNS.COLUMN_TYPE</c>）へ寄せる。
/// </summary>
/// <remarks>
/// <para>
/// MySQL は <c>decimal</c> の精度・スケールを省いた宣言を既定値で補って保存し、<c>COLUMN_TYPE</c> は補った形で返す
/// （<c>decimal(12)</c> → <c>decimal(12,0)</c>・素の <c>decimal</c> → <c>decimal(10,0)</c>）。図・型の選択肢・
/// <c>TryFormat</c> の表記と取り込んだ表記をそのまま比べると、適用しても消えない <c>MODIFY</c> が差分に出続ける
/// （実 MySQL 8.4 で確認済み。適用すると列の <c>DEFAULT</c> まで消えた）。
/// </para>
/// <para>
/// 規則（既定値の補いだけ。意味は変えない）:
/// <list type="bullet">
///   <item><c>decimal(p)</c> / <c>numeric(p)</c> → <c>decimal(p,0)</c>（スケールの既定は 0）</item>
///   <item>素の <c>decimal</c> / <c>numeric</c> → <c>decimal(10,0)</c>（精度の既定は 10）</item>
///   <item><c>numeric(p,s)</c> → <c>decimal(p,s)</c>（<c>numeric</c> は <c>decimal</c> の別名として保存される）</item>
/// </list>
/// 当てはまらない表記は前後の空白を落とすだけで変えない。照合は大文字小文字を区別せず、当てはまった表記は小文字で返す。
/// </para>
/// <para>
/// 取込もこの関数を通す（<c>COLUMN_TYPE</c> はすでにこの形なので不動点）。選択肢と <c>TryFormat</c> の全表記が
/// 取込と一致することは <c>MySqlTypeNotationIntegrationTests</c> が実 DB で固定する。
/// </para>
/// </remarks>
public static partial class MySqlTypeNotation
{
    /// <summary>精度を省いた decimal の既定の精度</summary>
    private const string DefaultDecimalPrecision = "10";

    /// <summary>型表記を取込が返す表記へ寄せる</summary>
    /// <param name="dataType">図・型の選択肢・<c>TryFormat</c>・取込のいずれかの型表記</param>
    /// <returns>取込が返す表記（当てはまる規則が無ければ前後の空白を除いただけの表記）</returns>
    public static string Normalize(string dataType)
    {
        var text = dataType.Trim();
        var number = DecimalPattern().Match(text);

        if (!number.Success)
        {
            return text;
        }

        var precision = number.Groups["p"].Success
            ? number.Groups["p"].Value
            : DefaultDecimalPrecision;
        var scale = number.Groups["s"].Success ? number.Groups["s"].Value : "0";
        var suffix = number.Groups["mods"].Value.Trim().ToLowerInvariant();

        return suffix.Length == 0
            ? $"decimal({precision},{scale})"
            : $"decimal({precision},{scale}) {suffix}";
    }

    // decimal / numeric（引数なし・精度のみ・精度とスケール）と、後ろの unsigned / zerofill
    [GeneratedRegex(
        @"^(?:decimal|numeric|dec)(?:\s*\(\s*(?<p>[0-9]{1,9})\s*(?:,\s*(?<s>[0-9]{1,9})\s*)?\))?(?<mods>(?:\s+(?:unsigned|zerofill))*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex DecimalPattern();
}
