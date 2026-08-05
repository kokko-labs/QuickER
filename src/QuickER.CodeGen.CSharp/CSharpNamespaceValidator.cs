using System.Text.RegularExpressions;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 文字列が C# の名前空間として妥当かを判定する共有バリデーター
/// </summary>
/// <remarks>
/// 判定規則は「<c>.</c> 区切りの各セグメントが C# 識別子の綴り（先頭は文字か <c>_</c>、以降は文字・数字・<c>_</c>）」
/// の簡易検証で、キーワード衝突・逐語的識別子（<c>@</c>）までは踏み込まない。
/// 生成前検証（<see cref="CSharpCodeGenerationService"/>）と GUI の入力検証（生成ダイアログ）で
/// 同一規則を使うための単一正本として、ここに置く（CLI / MCP / GUI で判定がずれないようにする）。
/// </remarks>
public static partial class CSharpNamespaceValidator
{
    /// <summary>名前空間として妥当な形式かを判定する（null・空白は不正扱い）</summary>
    /// <remarks>
    /// 空白のときに既定値へフォールバックするオプション（<see cref="CodeGenerationOptions.RootNamespace"/> 等）は、
    /// 呼び出し側で「空白なら検証対象外」と判断してから本メソッドを使う
    /// </remarks>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var segments = value.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        return segments.Length > 0 && segments.All(segment => IdentifierRegex().IsMatch(segment));
    }

    /// <summary>C# 識別子として有効なセグメントにマッチする正規表現</summary>
    [GeneratedRegex(@"^[_\p{L}][\p{L}\p{Nd}_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();
}
