using System.Collections.Generic;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// 第 2 ステップ（モックプロジェクト生成）が LLM へ渡す固定文＝<see cref="MockProjectPromptBuilder"/> の全プロンプト・
/// ターゲットプロファイル（<see cref="MockProjectTargetProfile"/> の 2 実装）のフラグメントと README・
/// <see cref="MockSchemaSerializer"/> のスキーマ記述が、中立言語（英語）で統一されていること＝日本語（CJK 文字）が
/// 紛れ込んでいないことを守る回帰防止ガード。
/// </summary>
/// <remarks>
/// ヘッドレス実行の機械向け指示は英語固定（UI 言語に追従させない）が確定方針。和文が混入すると回答言語が意図せず
/// 引きずられるうえ、生成物に同梱する README-QuickER.md まで言語が割れる。入力はすべて ASCII のフィクスチャに
/// 揃えており（ユーザーデータ由来の日本語を混ぜない）、検出されれば固定文そのものの回帰である。
/// 走査範囲は <c>MockToolDefinitionEnglishGuardTests</c> / <c>ErDiagramToolCatalogEnglishGuardTests</c> と同じ CJK 範囲。
/// </remarks>
public class MockProjectPromptEnglishGuardTests
{
    /// <summary>
    /// CJK 文字の検出パターン。既存の英語ガードと同じ範囲
    /// （U+3000-U+9FFF＝CJK 記号・ひらがな・カタカナ・CJK 統合漢字、U+FF00-U+FFEF＝全角英数記号・半角カナ）を対象にする。
    /// </summary>
    private static readonly Regex CjkPattern = new("[　-鿿＀-￯]", RegexOptions.Compiled);

    /// <summary>走査対象の 2 プロファイル（WPF / Blazor）</summary>
    public static TheoryData<string> ProfileNames =>
        new() { MockProjectTarget.Wpf.Id, MockProjectTarget.Blazor.Id };

    /// <summary>Id からプロファイルを解決する（TheoryData に internal 型を載せないための間接）</summary>
    private static MockProjectTargetProfile ResolveProfile(string targetId) =>
        string.Equals(targetId, MockProjectTarget.Blazor.Id, StringComparison.Ordinal)
            ? MockProjectTargetProfile.Resolve(MockProjectTarget.Blazor)
            : MockProjectTargetProfile.Resolve(MockProjectTarget.Wpf);

    /// <summary>ASCII のみの画面（プレースホルダ経路を避けるため各項目を埋める）</summary>
    private static MockScreen AsciiScreen() =>
        new()
        {
            File = "OrderList.html",
            Name = "Order List",
            Description = "Lists the orders and opens the detail screen.",
        };

    /// <summary>ASCII のみの提出済みファイル一覧</summary>
    private static IReadOnlyList<string> AsciiEmittedFiles() =>
        new[] { "MockApp/App.xaml", "MockApp/Program.cs" };

    /// <summary>エージェント型のシステムプロンプト・初回プロンプトに CJK が無いことを検証する</summary>
    [Theory(DisplayName = "エージェント型プロンプトに日本語（CJK）が含まれない")]
    [MemberData(nameof(ProfileNames))]
    public void AgentPrompts_ContainNoCjk(string targetId)
    {
        var profile = ResolveProfile(targetId);

        AssertNoCjk(
            MockProjectPromptBuilder.BuildSystemPrompt(profile, "MockApp"),
            $"{targetId}: BuildSystemPrompt"
        );
        AssertNoCjk(
            MockProjectPromptBuilder.BuildPrompt(profile, "MockApp", null),
            $"{targetId}: BuildPrompt"
        );
        // 追加指示は見出しだけがガード対象（本文はユーザーデータなので ASCII を渡す）
        AssertNoCjk(
            MockProjectPromptBuilder.BuildPrompt(profile, "MockApp", "Use a dark theme."),
            $"{targetId}: BuildPrompt (with additional instructions)"
        );
    }

    /// <summary>API キー方式の 4 プロンプト（system／共通部／画面／修正）に CJK が無いことを検証する</summary>
    [Theory(DisplayName = "API キー方式プロンプトに日本語（CJK）が含まれない")]
    [MemberData(nameof(ProfileNames))]
    public void ApiKeyPrompts_ContainNoCjk(string targetId)
    {
        var profile = ResolveProfile(targetId);

        AssertNoCjk(
            MockProjectPromptBuilder.BuildApiKeySystemPrompt(profile, "MockApp"),
            $"{targetId}: BuildApiKeySystemPrompt"
        );

        AssertNoCjk(
            MockProjectPromptBuilder.BuildApiKeyCommonPrompt(
                profile,
                "MockApp",
                schema: "# Database Schema",
                screensOverview: "- OrderList.html : Order List",
                stylesheet: "body { color: #111; }",
                generatedSummary: "Generated files:\n- Repositories.cs"
            ),
            $"{targetId}: BuildApiKeyCommonPrompt"
        );
        // 空引数はプレースホルダ経路（「情報なし」の固定文）を通す
        AssertNoCjk(
            MockProjectPromptBuilder.BuildApiKeyCommonPrompt(
                profile,
                "MockApp",
                schema: string.Empty,
                screensOverview: string.Empty,
                stylesheet: string.Empty,
                generatedSummary: string.Empty
            ),
            $"{targetId}: BuildApiKeyCommonPrompt (placeholders)"
        );

        AssertNoCjk(
            MockProjectPromptBuilder.BuildApiKeyScreenPrompt(
                profile,
                "MockApp",
                AsciiScreen(),
                screenHtml: "<html><body><h1>Order List</h1></body></html>",
                transitions: "- OrderDetail.html (row click)",
                emittedFiles: AsciiEmittedFiles()
            ),
            $"{targetId}: BuildApiKeyScreenPrompt"
        );
        AssertNoCjk(
            MockProjectPromptBuilder.BuildApiKeyScreenPrompt(
                profile,
                "MockApp",
                new MockScreen { File = "OrderList.html" },
                screenHtml: string.Empty,
                transitions: string.Empty,
                emittedFiles: Array.Empty<string>()
            ),
            $"{targetId}: BuildApiKeyScreenPrompt (placeholders)"
        );

        AssertNoCjk(
            MockProjectPromptBuilder.BuildApiKeyFixPrompt(
                "error CS1002: ; expected",
                AsciiEmittedFiles()
            ),
            $"{targetId}: BuildApiKeyFixPrompt"
        );
        AssertNoCjk(
            MockProjectPromptBuilder.BuildApiKeyFixPrompt(string.Empty, Array.Empty<string>()),
            $"{targetId}: BuildApiKeyFixPrompt (placeholders)"
        );
    }

    /// <summary>生成物に同梱する規約ドキュメント（README-QuickER.md）に CJK が無いことを検証する（方言 3 分岐すべて）</summary>
    [Theory(DisplayName = "README-QuickER.md に日本語（CJK）が含まれない")]
    [MemberData(nameof(ProfileNames))]
    public void Readme_ContainsNoCjk(string targetId)
    {
        var profile = ResolveProfile(targetId);

        foreach (var dialect in new string?[] { "sqlserver", "sqlite", null })
        {
            AssertNoCjk(
                profile.BuildReadme("MockApp", "MockApp", dialect),
                $"{targetId}: BuildReadme ({dialect ?? "(no dialect)"})"
            );
        }
    }

    /// <summary>Codex 保険の自動続行ナッジに CJK が無いことを検証する</summary>
    [Fact(DisplayName = "Codex 自動続行ナッジに日本語（CJK）が含まれない")]
    public void CodexContinuationNudge_ContainsNoCjk()
    {
        AssertNoCjk(MockProjectPromptBuilder.CodexContinuationNudge, "CodexContinuationNudge");
    }

    /// <summary>ASCII のみの ER 図から起こしたスキーマ記述に CJK が無いことを検証する（空の図も含む）</summary>
    [Fact(DisplayName = "スキーマ記述に日本語（CJK）が含まれない")]
    public void SchemaSerializer_ContainsNoCjk()
    {
        AssertNoCjk(
            MockSchemaSerializer.Serialize(BuildAsciiDiagram()),
            "Serialize (ASCII diagram)"
        );
        AssertNoCjk(MockSchemaSerializer.Serialize(new ErDiagram()), "Serialize (empty diagram)");
    }

    /// <summary>
    /// ASCII のみの ER 図（表示名・説明つき／主キー・外部キー・NULL 可の各分岐と 1 対多リレーションを網羅）を組む。
    /// </summary>
    private static ErDiagram BuildAsciiDiagram()
    {
        var customerPk = new Column
        {
            Name = "CustomerId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
            Description = "Customer identifier",
        };
        var customer = new Entity
        {
            TableName = "Customer",
            Description = "Customers",
            Columns =
            {
                customerPk,
                new Column
                {
                    Name = "Note",
                    DataType = "nvarchar(200)",
                    IsNullable = true,
                },
            },
        };

        var orderFk = new Column
        {
            Name = "CustomerId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var order = new Entity
        {
            TableName = "Order",
            Columns =
            {
                new Column
                {
                    Name = "OrderId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                orderFk,
            },
        };

        return new ErDiagram
        {
            Entities = { customer, order },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                    ColumnPairs = [new(customerPk.Id, orderFk.Id)],
                    Type = RelationshipType.OneToMany,
                },
            },
        };
    }

    /// <summary>本文に CJK が無いことを検証する（見つかれば該当箇所を添えて失敗させる）</summary>
    private static void AssertNoCjk(string text, string label)
    {
        text.Should().NotBeNullOrWhiteSpace($"{label} の走査対象が空です");

        var match = CjkPattern.Match(text);

        match
            .Success.Should()
            .BeFalse(
                "第 2 ステップの機械向け固定文（プロンプト・README・スキーマ記述）は英語で統一する必要があります。"
                    + $"{label} の位置 {match.Index} 付近に日本語が混入しています: 「{Excerpt(text, match.Index)}」"
            );
    }

    /// <summary>失敗メッセージ用に、検出位置の前後を抜き出す</summary>
    private static string Excerpt(string text, int index)
    {
        var start = Math.Max(0, index - 40);
        var length = Math.Min(text.Length - start, 120);
        return text.Substring(start, length);
    }
}
