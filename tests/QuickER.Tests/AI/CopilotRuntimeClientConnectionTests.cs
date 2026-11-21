using System.Globalization;
using System.IO;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.Copilot;
using QuickER.AI;
using AiStrings = QuickER.AI.Resources.Strings;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="CopilotRuntimeClient"/> の接続（SDK の失敗の形の分類・起動に失敗したときの一時フォルダと案内・
/// 接続が切れたときの破棄）を検証するテストクラス。実際の copilot は起動せず、起動の失敗は偽の実行ファイル
/// （<see cref="FakeCodexAppServerProcess.ExecutableFileName"/> の Copilot のランタイムの型）で起こす。
/// </summary>
[Collection(RealProcessTestCollection.Name)]
public class CopilotRuntimeClientConnectionTests
{
    /// <summary>
    /// SDK の失敗の形を分類することを検証する。
    /// <list type="bullet">
    /// <item>接続断として扱うのは、型がちょうど IOException で、内側が無いか SDK の接続断の例外のものだけ。
    /// ランタイムが返した通常のエラー応答（RemoteRpcException）や、別の例外を包んだ IOException は接続断ではない</item>
    /// <item>起動の失敗のうち案内へ訳すのは、SDK のプロトコルの不一致で、CLI の版が SDK の範囲より下（CLI の更新を案内）か
    /// 上（QuickER の更新を案内）のものだけ。版を報告しない形・範囲の中・ほかの例外は訳さない</item>
    /// </list>
    /// </summary>
    [Fact(
        DisplayName = "SDK の失敗の形の分類（接続断は内側が無いか SDK の接続断の例外を包んだ IOException だけ・案内へ訳すのは版が範囲の外のプロトコルの不一致だけ）"
    )]
    public void SdkFailureShapes_AreClassified()
    {
        CopilotRuntimeClient
            .ConnectionLostExceptionType.Should()
            .NotBeNull(
                "SDK の接続断の例外を名前で引けること（SDK を上げて名前が変わったら気づく）"
            );
        typeof(IOException)
            .IsAssignableFrom(CopilotRuntimeClient.ConnectionLostExceptionType)
            .Should()
            .BeTrue();

        CopilotRuntimeClient.IsConnectionLost(new IOException("pipe")).Should().BeTrue();
        CopilotRuntimeClient
            .IsConnectionLost(FakeCopilotRuntimeClient.ConnectionLostError())
            .Should()
            .BeTrue();
        CopilotRuntimeClient
            .IsConnectionLost(FakeCopilotRuntimeClient.ErrorResponse("model not available"))
            .Should()
            .BeFalse("ランタイムが返した通常のエラー応答では接続を捨てない");
        CopilotRuntimeClient
            .IsConnectionLost(new IOException("wrap", new IOException("inner")))
            .Should()
            .BeFalse();
        CopilotRuntimeClient.IsConnectionLost(new FileNotFoundException()).Should().BeFalse();
        CopilotRuntimeClient
            .IsConnectionLost(new InvalidOperationException("not connected"))
            .Should()
            .BeFalse();
        CopilotRuntimeClient.IsConnectionLost(new Exception("rpc error")).Should().BeFalse();

        var tooOld = ProtocolMismatch(4, 4, 3);
        CopilotRuntimeClient
            .TranslateStartFailure(tooOld)
            .Should()
            .BeEquivalentTo(
                new { Message = AiStrings.Copilot_CliProtocolTooOld, InnerException = tooOld },
                "CLI の版が SDK の範囲より下なら CLI の更新を案内し、元の例外を内側に残す"
            );
        var tooNew = ProtocolMismatch(4, 4, 5);
        CopilotRuntimeClient
            .TranslateStartFailure(tooNew)
            .Should()
            .BeEquivalentTo(
                new { Message = AiStrings.Copilot_CliProtocolTooNew, InnerException = tooNew },
                "CLI の版が SDK の範囲より上なら QuickER の更新を案内する"
            );
        CopilotRuntimeClient
            .TranslateStartFailure(ProtocolMismatch(3, 5, 4))
            .Should()
            .BeNull("範囲の中の版は訳さない");
        CopilotRuntimeClient
            .TranslateStartFailure(
                new InvalidOperationException(
                    "SDK protocol version mismatch: SDK supports versions 4-4, but server does not report a protocol version. Please update your server to ensure compatibility."
                )
            )
            .Should()
            .BeNull("版を報告しない形は、どちらを更新すべきか決められないので訳さない");
        CopilotRuntimeClient
            .TranslateStartFailure(new IOException(ProtocolMismatch(4, 4, 3).Message))
            .Should()
            .BeNull("SDK が不一致で投げるのは InvalidOperationException だけ");
        CopilotRuntimeClient
            .TranslateStartFailure(new InvalidOperationException("not connected"))
            .Should()
            .BeNull();
    }

    /// <summary>SDK がプロトコルの不一致で投げる形の例外を作る（<c>CopilotClient.VerifyProtocolVersionAsync</c> の文言）</summary>
    private static InvalidOperationException ProtocolMismatch(
        int minimum,
        int maximum,
        int server
    ) =>
        new(
            string.Create(
                CultureInfo.InvariantCulture,
                $"SDK protocol version mismatch: SDK supports versions {minimum}-{maximum}, but server reports version {server}. Please update your SDK or server to ensure compatibility."
            )
        );

    /// <summary>
    /// 呼び出しが接続断で失敗したら、例外を投げ直したうえで接続を捨てる（未接続へ戻り、一時作業フォルダも消える）ことを
    /// 検証する。捨てないと IsStarted が true のまま残り、以後の呼び出しがすべて失敗し続ける。
    /// </summary>
    /// <param name="isSdkShape">true なら SDK の接続断の例外を包んだ失敗（2.0.1 の形）、false なら素の IOException</param>
    [Theory(
        DisplayName = "接続断で失敗した呼び出しは接続を捨てて未接続へ戻す（素の IOException・SDK の接続断の例外を包んだ失敗）"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoke_WhenConnectionLost_ResetsConnection(bool isSdkShape)
    {
        var (client, workingDirectory) = CreateStartedClient();
        var failure = isSdkShape
            ? FakeCopilotRuntimeClient.ConnectionLostError()
            : new IOException("pipe closed");

        var call = async () => await client.InvokeAsync(() => Task.FromException(failure));

        await call.Should().ThrowAsync<IOException>();
        client.IsStarted.Should().BeFalse();
        Directory.Exists(workingDirectory).Should().BeFalse("捨てた接続の一時作業フォルダは消す");
    }

    /// <summary>
    /// 接続断でない失敗では、例外を投げ直すだけで接続を捨てない（会話の文脈を失わない）ことを検証する。
    /// ランタイムが返した通常のエラー応答（SDK が投げる RemoteRpcException）と、それ以外の失敗の両方を見る。
    /// </summary>
    /// <param name="isErrorResponse">true ならランタイムの通常のエラー応答（RemoteRpcException）、false ならそれ以外の失敗（InvalidOperationException）</param>
    [Theory(
        DisplayName = "接続断でない失敗では接続を捨てない（ランタイムの通常のエラー応答・それ以外の失敗）"
    )]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invoke_WhenNotConnectionLost_KeepsConnection(bool isErrorResponse)
    {
        var (client, workingDirectory) = CreateStartedClient();

        try
        {
            var call = isErrorResponse
                ? async () =>
                    await client.InvokeAsync(() =>
                        Task.FromException(
                            FakeCopilotRuntimeClient.ErrorResponse("model not available")
                        )
                    )
                : (Func<Task>)(
                    async () =>
                        await client.InvokeAsync<int>(() =>
                            Task.FromException<int>(new InvalidOperationException("rpc error"))
                        )
                );

            var thrown = await Record.ExceptionAsync(call);

            thrown
                .Should()
                .BeAssignableTo(
                    isErrorResponse ? typeof(RemoteRpcException) : typeof(InvalidOperationException)
                );
            client.IsStarted.Should().BeTrue();
        }
        finally
        {
            await client.DisposeAsync();
            Directory.Exists(workingDirectory).Should().BeFalse();
        }
    }

    /// <summary>
    /// 起動に失敗したら、その接続のために作った一時作業フォルダを消し（再試行のたびに新しいフォルダを作るため、
    /// 消さないと %TEMP% に溜まる）、プロトコルの不一致だけを案内へ訳すことを検証する。
    /// </summary>
    /// <remarks>
    /// プロトコル 3 のランタイムの行は、製品の経路（<see cref="CopilotRuntimeClient.StartAsync"/>）で本物の SDK に
    /// 偽の実行ファイルを起動させる。パスを渡すと SDK 同梱のランタイムでなくそのパスを起動すること・本物の SDK の
    /// 不一致の文言が分類の正規表現に当たること・StartAsync が訳を投げることを、まとめて見る
    /// （SDK を上げて文言や起動の契約が変わったら、この行が赤になる）
    /// </remarks>
    /// <param name="launch">起動の失敗の形（missing-executable＝実行ファイルが無い・protocol-3-runtime＝プロトコル 3 を名乗るランタイム）</param>
    [Theory(
        DisplayName = "起動に失敗したら一時作業フォルダを残さず、プロトコルの不一致だけを案内へ訳す（実行ファイルが無い・プロトコル 3 のランタイム）"
    )]
    [InlineData("missing-executable")]
    [InlineData("protocol-3-runtime")]
    public async Task Start_WhenLaunchFails_LeavesNoWorkingDirectory(string launch)
    {
        var isOldRuntime = launch == "protocol-3-runtime";
        var root = Path.Combine(Path.GetTempPath(), "QuickER", "copilot");
        var executable = isOldRuntime
            ? Path.Combine(AppContext.BaseDirectory, FakeCodexAppServerProcess.ExecutableFileName)
            : Path.Combine(
                Path.GetTempPath(),
                "QuickER.Tests",
                "missing-copilot",
                Guid.NewGuid().ToString("N"),
                "copilot.exe"
            );
        var before = ListDirectories(root);
        await using var client = new CopilotRuntimeClient(() => executable);

        var thrown = await Record.ExceptionAsync(async () =>
            await client.StartAsync(TestContext.Current.CancellationToken)
        );

        thrown.Should().NotBeNull();
        client.IsStarted.Should().BeFalse();
        ListDirectories(root).Should().BeSubsetOf(before, "起動に失敗した接続の一時フォルダは消す");

        if (isOldRuntime)
        {
            thrown
                .Should()
                .BeOfType<InvalidOperationException>()
                .Which.Message.Should()
                .Be(AiStrings.Copilot_CliProtocolTooOld, "古い CLI には CLI の更新を案内する");
            thrown!
                .InnerException.Should()
                .BeOfType<InvalidOperationException>(
                    "本物の SDK のプロトコルの不一致の例外を内側に残す"
                )
                .Which.Message.Should()
                .StartWith("SDK protocol version mismatch: SDK supports versions ")
                .And.Contain("but server reports version 3.");
        }
        else
        {
            thrown!
                .Message.Should()
                .NotBe(
                    AiStrings.Copilot_CliProtocolTooOld,
                    "プロトコルの不一致でない失敗は訳さない"
                );
        }
    }

    /// <summary>
    /// 接続済みの状態のクライアントを作る（実際の copilot を起動せず、起動前の SDK クライアントと一時作業フォルダを
    /// private フィールドへ置く）
    /// </summary>
    private static (CopilotRuntimeClient Client, string WorkingDirectory) CreateStartedClient()
    {
        var workingDirectory = BackendWorkingDirectory.Create("copilot");
        var client = new CopilotRuntimeClient(() => null);
        var sdkClient = new CopilotClient(
            new CopilotClientOptions
            {
                Mode = CopilotClientMode.CodingAgent,
                Runtime = RuntimeConnection.ForSpawnedStdio(
                    path: Path.Combine(workingDirectory, "copilot.exe"),
                    workingDirectory: workingDirectory
                ),
            }
        );
        SetField(client, "_client", sdkClient);
        SetField(client, "_workingDirectory", workingDirectory);
        client.IsStarted.Should().BeTrue();
        return (client, workingDirectory);
    }

    /// <summary>フォルダ直下のサブフォルダ名の一覧（無ければ空）</summary>
    private static List<string> ListDirectories(string root) =>
        Directory.Exists(root) ? Directory.GetDirectories(root).ToList() : [];

    private static void SetField(CopilotRuntimeClient client, string name, object value) =>
        typeof(CopilotRuntimeClient)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(client, value);
}
