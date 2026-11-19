using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 同期の計画の文の型（<c>SyncStep</c> の派生）の宣言と、文の型で振り分ける所の型の並びの読み取り
/// （照合の行 <c>sync-step-switches-cover-every-step-type</c> の正本と写し）
/// </summary>
/// <remarks>
/// <para>
/// 文の型の宣言は、<c>SyncStep</c> を基底の並びの先頭に書いた record・class の宣言の名前（主コンストラクターの引数つきの基底を含む）。
/// <c>SyncStep</c> 自身と、ほかの型を基底にした宣言は拾わない。
/// </para>
/// <para>
/// 振り分ける所は、名前で指したメソッドの中の switch の腕（switch 式の腕・switch 文の <c>case</c> のパターンと定数）の、いちばん外側の
/// パターンの型の名前（修飾を外した最後の部分）。<c>_</c> の腕と <c>default</c> は数えない。プロパティのパターンの中の型も数えない
/// （腕が振り分ける型だけを数える）。
/// </para>
/// <para>
/// 条件コンパイルのどの分岐も読む（全組み合わせの構文木の節で、同じ節は 1 回だけ数える）。
/// </para>
/// <para>
/// 読みの限界：振り分けを別のメソッドへ分けると、そのメソッドの腕は数えない（指したメソッドの中だけを読む）。is の連鎖で振り分ける形も
/// 拾わない。
/// </para>
/// </remarks>
internal static class SyncStepSwitches
{
    /// <summary>文の型で振り分ける所 1 つ</summary>
    /// <param name="Label">報告に出す名前</param>
    /// <param name="RelativePath">リポジトリの根からの相対パス（区切りは <c>/</c>）</param>
    /// <param name="Method">振り分けを持つメソッドの名前</param>
    internal sealed record Site(string Label, string RelativePath, string Method);

    /// <summary>文の型の宣言の名前（宣言ごとに 1 つ・序数の順）</summary>
    /// <param name="sources">ソースの本文（文の型を宣言するファイル）</param>
    public static IReadOnlyList<string> DeclaredSteps(IEnumerable<string> sources) =>
        [
            .. sources
                .SelectMany(Nodes)
                .OfType<TypeDeclarationSyntax>()
                .Where(type => BaseName(type) == "SyncStep")
                .Select(type => type.Identifier.ValueText)
                .Order(StringComparer.Ordinal),
        ];

    /// <summary>振り分ける所のメソッドの腕の型の名前（腕ごとに 1 つ・序数の順）</summary>
    /// <param name="source">ソースの本文（無ければ空として読む＝足りない型を名指しさせる）</param>
    /// <param name="method">振り分けを持つメソッドの名前</param>
    public static IReadOnlyList<string> SwitchedSteps(string? source, string method) =>
        source is null
            ? []
            :
            [
                .. Nodes(source)
                    .Where(node => InMethod(node, method))
                    .Select(ArmType)
                    .OfType<string>()
                    .Order(StringComparer.Ordinal),
            ];

    /// <summary>
    /// 文の型の、名前を運びうる型の位置指定の引数（<c>{文の型}.{引数}</c>・序数の順。照合の行 <c>sync-step-name-members-declared</c> の正本）
    /// </summary>
    /// <remarks>
    /// 名前を運ばない型（<see cref="NotNameTypes"/>）の引数は数えない。名前を運びうる型（<see cref="NameTypes"/>）の引数はそのまま、どちらでもない型の
    /// 引数は型を添えて数える（どの写しにも合わないので、型を足したら名前を運ぶかを決めるまで赤になる）。型は空白を外した綴りで比べる。
    /// </remarks>
    /// <param name="sources">ソースの本文（文の型を宣言するファイル）</param>
    public static IReadOnlyList<string> NameMembers(IEnumerable<string> sources) =>
        [
            .. sources
                .SelectMany(Nodes)
                .OfType<TypeDeclarationSyntax>()
                .Where(type => BaseName(type) == "SyncStep")
                .SelectMany(type =>
                    (type.ParameterList?.Parameters ?? [])
                        .Select(parameter => NameMember(type, parameter))
                        .OfType<string>()
                )
                .Order(StringComparer.Ordinal),
        ];

    /// <summary>名前を運びうる型（空白を外した綴り）</summary>
    private static readonly HashSet<string> NameTypes = new(StringComparer.Ordinal)
    {
        "string",
        "string?",
        "IReadOnlyList<string>",
        "Column",
        "Column?",
        "Entity",
        "IReadOnlyList<ForeignKeyColumnNamePair>",
        "TableRebuildPlan",
        "IReadOnlyDictionary<UniqueConstraint,string>?",
    };

    /// <summary>名前を運ばない型（文の由来と参照アクション）</summary>
    private static readonly HashSet<string> NotNameTypes = new(StringComparer.Ordinal)
    {
        "StepOrigin",
        "ForeignKeyReferentialAction",
    };

    /// <summary>位置指定の引数 1 つの表記（名前を運ばない型は null）</summary>
    private static string? NameMember(TypeDeclarationSyntax type, ParameterSyntax parameter)
    {
        var typeText = string.Concat(
            (parameter.Type?.ToString() ?? "?").Where(c => !char.IsWhiteSpace(c))
        );
        var member = $"{type.Identifier.ValueText}.{parameter.Identifier.ValueText}";

        if (NotNameTypes.Contains(typeText))
        {
            return null;
        }

        return NameTypes.Contains(typeText)
            ? member
            : $"{member}（名前を運ぶかを決めていない型 {typeText}）";
    }

    /// <summary>ソースの本文の、条件コンパイルのどの分岐も読んだ節</summary>
    private static IEnumerable<SyntaxNode> Nodes(string source) =>
        ConditionalSourceParse.DescendantNodes(ConditionalSourceParse.Parse(source));

    /// <summary>型の宣言の基底の並びの先頭の型の名前（無ければ null）</summary>
    private static string? BaseName(TypeDeclarationSyntax type) =>
        type.BaseList?.Types.FirstOrDefault()?.Type is { } baseType ? LastName(baseType) : null;

    /// <summary>節が名前で指したメソッドの中にあるか</summary>
    private static bool InMethod(SyntaxNode node, string method) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText
        == method;

    /// <summary>switch の腕が振り分ける型の名前（腕でない節・<c>_</c>・<c>default</c> は null）</summary>
    private static string? ArmType(SyntaxNode node) =>
        node switch
        {
            SwitchExpressionArmSyntax arm => PatternType(arm.Pattern),
            CasePatternSwitchLabelSyntax label => PatternType(label.Pattern),
            CaseSwitchLabelSyntax label => ExpressionName(label.Value),
            _ => null,
        };

    /// <summary>いちばん外側のパターンの型の名前</summary>
    private static string? PatternType(PatternSyntax pattern) =>
        pattern switch
        {
            TypePatternSyntax type => LastName(type.Type),
            DeclarationPatternSyntax declaration => LastName(declaration.Type),
            RecursivePatternSyntax { Type: { } type } => LastName(type),
            ConstantPatternSyntax constant => ExpressionName(constant.Expression),
            _ => null,
        };

    /// <summary>定数の位置に書いた型の名前（名前でない式は null）</summary>
    private static string? ExpressionName(ExpressionSyntax expression) =>
        expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => null,
        };

    /// <summary>型の構文の、修飾を外した最後の部分</summary>
    private static string LastName(TypeSyntax type) =>
        type switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            _ => type.ToString(),
        };
}
