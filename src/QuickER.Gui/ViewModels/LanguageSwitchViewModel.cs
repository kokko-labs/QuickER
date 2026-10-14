using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.Gui.Abstractions;
using QuickER.Resources;
using QuickER.Services;

namespace QuickER.ViewModels;

/// <summary>
/// ツールバーの言語切替ボタン専用の小さな ViewModel。
/// 現在の表示言語の保持と、選択時の設定保存＋再起動案内の通知を担う。
/// </summary>
/// <remarks>
/// 切替は再起動反映方式のため、選択しても即時にはカルチャを変えない
/// （設定を保存し、次回起動時に <see cref="App"/> がカルチャへ適用する）。
/// MainViewModel の肥大を避けるため独立した VM として分離している。
/// </remarks>
public partial class LanguageSwitchViewModel : ObservableObject
{
    /// <summary>再起動確認などのダイアログの表示先</summary>
    private readonly IDialogService _dialogs;

    /// <summary>言語設定の永続化ストア</summary>
    private readonly GuiAppSettingsStore _store;

    /// <summary>テスト専用: 設定の保存先</summary>
    internal GuiAppSettingsStore StoreForTests => _store;

    /// <summary>アプリ再起動サービス（確認で OK のとき呼び出す）</summary>
    private readonly IApplicationRestartService _restart;

    /// <summary>次の起動で使う表示言語コード（<c>"ja"</c> / <c>"en"</c>。メニューのチェック表示に使う）</summary>
    /// <remarks>
    /// 保存した設定を示す。再起動を断ったあとは、いま表示中の言語と食い違ってよい
    /// （チェックは「次に起動したときの言語」を表す）
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsJapaneseChecked))]
    [NotifyPropertyChangedFor(nameof(IsEnglishChecked))]
    private string _currentLanguage;

    /// <summary>日本語が現在選択されているか（メニュー項目のチェック用）</summary>
    public bool IsJapaneseChecked => CurrentLanguage == AppLanguage.Japanese;

    /// <summary>英語が現在選択されているか（メニュー項目のチェック用）</summary>
    public bool IsEnglishChecked => CurrentLanguage == AppLanguage.English;

    /// <summary>依存を注入して生成する（ストア・再起動サービス省略時は既定を使う）</summary>
    /// <param name="dialogs">確認ダイアログの表示先</param>
    /// <param name="store">言語設定ストア（省略時は既定の %LOCALAPPDATA%\QuickER 保存先）</param>
    /// <param name="restart">アプリ再起動サービス（省略時は WPF 実装。単体テストではスタブを渡す）</param>
    public LanguageSwitchViewModel(
        IDialogService dialogs,
        GuiAppSettingsStore? store = null,
        IApplicationRestartService? restart = null
    )
    {
        _dialogs = dialogs;
        _store = store ?? new GuiAppSettingsStore();
        _restart = restart ?? new WpfApplicationRestartService();

        // 次の起動で使う言語＝保存した設定（未設定・不正値は OS のカルチャから導く。起動時に App が適用するのと同じ規則）
        var settings = _store.Load();
        _currentLanguage = AppLanguage.Resolve(
            settings.Language,
            System.Globalization.CultureInfo.CurrentUICulture
        );
    }

    /// <summary>表示言語を選択して設定へ保存し、再起動で反映される旨を通知する</summary>
    /// <param name="languageCode">選択された言語コード（<c>"ja"</c> / <c>"en"</c>）</param>
    [RelayCommand]
    private void SelectLanguage(string? languageCode)
    {
        var resolved = AppLanguage.Resolve(
            languageCode,
            System.Globalization.CultureInfo.CurrentUICulture
        );

        // 同じ言語を選び直しただけなら何もしない（無用な再起動案内を出さない）
        if (resolved == CurrentLanguage)
        {
            return;
        }

        // 設定を保存し、メニューのチェック表示を更新する（実際のカルチャ反映は次回起動時）。
        // 保存できなければ表示も言語も変えずに知らせる（保存できないまま再起動しても元の言語で起動するだけになる）
        if (!_store.TryUpdate(settings => settings.Language = resolved, out var reason))
        {
            _dialogs.ShowError(
                Strings.Settings_SaveFailed + Environment.NewLine + reason,
                Strings.Common_Error
            );
            return;
        }

        CurrentLanguage = resolved;

        // いま表示中の言語へ戻しただけなら、再起動しても何も変わらないので尋ねない
        // （別の言語を選んで再起動を断ったあと、元の言語を選び直した場合）
        var displayed = AppLanguage.Resolve(
            null,
            System.Globalization.CultureInfo.CurrentUICulture
        );

        if (resolved == displayed)
        {
            return;
        }

        // 再起動反映方式のため、今すぐ再起動するか確認する。OK なら再起動して即座に反映する。
        // 断った場合も設定は保存済みで、次回起動時に反映される。
        if (_dialogs.Confirm(Strings.Language_RestartConfirm, Strings.Language_RestartConfirmTitle))
        {
            _restart.Restart();
        }
    }
}
