using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 変異テストの Safe Mode に当たる早く抜ける if の判定（規則 <c>mutation-safe-mode-jump-guards</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 拾うのは if 文で、次の 3 つがそろうもの。何かはそれを含むメソッドの名前。
/// </para>
/// <list type="number">
/// <item>条件で変数を宣言する：最上位の <c>||</c> の 2 つ目以降の項の <c>out var x</c>・<c>out T x</c>、または条件のどこかの
/// パターンの変数（<c>is not { } x</c> など。ラムダの中の宣言はラムダの中だけのものなので数えない）</item>
/// <item>本体（ブロックなら最後の文）が、変異テストが消す跳び（<c>continue;</c>・<c>break;</c>・値を返さない <c>return;</c>・
/// <c>throw</c>）</item>
/// <item>その変数を、同じブロックの if より後ろの文で読む</item>
/// </list>
/// <para>
/// 跳びが消えると、条件が真のまま後ろへ進む。2 つ目以降の項は評価されないことがあり、パターンの変数は条件が偽のときだけ
/// 代入される（if の後ろで使えるのはそのため）ので、どちらも確実に代入されず（CS0165）、Stryker はそのメソッドの変異を全部外す。
/// 1 つ目の項の out の宣言は呼び出しが必ず評価されるので数えない。値を返す <c>return x;</c> は Stryker が消さないので数えない。
/// </para>
/// <para>
/// 読みの限界：宣言だけして if / else の枝で代入し、最後の枝の跳びで抜ける形（変数宣言に初期化子が無い形）は拾わない。
/// 変数の読みは名前の一致で見る（同じ名前のラムダの引数も読みに数える）。
/// </para>
/// </remarks>
internal static class SafeModeJumpGuards
{
    /// <summary>節が Safe Mode に当たる早く抜ける if なら、それを含むメソッドの名前</summary>
    public static string? Describe(SyntaxNode node)
    {
        if (
            node is not IfStatementSyntax ifStatement
            || !EndsWithRemovableJump(ifStatement.Statement)
            || ifStatement.Parent is not BlockSyntax block
        )
        {
            return null;
        }

        var operands = OrOperands(ifStatement.Condition);
        var declared = operands
            .Skip(1)
            .SelectMany(OutDeclaredNames)
            .Concat(operands.SelectMany(PatternDeclaredNames))
            .ToHashSet(StringComparer.Ordinal);

        if (declared.Count == 0)
        {
            return null;
        }

        var following = block.Statements.SkipWhile(statement => statement != ifStatement).Skip(1);

        var readAfter = following
            .SelectMany(statement => statement.DescendantNodes().OfType<IdentifierNameSyntax>())
            .Any(name => declared.Contains(name.Identifier.ValueText));

        if (!readAfter)
        {
            return null;
        }

        return node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } method
            ? method.Identifier.ValueText
            : "(member outside a method)";
    }

    /// <summary>節が跳びで終わる if 文なら「跳びで終わる if 文」（空振りの検知の候補）</summary>
    public static string? DescribeCandidate(SyntaxNode node) =>
        node is IfStatementSyntax ifStatement && EndsWithRemovableJump(ifStatement.Statement)
            ? "跳びで終わる if 文"
            : null;

    /// <summary>文（ブロックなら最後の文）が変異テストの消す跳びか</summary>
    private static bool EndsWithRemovableJump(StatementSyntax statement)
    {
        var last = statement is BlockSyntax block ? block.Statements.LastOrDefault() : statement;

        return last switch
        {
            ContinueStatementSyntax or BreakStatementSyntax or ThrowStatementSyntax => true,
            ReturnStatementSyntax { Expression: null } => true,
            _ => false,
        };
    }

    /// <summary>条件の最上位の <c>||</c> の項（括弧は外す。<c>||</c> でなければ 1 項）</summary>
    private static List<ExpressionSyntax> OrOperands(ExpressionSyntax condition)
    {
        var expression = Unparenthesize(condition);

        if (!expression.IsKind(SyntaxKind.LogicalOrExpression))
        {
            return [expression];
        }

        var binary = (BinaryExpressionSyntax)expression;
        return [.. OrOperands(binary.Left), .. OrOperands(binary.Right)];
    }

    private static ExpressionSyntax Unparenthesize(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    /// <summary>項の out の宣言の変数の名前（ラムダの中の宣言は除く）</summary>
    private static IEnumerable<string> OutDeclaredNames(ExpressionSyntax operand) =>
        Designations(operand)
            .Where(designation => designation.Parent is DeclarationExpressionSyntax)
            .Select(designation => designation.Identifier.ValueText);

    /// <summary>項のパターンの変数の名前（ラムダの中の宣言は除く）</summary>
    private static IEnumerable<string> PatternDeclaredNames(ExpressionSyntax operand) =>
        Designations(operand)
            .Where(designation => designation.Parent is PatternSyntax)
            .Select(designation => designation.Identifier.ValueText);

    private static IEnumerable<SingleVariableDesignationSyntax> Designations(
        ExpressionSyntax operand
    ) =>
        operand
            .DescendantNodes(descendIntoChildren: child =>
                child is not AnonymousFunctionExpressionSyntax
            )
            .OfType<SingleVariableDesignationSyntax>();
}
