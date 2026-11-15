using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 同期の計画の除外の関数を、何も外れなくなるまで繰り返す周の外から呼ぶ呼び出しの判定
/// （規則 <c>sync-exclusions-inside-stable-loop</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 除外の関数は、クラス <c>SyncPlanner</c> の中の、名前が <c>Exclude</c> で始まるメソッドの呼び出し。積むだけの
/// <c>ExcludeSelected</c> と、周そのものの <c>ExcludeUntilStable</c> は数えない。
/// </para>
/// <para>
/// 拾うのは、呼び出しを含むメソッド（ローカル関数・ラムダの中なら、それを含むメソッド）が <c>ExcludeUntilStable</c> でも、名前が
/// <c>Exclude</c> で始まるメソッドでもないもの。<c>BuildPlan</c> からの直接の呼び出しも、補助のメソッドを経た呼び出しも拾う。
/// 何かは「呼び出し元: 呼び出し先」。
/// </para>
/// <para>
/// 読みの限界：メソッドをデリゲートへ取ってから呼ぶ形は拾わない。名前が <c>Exclude</c> で始まらない補助を周の外から呼び、その補助が
/// 項目を外す形も拾わない（外す関数は <c>Exclude</c> で、警告だけの関数は <c>Warn</c> で始める、という名前の規約が前提）。
/// </para>
/// </remarks>
internal static class SyncExclusionCalls
{
    private const string Planner = "SyncPlanner";
    private const string Prefix = "Exclude";
    private const string Loop = "ExcludeUntilStable";
    private const string Collector = "ExcludeSelected";

    /// <summary>節が周の外からの除外の関数の呼び出しなら「呼び出し元: 呼び出し先」</summary>
    public static string? Describe(SyntaxNode node)
    {
        if (ExclusionCallee(node) is not { } callee)
        {
            return null;
        }

        var caller = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var callerName = caller?.Identifier.ValueText ?? "(member outside a method)";

        return callerName.StartsWith(Prefix, StringComparison.Ordinal)
            ? null
            : $"{callerName}: {callee}";
    }

    /// <summary>節が除外の関数の呼び出し（周の中か外かに依らない）なら、呼び出し先の名前（候補の数の下限に使う）</summary>
    public static string? DescribeCandidate(SyntaxNode node) => ExclusionCallee(node);

    /// <summary>節が <c>SyncPlanner</c> の中の除外の関数の呼び出しなら、呼び出し先の名前</summary>
    private static string? ExclusionCallee(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        var name = invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => null,
        };

        if (
            name is null
            || !name.StartsWith(Prefix, StringComparison.Ordinal)
            || name is Loop or Collector
        )
        {
            return null;
        }

        var owner = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();

        return owner?.Identifier.ValueText == Planner ? name : null;
    }
}
