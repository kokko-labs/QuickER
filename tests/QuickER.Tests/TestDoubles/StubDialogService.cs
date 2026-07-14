using QuickER.Gui.Abstractions;

namespace QuickER.Tests.TestDoubles;

/// <summary>
/// UI を表示せずダイアログ呼び出しを記録する <see cref="IDialogService"/> のテスト用スタブ
/// 確認系の応答を <see cref="ConfirmResult"/> で切り替えることで OK / キャンセル両分岐を検証できる
/// </summary>
public sealed class StubDialogService : IDialogService
{
    /// <summary>Confirm / ConfirmWarning が返す固定応答（既定は true = OK）</summary>
    public bool ConfirmResult { get; set; } = true;

    /// <summary>Confirm に渡されたメッセージの記録</summary>
    public List<string> ConfirmMessages { get; } = new();

    /// <summary>ConfirmWarning に渡されたメッセージの記録</summary>
    public List<string> WarningConfirmMessages { get; } = new();

    /// <summary>ShowInformation に渡されたメッセージの記録</summary>
    public List<string> InformationMessages { get; } = new();

    /// <summary>ShowError に渡されたメッセージの記録</summary>
    public List<string> ErrorMessages { get; } = new();

    /// <summary>ShowInformationDetails に渡された (要約, 詳細, タイトル) の記録</summary>
    public List<(
        string Message,
        string Details,
        string Title
    )> InformationDetailsMessages { get; } = new();

    /// <summary>ShowErrorDetails に渡された (要約, 詳細, タイトル) の記録</summary>
    public List<(string Message, string Details, string Title)> ErrorDetailsMessages { get; } =
        new();

    /// <summary>メッセージを記録し <see cref="ConfirmResult"/> を返す</summary>
    public bool Confirm(string message, string title)
    {
        ConfirmMessages.Add(message);
        return ConfirmResult;
    }

    /// <summary>メッセージを記録し <see cref="ConfirmResult"/> を返す</summary>
    public bool ConfirmWarning(string message, string title)
    {
        WarningConfirmMessages.Add(message);
        return ConfirmResult;
    }

    /// <summary>情報メッセージを記録する</summary>
    public void ShowInformation(string message, string title) => InformationMessages.Add(message);

    /// <summary>エラーメッセージを記録する</summary>
    public void ShowError(string message, string title) => ErrorMessages.Add(message);

    /// <summary>要約＋詳細の情報表示を (要約, 詳細, タイトル) として記録する</summary>
    public void ShowInformationDetails(string message, string details, string title) =>
        InformationDetailsMessages.Add((message, details, title));

    /// <summary>要約＋詳細のエラー表示を (要約, 詳細, タイトル) として記録する</summary>
    public void ShowErrorDetails(string message, string details, string title) =>
        ErrorDetailsMessages.Add((message, details, title));
}
