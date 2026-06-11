using ERDesigner.Services;

namespace ERDesigner.Tests.TestDoubles;

/// <summary>
/// ダイアログ表示を記録し、確認応答を固定値で返すテスト用スタブです。
/// UI を表示せずに、確認の OK / キャンセル両分岐と通知メッセージを検証できます。
/// </summary>
public sealed class StubDialogService : IDialogService
{
    /// <summary>Confirm / ConfirmWarning が返す応答です (既定: OK)。</summary>
    public bool ConfirmResult { get; set; } = true;

    /// <summary>Confirm に渡されたメッセージの記録です。</summary>
    public List<string> ConfirmMessages { get; } = new();

    /// <summary>ConfirmWarning に渡されたメッセージの記録です。</summary>
    public List<string> WarningConfirmMessages { get; } = new();

    /// <summary>ShowInformation に渡されたメッセージの記録です。</summary>
    public List<string> InformationMessages { get; } = new();

    /// <summary>ShowError に渡されたメッセージの記録です。</summary>
    public List<string> ErrorMessages { get; } = new();

    public bool Confirm(string message, string title)
    {
        ConfirmMessages.Add(message);
        return ConfirmResult;
    }

    public bool ConfirmWarning(string message, string title)
    {
        WarningConfirmMessages.Add(message);
        return ConfirmResult;
    }

    public void ShowInformation(string message, string title) => InformationMessages.Add(message);

    public void ShowError(string message, string title) => ErrorMessages.Add(message);
}
