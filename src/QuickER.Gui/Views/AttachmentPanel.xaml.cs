using System.Windows;
using System.Windows.Controls;
using QuickER.ViewModels;

namespace QuickER.Views;

/// <summary>
/// 添付チップ列＋クリップ（添付追加）ボタンの共通 UserControl。
/// AiChatDialog / MockGenerationDialog の両方が入力欄の上に配置し、添付 UI を二重実装しない。
/// </summary>
/// <remarks>
/// チップ表示・削除・可否（無効化＋理由ツールチップ）は <see cref="AttachmentListViewModel"/> が担う。
/// クリップボタン押下は <see cref="AttachRequested"/> でホスト（ダイアログ）へ通知し、
/// ファイル選択ダイアログ（<c>IFileDialogService</c>）の表示・パス受け渡しはホスト側で行う。
/// </remarks>
public partial class AttachmentPanel : UserControl
{
    /// <summary>クリップボタンが押されたことをホストへ通知するルーティングイベント</summary>
    public static readonly RoutedEvent AttachRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(AttachRequested),
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(AttachmentPanel)
    );

    /// <summary>束ねる添付 VM 部品（チップ列・可否・追加/削除ロジック）</summary>
    public static readonly DependencyProperty AttachmentListProperty = DependencyProperty.Register(
        nameof(AttachmentList),
        typeof(AttachmentListViewModel),
        typeof(AttachmentPanel),
        new PropertyMetadata(null)
    );

    /// <summary>UserControl を初期化する</summary>
    public AttachmentPanel()
    {
        InitializeComponent();
    }

    /// <summary>クリップボタン押下の通知イベント（ホストがファイル選択を開く起点）</summary>
    public event RoutedEventHandler AttachRequested
    {
        add => AddHandler(AttachRequestedEvent, value);
        remove => RemoveHandler(AttachRequestedEvent, value);
    }

    /// <summary>束ねる添付 VM 部品</summary>
    public AttachmentListViewModel? AttachmentList
    {
        get => (AttachmentListViewModel?)GetValue(AttachmentListProperty);
        set => SetValue(AttachmentListProperty, value);
    }

    /// <summary>クリップボタン押下でホストへ添付要求を通知する</summary>
    private void AttachButton_Click(object sender, RoutedEventArgs e) =>
        RaiseEvent(new RoutedEventArgs(AttachRequestedEvent, this));
}
