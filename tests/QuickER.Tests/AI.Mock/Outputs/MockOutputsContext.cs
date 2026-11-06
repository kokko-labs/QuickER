using System.Globalization;
using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock.Outputs;

/// <summary>出力の表の 1 行の入力</summary>
/// <param name="Observations">数える（対象, 観点）の組（1 つ以上）</param>
/// <param name="Body">流して表明する本体</param>
internal sealed record MockOutputScenario(
    IReadOnlyList<(MockOutputSut Sut, string Aspect)> Observations,
    Action<MockOutputsContext> Body
);

/// <summary>
/// 出力の表の 1 行を流す文脈（ストアを要る行の一時フォルダ）と、どの出力にも掛ける照合
/// </summary>
/// <remarks>
/// <para>一時フォルダは <see cref="TestTempFolder"/> に行ごとに作り（ストアを作る行だけ）、破棄で中身ごと消す。</para>
/// <para>どの出力にも掛ける照合（行の本体が <see cref="ExportBundle"/>・<see cref="ExportDoc"/> を通すと掛かる）：</para>
/// <list type="bullet">
/// <item>同じストアから 2 回出すと同じ文字列（決定的。出典：MockBundleExporter.cs・MockDesignDocExporter.cs は時刻・乱数を使わない）。</item>
/// <item>単一 HTML：画面と CSS に外部参照（<c>://</c>）が無ければ、出力にも <c>http://</c>・<c>https://</c> が無く、CSS の検証の警告も無い（出典：MockBundleExporter.cs の BuildDocument）。</item>
/// <item>設計書：CR と U+2028 が無い（改行は LF 固定。出典：MockDesignDocExporter.cs の冒頭の remarks）。</item>
/// </list>
/// </remarks>
internal sealed class MockOutputsContext : IDisposable
{
    /// <summary>固定時刻（改訂のタイムスタンプを決定的にする）</summary>
    public static readonly DateTimeOffset FixedNow = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

    /// <summary>style.css への link</summary>
    public const string StyleLink = "<link rel=\"stylesheet\" href=\"style.css\">";

    private TestTempFolder? _temp;

    /// <summary>モックフォルダ（一時フォルダの下の mock。初めて読んだときに一時フォルダを作る）</summary>
    public string Folder => Path.Combine((_temp ??= new TestTempFolder()).Path, "mock");

    /// <summary>mock.json のパス</summary>
    public string ManifestPath => Path.Combine(Folder, MockManifest.ManifestFileName);

    /// <summary>style.css を参照する最小の完全な HTML</summary>
    public static string Screen(string body) =>
        $"<!DOCTYPE html><html><head>{StyleLink}</head><body>{body}</body></html>";

    /// <summary>モックフォルダを作る</summary>
    public MockFolderStore CreateStore(string title = "t", string schema = "s") =>
        MockFolderStore.CreateNew(Folder, title, schema, () => FixedNow);

    /// <summary>mock.json を手で書く</summary>
    public void WriteManifest(string json)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(ManifestPath, json);
    }

    /// <summary>単一 HTML を出す（決定的であること・外部参照の無い入力から外部参照を出さないことを照合する）</summary>
    public static string ExportBundle(MockFolderStore store)
    {
        var bundle = MockBundleExporter.Export(store);

        MockBundleExporter.Export(store).Should().Be(bundle, "同じストアからは同じ単一 HTML");

        var inputs = store
            .Manifest.Screens.Select(screen => store.GetScreenHtml(screen.File) ?? string.Empty)
            .Append(store.GetStylesheet() ?? string.Empty);

        if (!inputs.Any(input => input.Contains("://", StringComparison.Ordinal)))
        {
            bundle.Should().NotContain("http://").And.NotContain("https://");
            MockContentValidator.ValidateStylesheet(bundle).Should().BeEmpty();
        }

        return bundle;
    }

    /// <summary>設計書を出す（決定的であること・CR と U+2028 が無いことを照合する）</summary>
    public static string ExportDoc(MockFolderStore store)
    {
        var doc = MockDesignDocExporter.Export(store);

        MockDesignDocExporter.Export(store).Should().Be(doc, "同じストアからは同じ設計書");
        doc.Should().NotContain(((char)13).ToString()).And.NotContain(((char)0x2028).ToString());

        return doc;
    }

    /// <summary>単一 HTML から 1 画面の section を取り出す</summary>
    public static string SectionOf(string bundle, string screenId)
    {
        var start = bundle.IndexOf(
            $"<section data-screen=\"{screenId}\">",
            StringComparison.Ordinal
        );
        start.Should().BeGreaterThanOrEqualTo(0, $"画面 {screenId} の section がある");
        var end = bundle.IndexOf("</section>", start, StringComparison.Ordinal);

        return bundle.Substring(start, end + "</section>".Length - start);
    }

    /// <summary>UI のカルチャを一時的に切り替えて評価し、必ず元へ戻す</summary>
    public static T WithUiCulture<T>(string culture, Func<T> body)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            return body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    public void Dispose() => _temp?.Dispose();
}
