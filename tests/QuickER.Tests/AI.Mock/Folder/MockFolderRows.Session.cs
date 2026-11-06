using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Mock.Folder.MockFolderHarness;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Folder;

/// <summary>モックフォルダの保存単位の表のセッションの行（ツールの実行・会話の開始・初回ターン）</summary>
internal static partial class MockFolderRows
{
    private const string SaveScreenTool = MockFolderDesignTools.SaveScreenToolName;

    /// <summary>テーブル 1 つの図</summary>
    private static ErDiagram Diagram(string table, string description = "") =>
        new()
        {
            Entities =
            {
                new Entity { TableName = table, Description = description },
            },
        };

    /// <summary>ストアとセッションを作る</summary>
    private static void Start(MockFolderHarness h, string schema = "")
    {
        h.CreateNew(schema: schema);
        h.CreateSession();
    }

    /// <summary>OrderList.html を Order の R 宣言つきで保存する（セッション経由）</summary>
    private static void SaveOrderListDeclaringOrder(MockFolderHarness h) =>
        h.Execute(
            SaveScreenTool,
            SaveScreenArgs(
                "OrderList.html",
                "注文一覧",
                ScreenHtml("<h1>一覧</h1>"),
                entities: [new { name = "Order", operations = "R" }]
            )
        );

    private static IReadOnlyList<MockScreenEntity> RecordedEntities(MockFolderHarness h) =>
        h.Reopen().Screens.Single(s => s.File == "OrderList.html").Entities ?? [];

    /// <summary>ツールの実行</summary>
    private static IEnumerable<ScenarioRow<MockFolderScenario>> Session() =>
        [
            Row(
                "session-tool-save-screen-writes-upserts-and-notifies",
                "save_screen の成功は HTML を書き、宣言を足し、注記つきの ScreenSaved を出し、結果に画面の名前を含める",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    Start(h);

                    var (result, success) = h.Execute(
                        SaveScreenTool,
                        SaveScreenArgs(
                            "OrderList.html",
                            "注文一覧",
                            ScreenHtml("<h1>注文一覧</h1>"),
                            description: "注文の一覧",
                            revisionNote: "初版"
                        )
                    );

                    success.Should().BeTrue();
                    result.Should().Contain("OrderList.html");
                    File.Exists(Path.Combine(h.Folder, "OrderList.html")).Should().BeTrue();
                    var reopened = h.Reopen();
                    reopened.Screens.Should().ContainSingle();
                    reopened.Screens[0].File.Should().Be("OrderList.html");
                    reopened.Screens[0].Name.Should().Be("注文一覧");
                    h.ScreenSavedEvents.Should().ContainSingle();
                    h.ScreenSavedEvents[0].File.Should().Be("OrderList.html");
                    h.ScreenSavedEvents[0].RevisionNote.Should().Be("初版");
                }
            ),
            Row(
                "session-tool-save-screen-appends-warnings",
                "警告の付く画面（外部参照・共有 CSS の未参照）の save_screen は成功し、結果に Warnings: を連ね、通知にも警告を載せる",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    Start(h);
                    var html =
                        "<!DOCTYPE html><html><head></head>"
                        + "<body><img src=\"https://example.com/logo.png\"></body></html>";

                    var (result, success) = h.Execute(
                        SaveScreenTool,
                        SaveScreenArgs("Bad.html", "不正", html)
                    );

                    success.Should().BeTrue();
                    result.Should().Contain("Warnings:");
                    h.ScreenSavedEvents.Single().Warnings.Should().NotBeEmpty();
                }
            ),
            Row(
                "session-tool-save-screen-invalid-html-fails-without-notification",
                "HTML でない save_screen は失敗の結果を返し（例外を漏らさない）、通知もファイルも出さない",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Refused,
                h =>
                {
                    Start(h);

                    var (_, success) = h.Execute(
                        SaveScreenTool,
                        SaveScreenArgs("Frag.html", "断片", "<div>部分</div>")
                    );

                    success.Should().BeFalse();
                    h.ScreenSavedEvents.Should().BeEmpty();
                    File.Exists(Path.Combine(h.Folder, "Frag.html")).Should().BeFalse();
                }
            ),
            Row(
                "session-tool-save-screen-transitions-from-saved-screen",
                "save_screen の transitions は起点を保存した画面にして記録する",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    Start(h);

                    h.Execute(
                        SaveScreenTool,
                        SaveScreenArgs(
                            "OrderList.html",
                            "注文一覧",
                            ScreenHtml("<a href=\"OrderDetail.html\">詳細</a>"),
                            transitions: [new { to = "OrderDetail.html", trigger = "行クリック" }]
                        )
                    );

                    var transition = h.Reopen().Transitions.Should().ContainSingle().Which;
                    transition.From.Should().Be("OrderList.html");
                    transition.To.Should().Be("OrderDetail.html");
                    transition.Trigger.Should().Be("行クリック");
                }
            ),
            Row(
                "session-tool-remove-screen-removes-and-notifies",
                "remove_screen は宣言のある画面を消し、ScreenRemoved を出す",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew(schema: "");
                    h.SaveScreen(
                        "OrderList.html",
                        "注文一覧",
                        "注文の一覧",
                        ScreenHtml("<h1>一覧</h1>"),
                        None,
                        "初版"
                    );
                    h.CreateSession();

                    var (_, success) = h.Execute(
                        MockFolderDesignTools.RemoveScreenToolName,
                        "{\"file\":\"OrderList.html\"}"
                    );

                    success.Should().BeTrue();
                    h.ScreenRemovedEvents.Should().Equal("OrderList.html");
                    File.Exists(Path.Combine(h.Folder, "OrderList.html")).Should().BeFalse();
                }
            ),
            Row(
                "session-tool-remove-screen-missing-fails-with-screen-list",
                "宣言の無い画面の remove_screen は失敗の結果に今ある画面の一覧を添える",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Refused,
                h =>
                {
                    h.CreateNew(schema: "");
                    h.SaveScreen(
                        "OrderList.html",
                        "注文一覧",
                        "注文の一覧",
                        ScreenHtml("<h1>一覧</h1>"),
                        None,
                        "初版"
                    );
                    h.CreateSession();

                    var (result, success) = h.Execute(
                        MockFolderDesignTools.RemoveScreenToolName,
                        "{\"file\":\"Missing.html\"}"
                    );

                    success.Should().BeFalse();
                    result.Should().Contain("OrderList.html");
                }
            ),
            Row(
                "session-tool-save-stylesheet-writes-and-notifies",
                "save_stylesheet は style.css を書き、注記つきの StylesheetSaved を出す",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    Start(h);

                    var (_, success) = h.Execute(
                        MockFolderDesignTools.SaveStylesheetToolName,
                        "{\"css\":\"body{margin:0}\",\"revision_note\":\"初版CSS\"}"
                    );

                    success.Should().BeTrue();
                    File.Exists(Path.Combine(h.Folder, MockManifest.StylesheetFileName))
                        .Should()
                        .BeTrue();
                    h.StylesheetSavedEvents.Single().RevisionNote.Should().Be("初版CSS");
                }
            ),
            Row(
                "session-tool-get-screen-returns-html",
                "get_screen は宣言のある画面の HTML を全文そのまま返す",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    var html = ScreenHtml("<h1>注文一覧</h1>");
                    h.CreateNew(schema: "");
                    h.SaveScreen("OrderList.html", "注文一覧", "注文の一覧", html, None, "初版");
                    h.CreateSession();

                    var (result, success) = h.Execute(
                        MockFolderDesignTools.GetScreenToolName,
                        "{\"file\":\"OrderList.html\"}"
                    );

                    success.Should().BeTrue();
                    result.Should().Be(html);
                }
            ),
            Row(
                "session-tool-get-screen-missing-fails-with-screen-list",
                "無い画面の get_screen は失敗の結果に今ある画面の一覧を添える",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Refused,
                h =>
                {
                    h.CreateNew(schema: "");
                    h.SaveScreen(
                        "OrderList.html",
                        "注文一覧",
                        "注文の一覧",
                        ScreenHtml("<h1>一覧</h1>"),
                        None,
                        "初版"
                    );
                    h.CreateSession();

                    var (result, success) = h.Execute(
                        MockFolderDesignTools.GetScreenToolName,
                        "{\"file\":\"Missing.html\"}"
                    );

                    success.Should().BeFalse();
                    result.Should().Contain("OrderList.html");
                }
            ),
            Row(
                "session-tool-unknown-tool-fails",
                "知らないツールは、その名前を含む失敗の結果になる",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Refused,
                h =>
                {
                    Start(h);

                    var (result, success) = h.Execute("no_such_tool", "{}");

                    success.Should().BeFalse();
                    result.Should().Contain("no_such_tool");
                }
            ),
            .. new[] { ("array", "[]"), ("string", "\"x\""), ("number", "42") }.Select(c =>
                Row(
                    $"session-tool-non-object-arguments-fail-{c.Item1}",
                    $"引数のルートがオブジェクトでない（{c.Item2}）ツールの実行は、例外を漏らさず引数の誤りの失敗の結果になる",
                    MockFolderEntry.SessionTool,
                    MockFolderOutcome.Refused,
                    h =>
                    {
                        Start(h);

                        var (result, success) = h.Execute(SaveScreenTool, c.Item2);

                        success.Should().BeFalse();
                        result
                            .Should()
                            .Be(
                                string.Format(
                                    MockStrings.Mock_ArgumentsParseFailedResult,
                                    "The arguments must be a JSON object."
                                )
                            );
                    }
                )
            ),
            Row(
                "session-tool-save-screen-write-failure-fails-without-notification",
                "mock.json へ書けない save_screen は、フォルダの読み書きの失敗の結果を返し、ScreenSaved を出さず、メモリを変えない（失敗で残る一時の食い違い：宣言の無い HTML の OrderList.html）",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.IoFailure,
                h =>
                {
                    Start(h);

                    using (h.ManifestReadOnly())
                    {
                        var (result, success) = h.Execute(
                            SaveScreenTool,
                            SaveScreenArgs(
                                "OrderList.html",
                                "注文一覧",
                                ScreenHtml("<h1>一覧</h1>")
                            )
                        );

                        success.Should().BeFalse();
                        result
                            .Should()
                            .StartWith(MockStrings.Mock_ToolFileAccessFailedResult.Split("{0}")[0]);
                        h.ScreenSavedEvents.Should().BeEmpty();
                        h.Store.Manifest.Screens.Should().BeEmpty();
                    }
                }
            ),
        ];

    /// <summary>save_screen の entities</summary>
    private static IEnumerable<ScenarioRow<MockFolderScenario>> SessionEntities() =>
        [
            Row(
                "session-tool-save-screen-entities-normalized",
                "save_screen の entities は正規化して記録する",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    Start(h);

                    var (_, success) = h.Execute(
                        SaveScreenTool,
                        SaveScreenArgs(
                            "OrderList.html",
                            "注文一覧",
                            ScreenHtml("<h1>一覧</h1>"),
                            entities:
                            [
                                new { name = "Order", operations = "urc" },
                                new { name = "Customer", operations = "r" },
                            ]
                        )
                    );

                    success.Should().BeTrue();
                    RecordedEntities(h)
                        .Select(e => (e.Name, e.Operations))
                        .Should()
                        .BeEquivalentTo([("Order", "CRU"), ("Customer", "R")]);
                }
            ),
            Row(
                "session-tool-save-screen-omitted-entities-keep-declarations",
                "entities を省いた save_screen は既存の宣言を残す",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    Start(h);
                    h.Execute(
                        SaveScreenTool,
                        SaveScreenArgs(
                            "OrderList.html",
                            "注文一覧",
                            ScreenHtml("<h1>v1</h1>"),
                            entities: [new { name = "Order", operations = "CRUD" }]
                        )
                    );

                    h.Execute(
                        SaveScreenTool,
                        SaveScreenArgs("OrderList.html", "注文一覧", ScreenHtml("<h1>v2</h1>"))
                    );

                    var recorded = RecordedEntities(h);
                    recorded.Should().ContainSingle();
                    recorded[0].Name.Should().Be("Order");
                    recorded[0].Operations.Should().Be("CRUD");
                }
            ),
            Row(
                "session-tool-save-screen-discarded-entity-warns",
                "正規化で操作が空になった宣言は警告に名前と CRUD を載せる",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    Start(h);

                    var (result, success) = h.Execute(
                        SaveScreenTool,
                        SaveScreenArgs(
                            "OrderList.html",
                            "注文一覧",
                            ScreenHtml("<h1>一覧</h1>"),
                            entities: [new { name = "Junk", operations = "zzz" }]
                        )
                    );

                    success.Should().BeTrue();
                    result.Should().Contain("Warnings:");
                    h.ScreenSavedEvents.Single()
                        .Warnings.Should()
                        .Contain(w => w.Contains("Junk") && w.Contains("CRUD"));
                }
            ),
            RowAsync(
                "session-tool-save-screen-entity-not-in-diagram-warns",
                "会話を始めた後、図に無いテーブル名の宣言は警告し、図にある名前は警告しない",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Succeeds,
                async h =>
                {
                    Start(h);
                    await h.StartNewAsync(Diagram("Order"));

                    var (_, success) = h.Execute(
                        SaveScreenTool,
                        SaveScreenArgs(
                            "OrderList.html",
                            "注文一覧",
                            ScreenHtml("<h1>一覧</h1>"),
                            entities:
                            [
                                new { name = "Order", operations = "r" },
                                new { name = "Ghost", operations = "r" },
                            ]
                        )
                    );

                    success.Should().BeTrue();
                    var warnings = h.ScreenSavedEvents.Single().Warnings;
                    warnings.Should().Contain(w => w.Contains("Ghost"));
                    warnings.Should().NotContain(w => w.Contains("'Order'"));
                }
            ),
            Row(
                "session-tool-save-screen-nameless-entity-warns-and-skips",
                "名前が空の宣言の要素は警告して読み飛ばし、ほかの宣言は記録する",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    Start(h);

                    h.Execute(
                        SaveScreenTool,
                        SaveScreenArgs(
                            "OrderList.html",
                            "注文一覧",
                            ScreenHtml("<h1>一覧</h1>"),
                            entities:
                            [
                                new { name = "", operations = "cr" },
                                new { name = "Order", operations = "r" },
                            ]
                        )
                    );

                    h.ScreenSavedEvents.Single()
                        .Warnings.Should()
                        .Contain(w => w.Contains("name") && w.Contains("empty"));
                    var recorded = RecordedEntities(h);
                    recorded.Should().ContainSingle();
                    recorded[0].Name.Should().Be("Order");
                }
            ),
            .. new[]
            {
                ("nameless", """[{"name": ""}]"""),
                ("non-object", """["Order"]"""),
                ("invalid-operations", """[{"name": "Order", "operations": "Z"}]"""),
            }.Select(c =>
                Row(
                    $"session-tool-save-screen-all-invalid-entities-keep-declarations-{c.Item1}",
                    $"空でない entities の要素が 1 つも有効でない（{c.Item1}）ときは、消去でなく省略として既存の宣言を残し、その旨を警告する",
                    MockFolderEntry.SessionTool,
                    MockFolderOutcome.Succeeds,
                    h =>
                    {
                        Start(h);
                        SaveOrderListDeclaringOrder(h);
                        var args =
                            SaveScreenArgs(
                                    "OrderList.html",
                                    "注文一覧",
                                    ScreenHtml("<h1>一覧</h1>")
                                )
                                .TrimEnd('}')
                            + ",\"entities\":"
                            + c.Item2
                            + "}";

                        var (result, success) = h.Execute(SaveScreenTool, args);

                        success.Should().BeTrue();
                        result.Should().Contain("existing declarations were kept");
                        h.ScreenSavedEvents[^1]
                            .Warnings.Should()
                            .Contain(w => w.Contains("existing declarations were kept"));
                        var recorded = RecordedEntities(h);
                        recorded.Should().ContainSingle();
                        recorded[0].Name.Should().Be("Order");
                        recorded[0].Operations.Should().Be("R");
                    }
                )
            ),
            Row(
                "session-tool-save-screen-empty-entities-clear-declarations",
                "明示の空配列の entities は宣言を消す（全部不正の扱いとの対照）",
                MockFolderEntry.SessionTool,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    Start(h);
                    SaveOrderListDeclaringOrder(h);

                    var (result, success) = h.Execute(
                        SaveScreenTool,
                        SaveScreenArgs(
                            "OrderList.html",
                            "注文一覧",
                            ScreenHtml("<h1>一覧</h1>"),
                            entities: []
                        )
                    );

                    success.Should().BeTrue();
                    result.Should().NotContain("existing declarations were kept");
                    h.Reopen()
                        .Screens.Single(s => s.File == "OrderList.html")
                        .Entities.Should()
                        .BeNull();
                }
            ),
        ];

    /// <summary>会話の開始・初回ターン・フィードバック</summary>
    private static IEnumerable<ScenarioRow<MockFolderScenario>> InitialTurn() =>
        [
            RowAsync(
                "session-start-new-sends-schema-and-instructions-and-snapshots",
                "StartNewAsync はスキーマと補足の指示を初回のプロンプトで送り、スナップショットが空なので成功したターンで現在のスキーマを保存する",
                MockFolderEntry.SessionStart,
                MockFolderOutcome.Succeeds,
                async h =>
                {
                    Start(h);

                    await h.StartNewAsync(Diagram("Customer", "顧客"), "モダンな配色にして");

                    h.Engine.SentPrompts.Should().ContainSingle();
                    h.Engine.SentPrompts[0].Should().Contain("Customer");
                    h.Engine.SentPrompts[0].Should().Contain("モダンな配色にして");
                    h.Reopen().SourceSchema.Should().Contain("Customer");
                }
            ),
            RowAsync(
                "session-start-resume-sends-resume-prompt-and-updates-snapshot",
                "StartResumeAsync は画面の一覧とスキーマの変更の注記（resx）を含む再開のプロンプトを送り、成功したターンでスナップショットを現在のスキーマへ更新する",
                MockFolderEntry.SessionStart,
                MockFolderOutcome.Succeeds,
                async h =>
                {
                    h.CreateNew(schema: "# 旧スキーマ");
                    h.SaveScreen(
                        "OrderList.html",
                        "注文一覧",
                        "注文の一覧",
                        ScreenHtml("<h1>注文一覧</h1>"),
                        None,
                        "初版"
                    );
                    h.CreateSession();

                    await h.StartResumeAsync(Diagram("Orders", "注文"));

                    var prompt = h.Engine.SentPrompts.Should().ContainSingle().Which;
                    prompt.Should().Contain("OrderList.html");
                    prompt.Should().Contain("Orders");
                    prompt.Should().Contain(MockStrings.Mock_ResumePromptSchemaChangedNote);
                    var reopened = h.Reopen();
                    reopened.SourceSchema.Should().Contain("Orders");
                    reopened.SourceSchema.Should().NotContain("旧スキーマ");
                }
            ),
            RowAsync(
                "session-feedback-forwards-to-engine-and-relays-events",
                "フィードバックはそのままエンジンへ送り、応答の断片とターンの完了を転送する",
                MockFolderEntry.SessionFeedback,
                MockFolderOutcome.Succeeds,
                async h =>
                {
                    Start(h);

                    await h.SendFeedbackAsync("列を減らして");

                    h.Engine.SentPrompts.Should().ContainSingle().Which.Should().Be("列を減らして");
                    h.ForwardedDeltas.Should().Contain("了解しました。");
                    h.ForwardedTurns.Should().ContainSingle().Which.Success.Should().BeTrue();
                }
            ),
            RowAsync(
                "session-start-resume-send-exception-keeps-schema-changed-marker",
                "再開のプロンプトの送信が例外で終わったら、スナップショットを更新せず「スキーマの変更あり」の印を残す",
                MockFolderEntry.SessionStart,
                MockFolderOutcome.Refused,
                async h =>
                {
                    Start(h, "# 旧スキーマ");
                    h.Engine.SendException = new InvalidOperationException("API key is not set.");
                    var diagram = Diagram("Orders");

                    var act = () => h.StartResumeAsync(diagram);

                    await act.Should().ThrowAsync<InvalidOperationException>();
                    h.Store.Manifest.SourceSchema.Should().Be("# 旧スキーマ");
                    h.Reopen().SourceSchema.Should().Be("# 旧スキーマ");
                    MockResumePrompt
                        .IsSchemaChanged(MockSchemaSerializer.Serialize(diagram), h.Store.Manifest)
                        .Should()
                        .BeTrue();
                }
            ),
            .. new[] { ("failed", "session failed"), ("interrupted", (string?)null) }.SelectMany(
                c =>
                    new[]
                    {
                        RowAsync(
                            $"session-start-new-turn-{c.Item1}-does-not-snapshot-until-success",
                            $"新規の初回ターンが {c.Item1}（失敗・中断）で終わったらスナップショットを保存せず、やり直しの初回ターンの成功で保存する",
                            MockFolderEntry.SessionStart,
                            MockFolderOutcome.Refused,
                            async h =>
                            {
                                Start(h);
                                h.Engine.HoldTurnCompletion = true;
                                var diagram = Diagram("Customer");

                                await h.StartNewAsync(diagram);

                                h.Store.Manifest.SourceSchema.Should()
                                    .BeEmpty("ターンが終わるまでは保存しない");

                                h.FailHeldTurn(c.Item2);

                                h.Store.Manifest.SourceSchema.Should()
                                    .BeEmpty("失敗・中断したターンでは保存しない");
                                h.Reopen().SourceSchema.Should().BeEmpty();

                                await h.StartNewAsync(diagram);
                                h.CompleteHeldTurn();

                                h.Reopen().SourceSchema.Should().Contain("Customer");
                            }
                        ),
                        RowAsync(
                            $"session-start-resume-turn-{c.Item1}-keeps-schema-changed-marker",
                            $"再開の初回ターンが {c.Item1}（失敗・中断）で終わったら「スキーマの変更あり」の印を残し、やり直しの初回ターンの成功で更新する",
                            MockFolderEntry.SessionStart,
                            MockFolderOutcome.Refused,
                            async h =>
                            {
                                Start(h, "# 旧スキーマ");
                                h.Engine.HoldTurnCompletion = true;
                                var diagram = Diagram("Orders");

                                await h.StartResumeAsync(diagram);

                                h.Store.Manifest.SourceSchema.Should()
                                    .Be("# 旧スキーマ", "ターンが終わるまでは更新しない");

                                h.FailHeldTurn(c.Item2);

                                h.Store.Manifest.SourceSchema.Should().Be("# 旧スキーマ");
                                h.Reopen().SourceSchema.Should().Be("# 旧スキーマ");
                                MockResumePrompt
                                    .IsSchemaChanged(
                                        MockSchemaSerializer.Serialize(diagram),
                                        h.Store.Manifest
                                    )
                                    .Should()
                                    .BeTrue(
                                        "変更を AI へ伝えられなかったので、次の再開でも変更ありと伝える"
                                    );

                                await h.StartResumeAsync(diagram);
                                h.CompleteHeldTurn();

                                h.Reopen().SourceSchema.Should().Contain("Orders");
                            }
                        ),
                    }
            ),
            RowAsync(
                "session-feedback-after-failed-initial-turn-does-not-snapshot",
                "初回ターンが失敗した後のフィードバックのターンの成功は、スナップショットを確定しない",
                MockFolderEntry.SessionFeedback,
                MockFolderOutcome.Succeeds,
                async h =>
                {
                    Start(h, "# 旧スキーマ");
                    h.Engine.HoldTurnCompletion = true;

                    await h.StartResumeAsync(Diagram("Orders"));
                    h.FailHeldTurn("session failed");
                    await h.SendFeedbackAsync("続けて");
                    h.CompleteHeldTurn();

                    h.Store.Manifest.SourceSchema.Should().Be("# 旧スキーマ");
                }
            ),
            RowAsync(
                "session-feedback-after-initial-send-exception-does-not-snapshot",
                "初回の送信が例外で終わった後のフィードバックのターンの成功は、スナップショットを確定しない",
                MockFolderEntry.SessionFeedback,
                MockFolderOutcome.Succeeds,
                async h =>
                {
                    Start(h, "# 旧スキーマ");
                    h.Engine.SendException = new InvalidOperationException("API key is not set.");

                    var act = () => h.StartResumeAsync(Diagram("Orders"));
                    await act.Should().ThrowAsync<InvalidOperationException>();

                    h.Engine.SendException = null;
                    await h.SendFeedbackAsync("続けて");

                    h.Store.Manifest.SourceSchema.Should().Be("# 旧スキーマ");
                }
            ),
            RowAsync(
                "session-feedback-turn-failed-leaves-snapshot",
                "初回ターンの成功の後、フィードバックのターンが失敗しても、スナップショットは初回で保存したまま変わらず、失敗を 1 回だけ転送する",
                MockFolderEntry.SessionFeedback,
                MockFolderOutcome.Refused,
                async h =>
                {
                    Start(h, "# 旧スキーマ");
                    await h.StartResumeAsync(Diagram("Orders"));
                    var saved = h.Store.Manifest.SourceSchema;
                    h.Engine.HoldTurnCompletion = true;

                    await h.SendFeedbackAsync("続けて");
                    h.FailHeldTurn("session failed");

                    h.Store.Manifest.SourceSchema.Should().Be(saved).And.Contain("Orders");
                    h.ForwardedTurns.Select(t => t.Success).Should().Equal(true, false);
                }
            ),
            RowAsync(
                "session-start-snapshot-write-failure-forwards-failed-turn",
                "初回ターンが成功してもスナップショットを書けなければ、そのターンを失敗（理由つき）として転送し、メモリのスキーマを変えない",
                MockFolderEntry.SessionStart,
                MockFolderOutcome.IoFailure,
                async h =>
                {
                    Start(h, "# 旧スキーマ");
                    h.Engine.HoldTurnCompletion = true;

                    await h.StartResumeAsync(Diagram("Orders"));

                    using (h.ManifestReadOnly())
                    {
                        h.CompleteHeldTurn();
                    }

                    var completed = h.ForwardedTurns.Should().ContainSingle().Which;
                    completed.Success.Should().BeFalse();
                    completed.Error.Should().NotBeNullOrWhiteSpace();
                    h.Store.Manifest.SourceSchema.Should().Be("# 旧スキーマ");
                }
            ),
        ];
}
