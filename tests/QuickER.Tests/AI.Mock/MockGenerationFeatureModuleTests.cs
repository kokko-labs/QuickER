using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.AI.Mock;
using QuickER.Extensibility;
using QuickER.Tests.TestDoubles;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockGenerationFeatureModule"/> の DI 登録・ツールバー寄与・終了時後始末を検証するテストクラス。
/// </summary>
/// <remarks>
/// resx の期待値は厳密型アクセサ（<see cref="MockStrings"/>）経由で取得して比較する
/// （グローバルカルチャは変更しない）。ランチャーの Open()（実 Window 生成）は呼ばない。
/// </remarks>
public class MockGenerationFeatureModuleTests
{
    /// <summary>スタブ IErDiagramHost を登録し、ConfigureServices 後にランチャーが解決できることを検証する</summary>
    [Fact(DisplayName = "ConfigureServices 後にランチャーが解決できる")]
    public void ConfigureServices_RegistersResolvableLauncher()
    {
        var module = new MockGenerationFeatureModule();
        var services = new ServiceCollection();
        services.AddSingleton<IErDiagramHost>(new StubErDiagramHost());

        module.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        provider.GetService<IMockGenerationLauncher>().Should().NotBeNull();
    }

    /// <summary>Id が "mock-generation" であることを検証する</summary>
    [Fact(DisplayName = "Id は mock-generation")]
    public void Id_IsMockGeneration()
    {
        new MockGenerationFeatureModule().Id.Should().Be("mock-generation");
    }

    /// <summary>CreateToolbarItems が 1 件で、Icon/Label/Tooltip が resx と一致し Command が非 null であることを検証する</summary>
    [Fact(DisplayName = "CreateToolbarItems は resx 一致の 1 件を返す")]
    public void CreateToolbarItems_ReturnsSingleLocalizedItem()
    {
        var module = new MockGenerationFeatureModule();
        var services = new ServiceCollection();
        services.AddSingleton<IErDiagramHost>(new StubErDiagramHost());
        module.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var items = module.CreateToolbarItems(provider);

        items.Should().ContainSingle();
        var item = items[0];
        item.Icon.Should().Be("🖼");
        item.Label.Should().Be(MockStrings.Toolbar_MockGeneration);
        item.Tooltip.Should().Be(MockStrings.Toolbar_MockGenerationTooltip);
        item.Command.Should().NotBeNull();
    }

    /// <summary>ダイアログ未生成の状態で OnMainWindowClosing が例外なく完了することを検証する</summary>
    [Fact(DisplayName = "OnMainWindowClosing は未生成状態でも例外なく完了する")]
    public void OnMainWindowClosing_WithoutDialog_DoesNotThrow()
    {
        var module = new MockGenerationFeatureModule();
        var services = new ServiceCollection();
        services.AddSingleton<IErDiagramHost>(new StubErDiagramHost());
        module.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        var act = () => module.OnMainWindowClosing(provider);
        act.Should().NotThrow();
    }
}
