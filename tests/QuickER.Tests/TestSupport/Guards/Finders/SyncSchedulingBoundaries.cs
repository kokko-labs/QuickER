using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 同期の計画の組み立て（<c>Sync/Scheduling</c>）の境界を見る形：差分の項目・警告を作る所と、計画器の入口を呼ぶ所
/// </summary>
/// <remarks>
/// 読むのは構文だけ。型を書かない <c>new()</c>（対象の型から決まる生成）と、別名の <c>using</c> で名前を変えた型は拾わない。
/// </remarks>
internal static class SyncSchedulingBoundaries
{
    /// <summary>
    /// 差分の項目（<c>SchemaDiffItem</c>）・警告（<c>SyncPlanWarning</c>）を作る式。何かは「new 型」
    /// </summary>
    public static string? DescribeItemOrWarningCreation(SyntaxNode node) =>
        node is ObjectCreationExpressionSyntax creation
        && TypeName(creation.Type) is "SchemaDiffItem" or "SyncPlanWarning"
            ? $"new {TypeName(creation.Type)}"
            : null;

    /// <summary>
    /// 差分の項目（<c>SchemaDiffItem</c>）を作る式。警告を作る式は数えない
    /// （規則 <c>sync-assessment-builds-warnings-only</c> の見つける形）
    /// </summary>
    public static string? DescribeItemCreation(SyntaxNode node) =>
        DescribeItemOrWarningCreation(node) is { } what && what != "new SyncPlanWarning"
            ? what
            : null;

    /// <summary>
    /// 計画器の入口の呼び出し（<c>StepPlanner.Plan(…)</c>。何かは「含むメソッド: StepPlanner.Plan」。
    /// メソッドの外なら「(member outside a method): StepPlanner.Plan」）
    /// </summary>
    /// <remarks>
    /// 受け手を <c>StepPlanner</c> と書いた呼び出しとメソッドグループを拾う（完全修飾も）。<c>using static</c> で受け手を省いた
    /// 呼び出しは拾わない。
    /// </remarks>
    public static string? DescribeStepPlannerCall(SyntaxNode node)
    {
        if (
            node
            is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Plan",
                Expression: var receiver
            }
        )
        {
            return null;
        }

        var receiverName = receiver switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax qualified => qualified.Name.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            _ => null,
        };

        if (receiverName != "StepPlanner")
        {
            return null;
        }

        var method = node.Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault()
            ?.Identifier.ValueText;

        return $"{method ?? "(member outside a method)"}: StepPlanner.Plan";
    }

    /// <summary>型の名前の右端（名前空間で修飾した名前は最後の部分）</summary>
    private static string? TypeName(TypeSyntax type) =>
        type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            _ => null,
        };
}
