using QuickER.AI.UI.Resources;
using QuickER.Gui.Abstractions;
using QuickER.Platform;

namespace QuickER.AI.UI;

/// <summary>チャットの回答の中のリンクを、確認なしで既定のブラウザで開く</summary>
/// <remarks>
/// 受け取った URL も <see cref="UrlPolicy.IsSafe"/> でもう一度確かめる。組み立て側が通したものしか来ない前提だが、
/// 開く経路はここ 1 本なので、規則を外れた URL が渡ってもシェルへは渡さない（二重化）。
/// 開けなかったとき（既定のブラウザが無い等）は、原因の例外文を添えてエラーのモーダルで知らせる
/// （失敗の報告は提示先に依らずモーダル＝<see cref="IDialogService"/> の規約）。
/// </remarks>
public sealed class ShellChatLinkOpener : IChatLinkOpener
{
    /// <summary>失敗を知らせるダイアログ</summary>
    private readonly IDialogService _dialogs;

    /// <summary>失敗のダイアログの題名（ダイアログごとのウィンドウ名）</summary>
    private readonly string _errorTitle;

    /// <summary>URL をシェルへ渡す処理（テストで差し替える）</summary>
    private readonly Action<string> _launch;

    /// <summary>リンクを開く処理を組み立てる</summary>
    /// <param name="dialogs">失敗を知らせるダイアログ</param>
    /// <param name="errorTitle">失敗のダイアログの題名</param>
    /// <param name="launch">URL をシェルへ渡す処理（省略時は既定のブラウザで開く）</param>
    public ShellChatLinkOpener(
        IDialogService dialogs,
        string errorTitle,
        Action<string>? launch = null
    )
    {
        _dialogs = dialogs;
        _errorTitle = errorTitle;
        _launch = launch ?? (url => ExternalLauncher.OpenUrl(new Uri(url)));
    }

    /// <inheritdoc />
    public void Open(Uri uri)
    {
        if (!UrlPolicy.IsSafe(uri))
        {
            return;
        }

        try
        {
            _launch(uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            // クリックのハンドラから例外を漏らすとアプリの未処理例外になる。開けなかったことは必ず知らせる
            _dialogs.ShowError(
                string.Format(Strings.Markdown_OpenLinkFailedFormat, uri.AbsoluteUri, ex.Message),
                _errorTitle
            );
        }
    }
}
