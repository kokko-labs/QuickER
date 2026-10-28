using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider;

/// <summary>
/// プロバイダ（<c>QuickER.Provider*</c>）と方言の宣言（<c>QuickER.Dialects</c>）が数値・日時を文字列へ埋めるときは、
/// <c>string.Create(CultureInfo.InvariantCulture, …)</c> を通し、そうでないものは理由つきの一覧にあるものだけであることを固定する
/// </summary>
/// <remarks>
/// <para>
/// この 2 つが組み立てる文字列の多くは DDL・同期スクリプト・型の表記で、数値・日時をそのまま補間すると書式のカルチャが
/// 出る（スウェーデン語の負号 U+2212・タイ語の仏暦）。CA1305 は文字列への素の補間（<c>$"…"</c>）と連結を診断しないので、
/// ここで全数を数える。
/// </para>
/// <para>
/// 拾う基準：<see cref="SourceSymbolScan"/> で組んだコンパイルの意味モデルで、補間の穴（<c>{…}</c>）の式と、文字列の連結
/// （<c>+</c>）の被演算子の型が、数値（整数・<see cref="decimal"/>・<see cref="double"/>・<see cref="float"/>）か日時
/// （<see cref="DateTime"/>・<see cref="DateTimeOffset"/>・<see cref="DateOnly"/>・<see cref="TimeOnly"/>・<see cref="TimeSpan"/>）
/// のもの（<see cref="Nullable{T}"/> も）。<c>string.Create</c> の第 1 引数が <c>CultureInfo.InvariantCulture</c> の補間は数えない。
/// <c>ToString(…, CultureInfo.InvariantCulture)</c> を呼んだ穴は型が文字列なので拾わない。条件コンパイルのどの分岐も読む
/// （<see cref="SourceProject.ScanNodes"/>）。
/// </para>
/// <para>
/// 正当な出現はファイルごとの件数と理由で <see cref="Known"/> に載せる。照合は厳密で、一覧に無い出現も、一覧にあるのに無い出現
/// （件数の違いを含む）も赤にする。
/// </para>
/// </remarks>
public sealed class NumericInterpolationCultureGuardTests
{
    /// <summary>負にならない値（長さ・件数）を、プロセスの外へ出ない照合のキーへ埋める</summary>
    /// <remarks>整数の書式がカルチャで変わるのは負号だけ（.NET は数字を置き換えず、既定の書式は桁区切りを入れない）</remarks>
    private const string NonNegativeKey =
        "負にならない値（長さ・件数）を照合のキーへ埋める（整数の書式がカルチャで変わるのは負号だけで、キーはプロセスの外へ出ない）";

    /// <summary>既知の出現（ファイル・件数・理由）</summary>
    private static readonly (string File, int Count, string Reason)[] Known =
    [
        // 型の表記を解決するための仮の列の名前（T0, T1, …）
        ("src/QuickER.Provider/QueryParameterTypeResolver.cs", 1, NonNegativeKey),
        // 外部キーの署名の 1 部分（長さ:値）
        ("src/QuickER.Provider/SchemaDiffService.cs", 1, NonNegativeKey),
        // 一意制約の列の集合の署名（長さ:名前）
        ("src/QuickER.Provider/UniqueConstraintNaming.cs", 1, NonNegativeKey),
    ];

    [Fact(
        DisplayName = "プロバイダと方言の宣言が数値・日時を文字列へ埋めるのは、InvariantCulture を通すか理由つきの一覧にあるものだけ"
    )]
    public void NumericInterpolation_InProvidersAndDialects_IsOnlyKnown()
    {
        var hits = SourceSymbolScan
            .SrcProjects.Where(project =>
                project.Name.StartsWith("QuickER.Provider", StringComparison.Ordinal)
                || project.Name == "QuickER.Dialects"
            )
            .SelectMany(project =>
                project
                    .ScanNodes(TestContext.Current.CancellationToken)
                    .Where(item => CultureDependentText.IsHit(item.Model, item.Node))
                    .Select(item =>
                        (
                            item.File.RelativePath,
                            Line: item.Node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            Text: item.Node.ToString()
                        )
                    )
            )
            .ToList();

        var found = hits.GroupBy(hit => hit.RelativePath)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        GuardKnownList.AssertMatches(
            found,
            Known.Select(known => (known.File, known.Count)),
            (file, count) => $"{file} x{count}",
            "数値・日時を文字列へ埋めるときは string.Create(CultureInfo.InvariantCulture, …) を通し、例外は理由つきで一覧に載せる",
            describeUnexpected: (file, count) =>
                $"{file} x{count}: "
                + string.Join(
                    " / ",
                    hits.Where(hit => hit.RelativePath == file)
                        .Select(hit => $"{hit.Line} 行 {hit.Text}")
                )
        );
    }

    /// <summary>ガードが拾うべき書き方と、拾ってはいけない書き方</summary>
    private const string ProbeSource = """
        using System;
        using System.Globalization;

        internal static class Probe
        {
            public static string Found(int length, decimal scale, int? precision, DateTime at)
            {
                return $"varchar({length})"
                    + "numeric(10," + scale + ")"
                    + $"time({precision})"
                    + $"-- {at}";
            }

            public static string NotFound(int length, string name)
            {
                return string.Create(CultureInfo.InvariantCulture, $"varchar({length})")
                    + $"{name}({length.ToString(CultureInfo.InvariantCulture)})"
                    + "x" + name;
            }
        }
        """;

    [Fact(
        DisplayName = "走査は補間の穴と文字列の連結の数値・日時を型で拾い、InvariantCulture の string.Create と文字列は拾わない"
    )]
    public void IsHit_FindsCultureDependentHolesAndConcatenations()
    {
        var (compilation, tree) = SourceSymbolScan.CompileProbe(ProbeSource);
        compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot(TestContext.Current.CancellationToken);

        string[] Hits(string method) =>
            root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(declaration => declaration.Identifier.ValueText == method)
                .DescendantNodes()
                .Where(node => CultureDependentText.IsHit(model, node))
                .Select(node => node.ToString())
                .ToArray();

        Hits("Found").Should().Equal("{length}", "scale", "{precision}", "{at}");
        Hits("NotFound").Should().BeEmpty();
    }
}

/// <summary>
/// 書式のカルチャで結果が変わる文字列の組み立て（数値・日時の補間の穴と文字列の連結の被演算子）の判定
/// （<see cref="NumericInterpolationCultureGuardTests"/> が使う）
/// </summary>
internal static class CultureDependentText
{
    /// <summary>日時の型（<see cref="SpecialType"/> を持たないものは完全名で判定する）</summary>
    private static readonly HashSet<string> DateTypeNames = new(StringComparer.Ordinal)
    {
        "System.DateTimeOffset",
        "System.DateOnly",
        "System.TimeOnly",
        "System.TimeSpan",
    };

    /// <summary>
    /// 節が、カルチャで書式の変わる値を文字列へ埋める箇所（数値・日時の型の補間の穴、または文字列の連結の被演算子）か
    /// </summary>
    /// <remarks>補間の穴は <see cref="InterpolationSyntax"/>、連結は被演算子の式の節で拾う</remarks>
    public static bool IsHit(SemanticModel model, SyntaxNode node) =>
        node switch
        {
            InterpolationSyntax interpolation => IsCultureFormatted(model, interpolation.Expression)
                && !IsInvariantCreate(model, interpolation),
            ExpressionSyntax expression
                when expression.Parent is BinaryExpressionSyntax binary
                    && binary.IsKind(SyntaxKind.AddExpression)
                    && IsStringConcatenation(model, binary) => IsCultureFormatted(
                model,
                expression
            ),
            _ => false,
        };

    /// <summary>式の型が数値・日時（<see cref="Nullable{T}"/> を含む）か</summary>
    private static bool IsCultureFormatted(SemanticModel model, ExpressionSyntax expression)
    {
        var type = model.GetTypeInfo(expression).Type;

        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable
        )
        {
            type = nullable.TypeArguments[0];
        }

        return type is not null
            && (
                type.SpecialType
                    is SpecialType.System_Byte
                        or SpecialType.System_SByte
                        or SpecialType.System_Int16
                        or SpecialType.System_UInt16
                        or SpecialType.System_Int32
                        or SpecialType.System_UInt32
                        or SpecialType.System_Int64
                        or SpecialType.System_UInt64
                        or SpecialType.System_Decimal
                        or SpecialType.System_Double
                        or SpecialType.System_Single
                        or SpecialType.System_DateTime
                || DateTypeNames.Contains(type.ToDisplayString())
            );
    }

    /// <summary>加算の演算子が文字列の連結か（どちらかの被演算子が文字列）</summary>
    private static bool IsStringConcatenation(SemanticModel model, BinaryExpressionSyntax binary) =>
        model.GetSymbolInfo(binary).Symbol
            is IMethodSymbol { ContainingType.SpecialType: SpecialType.System_String };

    /// <summary>
    /// 補間の穴を含む補間文字列が、<c>string.Create</c> の第 1 引数に <c>CultureInfo.InvariantCulture</c> を渡した呼び出しの引数か
    /// </summary>
    private static bool IsInvariantCreate(SemanticModel model, InterpolationSyntax interpolation)
    {
        if (
            interpolation.Parent
            is not InterpolatedStringExpressionSyntax
            {
                Parent: ArgumentSyntax
                {
                    Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation },
                },
            }
        )
        {
            return false;
        }

        if (
            SourceSymbolScan.ResolveMethod(model, invocation)
                is not { Name: nameof(string.Create) } method
            || method.ContainingType?.SpecialType != SpecialType.System_String
            || invocation.ArgumentList.Arguments.Count == 0
        )
        {
            return false;
        }

        return model.GetSymbolInfo(invocation.ArgumentList.Arguments[0].Expression).Symbol
                is IPropertySymbol { Name: "InvariantCulture" } property
            && property.ContainingType?.ToDisplayString() == "System.Globalization.CultureInfo";
    }
}
