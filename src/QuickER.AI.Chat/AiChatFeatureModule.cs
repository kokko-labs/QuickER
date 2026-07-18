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
                icon: "🤖",
                label: Strings.Toolbar_OpenAiChat,
                tooltip: Strings.Toolbar_OpenAiChatTooltip,
                command: new RelayCommand(launcher.Open),
                // 前のグループ（DB 取込・DB 同期など）との区切りとして、先頭ボタンの直前にセパレータを描画する
                beginsGroup: true
            ),
        };
    }

    /// <inheritdoc />
    public void OnMainWindowClosing(IServiceProvider services)
    {
        services.GetRequiredService<IAiChatLauncher>().Close();
    }
}
