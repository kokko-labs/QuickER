using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期の HTTP の差分ソースが blob をコピーする経路も、生成エンドポイントの応答でない 2xx を成功と読まないことを固定する。
/// </summary>
/// <remarks>
/// 差分ソース（<c>HttpSyncServerSource</c>）の blob の読み書きは、リモート Repository と同じバイナリ転送の
/// 補助を通る。ログインページへ転送する認証プロキシの後ろで成功と読むと、ダウンロードは HTML をローカルの blob
/// として書き、アップロードは何も保存されていないのにコピー済みとして記録を片づけてしまう。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SyncBinaryTransferResponseRuntimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(
        DisplayName = "[Sync/HTTP] ログインページへ転送されたら、blob のダウンロードもアップロードも RemoteRepositoryException になる"
    )]
    public async Task BlobCopy_BehindRedirect_Throws()
    {
        await using var server = await InProcessRemoteServer.StartAsync(
            _ => { },
            app =>
                app.Use(
                    async (context, next) =>
                    {
                        if (context.Request.Path.StartsWithSegments(RemotePaths.DefaultPrefix))
                        {
                            context.Response.Redirect("/login");
                            return;
                        }

                        if (context.Request.Path.StartsWithSegments("/login"))
                        {
                            context.Response.ContentType = "text/html";
                            await context.Response.WriteAsync("<html>Sign in</html>");
                            return;
                        }

                        await next();
                    }
                ),
            Ct
        );
        await using var client = new ServiceCollection()
            .AddGeneratedHttpSyncSources(server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();
        var blobs = client
            .GetRequiredService<ISyncServerSource<SyncOrderEntity, int>>()
            .BinaryColumns!;
        var column = blobs.UnboundedBinaryColumnNames[0];
        var destination = new MemoryStream();

        var download = () => blobs.ReadUnboundedBinaryAsync(column, 1, destination, Ct);
        var upload = () =>
            blobs.WriteUnboundedBinaryAsync(column, 1, new MemoryStream([1, 2, 3]), null, Ct);

        await download.Should().ThrowAsync<RemoteRepositoryException>();
        destination.Length.Should().Be(0);
        await upload.Should().ThrowAsync<RemoteRepositoryException>();
    }
}
