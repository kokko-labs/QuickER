using QuickER.AI.Mock;
using QuickER.Model;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>英語の文言の表：AiMock（モックプロジェクト生成が LLM へ渡す固定文・同梱の README・スキーマ記述）</summary>
/// <remarks>
/// 第 2 ステップ（モックプロジェクト生成）の機械向け指示は英語固定（UI 言語に追従させない）が確定方針。和文が混入すると回答言語が
/// 意図せず引きずられるうえ、生成物に同梱する README-QuickER.md まで言語が割れる。入力はすべて ASCII のフィクスチャに揃えており
/// （ユーザーデータ由来の日本語を混ぜない）、検出されれば固定文そのものの回帰である。出力は日本語 UI で作る。
/// </remarks>
internal static partial class EnglishTextRows
{
    /// <summary>第 2 ステップの固定文の行の守ること</summary>
    private const string MockProjectBecause =
        "第 2 ステップの機械向け固定文（プロンプト・README・スキーマ記述）は英語で統一する";

    private static IEnumerable<EnglishTextRow> AiMock() =>
        [
            .. new[] { MockProjectTarget.Wpf, MockProjectTarget.Blazor }.SelectMany(target =>
                new EnglishTextRow[]
                {
                    MockProjectRow(
                        $"mock-project-agent-prompts-{target.Id}",
                        "エージェント型のシステムプロンプト・初回プロンプト",
                        () => AgentPrompts(target)
                    ),
                    MockProjectRow(
                        $"mock-project-api-key-prompts-{target.Id}",
                        "API キー方式の 4 プロンプト（system／共通部／画面／修正。空引数のプレースホルダ経路も）",
                        () => ApiKeyPrompts(target)
                    ),
                    MockProjectRow(
                        $"mock-project-readme-{target.Id}",
                        "生成物に同梱する規約ドキュメント README-QuickER.md（方言 3 分岐すべて）",
                        () => Readmes(target)
                    ),
                }
            ),
            MockProjectRow(
                "mock-project-continuation-nudge",
                "Codex 保険の自動続行ナッジ",
                () => [Mock("ContinuationNudge", MockProjectPromptBuilder.ContinuationNudge)]
            ),
            MockProjectRow(
                "mock-project-schema-description",
                "ASCII のみの ER 図から起こしたスキーマ記述（空の図も）",
                () =>
                    [
                        Mock(
                            "Serialize (ASCII diagram)",
                            MockSchemaSerializer.Serialize(BuildAsciiDiagram())
                        ),
                        Mock(
                            "Serialize (empty diagram)",
                            MockSchemaSerializer.Serialize(new ErDiagram())
                        ),
                    ]
            ),
        ];

    /// <summary>第 2 ステップの固定文の行（出どころは日本語 UI で評価する）</summary>
    private static EnglishTextRow MockProjectRow(
        string id,
        string what,
        Func<IReadOnlyList<EnglishText>> texts
    ) => new(id, $"{MockProjectBecause}：{what}", () => UnderUiCulture(JapaneseUi, texts));

    /// <summary>走査する本文 1 つ（空なら走査が空振りしているので例外）</summary>
    private static EnglishText Mock(string where, string text) =>
        string.IsNullOrWhiteSpace(text)
            ? throw new InvalidOperationException($"{where} の走査対象が空")
            : new(where, text);

    /// <summary>ターゲットのプロファイル</summary>
    private static MockProjectTargetProfile ResolveProfile(MockProjectTarget target) =>
        MockProjectTargetProfile.Resolve(target);

    /// <summary>エージェント型のシステムプロンプト・初回プロンプト（追加指示あり・なし）</summary>
    private static IReadOnlyList<EnglishText> AgentPrompts(MockProjectTarget target)
    {
        var profile = ResolveProfile(target);

        return
        [
            Mock(
                $"{target.Id}: BuildSystemPrompt",
                MockProjectPromptBuilder.BuildSystemPrompt(profile, "MockApp")
            ),
            Mock(
                $"{target.Id}: BuildPrompt",
                MockProjectPromptBuilder.BuildPrompt(profile, "MockApp", null)
            ),
            // 追加指示は見出しだけがガード対象（本文はユーザーデータなので ASCII を渡す）
            Mock(
                $"{target.Id}: BuildPrompt (with additional instructions)",
                MockProjectPromptBuilder.BuildPrompt(profile, "MockApp", "Use a dark theme.")
            ),
        ];
    }

    /// <summary>API キー方式の 4 プロンプト（空引数はプレースホルダ経路＝「情報なし」の固定文を通す）</summary>
    private static IReadOnlyList<EnglishText> ApiKeyPrompts(MockProjectTarget target)
    {
        var profile = ResolveProfile(target);

        return
        [
            Mock(
                $"{target.Id}: BuildApiKeySystemPrompt",
                MockProjectPromptBuilder.BuildApiKeySystemPrompt(profile, "MockApp", null)
            ),
            Mock(
                $"{target.Id}: BuildApiKeyCommonPrompt",
                MockProjectPromptBuilder.BuildApiKeyCommonPrompt(
                    profile,
                    "MockApp",
                    schema: "# Database Schema",
                    screensOverview: "- OrderList.html : Order List",
                    stylesheet: "body { color: #111; }",
                    generatedSummary: "Generated files:\n- Repositories.cs"
                )
            ),
            Mock(
                $"{target.Id}: BuildApiKeyCommonPrompt (placeholders)",
                MockProjectPromptBuilder.BuildApiKeyCommonPrompt(
                    profile,
                    "MockApp",
                    schema: string.Empty,
                    screensOverview: string.Empty,
                    stylesheet: string.Empty,
                    generatedSummary: string.Empty
                )
            ),
            Mock(
                $"{target.Id}: BuildApiKeyScreenPrompt",
                MockProjectPromptBuilder.BuildApiKeyScreenPrompt(
                    profile,
                    "MockApp",
                    AsciiScreen(),
                    screenHtml: "<html><body><h1>Order List</h1></body></html>",
                    transitions: "- OrderDetail.html (row click)",
                    emittedFiles: AsciiEmittedFiles()
                )
            ),
            Mock(
                $"{target.Id}: BuildApiKeyScreenPrompt (placeholders)",
                MockProjectPromptBuilder.BuildApiKeyScreenPrompt(
                    profile,
                    "MockApp",
                    new MockScreen { File = "OrderList.html" },
                    screenHtml: string.Empty,
                    transitions: string.Empty,
                    emittedFiles: Array.Empty<string>()
                )
            ),
            Mock(
                $"{target.Id}: BuildApiKeyFixPrompt",
                MockProjectPromptBuilder.BuildApiKeyFixPrompt(
                    "error CS1002: ; expected",
                    AsciiEmittedFiles()
                )
            ),
            Mock(
                $"{target.Id}: BuildApiKeyFixPrompt (placeholders)",
                MockProjectPromptBuilder.BuildApiKeyFixPrompt(string.Empty, Array.Empty<string>())
            ),
        ];
    }

    /// <summary>README-QuickER.md（方言 sqlserver / sqlite / なしの 3 分岐）</summary>
    private static IReadOnlyList<EnglishText> Readmes(MockProjectTarget target)
    {
        var profile = ResolveProfile(target);

        return
        [
            .. new string?[] { "sqlserver", "sqlite", null }.Select(dialect =>
                Mock(
                    $"{target.Id}: BuildReadme ({dialect ?? "(no dialect)"})",
                    profile.BuildReadme(
                        "MockApp",
                        MockProjectScaffoldService.ResolveNamespaces("MockApp", dialect),
                        dialect
                    )
                )
            ),
        ];
    }

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
}
