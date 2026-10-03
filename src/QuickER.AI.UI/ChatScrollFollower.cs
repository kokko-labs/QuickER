using System.Windows.Controls;

namespace QuickER.AI.UI;

/// <summary>
/// チャット欄の <see cref="ScrollViewer"/> を、ストリーミングで伸びる吹き出しに追従させる共有部品
/// （AI チャット・AI モック生成のチャット欄が使う）。
/// </summary>
/// <remarks>
/// <para>
/// 新しい吹き出しが増えたときの末尾へのスクロールは、各ダイアログがメッセージ一覧の変更で行う。
/// そのとき使う <see cref="ScrollViewer.ScrollToEnd"/> は位置を「末尾」として覚えるので、直後のストリーミングの伸びには
/// それだけで付いていく。ところが利用者がホイールなどで一度でも動かすと（末尾まで戻しても）位置は数値になり、
/// 一覧の変わらないストリーミングの伸びは画面の外へ出ていく。ここでは内容の高さが伸びたことを
/// <see cref="ScrollViewer.ScrollChanged"/> で拾い、末尾へ付け直す。
/// </para>
/// <para>
/// 追従するのは、伸びる直前に末尾付近を見ていたときだけ。上へスクロールして読み返している最中に
/// 末尾へ引き戻さないため。
/// </para>
/// </remarks>
public static class ChatScrollFollower
{
    /// <summary>「末尾付近」とみなす、末尾からの距離（論理ピクセル）</summary>
    internal const double NearBottomThreshold = 24;

    /// <summary>スクロール領域を、内容の伸びに追従させる</summary>
    /// <param name="viewer">チャット欄のスクロール領域</param>
    public static void Attach(ScrollViewer viewer) => viewer.ScrollChanged += OnScrollChanged;

    /// <summary>内容の高さが伸びたとき、伸びる直前に末尾付近にいたら末尾へスクロールする</summary>
    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange <= 0 || sender is not ScrollViewer viewer)
        {
            return;
        }

        // 伸びる直前の末尾の位置（直前の内容の高さ − 表示の高さ）と、いまの表示位置を比べる
        var previousBottom = e.ExtentHeight - e.ExtentHeightChange - e.ViewportHeight;

        if (e.VerticalOffset >= previousBottom - NearBottomThreshold)
        {
            viewer.ScrollToEnd();
        }
    }
}
