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
/// <see cref="Tooltip"/> は実行中に切り替わり得るため <see cref="INotifyPropertyChanged"/> を
/// 実装する（WPF のツールバー UI が変更へ追随する）。
/// Extensibility は CommunityToolkit 非依存の純契約プロジェクトのため、INPC は手実装する。
/// <see cref="BeginsGroup"/> も通知は出すが、ホスト側の折返しは起動時に 1 回だけ組み立てられるため
/// 実行中に変えても見た目は変わらない（<see cref="BeginsGroup"/> の説明を参照）。
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
    /// <param name="beginsGroup">true のときボタンの直前にツールバーのグループ区切り（セパレータ）を描画する（起動時の組み立てでのみ反映）</param>
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

    /// <summary>true のときボタンの直前にツールバーのグループ区切り（セパレータ）を描画する</summary>
    /// <remarks>
    /// <b>反映されるのは起動時の組み立てのときだけ</b>。ホストは受け取ったボタン列を
    /// この値で区切ってグループへ分割し、ビューは分割済みのグループへ束縛するため、
    /// 実行中にこの値を変えても（通知は出るが）折返しの単位は変わらない。
    /// 実行中に切り替えて効くのは <see cref="Tooltip"/> のほうで、そちらはビューが直接束縛している。
    /// </remarks>
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
