using System.IO;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockFolderStore"/> のマニフェスト往復・画面 upsert・削除・検証委譲・例外を検証するテストクラス。
/// </summary>
public class MockFolderStoreTests : IDisposable
{
    private readonly string _folder;

    /// <summary>固定時刻（改訂履歴のタイムスタンプを決定的にする）</summary>
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

    public MockFolderStoreTests()
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

    private MockFolderStore CreateNew(string title = "受注管理", string schema = "# schema") =>
        MockFolderStore.CreateNew(_folder, title, schema, () => FixedNow);

    private static string ScreenHtml(string body, bool withStylesheet = true, string bodyExtra = "")
    {
        var link = withStylesheet ? "<link rel=\"stylesheet\" href=\"style.css\">" : string.Empty;

        return $"<!DOCTYPE html><html><head>{link}</head><body>{body}{bodyExtra}</body></html>";
    }

    [Fact(DisplayName = "IsMockFolder は mock.json の有無を返す")]
    public void IsMockFolder_ReflectsManifestPresence()
    {
        MockFolderStore.IsMockFolder(_folder).Should().BeFalse();

        CreateNew();

        MockFolderStore.IsMockFolder(_folder).Should().BeTrue();
    }

    [Fact(DisplayName = "CreateNew→Open でマニフェスト内容が一致する")]
    public void CreateNew_ThenOpen_RoundTrips()
    {
        CreateNew(title: "受注管理", schema: "# データベーススキーマ");

        var reopened = MockFolderStore.Open(_folder);

        reopened.Manifest.Version.Should().Be(MockManifest.CurrentVersion);
        reopened.Manifest.Title.Should().Be("受注管理");
        reopened.Manifest.SourceSchema.Should().Be("# データベーススキーマ");
        reopened.Manifest.Screens.Should().BeEmpty();
    }

    [Fact(DisplayName = "mock.json は camelCase・日本語非エスケープで書かれる")]
    public void Manifest_IsCamelCaseAndUnescapedJapanese()
    {
        CreateNew(title: "受注管理", schema: "スキーマ");

        var json = File.ReadAllText(Path.Combine(_folder, "mock.json"));

        json.Should().Contain("\"version\"");
        json.Should().Contain("\"sourceSchema\"");
        json.Should().Contain("受注管理");
        // Unicode エスケープされていないこと
        json.Should().NotContain("\\u53d7");
    }

    [Fact(DisplayName = "mock.json は BOM なし UTF-8 で書かれる")]
    public void Manifest_HasNoBom()
    {
        CreateNew();

        var bytes = File.ReadAllBytes(Path.Combine(_folder, "mock.json"));

        (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            .Should()
            .BeFalse();
    }

    [Fact(DisplayName = "CreateNew は既存フォルダで InvalidOperationException")]
    public void CreateNew_OnExisting_Throws()
    {
        CreateNew();

        var act = () => CreateNew();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact(DisplayName = "Open は mock.json 不在で明確な例外")]
    public void Open_MissingManifest_Throws()
    {
        var act = () => MockFolderStore.Open(_folder);

        act.Should().Throw<InvalidOperationException>().WithMessage("*mock.json*");
    }

    [Fact(DisplayName = "Open は破損 JSON で明確な例外")]
    public void Open_CorruptManifest_Throws()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "mock.json"), "{ this is not json");

        var act = () => MockFolderStore.Open(_folder);

        act.Should().Throw<InvalidOperationException>().WithMessage("*解釈できませんでした*");
    }

    [Fact(DisplayName = "Open は新フォーマット（Version 超過）で明確な例外")]
    public void Open_NewerFormat_Throws()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(
            Path.Combine(_folder, "mock.json"),
            "{ \"version\": 99, \"title\": \"x\" }"
        );

        var act = () => MockFolderStore.Open(_folder);

        act.Should().Throw<InvalidOperationException>().WithMessage("*version*");
    }

    [Fact(DisplayName = "SaveScreen は画面を追加し HTML を書き出す")]
    public void SaveScreen_AddsScreenAndWritesHtml()
    {
        var store = CreateNew();

        store.SaveScreen(
            "OrderList.html",
            "注文一覧",
            "注文の一覧画面",
            ScreenHtml("<h1>注文一覧</h1>"),
            Array.Empty<MockTransition>(),
            "初版"
        );

        File.Exists(Path.Combine(_folder, "OrderList.html")).Should().BeTrue();

        var manifest = store.Manifest;
        manifest.Screens.Should().ContainSingle();
        manifest.Screens[0].File.Should().Be("OrderList.html");
        manifest.Screens[0].Name.Should().Be("注文一覧");
        manifest.Revisions.Should().ContainSingle(r => r.Note == "初版");
        manifest.Revisions[0].Timestamp.Should().Be(FixedNow);
    }

    [Fact(DisplayName = "SaveScreen は同名 file を大文字小文字無視で upsert する")]
    public void SaveScreen_UpsertsCaseInsensitively()
    {
        var store = CreateNew();

        store.SaveScreen(
            "OrderList.html",
            "旧名",
            "旧説明",
            ScreenHtml("<h1>v1</h1>"),
            Array.Empty<MockTransition>(),
            "v1"
        );
        store.SaveScreen(
            "orderlist.html",
            "新名",
            "新説明",
            ScreenHtml("<h1>v2</h1>"),
            Array.Empty<MockTransition>(),
            "v2"
        );

        var manifest = store.Manifest;
        manifest.Screens.Should().ContainSingle();
        manifest.Screens[0].Name.Should().Be("新名");
        manifest.Screens[0].Description.Should().Be("新説明");
        manifest.Revisions.Should().HaveCount(2);
    }

    [Fact(DisplayName = "SaveScreen は同起点の遷移を差し替える")]
    public void SaveScreen_ReplacesTransitionsFromSameScreen()
    {
        var store = CreateNew();

        store.SaveScreen(
            "OrderList.html",
            "一覧",
            "",
            ScreenHtml("<a href=\"OrderDetail.html\">詳細</a>"),
            new[]
            {
                new MockTransition
                {
                    From = "OrderList.html",
                    To = "OrderDetail.html",
                    Trigger = "行クリック",
                },
            },
            "v1"
        );

        // 差し替え: 新しい遷移一式で上書き
        store.SaveScreen(
            "OrderList.html",
            "一覧",
            "",
            ScreenHtml("<a href=\"OrderNew.html\">新規</a>"),
            new[]
            {
                new MockTransition
                {
                    From = "OrderList.html",
                    To = "OrderNew.html",
                    Trigger = "ボタン",
                },
            },
            "v2"
        );

        var manifest = store.Manifest;
        manifest.Transitions.Should().ContainSingle();
        manifest.Transitions[0].To.Should().Be("OrderNew.html");
    }

    [Fact(DisplayName = "SaveScreen は不正なファイル名で ArgumentException")]
    public void SaveScreen_InvalidFileName_Throws()
    {
        var store = CreateNew();
        var html = ScreenHtml("<h1>x</h1>");

        var cases = new[] { "sub/Order.html", "..\\Order.html", "Order.txt", "" };

        foreach (var file in cases)
        {
            var act = () =>
                store.SaveScreen(file, "n", "d", html, Array.Empty<MockTransition>(), "note");
            act.Should().Throw<ArgumentException>();
        }
    }

    [Fact(DisplayName = "SaveScreen は空・非 HTML で ArgumentException")]
    public void SaveScreen_EmptyOrNonHtml_Throws()
    {
        var store = CreateNew();

        var empty = () =>
            store.SaveScreen("A.html", "n", "d", "   ", Array.Empty<MockTransition>(), "note");
        empty.Should().Throw<ArgumentException>();

        var nonHtml = () =>
            store.SaveScreen(
                "A.html",
                "n",
                "d",
                "<div>断片のみ</div>",
                Array.Empty<MockTransition>(),
                "note"
            );
        nonHtml.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "RemoveScreen は HTML と From/To 両方向の遷移を除去する")]
    public void RemoveScreen_RemovesHtmlAndBothDirectionTransitions()
    {
        var store = CreateNew();

        store.SaveScreen(
            "OrderList.html",
            "一覧",
            "",
            ScreenHtml("<a href=\"OrderDetail.html\">詳細</a>"),
            new[]
            {
                new MockTransition { From = "OrderList.html", To = "OrderDetail.html" },
            },
            "v1"
        );
        store.SaveScreen(
            "OrderDetail.html",
            "詳細",
            "",
            ScreenHtml("<a href=\"OrderList.html\">戻る</a>"),
            new[]
            {
                new MockTransition { From = "OrderDetail.html", To = "OrderList.html" },
            },
            "v2"
        );

        store.RemoveScreen("OrderDetail.html");

        File.Exists(Path.Combine(_folder, "OrderDetail.html")).Should().BeFalse();

        var manifest = store.Manifest;
        manifest.Screens.Should().ContainSingle(s => s.File == "OrderList.html");
        // From=OrderDetail も To=OrderDetail も消える
        manifest.Transitions.Should().BeEmpty();
    }

    [Fact(DisplayName = "RemoveScreen は存在しない画面で InvalidOperationException")]
    public void RemoveScreen_Missing_Throws()
    {
        var store = CreateNew();

        var act = () => store.RemoveScreen("Nope.html");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact(DisplayName = "SaveStylesheet は style.css を書き出し改訂を追記する")]
    public void SaveStylesheet_WritesCssAndRevision()
    {
        var store = CreateNew();

        store.HasStylesheet.Should().BeFalse();

        var warnings = store.SaveStylesheet("body { color: #333; }", "スタイル初版");

        store.HasStylesheet.Should().BeTrue();
        store.GetStylesheet().Should().Be("body { color: #333; }");
        store.Manifest.Revisions.Should().ContainSingle(r => r.Note == "スタイル初版");
        warnings.Should().BeEmpty();
    }

    [Fact(DisplayName = "GetScreenHtml は未存在で null を返す")]
    public void GetScreenHtml_Missing_ReturnsNull()
    {
        var store = CreateNew();

        store.GetScreenHtml("Nope.html").Should().BeNull();
    }

    [Fact(DisplayName = "UpdateSourceSchema はスナップショットを更新して保存する")]
    public void UpdateSourceSchema_UpdatesAndPersists()
    {
        var store = CreateNew(schema: "旧スキーマ");

        store.UpdateSourceSchema("新スキーマ");

        MockFolderStore.Open(_folder).Manifest.SourceSchema.Should().Be("新スキーマ");
    }

    [Fact(DisplayName = "SaveScreen は BOM なし UTF-8 で HTML を書く")]
    public void SaveScreen_WritesUtf8NoBom()
    {
        var store = CreateNew();

        store.SaveScreen(
            "A.html",
            "n",
            "d",
            ScreenHtml("<h1>日本語</h1>"),
            Array.Empty<MockTransition>(),
            "note"
        );

        var bytes = File.ReadAllBytes(Path.Combine(_folder, "A.html"));
        (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            .Should()
            .BeFalse();
        File.ReadAllText(Path.Combine(_folder, "A.html"), Encoding.UTF8).Should().Contain("日本語");
    }

    [Fact(DisplayName = "SaveScreen は共有 CSS 未参照時に検証警告を返す")]
    public void SaveScreen_ReturnsWarnings_WhenNoStylesheet()
    {
        var store = CreateNew();

        var warnings = store.SaveScreen(
            "A.html",
            "n",
            "d",
            ScreenHtml("<h1>x</h1>", withStylesheet: false),
            Array.Empty<MockTransition>(),
            "note"
        );

        warnings.Should().Contain(w => w.Contains("style.css"));
    }

    // --- エンティティ宣言（画面×エンティティ CRUD） --------------------

    private static MockScreenEntity Entity(string name, string operations) =>
        new() { Name = name, Operations = operations };

    private MockFolderStore SaveWithEntities(IReadOnlyList<MockScreenEntity>? entities) =>
        SaveWithEntities(CreateNew(), entities);

    private static MockFolderStore SaveWithEntities(
        MockFolderStore store,
        IReadOnlyList<MockScreenEntity>? entities
    )
    {
        store.SaveScreen(
            "OrderList.html",
            "注文一覧",
            "注文の一覧",
            ScreenHtml("<h1>一覧</h1>"),
            Array.Empty<MockTransition>(),
            "note",
            entities
        );

        return store;
    }

    private static IReadOnlyList<MockScreenEntity> EntitiesOf(MockFolderStore store) =>
        store.Manifest.Screens.Single(s => s.File == "OrderList.html").Entities
        ?? new List<MockScreenEntity>();

    [Fact(DisplayName = "NormalizeOperations は大文字化・無効文字除去・重複除去・正順化する")]
    public void NormalizeOperations_UppercasesFiltersDedupsOrders()
    {
        MockFolderStore.NormalizeOperations("urc").Should().Be("CRU");
        MockFolderStore.NormalizeOperations("dcru").Should().Be("CRUD");
        MockFolderStore.NormalizeOperations("CCRr").Should().Be("CR");
        MockFolderStore.NormalizeOperations("xyz").Should().BeEmpty();
        MockFolderStore.NormalizeOperations("").Should().BeEmpty();
        MockFolderStore.NormalizeOperations(null).Should().BeEmpty();
    }

    [Fact(DisplayName = "NormalizeEntities は空操作エントリを破棄し名前を控える")]
    public void NormalizeEntities_DropsEmptyOperationEntries_AndTrimsNames()
    {
        var result = MockFolderStore.NormalizeEntities(
            new[] { Entity("  Product ", "ur"), Entity("Junk", "zzz"), Entity("   ", "cru") }
        );

        result.Entities.Should().ContainSingle();
        result.Entities[0].Name.Should().Be("Product");
        result.Entities[0].Operations.Should().Be("RU");
        // 操作が空になった 'Junk' は破棄・名前を控える。名前空のエントリは黙って除外
        result.DiscardedNames.Should().BeEquivalentTo(new[] { "Junk" });
    }

    [Fact(DisplayName = "SaveScreen は entities 省略(null)で既存宣言を維持する")]
    public void SaveScreen_NullEntities_KeepsExistingDeclarations()
    {
        var store = SaveWithEntities(new[] { Entity("Order", "CRUD") });

        // entities を渡さずに再保存（省略＝維持）
        store.SaveScreen(
            "OrderList.html",
            "注文一覧",
            "説明変更",
            ScreenHtml("<h1>v2</h1>"),
            Array.Empty<MockTransition>(),
            "v2"
        );

        var entities = EntitiesOf(store);
        entities.Should().ContainSingle();
        entities[0].Name.Should().Be("Order");
        entities[0].Operations.Should().Be("CRUD");
    }

    [Fact(DisplayName = "SaveScreen は entities 空配列で宣言を消去する")]
    public void SaveScreen_EmptyEntities_ClearsDeclarations()
    {
        var store = SaveWithEntities(new[] { Entity("Order", "CRUD") });

        SaveWithEntities(store, Array.Empty<MockScreenEntity>());

        EntitiesOf(store).Should().BeEmpty();
    }

    [Fact(DisplayName = "SaveScreen は非空 entities で全置換し正規化する")]
    public void SaveScreen_NonEmptyEntities_ReplacesAndNormalizes()
    {
        var store = SaveWithEntities(new[] { Entity("Order", "CRUD") });

        SaveWithEntities(store, new[] { Entity("Customer", "urc"), Entity("Product", "r") });

        var entities = EntitiesOf(store);
        entities.Should().HaveCount(2);
        entities
            .Select(e => (e.Name, e.Operations))
            .Should()
            .BeEquivalentTo(new[] { ("Customer", "CRU"), ("Product", "R") });
    }

    [Fact(DisplayName = "SaveScreen は entities 往復（保存→Open で復元）できる")]
    public void SaveScreen_Entities_RoundTripThroughManifest()
    {
        SaveWithEntities(new[] { Entity("Order", "cr"), Entity("Customer", "r") });

        var reopened = MockFolderStore.Open(_folder);
        var entities =
            reopened.Manifest.Screens.Single(s => s.File == "OrderList.html").Entities
            ?? new List<MockScreenEntity>();

        entities
            .Select(e => (e.Name, e.Operations))
            .Should()
            .BeEquivalentTo(new[] { ("Order", "CR"), ("Customer", "R") });
    }

    [Fact(DisplayName = "mock.json は宣言なし画面に entities キーを書かない")]
    public void Manifest_OmitsEntitiesKey_WhenNoDeclarations()
    {
        var store = CreateNew();
        store.SaveScreen(
            "OrderList.html",
            "注文一覧",
            "注文の一覧",
            ScreenHtml("<h1>一覧</h1>"),
            Array.Empty<MockTransition>(),
            "note"
        );

        File.ReadAllText(Path.Combine(_folder, "mock.json")).Should().NotContain("entities");
    }

    [Fact(DisplayName = "entities なしの既存 mock.json を読める")]
    public void Open_LegacyManifestWithoutEntities_Works()
    {
        Directory.CreateDirectory(_folder);
        // entities フィールドを持たない旧フォーマットの mock.json
        File.WriteAllText(
            Path.Combine(_folder, "mock.json"),
            "{ \"version\": 1, \"title\": \"旧\", \"screens\": [ { \"file\": \"A.html\", \"name\": \"A\" } ] }"
        );

        var store = MockFolderStore.Open(_folder);

        var screen = store.Manifest.Screens.Single();
        screen.File.Should().Be("A.html");
        // 宣言なしの既存画面は null のまま（例外にならない）
        (screen.Entities is null || screen.Entities.Count == 0)
            .Should()
            .BeTrue();
    }
}
