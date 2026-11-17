using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 同期の計画の除外の関数を、何も外れなくなるまで繰り返す周の外から使う参照の判定
/// （規則 <c>sync-exclusions-inside-stable-loop</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 除外の関数は、クラス <c>SyncPlanner</c> の中の、名前が <c>Exclude</c> で始まるメソッド。積むだけの
/// <c>ExcludeSelected</c> と、周そのものの <c>ExcludeUntilStable</c> は数えない。
/// </para>
/// <para>
/// 参照は、名前が <c>Exclude</c> に大文字が続く識別子（<c>this.</c> などで修飾した名前も。<c>ExcludedItems</c> のような名前は数えない）の節で、呼び出し（<c>ExcludeX(…)</c>）と
/// メソッドグループ（<c>SelectionRule r = ExcludeX;</c>・配列の要素・引数）の両方を数える。メソッドの宣言の名前はトークンで識別子の節ではなく、
/// XmlDoc の cref と <c>nameof(…)</c> の中の名前は参照に数えない。
/// </para>
/// <para>
/// 許す場所は 2 つ：名前が <c>Exclude</c> で始まるメソッドの中（周そのものと、ほかの除外の関数の中）と、規則の表のフィールド
/// （<c>SelectionRulesBeforeRounds</c>・<c>SelectionRulesEachRound</c>）の初期化子の中。それ以外は、<c>BuildPlan</c> からの直接の参照も、
/// 補助のメソッド（そのローカル関数・ラムダの中なら、それを含むメソッド）からの参照も、ほかのフィールドの初期化子の中の参照も拾う。
/// 何かは「場所: 参照先」で、場所は参照を含むメソッドの名前か、初期化子を持つフィールドの名前（どちらでもなければ
/// <c>(member outside a method)</c>）。
/// </para>
/// <para>
/// 読みの限界：規則の表の要素を周の外から添字で取り出して呼ぶ形（<c>SelectionRulesEachRound[3](…)</c>）は、参照が表の初期化子の中にしか
/// 無いので拾わない。名前が <c>Exclude</c> で始まらない補助を周の外から呼び、その補助が項目を外す形も拾わない（外す関数は
/// <c>Exclude</c> で、警告だけの関数は <c>Warn</c> で始める、という名前の規約が前提）。<c>using static</c>・別名で名前を変えた参照も拾わない。
/// </para>
/// </remarks>
internal static class SyncExclusionCalls
{
    private const string Planner = "SyncPlanner";
    private const string Prefix = "Exclude";
    private const string Loop = "ExcludeUntilStable";
    private const string Collector = "ExcludeSelected";

    /// <summary>規則の表のフィールドの名前（初期化子の中の参照を許す）</summary>
    private static readonly HashSet<string> RuleTables = new(StringComparer.Ordinal)
    {
        "SelectionRulesBeforeRounds",
        "SelectionRulesEachRound",
    };

    /// <summary>節が周の外からの除外の関数への参照なら「場所: 参照先」</summary>
    public static string? Describe(SyntaxNode node)
    {
        if (ExclusionReference(node) is not { } callee)
        {
            return null;
        }

        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();

        if (method is not null)
        {
            var methodName = method.Identifier.ValueText;

            return methodName.StartsWith(Prefix, StringComparison.Ordinal)
                ? null
                : $"{methodName}: {callee}";
        }

        var field = node.Ancestors()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.Parent?.Parent is FieldDeclarationSyntax);

        if (field is null)
        {
            return $"(member outside a method): {callee}";
        }

        var fieldName = field.Identifier.ValueText;

        return RuleTables.Contains(fieldName) ? null : $"{fieldName}: {callee}";
    }

    /// <summary>節が除外の関数への参照（周の中か外かに依らない）なら、参照先の名前（候補の数の下限に使う）</summary>
    public static string? DescribeCandidate(SyntaxNode node) => ExclusionReference(node);

    /// <summary>節が <c>SyncPlanner</c> の中の除外の関数への参照（呼び出しかメソッドグループ）なら、参照先の名前</summary>
    private static string? ExclusionReference(SyntaxNode node)
    {
        if (node is not IdentifierNameSyntax identifier)
        {
            return null;
        }

        var name = identifier.Identifier.ValueText;

        if (!IsExclusionName(name) || name is Loop or Collector)
        {
            return null;
        }

        // 修飾した名前（this.ExcludeX）は右側の名前だけを数え、左側の受け手の名前は数えない
        if (node.Parent is MemberAccessExpressionSyntax member && member.Name != identifier)
        {
            return null;
        }

        if (node.Ancestors().Any(a => a is CrefSyntax or DocumentationCommentTriviaSyntax))
        {
            return null;
        }

        if (IsInsideNameOf(node))
        {
            return null;
        }

        var owner = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();

        return owner?.Identifier.ValueText == Planner ? name : null;
    }

    /// <summary>
    /// 名前が除外の関数の名前の形（<c>Exclude</c> の直後が大文字）か。<c>ExcludedItems</c> のような <c>Excluded</c> で始まるプロパティ・
    /// 変数の名前は数えない
    /// </summary>
    private static bool IsExclusionName(string name) =>
        name.Length > Prefix.Length
        && name.StartsWith(Prefix, StringComparison.Ordinal)
        && char.IsUpper(name[Prefix.Length]);

    /// <summary>節が <c>nameof(…)</c> の引数の中にあるか</summary>
    private static bool IsInsideNameOf(SyntaxNode node) =>
        node.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
                invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }
                && invocation.ArgumentList.Span.Contains(node.Span)
            );
}
