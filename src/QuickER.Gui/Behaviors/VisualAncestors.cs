using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace QuickER.Behaviors;

/// <summary>キャンバスのビヘイビアが視覚ツリーを上へたどる唯一の口</summary>
/// <remarks>
/// たどり方（<see cref="VisualTreeHelper.GetParent"/> で自分から根まで）を 1 か所に置く。ビヘイビアが自前で
/// たどる写しを持たないことは、ソースの規則 <c>behavior-visual-ancestor-copies</c> が見張る。
/// </remarks>
internal static class VisualAncestors
{
    /// <summary>自分と祖先を、近い順に並べる（<paramref name="node"/> が null なら空）</summary>
    public static IEnumerable<DependencyObject> SelfAndAncestors(DependencyObject? node)
    {
        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            yield return current;
        }
    }

    /// <summary>自分と祖先のうち、最初に見つかった <typeparamref name="T"/>（無ければ null）</summary>
    public static T? FirstOrSelf<T>(DependencyObject? node)
        where T : DependencyObject => SelfAndAncestors(node).OfType<T>().FirstOrDefault();
}
