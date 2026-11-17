using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 同期の計画の除外の規則の表と、規則の形の関数の宣言との突き合わせ（規則 <c>sync-selection-rules-table-complete</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 規則の形の関数は、クラス <c>SyncPlanner</c> の中の、名前が <c>Exclude</c> に大文字が続き、戻り値の型が
/// <c>IReadOnlyList&lt;Withdrawal&gt;</c>、引数の型が <c>IReadOnlyList&lt;SchemaDiffItem&gt;</c>・<c>SelectionContext</c> の順の 2 つのメソッドの宣言
/// （<c>SelectionRule</c> のデリゲートと同じ形）。型は構文の綴り（空白を除く）で比べる。
/// </para>
/// <para>
/// 規則の表は、<c>SyncPlanner</c> のフィールド <c>SelectionRulesBeforeRounds</c>・<c>SelectionRulesEachRound</c> の初期化子の要素
/// （コレクション式・配列の初期化子）で、2 つの表の和で数える。
/// </para>
/// <para>
/// 拾う何か：表に無い規則（<c>missing from the rule tables: 名前</c>）・表に 2 回以上ある規則（<c>listed n times: 名前</c>）・
/// 規則の形でない関数の要素（<c>not rule-shaped: 名前</c>）・<c>SyncPlanner</c> に宣言の無い名前やメソッドグループでない要素
/// （<c>not declared in SyncPlanner: 要素</c>）。条件コンパイルのどの分岐も読む（全組み合わせの構文木の節で、同じ節は 1 回だけ数える）。
/// </para>
/// <para>
/// 読みの限界：同じ表を条件コンパイルの分岐ごとに違う中身で書くと、両方の分岐の要素を合わせて数える（表に 2 回と読む）。
/// 規則の形は綴りで比べるので、別名の <c>using</c> や完全修飾で書いた型は規則の形と読まない。
/// </para>
/// </remarks>
internal static class SyncSelectionRuleTable
{
    private const string Planner = "SyncPlanner";
    private const string Prefix = "Exclude";

    /// <summary>規則の表のフィールドの名前</summary>
    private static readonly HashSet<string> RuleTables = new(StringComparer.Ordinal)
    {
        "SelectionRulesBeforeRounds",
        "SelectionRulesEachRound",
    };

    /// <summary>ファイルから、規則の表と規則の形の関数の食い違いを拾う</summary>
    public static IEnumerable<GuardFinding> Find(GuardFile file)
    {
        var methods = file.Nodes.OfType<MethodDeclarationSyntax>().Where(InPlanner).ToList();
        var ruleShaped = methods
            .Where(IsRuleShaped)
            .GroupBy(m => m.Identifier.ValueText, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var declared = methods
            .Select(m => m.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        var entries = TableEntries(file).ToList();
        var listed = entries
            .Select(e => e.Name)
            .OfType<string>()
            .GroupBy(n => n, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (var (name, declaration) in ruleShaped.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var count = listed.GetValueOrDefault(name);

            if (count == 0)
            {
                yield return new GuardFinding(
                    $"missing from the rule tables: {name}",
                    GuardFile.LineOf(declaration)
                );
            }

            if (count >= 2)
            {
                yield return new GuardFinding(
                    $"listed {count} times: {name}",
                    GuardFile.LineOf(declaration)
                );
            }
        }

        foreach (var entry in entries)
        {
            if (entry.Name is not null && ruleShaped.ContainsKey(entry.Name))
            {
                continue;
            }

            yield return entry.Name is not null && declared.Contains(entry.Name)
                ? new GuardFinding($"not rule-shaped: {entry.Name}", GuardFile.LineOf(entry.Node))
                : new GuardFinding(
                    $"not declared in SyncPlanner: {entry.Node}",
                    GuardFile.LineOf(entry.Node)
                );
        }
    }

    /// <summary>規則の表の要素（候補の数の下限に使う）</summary>
    public static IEnumerable<GuardFinding> FindTableEntries(GuardFile file) =>
        TableEntries(file)
            .Select(e => new GuardFinding($"entry: {e.Node}", GuardFile.LineOf(e.Node)));

    /// <summary>規則の表の初期化子の要素と、要素がメソッドグループの名前ならその名前</summary>
    private static IEnumerable<(ExpressionSyntax Node, string? Name)> TableEntries(GuardFile file)
    {
        foreach (var declarator in file.Nodes.OfType<VariableDeclaratorSyntax>())
        {
            if (
                declarator.Parent?.Parent is not FieldDeclarationSyntax
                || !InPlanner(declarator)
                || !RuleTables.Contains(declarator.Identifier.ValueText)
            )
            {
                continue;
            }

            var value = declarator.Initializer?.Value;
            IEnumerable<ExpressionSyntax> elements = value switch
            {
                CollectionExpressionSyntax collection => collection
                    .Elements.OfType<ExpressionElementSyntax>()
                    .Select(e => e.Expression),
                ArrayCreationExpressionSyntax array => array.Initializer?.Expressions ?? [],
                ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray
                    .Initializer
                    .Expressions,
                InitializerExpressionSyntax initializer => initializer.Expressions,
                _ => [],
            };

            foreach (var element in elements)
            {
                yield return (
                    element,
                    element is IdentifierNameSyntax identifier
                        ? identifier.Identifier.ValueText
                        : null
                );
            }
        }
    }

    /// <summary>節が <c>SyncPlanner</c> の中にあるか</summary>
    private static bool InPlanner(SyntaxNode node) =>
        node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText
        == Planner;

    /// <summary>メソッドが規則の形（<c>SelectionRule</c> と同じ引数と戻り値）の除外の関数か</summary>
    private static bool IsRuleShaped(MethodDeclarationSyntax method)
    {
        var name = method.Identifier.ValueText;
        var parameters = method.ParameterList.Parameters;

        return name.Length > Prefix.Length
            && name.StartsWith(Prefix, StringComparison.Ordinal)
            && char.IsUpper(name[Prefix.Length])
            && Spelling(method.ReturnType) == "IReadOnlyList<Withdrawal>"
            && parameters.Count == 2
            && Spelling(parameters[0].Type) == "IReadOnlyList<SchemaDiffItem>"
            && Spelling(parameters[1].Type) == "SelectionContext";
    }

    /// <summary>型の構文の綴り（空白を除く）</summary>
    private static string Spelling(TypeSyntax? type) =>
        type is null
            ? string.Empty
            : string.Concat(type.ToString().Where(c => !char.IsWhiteSpace(c)));
}
