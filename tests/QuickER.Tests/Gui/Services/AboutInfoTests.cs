using System.Globalization;
using AwesomeAssertions;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// バージョン情報ダイアログの表示内容（<see cref="AboutInfo"/>）の組み立てを検証するテストクラス。
/// </summary>
public class AboutInfoTests
{
    /// <summary>ビルドメタデータ（+ 以降）を除いた表示用の版になることを検証する</summary>
    [Theory(DisplayName = "ToDisplayVersion: + 以降を除き、空・欠落は unknown にする")]
    [InlineData("0.2.0+abc123def", "0.2.0")]
    [InlineData("0.2.0", "0.2.0")]
    [InlineData("0.2.0-preview.1+abc", "0.2.0-preview.1")]
    [InlineData("+abc", AboutInfo.UnknownVersion)]
    [InlineData("", AboutInfo.UnknownVersion)]
    [InlineData("   ", AboutInfo.UnknownVersion)]
    [InlineData(null, AboutInfo.UnknownVersion)]
    public void ToDisplayVersion_StripsBuildMetadata(string? informational, string expected) =>
        AboutInfo.ToDisplayVersion(informational).Should().Be(expected);

    /// <summary>実アセンブリから版・著作権・実行環境・リポジトリを読み取ることを検証する</summary>
    [Fact(
        DisplayName = "FromAssembly: 版は + 以降を除き、ビルド版・著作権・実行環境・リポジトリを持つ"
    )]
    public void FromAssembly_ReadsVersionCopyrightAndRuntime()
    {
        // GUI 本体と同じ Directory.Build.props（VersionPrefix / Copyright）でビルドされたアセンブリを読む
        var info = AboutInfo.FromAssembly(
            typeof(MainViewModel).Assembly,
            CultureInfo.GetCultureInfo("en")
        );

        info.Version.Should().NotBe(AboutInfo.UnknownVersion).And.NotContain("+");
        info.BuildVersion.Should().StartWith(info.Version);
        info.Copyright.Should().Contain("kokko-labs");
        info.Runtime.Should().StartWith(".NET");
        info.OperatingSystem.Should().NotBeNullOrWhiteSpace();
        info.RepositoryUrl.Should().Be(AboutInfo.DefaultRepositoryUrl);
        Uri.TryCreate(info.RepositoryUrl, UriKind.Absolute, out _).Should().BeTrue();
        info.DocumentationUrl.Should().Be(AboutInfo.EnglishDocumentationUrl);
    }

    /// <summary>ドキュメントの目次は表示言語に合わせ、日本語なら日本語版・それ以外は英語版を指すことを検証する</summary>
    [Theory(
        DisplayName = "DocumentationUrlFor: 日本語は README.ja.md・それ以外は README.md を指す"
    )]
    [InlineData("ja", "/docs/README.ja.md")]
    [InlineData("ja-JP", "/docs/README.ja.md")]
    [InlineData("en", "/docs/README.md")]
    [InlineData("en-US", "/docs/README.md")]
    [InlineData("", "/docs/README.md")]
    public void DocumentationUrlFor_FollowsUiLanguage(string cultureName, string expectedSuffix)
    {
        var url = AboutInfo.DocumentationUrlFor(CultureInfo.GetCultureInfo(cultureName));

        url.Should().StartWith(AboutInfo.DefaultRepositoryUrl).And.EndWith(expectedSuffix);
        Uri.TryCreate(url, UriKind.Absolute, out _).Should().BeTrue();
    }

    /// <summary>クリップボードへ渡すテキストが英語固定の 4 行で、ビルド版まで含むことを検証する</summary>
    [Fact(DisplayName = "ToClipboardText: 版・ビルド版・ランタイム・OS を英語固定の 4 行で出す")]
    public void ToClipboardText_ListsVersionBuildRuntimeAndOs()
    {
        var info = new AboutInfo(
            "0.2.0",
            "0.2.0+abc123",
            ".NET 10.0.1",
            "Microsoft Windows 10.0.26200",
            "Copyright (c) 2026 kokko-labs",
            "https://example.invalid/repo",
            "https://example.invalid/docs"
        );

        info.ToClipboardText()
            .Split(Environment.NewLine)
            .Should()
            .Equal(
                "QuickER 0.2.0",
                "Build: 0.2.0+abc123",
                "Runtime: .NET 10.0.1",
                "OS: Microsoft Windows 10.0.26200"
            );
    }
}
