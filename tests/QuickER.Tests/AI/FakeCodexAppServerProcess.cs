using System.IO;
using System.Reflection;
using System.Text;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="CodexAppServerClient"/> を実プロセス越しに検証するための偽の app-server
/// （powershell.exe で動く 1 行 1 JSON のスクリプト）。
/// </summary>
/// <remarks>
/// <para>
/// リフレクションでフィールドを組むと <see cref="CodexAppServerClient.StartAsync"/> の配線（受信ループの寿命・
/// ハンドシェイク）を通らないため、起動から破棄までを本物の経路で動かすためにある。規則は 3 つだけ：
/// (1) クライアントのリクエスト（method と id を持つ行）には空の result を返す
/// (2) <c>initialized</c> 通知を受けたら、テストが渡した行をそのまま送る（サーバー発のリクエスト・通知）
/// (3) クライアントの応答（id を持ち method を持たない行）は <c>test/echo</c> 通知の params に包んで送り返す
/// ＝テストは <see cref="CodexAppServerClient.NotificationReceived"/> で、クライアントが何を返したかを見られる。
/// </para>
/// <para>
/// (2) で送る行のうち、次の 4 つだけは送らずに動作の指示として扱う：<c>stderr:文字列</c>（標準エラーへ書く）・
/// <c>stdin-handle</c>（自分の標準入力のハンドルの値を <c>stdin-handle:値</c> として標準エラーへ書く＝テストが
/// 読み口を複製して、相手が止まった後もパイプを開いたままにするため）・
/// <c>exit</c>（終了する）・<c>block</c>（以後は標準入力を読まずに眠り続ける＝書き込みが詰まる相手の代役）。
/// </para>
/// <para>
/// 後始末: クライアントの破棄が stdin を閉じてプロセスツリーごと止める。それに加えてスクリプト自身も stdin の
/// EOF で終わる（テストのプロセスが落ちても孤児にならない）。<see cref="DisposeAsync"/> は念のため、
/// <see cref="StartAsync"/> で起動した powershell.exe が残っていれば止めてから一時フォルダを消す。
/// </para>
/// </remarks>
internal sealed class FakeCodexAppServerProcess : IAsyncDisposable
{
    /// <summary>偽サーバーのスクリプトのファイル名（残存プロセスの検出にも使う目印）</summary>
    internal const string ScriptFileName = "quicker-fake-codex-app-server.ps1";

    /// <summary>クライアントの応答を送り返す通知のメソッド名</summary>
    internal const string EchoMethod = "test/echo";

    private const string Script = """
        param([string]$EmitFile)
        $out = [Console]::Out
        while ($true) {
          $line = [Console]::In.ReadLine()
          if ($null -eq $line) { break }
          if ($line -cmatch '"method":"([^"]+)"') {
            $method = $Matches[1]
            if ($line -cmatch '"id":(\d+)') {
              $out.WriteLine('{"id":' + $Matches[1] + ',"result":{}}')
              $out.Flush()
            } elseif ($method -ceq 'initialized') {
              foreach ($e in [IO.File]::ReadAllLines($EmitFile)) {
                if ($e.StartsWith('stderr:')) { [Console]::Error.WriteLine($e.Substring(7)); [Console]::Error.Flush() }
                elseif ($e -ceq 'stdin-handle') {
                  Add-Type -Namespace QuickERFake -Name Native -MemberDefinition '[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern System.IntPtr GetStdHandle(int n);'
                  [Console]::Error.WriteLine('stdin-handle:' + [QuickERFake.Native]::GetStdHandle(-10).ToInt64()); [Console]::Error.Flush()
                }
                elseif ($e -ceq 'exit') { $out.Flush(); exit 0 }
                elseif ($e -ceq 'block') { $out.Flush(); Start-Sleep -Seconds 120; exit 0 }
                else { $out.WriteLine($e) }
              }
              $out.Flush()
            }
          } elseif ($line -cmatch '"id":') {
            $out.WriteLine('{"method":"test/echo","params":' + $line + '}')
            $out.Flush()
          }
        }
        """;

    private readonly string _directory;

    /// <param name="serverMessages"><c>initialized</c> 通知を受けた直後にサーバーから送る行（1 行 1 JSON）</param>
    public FakeCodexAppServerProcess(params string[] serverMessages)
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "QuickER.Tests",
            "fake-codex",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ScriptPath, Script, new UTF8Encoding(false));
        File.WriteAllLines(EmitPath, serverMessages, new UTF8Encoding(false));
    }

    private string ScriptPath => Path.Combine(_directory, ScriptFileName);

    /// <summary>次に起動する偽サーバーが <c>initialized</c> の直後に送る行を差し替える（再接続の検証用）</summary>
    public void ReplaceServerMessages(params string[] serverMessages) =>
        File.WriteAllLines(EmitPath, serverMessages, new UTF8Encoding(false));

    private string EmitPath => Path.Combine(_directory, "emit.txt");

    /// <summary>この偽サーバーを起動するクライアントを作る</summary>
    public CodexAppServerClient CreateClient() =>
        new(() =>
            (
                Path.Combine(
                    Environment.SystemDirectory,
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe"
                ),
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{ScriptPath}\" \"{EmitPath}\""
            )
        );

    /// <summary>
    /// クライアントを起動する。偽サーバーが立ち上がらない（実行ポリシー・powershell.exe の不在など）ときは、
    /// その旨と偽サーバーの標準エラー出力を添えてテストの失敗にする
    /// </summary>
    public async Task StartAsync(CodexAppServerClient client, CancellationToken cancellationToken)
    {
        try
        {
            await client.StartAsync(
                new CodexAppServerSettings(),
                "quicker-tests",
                "QuickER Tests",
                "1.0.0",
                cancellationToken
            );
            TrackServerOf(client);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new Xunit.Sdk.XunitException(
                $"偽の app-server（powershell.exe）を起動できませんでした: {ex.GetType().Name}: {ex.Message}"
                    + $" / {client.BuildRecentStandardErrorSuffix()}"
            );
        }
    }

    /// <summary>起動した偽サーバーのプロセス（クライアントが起動した直後に控える。PID の再利用に備えて開始時刻も持つ）</summary>
    private readonly List<(int Id, DateTime StartTime)> _servers = [];

    /// <summary>クライアントがいま掴んでいる偽サーバーのプロセスを控える（<see cref="StartAsync"/> が起動の直後に呼ぶ）</summary>
    private void TrackServerOf(CodexAppServerClient client)
    {
        var process = (System.Diagnostics.Process)
            typeof(CodexAppServerClient)
                .GetField("_process", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(client)!;
        _servers.Add((process.Id, process.StartTime));
    }

    /// <summary>控えた偽サーバーがすべて止まるまで待ち、上限を過ぎても動いているものの数を返す（後始末の確認用）</summary>
    /// <remarks>プロセスツリーの停止は非同期に進むので、破棄の直後に数えると止まりかけのものを拾うことがある</remarks>
    public async Task<int> WaitForServersToExitAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            var running = _servers.Count(server =>
            {
                using var process = FindRunning(server);
                return process is not null;
            });

            if (running == 0 || DateTime.UtcNow >= deadline)
            {
                return running;
            }

            await Task.Delay(100);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // 念のための後始末（クライアントの破棄が止めているはずのものを、残っていれば止める）
        foreach (var server in _servers)
        {
            using var process = FindRunning(server);

            if (process is null)
            {
                continue;
            }

            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch (Exception)
            {
                // 既に終わっていれば何もしない
            }
        }

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 消せなくてもテストの結果には関係しない（一時フォルダ）
        }
    }

    /// <summary>控えたプロセスがまだ動いていれば返す（同じ PID でも開始時刻が違えば別のプロセス）</summary>
    private static System.Diagnostics.Process? FindRunning((int Id, DateTime StartTime) server)
    {
        try
        {
            var process = System.Diagnostics.Process.GetProcessById(server.Id);

            if (!process.HasExited && process.StartTime == server.StartTime)
            {
                return process;
            }

            process.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // 既に終わっている
            return null;
        }
    }
}
