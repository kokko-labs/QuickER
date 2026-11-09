using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// マウスのボタン・位置・修飾キー・ドラッグ＆ドロップの開始を WPF から直に読む書き方の判定
/// （規則 <c>behavior-input-reads</c> の見つける形）
/// </summary>
/// <remarks>
/// 名前（<see cref="SimpleNameSyntax"/>）ごとにシンボルを引き、宣言した型が WPF の入力の型で名前が読む口のものなら拾う。
/// 受け手（<c>e.</c>・<c>Mouse.</c>）の書き方に依らず、メソッドグループ（<c>e.GetPosition</c> を引数に渡す形）も拾う。
/// 解決できない名前は、名前が読む口のどれかなら「未解決」として拾う（外れる分岐の書き間違いを黙って見逃さない）。
/// </remarks>
internal static class BehaviorInputReads
{
    /// <summary>宣言した型の完全名 → 拾う名前</summary>
    private static readonly Dictionary<string, HashSet<string>> ForbiddenMembers = new(
        StringComparer.Ordinal
    )
    {
        ["System.Windows.Input.MouseEventArgs"] = new(StringComparer.Ordinal)
        {
            "LeftButton",
            "MiddleButton",
            "RightButton",
            "XButton1",
            "XButton2",
            "GetPosition",
        },
        ["System.Windows.Input.MouseButtonEventArgs"] = new(StringComparer.Ordinal)
        {
            "ButtonState",
        },
        ["System.Windows.Input.Mouse"] = new(StringComparer.Ordinal)
        {
            "LeftButton",
            "MiddleButton",
            "RightButton",
            "XButton1",
            "XButton2",
            "GetPosition",
        },
        ["System.Windows.Input.MouseDevice"] = new(StringComparer.Ordinal)
        {
            "LeftButton",
            "MiddleButton",
            "RightButton",
            "XButton1",
            "XButton2",
            "GetPosition",
            "GetButtonState",
        },
        ["System.Windows.Input.Keyboard"] = new(StringComparer.Ordinal) { "Modifiers" },
        ["System.Windows.Input.KeyboardDevice"] = new(StringComparer.Ordinal) { "Modifiers" },
        ["System.Windows.DragDrop"] = new(StringComparer.Ordinal) { "DoDragDrop" },
    };

    private static readonly HashSet<string> ForbiddenNames = ForbiddenMembers
        .Values.SelectMany(names => names)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>節が拾う読み方なら「型.名前」（解決できなければ「未解決 名前」）</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node)
    {
        if (node is not SimpleNameSyntax name)
        {
            return null;
        }

        var text = name.Identifier.ValueText;

        if (!ForbiddenNames.Contains(text))
        {
            return null;
        }

        // 受け手の側（a.b の a）は名前でなく式として読まれるので、b の側でだけ数える
        if (name.Parent is MemberAccessExpressionSyntax access && access.Expression == name)
        {
            return null;
        }

        var info = model.GetSymbolInfo(name);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();

        if (symbol is null)
        {
            return $"未解決 {text}";
        }

        var type = symbol.ContainingType?.ToDisplayString();

        return
            type is not null
            && ForbiddenMembers.TryGetValue(type, out var members)
            && members.Contains(symbol.Name)
            ? $"{symbol.ContainingType!.Name}.{symbol.Name}"
            : null;
    }
}
