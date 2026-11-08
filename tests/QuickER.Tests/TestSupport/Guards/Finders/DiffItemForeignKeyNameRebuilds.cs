using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 差分の項目から外部キーの名前を作り直す呼び出しの判定（規則 <c>sync-selected-foreign-key-names</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 拾うのは次の 2 つ。何かは呼び出しを含むメソッドの名前。
/// </para>
/// <list type="bullet">
/// <item>名前が <c>ResolveForeignKeyName</c> の呼び出しで、第 1 引数が <c>QuickER.Provider.SchemaDiffItem.Relationship</c> に
/// 解決するもの（<c>item.Relationship</c>・<c>item.Relationship!</c>）。live の外部キーのリレーション
/// （<c>ResolvedForeignKeyRelationship.Relationship</c>）からの呼び出しは拾わない。</item>
/// <item>名前が <c>EffectiveForeignKey</c> の呼び出しで、第 1 引数（<c>??</c> の左辺を含む）が
/// <c>QuickER.Provider.SchemaDiffItem.ForeignKeyName</c> に解決するもの（スクリプトが付ける名前と同じ規則を写した呼び出し。
/// 差分の項目の名前が無いときにリレーションの名前へ落とす規則が食い違いうる）。</item>
/// </list>
/// <para>読みの限界：リレーション・名前を別の変数へ取ってから渡す形は拾わない。</para>
/// </remarks>
internal static class DiffItemForeignKeyNameRebuilds
{
    private const string DiffItemType = "QuickER.Provider.SchemaDiffItem";

    /// <summary>節が差分の項目から名前を作り直す呼び出しなら、それを含むメソッドの名前</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node)
    {
        if (
            node
            is not InvocationExpressionSyntax { ArgumentList.Arguments: [var first, ..] } invocation
        )
        {
            return null;
        }

        var name = invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => null,
        };
        var property = name switch
        {
            "ResolveForeignKeyName" => "Relationship",
            "EffectiveForeignKey" => "ForeignKeyName",
            _ => null,
        };

        if (property is null || !ReadsDiffItemProperty(model, first.Expression, property))
        {
            return null;
        }

        return node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } method
            ? method.Identifier.ValueText
            : "(member outside a method)";
    }

    /// <summary>式（<c>!</c>・<c>??</c> の左辺を外した形）が差分の項目のこのプロパティの読み取りか</summary>
    private static bool ReadsDiffItemProperty(
        SemanticModel model,
        ExpressionSyntax expression,
        string property
    )
    {
        while (true)
        {
            switch (expression)
            {
                case PostfixUnaryExpressionSyntax suppress:
                    expression = suppress.Operand;
                    continue;
                case BinaryExpressionSyntax coalesce
                    when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                    expression = coalesce.Left;
                    continue;
            }

            break;
        }

        return model.GetSymbolInfo(expression).Symbol is IPropertySymbol symbol
            && symbol.Name == property
            && symbol.ContainingType?.ToDisplayString() == DiffItemType;
    }
}
