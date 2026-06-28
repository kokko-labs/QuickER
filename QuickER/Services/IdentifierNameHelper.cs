using System.Text.RegularExpressions;

namespace QuickER.Services;

/// <summary>テーブル名・カラム名などの識別子を単語単位で扱うユーティリティ</summary>
/// <remarks>AI スキーマの命名正規化と外部キー列の名前解決（<see cref="ForeignKeyColumnResolver"/>）で共用する</remarks>
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
    public static string SingularizeWord(string word)
    {
        if (!IsLikelyPlural(word))
        {
            return word;
        }

        var lower = word.ToLowerInvariant();

        if (lower.EndsWith("ies", StringComparison.Ordinal) && word.Length > 3)
        {
            return word[..^3] + "y";
        }

        if (
            lower.EndsWith("ches", StringComparison.Ordinal)
            || lower.EndsWith("shes", StringComparison.Ordinal)
            || lower.EndsWith("xes", StringComparison.Ordinal)
            || lower.EndsWith("zes", StringComparison.Ordinal)
            || lower.EndsWith("ses", StringComparison.Ordinal)
            || lower.EndsWith("oes", StringComparison.Ordinal)
        )
        {
            return word[..^2];
        }

        return word[..^1];
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
