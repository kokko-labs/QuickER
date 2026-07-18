using System.Windows;
using System.Windows.Controls;

namespace QuickER.Gui.Common;

/// <summary>
/// <see cref="PasswordBox"/> の <c>Password</c> を ViewModel の string プロパティと双方向同期する添付ビヘイビア
/// </summary>
/// <remarks>
/// WPF の <see cref="PasswordBox.Password"/> はセキュリティ上バインドできないため、
/// 添付プロパティを介して PasswordChanged→VM・VM 変更→PasswordBox を橋渡しする。
/// 相互更新による無限ループは <see cref="UpdatingProperty"/> フラグで防止する。
/// PasswordChanged の購読は初回の値変更コールバックで行うため、既定値は <c>null</c> とし、
/// バインド先が空文字でも「null→空文字」の変更で確実に購読が始まるようにしている
/// （バインド先の初期値が <c>null</c> だと購読されないため、VM 側は空文字で初期化すること）。
/// </remarks>
public static class PasswordBoxBehavior
{
    /// <summary>ViewModel の string プロパティと双方向同期する添付プロパティ</summary>
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxBehavior),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundPasswordChanged
            )
        );

    /// <summary>この PasswordBox が同期対象として初期化済みか</summary>
    private static readonly DependencyProperty AttachedProperty =
        DependencyProperty.RegisterAttached(
            "Attached",
            typeof(bool),
            typeof(PasswordBoxBehavior),
            new PropertyMetadata(false)
        );

    /// <summary>相互更新の再入（無限ループ）を防ぐガードフラグ</summary>
    private static readonly DependencyProperty UpdatingProperty =
        DependencyProperty.RegisterAttached(
            "Updating",
            typeof(bool),
            typeof(PasswordBoxBehavior),
            new PropertyMetadata(false)
        );

    /// <summary><see cref="BoundPasswordProperty"/> の値を取得する</summary>
    public static string GetBoundPassword(DependencyObject obj) =>
        (string)obj.GetValue(BoundPasswordProperty);

    /// <summary><see cref="BoundPasswordProperty"/> の値を設定する</summary>
    public static void SetBoundPassword(DependencyObject obj, string value) =>
        obj.SetValue(BoundPasswordProperty, value);

    private static bool GetAttached(DependencyObject obj) => (bool)obj.GetValue(AttachedProperty);

    private static void SetAttached(DependencyObject obj, bool value) =>
        obj.SetValue(AttachedProperty, value);

    private static bool GetUpdating(DependencyObject obj) => (bool)obj.GetValue(UpdatingProperty);

    private static void SetUpdating(DependencyObject obj, bool value) =>
        obj.SetValue(UpdatingProperty, value);

    /// <summary>VM 側の値が変化したら PasswordBox へ反映する（初回はイベント購読も行う）</summary>
    private static void OnBoundPasswordChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e
    )
    {
        if (d is not PasswordBox passwordBox)
        {
            return;
        }

        if (!GetAttached(passwordBox))
        {
            SetAttached(passwordBox, true);
            passwordBox.PasswordChanged += OnPasswordChanged;
        }

        // PasswordChanged 由来の更新（VM 反映）による再入では PasswordBox を上書きしない
        if (GetUpdating(passwordBox))
        {
            return;
        }

        var newValue = (string?)e.NewValue ?? string.Empty;

        if (passwordBox.Password != newValue)
        {
            passwordBox.Password = newValue;
        }
    }

    /// <summary>PasswordBox の変更内容を VM（添付プロパティ）へ反映する</summary>
    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox)
        {
            return;
        }

        SetUpdating(passwordBox, true);
        try
        {
            SetBoundPassword(passwordBox, passwordBox.Password);
        }
        finally
        {
            SetUpdating(passwordBox, false);
        }
    }
}
