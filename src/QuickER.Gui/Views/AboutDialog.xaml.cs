using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using QuickER.Resources;
using QuickER.Services;

namespace QuickER.Views;

/// <summary>アプリの版・実行環境・著作権・リポジトリ・ドキュメントを表示するバージョン情報ダイアログ</summary>
/// <remarks>
/// 表示するだけで状態をほぼ持たないため ViewModel は設けず、コードビハインドで <see cref="AboutInfo"/> を流し込む
/// （印刷オプションダイアログと同じ流儀）。表示内容の組み立ては <see cref="AboutInfo"/> 側で単体テストする
/// </remarks>
public partial class AboutDialog : Window
{
    /// <summary>「版情報をコピー」で渡す内容を保持する表示元</summary>
    private readonly AboutInfo _info;

    /// <summary>表示内容を受け取ってダイアログを初期化する</summary>
    /// <param name="info">表示する版・実行環境・著作権・リポジトリ・ドキュメント</param>
    public AboutDialog(AboutInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        InitializeComponent();

        _info = info;
        VersionText.Text = string.Format(Strings.About_Version, info.Version);
        RuntimeText.Text = info.Runtime;
        CopyrightText.Text = info.Copyright;
        CopyrightText.Visibility = string.IsNullOrWhiteSpace(info.Copyright)
            ? Visibility.Collapsed
            : Visibility.Visible;
        SetLink(RepositoryLine, RepositoryLink, RepositoryLinkText, info.RepositoryUrl);
        SetLink(DocumentationLine, DocumentationLink, DocumentationLinkText, info.DocumentationUrl);
    }

    /// <summary>リンクの行へ URL を設定する。URL として読めないときは行ごと隠す（表示を開いただけで例外にしない）</summary>
    private static void SetLink(TextBlock line, Hyperlink link, Run text, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            link.NavigateUri = uri;
            text.Text = url;
        }
        else
        {
            line.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>リポジトリ・ドキュメントのリンクを既定のブラウザーで開く</summary>
    private void OnLinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
            when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 既定のブラウザーが無い等で開けない。URL は画面に出ているので、コピー結果の欄で知らせるだけにする
            CopyStatusText.Text = Strings.About_OpenLinkFailed;
        }

        e.Handled = true;
    }

    /// <summary>版・ビルド・ランタイム・OS をクリップボードへコピーし、結果をボタン横に表示する</summary>
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_info.ToClipboardText());
            CopyStatusText.Text = Strings.About_Copied;
        }
        catch (COMException)
        {
            // 他のアプリがクリップボードを開いたままのときに起きる（CLIPBRD_E_CANT_OPEN）。再度押せば通ることが多い
            CopyStatusText.Text = Strings.About_CopyFailed;
        }
    }
}
