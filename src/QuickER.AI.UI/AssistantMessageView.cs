using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickER.AI.UI;

/// <summary>
/// アシスタントの回答の吹き出しの中身。テキスト表示（読み取り専用の <see cref="TextBox"/>）と
/// Markdown 表示（読み取り専用の <see cref="RichTextBox"/>）を切り替える（AI チャット・AI モック生成が共有する）。
/// </summary>
/// <remarks>
/// <para>
/// Markdown 表示にするのは <see cref="ShowMarkdown"/> が true で、かつ応答が終わっている（<see cref="IsStreaming"/> が false）
/// ときだけ。応答中は伸び続ける文章を組み立て直さないよう、テキストで表示する。
/// </para>
/// <para>
/// 組み立て（<see cref="ChatMarkdownRenderer.Render"/>）が失敗したときは、その吹き出しだけテキスト表示へ落とす。
/// AI の回答は外部の入力で、入れ子が深すぎる文章では組み立てが例外で断る。依存関係プロパティの変更から例外を
/// 漏らすとアプリの未処理例外になるため、ここで受け止める。同じ本文で何度も組み立て直さないよう、失敗した本文は覚えておく。
/// </para>
/// <para>
/// FlowDocument は Markdown 表示が必要になった時点で初めて組み立てる（テキスト表示のあいだは作らない）。
/// </para>
/// </remarks>
public sealed class AssistantMessageView : ContentControl
{
    /// <summary>本文の文字色（テキスト表示・Markdown 表示で共通）</summary>
    private static readonly Brush MessageForeground = CreateForeground();

    /// <summary>本文の <see cref="DependencyProperty"/></summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(AssistantMessageView),
        new PropertyMetadata(string.Empty, OnDisplayInputChanged)
    );

    /// <summary>応答中かどうかの <see cref="DependencyProperty"/></summary>
    public static readonly DependencyProperty IsStreamingProperty = DependencyProperty.Register(
        nameof(IsStreaming),
        typeof(bool),
        typeof(AssistantMessageView),
        new PropertyMetadata(false, OnDisplayInputChanged)
    );

    /// <summary>Markdown 表示にするかの <see cref="DependencyProperty"/></summary>
    public static readonly DependencyProperty ShowMarkdownProperty = DependencyProperty.Register(
        nameof(ShowMarkdown),
        typeof(bool),
        typeof(AssistantMessageView),
        new PropertyMetadata(true, OnDisplayInputChanged)
    );

    /// <summary>リンクを開く処理の <see cref="DependencyProperty"/></summary>
    public static readonly DependencyProperty LinkOpenerProperty = DependencyProperty.Register(
        nameof(LinkOpener),
        typeof(IChatLinkOpener),
        typeof(AssistantMessageView),
        new PropertyMetadata(null, OnLinkOpenerChanged)
    );

    /// <summary>テキスト表示の部品（初回の表示で作る）</summary>
    private TextBox? _textView;

    /// <summary>Markdown 表示の部品（初回の Markdown 表示で作る）</summary>
    private RichTextBox? _markdownView;

    /// <summary>いま Markdown 表示に載っている本文（同じ本文なら組み立て直さない）</summary>
    private string? _renderedText;

    /// <summary>組み立てに失敗した本文（同じ本文で組み立てを繰り返さない）</summary>
    private string? _failedText;

    /// <summary>内容の幅を測ったときの条件（本文・書体・大きさ・描画方式。同じなら測り直さない）</summary>
    private (string Text, FontFamily Family, double Size, TextFormattingMode Mode)? _fitKey;

    /// <summary>測った内容の幅（RichTextBox の内側の余白を含まない。測れない文書は null＝最大幅）</summary>
    private double? _contentWidth;

    /// <summary>
    /// 内容の幅に足す余裕（論理ピクセル）。<see cref="RichTextBox"/> の内側の余白は値から求めて足すが、
    /// 測る側と表示する側の文字幅の丸めの差（100% 表示の実測で 0.22 まで。余裕なしでは最後の文字が次の行へ落ちる）と、
    /// 表示倍率による丸めの違いを見込んで広げる。狭すぎると文字が欠けるので、広いほうへ倒す。
    /// </summary>
    internal const double FitSafetyMargin = 4;

    /// <summary>部品を作り、テキスト表示から始める</summary>
    public AssistantMessageView()
    {
        Focusable = false;
        Refresh();
    }

    /// <summary>回答の本文</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>応答中か（応答中はテキストで表示する）</summary>
    public bool IsStreaming
    {
        get => (bool)GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    /// <summary>Markdown 表示にするか（ウィンドウのトグルに連動する）</summary>
    public bool ShowMarkdown
    {
        get => (bool)GetValue(ShowMarkdownProperty);
        set => SetValue(ShowMarkdownProperty, value);
    }

    /// <summary>リンクを開く処理（未設定のあいだはテキストで表示する）</summary>
    public IChatLinkOpener? LinkOpener
    {
        get => (IChatLinkOpener?)GetValue(LinkOpenerProperty);
        set => SetValue(LinkOpenerProperty, value);
    }

    /// <summary>いま Markdown 表示になっているか（テスト・表示確認用）</summary>
    internal bool IsShowingMarkdown =>
        ReferenceEquals(Content, _markdownView) && Content is not null;

    /// <summary>
    /// 文章だけの回答（段落と見出しだけ）を Markdown 表示しているときは、吹き出しを内容の幅へ縮める。
    /// それ以外の回答と、測れない回答は最大幅のまま（上限は親の吹き出しの最大幅とウィンドウの幅）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 幅は Markdown 表示の部品の <see cref="FrameworkElement.MaxWidth"/> として与えるので、ウィンドウを狭めて
    /// 与えられる幅が内容の幅を下回れば、そのまま折り返す（測った幅は「これより広くはしない」という上限）。
    /// </para>
    /// <para>
    /// 内側の余白（文書の <see cref="FlowDocument.PagePadding"/>）は、<see cref="RichTextBox"/> が最初のレイアウトの途中で
    /// 自分の既定（左右 5）へ置き換える。置き換わる前の値で幅を決めると内容より狭くなり、最後の文字が次の行へ落ちる。
    /// そのため先に部品を測ってから幅を求め、上限が変わったときだけ測り直す。
    /// </para>
    /// </remarks>
    protected override Size MeasureOverride(Size constraint)
    {
        var desired = base.MeasureOverride(constraint);

        if (Content is not RichTextBox markdownView || !IsShowingMarkdown)
        {
            return desired;
        }

        var limit = FitWidth(markdownView) ?? double.PositiveInfinity;

        if (markdownView.MaxWidth.Equals(limit))
        {
            return desired;
        }

        markdownView.MaxWidth = limit;
        return base.MeasureOverride(constraint);
    }

    /// <summary>Markdown 表示の部品の、内容に合わせた幅を求める（測れないときは null）</summary>
    private double? FitWidth(RichTextBox markdownView)
    {
        var document = markdownView.Document;
        var key = (
            _renderedText ?? string.Empty,
            markdownView.FontFamily,
            markdownView.FontSize,
            TextOptions.GetTextFormattingMode(markdownView)
        );

        if (_fitKey != key)
        {
            _fitKey = key;
            _contentWidth = ChatMarkdownWidth.Measure(document, key.Item4);
        }

        // 内側の余白は測った時点の値を毎回読む（RichTextBox が後から置き換えるため、覚えておかない）
        return _contentWidth is { } width
            ? width
                + document.PagePadding.Left
                + document.PagePadding.Right
                + markdownView.Padding.Left
                + markdownView.Padding.Right
                + markdownView.BorderThickness.Left
                + markdownView.BorderThickness.Right
                + FitSafetyMargin
            : null;
    }

    /// <summary>表示を決める値が変わったら、表示を選び直す</summary>
    private static void OnDisplayInputChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e
    ) => ((AssistantMessageView)d).Refresh();

    /// <summary>リンクを開く処理が変わったら、組み立て済みの文書を捨てて選び直す（古い opener を握ったリンクを残さない）</summary>
    private static void OnLinkOpenerChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e
    )
    {
        var view = (AssistantMessageView)d;
        view._renderedText = null;
        view._failedText = null;
        view.Refresh();
    }

    /// <summary>現在の値から、テキスト表示と Markdown 表示のどちらを出すかを決めて反映する</summary>
    private void Refresh()
    {
        var text = Text ?? string.Empty;

        if (
            ShowMarkdown
            && !IsStreaming
            && LinkOpener is { } opener
            && TryShowMarkdown(text, opener)
        )
        {
            return;
        }

        var textView = _textView ??= CreateTextView();
        textView.Text = text;
        Content = textView;
    }

    /// <summary>Markdown 表示を出す。組み立てに失敗したときは false を返す（呼び出し側がテキスト表示へ落とす）</summary>
    private bool TryShowMarkdown(string text, IChatLinkOpener opener)
    {
        if (text == _failedText)
        {
            return false;
        }

        var markdownView = _markdownView ??= CreateMarkdownView();

        if (text != _renderedText)
        {
            try
            {
                markdownView.Document = ChatMarkdownRenderer.Render(text, opener);
                _renderedText = text;
            }
            catch (Exception)
            {
                // 外部の入力（入れ子が深すぎる回答など）で組み立てられない。この吹き出しはテキストで出す
                _failedText = text;
                _renderedText = null;
                return false;
            }
        }

        Content = markdownView;
        return true;
    }

    /// <summary>テキスト表示の部品を作る（従来の吹き出しの TextBox と同じ見た目）</summary>
    private static TextBox CreateTextView() =>
        new()
        {
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = MessageForeground,
            FontSize = 13,
        };

    /// <summary>Markdown 表示の部品を作る（読み取り専用・リンクは押せる・文字は選択してコピーできる）</summary>
    private static RichTextBox CreateMarkdownView() =>
        new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            IsReadOnly = true,
            IsDocumentEnabled = true,
            IsUndoEnabled = false,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = MessageForeground,
            FontSize = 13,
        };

    /// <summary>本文の文字色を作る</summary>
    private static Brush CreateForeground()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37));
        brush.Freeze();
        return brush;
    }
}
