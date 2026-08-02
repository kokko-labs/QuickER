using System.Globalization;
using AwesomeAssertions;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary><see cref="AppLanguage.Resolve"/> の言語コード解決を検証するテストクラス</summary>
public class AppLanguageTests
{
    /// <summary>未設定（null）のとき OS が日本語なら ja を導出する</summary>
    [Fact(DisplayName = "未設定＋OS 日本語 → ja")]
    public void Resolve_NullSetting_JapaneseOs_ReturnsJa()
    {
        var result = AppLanguage.Resolve(null, new CultureInfo("ja-JP"));

        result.Should().Be("ja");
    }

    /// <summary>未設定（null）のとき OS が英語なら en を導出する</summary>
    [Fact(DisplayName = "未設定＋OS 英語 → en")]
    public void Resolve_NullSetting_EnglishOs_ReturnsEn()
    {
        var result = AppLanguage.Resolve(null, new CultureInfo("en-US"));

        result.Should().Be("en");
    }

    /// <summary>未設定（null）のとき OS が日本語・英語以外なら en へフォールバックする</summary>
    [Fact(DisplayName = "未設定＋OS その他言語 → en")]
    public void Resolve_NullSetting_OtherOs_ReturnsEn()
    {
        var result = AppLanguage.Resolve(null, new CultureInfo("fr-FR"));

        result.Should().Be("en");
    }

    /// <summary>ja / en を明示指定したときはそのまま採用する（OS に依らない）</summary>
    [Theory(DisplayName = "明示指定はそのまま採用")]
    [InlineData("ja")]
    [InlineData("en")]
    public void Resolve_ExplicitSetting_ReturnsAsIs(string setting)
    {
        // OS を逆言語にしても明示設定が優先されることを確認する
        var oppositeOs = setting == "ja" ? new CultureInfo("en-US") : new CultureInfo("ja-JP");

        var result = AppLanguage.Resolve(setting, oppositeOs);

        result.Should().Be(setting);
    }

    /// <summary>大文字・前後空白付きの明示指定も正規化して採用する</summary>
    [Theory(DisplayName = "大文字・空白付き明示指定を正規化")]
    [InlineData("JA", "ja")]
    [InlineData("  en  ", "en")]
    public void Resolve_MessyExplicitSetting_Normalizes(string setting, string expected)
    {
        var result = AppLanguage.Resolve(setting, new CultureInfo("fr-FR"));

        result.Should().Be(expected);
    }

    /// <summary>不正値・非対応言語コードは OS 導出へフォールバックする</summary>
    [Theory(DisplayName = "不正値は OS 導出へフォールバック")]
    [InlineData("")]
    [InlineData("zz")]
    [InlineData("fr")]
    public void Resolve_InvalidSetting_FallsBackToOs(string setting)
    {
        // OS が日本語なら ja・英語なら en へフォールバックする
        AppLanguage.Resolve(setting, new CultureInfo("ja-JP")).Should().Be("ja");
        AppLanguage.Resolve(setting, new CultureInfo("en-US")).Should().Be("en");
    }
}
