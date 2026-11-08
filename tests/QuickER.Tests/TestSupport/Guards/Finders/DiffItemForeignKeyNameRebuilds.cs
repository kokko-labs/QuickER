using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 差分の項目のリレーション（<c>SchemaDiffItem.Relationship</c>）から外部キーの名前を作り直す呼び出しの判定
/// （規則 <c>sync-selected-foreign-key-names</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 拾うのは名前が <c>ResolveForeignKeyName</c> の呼び出しで、第 1 引数が <c>QuickER.Provider.SchemaDiffItem.Relationship</c> に
/// 解決するもの（<c>item.Relationship</c>・<c>item.Relationship!</c>）。何かは呼び出しを含むメソッドの名前。live の外部キーの
/// リレーション（<c>ResolvedForeignKeyRelationship.Relationship</c>）からの呼び出しは拾わない。
/// </para>
/// <para>読みの限界：リレーションを別の変数へ取ってから渡す形は拾わない。</para>
/// </remarks>
internal static class DiffItemForeignKeyNameRebuilds
{
    private const string DiffItemType = "QuickER.Provider.SchemaDiffItem";

    /// <summary>節が差分の項目のリレーションから名前を作り直す呼び出しなら、それを含むメソッドの名前</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node)
    {
        if (
            node
            is not InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "ResolveForeignKeyName" },
                ArgumentList.Arguments: [var first, ..],
            }
        )
        {
            return null;
        }

        var argument = first.Expression;

        while (argument is PostfixUnaryExpressionSyntax suppress)
        {
            argument = suppress.Operand;
        }

        if (
            model.GetSymbolInfo(argument).Symbol
                is not IPropertySymbol { Name: "Relationship" } property
            || property.ContainingType?.ToDisplayString() != DiffItemType
        )
        {
            return null;
        }

        return node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } method
            ? method.Identifier.ValueText
            : "(member outside a method)";
    }
}
