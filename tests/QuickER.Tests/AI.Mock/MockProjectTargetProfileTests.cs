using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockProjectTargetProfile.Resolve"/> のターゲット→プロファイル解決を検証するテストクラス。
/// （プロファイル各フラグメントの文面は、エージェント別テストがプロンプト経由で検証する。）
/// </summary>
public class MockProjectTargetProfileTests
{
    /// <summary>WPF ターゲットは WPF プロファイル（*.xaml・Id=wpf）へ解決されることを検証する</summary>
    [Fact(DisplayName = "Resolve(Wpf) は WPF プロファイルを返す")]
    public void Resolve_Wpf_ReturnsWpfProfile()
    {
        var profile = MockProjectTargetProfile.Resolve(MockProjectTarget.Wpf);

        profile.Should().BeSameAs(MockProjectTargetProfile.Wpf);
        profile.Target.Should().Be(MockProjectTarget.Wpf);
        profile.UiFileSearchPattern.Should().Be("*.xaml");
    }

    /// <summary>Blazor ターゲットは Blazor プロファイル（*.razor・Id=blazor）へ解決されることを検証する</summary>
    [Fact(DisplayName = "Resolve(Blazor) は Blazor プロファイルを返す")]
    public void Resolve_Blazor_ReturnsBlazorProfile()
    {
        var profile = MockProjectTargetProfile.Resolve(MockProjectTarget.Blazor);

        profile.Should().BeSameAs(MockProjectTargetProfile.Blazor);
        profile.Target.Should().Be(MockProjectTarget.Blazor);
        profile.UiFileSearchPattern.Should().Be("*.razor");
    }

    /// <summary>未対応のターゲットは ArgumentException になることを検証する</summary>
    [Fact(DisplayName = "未対応ターゲットは例外")]
    public void Resolve_UnknownTarget_Throws()
    {
        var unknown = new MockProjectTarget("android", "Android");

        FluentActions
            .Invoking(() => MockProjectTargetProfile.Resolve(unknown))
            .Should()
            .Throw<ArgumentException>();
    }

    /// <summary>
    /// エージェントへの指示と README が、ビルド対象としてソリューションファイルを名指しすることを検証する
    /// （フォルダ指定の dotnet build は、別のソリューションが同居していると MSB1011 で落ち、エージェントが自己検証できない）。
    /// </summary>
    [Theory(DisplayName = "指示と README はソリューションを名指しして dotnet build させる")]
    [InlineData("wpf")]
    [InlineData("blazor")]
    public void PromptsAndReadme_NameTheSolutionToBuild(string target)
    {
        var profile = MockProjectTargetProfile.Resolve(
            target == "wpf" ? MockProjectTarget.Wpf : MockProjectTarget.Blazor
        );
        const string expected = "dotnet build AcmeMock.sln";

        MockProjectPromptBuilder.BuildSystemPrompt(profile, "AcmeMock").Should().Contain(expected);
        MockProjectPromptBuilder.BuildPrompt(profile, "AcmeMock", null).Should().Contain(expected);
        profile
            .BuildReadme(
                "AcmeMock",
                MockProjectScaffoldService.ResolveNamespaces("AcmeMock", "sqlite"),
                "sqlite"
            )
            .Should()
            .Contain(expected);
        MockProjectPromptBuilder
            .BuildSystemPrompt(profile, "AcmeMock")
            .Should()
            .NotContain("run `dotnet build` in this folder");
    }
}
