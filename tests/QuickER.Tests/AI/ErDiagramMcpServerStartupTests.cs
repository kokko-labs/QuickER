using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using QuickER.AI;
using QuickER.AI.Chat;

namespace QuickER.Tests.AI;

/// <summary><see cref="ErDiagramMcpServer"/> の起動失敗時の後始末を検証するテストクラス</summary>
public class ErDiagramMcpServerStartupTests
{
    /// <summary>
    /// 起動に失敗したときは、組み立て済みのアプリを破棄してから例外を返すことを検証する
    /// （サーバーがアプリを保持する前に失敗するため、サーバーの DisposeAsync からは届かない）。
    /// 破棄の観測は、起動直前のフックで受け取ったアプリのサービスプロバイダーが使えなくなったことで行う。
    /// </summary>
    [Fact(DisplayName = "MCP サーバーの起動に失敗したら組み立てたアプリを破棄する")]
    public async Task StartAsync_WhenStartFails_DisposesBuiltApp()
    {
        WebApplication? builtApp = null;
        var server = new ErDiagramMcpServer(
            (_, _) => (string.Empty, true),
            ErDesignProfile.ErDesign.Tools
        )
        {
            AppBuilt = app => builtApp = app,
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var start = () => server.StartAsync(cts.Token);
        await start.Should().ThrowAsync<OperationCanceledException>();

        builtApp.Should().NotBeNull();
        var resolve = () => builtApp!.Services.GetService<IServiceProvider>();
        resolve.Should().Throw<ObjectDisposedException>();
        server.Url.Should().BeNull();

        await server.DisposeAsync();
    }
}
