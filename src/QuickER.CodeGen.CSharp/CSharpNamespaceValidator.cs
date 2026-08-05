using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 文字列が C# の名前空間として妥当かを判定する共有バリデーター
/// </summary>
/// <remarks>
/// 判定規則は「<c>.</c> 区切りの各セグメントが C# 識別子の綴り（先頭は文字か <c>_</c>、以降は文字・数字・<c>_</c>）
/// であり、かつ予約語（<c>class</c> / <c>int</c> 等）でないこと」。空セグメント（<c>.Foo</c> / <c>Foo.</c> /
/// <c>Foo..Bar</c>）も不正として弾く。逐語的識別子（<c>@</c> 前置）までは踏み込まない
/// （生成器は識別子を <c>@</c> エスケープせずそのまま出力するため、予約語を許すとコンパイル不能になる）。
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

        // RemoveEmptyEntries は付けない。空セグメントを消してしまうと ".Foo" / "Foo." / "Foo..Bar" が
        // 妥当判定になり、"namespace .Foo;" のようなコンパイル不能な出力を無警告で書き出してしまう
        // （空セグメントは識別子の正規表現に一致しないため、そのまま不正として弾かれる）。
        var segments = value.Split('.', StringSplitOptions.TrimEntries);

        return segments.Length > 0
            && segments.All(segment =>
                IdentifierRegex().IsMatch(segment) && !ReservedKeywords.Contains(segment)
            );
    }

    /// <summary>セグメントが C# の予約語かを判定する（名前空間候補のサニタイズ側と判定表を共有するために公開する）</summary>
    public static bool IsReservedKeyword(string segment) => ReservedKeywords.Contains(segment);

    /// <summary>C# 識別子として有効なセグメントにマッチする正規表現</summary>
    [GeneratedRegex(@"^[_\p{L}][\p{L}\p{Nd}_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    /// <summary>
    /// C# の予約語（reserved keywords）。名前空間セグメントに使うとコンパイル不能になるため不正とする
    /// </summary>
    /// <remarks>
    /// 出典は C# 言語リファレンスの「C# keywords」の予約識別子表（77 語）。
    /// 文脈キーワード（<c>var</c> / <c>record</c> / <c>partial</c> / <c>where</c> / <c>nint</c> 等）は
    /// 識別子として合法なため対象にしない（拒否すると妥当な名前空間まで弾いてしまう）。
    /// </remarks>
    private static readonly FrozenSet<string> ReservedKeywords = new[]
    {
        "abstract",
        "as",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while",
    }.ToFrozenSet(StringComparer.Ordinal);
}
