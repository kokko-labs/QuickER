using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 同期の計画が同期後の事実（<c>PostSyncSchema</c>）を求める呼び出しの判定
/// （規則 <c>sync-post-sync-schema-single-source</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 拾うのは、受け手の名前（修飾の右端）が <c>PostSyncSchema</c> の静的なメソッドの呼び出し。
/// <c>PostSyncSchema.From(PlanStage.X, …)</c> は「含むメソッド: X」、第 1 引数が <c>PlanStage.X</c> の形でない <c>From</c>
/// （変数・条件式で渡すなど）は「含むメソッド: ?」、ほかの静的なメソッドの直接の呼び出し（<c>PostSyncPrimaryKeyOf</c>・
/// <c>ExplicitlyDroppedUniqueConstraints</c>）は「含むメソッド: メソッド名」。含むメソッドは、ローカル関数・ラムダの中なら
/// それを含むメソッド。
/// </para>
/// <para>
/// 読みの限界：<c>using static</c> で受け手を省いた呼び出しと、別名の <c>using</c> で受け手の名前を変えた呼び出しは拾わない。
/// </para>
/// </remarks>
internal static class PostSyncSchemaCalls
{
    private const string Owner = "PostSyncSchema";
    private const string Factory = "From";
    private const string Stage = "PlanStage";

    /// <summary>節が同期後の事実を求める呼び出しなら「含むメソッド: 時点かメソッド名」</summary>
    public static string? Describe(SyntaxNode node)
    {
        if (Callee(node) is not { } callee)
        {
            return null;
        }

        var invocation = (InvocationExpressionSyntax)node;
        var what =
            callee == Factory
                ? StageOf(invocation.ArgumentList.Arguments.FirstOrDefault())
                : callee;

        return $"{ContainingMember(node)}: {what}";
    }

    /// <summary>節が <c>PostSyncSchema</c> の静的なメソッドの呼び出しなら、そのメソッド名（候補の数の下限に使う）</summary>
    public static string? DescribeCandidate(SyntaxNode node) => Callee(node);

    /// <summary>節が <c>PostSyncSchema</c> の静的なメソッドの呼び出しなら、そのメソッド名</summary>
    private static string? Callee(SyntaxNode node)
    {
        if (
            node
            is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name: var name } access,
            }
        )
        {
            return null;
        }

        var owner = access.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax qualified => qualified.Name.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax qualified => qualified.Name.Identifier.ValueText,
            _ => null,
        };

        return owner == Owner ? name.Identifier.ValueText : null;
    }

    /// <summary>第 1 引数が <c>PlanStage.X</c> の形なら X、そうでなければ <c>?</c></summary>
    private static string StageOf(ArgumentSyntax? argument) =>
        argument?.Expression
            is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: Stage },
                Name: var stage,
            }
            ? stage.Identifier.ValueText
            : "?";

    private static string ContainingMember(SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText
        ?? node.Ancestors()
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault()
            ?.Identifier.ValueText
        ?? "(member outside a method)";
}
