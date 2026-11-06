using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の表：MCP とツール・AI のチャットエンジン</summary>
internal static partial class SourceRules
{
    /// <summary>JSON の引数の読み取りの正本</summary>
    private const string ToolArgumentsCode = "src/QuickER.Mcp/ToolArguments.cs";

    /// <summary>リレーションの種別の名前の解釈の正本</summary>
    private const string RelationshipTypeCode = "src/QuickER.Model/RelationshipType.cs";

    /// <summary>図の読み込みの「新しい版で保存された」の警告文（ER 図操作ツールとコード生成ツールで共有）</summary>
    private const string NewerFormatWarningText =
        "Warning: this diagram was saved in a newer format";

    /// <summary>ツール実行と活動通知を共有のヘルパーへ寄せたチャットエンジン 4 種</summary>
    private static readonly string[] ChatEngineCodes =
    [
        "src/QuickER.AI/ChatTurnEngine.cs",
        "src/QuickER.AI/ClaudeCodeChatEngine.cs",
        "src/QuickER.AI/CodexChatEngine.cs",
        "src/QuickER.AI/CopilotChatEngine.cs",
    ];

    /// <summary>JSON の引数を読む写しの名前</summary>
    private static readonly HashSet<string> JsonArgumentReaderNames = new(StringComparer.Ordinal)
    {
        "GetString",
        "GetBool",
    };

    /// <summary>種別の解釈・列ペアの比較の写しの名前</summary>
    private static readonly HashSet<string> CopiedToolHelperNames = new(StringComparer.Ordinal)
    {
        "ParseRelationshipType",
        "HasSameColumnPairs",
    };

    /// <summary>種別の名前の解釈に使えてしまう <see cref="Enum"/> のメソッド</summary>
    private static readonly HashSet<string> RelationshipTypeEnumMethods = new(
        StringComparer.Ordinal
    )
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

    private static readonly (string What, Regex Pattern)[] ChatToolInvocationForms =
    [
        FixedText("ChatToolInvocation.Execute("),
        FixedText("ChatToolInvocation.Notify("),
    ];

    private static readonly (string What, Regex Pattern)[] ChatToolInvocationCopyForms =
    [
        FixedText("threw an exception:"),
        FixedText("ToolActivityReceived?.Invoke("),
    ];

    private static IEnumerable<SourceRule> Mcp() =>
        [
            // ツール引数の読み取りとリレーションの種別の解釈を、正本の外で写さない（JSON の引数は ToolArguments・種別の名前は
            // RelationshipTypeNames.TryParse・列ペアの比較は RelationshipColumnPair.HaveSamePairs）。シンボルで判定するので
            // using static・別名・目的型の書き方に依らない。拾う形：(a) 第 1 引数が JsonElement（NULL 許容を含む）・JsonObject・
            // JsonNode の GetString / GetBool の宣言（メソッドとローカル関数）、(b) RelationshipType を列挙型とする Enum.Parse /
            // TryParse / GetValues の呼び出し（型引数の形と typeof を渡す形。Enum.GetNames は断るときの候補の並びに使うので拾わない。
            // メソッドを解決できない呼び出しは「未解決」として数える）、(c) ParseRelationshipType / HasSameColumnPairs の宣言。
            // 正本のファイルの中の正本自身の書き方（ToolArguments.cs の Get… の宣言・RelationshipType.cs の Enum.…）は拾わない
            SourceRule.Forbid(
                "tool-argument-parsing-copies",
                "JSON の引数は ToolArguments・種別の名前は RelationshipTypeNames.TryParse を通し、例外は理由つきで一覧に載せる",
                Src,
                GuardFinder.Symbols(
                    (model, node) =>
                        DescribeToolArgumentParsing(model, node) is { } what
                        && !IsToolArgumentParsingOwner(node, what)
                            ? what
                            : null
                ),
                new(
                    "src/QuickER.Gui/Services/MermaidImporter.cs",
                    "ParseRelationshipType の宣言",
                    1,
                    "Mermaid のカーディナリティ記号（||--o{ など）から種別を読む＝列挙子の名前を読むツール引数とは別の書式"
                ),
                new(
                    "src/QuickER.Gui/Services/TableDefinitionDocumentImporter.cs",
                    "ParseRelationshipType の宣言",
                    1,
                    "Excel 定義書の関係の表記（1:1 / N:1 / N:N）から種別を読む＝列挙子の名前を読むツール引数とは別の書式"
                )
            ),
            // 正本自身の宣言と呼び出しは拾える（上の規則の走査が空振りしていない・所有の判定が正本を見失っていない）
            SourceRule.Require(
                "tool-argument-parsing-owners",
                "正本自身の宣言と呼び出しは拾える（走査が空振りしていない）",
                SourceScope.Files("src", ToolArgumentsCode, RelationshipTypeCode),
                GuardFinder.Symbols(DescribeToolArgumentParsing),
                new(
                    ToolArgumentsCode,
                    "GetString(JsonElement) の宣言",
                    1,
                    "JSON の引数の文字列の読み取りの正本"
                ),
                new(
                    ToolArgumentsCode,
                    "GetBool(JsonElement) の宣言",
                    1,
                    "JSON の引数の真偽値の読み取りの正本"
                ),
                new(
                    RelationshipTypeCode,
                    "Enum.GetValues<RelationshipType>",
                    1,
                    "種別の名前の解釈の正本（列挙子の名前を読む）"
                )
            ) with
            {
                MinimumFiles = 2,
            },
            // 図を読むツールの「新しい版で保存された」の警告文は、src に 1 か所だけ書く（文言を直すとき 1 か所で済み、ツールどうしで
            // 食い違わない）。文字列で読むので、コメント・条件コンパイルの外れる分岐の中の表記も数える
            SourceRule.Require(
                "newer-format-warning-text",
                "警告文は 1 か所に置き、ER 図操作ツールとコード生成ツールで共有する",
                Src,
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindForms(file, [FixedText(NewerFormatWarningText)])
                ),
                new GuardKnown(
                    "src/QuickER.Mcp.Tools/DiagramFileReader.cs",
                    NewerFormatWarningText,
                    1,
                    "図のファイルの読み込みの共有の部品（両ツールが使う）"
                )
            ),
            // チャットエンジン 4 種は、ツール実行と活動通知を共有のヘルパー ChatToolInvocation で行う（4 か所へ写すと、通知を保護
            // しない・失敗の文言が違うずれが実際に起きた）。文字列で読む必須の規則なので、コメント・条件コンパイルの外れる分岐の中の
            // 表記でも満たされる
            SourceRule.Require(
                "chat-engine-tool-invocation-uses",
                "4 エンジンはツール実行と活動通知を ChatToolInvocation の Execute / Notify で行う",
                SourceScope.Files("src", ChatEngineCodes),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindForms(file, ChatToolInvocationForms)
                ),
                new(
                    ChatEngineCodes[0],
                    "ChatToolInvocation.Execute(",
                    1,
                    "API キー方式のツール実行"
                ),
                new(ChatEngineCodes[0], "ChatToolInvocation.Notify(", 1, "API キー方式の活動通知"),
                new(
                    ChatEngineCodes[1],
                    "ChatToolInvocation.Execute(",
                    1,
                    "Claude Code のツール実行"
                ),
                new(ChatEngineCodes[1], "ChatToolInvocation.Notify(", 1, "Claude Code の活動通知"),
                new(ChatEngineCodes[2], "ChatToolInvocation.Execute(", 1, "Codex のツール実行"),
                new(ChatEngineCodes[2], "ChatToolInvocation.Notify(", 2, "Codex の活動通知"),
                new(ChatEngineCodes[3], "ChatToolInvocation.Execute(", 1, "Copilot のツール実行"),
                new(ChatEngineCodes[3], "ChatToolInvocation.Notify(", 2, "Copilot の活動通知")
            ) with
            {
                MinimumFiles = ChatEngineCodes.Length,
            },
            // エンジン側に生の失敗の文言と、保護しない裸の通知を残さない（ヘルパー 1 か所に置く）
            SourceRule.Forbid(
                "chat-engine-tool-invocation-copies",
                "失敗の結果の文言と活動通知の保護は ChatToolInvocation の 1 か所に置く（生の文言・裸の通知を残さない）",
                SourceScope.Files("src", ChatEngineCodes),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindForms(file, ChatToolInvocationCopyForms)
                )
            ) with
            {
                MinimumFiles = ChatEngineCodes.Length,
            },
        ];

    /// <summary>出現が、その正本のファイルの中の正本自身の書き方か</summary>
    private static bool IsToolArgumentParsingOwner(SyntaxNode node, string what)
    {
        var path = node.SyntaxTree.FilePath.Replace('\\', '/');

        bool In(string file) => path == file || path.EndsWith("/" + file, StringComparison.Ordinal);

        return (
                In(ToolArgumentsCode)
                && what.EndsWith(" の宣言", StringComparison.Ordinal)
                && what.StartsWith("Get", StringComparison.Ordinal)
            ) || (In(RelationshipTypeCode) && what.StartsWith("Enum.", StringComparison.Ordinal));
    }

    /// <summary>節がツール引数の読み取り・種別の解釈の写しの形ならその説明（拾わなければ null）</summary>
    private static string? DescribeToolArgumentParsing(SemanticModel model, SyntaxNode node) =>
        node switch
        {
            MethodDeclarationSyntax or LocalFunctionStatementSyntax =>
                DescribeToolHelperDeclaration(model.GetDeclaredSymbol(node) as IMethodSymbol),
            InvocationExpressionSyntax invocation => DescribeRelationshipTypeEnumCall(
                model,
                invocation
            ),
            _ => null,
        };

    private static string? DescribeToolHelperDeclaration(IMethodSymbol? method)
    {
        if (method is null)
        {
            return null;
        }

        if (CopiedToolHelperNames.Contains(method.Name))
        {
            return $"{method.Name} の宣言";
        }

        if (
            JsonArgumentReaderNames.Contains(method.Name)
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

    private static string? DescribeRelationshipTypeEnumCall(
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

        if (name is null || !RelationshipTypeEnumMethods.Contains(name))
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
        type.ToDisplayString() == "QuickER.Model.RelationshipType";
}
