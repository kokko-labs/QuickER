using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 同期の計画の評価の規則（<c>AssessmentRules</c> の <c>Warn*</c>）への参照（規則 <c>sync-assessment-rule-callers</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 規則への参照は、名前が <c>Warn</c> に大文字が続く識別子で、<c>AssessmentRules.</c> で修飾してあるか、クラス
/// <c>AssessmentRules</c> の中にあるもの（呼び出しとメソッドグループの両方）。規則の宣言そのもの・XmlDoc の cref・nameof の中の
/// 名前は数えない。
/// </para>
/// <para>
/// 規則の表（<c>AssessmentRules</c> のフィールド <c>All</c>）の初期化子の中の参照は拾わない。ほかの参照は「含むメソッド: 規則の名前」で
/// 返す（メソッドの外ならフィールドの名前）。
/// </para>
/// <para>
/// 読みの限界：<c>using static</c> でクラス名を省いた呼び出しと、別名の <c>using</c> で受け手の名前を変えた呼び出しは拾わない。
/// </para>
/// </remarks>
internal static class SyncAssessmentRuleCalls
{
    private const string Rules = "AssessmentRules";
    private const string Prefix = "Warn";
    private const string RuleTable = "All";
    private const string OutsideMethod = "(member outside a method)";

    /// <summary>規則の表の外にある、評価の規則への参照を「含むメソッド: 規則の名前」で返す（当たらなければ null）</summary>
    public static string? Describe(SyntaxNode node)
    {
        if (RuleReference(node) is not { } rule)
        {
            return null;
        }

        if (ContainingMethod(node) is { } methodName)
        {
            return $"{methodName}: {rule}";
        }

        var field = node.Ancestors()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.Parent?.Parent is FieldDeclarationSyntax);

        if (field is null)
        {
            return $"{OutsideMethod}: {rule}";
        }

        var fieldName = field.Identifier.ValueText;

        return fieldName == RuleTable && OwnerOf(field) == Rules ? null : $"{fieldName}: {rule}";
    }

    /// <summary>評価の規則への参照（規則の表の中も数える。空振りの検知の候補）</summary>
    public static string? DescribeCandidate(SyntaxNode node) => RuleReference(node);

    private static string? RuleReference(SyntaxNode node)
    {
        if (node is not IdentifierNameSyntax identifier)
        {
            return null;
        }

        var name = identifier.Identifier.ValueText;

        if (!IsRuleName(name))
        {
            return null;
        }

        // 修飾した名前（AssessmentRules.WarnX）は右側の名前だけを数え、左側の受け手の名前は数えない
        if (node.Parent is MemberAccessExpressionSyntax member && member.Name != identifier)
        {
            return null;
        }

        if (IsDocumentationOrNameOf(node))
        {
            return null;
        }

        return IsQualifiedByRules(identifier) || IsUnqualifiedInsideRules(identifier) ? name : null;
    }

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

    private static bool IsUnqualifiedInsideRules(IdentifierNameSyntax identifier) =>
        identifier.Parent is not MemberAccessExpressionSyntax && OwnerOf(identifier) == Rules;

    private static string? OwnerOf(SyntaxNode node) =>
        node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;

    private static string? ContainingMethod(SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;

    private static bool IsRuleName(string name) =>
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
