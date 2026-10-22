using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Mcp;
using QuickER.Model;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp;

/// <summary>
/// ツール引数の読み取りとリレーションの種別の解釈を、正本の外で写していないことを固定する
/// （JSON の引数は <see cref="ToolArguments"/>・種別の名前は <see cref="RelationshipTypeNames.TryParse"/> を通す）
/// </summary>
/// <remarks>
/// <para>
/// 拾う形（シンボルで判定するので、<c>using static</c>・別名・目的型の書き方に依らない）：
/// (a) 第 1 引数が <c>System.Text.Json.JsonElement</c>（NULL 許容を含む）・<c>System.Text.Json.Nodes.JsonObject</c>・
/// <c>JsonNode</c> の <c>GetString</c> / <c>GetBool</c> の宣言（メソッドとローカル関数）で、<c>src/QuickER.Mcp/ToolArguments.cs</c> の外のもの。
/// (b) <c>QuickER.Model.RelationshipType</c> を列挙型とする <c>Enum.Parse</c> / <c>Enum.TryParse</c> / <c>Enum.GetValues</c> の呼び出し
/// （型引数の形と <c>typeof(...)</c> を渡す形）で、<c>src/QuickER.Model/RelationshipType.cs</c> の外のもの
/// （<c>Enum.GetNames</c> は断るときの候補の並びに使うので拾わない）。
/// (c) <c>ParseRelationshipType</c> / <c>HasSameColumnPairs</c> という名前の宣言。前者は別の書式（Mermaid の記号・定義書の表記）を
/// 読むものだけを理由つきの <see cref="Known"/> に載せ、後者は例外なし（列ペアの比較の正本は <see cref="RelationshipColumnPair.HaveSamePairs"/>）。
/// </para>
/// <para>
/// 照合は厳密で、一覧に無い出現も、一覧にあるのに無い出現（件数の違いを含む）も赤にする。
/// メソッドを解決できない <c>Enum</c> の呼び出しは数えないのでなく、「未解決」として同じ一覧で照合する。
/// </para>
/// </remarks>
public sealed class ToolArgumentParsingGuardTests
{
    /// <summary>JSON の引数の読み取りの正本</summary>
    private const string ToolArgumentsFile = "src/QuickER.Mcp/ToolArguments.cs";

    /// <summary>リレーションの種別の名前の解釈の正本</summary>
    private const string RelationshipTypeFile = "src/QuickER.Model/RelationshipType.cs";

    /// <summary>既知の出現（ファイル・出現・件数・理由）</summary>
    private static readonly (string File, string Hit, int Count, string Reason)[] Known =
    [
        (
            "src/QuickER.Gui/Services/MermaidImporter.cs",
            "ParseRelationshipType の宣言",
            1,
            "Mermaid のカーディナリティ記号（||--o{ など）から種別を読む＝列挙子の名前を読むツール引数とは別の書式"
        ),
        (
            "src/QuickER.Gui/Services/TableDefinitionDocumentImporter.cs",
            "ParseRelationshipType の宣言",
            1,
            "Excel 定義書の関係の表記（1:1 / N:1 / N:N）から種別を読む＝列挙子の名前を読むツール引数とは別の書式"
        ),
    ];

    [Fact(
        DisplayName = "ツール引数の読み取りとリレーションの種別の解釈は、正本の外では理由つきの一覧にあるものだけ"
    )]
    public void SharedParsingHelpers_OutsideOwners_AreOnlyKnown()
    {
        var found = SourceSymbolScan
            .SrcProjects.SelectMany(project =>
                project
                    .ScanNodes(TestContext.Current.CancellationToken)
                    .Select(item =>
                        (
                            File: item.File.RelativePath,
                            Hit: ToolArgumentParsingHits.Describe(item.Model, item.Node)
                        )
                    )
                    .Where(hit => hit.Hit is not null)
                    .Select(hit => (hit.File, Hit: hit.Hit!))
            )
            .Where(hit => !IsOwnerOf(hit.File, hit.Hit))
            .GroupBy(hit => hit)
            .ToDictionary(group => group.Key, group => group.Count());

        var expected = Known.ToDictionary(known => (known.File, known.Hit), known => known.Count);

        var unexpected = found
            .Where(pair => !expected.TryGetValue(pair.Key, out var count) || count != pair.Value)
            .Select(pair => $"{pair.Key.File}: {pair.Key.Hit} x{pair.Value}")
            .ToList();
        var stale = expected
            .Where(pair => !found.TryGetValue(pair.Key, out var count) || count != pair.Value)
            .Select(pair => $"{pair.Key.File}: {pair.Key.Hit} x{pair.Value}")
            .ToList();

        string.Join(Environment.NewLine, unexpected)
            .Should()
            .BeEmpty(
                "JSON の引数は ToolArguments・種別の名前は RelationshipTypeNames.TryParse を通し、例外は理由つきで一覧に載せる"
            );
        string.Join(Environment.NewLine, stale).Should().BeEmpty("無くなった出現は一覧から消す");
    }

    [Fact(DisplayName = "正本自身の宣言と呼び出しは拾える（走査が空振りしていない）")]
    public void Owners_ThemselvesAreFound()
    {
        var hits = SourceSymbolScan
            .SrcProjects.Where(project => project.Name is "QuickER.Mcp" or "QuickER.Model")
            .SelectMany(project => project.ScanNodes(TestContext.Current.CancellationToken))
            .Where(item => item.File.RelativePath is ToolArgumentsFile or RelationshipTypeFile)
            .Select(item =>
                ToolArgumentParsingHits.Describe(item.Model, item.Node) is { } hit
                    ? $"{item.File.RelativePath}: {hit}"
                    : null
            )
            .OfType<string>()
            .ToList();

        hits.Should()
            .BeEquivalentTo([
                $"{ToolArgumentsFile}: GetString(JsonElement) の宣言",
                $"{ToolArgumentsFile}: GetBool(JsonElement) の宣言",
                $"{RelationshipTypeFile}: Enum.GetValues<RelationshipType>",
            ]);
    }

    /// <summary>ガードが拾うべき書き方と、拾わない書き方</summary>
    private const string ProbeSource = """
        using System;
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using QuickER.Model;
        using E = System.Enum;

        internal static class Hits
        {
            public static string? GetString(JsonElement element, string name) => null;

            public static bool? GetBool(JsonObject element, string name) => null;

            public static bool? GetBool(JsonElement? element, string name) => null;

            public static string? GetString(this JsonNode node) => null;

            private static RelationshipType ParseRelationshipType(string text) => default;

            private static bool HasSameColumnPairs(object a, object b) => false;

            public static void Calls(string text)
            {
                _ = Enum.Parse<RelationshipType>(text);
                _ = Enum.TryParse(typeof(RelationshipType), text, out _);
                _ = Enum.TryParse<RelationshipType>(text, true, out _);
                _ = Enum.GetValues<RelationshipType>();
                _ = E.Parse(typeof(RelationshipType), text);
                _ = Enum.GetValues(typeof(RelationshipType));

                static string? GetString(JsonElement element) => null;
            }
        }

        internal static class Misses
        {
            public static string? GetString(string text, string name) => null;

            public static bool GetBool(int value) => false;

            public static string? GetStringValue(JsonElement element) => null;

            public static void Calls(string text)
            {
                _ = Enum.GetNames<RelationshipType>();
                _ = Enum.Parse<DayOfWeek>(text);
                _ = Enum.TryParse(typeof(DayOfWeek), text, out _);
                _ = Enum.GetValues<DayOfWeek>();
                _ = Enum.IsDefined(RelationshipType.OneToOne);
            }
        }
        """;

    [Fact(
        DisplayName = "走査はシンボルで判定し、JSON の第 1 引数の宣言・種別の列挙型の Parse / TryParse / GetValues・写しの名前を拾う"
    )]
    public void Describe_FindsEveryForm()
    {
        var (compilation, tree) = SourceSymbolScan.CompileProbe(ProbeSource);
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot(TestContext.Current.CancellationToken);

        compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();

        string[] HitsIn(string type) =>
            root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(declaration => declaration.Identifier.ValueText == type)
                .DescendantNodes()
                .Select(node => ToolArgumentParsingHits.Describe(model, node))
                .OfType<string>()
                .ToArray();

        HitsIn("Hits")
            .Should()
            .BeEquivalentTo([
                "GetString(JsonElement) の宣言",
                "GetBool(JsonObject) の宣言",
                "GetBool(JsonElement) の宣言",
                "GetString(JsonNode) の宣言",
                "ParseRelationshipType の宣言",
                "HasSameColumnPairs の宣言",
                "Enum.Parse<RelationshipType>",
                "Enum.TryParse<RelationshipType>",
                "Enum.TryParse<RelationshipType>",
                "Enum.GetValues<RelationshipType>",
                "Enum.Parse<RelationshipType>",
                "Enum.GetValues<RelationshipType>",
                "GetString(JsonElement) の宣言",
            ]);
        HitsIn("Misses").Should().BeEmpty();
    }

    /// <summary>出現が、その正本のファイルの中の正本自身の書き方か</summary>
    private static bool IsOwnerOf(string file, string hit) =>
        (
            file == ToolArgumentsFile
            && hit.EndsWith(" の宣言", StringComparison.Ordinal)
            && hit.StartsWith("Get", StringComparison.Ordinal)
        ) || (file == RelationshipTypeFile && hit.StartsWith("Enum.", StringComparison.Ordinal));
}

/// <summary>
/// ツール引数の読み取り・リレーションの種別の解釈の写しの判定（<see cref="ToolArgumentParsingGuardTests"/> の走査）
/// </summary>
internal static class ToolArgumentParsingHits
{
    /// <summary>JSON の引数を読む写しの名前</summary>
    private static readonly HashSet<string> JsonReaderNames = new(StringComparer.Ordinal)
    {
        "GetString",
        "GetBool",
    };

    /// <summary>種別の解釈・列ペアの比較の写しの名前</summary>
    private static readonly HashSet<string> CopiedHelperNames = new(StringComparer.Ordinal)
    {
        "ParseRelationshipType",
        "HasSameColumnPairs",
    };

    /// <summary>種別の名前の解釈に使えてしまう <see cref="Enum"/> のメソッド</summary>
    private static readonly HashSet<string> EnumMethods = new(StringComparer.Ordinal)
    {
        nameof(Enum.Parse),
        nameof(Enum.TryParse),
        nameof(Enum.GetValues),
    };

    /// <summary>JSON の引数の型（名前空間と型名）</summary>
    private static readonly HashSet<(string Namespace, string Name)> JsonArgumentTypes =
    [
        ("System.Text.Json", "JsonElement"),
        ("System.Text.Json.Nodes", "JsonObject"),
        ("System.Text.Json.Nodes", "JsonNode"),
    ];

    private const string RelationshipTypeName = "QuickER.Model.RelationshipType";

    /// <summary>節が拾う形ならその説明（拾わなければ null）</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node) =>
        node switch
        {
            MethodDeclarationSyntax or LocalFunctionStatementSyntax => DescribeDeclaration(
                model.GetDeclaredSymbol(node) as IMethodSymbol
            ),
            InvocationExpressionSyntax invocation => DescribeEnumCall(model, invocation),
            _ => null,
        };

    private static string? DescribeDeclaration(IMethodSymbol? method)
    {
        if (method is null)
        {
            return null;
        }

        if (CopiedHelperNames.Contains(method.Name))
        {
            return $"{method.Name} の宣言";
        }

        if (
            JsonReaderNames.Contains(method.Name)
            && method.Parameters.Length > 0
            && JsonArgumentTypeName(method.Parameters[0].Type) is { } typeName
        )
        {
            return $"{method.Name}({typeName}) の宣言";
        }

        return null;
    }

    /// <summary>JSON の引数の型ならその型名（<c>Nullable&lt;T&gt;</c> は中身で見る。解決できない型は名前で見る）</summary>
    private static string? JsonArgumentTypeName(ITypeSymbol type)
    {
        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                TypeArguments: [var inner],
            }
        )
        {
            type = inner;
        }

        if (type.TypeKind == TypeKind.Error)
        {
            return JsonArgumentTypes.Any(known => known.Name == type.Name)
                ? $"未解決 {type.Name}"
                : null;
        }

        return JsonArgumentTypes.Contains(
            (type.ContainingNamespace?.ToDisplayString() ?? "", type.Name)
        )
            ? type.Name
            : null;
    }

    private static string? DescribeEnumCall(
        SemanticModel model,
        InvocationExpressionSyntax invocation
    )
    {
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            _ => null,
        };

        if (name is null || !EnumMethods.Contains(name))
        {
            return null;
        }

        var method = SourceSymbolScan.ResolveMethod(model, invocation);

        if (method is null)
        {
            return invocation.ToString().Contains("RelationshipType", StringComparison.Ordinal)
                ? $"未解決 Enum.{name}"
                : null;
        }

        if (method.ContainingType?.SpecialType != SpecialType.System_Enum)
        {
            return null;
        }

        var targetsRelationshipType = method.IsGenericMethod
            ? method.TypeArguments is [var argument] && IsRelationshipType(argument)
            : invocation.ArgumentList.Arguments
                is [{ Expression: TypeOfExpressionSyntax typeOf }, ..]
                && model.GetTypeInfo(typeOf.Type).Type is { } target
                && IsRelationshipType(target);

        return targetsRelationshipType ? $"Enum.{method.Name}<RelationshipType>" : null;
    }

    private static bool IsRelationshipType(ITypeSymbol type) =>
        type.ToDisplayString() == RelationshipTypeName;
}
