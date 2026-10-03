using System.IO;
using System.Reflection;
using AwesomeAssertions;
using GitHub.Copilot;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="CopilotRuntimeClient"/> の接続の後始末（起動に失敗したときの一時フォルダ・接続が切れたときの破棄）を
/// 検証するテストクラス。実際の copilot は起動しない。
/// </summary>
public class CopilotRuntimeClientConnectionTests
{
    /// <summary>接続断として扱うのは、実機で確かめた形（型がちょうど IOException）だけであることを検証する</summary>
    [Fact(DisplayName = "接続断として扱うのは型がちょうど IOException の失敗だけ")]
    public void IsConnectionLost_OnlyExactIOException()
    {
        CopilotRuntimeClient.IsConnectionLost(new IOException("pipe")).Should().BeTrue();
        CopilotRuntimeClient
            .IsConnectionLost(new IOException("wrap", new IOException("inner")))
            .Should()
            .BeTrue();
        CopilotRuntimeClient.IsConnectionLost(new FileNotFoundException()).Should().BeFalse();
        CopilotRuntimeClient
            .IsConnectionLost(new InvalidOperationException("not connected"))
            .Should()
            .BeFalse();
        CopilotRuntimeClient.IsConnectionLost(new Exception("rpc error")).Should().BeFalse();
    }

    /// <summary>
    /// 呼び出しが接続断で失敗したら、例外を投げ直したうえで接続を捨てる（未接続へ戻り、一時作業フォルダも消える）ことを
    /// 検証する。捨てないと IsStarted が true のまま残り、以後の呼び出しがすべて失敗し続ける。
    /// </summary>
    [Fact(DisplayName = "接続断で失敗した呼び出しは接続を捨てて未接続へ戻す")]
    public async Task Invoke_WhenConnectionLost_ResetsConnection()
    {
        var (client, workingDirectory) = CreateStartedClient();

        var call = async () =>
            await client.InvokeAsync(() => Task.FromException(new IOException("pipe closed")));

        await call.Should().ThrowAsync<IOException>();
        client.IsStarted.Should().BeFalse();
        Directory.Exists(workingDirectory).Should().BeFalse("捨てた接続の一時作業フォルダは消す");
    }

    /// <summary>接続断でない失敗では接続を捨てない（会話の文脈を失わない）ことを検証する</summary>
    [Fact(DisplayName = "接続断でない失敗では接続を捨てない")]
    public async Task Invoke_WhenOtherFailure_KeepsConnection()
    {
        var (client, workingDirectory) = CreateStartedClient();

        try
        {
            var call = async () =>
                await client.InvokeAsync<int>(() =>
                    Task.FromException<int>(new InvalidOperationException("rpc error"))
                );

            await call.Should().ThrowAsync<InvalidOperationException>();
            client.IsStarted.Should().BeTrue();
        }
        finally
        {
            await client.DisposeAsync();
            Directory.Exists(workingDirectory).Should().BeFalse();
        }
    }

    /// <summary>
    /// 起動に失敗したら、その接続のために作った一時作業フォルダを消すことを検証する
    /// （再試行のたびに新しいフォルダを作るため、消さないと %TEMP% に溜まる）。
    /// </summary>
    [Fact(DisplayName = "起動に失敗したら一時作業フォルダを残さない")]
    public async Task Start_WhenLaunchFails_DeletesWorkingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuickER", "copilot");
        var missingExecutable = Path.Combine(
            Path.GetTempPath(),
            "QuickER.Tests",
            "missing-copilot",
            Guid.NewGuid().ToString("N"),
            "copilot.exe"
        );
        var before = ListDirectories(root);
        await using var client = new CopilotRuntimeClient(() => missingExecutable);

        var start = async () => await client.StartAsync(TestContext.Current.CancellationToken);

        await start.Should().ThrowAsync<Exception>();
        client.IsStarted.Should().BeFalse();
        ListDirectories(root).Should().BeSubsetOf(before, "起動に失敗した接続の一時フォルダは消す");
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
                Connection = RuntimeConnection.ForStdio(
                    Path.Combine(workingDirectory, "copilot.exe")
                ),
                WorkingDirectory = workingDirectory,
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
