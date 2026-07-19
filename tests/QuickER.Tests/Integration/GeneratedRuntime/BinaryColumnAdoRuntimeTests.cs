using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 無制限バイナリ列の除外を<b>QuickER の <c>SqliteRepository</c> 版</b>（<c>AddGeneratedSqliteRepositories</c>）で検証する派生。
/// 除外が効く側＝SELECT / UPDATE から payload / thumb が外れ、INSERT / BulkInsert では DB に書かれることを確認する。
/// </summary>
public sealed class BinaryColumnAdoRuntimeTests : BinaryColumnRuntimeTestsBase
{
    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ（接続文字列は基底の一時 DB）</summary>
    private ServiceProvider? _provider;

    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedSqliteRepositories(ConnectionString)
            .BuildServiceProvider();

    protected override IDocumentRepository CreateDocumentRepository() =>
        Provider().GetRequiredService<IDocumentRepository>();

    protected override IDocumentNoteRepository CreateDocumentNoteRepository() =>
        Provider().GetRequiredService<IDocumentNoteRepository>();

    /// <summary>1. GetById / GetAll で除外列は未取得状態（nullable=null・非 nullable=空配列）・除外対象外は値が取れる</summary>
    [Fact(
        DisplayName = "[Binary/Ado] 1: GetById/GetAll で除外列は未取得状態・有界バイナリは取得される"
    )]
    public async Task GetById_ExcludesUnboundedBinary_KeepsBounded()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var doc = await documents.GetByIdAsync(1, Ct);
        doc.Should().NotBeNull();
        doc!.Title.Should().Be("alpha", "除外対象外の列は通常どおり取得される");
        doc.Payload.Should().BeNull("除外列 payload（nullable）は SELECT されず null のまま");
        doc.Thumb.Should().BeEmpty("除外列 thumb（非 nullable）は SELECT されず空配列のまま");
        doc.Checksum.Should().Equal(Doc1Checksum, "有界バイナリ checksum は除外対象外＝値が取れる");

        var all = await documents.GetAllAsync(Ct);
        all.Should().HaveCount(3);
        all.Should().OnlyContain(d => d.Payload == null);
        all.Should().OnlyContain(d => d.Thumb.Length == 0);
    }

    /// <summary>2. Query() 一覧・Include（子）でも除外列は未取得状態（子は通常どおり取得される）</summary>
    [Fact(DisplayName = "[Binary/Ado] 2: Query()/Include でも除外列は未取得状態（子は取得される）")]
    public async Task Query_AndInclude_ExcludeUnboundedBinary()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var doc = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .Include(d => d.DocumentNotes)
            .FirstOrDefaultAsync(Ct);

        doc.Should().NotBeNull();
        doc!.Payload.Should().BeNull();
        doc.Thumb.Should().BeEmpty();
        doc.DocumentNotes.Should().HaveCount(2, "Include した子は通常どおり取得される");
        doc.DocumentNotes.Select(n => n.Note).Should().BeEquivalentTo(["note-a", "note-b"]);
    }

    /// <summary>3. 取得後に除外列へ非空値を代入して UpdateAsync すると例外（列名・生 SQL への誘導を含む）</summary>
    [Fact(DisplayName = "[Binary/Ado] 3: 除外列へ非空値代入後の UpdateAsync が例外になる")]
    public async Task Update_WithAssignedExcludedColumn_Throws()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Payload = [1, 2, 3];

        var act = async () => await documents.UpdateAsync(doc, Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("Payload")
            .And.Contain("ExecuteSqlAsync");
    }

    /// <summary>4. SaveAsync のグラフ（親 Updated＋除外列非空）でも例外になる</summary>
    [Fact(DisplayName = "[Binary/Ado] 4: SaveAsync のグラフ更新でも除外列非空は例外になる")]
    public async Task Save_Graph_WithAssignedExcludedColumn_Throws()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var doc = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .Include(d => d.DocumentNotes)
            .FirstOrDefaultAsync(Ct);
        doc!.MarkUpdated();
        doc.Payload = [7, 7];

        var act = () => documents.SaveAsync(doc, cancellationToken: Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>5. 除外列が未取得状態のままなら UpdateAsync は成功し、DB の blob は温存される</summary>
    [Fact(DisplayName = "[Binary/Ado] 5: 除外列が未取得状態なら UpdateAsync 成功・blob は温存")]
    public async Task Update_WithUnsetExcludedColumn_Succeeds_AndKeepsBlob()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Title = "alpha2";

        (await documents.UpdateAsync(doc, Ct)).Should().BeTrue();
        (await documents.GetByIdAsync(1, Ct))!.Title.Should().Be("alpha2");

        // UPDATE は payload / thumb 列に触れないため DB の blob はそのまま残る
        var payloadLength = await documents.ExecuteScalarSqlAsync<long>(
            "SELECT length(payload) FROM documents WHERE document_id = @id",
            new { id = 1 },
            Ct
        );
        payloadLength.Should().Be(Doc1Payload.Length);
    }

    /// <summary>6. INSERT は全列書き込み＝生 SQL で DB に blob が実在することを確認する</summary>
    [Fact(DisplayName = "[Binary/Ado] 6: INSERT は全列書き込み（DB に blob が実在する）")]
    public async Task Insert_WritesAllColumns_BlobPersisted()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var payloadLength = await documents.ExecuteScalarSqlAsync<long>(
            "SELECT length(payload) FROM documents WHERE document_id = @id",
            new { id = 1 },
            Ct
        );
        payloadLength.Should().Be(Doc1Payload.Length);

        var thumbLength = await documents.ExecuteScalarSqlAsync<long>(
            "SELECT length(thumb) FROM documents WHERE document_id = @id",
            new { id = 1 },
            Ct
        );
        thumbLength.Should().Be(Doc1Thumb.Length);
    }

    /// <summary>7. BulkInsert も全列書き込み＝DB に blob が実在する</summary>
    [Fact(DisplayName = "[Binary/Ado] 7: BulkInsert も全列書き込み（DB に blob が実在する）")]
    public async Task BulkInsert_WritesAllColumns_BlobPersisted()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        await documents.BulkInsertAsync([NewDocument(20, "bulk", [7, 7, 7], [1], null)], Ct);

        var payloadLength = await documents.ExecuteScalarSqlAsync<long>(
            "SELECT length(payload) FROM documents WHERE document_id = @id",
            new { id = 20 },
            Ct
        );
        payloadLength.Should().Be(3);
    }

    /// <summary>8. 名前付きクエリ: 射影は除外列を取得・一覧は除外・件数は WHERE 参照が動く</summary>
    [Fact(DisplayName = "[Binary/Ado] 8: 名前付きクエリ（射影/一覧/件数）が除外の仕様どおり動く")]
    public async Task NamedQueries_HonorExclusion()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        // 射影（GetPayloads）: 除外列 payload を射影が参照＝サーバー側刈り込み SELECT に含まれ実データが取れる
        var rows = await documents.GetPayloadsAsync(Ct);
        rows.Should().HaveCount(3);
        rows.Single(r => r.DocumentId == 1).Payload.Should().Equal(Doc1Payload);
        rows.Single(r => r.DocumentId == 2).Payload.Should().BeNull();
        rows.Single(r => r.DocumentId == 3).Payload.Should().Equal(Doc3Payload);

        // 一覧（GetByTitle）: 全エンティティ取得のため除外が効き payload は null
        var alphas = await documents.GetByTitleAsync("alpha", Ct);
        alphas.Should().ContainSingle();
        alphas[0].Payload.Should().BeNull();
        alphas[0].Thumb.Should().BeEmpty();

        // 件数（CountWithPayload・payload IS NOT NULL）: 除外列への WHERE 参照が動く（文書 1・3）
        (await documents.CountWithPayloadAsync(Ct))
            .Should()
            .Be(2);
    }

    /// <summary>9. 生 SQL エンティティ取得: SELECT * は opportunistic に除外列も取り込む・縮小列は成功・必須列不足は例外</summary>
    [Fact(
        DisplayName = "[Binary/Ado] 9: 生 SQL は SELECT * で除外列も取り込み・縮小列は成功・不足は例外"
    )]
    public async Task RawSql_EntityRetrieval_OpportunisticAndShrunk()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        // SELECT *（全列）→ 除外列 payload / thumb も opportunistic にマップされる
        var full = await documents.QueryBySqlAsync(
            "SELECT * FROM documents WHERE document_id = @id",
            new { id = 1 },
            Ct
        );
        full.Should().ContainSingle();
        full[0].Payload.Should().Equal(Doc1Payload);
        full[0].Thumb.Should().Equal(Doc1Thumb);

        // 縮小列（除外列を含まない必須列のみ）→ 成功・payload は null
        var shrunk = await documents.QueryBySqlAsync(
            "SELECT document_id, title, checksum, row_ver FROM documents WHERE document_id = @id",
            new { id = 1 },
            Ct
        );
        shrunk.Should().ContainSingle();
        shrunk[0].Title.Should().Be("alpha");
        shrunk[0].Payload.Should().BeNull();

        // 必須列（縮小後の SELECT 列集合）に不足があれば分かる例外を投げる
        var act = async () =>
            await documents.QueryBySqlAsync("SELECT document_id FROM documents", null, Ct);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>10. WithUnboundedBinary で取得したエンティティをそのまま UpdateAsync すると既存の除外列ガードで例外になる</summary>
    [Fact(
        DisplayName = "[Binary/Ado] 10: WithUnboundedBinary 取得エンティティの UpdateAsync は既存ガードで例外"
    )]
    public async Task WithUnboundedBinary_ThenUpdate_Throws()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        // 除外列を含めて取得すると payload / thumb に実値が載る。そのまま UpdateAsync すると
        // 「除外列は UPDATE 対象外」の既存ガードが働く（更新は生 SQL へ誘導される）
        var doc = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);
        doc!.Title = "alpha-x";

        var act = async () => await documents.UpdateAsync(doc, Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("ExecuteSqlAsync");
    }

    /// <summary>11. 取得後に除外列を未取得状態（null / 空）へ戻して非除外列を変更すれば UpdateAsync は成功する（正当なエンティティである証明）</summary>
    [Fact(
        DisplayName = "[Binary/Ado] 11: 除外列を未取得状態へ戻せば WithUnboundedBinary 取得エンティティの Update は成功"
    )]
    public async Task WithUnboundedBinary_ResetExcluded_ThenUpdate_Succeeds()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var doc = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);

        // 除外列を「未取得状態」（nullable=null・非 nullable=空配列）へ戻し、非除外列だけを変更する
        doc!.Payload = null;
        doc.Thumb = [];
        doc.Title = "alpha-reset";

        (await documents.UpdateAsync(doc, Ct)).Should().BeTrue();
        (await documents.GetByIdAsync(1, Ct))!.Title.Should().Be("alpha-reset");

        // UPDATE は除外列に触れないため DB の blob は温存される（RowState=Unchanged の正当なエンティティだった証拠）
        var payloadLength = await documents.ExecuteScalarSqlAsync<long>(
            "SELECT length(payload) FROM documents WHERE document_id = @id",
            new { id = 1 },
            Ct
        );
        payloadLength.Should().Be(Doc1Payload.Length);
    }

    // ── Stream アクセサ（Read/Write{Column}Async）の実 DB 検証 ──

    /// <summary>数 MB の Write→Read 往復でデータが完全一致する（O(チャンク) のストリーミング）</summary>
    [Fact(DisplayName = "[Binary/Ado] Stream: 数 MB の Write→Read 往復が一致する")]
    public async Task Stream_WriteThenRead_RoundTripsLargeData()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var large = new byte[3 * 1024 * 1024];
        new Random(1234).NextBytes(large);

        (await documents.WritePayloadAsync(1, new MemoryStream(large), cancellationToken: Ct))
            .Should()
            .BeTrue("既存行への書き込みは true");

        using var destination = new MemoryStream();
        (await documents.ReadPayloadAsync(1, destination, Ct))
            .Should()
            .BeTrue("データを書いたので true");
        destination.ToArray().Should().Equal(large, "書いた blob がそのまま読める");

        // 非 nullable の除外列（thumb）も同様に往復できる
        var thumb = new byte[1024];
        new Random(55).NextBytes(thumb);
        (await documents.WriteThumbAsync(1, new MemoryStream(thumb), cancellationToken: Ct))
            .Should()
            .BeTrue();
        using var thumbDest = new MemoryStream();
        (await documents.ReadThumbAsync(1, thumbDest, Ct)).Should().BeTrue();
        thumbDest.ToArray().Should().Equal(thumb);
    }

    /// <summary>Read は行なし・列 NULL で false（宛先へ何も書かない）</summary>
    [Fact(DisplayName = "[Binary/Ado] Stream: Read は行なし・NULL で false（宛先は空）")]
    public async Task Stream_Read_ReturnsFalse_ForMissingRowOrNull()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        using var noRow = new MemoryStream();
        (await documents.ReadPayloadAsync(999, noRow, Ct)).Should().BeFalse("行なしは false");
        noRow.Length.Should().Be(0, "宛先へ何も書かない");

        using var nullColumn = new MemoryStream();
        // 文書 2 は payload=null（除外列だが INSERT では NULL が書かれている）
        (await documents.ReadPayloadAsync(2, nullColumn, Ct))
            .Should()
            .BeFalse("列 NULL は false");
        nullColumn.Length.Should().Be(0);
    }

    /// <summary>Write の source=null は列を NULL に設定する（行なしは false）</summary>
    [Fact(DisplayName = "[Binary/Ado] Stream: Write source=null で NULL 設定・行なしは false")]
    public async Task Stream_Write_NullSource_SetsNull()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        // 文書 1 は payload あり → null で SET NULL
        (await documents.WritePayloadAsync(1, null, cancellationToken: Ct))
            .Should()
            .BeTrue();

        var isNull = await documents.ExecuteScalarSqlAsync<long>(
            "SELECT CASE WHEN payload IS NULL THEN 1 ELSE 0 END FROM documents WHERE document_id = @id",
            new { id = 1 },
            Ct
        );
        isNull.Should().Be(1, "payload は NULL になった");

        // 読み直すと NULL なので false
        using var destination = new MemoryStream();
        (await documents.ReadPayloadAsync(1, destination, Ct)).Should().BeFalse();

        // 行なしへの書き込みは false
        (
            await documents.WritePayloadAsync(
                999,
                new MemoryStream([1, 2, 3]),
                cancellationToken: Ct
            )
        )
            .Should()
            .BeFalse();
    }

    /// <summary>CanSeek でない Stream は length 指定が必須（欠落は ArgumentException・指定すれば成功）</summary>
    [Fact(
        DisplayName = "[Binary/Ado] Stream: 非シーク Stream は length 必須（欠落で例外・指定で成功）"
    )]
    public async Task Stream_Write_NonSeekable_RequiresLength()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var payload = new byte[8192];
        new Random(7).NextBytes(payload);

        // length なし → ArgumentException
        var withoutLength = async () =>
            await documents.WritePayloadAsync(
                1,
                new NonSeekableStream(payload),
                cancellationToken: Ct
            );
        (await withoutLength.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should()
            .Contain("length");

        // length 指定 → 成功し、読み戻すと一致する
        (await documents.WritePayloadAsync(1, new NonSeekableStream(payload), payload.Length, Ct))
            .Should()
            .BeTrue();

        using var destination = new MemoryStream();
        (await documents.ReadPayloadAsync(1, destination, Ct)).Should().BeTrue();
        destination.ToArray().Should().Equal(payload);
    }

    /// <summary>ファイル糖衣（Write...FromFile / Read...ToFile）でファイル⇔DB を往復できる</summary>
    [Fact(DisplayName = "[Binary/Ado] Stream: ファイル糖衣でファイル⇔DB を往復する")]
    public async Task Stream_FileSugar_RoundTrips()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var directory = Directory.CreateTempSubdirectory("quicker-binary-stream");

        try
        {
            var payload = new byte[128 * 1024];
            new Random(99).NextBytes(payload);

            var sourcePath = Path.Combine(directory.FullName, "in.bin");
            var destinationPath = Path.Combine(directory.FullName, "out.bin");
            await File.WriteAllBytesAsync(sourcePath, payload, Ct);

            (await documents.WritePayloadFromFileAsync(1, sourcePath, Ct)).Should().BeTrue();
            (await documents.ReadPayloadToFileAsync(1, destinationPath, Ct)).Should().BeTrue();

            (await File.ReadAllBytesAsync(destinationPath, Ct)).Should().Equal(payload);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>Write 後は WithUnboundedBinary 取得で同じ blob が見える（既存機能との整合）</summary>
    [Fact(DisplayName = "[Binary/Ado] Stream: Write 後は WithUnboundedBinary で同データが見える")]
    public async Task Stream_Write_ThenWithUnboundedBinary_SeesSameData()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var payload = new byte[64 * 1024];
        new Random(3).NextBytes(payload);
        (await documents.WritePayloadAsync(1, new MemoryStream(payload), cancellationToken: Ct))
            .Should()
            .BeTrue();

        var doc = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);

        doc.Should().NotBeNull();
        doc!
            .Payload.Should()
            .Equal(payload, "Stream 版で書いた blob が WithUnboundedBinary でも取れる");
    }

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }

    /// <summary>CanSeek を持たない読み取り専用ストリーム（length 指定必須経路の検証用）</summary>
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
