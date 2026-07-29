namespace QuickER.Gui.Abstractions;

/// <summary><see cref="IDialogService"/> の確認ダイアログに関する補助拡張</summary>
public static class DialogServiceExtensions
{
    /// <summary>現在の内容を失う操作の続行確認を、未保存変更の有無に応じた警告水準で表示する</summary>
    /// <remarks>
    /// 未保存変更があるときは続行で編集内容が失われ元に戻せないため
    /// <see cref="IDialogService.ConfirmWarning"/>（Warning）、
    /// ないときは保存済み内容をファイルから開き直せるため
    /// <see cref="IDialogService.Confirm"/>（Question）で確認する。
    /// 図のクリア・取込による図置換など「現在の図の内容を捨てる」確認はこの拡張へ集約する。
    /// </remarks>
    /// <param name="dialogs">確認ダイアログの表示先</param>
    /// <param name="hasUnsavedChanges">未保存の変更があるかどうか</param>
    /// <param name="message">確認メッセージ</param>
    /// <param name="title">ダイアログのタイトル</param>
    /// <returns>OK が選択された場合 true</returns>
    public static bool ConfirmDiscard(
        this IDialogService dialogs,
        bool hasUnsavedChanges,
        string message,
        string title
    ) =>
        hasUnsavedChanges
            ? dialogs.ConfirmWarning(message, title)
            : dialogs.Confirm(message, title);
}
