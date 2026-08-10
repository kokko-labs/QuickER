using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 無制限バイナリ列の除外が<b>EF Core（<c>DbSet</c> 経由）には非適用</b>であることを検証する派生
/// （<c>AddGeneratedEfCoreRepositories</c>）。EF Core の列選択は EF Core の責務のため、GetById で除外列も普通に読め、
/// 除外列に値を入れたまま Update しても例外にならず DB へ書ける。共有 DSL 実装の名前付きクエリは EF Core でも動く。
/// </summary>
public sealed class BinaryColumnEfCoreRuntimeTests : BinaryColumnRuntimeTestsBase
{
    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ（UseSqlite・接続文字列は基底の一時 DB）</summary>
    private ServiceProvider? _provider;

    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options => options.UseSqlite(ConnectionString))
            .BuildServiceProvider();

    protected override IDocumentRepository CreateDocumentRepository() =>
        Provider().GetRequiredService<IDocumentRepository>();

    protected override IDocumentNoteRepository CreateDocumentNoteRepository() =>
        Provider().GetRequiredService<IDocumentNoteRepository>();

    /// <summary>1. EF Core は除外非適用＝GetById で除外列（payload / thumb）も全列読める</summary>
    [Fact(DisplayName = "[Binary/EF Core] 1: EF Core は除外非適用で除外列も全列読める")]
    public async Task GetById_ReadsAllColumns_IncludingExcluded()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var doc = await documents.GetByIdAsync(1, Ct);
        doc.Should().NotBeNull();
        doc!
            .Payload.Should()
            .Equal(Doc1Payload, "EF Core は列を全選択するため除外列 payload も読める");
        doc.Thumb.Should().Equal(Doc1Thumb, "EF Core は除外列 thumb も読める");
        doc.Checksum.Should().Equal(Doc1Checksum);
    }

    /// <summary>2. EF Core は除外非適用＝除外列に値を入れたまま Update しても例外にならず DB へ書ける</summary>
    [Fact(
        DisplayName = "[Binary/EF Core] 2: EF Core は除外列入り Update が例外にならず DB へ書ける"
    )]
    public async Task Update_WithAssignedExcludedColumn_Succeeds()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var doc = await documents.GetByIdAsync(2, Ct);
        doc!.Title = "beta-ef";
        doc.Payload = [42, 43];

        (await documents.UpdateAsync(doc, cancellationToken: Ct)).Should().BeTrue();

        var reread = await documents.GetByIdAsync(2, Ct);
        reread!.Title.Should().Be("beta-ef");
        reread.Payload.Should().Equal([42, 43], "EF Core は除外列 payload を通常どおり書き込む");
    }

    /// <summary>3. 共有 DSL 実装の名前付きクエリ（射影・件数）は EF Core でも同じ結果を返す</summary>
    [Fact(DisplayName = "[Binary/EF Core] 3: 名前付きクエリ（射影・件数）が EF Core でも動く")]
    public async Task NamedQueries_WorkOnEfCore()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        // 射影（GetPayloads）: EF Core も payload を実体化して DTO へ射影する
        var rows = await documents.GetPayloadsAsync(Ct);
        rows.Should().HaveCount(3);
        rows.Single(r => r.DocumentId == 1).Payload.Should().Equal(Doc1Payload);
        rows.Single(r => r.DocumentId == 2).Payload.Should().BeNull();

        // 件数（CountWithPayload）: payload IS NOT NULL（文書 1・3）
        (await documents.CountWithPayloadAsync(Ct))
            .Should()
            .Be(2);
    }

    /// <summary>EF Core モードでは Stream アクセサ（Read/Write）は NotSupportedException を投げる</summary>
    [Fact(DisplayName = "[Binary/EF Core] Stream アクセサは NotSupportedException")]
    public async Task StreamAccessors_ThrowNotSupported()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var read = async () => await documents.ReadPayloadAsync(1, new MemoryStream(), Ct);
        (await read.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should()
            .Contain("EF Core");

        var write = async () =>
            await documents.WritePayloadAsync(
                1,
                new MemoryStream([1, 2, 3]),
                cancellationToken: Ct
            );
        await write.Should().ThrowAsync<NotSupportedException>();
    }

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }
}
