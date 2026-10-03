using System.Windows;
using System.Windows.Controls;
using AwesomeAssertions;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// <see cref="ChatScrollFollower"/> が、内容の伸びに「末尾付近を見ていたときだけ」追従することを検証するテストクラス。
/// </summary>
/// <remarks>実際の <see cref="ScrollViewer"/> を画面外のウィンドウへ載せ、レイアウトを通して確かめる。</remarks>
public class ChatScrollFollowerTests
{
    /// <summary>追従させたスクロール領域（高さ 200）に、長い文字列を載せたウィンドウで検証を実行する</summary>
    private static void WithFollowedViewer(Action<ScrollViewer, TextBlock> assert)
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var text = new TextBlock
            {
                Text = string.Join("\n", Enumerable.Range(0, 40).Select(i => $"line {i}")),
            };
            var viewer = new ScrollViewer
            {
                Height = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = text,
            };
            ChatScrollFollower.Attach(viewer);

            var window = new Window
            {
                Content = viewer,
                Width = 300,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000,
                ShowActivated = false,
                ShowInTaskbar = false,
            };
            window.Show();

            try
            {
                WpfApplicationTestSupport.DoEvents();
                viewer.ScrollableHeight.Should().BeGreaterThan(0, "スクロールできる状態を作る");
                assert(viewer, text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>テキストを伸ばしてレイアウトを通す</summary>
    private static void Grow(TextBlock text)
    {
        text.Text += string.Concat(Enumerable.Range(0, 20).Select(i => $"\nmore {i}"));
        WpfApplicationTestSupport.DoEvents();
    }

    /// <summary>末尾を見ているときに内容が伸びると、末尾へ追従することを検証する</summary>
    [Fact(DisplayName = "末尾を見ているときに内容が伸びると末尾へ追従する")]
    public void Grow_WhenAtBottom_ScrollsToEnd()
    {
        WithFollowedViewer(
            (viewer, text) =>
            {
                // ScrollToEnd は位置を「無限大」として覚え、以後の伸びにも自然に付いていくため使わない。
                // 利用者がホイールなどで末尾まで戻した状態（有限の位置）を作る
                viewer.ScrollToVerticalOffset(viewer.ScrollableHeight);
                WpfApplicationTestSupport.DoEvents();
                var before = viewer.ScrollableHeight;
                viewer.VerticalOffset.Should().Be(before, "末尾を見ている状態を作る");

                Grow(text);

                viewer.ScrollableHeight.Should().BeGreaterThan(before);
                viewer.VerticalOffset.Should().Be(viewer.ScrollableHeight);
            }
        );
    }

    /// <summary>上へスクロールして読み返しているときに内容が伸びても、表示位置を動かさないことを検証する</summary>
    [Fact(DisplayName = "読み返し中に内容が伸びても表示位置を動かさない")]
    public void Grow_WhenScrolledUp_KeepsOffset()
    {
        WithFollowedViewer(
            (viewer, text) =>
            {
                viewer.ScrollToVerticalOffset(0);
                WpfApplicationTestSupport.DoEvents();

                Grow(text);

                viewer.VerticalOffset.Should().Be(0);
            }
        );
    }
}
