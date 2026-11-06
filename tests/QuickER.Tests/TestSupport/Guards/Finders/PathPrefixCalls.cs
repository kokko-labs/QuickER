using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>パスの包含を文字列の前方一致で書く呼び出しの判定（規則 <c>path-prefix-comparisons</c> の見つける形）</summary>
internal static class PathPrefixCalls
{
    private const int InitializerDepth = 3;

    private static readonly HashSet<string> PathMembers = new(StringComparer.Ordinal)
    {
        "DirectorySeparatorChar",
        "AltDirectorySeparatorChar",
        "EndsInDirectorySeparator",
        "TrimEndingDirectorySeparator",
        "GetFullPath",
    };

    /// <summary>節が拾う形の呼び出しならその名前（<c>string.StartsWith</c> か、解決できなければ <c>未解決 StartsWith</c>）</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node)
    {
        if (
            node
            is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: nameof(string.StartsWith)
                },
                ArgumentList.Arguments: [var first, ..],
            } invocation
        )
        {
            return null;
        }

        var method = SourceSymbolScan.ResolveMethod(model, invocation);

        if (
            method is not null
            && (
                method.ContainingType?.SpecialType != SpecialType.System_String
                || method.Parameters[0].Type.SpecialType != SpecialType.System_String
            )
        )
        {
            return null;
        }

        if (!MentionsPathMember(model, first.Expression, InitializerDepth))
        {
            return null;
        }

        return method is null ? "未解決 StartsWith" : "string.StartsWith";
    }

    private static bool MentionsPathMember(SemanticModel model, SyntaxNode expression, int depth)
    {
        foreach (
            var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
        )
        {
            var symbol = model.GetSymbolInfo(identifier).Symbol;

            if (
                symbol?.ContainingType?.ToDisplayString() == "System.IO.Path"
                && PathMembers.Contains(symbol.Name)
            )
            {
                return true;
            }

            if (depth > 0 && symbol is ILocalSymbol local)
            {
                foreach (var reference in local.DeclaringSyntaxReferences)
                {
                    if (
                        reference.GetSyntax()
                            is VariableDeclaratorSyntax { Initializer.Value: { } value }
                        && MentionsPathMember(model, value, depth - 1)
                    )
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
