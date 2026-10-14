using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

// CodexAppServerClient のテストが実プロセスとして起動する偽の app-server（1 行 1 JSON）。
// 規則は 3 つだけ：
// (1) クライアントのリクエスト（method と id を持つ行）には空の result を返す
// (2) initialized 通知を受けたら、引数のファイルに書かれた行をそのまま送る（サーバー発のリクエスト・通知）
// (3) クライアントの応答（id を持ち method を持たない行）は test/echo 通知の params に包んで送り返す
// (2) で送る行のうち、次の 4 つだけは送らずに動作の指示として扱う：
// stderr:文字列（標準エラーへ書く）・stdin-handle（自分の標準入力のハンドルの値を stdin-handle:値 として標準エラーへ書く）・
// exit（終了する）・block（以後は標準入力を読まずに眠り続ける＝書き込みが詰まる相手の代役）。
// 標準入力の EOF で終わる（テストのプロセスが落ちても孤児にならない）。

var emitFile = args.Length > 0 ? args[0] : throw new ArgumentException("emit file is required");
var utf8 = new UTF8Encoding(false);
using var input = new StreamReader(Console.OpenStandardInput(), utf8);
using var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
using var error = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true };

var methodPattern = new Regex("\"method\":\"([^\"]+)\"", RegexOptions.CultureInvariant);
var idPattern = new Regex("\"id\":(\\d+)", RegexOptions.CultureInvariant);

while (input.ReadLine() is { } line)
{
    var method = methodPattern.Match(line);

    if (method.Success)
    {
        var id = idPattern.Match(line);

        if (id.Success)
        {
            output.WriteLine("{\"id\":" + id.Groups[1].Value + ",\"result\":{}}");
        }
        else if (method.Groups[1].Value == "initialized")
        {
            foreach (var emit in File.ReadAllLines(emitFile, utf8))
            {
                if (emit.StartsWith("stderr:", StringComparison.Ordinal))
                {
                    error.WriteLine(emit["stderr:".Length..]);
                }
                else if (emit == "stdin-handle")
                {
                    error.WriteLine("stdin-handle:" + Native.GetStdHandle(-10).ToInt64());
                }
                else if (emit == "exit")
                {
                    return 0;
                }
                else if (emit == "block")
                {
                    Thread.Sleep(TimeSpan.FromSeconds(120));
                    return 0;
                }
                else
                {
                    output.WriteLine(emit);
                }
            }
        }
    }
    else if (line.Contains("\"id\":", StringComparison.Ordinal))
    {
        output.WriteLine("{\"method\":\"test/echo\",\"params\":" + line + "}");
    }
}

return 0;

/// <summary>標準ハンドルの取得（stdin-handle の指示で使う）</summary>
internal static class Native
{
    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetStdHandle(int handle);
}
