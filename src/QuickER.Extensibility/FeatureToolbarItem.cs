using System.Windows.Input;

namespace QuickER.Extensibility;

/// <summary>
/// フィーチャーモジュールがホストのツールバーへ寄与する、ボタン 1 個分の記述子。
/// </summary>
/// <remarks>
/// ホスト（QuickER.Gui）はモジュールから受け取ったこの記述子群をツールバーへ並べる。
/// <see cref="ICommand"/> は <c>System.Windows.Input</c>（net10.0 の System.ObjectModel）にあり、
/// WPF アセンブリ参照を必要としない。
/// </remarks>
/// <param name="Icon">ツールバーアイコン（絵文字 1 文字）</param>
/// <param name="Label">ボタンキャプション（ローカライズ済み文字列）</param>
/// <param name="Tooltip">ツールチップ（<c>null</c> なら無し）</param>
/// <param name="Command">押下時に実行するコマンド</param>
public sealed record FeatureToolbarItem(
    string Icon,
    string Label,
    string? Tooltip,
    ICommand Command
);
