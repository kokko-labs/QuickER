using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Extensibility;
using QuickER.Gui.Abstractions;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.FeatureModules;

/// <summary>
/// <see cref="FeatureModuleCatalog"/>（ホスト同梱モジュールの静的カタログ）と、
/// カタログ全体を通した「登録 → 構築 → ツールバー寄与 → 終了時後始末」のライフサイクルを検証するテストクラス。
/// </summary>
/// <remarks>
/// App.xaml.cs の合成ルートが行う流れ（ConfigureServices → BuildServiceProvider →
/// CreateToolbarItems の集約 → OnMainWindowClosing）を実モジュールで通しで踏む。
/// ランチャーの Open()（実 Window 生成）は呼ばない。
/// </remarks>
public class FeatureModuleCatalogTests
{
    /// <summary>カタログが AI チャット・モック生成・コード生成の 3 モジュールをこの順で返すことを検証する</summary>
    [Fact(
        DisplayName = "カタログは ai-chat / mock-generation / code-generation の 3 モジュールを返す"
    )]
    public void CreateModules_ReturnsAiChatMockAndCodeGeneration()
    {
        var modules = FeatureModuleCatalog.CreateModules();

        modules
            .Select(module => module.Id)
            .Should()
            .Equal("ai-chat", "mock-generation", "code-generation");
    }

    /// <summary>合成ルートと同じライフサイクルを全モジュールで通しで実行できることを検証する</summary>
    [Fact(DisplayName = "登録 → 構築 → ツールバー寄与 → 終了時後始末が通しで完了する")]
    public void FullLifecycle_RegistersContributesAndClosesWithoutError()
    {
        var modules = FeatureModuleCatalog.CreateModules();
        var services = new ServiceCollection();
        services.AddSingleton<IErDiagramHost>(new StubErDiagramHost());
        // コード生成モジュールのダイアログ提示シームが解決に必要とする依存
        services.AddSingleton<IDialogService>(new StubDialogService());
        services.AddSingleton<IFileDialogService>(new NullFileDialogService());

        foreach (var module in modules)
        {
            module.ConfigureServices(services);
        }

        using var provider = services.BuildServiceProvider();

        // App.xaml.cs と同じ集約でツールバー寄与を得る（AI チャット → モック生成 → コード生成 → クエリ定義の順）
        var items = modules.SelectMany(module => module.CreateToolbarItems(provider)).ToList();

        items.Should().HaveCount(4);
        items.Select(item => item.Icon).Should().Equal("🤖", "🖼", "⌘", "🔎");
        items.Should().OnlyContain(item => item.Command != null && item.Command.CanExecute(null));

        var act = () =>
        {
            foreach (var module in modules)
            {
                module.OnMainWindowClosing(provider);
            }
        };
        act.Should().NotThrow();
    }
}
