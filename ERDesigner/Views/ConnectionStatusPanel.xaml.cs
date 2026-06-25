using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ERDesigner.Services.Chat;

namespace ERDesigner.Views;

/// <summary>
/// Codex / Claude 接続タブで共用する状態パネル。
/// 「状態ドット ＋ サマリー ＋ 再確認」を共通骨格とし、下段スロット（<see cref="Body"/>）に
/// 各タブ固有の操作・案内を差し込む。
/// </summary>
public partial class ConnectionStatusPanel : UserControl
{
    /// <summary>状態ドットの健全度（緑/灰/赤）</summary>
    public static readonly DependencyProperty StatusLevelProperty = DependencyProperty.Register(
        nameof(StatusLevel),
        typeof(ConnectionHealth),
        typeof(ConnectionStatusPanel),
        new PropertyMetadata(ConnectionHealth.Pending)
    );

    /// <summary>状態サマリー文言</summary>
    public static readonly DependencyProperty SummaryTextProperty = DependencyProperty.Register(
        nameof(SummaryText),
        typeof(string),
        typeof(ConnectionStatusPanel),
        new PropertyMetadata(string.Empty)
    );

    /// <summary>「再確認」ボタンのコマンド</summary>
    public static readonly DependencyProperty RefreshCommandProperty = DependencyProperty.Register(
        nameof(RefreshCommand),
        typeof(ICommand),
        typeof(ConnectionStatusPanel),
        new PropertyMetadata(null)
    );

    /// <summary>下段スロットに表示する内容</summary>
    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body),
        typeof(object),
        typeof(ConnectionStatusPanel),
        new PropertyMetadata(null)
    );

    /// <summary>状態パネルを生成する</summary>
    public ConnectionStatusPanel()
    {
        InitializeComponent();
    }

    /// <inheritdoc cref="StatusLevelProperty" />
    public ConnectionHealth StatusLevel
    {
        get => (ConnectionHealth)GetValue(StatusLevelProperty);
        set => SetValue(StatusLevelProperty, value);
    }

    /// <inheritdoc cref="SummaryTextProperty" />
    public string SummaryText
    {
        get => (string)GetValue(SummaryTextProperty);
        set => SetValue(SummaryTextProperty, value);
    }

    /// <inheritdoc cref="RefreshCommandProperty" />
    public ICommand? RefreshCommand
    {
        get => (ICommand?)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    /// <inheritdoc cref="BodyProperty" />
    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }
}
