using System.Globalization;
using AwesomeAssertions;
using QuickER.Resources;
using Xunit;

namespace QuickER.Tests.Resources;

/// <summary>厳密型リソースアクセサ（<see cref="Strings"/>）のローカライズ解決を検証するテストクラス</summary>
public class StringsLocalizationTests
{
    // 注意: 検証はグローバル静的 Strings.Culture を変更せず、ResourceManager.GetString(key, culture) で
    // 明示カルチャ指定して読む。Strings.Culture は プロセス全体で共有される静的のため、これを一時変更すると
    // xUnit のクラス並列実行で他テスト（Strings.X を参照する VM 等）が変更を観測しフレークする（順序/並列依存）。

    /// <summary>日本語サテライトから日本語文言が返ることを検証する（resx パイプライン全体の疎通確認）</summary>
    [Fact(DisplayName = "ja カルチャで日本語文言を返す")]
    public void Strings_JapaneseCulture_ReturnsJapanese()
    {
        var ja = new CultureInfo("ja");

        Strings.ResourceManager.GetString("Language_Caption", ja).Should().Be("言語");
        Strings.ResourceManager.GetString("Language_English", ja).Should().Be("English");
    }

    /// <summary>中立カルチャ（英語）から英語文言が返ることを検証する（resx パイプライン全体の疎通確認）</summary>
    [Fact(DisplayName = "en カルチャで英語文言を返す")]
    public void Strings_EnglishCulture_ReturnsEnglish()
    {
        var en = new CultureInfo("en");

        Strings.ResourceManager.GetString("Language_Caption", en).Should().Be("Language");
        Strings
            .ResourceManager.GetString("Language_RestartConfirm", en)
            .Should()
            .Be("The display language has been changed. Restart the app now to apply it?");
    }
}
