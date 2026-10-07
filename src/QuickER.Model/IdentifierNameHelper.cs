using System.Text.RegularExpressions;

namespace QuickER.Model;

/// <summary>テーブル名・カラム名などの識別子を単語単位で扱うユーティリティ</summary>
/// <remarks>
/// 外部キー列の名前解決（<see cref="ForeignKeyColumnResolver"/>）が用いる。
/// GUI のリレーション作成・内蔵チャットと MCP のツール・Mermaid の取込が同じ命名規則を使うよう、
/// どれからも参照できるモデル側に置く
/// </remarks>
public static class IdentifierNameHelper
{
    /// <summary>スネークケース・パスカルケース等の識別子を単語のリストへ分解する</summary>
    public static List<string> SplitIdentifierWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var normalized = Regex.Replace(value.Trim(), @"[^A-Za-z0-9]+", " ");
        normalized = Regex.Replace(normalized, @"([A-Z]+)([A-Z][a-z])", "$1 $2");
        normalized = Regex.Replace(normalized, @"([a-z0-9])([A-Z])", "$1 $2");
        normalized = Regex.Replace(normalized, @"([A-Za-z])([0-9])", "$1 $2");
        normalized = Regex.Replace(normalized, @"([0-9])([A-Za-z])", "$1 $2");

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static word => word.Length > 0)
            .ToList();
    }

    /// <summary>単語が英語の複数形らしいかどうかを語尾の簡易ルールで判定する</summary>
    public static bool IsLikelyPlural(string word)
    {
        if (word.Length <= 1)
        {
            return false;
        }

        var lower = word.ToLowerInvariant();

        if (
            lower.EndsWith("ies", StringComparison.Ordinal)
            || lower.EndsWith("ses", StringComparison.Ordinal)
            || lower.EndsWith("xes", StringComparison.Ordinal)
            || lower.EndsWith("zes", StringComparison.Ordinal)
            || lower.EndsWith("ches", StringComparison.Ordinal)
            || lower.EndsWith("shes", StringComparison.Ordinal)
            || lower.EndsWith("oes", StringComparison.Ordinal)
        )
        {
            return true;
        }

        return lower.EndsWith('s')
            && !lower.EndsWith("ss", StringComparison.Ordinal)
            && !lower.EndsWith("us", StringComparison.Ordinal)
            && !lower.EndsWith("is", StringComparison.Ordinal);
    }

    /// <summary>単語を語尾の簡易ルールで単数形へ変換する (不規則変化は非対応)</summary>
    /// <remarks>最も確からしい候補（<see cref="SingularizeCandidates"/> の先頭）を返す</remarks>
    public static string SingularizeWord(string word) => SingularizeCandidates(word)[0];

    /// <summary>単語の単数形の候補を、確からしい順に返す (不規則変化は非対応)</summary>
    /// <returns>複数形らしくない単語はその単語だけ。語尾から 1 つに決まらないときは 2 つ</returns>
    /// <remarks>
    /// <para>
    /// <c>-ses</c> / <c>-zes</c> / <c>-ches</c> / <c>-oes</c> などは、<c>es</c> を落とすか <c>s</c> だけを落とすかが語尾から決まらない
    /// （<c>statuses</c>→<c>status</c> と <c>courses</c>→<c>course</c>、<c>matches</c>→<c>match</c> と <c>caches</c>→<c>cache</c>）。
    /// 1 つに決め打つとどちらかの語を必ず取り違えるため、両方を候補として返す（外部キー列名の推定は全候補を期待名に加える）。
    /// </para>
    /// <para>
    /// <c>-ies</c> も同じで、<c>y</c> 由来の語（<c>cities</c>→<c>city</c>）と <c>ie</c> で終わる語（<c>movies</c>→<c>movie</c>）がある。
    /// <c>y</c> へ戻した形を先に、<c>s</c> だけを落とした形を後に置く。
    /// </para>
    /// <para>
    /// 先頭に置く候補は、<c>-sses</c> / <c>-xes</c> / <c>-ches</c> / <c>-shes</c> / <c>-zzes</c> / <c>-oes</c> と、
    /// <c>-uses</c>（<c>-ouses</c> / <c>-auses</c> を除く）で <c>es</c> を落とした形、それ以外の <c>-ses</c> / <c>-zes</c> で
    /// <c>s</c> だけを落とした形（<c>addresses</c>→<c>address</c>・<c>statuses</c>→<c>status</c>・<c>warehouses</c>→<c>warehouse</c>・
    /// <c>courses</c>→<c>course</c>・<c>sizes</c>→<c>size</c>）
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> SingularizeCandidates(string word)
    {
        if (!IsLikelyPlural(word))
        {
            return [word];
        }

        var lower = word.ToLowerInvariant();

        // -ies は y 由来（cities→city）と ie で終わる語（movies→movie）のどちらもある。y 由来が大半なので先に置く
        if (lower.EndsWith("ies", StringComparison.Ordinal) && word.Length > 3)
        {
            return [word[..^3] + "y", word[..^1]];
        }

        string[] ambiguousEndings = ["ses", "xes", "zes", "ches", "shes", "oes"];

        if (!ambiguousEndings.Any(ending => lower.EndsWith(ending, StringComparison.Ordinal)))
        {
            return [word[..^1]];
        }

        string[] esFirstEndings = ["sses", "xes", "ches", "shes", "zzes", "oes"];
        var dropEs = word[..^2];
        var dropS = word[..^1];
        var esFirst =
            esFirstEndings.Any(ending => lower.EndsWith(ending, StringComparison.Ordinal))
            || (
                lower.EndsWith("uses", StringComparison.Ordinal)
                && !lower.EndsWith("ouses", StringComparison.Ordinal)
                && !lower.EndsWith("auses", StringComparison.Ordinal)
            );

        return esFirst ? [dropEs, dropS] : [dropS, dropEs];
    }

    /// <summary>単語を語尾の簡易ルールで複数形へ変換する (不規則変化は非対応)</summary>
    public static string PluralizeWord(string word)
    {
        if (IsLikelyPlural(word))
        {
            return word;
        }

        var lower = word.ToLowerInvariant();

        if (lower.EndsWith('y') && word.Length > 1)
        {
            var beforeLast = char.ToLowerInvariant(word[^2]);

            if (beforeLast is not ('a' or 'e' or 'i' or 'o' or 'u'))
            {
                return word[..^1] + "ies";
            }
        }

        if (
            lower.EndsWith('s')
            || lower.EndsWith('x')
            || lower.EndsWith('z')
            || lower.EndsWith("ch", StringComparison.Ordinal)
            || lower.EndsWith("sh", StringComparison.Ordinal)
            || lower.EndsWith('o')
        )
        {
            return word + "es";
        }

        return word + "s";
    }

    /// <summary>単語を先頭大文字・以降小文字のパスカルケース表記へ整える</summary>
    public static string ToPascalWord(string word)
    {
        if (word.Length == 0)
        {
            return string.Empty;
        }

        if (word.Length == 1)
        {
            return word.ToUpperInvariant();
        }

        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }
}
