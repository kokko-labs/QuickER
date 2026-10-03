using System.Collections.Concurrent;
using System.Diagnostics;

namespace QuickER.AI;

/// <summary>
/// 子プロセスの標準エラー出力を常時読み出し、直近行だけをリングバッファで保持するドレイナ。
/// </summary>
/// <remarks>
/// stderr を読まないままにすると、子プロセスが数 KB 書いた時点でパイプバッファが満杯になって
/// write でブロックし、stdout も進まなくなって読み取りが永久に返らない（デッドロック）。
/// 読み捨てつつ診断用に直近行だけを残す。プロセス 1 つにつき 1 つ作る（<see cref="ClaudeCodeProcessClient"/> の
/// ターン・ログイン確認と、<see cref="CodexAppServerClient"/> の接続ごと）＝直近行は作ったプロセスのものだけになる。
/// </remarks>
internal sealed class StandardErrorDrain
{
    /// <summary>診断用に保持する直近行数</summary>
    private const int MaxRetainedLines = 20;

    /// <summary>読み切りを待つ上限（子孫プロセスがパイプを握ったままでも固まらないようにする）</summary>
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentQueue<string> _lines = new();
    private readonly Task _drainTask;

    public StandardErrorDrain(Process process)
    {
        _drainTask = Task.Run(() => DrainAsync(process), CancellationToken.None);
    }

    /// <summary>保持している直近の stderr 行（古い順）</summary>
    public IReadOnlyList<string> RecentLines => _lines.ToArray();

    /// <summary>EOF までの読み切りを上限付きで待つ（超過しても保持済みの行はそのまま使える）</summary>
    public async Task WaitForCompletionAsync()
    {
        try
        {
            await _drainTask.WaitAsync(CompletionTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // 読み切れなくても保持済みの行で診断する（待ち続けない）
        }
    }

    /// <summary>EOF まで stderr を読み続け、直近 <see cref="MaxRetainedLines"/> 行だけを残す</summary>
    private async Task DrainAsync(Process process)
    {
        try
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);

                if (line is null)
                {
                    break;
                }

                _lines.Enqueue(line);

                while (_lines.Count > MaxRetainedLines && _lines.TryDequeue(out _)) { }
            }
        }
        catch
        {
            // 中断・プロセス破棄に伴うストリーム例外は無視する（診断目的のため取り逃しは許容）
        }
    }
}
