using System.Linq;
using System.Text;

namespace QuickER.CodeGen.UI;

/// <summary>
/// シグネチャプレビュー専用の簡易フォーマッタ（方言中立トークン → C# 型名・テーブル名 → Entity クラス名）
/// </summary>
/// <remarks>
/// あくまで画面上の目安表示のための近似変換。厳密な型解決はプロバイダ層（<c>QueryParameterTypeResolver</c>）と
/// 生成器の責務で、ここでは主要トークンだけを簡易表で写す（未知トークンはそのまま返す）。
/// </remarks>
internal static class QueryTypeTokenFormatter
{
    /// <summary>方言中立トークンを C# 型名へ近似変換する（未知はトークンをそのまま返す）</summary>
    public static string ToCSharpType(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "object";
        }

        var trimmed = token.Trim();
        var parenIndex = trimmed.IndexOf('(');
        var baseToken = (parenIndex >= 0 ? trimmed[..parenIndex] : trimmed)
            .Trim()
            .ToLowerInvariant();

        return baseToken switch
        {
            "int32" or "int" => "int",
            "int64" or "long" => "long",
            "int16" or "short" => "short",
            "byte" => "byte",
            "string" or "text" => "string",
            "decimal" => "decimal",
            "double" => "double",
            "float" or "single" => "float",
            "boolean" or "bool" => "bool",
            "datetime" => "DateTime",
            "datetimeoffset" => "DateTimeOffset",
            "date" => "DateOnly",
            "time" => "TimeOnly",
            "guid" => "Guid",
            _ => trimmed,
        };
    }

    /// <summary>テーブル名から Entity クラス名を近似生成する（PascalCase 化 ＋ Entity サフィックス）</summary>
    /// <remarks>単数形化は行わない簡易版（生成器の厳密な命名変換とは別物の目安）。</remarks>
    public static string ToEntityClassName(string tableName)
    {
        var pascal = ToPascalCase(tableName);

        if (pascal.Length == 0)
        {
            pascal = "Entity";
        }

        return pascal.EndsWith("Entity", StringComparison.Ordinal) ? pascal : pascal + "Entity";
    }

    /// <summary>区切り文字・大小境界で単語分割し、各単語を先頭大文字化して連結する</summary>
    private static string ToPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var startOfWord = true;

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(startOfWord ? char.ToUpperInvariant(c) : c);
                startOfWord = false;
            }
            else
            {
                startOfWord = true;
            }
        }

        return builder.ToString();
    }
}
