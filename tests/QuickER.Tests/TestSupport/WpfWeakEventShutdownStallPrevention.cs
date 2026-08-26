using System.Runtime.CompilerServices;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// WPF の WeakEventTable がプロセス終了を停滞させ、xunit v3 の前景スレッド監視が
/// 「全テスト合格なのに exit code 1」の偽赤を出すのを防ぐスイッチ登録。
/// </summary>
/// <remarks>
/// <para>
/// WPF の <c>MS.Internal.WeakEventTable</c> は [ThreadStatic]＝WPF に触れたスレッドごとに 1 個生成され、
/// プロセス終了時にファイナライザスレッド上の ProcessExit フックが各テーブルの
/// <c>Dispatcher.Invoke(..., timeout: 300ms)</c> を「そのテーブルを作ったスレッドの Dispatcher」へ発行する。
/// テストスイートは STA スレッドを使い捨てるため宛先スレッドは既に死んでおり（Dispatcher 未シャットダウン＝
/// スキップ判定も効かない）、Invoke は必ずタイムアウトまで待つ。停滞は 300ms × 残存テーブル数で、
/// 実測ダンプでは 38 テーブル ≒ 11.4 秒だった（GC が回収済みなら短く、残っていれば長い＝間欠性の正体）。
/// </para>
/// <para>
/// xunit v3 4.0.0 は「テスト実行完了後もプロセスが終了しない」状態を 10 秒
/// （<c>shutdownForegroundThreadWaitSeconds</c> 既定値）で打ち切り、
/// <c>[FATAL ERROR] Xunit.Sdk.TestPipelineException: Foreground threads were left running, forcing process exit</c>
/// として exit code 1 にする。上記の停滞が閾値をまたぐと全テスト合格のまま失敗する。
/// </para>
/// <para>
/// WPF 側に用意された互換スイッチでシャットダウン時の Invoke を丸ごと無効化する
/// （テーブルは自スレッドでの部分クリーンアップに退化するが、プロセス終了時のため実害はない）。
/// スイッチの正本は dotnet/wpf の <c>BaseAppContextSwitches.SwitchDoNotInvokeInWeakEventTableShutdownListener</c>。
/// 影響はこのテストアセンブリのみで、製品（単一 UI スレッド＋正規のシャットダウンを踏む GUI）には関係しない。
/// </para>
/// </remarks>
internal static class WpfWeakEventShutdownStallPrevention
{
    /// <summary>エントリポイント実行前に WPF の互換スイッチを立てる</summary>
    [ModuleInitializer]
    internal static void Register()
    {
        AppContext.SetSwitch(
            "Switch.MS.Internal.DoNotInvokeInWeakEventTableShutdownListener",
            true
        );
    }
}
