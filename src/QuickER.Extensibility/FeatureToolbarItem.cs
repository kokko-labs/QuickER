using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace QuickER.Extensibility;

/// <summary>
/// フィーチャーモジュールがホストのツールバーへ寄与する、ボタン 1 個分の記述子。
/// </summary>
/// <remarks>
/// ホスト（QuickER.Gui）はモジュールから受け取ったこの記述子群をツールバーへ並べる。
/// <see cref="ICommand"/> は <c>System.Windows.Input</c>（net10.0 の System.ObjectModel）にあり、
/// WPF アセンブリ参照を必要としない。
/// <para>
/// <see cref="Tooltip"/> と <see cref="BeginsGroup"/> は実行中に切り替わり得るため
/// <see cref="INotifyPropertyChanged"/> を実装する（WPF のツールバー UI が変更へ追随する）。
/// Extensibility は CommunityToolkit 非依存の純契約プロジェクトのため、INPC は手実装する。
/// </para>
/// </remarks>
public sealed class FeatureToolbarItem : INotifyPropertyChanged
{
    private string? _tooltip;
    private bool _beginsGroup;

    /// <summary>ボタン記述子を生成する</summary>
    /// <param name="icon">ツールバーアイコン（絵文字 1 文字）</param>
    /// <param name="label">ボタンキャプション（ローカライズ済み文字列）</param>
    /// <param name="tooltip">ツールチップ（<c>null</c> なら無し・動的切替可）</param>
    /// <param name="command">押下時に実行するコマンド</param>
    /// <param name="beginsGroup">true のときボタンの直前にツールバーのグループ区切り（セパレータ）を描画する</param>
    public FeatureToolbarItem(
        string icon,
        string label,
        string? tooltip,
        ICommand command,
        bool beginsGroup = false
    )
    {
        Icon = icon;
        Label = label;
        _tooltip = tooltip;
        Command = command;
        _beginsGroup = beginsGroup;
    }

    /// <summary>プロパティ変更通知（<see cref="Tooltip"/> / <see cref="BeginsGroup"/> の動的切替で発火）</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>ツールバーアイコン（絵文字 1 文字）</summary>
    public string Icon { get; }

    /// <summary>ボタンキャプション（ローカライズ済み文字列）</summary>
    public string Label { get; }

    /// <summary>押下時に実行するコマンド</summary>
    public ICommand Command { get; }

    /// <summary>ツールチップ（<c>null</c> なら無し）。実行中に切り替え可能（例: 対象 DBMS の切替に追従して文言を差し替える）</summary>
    public string? Tooltip
    {
        get => _tooltip;
        set => SetField(ref _tooltip, value);
    }

    /// <summary>true のときボタンの直前にツールバーのグループ区切り（セパレータ）を描画する。実行中に切り替え可能</summary>
    public bool BeginsGroup
    {
        get => _beginsGroup;
        set => SetField(ref _beginsGroup, value);
    }

    /// <summary>フィールドを更新し、値が変化したときのみ <see cref="PropertyChanged"/> を発火する</summary>
    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
