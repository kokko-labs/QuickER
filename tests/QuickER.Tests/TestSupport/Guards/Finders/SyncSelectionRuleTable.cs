using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 同期の計画の選んだ項目を外す規則の表と、規則の形の関数の宣言の読み取り（照合の行 <c>sync-selection-rules-table-complete</c> の
/// 正本と写し）
/// </summary>
/// <remarks>
/// <para>
/// 規則は partial クラス <c>SelectionRules</c> に 1 規則 1 ファイルで置き、表は別のファイルにあるので、宣言と表はファイルをまたいで
/// 突き合わせる（1 ファイルずつ読む見つける形では比べられない）。
/// </para>
/// <para>
/// 規則の形の関数は、クラス <c>SelectionRules</c> の中の、名前が <c>Exclude</c> に大文字が続き、戻り値の型が
/// <c>IReadOnlyList&lt;Withdrawal&gt;</c>、引数の型が <c>SelectionState</c>・<c>SelectionContext</c> の順の 2 つのメソッドの宣言
/// （<c>SelectionRule</c> のデリゲートと同じ形）。型は構文の綴り（空白を除く）で比べる。
/// </para>
/// <para>
/// 規則の表は、<c>SelectionRules</c> のフィールド <c>BeforeRounds</c>・<c>EachRound</c> の初期化子の要素（コレクション式・配列の
/// 初期化子）で、2 つの表の和で数える。メソッドの名前でない要素は、要素の綴りのまま数える（規則の形の関数の名前と一致しない）。
/// </para>
/// <para>
/// 条件コンパイルのどの分岐も読む（全組み合わせの構文木の節で、同じ節は 1 回だけ数える）。
/// </para>
/// <para>
/// 読みの限界：同じ表を条件コンパイルの分岐ごとに違う中身で書くと、両方の分岐の要素を合わせて数える（表に 2 回と読む）。
/// 規則の形は綴りで比べるので、別名の <c>using</c> や完全修飾で書いた型は規則の形と読まない。
/// </para>
/// </remarks>
internal static class SyncSelectionRuleTable
{
    private const string Rules = "SelectionRules";
    private const string Prefix = "Exclude";

    private static readonly HashSet<string> RuleTables = new(StringComparer.Ordinal)
    {
        "BeforeRounds",
        "EachRound",
    };

    /// <summary>規則の形の関数の宣言の名前（宣言ごとに 1 つ・序数の順）</summary>
    /// <param name="sources">ソースの本文（<c>SelectionRules</c> の partial のファイルを含む並び）</param>
    public static IReadOnlyList<string> DeclaredRules(IEnumerable<string> sources) =>
        [
            .. sources
                .SelectMany(Nodes)
                .OfType<MethodDeclarationSyntax>()
                .Where(method => InRules(method) && IsRuleShaped(method))
                .Select(method => method.Identifier.ValueText)
                .Order(StringComparer.Ordinal),
        ];

    /// <summary>規則の表（2 つの表の和）の要素（要素ごとに 1 つ・序数の順）</summary>
    /// <param name="sources">ソースの本文（<c>SelectionRules</c> の表のファイルを含む並び）</param>
    public static IReadOnlyList<string> ListedRules(IEnumerable<string> sources) =>
        [
            .. sources
                .SelectMany(Nodes)
                .OfType<VariableDeclaratorSyntax>()
                .Where(declarator =>
                    declarator.Parent?.Parent is FieldDeclarationSyntax
                    && InRules(declarator)
                    && RuleTables.Contains(declarator.Identifier.ValueText)
                )
                .SelectMany(Elements)
                .Select(element =>
                    element is IdentifierNameSyntax identifier
                        ? identifier.Identifier.ValueText
                        : element.ToString()
                )
                .Order(StringComparer.Ordinal),
        ];

    /// <summary>ソースの本文の、条件コンパイルのどの分岐も読んだ節</summary>
    private static IEnumerable<SyntaxNode> Nodes(string source) =>
        ConditionalSourceParse.DescendantNodes(ConditionalSourceParse.Parse(source));

    /// <summary>表のフィールドの初期化子の要素</summary>
    private static IEnumerable<ExpressionSyntax> Elements(VariableDeclaratorSyntax declarator) =>
        declarator.Initializer?.Value switch
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

    /// <summary>節が <c>SelectionRules</c> の中にあるか</summary>
    private static bool InRules(SyntaxNode node) =>
        node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText
        == Rules;

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
            && Spelling(parameters[0].Type) == "SelectionState"
            && Spelling(parameters[1].Type) == "SelectionContext";
    }

    /// <summary>型の構文の綴り（空白を除く）</summary>
    private static string Spelling(TypeSyntax? type) =>
        type is null
            ? string.Empty
            : string.Concat(type.ToString().Where(c => !char.IsWhiteSpace(c)));
}
