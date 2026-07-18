using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using QuickER.AI.Mock.Resources;
using QuickER.Extensibility;

namespace QuickER.AI.Mock;

/// <summary>
/// AI モック生成機能をホスト（QuickER.Gui）へ着脱可能な形で提供するフィーチャーモジュール。
/// </summary>
/// <remarks>
/// DI へランチャーを登録し、ツールバーへ「モック生成を開く」ボタン 1 個を寄与する。
/// アプリ終了時にはランチャー経由でウィンドウを閉じる。
/// </remarks>
public sealed class MockGenerationFeatureModule : IFeatureModule
{
    /// <inheritdoc />
    public string Id => "mock-generation";

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IMockGenerationLauncher, MockGenerationLauncher>();
    }

    /// <inheritdoc />
    public IReadOnlyList<FeatureToolbarItem> CreateToolbarItems(IServiceProvider services)
    {
        var launcher = services.GetRequiredService<IMockGenerationLauncher>();

        return new[]
        {
            new FeatureToolbarItem(
                icon: "🖼",
                label: Strings.Toolbar_MockGeneration,
                tooltip: Strings.Toolbar_MockGenerationTooltip,
                command: new RelayCommand(launcher.Open)
            ),
        };
    }

    /// <inheritdoc />
    public void OnMainWindowClosing(IServiceProvider services)
    {
        services.GetRequiredService<IMockGenerationLauncher>().Close();
    }
}
