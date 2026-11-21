using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

// AI の CLI バックエンドのテストが実プロセスとして起動する偽物。Codex の app-server と Copilot のランタイムの 2 つの型を持ち、
// 起動の引数で分ける（Copilot の SDK は第 1 引数に --headless を付けて起動し、Codex のテストは第 1 引数に emit ファイルのパスを渡す）。
//
// Copilot のランタイムの型（CopilotRuntimeFake）：
// LSP の枠（Content-Length のヘッダー＋JSON）で話し、connect にプロトコル 3（SDK 1.x の版）を名乗って応える。
// SDK 2.x はプロトコル 4 だけを受けるので、起動がプロトコルの不一致で断られる＝古い CLI の代役。
// ほかの要求には Method not found のエラーを返す。標準入力の EOF で終わる。
//
// Codex の app-server の型（1 行 1 JSON）。規則は 3 つだけ：
// (1) クライアントのリクエスト（method と id を持つ行）には空の result を返す
// (2) initialized 通知を受けたら、引数のファイルに書かれた行をそのまま送る（サーバー発のリクエスト・通知）
// (3) クライアントの応答（id を持ち method を持たない行）は test/echo 通知の params に包んで送り返す
// (2) で送る行のうち、次の 4 つだけは送らずに動作の指示として扱う：
// stderr:文字列（標準エラーへ書く）・stdin-handle（自分の標準入力のハンドルの値を stdin-handle:値 として標準エラーへ書く）・
// exit（終了する）・block（以後は標準入力を読まずに眠り続ける＝書き込みが詰まる相手の代役）。
// 標準入力の EOF で終わる（テストのプロセスが落ちても孤児にならない）。

if (args.Length > 0 && args[0] == CopilotRuntimeFake.HeadlessArgument)
{
    return CopilotRuntimeFake.Run();
}

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

/// <summary>
/// Copilot のランタイムの偽物（LSP の枠の JSON-RPC）。connect にプロトコル 3 を名乗り、SDK 2.x の起動をプロトコルの不一致で断らせる
/// </summary>
internal static class CopilotRuntimeFake
{
    /// <summary>Copilot の SDK が copilot を起動するときに第 1 引数へ付ける引数</summary>
    internal const string HeadlessArgument = "--headless";

    /// <summary>名乗るプロトコルの版（SDK 1.x が話す版）</summary>
    private const string ProtocolVersion = "3";

    private static readonly Regex IdPattern = new(
        "\"id\":(\"[^\"]*\"|[0-9]+)",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex MethodPattern = new(
        "\"method\":\"([^\"]+)\"",
        RegexOptions.CultureInvariant
    );

    /// <summary>標準入力の EOF まで要求を読み、応答を返す</summary>
    internal static int Run()
    {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();

        while (ReadFrame(input) is { } body)
        {
            var id = IdPattern.Match(body);

            // id を持たない通知には応えない
            if (!id.Success)
            {
                continue;
            }

            var method = MethodPattern.Match(body);
            var response =
                method.Success && method.Groups[1].Value == "connect"
                    ? "{\"jsonrpc\":\"2.0\",\"id\":"
                        + id.Groups[1].Value
                        + ",\"result\":{\"ok\":true,\"protocolVersion\":"
                        + ProtocolVersion
                        + ",\"version\":\"0.0.0-fake\"}}"
                    : "{\"jsonrpc\":\"2.0\",\"id\":"
                        + id.Groups[1].Value
                        + ",\"error\":{\"code\":-32601,\"message\":\"Method not found\"}}";
            WriteFrame(output, response);
        }

        return 0;
    }

    /// <summary>LSP の枠を 1 つ読む（EOF なら null）</summary>
    private static string? ReadFrame(Stream input)
    {
        var header = new StringBuilder();

        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            var b = input.ReadByte();

            if (b < 0)
            {
                return null;
            }

            header.Append((char)b);
        }

        var length = 0;

        foreach (var line in header.ToString().Split("\r\n"))
        {
            if (line.StartsWith("Content-Length: ", StringComparison.Ordinal))
            {
                length = int.Parse(
                    line["Content-Length: ".Length..],
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture
                );
            }
        }

        var body = new byte[length];
        var read = 0;

        while (read < length)
        {
            var n = input.Read(body, read, length - read);

            if (n <= 0)
            {
                return null;
            }

            read += n;
        }

        return Encoding.UTF8.GetString(body);
    }

    /// <summary>LSP の枠で 1 つ書く</summary>
    private static void WriteFrame(Stream output, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes(
            "Content-Length: "
                + body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\r\n\r\n"
        );
        output.Write(header);
        output.Write(body);
        output.Flush();
    }
}

/// <summary>標準ハンドルの取得（stdin-handle の指示で使う）</summary>
internal static class Native
{
    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetStdHandle(int handle);
}
