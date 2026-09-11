using CommunityToolkit.Mvvm.ComponentModel;
using QuickER.Services;

namespace QuickER.ViewModels;

/// <summary>
/// 設定メニューの「起動時に更新を確認する」トグル専用の小さな ViewModel。
/// 現在値の保持と、切り替え時の設定保存を担う。
/// </summary>
/// <remarks>
/// 実際の更新チェックは起動時に <see cref="UpdateService"/> が設定を読んで判断するため、
/// ここでの切り替えは次回起動から効く（今の起動でのチェックは既に済んでいる）。
/// <see cref="LanguageSwitchViewModel"/> と同じく、MainViewModel の肥大を避けるため独立した VM として分離している。
/// </remarks>
public partial class UpdateSettingsViewModel : ObservableObject
{
    /// <summary>設定の永続化ストア</summary>
    private readonly GuiAppSettingsStore _store;

    /// <summary>起動時に更新を確認するか（メニュー項目のチェック用。設定された値をそのまま表示する）</summary>
    [ObservableProperty]
    private bool _isCheckOnStartupEnabled;

    /// <summary>ストアを注入して生成する（省略時は既定の %LOCALAPPDATA%\QuickER 保存先）</summary>
    /// <param name="store">設定ストア（単体テストでは一時フォルダのストアを渡す）</param>
    public UpdateSettingsViewModel(GuiAppSettingsStore? store = null)
    {
        _store = store ?? new GuiAppSettingsStore();
        _isCheckOnStartupEnabled = _store.Load().CheckForUpdatesOnStartup;
    }

    /// <summary>トグルの変更を設定ファイルへ反映する（他セクションを消さない read-modify-write）</summary>
    partial void OnIsCheckOnStartupEnabledChanged(bool value)
    {
        var settings = _store.Load();
        settings.CheckForUpdatesOnStartup = value;
        _store.Save(settings);
    }
}
