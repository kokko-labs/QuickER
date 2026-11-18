using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 同期の計画の選んだ項目を外す規則（<c>SelectionRules</c> の <c>Exclude*</c>）と規則の表への参照（規則
/// <c>sync-exclusions-inside-stable-loop</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 規則への参照は、名前が <c>Exclude</c> に大文字が続く識別子で、クラス <c>SelectionRules</c> か <c>SyncPlanner</c> の中にあるか、
/// <c>SelectionRules.</c> で修飾したもの（呼び出しとメソッドグループの両方）。周そのもの（<c>ExcludeUntilStable</c>）と、外した項目を
/// 積むだけの <c>ExcludeSelected</c> は数えない。規則の表への参照は、<c>SelectionRules.</c> で修飾した（か <c>SelectionRules</c> の
/// 中の）<c>BeforeRounds</c>・<c>EachRound</c> の読み。
/// </para>
/// <para>
/// 許す場所：規則への参照は、規則の表（<c>SelectionRules</c> のフィールド <c>BeforeRounds</c>・<c>EachRound</c>）の初期化子と、名前が
/// <c>Exclude</c> で始まるメソッドの中。規則の表への参照は、周（<c>ExcludeUntilStable</c>）の中だけ。許す場所の外の参照を
/// 「含むメソッドかフィールド: 名前」で返す（メソッドにもフィールドにも入らない参照は <c>(member outside a method)</c>）。
/// </para>
/// <para>
/// 読みの限界：<c>using static</c>・別名の <c>using</c> で受け手を書かずに、<c>SelectionRules</c>・<c>SyncPlanner</c> の外から呼ぶ形は拾わない。
/// XmlDoc の <c>cref</c> と <c>nameof</c> の中の名前は数えない。
/// </para>
/// </remarks>
internal static class SyncExclusionCalls
{
    private const string Planner = "SyncPlanner";
    private const string Rules = "SelectionRules";
    private const string Prefix = "Exclude";
    private const string Loop = "ExcludeUntilStable";
    private const string Collector = "ExcludeSelected";

    private static readonly HashSet<string> RuleTables = new(StringComparer.Ordinal)
    {
        "BeforeRounds",
        "EachRound",
    };

    /// <summary>節が、許す場所の外にある規則か規則の表への参照なら「含むメソッドかフィールド: 名前」</summary>
    public static string? Describe(SyntaxNode node)
    {
        if (TableReference(node) is { } table)
        {
            var reader = ContainingMethod(node);
            return reader == Loop ? null : $"{reader ?? OutsideMethod}: {table}";
        }

        if (ExclusionReference(node) is not { } callee)
        {
            return null;
        }

        if (ContainingMethod(node) is { } methodName)
        {
            return methodName.StartsWith(Prefix, StringComparison.Ordinal)
                ? null
                : $"{methodName}: {callee}";
        }

        var field = node.Ancestors()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.Parent?.Parent is FieldDeclarationSyntax);

        if (field is null)
        {
            return $"{OutsideMethod}: {callee}";
        }

        var fieldName = field.Identifier.ValueText;

        return RuleTables.Contains(fieldName) && OwnerOf(field) == Rules
            ? null
            : $"{fieldName}: {callee}";
    }

    /// <summary>節が規則か規則の表への参照なら名前（許す場所の参照も数える）</summary>
    public static string? DescribeCandidate(SyntaxNode node) =>
        TableReference(node) ?? ExclusionReference(node);

    private const string OutsideMethod = "(member outside a method)";

    /// <summary>節が規則への参照なら規則の名前</summary>
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

        // 修飾した名前（SelectionRules.ExcludeX・this.ExcludeX）は右側の名前だけを数え、左側の受け手の名前は数えない
        if (node.Parent is MemberAccessExpressionSyntax member && member.Name != identifier)
        {
            return null;
        }

        if (IsDocumentationOrNameOf(node))
        {
            return null;
        }

        return OwnerOf(node) is Planner or Rules || IsQualifiedByRules(identifier) ? name : null;
    }

    /// <summary>節が規則の表の読みなら表の名前</summary>
    private static string? TableReference(SyntaxNode node)
    {
        if (
            node is not IdentifierNameSyntax identifier
            || !RuleTables.Contains(identifier.Identifier.ValueText)
            || IsDocumentationOrNameOf(node)
        )
        {
            return null;
        }

        if (node.Parent is MemberAccessExpressionSyntax member && member.Name != identifier)
        {
            return null;
        }

        return OwnerOf(node) == Rules || IsQualifiedByRules(identifier)
            ? identifier.Identifier.ValueText
            : null;
    }

    /// <summary>名前が <c>SelectionRules.</c> で修飾されているか（<c>global::</c> や名前空間を前に付けた形も）</summary>
    private static bool IsQualifiedByRules(IdentifierNameSyntax identifier) =>
        identifier.Parent is MemberAccessExpressionSyntax member
        && member.Name == identifier
        && member.Expression switch
        {
            IdentifierNameSyntax receiver => receiver.Identifier.ValueText == Rules,
            MemberAccessExpressionSyntax qualified => qualified.Name.Identifier.ValueText == Rules,
            AliasQualifiedNameSyntax qualified => qualified.Name.Identifier.ValueText == Rules,
            _ => false,
        };

    /// <summary>節を含むクラスの名前</summary>
    private static string? OwnerOf(SyntaxNode node) =>
        node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;

    /// <summary>節を含むメソッドの名前（メソッドの外なら null）</summary>
    private static string? ContainingMethod(SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;

    private static bool IsExclusionName(string name) =>
        name.Length > Prefix.Length
        && name.StartsWith(Prefix, StringComparison.Ordinal)
        && char.IsUpper(name[Prefix.Length]);

    private static bool IsDocumentationOrNameOf(SyntaxNode node) =>
        node.Ancestors().Any(a => a is CrefSyntax or DocumentationCommentTriviaSyntax)
        || node.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
                invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }
                && invocation.ArgumentList.Span.Contains(node.Span)
            );
}
