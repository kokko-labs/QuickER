using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;
using Bin = QuickER.Tests.GeneratedBinaryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>シークできず、決めたバイト数を返した後に終わるか、例外を投げる Stream</summary>
internal sealed class FailingBlobStream(byte[] data, bool throwAtEnd) : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position == data.Length && throwAtEnd)
        {
            throw new IOException("The source failed in the middle of the copy.");
        }

        var read = Math.Min(count, data.Length - _position);
        Array.Copy(data, _position, buffer, offset, read);
        _position += read;
        return read;
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}

/// <summary>
/// グラフ保存の After フックが context で書く無制限バイナリ列の書き込みが失敗したとき、フックが例外を握りつぶして保存が
/// コミットしても、列が書きかけで残らないことを、SQLite の QuickER 版 Repository（BinaryFixture・実ファイル DB）で検証する。
/// </summary>
/// <remarks>
/// SQLite の書き込みは、宣言の長さの zeroblob を確保してから写すので、書き込みが失敗すると、確保した 0 と写した途中の
/// バイトが残る。参加モードの書き込みはセーブポイントの中で行い、失敗したらそこまで戻す。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SaveHookBinaryWriteSqliteRuntimeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Theory(
        DisplayName = "[SaveHook/blob] After フックの書き込みが失敗して握りつぶされても、列は元のまま・保存の残りはコミットされる"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedWriteInHook_LeavesColumnAsItWas(bool throwInTheMiddle)
    {
        using var db = SqliteTempDatabase.Create();
        await db.ApplyDdlAsync(Bin.BinaryFixtureDefinition.Build(), Ct);
        var hook = new SwallowingWriteHook(throwInTheMiddle);
        using var provider = new ServiceCollection()
            .AddGeneratedSqliteRepositories(db.ReadWriteCreateConnectionString)
            .AddSaveHook(hook)
            .BuildServiceProvider();
        var documents = provider.GetRequiredService<Bin.IDocumentRepository>();
        await documents.InsertAsync(
            new Bin.DocumentEntity
            {
                DocumentId = 1,
                Title = "a",
                Thumb = [1],
                Payload = [1, 2],
            },
            Ct
        );

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

    [Fact(
        DisplayName = "[SaveHook/blob] After フックの書き込みが成功すれば、保存と一緒にコミットされる"
    )]
    public async Task SuccessfulWriteInHook_IsCommitted()
    {
        using var db = SqliteTempDatabase.Create();
        await db.ApplyDdlAsync(Bin.BinaryFixtureDefinition.Build(), Ct);
        using var provider = new ServiceCollection()
            .AddGeneratedSqliteRepositories(db.ReadWriteCreateConnectionString)
            .AddSaveHook(new WritingHook())
            .BuildServiceProvider();
        var documents = provider.GetRequiredService<Bin.IDocumentRepository>();
        await documents.InsertAsync(
            new Bin.DocumentEntity
            {
                DocumentId = 1,
                Title = "a",
                Thumb = [1],
                Payload = [1, 2],
            },
            Ct
        );

        var document = (await documents.GetByIdAsync(1, Ct))!;
        document.Title = "b";
        document.MarkUpdated();
        await documents.SaveAsync(document, cancellationToken: Ct);

        (
            await documents.ExecuteScalarSqlAsync<byte[]>(
                "SELECT payload FROM documents WHERE document_id = 1",
                cancellationToken: Ct
            )
        )
            .Should()
            .Equal([4, 4, 4]);
    }

    /// <summary>After で、宣言より短い Stream（または途中で失敗する Stream）を書こうとし、例外を握りつぶす</summary>
    private sealed class SwallowingWriteHook(bool throwInTheMiddle)
        : Bin.ISaveHook<Bin.DocumentEntity>
    {
        public Exception? Caught { get; private set; }

        public async Task AfterSaveAsync(
            Bin.DocumentEntity entity,
            Bin.SaveOperation operation,
            Bin.ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                await context.WriteBinaryColumnAsync(
                    nameof(Bin.DocumentEntity.Payload),
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

    /// <summary>After で payload を書く</summary>
    private sealed class WritingHook : Bin.ISaveHook<Bin.DocumentEntity>
    {
        public Task AfterSaveAsync(
            Bin.DocumentEntity entity,
            Bin.SaveOperation operation,
            Bin.ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) =>
            context.WriteBinaryColumnAsync(
                nameof(Bin.DocumentEntity.Payload),
                entity.DocumentId,
                new MemoryStream([4, 4, 4]),
                cancellationToken: cancellationToken
            );
    }
}
