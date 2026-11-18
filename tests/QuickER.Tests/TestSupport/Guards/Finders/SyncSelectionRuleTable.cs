using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 同期の計画の、同じ形の関数を並べた表と、その形の関数の宣言の読み取り（照合の行 <c>sync-selection-rules-table-complete</c>・
/// <c>sync-rebuild-declarations-table-complete</c> の正本と写し）
/// </summary>
/// <remarks>
/// <para>
/// 関数は partial クラスに 1 つ 1 ファイルで置き、表は別のファイルにあるので、宣言と表はファイルをまたいで突き合わせる
/// （1 ファイルずつ読む見つける形では比べられない）。読む形（<see cref="Shape"/>）は 2 つ。
/// </para>
/// <list type="bullet">
/// <item><see cref="Selection"/>：選んだ項目を外す規則。クラス <c>SelectionRules</c> の中の、名前が <c>Exclude</c> に大文字が続き、
/// 戻り値の型が <c>IReadOnlyList&lt;Withdrawal&gt;</c>、引数の型が <c>SelectionState</c>・<c>SelectionContext</c> の順の 2 つの
/// メソッドの宣言（<c>SelectionRule</c> のデリゲートと同じ形）。表はフィールド <c>BeforeRounds</c>・<c>EachRound</c>。</item>
/// <item><see cref="Rebuild"/>：外し直しの宣言。クラス <c>RebuildDeclarations</c> の中の、名前が <c>Require</c> に大文字が続き、
/// 戻り値の型が <c>IEnumerable&lt;LiveConstraint&gt;</c>、引数の型が <c>SchedulingInput</c> の 1 つのメソッドの宣言
/// （<c>RebuildDeclaration</c> のデリゲートと同じ形）。表はフィールド <c>All</c>。</item>
/// </list>
/// <para>
/// 型は構文の綴り（空白を除く）で比べる。表は、フィールドの初期化子の要素（コレクション式・配列の初期化子）で、形の表の和で数える。
/// メソッドの名前でない要素は、要素の綴りのまま数える（形の関数の名前と一致しない）。
/// </para>
/// <para>
/// 条件コンパイルのどの分岐も読む（全組み合わせの構文木の節で、同じ節は 1 回だけ数える）。
/// </para>
/// <para>
/// 読みの限界：同じ表を条件コンパイルの分岐ごとに違う中身で書くと、両方の分岐の要素を合わせて数える（表に 2 回と読む）。
/// 形は綴りで比べるので、別名の <c>using</c> や完全修飾で書いた型は形と読まない。
/// </para>
/// </remarks>
internal static class SyncSelectionRuleTable
{
    /// <summary>読む形</summary>
    /// <param name="Class">関数と表を持つクラスの名前</param>
    /// <param name="Prefix">関数の名前の前置き（大文字が続く）</param>
    /// <param name="ReturnType">戻り値の型の綴り（空白を除く）</param>
    /// <param name="ParameterTypes">引数の型の綴り（順に）</param>
    /// <param name="Tables">表のフィールドの名前</param>
    internal sealed record Shape(
        string Class,
        string Prefix,
        string ReturnType,
        IReadOnlyList<string> ParameterTypes,
        IReadOnlyList<string> Tables
    );

    /// <summary>選んだ項目を外す規則の形</summary>
    public static readonly Shape Selection = new(
        "SelectionRules",
        "Exclude",
        "IReadOnlyList<Withdrawal>",
        ["SelectionState", "SelectionContext"],
        ["BeforeRounds", "EachRound"]
    );

    /// <summary>外し直しの宣言の形</summary>
    public static readonly Shape Rebuild = new(
        "RebuildDeclarations",
        "Require",
        "IEnumerable<LiveConstraint>",
        ["SchedulingInput"],
        ["All"]
    );

    /// <summary>形の関数の宣言の名前（宣言ごとに 1 つ・序数の順）</summary>
    /// <param name="sources">ソースの本文（形のクラスの partial のファイルを含む並び）</param>
    /// <param name="shape">読む形（省くと選んだ項目を外す規則）</param>
    public static IReadOnlyList<string> DeclaredRules(
        IEnumerable<string> sources,
        Shape? shape = null
    )
    {
        var read = shape ?? Selection;

        return
        [
            .. sources
                .SelectMany(Nodes)
                .OfType<MethodDeclarationSyntax>()
                .Where(method => InClass(method, read) && IsShaped(method, read))
                .Select(method => method.Identifier.ValueText)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>表（形の表の和）の要素（要素ごとに 1 つ・序数の順）</summary>
    /// <param name="sources">ソースの本文（形のクラスの表のファイルを含む並び）</param>
    /// <param name="shape">読む形（省くと選んだ項目を外す規則）</param>
    public static IReadOnlyList<string> ListedRules(
        IEnumerable<string> sources,
        Shape? shape = null
    )
    {
        var read = shape ?? Selection;

        return
        [
            .. sources
                .SelectMany(Nodes)
                .OfType<VariableDeclaratorSyntax>()
                .Where(declarator =>
                    declarator.Parent?.Parent is FieldDeclarationSyntax
                    && InClass(declarator, read)
                    && read.Tables.Contains(declarator.Identifier.ValueText, StringComparer.Ordinal)
                )
                .SelectMany(Elements)
                .Select(element =>
                    element is IdentifierNameSyntax identifier
                        ? identifier.Identifier.ValueText
                        : element.ToString()
                )
                .Order(StringComparer.Ordinal),
        ];
    }

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

    /// <summary>節が形のクラスの中にあるか</summary>
    private static bool InClass(SyntaxNode node, Shape shape) =>
        node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText
        == shape.Class;

    /// <summary>メソッドが形（表の要素のデリゲートと同じ引数と戻り値）の関数か</summary>
    private static bool IsShaped(MethodDeclarationSyntax method, Shape shape)
    {
        var name = method.Identifier.ValueText;
        var parameters = method.ParameterList.Parameters;

        return name.Length > shape.Prefix.Length
            && name.StartsWith(shape.Prefix, StringComparison.Ordinal)
            && char.IsUpper(name[shape.Prefix.Length])
            && Spelling(method.ReturnType) == shape.ReturnType
            && parameters
                .Select(p => Spelling(p.Type))
                .SequenceEqual(shape.ParameterTypes, StringComparer.Ordinal);
    }

    /// <summary>型の構文の綴り（空白を除く）</summary>
    private static string Spelling(TypeSyntax? type) =>
        type is null
            ? string.Empty
            : string.Concat(type.ToString().Where(c => !char.IsWhiteSpace(c)));
}
