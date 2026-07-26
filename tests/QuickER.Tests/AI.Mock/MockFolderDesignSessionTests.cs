using System.IO;
using System.Text.Json;
using FluentAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Model;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockFolderDesignSession"/> の会話開始（新規／再開）とツール実行（save_screen /
/// remove_screen / save_stylesheet / get_screen）・イベント発火・ファイル/マニフェスト更新を検証するテストクラス。
/// </summary>
public class MockFolderDesignSessionTests : IDisposable
{
    private readonly string _folder;

    private static readonly DateTimeOffset FixedNow = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

    public MockFolderDesignSessionTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private MockFolderStore CreateStore(string title = "受注管理", string schema = "") =>
        MockFolderStore.CreateNew(_folder, title, schema, () => FixedNow);

    /// <summary>ファクトリ構築（セッション自身がツールホスト）で新しいセッションを作る</summary>
    private static (MockFolderDesignSession Session, FakeChatEngine Engine) CreateSession(
        MockFolderStore store
    )
    {
        FakeChatEngine? captured = null;
        var session = new MockFolderDesignSession(
            toolHost => captured = new FakeChatEngine(toolHost),
            store
        );

        return (session, captured!);
    }

    private static string ScreenHtml(string body, bool withStylesheet = true)
    {
        var link = withStylesheet ? "<link rel=\"stylesheet\" href=\"style.css\">" : string.Empty;

        return $"<!DOCTYPE html><html><head>{link}</head><body>{body}</body></html>";
    }

    private static string SaveScreenArgs(
        string file,
        string name,
        string html,
        string? description = null,
        object[]? transitions = null,
        string? revisionNote = null,
        object[]? entities = null
    )
    {
        var payload = new Dictionary<string, object?>
        {
            ["file"] = file,
            ["name"] = name,
            ["html"] = html,
        };

        if (description is not null)
        {
            payload["description"] = description;
        }

        if (transitions is not null)
        {
            payload["transitions"] = transitions;
        }

        if (revisionNote is not null)
        {
            payload["revision_note"] = revisionNote;
        }

        if (entities is not null)
        {
            payload["entities"] = entities;
        }

        return JsonSerializer.Serialize(payload);
    }

    // --- 会話開始 --------------------------------------------------------

    /// <summary>StartNewAsync がスキーマ＋補足指示を含む初回プロンプトを送り、スナップショットを保存することを検証する</summary>
    [Fact(DisplayName = "StartNewAsync はスキーマ＋補足指示を送りスナップショット保存")]
    public async Task StartNewAsync_SendsSchemaAndInstructions_AndSnapshots()
    {
        var store = CreateStore(schema: string.Empty);
        var (session, engine) = CreateSession(store);

        var diagram = new ErDiagram
        {
            Entities =
            {
                new Entity { TableName = "Customer", Description = "顧客" },
            },
        };

        await session.StartNewAsync(
            diagram,
            "モダンな配色にして",
            null,
            TestContext.Current.CancellationToken
        );

        engine.SentPrompts.Should().ContainSingle();
        engine.SentPrompts[0].Should().Contain("Customer");
        engine.SentPrompts[0].Should().Contain("モダンな配色にして");

        // スキーマスナップショットが空だったので現在スキーマで保存される（mock.json を実ファイルで確認）
        var reopened = MockFolderStore.Open(_folder);
        reopened.Manifest.SourceSchema.Should().Contain("Customer");
    }

    /// <summary>StartResumeAsync が画面一覧を含む再開プロンプトを送り、スキーマ差異注記を含み、スナップショットを更新することを検証する</summary>
    [Fact(DisplayName = "StartResumeAsync は画面一覧・差異注記を送りスナップショット更新")]
    public async Task StartResumeAsync_SendsResumePrompt_AndUpdatesSnapshot()
    {
        // 旧スキーマで作成し、画面を 1 つ入れておく
        var store = CreateStore(schema: "# 旧スキーマ");
        store.SaveScreen(
            "OrderList.html",
            "注文一覧",
            "注文の一覧",
            ScreenHtml("<h1>注文一覧</h1>"),
            Array.Empty<MockTransition>(),
            "初版"
        );

        var (session, engine) = CreateSession(store);

        var diagram = new ErDiagram
        {
            Entities =
            {
                new Entity { TableName = "Orders", Description = "注文" },
            },
        };

        await session.StartResumeAsync(diagram, null, null, TestContext.Current.CancellationToken);

        engine.SentPrompts.Should().ContainSingle();
        var prompt = engine.SentPrompts[0];
        prompt.Should().Contain("OrderList.html");
        prompt.Should().Contain("Orders");
        // 旧スキーマ != 現在スキーマなので差異注記が入る
        prompt.Should().Contain("スキーマが変更されています");

        // 送信前にスナップショットが現在スキーマへ更新される
        var reopened = MockFolderStore.Open(_folder);
        reopened.Manifest.SourceSchema.Should().Contain("Orders");
        reopened.Manifest.SourceSchema.Should().NotContain("旧スキーマ");
    }

    // --- save_screen -----------------------------------------------------

    /// <summary>save_screen 成功でファイル書き込み・マニフェスト upsert・ScreenSaved 発火することを検証する</summary>
    [Fact(DisplayName = "save_screen 成功でファイル書込・upsert・ScreenSaved 発火")]
    public void Execute_SaveScreen_WritesFile_Upserts_RaisesEvent()
    {
        var store = CreateStore();
        var (session, _) = CreateSession(store);

        MockScreenSavedEventArgs? saved = null;
        session.ScreenSaved += (_, e) => saved = e;

        var args = SaveScreenArgs(
            "OrderList.html",
            "注文一覧",
            ScreenHtml("<h1>注文一覧</h1>"),
            description: "注文の一覧",
            revisionNote: "初版"
        );

        var (result, success) = session.Execute(MockFolderDesignTools.SaveScreenToolName, args);

        success.Should().BeTrue();
        result.Should().Contain("OrderList.html");

        File.Exists(Path.Combine(_folder, "OrderList.html")).Should().BeTrue();

        var reopened = MockFolderStore.Open(_folder);
        reopened.Manifest.Screens.Should().ContainSingle();
        reopened.Manifest.Screens[0].File.Should().Be("OrderList.html");
        reopened.Manifest.Screens[0].Name.Should().Be("注文一覧");

        saved.Should().NotBeNull();
        saved!.File.Should().Be("OrderList.html");
        saved.RevisionNote.Should().Be("初版");
    }

    /// <summary>外部参照を含む画面で警告が「Warnings:」として結果に連結されることを検証する</summary>
    [Fact(DisplayName = "save_screen の警告が Warnings: として連結される")]
    public void Execute_SaveScreen_AppendsWarnings()
    {
        var store = CreateStore();
        var (session, _) = CreateSession(store);

        MockScreenSavedEventArgs? saved = null;
        session.ScreenSaved += (_, e) => saved = e;

        // style.css を参照せず外部リソースを参照する画面（機械検証で警告が付く）
        var html =
            "<!DOCTYPE html><html><head></head>"
            + "<body><img src=\"https://example.com/logo.png\"></body></html>";
        var args = SaveScreenArgs("Bad.html", "不正", html);

        var (result, success) = session.Execute(MockFolderDesignTools.SaveScreenToolName, args);

        success.Should().BeTrue();
        result.Should().Contain("Warnings:");
        saved!.Warnings.Should().NotBeEmpty();
    }

    /// <summary>html が空/非 HTML のとき失敗結果を返し、例外が外へ漏れないことを検証する</summary>
    [Fact(DisplayName = "save_screen の不正 HTML は失敗結果（例外は漏れない）")]
    public void Execute_SaveScreen_InvalidHtml_ReturnsFailure()
    {
        var store = CreateStore();
        var (session, _) = CreateSession(store);

        var raised = false;
        session.ScreenSaved += (_, _) => raised = true;

        var args = SaveScreenArgs("Frag.html", "断片", "<div>部分</div>");

        var (_, success) = session.Execute(MockFolderDesignTools.SaveScreenToolName, args);

        success.Should().BeFalse();
        raised.Should().BeFalse();
        File.Exists(Path.Combine(_folder, "Frag.html")).Should().BeFalse();
    }

    /// <summary>transitions が From＝当該画面へ変換され、マニフェストに格納されることを検証する</summary>
    [Fact(DisplayName = "save_screen の transitions は From=当該画面に変換される")]
    public void Execute_SaveScreen_TransitionsConverted_WithFromSetToFile()
    {
        var store = CreateStore();
        var (session, _) = CreateSession(store);

        var transitions = new object[] { new { to = "OrderDetail.html", trigger = "行クリック" } };
        var args = SaveScreenArgs(
            "OrderList.html",
            "注文一覧",
            ScreenHtml("<a href=\"OrderDetail.html\">詳細</a>"),
            transitions: transitions
        );

        session.Execute(MockFolderDesignTools.SaveScreenToolName, args);

        var reopened = MockFolderStore.Open(_folder);
        reopened.Manifest.Transitions.Should().ContainSingle();
        var t = reopened.Manifest.Transitions[0];
        t.From.Should().Be("OrderList.html");
        t.To.Should().Be("OrderDetail.html");
        t.Trigger.Should().Be("行クリック");
    }

    // --- save_screen entities（画面×エンティティ CRUD） -----------------

    /// <summary>entities 引数がストアへ届き、正規化されてマニフェストに記録されることを検証する</summary>
    [Fact(DisplayName = "save_screen の entities は正規化されて記録される")]
    public void Execute_SaveScreen_EntitiesRecorded_Normalized()
    {
        var store = CreateStore();
        var (session, _) = CreateSession(store);

        var entities = new object[]
        {
            new { name = "Order", operations = "urc" },
            new { name = "Customer", operations = "r" },
        };
        var args = SaveScreenArgs(
            "OrderList.html",
            "注文一覧",
            ScreenHtml("<h1>一覧</h1>"),
            entities: entities
        );

        var (_, success) = session.Execute(MockFolderDesignTools.SaveScreenToolName, args);

        success.Should().BeTrue();

        var reopened = MockFolderStore.Open(_folder);
        var recorded =
            reopened.Manifest.Screens.Single(s => s.File == "OrderList.html").Entities
            ?? new List<MockScreenEntity>();
        recorded
            .Select(e => (e.Name, e.Operations))
            .Should()
            .BeEquivalentTo(new[] { ("Order", "CRU"), ("Customer", "R") });
    }

    /// <summary>entities 省略で既存宣言が維持されることを検証する</summary>
    [Fact(DisplayName = "save_screen の entities 省略で既存宣言を維持")]
    public void Execute_SaveScreen_OmittedEntities_KeepsExisting()
    {
        var store = CreateStore();
        var (session, _) = CreateSession(store);

        // 初回で宣言してから、entities を省略して再保存する
        session.Execute(
            MockFolderDesignTools.SaveScreenToolName,
            SaveScreenArgs(
                "OrderList.html",
                "注文一覧",
                ScreenHtml("<h1>v1</h1>"),
                entities: new object[] { new { name = "Order", operations = "CRUD" } }
            )
        );
        session.Execute(
            MockFolderDesignTools.SaveScreenToolName,
            SaveScreenArgs("OrderList.html", "注文一覧", ScreenHtml("<h1>v2</h1>"))
        );

        var reopened = MockFolderStore.Open(_folder);
        var recorded =
            reopened.Manifest.Screens.Single(s => s.File == "OrderList.html").Entities
            ?? new List<MockScreenEntity>();
        recorded.Should().ContainSingle();
        recorded[0].Name.Should().Be("Order");
        recorded[0].Operations.Should().Be("CRUD");
    }

    /// <summary>正規化で操作が空になった宣言が警告されることを検証する</summary>
    [Fact(DisplayName = "save_screen 正規化空エントリで警告")]
    public void Execute_SaveScreen_DiscardedEntity_Warns()
    {
        var store = CreateStore();
        var (session, _) = CreateSession(store);

        MockScreenSavedEventArgs? saved = null;
        session.ScreenSaved += (_, e) => saved = e;

        var args = SaveScreenArgs(
            "OrderList.html",
            "注文一覧",
            ScreenHtml("<h1>一覧</h1>"),
            entities: new object[] { new { name = "Junk", operations = "zzz" } }
        );

        var (result, success) = session.Execute(MockFolderDesignTools.SaveScreenToolName, args);

        success.Should().BeTrue();
        result.Should().Contain("Warnings:");
        saved!.Warnings.Should().Contain(w => w.Contains("Junk") && w.Contains("CRUD"));
    }

    /// <summary>会話開始後、図に存在しないエンティティ名の宣言が警告されることを検証する</summary>
    [Fact(DisplayName = "save_screen 図に無いエンティティ名で警告")]
    public async Task Execute_SaveScreen_UnknownEntity_Warns()
    {
        var store = CreateStore(schema: string.Empty);
        var (session, _) = CreateSession(store);

        MockScreenSavedEventArgs? saved = null;
        session.ScreenSaved += (_, e) => saved = e;

        // 図に Order だけがある状態で会話開始（照合用の名前集合を取り込む）
        var diagram = new ErDiagram { Entities = { new Entity { TableName = "Order" } } };
        await session.StartNewAsync(diagram, null, null, TestContext.Current.CancellationToken);

        var args = SaveScreenArgs(
            "OrderList.html",
            "注文一覧",
            ScreenHtml("<h1>一覧</h1>"),
            entities: new object[]
            {
                new { name = "Order", operations = "r" },
                new { name = "Ghost", operations = "r" },
            }
        );

        var (_, success) = session.Execute(MockFolderDesignTools.SaveScreenToolName, args);

        success.Should().BeTrue();
        // Order は図に在る＝警告なし・Ghost は図に無い＝警告
        saved!.Warnings.Should().Contain(w => w.Contains("Ghost"));
        saved.Warnings.Should().NotContain(w => w.Contains("'Order'"));
    }

    /// <summary>名前が空の壊れた宣言要素が読み飛ばされ警告されることを検証する</summary>
    [Fact(DisplayName = "save_screen 名前空の壊れた宣言で警告し読み飛ばす")]
    public void Execute_SaveScreen_BrokenEntity_WarnsAndSkips()
    {
        var store = CreateStore();
        var (session, _) = CreateSession(store);

        MockScreenSavedEventArgs? saved = null;
        session.ScreenSaved += (_, e) => saved = e;

        var args = SaveScreenArgs(
            "OrderList.html",
            "注文一覧",
            ScreenHtml("<h1>一覧</h1>"),
            entities: new object[]
            {
                new { name = "", operations = "cr" },
                new { name = "Order", operations = "r" },
            }
        );

        session.Execute(MockFolderDesignTools.SaveScreenToolName, args);

        saved!.Warnings.Should().Contain(w => w.Contains("name") && w.Contains("empty"));

        var reopened = MockFolderStore.Open(_folder);
        var recorded =
            reopened.Manifest.Screens.Single(s => s.File == "OrderList.html").Entities
            ?? new List<MockScreenEntity>();
        // 壊れた要素は除外され Order だけ残る
        recorded.Should().ContainSingle();
        recorded[0].Name.Should().Be("Order");
    }

    // --- remove_screen ---------------------------------------------------

    /// <summary>remove_screen が存在する画面を削除し ScreenRemoved を発火することを検証する</summary>
    [Fact(DisplayName = "remove_screen で削除・ScreenRemoved 発火")]
    public void Execute_RemoveScreen_Existing_Removes_RaisesEvent()
    {
        var store = CreateStore();
        store.SaveScreen(
            "OrderList.html",
            "注文一覧",
            "注文の一覧",
            ScreenHtml("<h1>一覧</h1>"),
            Array.Empty<MockTransition>(),
            "初版"
        );

        var (session, _) = CreateSession(store);

        string? removed = null;
        session.ScreenRemoved += (_, f) => removed = f;

        var (_, success) = session.Execute(
            MockFolderDesignTools.RemoveScreenToolName,
            "{\"file\":\"OrderList.html\"}"
        );

        success.Should().BeTrue();
        removed.Should().Be("OrderList.html");
        File.Exists(Path.Combine(_folder, "OrderList.html")).Should().BeFalse();
    }

    /// <summary>存在しない画面の remove_screen は失敗し、利用可能な画面一覧を添えることを検証する</summary>
    [Fact(DisplayName = "remove_screen 非存在は失敗（画面一覧付き）")]
    public void Execute_RemoveScreen_Missing_ReturnsFailureWithScreenList()
    {
        var store = CreateStore();
        store.SaveScreen(
            "OrderList.html",
            "注文一覧",
            "注文の一覧",
            ScreenHtml("<h1>一覧</h1>"),
            Array.Empty<MockTransition>(),
            "初版"
        );

        var (session, _) = CreateSession(store);

        var (result, success) = session.Execute(
            MockFolderDesignTools.RemoveScreenToolName,
            "{\"file\":\"Missing.html\"}"
        );

        success.Should().BeFalse();
        result.Should().Contain("OrderList.html");
    }

    // --- save_stylesheet -------------------------------------------------

    /// <summary>save_stylesheet がファイルを書き、StylesheetSaved を発火することを検証する</summary>
    [Fact(DisplayName = "save_stylesheet で書込・StylesheetSaved 発火")]
    public void Execute_SaveStylesheet_WritesFile_RaisesEvent()
    {
        var store = CreateStore();
        var (session, _) = CreateSession(store);

        MockStylesheetSavedEventArgs? saved = null;
        session.StylesheetSaved += (_, e) => saved = e;

        var (_, success) = session.Execute(
            MockFolderDesignTools.SaveStylesheetToolName,
            "{\"css\":\"body{margin:0}\",\"revision_note\":\"初版CSS\"}"
        );

        success.Should().BeTrue();
        File.Exists(Path.Combine(_folder, MockManifest.StylesheetFileName)).Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.RevisionNote.Should().Be("初版CSS");
    }

    // --- get_screen ------------------------------------------------------

    /// <summary>get_screen が存在する画面の HTML 全文を返すことを検証する</summary>
    [Fact(DisplayName = "get_screen は存在画面の HTML を返す")]
    public void Execute_GetScreen_Existing_ReturnsHtml()
    {
        var store = CreateStore();
        var html = ScreenHtml("<h1>注文一覧</h1>");
        store.SaveScreen(
            "OrderList.html",
            "注文一覧",
            "注文の一覧",
            html,
            Array.Empty<MockTransition>(),
            "初版"
        );

        var (session, _) = CreateSession(store);

        var (result, success) = session.Execute(
            MockFolderDesignTools.GetScreenToolName,
            "{\"file\":\"OrderList.html\"}"
        );

        success.Should().BeTrue();
        result.Should().Be(html);
    }

    /// <summary>get_screen が非存在画面で失敗し、利用可能な画面一覧を添えることを検証する</summary>
    [Fact(DisplayName = "get_screen 非存在は失敗（画面一覧付き）")]
    public void Execute_GetScreen_Missing_ReturnsFailureWithScreenList()
    {
        var store = CreateStore();
        store.SaveScreen(
            "OrderList.html",
            "注文一覧",
            "注文の一覧",
            ScreenHtml("<h1>一覧</h1>"),
            Array.Empty<MockTransition>(),
            "初版"
        );

        var (session, _) = CreateSession(store);

        var (result, success) = session.Execute(
            MockFolderDesignTools.GetScreenToolName,
            "{\"file\":\"Missing.html\"}"
        );

        success.Should().BeFalse();
        result.Should().Contain("OrderList.html");
    }

    // --- 未知ツール ------------------------------------------------------

    /// <summary>未知のツール名は失敗結果を返すことを検証する</summary>
    [Fact(DisplayName = "未知ツールは失敗結果")]
    public void Execute_UnknownTool_ReturnsFailure()
    {
        var store = CreateStore();
        var (session, _) = CreateSession(store);

        var (result, success) = session.Execute("no_such_tool", "{}");

        success.Should().BeFalse();
        result.Should().Contain("no_such_tool");
    }

    // --- Store 公開 ------------------------------------------------------

    /// <summary>Store プロパティが渡したストアを公開することを検証する</summary>
    [Fact(DisplayName = "Store プロパティが渡したストアを公開する")]
    public void Store_ExposesInjectedStore()
    {
        var store = CreateStore();
        var (session, _) = CreateSession(store);

        session.Store.Should().BeSameAs(store);
    }

    // --- エンジンイベント転送 -------------------------------------------

    /// <summary>フィードバックがエンジンへ送信され、エンジンイベントが転送されることを検証する</summary>
    [Fact(DisplayName = "フィードバックはエンジンへ送信されイベント転送")]
    public async Task SendFeedbackAsync_ForwardsToEngine_AndRelaysEvents()
    {
        var store = CreateStore();
        var (session, engine) = CreateSession(store);

        var deltas = new List<string>();
        ErChatTurnResult? completed = null;
        session.AssistantDeltaReceived += (_, d) => deltas.Add(d);
        session.TurnCompleted += (_, r) => completed = r;

        await session.SendFeedbackAsync(
            "列を減らして",
            cancellationToken: TestContext.Current.CancellationToken
        );

        engine.SentPrompts.Should().ContainSingle().Which.Should().Be("列を減らして");
        deltas.Should().Contain("了解しました。");
        completed!.Value.Success.Should().BeTrue();
    }
}
