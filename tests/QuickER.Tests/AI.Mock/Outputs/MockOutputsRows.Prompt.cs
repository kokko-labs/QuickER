using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Mock.Outputs.MockOutputsContext;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Outputs;

/// <summary>出力の表の再開のプロンプト・スキーマの変更の判定・スキーマの食い違いの行（入力はメモリのマニフェストと図だけ）</summary>
/// <remarks>再開のプロンプトの日本語の文言を見る行は、UI のカルチャを行の中で ja に固定する。</remarks>
internal static partial class MockOutputsRows
{
    private static MockManifest SampleManifest(string schema = "# schema") =>
        new()
        {
            Title = "受注管理",
            SourceSchema = schema,
            Screens =
            {
                new MockScreen
                {
                    File = "OrderList.html",
                    Name = "注文一覧",
                    Description = "注文の一覧",
                },
                new MockScreen
                {
                    File = "OrderDetail.html",
                    Name = "注文詳細",
                    Description = "1 注文の詳細",
                },
            },
            Transitions =
            {
                new MockTransition
                {
                    From = "OrderList.html",
                    To = "OrderDetail.html",
                    Trigger = "行クリック",
                },
            },
            Revisions =
            {
                new MockRevision
                {
                    Timestamp = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero),
                    Note = "初版作成",
                },
            },
        };

    private static string ResumeJa(MockManifest manifest, bool schemaChanged) =>
        WithUiCulture(
            "ja",
            () => MockResumePrompt.Build("# 現在スキーマ", manifest, schemaChanged)
        );

    /// <summary>再開のプロンプトとスキーマの変更の判定</summary>
    private static IEnumerable<ScenarioRow<MockOutputScenario>> Resume() =>
        [
            Row(
                "resume-includes-screens-transitions-revisions-schema",
                "再開のプロンプトは画面・遷移・改訂・現在のスキーマ・get_screen の指示を含む",
                [(MockOutputSut.ResumePrompt, "resume-contents")],
                _ =>
                {
                    var prompt = ResumeJa(SampleManifest(), schemaChanged: false);

                    prompt.Should().Contain("OrderList.html");
                    prompt.Should().Contain("注文一覧");
                    prompt.Should().Contain("OrderList.html → OrderDetail.html");
                    prompt.Should().Contain("行クリック");
                    prompt.Should().Contain("初版作成");
                    prompt.Should().Contain("# 現在スキーマ");
                    prompt.Should().Contain("get_screen");
                }
            ),
            Row(
                "resume-injects-entity-declaration-state",
                "宣言のある画面はエンティティと CRUD を Name(OPS) で、宣言の無い画面は未宣言と注入する",
                [(MockOutputSut.ResumePrompt, "resume-entities")],
                _ =>
                {
                    var manifest = SampleManifest();
                    manifest.Screens[0].Entities =
                    [
                        new MockScreenEntity { Name = "Order", Operations = "CRU" },
                        new MockScreenEntity { Name = "Customer", Operations = "R" },
                    ];

                    var prompt = ResumeJa(manifest, schemaChanged: false);

                    prompt.Should().Contain("エンティティ: Order(CRU), Customer(R)");
                    prompt.Should().Contain("（未宣言）");
                }
            ),
            .. new[] { true, false }.Select(changed =>
                Row(
                    $"resume-schema-{(changed ? "changed-includes" : "unchanged-omits")}-note",
                    $"スキーマの変更 {changed} のとき、差異の注記を{(changed ? "含む" : "含まない")}",
                    [(MockOutputSut.ResumePrompt, "resume-schema-note")],
                    _ =>
                    {
                        var prompt = ResumeJa(SampleManifest(), changed);

                        prompt
                            .Contains("スキーマが変更されています", StringComparison.Ordinal)
                            .Should()
                            .Be(changed);
                    }
                )
            ),
            Row(
                "resume-revision-timestamp-culture-invariant",
                "改訂の日時は書式のカルチャに依らず西暦の同じ書式で渡す",
                [(MockOutputSut.ResumePrompt, "resume-revision-time")],
                _ =>
                {
                    foreach (var culture in CultureScope.Cultures)
                    {
                        using var scope = new CultureScope(culture);

                        MockResumePrompt
                            .Build("# schema", SampleManifest(), schemaChanged: false)
                            .Should()
                            .Contain("- 2026-07-20 09:00", culture);
                    }
                }
            ),
            .. new[]
            {
                (
                    "change-same",
                    "# データベーススキーマ\n本文",
                    "# データベーススキーマ\n本文",
                    false
                ),
                ("change-newline", "line1\nline2", "line1\r\nline2", false),
                ("change-difference", "# 新スキーマ", "# 旧スキーマ", true),
            }.Select(c =>
                Row(
                    $"schema-{c.Item1}-is-{(c.Item4 ? "changed" : "unchanged")}",
                    $"IsSchemaChanged は（{c.Item1}）を{(c.Item4 ? "変更あり" : "変更なし")}と判定する（改行の違いは吸収する）",
                    [(MockOutputSut.SchemaChange, c.Item1)],
                    _ =>
                        MockResumePrompt
                            .IsSchemaChanged(c.Item2, SampleManifest(c.Item3))
                            .Should()
                            .Be(c.Item4)
                )
            ),
        ];

    /// <summary>列 1 つのテーブルを作る</summary>
    private static Entity OneColumnTable(string name, string column = "id", string type = "int") =>
        new()
        {
            Id = Guid.NewGuid(),
            TableName = name,
            Columns =
            [
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = column,
                    DataType = type,
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };

    private static ErDiagram Diagram(params Entity[] tables) =>
        new() { TargetDbms = "sqlserver", Entities = [.. tables] };

    /// <summary>設計（元のスキーマと、テーブルを宣言した画面）を作る（元のスキーマが無ければ null）</summary>
    private static MockManifest Design(
        ErDiagram? source,
        params (string Screen, string Table)[] declarations
    ) =>
        new()
        {
            SourceSchema = source is null ? string.Empty : MockSchemaSerializer.Serialize(source),
            Screens =
            [
                .. declarations
                    .GroupBy(d => d.Screen)
                    .Select(group => new MockScreen
                    {
                        File = group.Key,
                        Name = group.Key,
                        Entities =
                        [
                            .. group.Select(d => new MockScreenEntity
                            {
                                Name = d.Table,
                                Operations = "R",
                            }),
                        ],
                    }),
            ],
        };

    /// <summary>スキーマの食い違いと、スキーマの書き出しの分割</summary>
    private static IEnumerable<ScenarioRow<MockOutputScenario>> Mismatch() =>
        [
            Row(
                "mismatch-same-schema-has-none",
                "設計と同じ図では食い違いが無く、元のスキーマを記録済みと読む",
                [(MockOutputSut.SchemaMismatch, "mismatch-same")],
                _ =>
                {
                    var diagram = Diagram(OneColumnTable("Customer"), OneColumnTable("Order"));

                    var mismatch = MockSchemaMismatch.Compare(
                        diagram,
                        Design(diagram, ("CustomerList.html", "Customer"))
                    );

                    mismatch.RequiresConfirmation.Should().BeFalse();
                    mismatch.HasContentDifferences.Should().BeFalse();
                    mismatch.SourceSchemaRecorded.Should().BeTrue();
                }
            ),
            Row(
                "mismatch-tables-added-or-removed-require-confirmation",
                "テーブルの増減と、画面が宣言しているのに図に無いテーブルは確認の対象で、内訳（resx の書式）がテーブル名と画面を名指しする",
                [(MockOutputSut.SchemaMismatch, "mismatch-tables")],
                _ =>
                {
                    var design = Diagram(OneColumnTable("Product"), OneColumnTable("Category"));
                    var now = Diagram(OneColumnTable("Customer"), OneColumnTable("Category"));

                    var mismatch = MockSchemaMismatch.Compare(
                        now,
                        Design(
                            design,
                            ("ProductList.html", "Product"),
                            ("ProductList.html", "Category")
                        )
                    );

                    mismatch.RequiresConfirmation.Should().BeTrue();
                    mismatch.TablesOnlyInDesign.Should().Equal("Product");
                    mismatch.TablesOnlyInDiagram.Should().Equal("Customer");
                    mismatch
                        .ScreenTablesNotInDiagram.Should()
                        .Equal(("ProductList.html", "Product"));
                    mismatch.ChangedTables.Should().BeEmpty();
                    var details = mismatch.Describe();
                    details
                        .Should()
                        .Contain(
                            string.Format(
                                MockStrings.Mock_SchemaMismatch_TablesOnlyInDesignFormat,
                                "Product"
                            )
                        );
                    details
                        .Should()
                        .Contain(
                            string.Format(
                                MockStrings.Mock_SchemaMismatch_TablesOnlyInDiagramFormat,
                                "Customer"
                            )
                        );
                    details
                        .Should()
                        .Contain(
                            string.Format(
                                MockStrings.Mock_SchemaMismatch_ScreenTableFormat,
                                "ProductList.html",
                                "Product"
                            )
                        );
                }
            ),
            Row(
                "mismatch-content-only-does-not-require-confirmation",
                "列・型・リレーション・対象 DBMS だけの違いは確認の対象にせず内訳に載せ、画面の宣言は大文字小文字を無視して照らす",
                [(MockOutputSut.SchemaMismatch, "mismatch-content")],
                _ =>
                {
                    var designCustomer = OneColumnTable("Customer");
                    var designOrder = OneColumnTable("Order");
                    var design = Diagram(designCustomer, designOrder);
                    design.Relationships.Add(
                        new Relationship
                        {
                            Id = Guid.NewGuid(),
                            SourceEntityId = designCustomer.Id,
                            TargetEntityId = designOrder.Id,
                            Type = RelationshipType.OneToMany,
                        }
                    );
                    var now = Diagram(
                        OneColumnTable("Customer", type: "bigint"),
                        OneColumnTable("Order")
                    );
                    now.TargetDbms = "sqlite";

                    var mismatch = MockSchemaMismatch.Compare(
                        now,
                        Design(design, ("CustomerList.html", "customer"))
                    );

                    mismatch.RequiresConfirmation.Should().BeFalse();
                    mismatch.HasContentDifferences.Should().BeTrue();
                    mismatch.ChangedTables.Should().Equal("Customer");
                    mismatch.RelationshipsChanged.Should().BeTrue();
                    mismatch.TargetChanged.Should().BeTrue();
                    mismatch.ScreenTablesNotInDiagram.Should().BeEmpty();
                    mismatch
                        .Describe()
                        .Should()
                        .Contain(
                            string.Format(
                                MockStrings.Mock_SchemaMismatch_ChangedTablesFormat,
                                "Customer"
                            )
                        );
                }
            ),
            .. new[] { ("Customer", false), ("Product", true) }.Select(c =>
                Row(
                    $"mismatch-no-source-schema-checks-declarations-only-{c.Item1.ToLowerInvariant()}",
                    $"元のスキーマが無い設計はスキーマを比べず、画面の宣言（{c.Item1}）だけを図と照らす（確認 {c.Item2}）",
                    [(MockOutputSut.SchemaMismatch, "mismatch-no-source")],
                    _ =>
                    {
                        var mismatch = MockSchemaMismatch.Compare(
                            Diagram(OneColumnTable("Customer")),
                            Design(null, ("Screen.html", c.Item1))
                        );

                        mismatch.SourceSchemaRecorded.Should().BeFalse();
                        mismatch.RequiresConfirmation.Should().Be(c.Item2);
                        mismatch.TablesOnlyInDesign.Should().BeEmpty();
                        mismatch.TablesOnlyInDiagram.Should().BeEmpty();
                        mismatch.HasContentDifferences.Should().BeFalse();
                    }
                )
            ),
            Row(
                "mismatch-split-covers-every-line",
                "スキーマの書き出しをテーブルごとの節に分けると、表示名つきの見出しからもテーブル名が取れ、見出し・節・リレーションに全行が漏れなく入る",
                [(MockOutputSut.SchemaMismatch, "mismatch-split")],
                _ =>
                {
                    var customer = OneColumnTable("Customer");
                    customer.Description = "顧客";
                    var order = OneColumnTable("Order");
                    var diagram = Diagram(customer, order);
                    diagram.Relationships.Add(
                        new Relationship
                        {
                            Id = Guid.NewGuid(),
                            SourceEntityId = customer.Id,
                            TargetEntityId = order.Id,
                            Type = RelationshipType.OneToMany,
                        }
                    );
                    var text = MockSchemaSerializer.Serialize(diagram);

                    var sections = MockSchemaSerializer.Split(text);

                    sections.Tables.Select(t => t.Key).Should().Equal("Customer", "Order");
                    sections.Tables[0].Value.Should().StartWith("### Customer (顧客)");
                    sections.Header.Should().Contain("Target DBMS: sqlserver");
                    sections.Relationships.Should().Contain("Customer → Order");
                    var parts = string.Join(
                        "\n",
                        [
                            sections.Header,
                            .. sections.Tables.Select(t => t.Value),
                            sections.Relationships,
                        ]
                    );

                    foreach (
                        var line in text.ReplaceLineEndings("\n")
                            .Split('\n')
                            .Where(line =>
                                line.Length > 0
                                && line is not "## Tables" and not "## Relationships"
                            )
                    )
                    {
                        parts.Should().Contain(line);
                    }
                }
            ),
        ];
}
