using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.AI.Chat;
using QuickER.Extensibility;
using QuickER.Tests.TestDoubles;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// <see cref="AiChatFeatureModule"/> の DI 登録・ツールバー寄与・終了時後始末を検証するテストクラス。
/// </summary>
/// <remarks>
/// resx の期待値は厳密型アクセサ（<see cref="ChatStrings"/>）経由で取得して比較する
/// （グローバルカルチャは変更しない）。ランチャーの Open()（実 Window 生成）は呼ばない。
/// </remarks>
public class AiChatFeatureModuleTests
{
    /// <summary>スタブ IErDiagramHost を登録し、ConfigureServices 後にランチャーが解決できることを検証する</summary>
    [Fact(DisplayName = "ConfigureServices 後にランチャーが解決できる")]
    public void ConfigureServices_RegistersResolvableLauncher()
    {
        var module = new AiChatFeatureModule();
        var services = new ServiceCollection();
        services.AddSingleton<IErDiagramHost>(new StubErDiagramHost());

        module.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        provider.GetService<IAiChatLauncher>().Should().NotBeNull();
    }

    /// <summary>Id が "ai-chat" であることを検証する</summary>
    [Fact(DisplayName = "Id は ai-chat")]
    public void Id_IsAiChat()
    {
        new AiChatFeatureModule().Id.Should().Be("ai-chat");
    }

    /// <summary>CreateToolbarItems が 1 件で、Icon/Label/Tooltip が resx と一致し Command が非 null であることを検証する</summary>
    [Fact(DisplayName = "CreateToolbarItems は resx 一致の 1 件を返す")]
    public void CreateToolbarItems_ReturnsSingleLocalizedItem()
    {
        var module = new AiChatFeatureModule();
        var services = new ServiceCollection();
        services.AddSingleton<IErDiagramHost>(new StubErDiagramHost());
        module.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var items = module.CreateToolbarItems(provider);

        items.Should().ContainSingle();
        var item = items[0];
        item.Icon.Should().Be("🤖");
        item.Label.Should().Be(ChatStrings.Toolbar_OpenAiChat);
        item.Tooltip.Should().Be(ChatStrings.Toolbar_OpenAiChatTooltip);
        item.Command.Should().NotBeNull();
    }

    /// <summary>ダイアログ未生成の状態で OnMainWindowClosing が例外なく完了することを検証する</summary>
    [Fact(DisplayName = "OnMainWindowClosing は未生成状態でも例外なく完了する")]
    public void OnMainWindowClosing_WithoutDialog_DoesNotThrow()
    {
        var module = new AiChatFeatureModule();
        var services = new ServiceCollection();
        services.AddSingleton<IErDiagramHost>(new StubErDiagramHost());
        module.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        var act = () => module.OnMainWindowClosing(provider);
        act.Should().NotThrow();
    }
}
