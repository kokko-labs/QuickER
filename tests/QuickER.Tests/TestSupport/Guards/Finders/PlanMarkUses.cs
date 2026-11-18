using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 計画器が自分で作った差分の項目に付ける印（<c>SchemaDiffItem.PlanMark</c>）を付ける所・読む所の判定
/// （規則 <c>sync-plan-mark-owners</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 拾うのは、名前 <c>PlanMark</c> の参照。オブジェクトの初期化子（<c>new X { PlanMark = … }</c>・<c>with { PlanMark = … }</c>）の左辺は
/// 「含むメソッド: init」、それ以外（<c>x.PlanMark</c>・<c>x?.PlanMark</c>・<c>nameof</c> の中）は「含むメソッド: read」。
/// </para>
/// <para>
/// プロパティの宣言・XmlDoc の <c>cref</c>・文字列・コメントは拾わない。
/// </para>
/// </remarks>
internal static class PlanMarkUses
{
    private const string Member = "PlanMark";
    private const string Init = "init";
    private const string Read = "read";

    /// <summary>節が印の参照なら「含むメソッド: init」か「含むメソッド: read」</summary>
    public static string? Describe(SyntaxNode node)
    {
        if (
            node is not IdentifierNameSyntax { Identifier.ValueText: Member } name
            || name.IsPartOfStructuredTrivia()
        )
        {
            return null;
        }

        return $"{ContainingMember(node)}: {(IsInitializerTarget(name) ? Init : Read)}";
    }

    /// <summary>節が印を付ける所（初期化子の左辺）なら「含むメソッド: init」（候補の数の下限に使う）</summary>
    public static string? DescribeInit(SyntaxNode node) =>
        node is IdentifierNameSyntax { Identifier.ValueText: Member } name
        && !name.IsPartOfStructuredTrivia()
        && IsInitializerTarget(name)
            ? $"{ContainingMember(node)}: {Init}"
            : null;

    private static bool IsInitializerTarget(IdentifierNameSyntax name) =>
        name.Parent is AssignmentExpressionSyntax assignment
        && assignment.Left == name
        && assignment.Parent is InitializerExpressionSyntax initializer
        && (
            initializer.IsKind(SyntaxKind.ObjectInitializerExpression)
            || initializer.IsKind(SyntaxKind.WithInitializerExpression)
        );

    private static string ContainingMember(SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText
        ?? node.Ancestors()
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault()
            ?.Identifier.ValueText
        ?? "(member outside a method)";
}
