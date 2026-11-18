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
