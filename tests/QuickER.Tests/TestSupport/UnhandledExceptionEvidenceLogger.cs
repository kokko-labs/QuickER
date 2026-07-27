using System.IO;
using System.Runtime.CompilerServices;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストプロセスの未処理例外を、プロセス終了前に標準エラーとファイルへ全文記録するロガー。
/// </summary>
/// <remarks>
/// テスト本体の例外は xunit が捕捉して失敗として報告するが、テストスレッド外
/// （Dispatcher へ投函されたコールバック・システムイベント・fire-and-forget スレッド等）の
/// 未処理例外はプロセスを即死させ、ランナーは「Catastrophic failure」と例外メッセージ 1 行しか
/// 残さない（2026-07-27 の全テスト並列実行で WPF の所有権違反
/// 「このオブジェクトは別のスレッドに所有されているため…」による即死を観測したが、
/// スタックが残らず発生源を特定できなかった）。次回発生時に発生源を特定できるよう、
/// 例外の全文（スタック・スレッド情報）を確実に保全する。
/// </remarks>
internal static class UnhandledExceptionEvidenceLogger
{
    /// <summary>エントリポイント実行前に未処理例外のフックを登録する</summary>
    [ModuleInitializer]
    internal static void Register()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var thread = Thread.CurrentThread;
            var evidence =
                $"[QuickER.Tests unhandled exception] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} "
                + $"thread={thread.ManagedThreadId}({thread.Name ?? "no-name"}) apartment={thread.GetApartmentState()}{Environment.NewLine}"
                + $"{e.ExceptionObject}{Environment.NewLine}";

            try
            {
                Console.Error.WriteLine(evidence);

                // ランナーが標準エラーを握りつぶす場合に備えてファイルにも残す（追記・プロセス並走を考慮した共有指定）
                var path = Path.Combine(
                    Path.GetTempPath(),
                    "QuickER.Tests-unhandled-exceptions.log"
                );

                using var stream = new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite
                );
                using var writer = new StreamWriter(stream);
                writer.Write(evidence);
            }
            catch
            {
                // 証拠保全自体の失敗でプロセス終了経路を乱さない
            }
        };
    }
}
