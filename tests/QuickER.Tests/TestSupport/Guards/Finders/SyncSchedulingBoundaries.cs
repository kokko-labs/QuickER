using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 同期の計画の新しい組み立て（<c>Sync/Scheduling</c>）の境界を見る形：計画器の途中の結果を受け取る入れ物を作る所と、
/// 差分の項目・警告を作る所
/// </summary>
/// <remarks>
/// 読むのは構文だけ。型を書かない <c>new()</c>（対象の型から決まる生成）と、別名の <c>using</c> で名前を変えた型は拾わない。
/// </remarks>
internal static class SyncSchedulingBoundaries
{
    private const string Probe = "SyncPlanProbe";

    /// <summary>入れ物（<c>SyncPlanProbe</c>）を作る式（何かは「new SyncPlanProbe」）</summary>
    public static string? DescribeProbeCreation(SyntaxNode node) =>
        node is ObjectCreationExpressionSyntax creation && TypeName(creation.Type) == Probe
            ? $"new {Probe}"
            : null;

    /// <summary>
    /// 差分の項目（<c>SchemaDiffItem</c>）・警告（<c>SyncPlanWarning</c>）を作る式と、差分の項目の複製を作る呼び出し
    /// （<c>WithForeignKeyColumnPairs</c>・<c>WithRearrangedForeignKeyColumnPairs</c>）。何かは「new 型」か呼び出しの名前
    /// </summary>
    public static string? DescribeItemOrWarningCreation(SyntaxNode node) =>
        node switch
        {
            ObjectCreationExpressionSyntax creation
                when TypeName(creation.Type) is "SchemaDiffItem" or "SyncPlanWarning" =>
                $"new {TypeName(creation.Type)}",
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }
                when IsItemCopy(member.Name) => member.Name.Identifier.ValueText,
            InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding }
                when IsItemCopy(binding.Name) => binding.Name.Identifier.ValueText,
            _ => null,
        };

    /// <summary>
    /// 差分の項目（<c>SchemaDiffItem</c>）を作る式と、差分の項目の複製を作る呼び出し。警告を作る式は数えない
    /// （規則 <c>sync-assessment-builds-warnings-only</c> の見つける形）
    /// </summary>
    public static string? DescribeItemCreation(SyntaxNode node) =>
        DescribeItemOrWarningCreation(node) is { } what && what != "new SyncPlanWarning"
            ? what
            : null;

    /// <summary>
    /// 新しい計画器の入口の呼び出し（<c>SequentialPlanner.Plan(…)</c>。何かは「含むメソッド: SequentialPlanner.Plan」。
    /// メソッドの外なら「(member outside a method): SequentialPlanner.Plan」）
    /// </summary>
    /// <remarks>
    /// 受け手を <c>SequentialPlanner</c> と書いた呼び出しとメソッドグループを拾う（完全修飾も）。<c>using static</c> で受け手を省いた
    /// 呼び出しは拾わない。
    /// </remarks>
    public static string? DescribeSequentialPlannerCall(SyntaxNode node)
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

        if (receiverName != "SequentialPlanner")
        {
            return null;
        }

        var method = node.Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault()
            ?.Identifier.ValueText;

        return $"{method ?? "(member outside a method)"}: SequentialPlanner.Plan";
    }

    private static bool IsItemCopy(SimpleNameSyntax name) =>
        name.Identifier.ValueText
            is "WithForeignKeyColumnPairs"
                or "WithRearrangedForeignKeyColumnPairs";

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
