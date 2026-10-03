using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockBundleExporter"/> の単一 HTML 結合・CSS インライン化・リンクのハッシュ化・
/// ハッシュルーター JS 埋め込み・外部参照排除を検証するテストクラス。
/// </summary>
public class MockBundleExporterTests : IDisposable
{
    private readonly string _folder;

    public MockBundleExporterTests()
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

    private static string Screen(string body) =>
        "<!DOCTYPE html><html><head><link rel=\"stylesheet\" href=\"style.css\"></head><body>"
        + body
        + "</body></html>";

    private MockFolderStore BuildTwoScreenMock()
    {
        var store = MockFolderStore.CreateNew(_folder, "受注管理", "schema");

        store.SaveStylesheet("body { color: #222; }", "css");
        store.SaveScreen(
            "OrderList.html",
            "一覧",
            "",
            Screen("<h1>注文一覧</h1><a href=\"OrderDetail.html\">詳細へ</a>"),
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
            Screen("<h1>注文詳細</h1><a href=\"OrderList.html\">戻る</a>"),
            new[]
            {
                new MockTransition { From = "OrderDetail.html", To = "OrderList.html" },
            },
            "v2"
        );

        return store;
    }

    [Fact(DisplayName = "2 画面＋CSS を単一 HTML へ結合する")]
    public void Export_BundlesTwoScreens()
    {
        var store = BuildTwoScreenMock();

        var bundle = MockBundleExporter.Export(store);

        // 両画面のセクションが存在する
        bundle.Should().Contain("data-screen=\"OrderList\"");
        bundle.Should().Contain("data-screen=\"OrderDetail\"");
        bundle.Should().Contain("注文一覧");
        bundle.Should().Contain("注文詳細");
    }

    [Fact(DisplayName = "共有 CSS を <style> へインライン化する")]
    public void Export_InlinesStylesheet()
    {
        var store = BuildTwoScreenMock();

        var bundle = MockBundleExporter.Export(store);

        bundle.Should().Contain("<style>");
        bundle.Should().Contain("body { color: #222; }");
        // style.css への link 参照は残らない
        bundle.Should().NotContain("href=\"style.css\"");
    }

    [Fact(DisplayName = "相対リンクをハッシュへ書き換える")]
    public void Export_RewritesLinksToHash()
    {
        var store = BuildTwoScreenMock();

        var bundle = MockBundleExporter.Export(store);

        bundle.Should().Contain("href=\"#OrderDetail\"");
        bundle.Should().Contain("href=\"#OrderList\"");
        bundle.Should().NotContain("href=\"OrderDetail.html\"");
        bundle.Should().NotContain("href=\"OrderList.html\"");
    }

    [Fact(DisplayName = "ハッシュルーターの JS を埋め込む")]
    public void Export_EmbedsHashRouter()
    {
        var store = BuildTwoScreenMock();

        var bundle = MockBundleExporter.Export(store);

        bundle.Should().Contain("hashchange");
        bundle.Should().Contain("location.hash");
        bundle.Should().Contain("data-screen");
    }

    [Fact(DisplayName = "生成 HTML に外部参照が残らない")]
    public void Export_HasNoExternalReference()
    {
        var store = MockFolderStore.CreateNew(_folder, "t", "s");
        store.SaveStylesheet("body{}", "css");
        store.SaveScreen(
            "A.html",
            "a",
            "",
            Screen("<h1>x</h1><a href=\"B.html\">next</a>"),
            new[]
            {
                new MockTransition { From = "A.html", To = "B.html" },
            },
            "v1"
        );
        store.SaveScreen(
            "B.html",
            "b",
            "",
            Screen("<h1>y</h1>"),
            Array.Empty<MockTransition>(),
            "v2"
        );

        var bundle = MockBundleExporter.Export(store);

        var warnings = MockContentValidator.ValidateStylesheet(bundle);
        warnings.Should().BeEmpty();
        bundle.Should().NotContain("http://");
        bundle.Should().NotContain("https://");
    }

    [Fact(DisplayName = "画面内 script は末尾へ移設される")]
    public void Export_MovesScriptToSectionEnd()
    {
        var store = MockFolderStore.CreateNew(_folder, "t", "s");
        store.SaveStylesheet("body{}", "css");
        store.SaveScreen(
            "A.html",
            "a",
            "",
            Screen("<h1>x</h1><script>console.log('hi');</script>"),
            Array.Empty<MockTransition>(),
            "v1"
        );

        var bundle = MockBundleExporter.Export(store);

        bundle.Should().Contain("console.log('hi');");
    }

    [Fact(DisplayName = "画面リンクのページ内フラグメントは捨て、#画面ID だけにする")]
    public void Export_DropsFragmentOfScreenLink()
    {
        var store = MockFolderStore.CreateNew(_folder, "t", "s");
        store.SaveStylesheet("body{}", "css");
        store.SaveScreen(
            "A.html",
            "a",
            "",
            Screen("<a href=\"OrderDetail.html#items\">x</a><a href='B.html#top'>y</a>"),
            Array.Empty<MockTransition>(),
            "v1"
        );

        var bundle = MockBundleExporter.Export(store);

        // #OrderDetail#items のように残すと、ルーターの画面 ID と一致せず遷移できない
        bundle.Should().Contain("href=\"#OrderDetail\"");
        bundle.Should().Contain("href='#B'");
        bundle.Should().NotContain("#items");
        bundle.Should().NotContain("#top");
    }

    [Fact(
        DisplayName = "ルーターは hash をデコードし大文字小文字を無視して照合し、一致しない hash では表示中の画面を据え置く"
    )]
    public void Export_RouterDecodesHashIgnoresCaseAndKeepsCurrentScreen()
    {
        var bundle = MockBundleExporter.Export(BuildTwoScreenMock());

        // 照合キー：decodeURIComponent（失敗したら元の値）＋小文字化を、hash と data-screen の両方へ掛ける
        bundle.Should().Contain("try { value = decodeURIComponent(value); } catch (e) {}");
        bundle.Should().Contain("return value.toLowerCase();");
        bundle.Should().Contain("var id = key(location.hash.replace(/^#/, ''));");
        bundle.Should().Contain("key(s.getAttribute('data-screen')) === id");

        // 一致しない hash（ページ内アンカー等）では、表示中の画面があれば何もしない（初回だけ先頭画面）
        bundle.Should().Contain("if (current !== null) { return; }");
        bundle.Should().Contain("var fallback = \"OrderList\";");
        bundle.Should().Contain("current = target;");
    }

    [Fact(
        DisplayName = "検証が解決済みとした相対リンクは、すべて単一 HTML で #画面ID へ書き換わる"
    )]
    public void Export_RewritesEveryLinkTheValidatorResolves()
    {
        var store = MockFolderStore.CreateNew(_folder, "t", "s");
        store.SaveStylesheet("body{}", "css");

        // 引用符の種類・./ 付き・フォルダ付き・大文字小文字違い・フラグメント付き・引用符の食い違い・外部 URL
        var body =
            "<a href=\"B.html\">1</a>"
            + "<a href='B.html'>2</a>"
            + "<a href=\"./B.html\">3</a>"
            + "<a href=\"sub/B.html\">4</a>"
            + "<a href=\"b.HTML\">5</a>"
            + "<a href=\"B.html#x\">6</a>"
            + "<a href=\"C.html'>7</a>"
            + "<a href=\"https://example.com/D.html\">8</a>";
        var html = Screen(body);

        store.SaveScreen("A.html", "a", "", html, Array.Empty<MockTransition>(), "v1");
        store.SaveScreen(
            "B.html",
            "b",
            "",
            Screen("<p>b</p>"),
            Array.Empty<MockTransition>(),
            "v2"
        );

        // 検証はリンク先が既知でなければ警告する＝警告の出ないリンクは「解決済み」
        var warnings = MockContentValidator.ValidateScreen(
            "A.html",
            html,
            Array.Empty<MockTransition>(),
            new[] { "A.html", "B.html" }
        );
        warnings.Should().NotContain(w => w.Contains("Link target"));

        var bundle = MockBundleExporter.Export(store);

        // 解決済みの相対リンク 6 本はすべて書き換わり、.html の相対リンクは 1 本も残らない
        bundle.Should().NotContain("href=\"B.html\"");
        bundle.Should().NotContain("href='B.html'");
        bundle.Should().NotContain("./B.html");
        bundle.Should().NotContain("sub/B.html");
        bundle.Should().NotContain("b.HTML\"");
        bundle.Should().NotContain("B.html#x");
        // 引用符の食い違いはどちらもリンクとして扱わない（検証も警告せず、書き換えもしない）
        bundle.Should().Contain("href=\"C.html'");
        // 外部 URL は素通し
        bundle.Should().Contain("https://example.com/D.html");
    }
}
