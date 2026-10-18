using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSqlServerBinaryFixture;
using Xunit;
using SsBin = QuickER.Tests.GeneratedSqlServerBinaryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// After フックが context で書く無制限バイナリ列の、書いた後の版と失敗した書き込みを、実 SQL Server（SqlServerBinaryFixture＝rowversion 列を持つ）で
/// 検証する。
/// </summary>
/// <remarks>
/// SQL Server の書き込みは Stream を UPDATE のパラメータへ流す 1 文なので、失敗した書き込みは文ごと何も変えない。
/// フックが書いた行の版は、保存がコミットの前に読み直す。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SaveHookBinaryWriteSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly SqlServerContainerFixture _fixture = fixture;
    private ServiceProvider? _provider;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(SsBin.SqlServerBinaryFixtureDefinition.Build(), Ct);
    }

    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<SsBin.IDocumentRepository> SeededAsync(object hook)
    {
        _provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .AddSaveHook(hook)
            .BuildServiceProvider();
        var documents = _provider.GetRequiredService<SsBin.IDocumentRepository>();
        await documents.InsertAsync(
            new SsBin.DocumentEntity
            {
                DocumentId = 1,
                Title = "a",
                Thumb = [1],
                Payload = [1, 2],
            },
            Ct
        );
        return documents;
    }

    [Fact(
        DisplayName = "[SaveHook/版] After フックが同じ行の blob を書いても、保存の後の版は DB の版で、同じインスタンスを更新できる"
    )]
    public async Task BlobWrittenInHook_EntityCarriesStoredVersion()
    {
        var documents = await SeededAsync(new WritingHook());
        var document = (await documents.GetByIdAsync(1, Ct))!;

        document.Title = "b";
        document.MarkUpdated();
        await documents.SaveAsync(document, cancellationToken: Ct);

        document.RowVer.Should().Equal((await documents.GetByIdAsync(1, Ct))!.RowVer);

        document.Title = "c";
        (await documents.UpdateAsync(document, cancellationToken: Ct)).Should().BeTrue();
    }

    [Fact(
        DisplayName = "[SaveHook/版] blob を書いた保存の版を読み直した後でコミットが失敗しても、読み直した版はエンティティに残らない"
    )]
    public async Task CommitFailsAfterReread_EntityKeepsLoadedVersion()
    {
        var documents = await SeededAsync(new WritingHook());
        var document = (await documents.GetByIdAsync(1, Ct))!;
        var loaded = document.RowVer!.ToArray();

        // 版を読み直す SELECT（フックの blob の書き込みの後）が値を返した時点で取り消し、コミットを失敗させる
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var armed = false;
        var reread = false;
        using var capture = new SqlCommandObserver(
            "documents",
            afterCommand: text =>
            {
                if (
                    armed
                    && text.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                )
                {
                    reread = true;
                    cts.Cancel();
                }
            }
        );

        document.Title = "b";
        document.MarkUpdated();
        armed = true;
        var act = () => documents.SaveAsync(document, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        armed = false;
        reread.Should().BeTrue("取り消したのは版を読み直した後");
        document.RowVer.Should().Equal(loaded, "コミットされなかった保存の版を残さない");
        document.RowState.Should().Be(RowState.Updated);
        (await documents.GetByIdAsync(1, Ct))!
            .RowVer.Should()
            .Equal(loaded, "保存はロールバックされる");
    }

    [Theory(
        DisplayName = "[SaveHook/blob] After フックの書き込みが失敗して握りつぶされても、列は元のまま・保存の残りはコミットされる"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedWriteInHook_LeavesColumnAsItWas(bool throwInTheMiddle)
    {
        var hook = new SwallowingWriteHook(throwInTheMiddle);
        var documents = await SeededAsync(hook);
        var document = (await documents.GetByIdAsync(1, Ct))!;

        document.Title = "b";
        document.MarkUpdated();
        await documents.SaveAsync(document, cancellationToken: Ct);

        hook.Caught.Should().NotBeNull("書き込みは失敗する");
        (
            await documents.ExecuteScalarSqlAsync<byte[]>(
                "SELECT payload FROM documents WHERE document_id = 1",
                cancellationToken: Ct
            )
        )
            .Should()
            .Equal([1, 2], "失敗した書き込みは何も残さない");
        (await documents.GetByIdAsync(1, Ct))!.Title.Should().Be("b", "保存の残りはコミットされる");
    }

    private sealed class SwallowingWriteHook(bool throwInTheMiddle)
        : SsBin.ISaveHook<SsBin.DocumentEntity>
    {
        public Exception? Caught { get; private set; }

        public async Task AfterSaveAsync(
            SsBin.DocumentEntity entity,
            SsBin.SaveOperation operation,
            SsBin.ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                await context.WriteBinaryColumnAsync(
                    nameof(SsBin.DocumentEntity.Payload),
                    entity.DocumentId,
                    new FailingBlobStream([7, 7, 7], throwInTheMiddle),
                    throwInTheMiddle ? 3 + 1 : 10,
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                Caught = ex;
            }
        }
    }

    private sealed class WritingHook : SsBin.ISaveHook<SsBin.DocumentEntity>
    {
        public Task AfterSaveAsync(
            SsBin.DocumentEntity entity,
            SsBin.SaveOperation operation,
            SsBin.ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) =>
            context.WriteBinaryColumnAsync(
                nameof(SsBin.DocumentEntity.Payload),
                entity.DocumentId,
                new MemoryStream([4, 4, 4]),
                cancellationToken: cancellationToken
            );
    }
}
