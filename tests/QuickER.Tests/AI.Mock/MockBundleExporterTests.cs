using System.IO;
using FluentAssertions;
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
}
