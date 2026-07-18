using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.UI.Resources;
using QuickER.Extensibility;

namespace QuickER.CodeGen.UI;

/// <summary>
/// C# コード生成・名前付きクエリ定義機能をホスト（QuickER.Gui）へ着脱可能な形で提供するフィーチャーモジュール。
/// </summary>
/// <remarks>
/// DI へコマンドサービス・ダイアログ提示シーム・列リネーム追従フォロワーを登録し、
/// ツールバーへ「コード生成」「クエリ定義」の 2 ボタンを寄与する。
/// 初期化時にフォロワーをホストの列リネーム通知へ購読させる。
/// </remarks>
public sealed class CodeGenerationFeatureModule : IFeatureModule
{
    /// <inheritdoc />
    public string Id => "code-generation";

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        // ダイアログ提示シーム（テスト容易性のためインターフェイス越しに提供）
        services.AddSingleton<ICSharpGenerationDialogPresenter, CSharpGenerationDialogPresenter>();
        services.AddSingleton<IQueryDefinitionDialogPresenter, QueryDefinitionDialogPresenter>();

        // コマンドサービスと列リネーム追従フォロワー
        services.AddSingleton<CSharpGenerationCommandService>();
        services.AddSingleton<QueryDefinitionCommandService>();
        services.AddSingleton<QueryConditionRenameFollower>();
    }

    /// <inheritdoc />
    public void Initialize(IServiceProvider services)
    {
        // 列リネーム追従フォロワーをホストの ColumnRenamed 通知へ購読させる
        services.GetRequiredService<QueryConditionRenameFollower>().Attach();
    }

    /// <inheritdoc />
    public IReadOnlyList<FeatureToolbarItem> CreateToolbarItems(IServiceProvider services)
    {
        var generation = services.GetRequiredService<CSharpGenerationCommandService>();
        var queries = services.GetRequiredService<QueryDefinitionCommandService>();

        return new[]
        {
            new FeatureToolbarItem(
                Icon: "⌘",
                Label: Strings.Toolbar_GenerateCSharp,
                Tooltip: Strings.Toolbar_GenerateCSharpTooltip,
                Command: new RelayCommand(generation.Run),
                // AI モジュール群との区切りとして、先頭ボタンの直前にセパレータを描画する
                BeginsGroup: true
            ),
            new FeatureToolbarItem(
                Icon: "🔎",
                Label: Strings.Toolbar_QueryDefinitions,
                Tooltip: Strings.Toolbar_QueryDefinitionsTooltip,
                Command: new RelayCommand(queries.Run)
            ),
        };
    }

    /// <inheritdoc />
    public void OnMainWindowClosing(IServiceProvider services)
    {
        // モーダルダイアログのみで残存するモードレスウィンドウが無いため、後始末は不要（空実装）。
    }
}
