using ERDesigner.Services;

namespace ERDesigner.Tests.TestDoubles;

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
}
