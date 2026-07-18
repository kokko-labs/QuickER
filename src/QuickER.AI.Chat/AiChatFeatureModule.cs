using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using QuickER.AI.Chat.Resources;
using QuickER.Extensibility;

namespace QuickER.AI.Chat;

/// <summary>
/// AI チャット機能をホスト（QuickER.Gui）へ着脱可能な形で提供するフィーチャーモジュール。
/// </summary>
/// <remarks>
/// DI へランチャーを登録し、ツールバーへ「AI チャットを開く」ボタン 1 個を寄与する。
/// アプリ終了時にはランチャー経由でウィンドウを閉じる。
/// </remarks>
public sealed class AiChatFeatureModule : IFeatureModule
{
    /// <inheritdoc />
    public string Id => "ai-chat";

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAiChatLauncher, AiChatLauncher>();
    }

    /// <inheritdoc />
    public IReadOnlyList<FeatureToolbarItem> CreateToolbarItems(IServiceProvider services)
    {
        var launcher = services.GetRequiredService<IAiChatLauncher>();

        return new[]
        {
            new FeatureToolbarItem(
                Icon: "🤖",
                Label: Strings.Toolbar_OpenAiChat,
                Tooltip: Strings.Toolbar_OpenAiChatTooltip,
                Command: new RelayCommand(launcher.Open)
            ),
        };
    }

    /// <inheritdoc />
    public void OnMainWindowClosing(IServiceProvider services)
    {
        services.GetRequiredService<IAiChatLauncher>().Close();
    }
}
