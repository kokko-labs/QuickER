using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 変異テストの Safe Mode に当たる早く抜ける if の判定（規則 <c>mutation-safe-mode-jump-guards</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 拾うのは、同じブロックの中の if 文で、次の 2 つの形のどちらかに当たるもの。何かはそれを含むメソッドの名前。
/// </para>
/// <para>
/// 形 1（条件で宣言する変数）：次の 3 つがそろうもの。
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
/// 形 2（枝で代入する変数）：if / else if の鎖の先頭の if で、次の 3 つがそろうもの。
/// </para>
/// <list type="number">
/// <item>鎖の最後の枝が else で、その本体（ブロックなら最後の文）が変異テストが消す跳び</item>
/// <item>同じブロックの if より前で初期化子なしに宣言し、宣言と if の間で代入しないローカル変数を、鎖の跳びで終わらない枝で
/// 代入する（単純な代入 <c>x = …</c> か <c>out x</c> の引数。ラムダの中の代入は数えない）</item>
/// <item>その変数を、同じブロックの if より後ろの文で読む</item>
/// </list>
/// <para>
/// 最後の else の跳びが消えると、どの枝も代入しないまま後ろへ進むので、変数が確実に代入されず（CS0165）、形 1 と同じく
/// Stryker はそのメソッドの変異を全部外す（<c>RefusableChangeNaming.Constraints</c> で 62 件のうち 56 件が外れた）。
/// </para>
/// <para>
/// 読みの限界：変数の読みと代入は名前の一致で見る（同じ名前のラムダの引数も読みに数える）。宣言と if の間の代入は、
/// 確実に代入するかを問わず代入とみなして外す（条件つきの代入の後の鎖は拾わない）。鎖の外側のブロックへ跳ぶ形
/// （跳びで終わる枝の中の入れ子の if の跳び）と、鎖の後ろの別のブロックの読みは見ない。
/// </para>
/// </remarks>
internal static class SafeModeJumpGuards
{
    /// <summary>節が Safe Mode に当たる早く抜ける if なら、それを含むメソッドの名前</summary>
    public static string? Describe(SyntaxNode node)
    {
        if (
            node is not IfStatementSyntax ifStatement
            || ifStatement.Parent is not BlockSyntax block
        )
        {
            return null;
        }

        if (
            !DeclaresInConditionAndReadsAfter(ifStatement, block)
            && !AssignsInBranchesAndReadsAfter(ifStatement, block)
        )
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

    /// <summary>形 1：本体が跳びで終わり、条件で宣言した変数を if の後ろで読むか</summary>
    private static bool DeclaresInConditionAndReadsAfter(
        IfStatementSyntax ifStatement,
        BlockSyntax block
    )
    {
        if (!EndsWithRemovableJump(ifStatement.Statement))
        {
            return false;
        }

        var operands = OrOperands(ifStatement.Condition);
        var declared = operands
            .Skip(1)
            .SelectMany(OutDeclaredNames)
            .Concat(operands.SelectMany(PatternDeclaredNames))
            .ToHashSet(StringComparer.Ordinal);

        return declared.Count > 0 && ReadsAfter(ifStatement, block, declared);
    }

    /// <summary>
    /// 形 2：if / else if の鎖の最後の else が跳びで終わり、if より前で初期化子なしに宣言した変数を跳びで終わらない枝で代入し、
    /// if の後ろで読むか
    /// </summary>
    private static bool AssignsInBranchesAndReadsAfter(
        IfStatementSyntax ifStatement,
        BlockSyntax block
    )
    {
        var branches = new List<StatementSyntax>();
        var current = ifStatement;

        while (true)
        {
            branches.Add(current.Statement);

            if (current.Else is null)
            {
                return false;
            }

            if (current.Else.Statement is IfStatementSyntax next)
            {
                current = next;
                continue;
            }

            if (!EndsWithRemovableJump(current.Else.Statement))
            {
                return false;
            }

            break;
        }

        var before = block.Statements.TakeWhile(statement => statement != ifStatement).ToList();
        var unassigned = before
            .OfType<LocalDeclarationStatementSyntax>()
            .SelectMany(declaration => declaration.Declaration.Variables)
            .Where(variable => variable.Initializer is null)
            .Select(variable => variable.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);

        unassigned.ExceptWith(before.SelectMany(AssignedNames));

        var assignedInBranches = branches
            .Where(branch => !EndsWithRemovableJump(branch))
            .SelectMany(AssignedNames)
            .Where(unassigned.Contains)
            .ToHashSet(StringComparer.Ordinal);

        return assignedInBranches.Count > 0 && ReadsAfter(ifStatement, block, assignedInBranches);
    }

    /// <summary>名前のどれかを、同じブロックの if より後ろの文で読むか（名前の一致で見る）</summary>
    private static bool ReadsAfter(
        IfStatementSyntax ifStatement,
        BlockSyntax block,
        HashSet<string> names
    ) =>
        block
            .Statements.SkipWhile(statement => statement != ifStatement)
            .Skip(1)
            .SelectMany(statement => statement.DescendantNodes().OfType<IdentifierNameSyntax>())
            .Any(name => names.Contains(name.Identifier.ValueText));

    /// <summary>節の中で代入する変数の名前（単純な代入の左辺と out の引数。ラムダの中は除く）</summary>
    private static IEnumerable<string> AssignedNames(SyntaxNode node)
    {
        var nodes = node.DescendantNodesAndSelf(descendIntoChildren: child =>
            child is not AnonymousFunctionExpressionSyntax
        );

        foreach (var child in nodes)
        {
            if (
                child is AssignmentExpressionSyntax assignment
                && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && assignment.Left is IdentifierNameSyntax left
            )
            {
                yield return left.Identifier.ValueText;
            }
            else if (
                child is ArgumentSyntax argument
                && argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)
                && argument.Expression is IdentifierNameSyntax target
            )
            {
                yield return target.Identifier.ValueText;
            }
        }
    }

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
