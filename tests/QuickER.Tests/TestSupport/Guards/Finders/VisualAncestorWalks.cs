using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>視覚・論理ツリーの親をたどる呼び出しの判定（規則 <c>behavior-visual-ancestor-copies</c> の見つける形）</summary>
internal static class VisualAncestorWalks
{
    private static readonly HashSet<string> TreeHelpers = new(StringComparer.Ordinal)
    {
        "System.Windows.Media.VisualTreeHelper",
        "System.Windows.LogicalTreeHelper",
    };

    /// <summary>節が親をたどる呼び出しなら「型.GetParent」（解決できなければ「未解決 GetParent」）</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node)
    {
        if (
            node
            is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetParent" },
            } invocation
        )
        {
            return null;
        }

        var method = SourceSymbolScan.ResolveMethod(model, invocation);

        if (method is null)
        {
            return "未解決 GetParent";
        }

        return TreeHelpers.Contains(SourceSymbolScan.ContainingTypeName(method) ?? "")
            ? $"{method.ContainingType.Name}.GetParent"
            : null;
    }
}
