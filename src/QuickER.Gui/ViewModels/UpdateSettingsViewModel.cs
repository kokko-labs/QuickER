using CommunityToolkit.Mvvm.ComponentModel;
using QuickER.Gui.Abstractions;
using QuickER.Resources;
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

    /// <summary>保存の失敗を知らせるダイアログの表示先</summary>
    private readonly IDialogService _dialogs;

    /// <summary>保存できなかった切り替えを元へ戻している最中か（戻す代入で保存し直さないため）</summary>
    private bool _reverting;

    /// <summary>起動時に更新を確認するか（メニュー項目のチェック用。設定された値をそのまま表示する）</summary>
    [ObservableProperty]
    private bool _isCheckOnStartupEnabled;

    /// <summary>依存を注入して生成する（ストア省略時は既定の %LOCALAPPDATA%\QuickER 保存先）</summary>
    /// <param name="dialogs">保存の失敗を知らせるダイアログの表示先</param>
    /// <param name="store">設定ストア（単体テストでは一時フォルダのストアを渡す）</param>
    public UpdateSettingsViewModel(IDialogService dialogs, GuiAppSettingsStore? store = null)
    {
        _dialogs = dialogs;
        _store = store ?? new GuiAppSettingsStore();
        _isCheckOnStartupEnabled = _store.Load().CheckForUpdatesOnStartup;
    }

    /// <summary>トグルの変更を設定ファイルへ反映する（他セクションを消さない read-modify-write）</summary>
    /// <remarks>
    /// 保存できなかったときは表示を元に戻し、理由を添えてエラーで知らせる。戻さないと、チェックは外れて見えるのに
    /// 次回起動でも更新を確認する（逆も同じ）という食い違いが黙って残る。
    /// </remarks>
    partial void OnIsCheckOnStartupEnabledChanged(bool value)
    {
        if (_reverting)
        {
            return;
        }

        if (_store.TryUpdate(settings => settings.CheckForUpdatesOnStartup = value, out var reason))
        {
            return;
        }

        _reverting = true;

        try
        {
            IsCheckOnStartupEnabled = !value;
        }
        finally
        {
            _reverting = false;
        }

        _dialogs.ShowError(
            Strings.Settings_SaveFailed + Environment.NewLine + reason,
            Strings.Common_Error
        );
    }
}
