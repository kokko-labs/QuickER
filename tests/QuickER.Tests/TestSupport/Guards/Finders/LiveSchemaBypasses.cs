using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 同期の計画が DB の今の図を、live の状態（<c>LiveSchema</c>）を通さずに読む箇所の判定
/// （規則 <c>sync-live-schema-single-source</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 拾うのは、メンバーの読み <c>x.LiveEntities</c>・<c>x.LiveRelationships</c>（<c>?.</c> を含む）と、
/// <c>ForeignKeyColumnPairResolver.ResolveAll(…)</c> の呼び出し。何かは「含むメソッド: 読んだもの」。
/// </para>
/// <para>
/// プロパティの宣言と、オブジェクトの初期化子の左辺（<c>LiveEntities = …</c>）は、メンバーの読みの形でないので拾わない
/// （<c>SyncPlanContext</c> の定義と <c>FromImport</c>）。
/// </para>
/// <para>
/// 読みの限界：live の表の一覧を別の名前の変数・引数で受け取ってから索引を作り直す形は拾わない（文脈から読む所だけを見る）。
/// </para>
/// </remarks>
internal static class LiveSchemaBypasses
{
    private const string State = "liveSchema";
    private const string Resolver = "ForeignKeyColumnPairResolver";
    private const string ResolveAll = "ResolveAll";

    /// <summary>節が live の図の直接の読みなら「含むメソッド: 読んだもの」</summary>
    public static string? Describe(SyntaxNode node)
    {
        var what = node switch
        {
            MemberAccessExpressionSyntax member when IsLiveMember(member.Name) => member
                .Name
                .Identifier
                .ValueText,
            MemberBindingExpressionSyntax binding when IsLiveMember(binding.Name) => binding
                .Name
                .Identifier
                .ValueText,
            InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax owner,
                    Name: var name,
                },
            }
                when owner.Identifier.ValueText == Resolver
                    && name.Identifier.ValueText == ResolveAll => $"{Resolver}.{ResolveAll}",
            _ => null,
        };

        return what is null ? null : $"{ContainingMember(node)}: {what}";
    }

    /// <summary>節が live の状態の読み（<c>liveSchema.X</c>・<c>liveSchema?.X</c>・<c>liveSchema!.X</c>）なら、読んだメンバー（候補の数の下限に使う）</summary>
    public static string? DescribeCandidate(SyntaxNode node) =>
        node switch
        {
            MemberAccessExpressionSyntax member when IsState(member.Expression) => member
                .Name
                .Identifier
                .ValueText,
            ConditionalAccessExpressionSyntax
            {
                Expression: var target,
                WhenNotNull: MemberBindingExpressionSyntax binding,
            } when IsState(target) => binding.Name.Identifier.ValueText,
            _ => null,
        };

    private static bool IsLiveMember(SimpleNameSyntax name) =>
        name.Identifier.ValueText is "LiveEntities" or "LiveRelationships";

    private static bool IsState(ExpressionSyntax expression) =>
        expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == State,
            PostfixUnaryExpressionSyntax { Operand: IdentifierNameSyntax identifier } => identifier
                .Identifier
                .ValueText == State,
            _ => false,
        };

    private static string ContainingMember(SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText
        ?? node.Ancestors()
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault()
            ?.Identifier.ValueText
        ?? "(member outside a method)";
}
