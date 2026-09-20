using System.Windows;
using System.Windows.Controls;

namespace QuickER.Behaviors;

/// <summary><see cref="ColumnDefinition"/> を幅ごと畳む（0 にする）添付ビヘイビア</summary>
/// <remarks>
/// <para>
/// 列の中身を <see cref="UIElement.Visibility"/> で消しても、列そのものは宣言幅を占有し続ける。
/// 左右のパネルを隠してキャンバスを広げるには、列の <see cref="ColumnDefinition.Width"/> を
/// 0 にする必要がある。
/// </para>
/// <para>
/// <b>前提</b>: 畳む・戻すは交互にしか起きない。呼び出し口は <see cref="IsExpandedProperty"/> の
/// 変更コールバックだけで、依存関係プロパティは同じ値では再発火しないため、畳んだ列をもう一度
/// 畳む経路が存在しない（あれば退避値が 0 で上書きされ、元の幅が失われる）。この型へ別の
/// 呼び出し口を足すときは、その前提が保てるかを先に確かめること。
/// </para>
/// <para>
/// 畳むときは現在の幅・最小幅を退避し、戻すときにその値を復元する（宣言値へ戻さない）。
/// <see cref="GridSplitter"/> はドラッグのたびに <see cref="ColumnDefinition.Width"/> へ
/// 直接書き込むため、宣言値へ戻す実装だと「幅を調整 → 隠す → 戻す」で調整が消える。
/// <see cref="ColumnDefinition.MinWidth"/> も退避対象とするのは、これが残っていると
/// 幅 0 を指定しても最小幅までしか縮まないため。
/// </para>
/// </remarks>
public static class CollapsibleColumnBehavior
{
    /// <summary>列を展開する（<c>true</c>）か畳む（<c>false</c>）かを表す添付プロパティ</summary>
    /// <remarks>
    /// 既定は展開＝この添付プロパティを付けない列、および束縛が <c>true</c> を返す列は
    /// 宣言した幅のまま何も変更されない。
    /// </remarks>
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.RegisterAttached(
            "IsExpanded",
            typeof(bool),
            typeof(CollapsibleColumnBehavior),
            new PropertyMetadata(true, OnIsExpandedChanged)
        );

    /// <summary><see cref="IsExpandedProperty"/> を設定する</summary>
    public static void SetIsExpanded(DependencyObject d, bool value) =>
        d.SetValue(IsExpandedProperty, value);

    /// <summary><see cref="IsExpandedProperty"/> を取得する</summary>
    public static bool GetIsExpanded(DependencyObject d) => (bool)d.GetValue(IsExpandedProperty);

    /// <summary>畳む直前の幅・最小幅を列ごとに保持する添付プロパティ（展開中は未設定）</summary>
    private static readonly DependencyProperty SavedSizeProperty =
        DependencyProperty.RegisterAttached(
            "SavedSize",
            typeof(SavedColumnSize),
            typeof(CollapsibleColumnBehavior),
            new PropertyMetadata(null)
        );

    /// <summary>展開・畳みの切替に応じて列幅を操作する</summary>
    private static void OnIsExpandedChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e
    )
    {
        if (d is not ColumnDefinition column)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            Expand(column);
        }
        else
        {
            Collapse(column);
        }
    }

    /// <summary>現在の幅・最小幅を退避してから列を 0 幅にする</summary>
    private static void Collapse(ColumnDefinition column)
    {
        column.SetValue(SavedSizeProperty, new SavedColumnSize(column.Width, column.MinWidth));

        // 最小幅を先に外す（残したままだと幅 0 を指定しても最小幅までしか縮まない）
        column.MinWidth = 0;
        column.Width = new GridLength(0);
    }

    /// <summary>退避しておいた幅・最小幅を復元する</summary>
    private static void Expand(ColumnDefinition column)
    {
        // 一度も畳んでいない列は宣言値のまま＝何もしない
        if (column.GetValue(SavedSizeProperty) is not SavedColumnSize saved)
        {
            return;
        }

        column.ClearValue(SavedSizeProperty);

        // 幅より先に最小幅を戻す（幅を戻す時点で最小幅の制約が効いている状態にする）
        column.MinWidth = saved.MinWidth;
        column.Width = saved.Width;
    }

    /// <summary>畳む直前の列サイズ</summary>
    private sealed record SavedColumnSize(GridLength Width, double MinWidth);
}
