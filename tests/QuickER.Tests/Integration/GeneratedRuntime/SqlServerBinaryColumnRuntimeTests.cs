using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedSqlServerBinaryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 無制限バイナリ列の除外（<c>ExcludeUnboundedBinaryColumns</c>）と Stream アクセサ（<c>Read/Write{Column}Async</c>）を、
/// <b>SQL Server 方言</b>のQuickER 版 Repository で実 SQL Server（Testcontainers・Docker 依存）に流して検証する。
/// </summary>
/// <remarks>
/// <para>
/// バイナリフィクスチャ（<see cref="Tests.GeneratedBinaryFixture.BinaryFixtureDefinition"/>）と同一の図を SQL Server
/// 方言で生成した固定フィクスチャ（<see cref="SqlServerBinaryFixtureDefinition"/>）を入力にする。SQLite 一時ファイル DB
/// では検証できない SQL Server 固有経路——(1) Stream アクセサのストリーミングエンジン
/// （読み=<c>CommandBehavior.SequentialAccess</c>＋<c>GetStream</c>・書き=Stream 値の <c>SqlParameter</c>）、
/// (2) 除外の FOR JSON 縮小 SELECT、(3) <c>WithUnboundedBinary()</c> のプレーン全列 SELECT——を実 DB で往復検証する。
/// </para>
/// <para>
/// シードは <b>生成された Repository の <c>InsertAsync</c></b> で行う。<c>row_ver</c>＝<c>rowversion</c> 列は
/// <c>[StoreGeneratedColumn]</c> により INSERT / BulkInsert / UPDATE の対象から自動的に外れ、SQL Server が採番する
/// （store-generated 列の除外＝本テストが検証する修正の 1 つ）。<c>InsertAsync</c> が rowversion 列入りテーブルで
/// クラッシュしないこと自体が <see cref="InitializeAsync"/> のシード成立で実証される。
/// </para>
/// <para>
/// SQL Server 側は Docker（Testcontainers）依存のため、Docker 不在時は <see cref="SqlServerContainerFixture"/> の
/// 検出でスキップされる（CI では常にスキップ）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerBinaryColumnRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>QuickER の SQL Server リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider _provider = null!;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>文書 1 の本体バイナリ（payload。除外列だが DB には実在する）</summary>
    private static readonly byte[] Doc1Payload = [1, 2, 3, 4];

    /// <summary>文書 1 のサムネイル（thumb。非 nullable の除外列）</summary>
    private static readonly byte[] Doc1Thumb = [9, 9];

    /// <summary>文書 1 のチェックサム（checksum。有界 varbinary(16)＝除外対象外）</summary>
    private static readonly byte[] Doc1Checksum = [10, 11, 12];

    /// <summary>文書 3 の本体バイナリ（payload。CountWithPayload の 2 件目）</summary>
    private static readonly byte[] Doc3Payload = [5, 6];

    /// <summary>スキーマを作成し、Repository の <c>InsertAsync</c> でシードデータを投入する（rowversion 列は SQL Server が自動採番）。</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ExecuteAsync(
            new SqlServerDdlGenerator().Build(SqlServerBinaryFixtureDefinition.Build()),
            Ct
        );

        _provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();

        // documents: 1="alpha"（payload あり・checksum あり）・2="beta"（payload なし）・3="gamma"（payload あり）
        await SeedDocumentAsync(1, "alpha", Doc1Payload, Doc1Thumb, Doc1Checksum);
        await SeedDocumentAsync(2, "beta", null, [8], null);
        await SeedDocumentAsync(3, "gamma", Doc3Payload, [6], null);
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>文書リポジトリを解決する</summary>
    private IDocumentRepository Documents() => _provider.GetRequiredService<IDocumentRepository>();

    /// <summary>
    /// 生成された Repository の <c>InsertAsync</c> で 1 行投入する。無制限バイナリ列（<c>payload</c> / <c>thumb</c>）は
    /// INSERT に含まれるため値を渡せる。rowversion（store-generated）の <c>row_ver</c> は <c>[StoreGeneratedColumn]</c> で
    /// INSERT から自動的に外れ、SQL Server が採番する（明示値を書かないためクラッシュしない）。
    /// </summary>
    private async Task SeedDocumentAsync(
        int id,
        string title,
        byte[]? payload,
        byte[] thumb,
        byte[]? checksum
    )
    {
        await Documents()
            .InsertAsync(
                new DocumentEntity
                {
                    DocumentId = id,
                    Title = title,
                    Payload = payload,
                    Thumb = thumb,
                    Checksum = checksum,
                },
                Ct
            );
    }

    // ── Stream アクセサ（Read/Write{Column}Async）の実 DB 検証 ──

    /// <summary>1. 数 MB の Write→Read 往復でデータが完全一致する（SequentialAccess+GetStream / Stream 値 SqlParameter）</summary>
    [Fact(DisplayName = "[Binary/SqlServer] Stream: 数 MB の Write→Read 往復が一致する")]
    public async Task Stream_WriteThenRead_RoundTripsLargeData()
    {
        var documents = Documents();

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

    /// <summary>2. Read は行なし・列 NULL で false（宛先へ何も書かない）</summary>
    [Fact(DisplayName = "[Binary/SqlServer] Stream: Read は行なし・NULL で false（宛先は空）")]
    public async Task Stream_Read_ReturnsFalse_ForMissingRowOrNull()
    {
        var documents = Documents();

        using var noRow = new MemoryStream();
        (await documents.ReadPayloadAsync(999, noRow, Ct)).Should().BeFalse("行なしは false");
        noRow.Length.Should().Be(0, "宛先へ何も書かない");

        using var nullColumn = new MemoryStream();
        // 文書 2 は payload=null
        (await documents.ReadPayloadAsync(2, nullColumn, Ct))
            .Should()
            .BeFalse("列 NULL は false");
        nullColumn.Length.Should().Be(0);
    }

    /// <summary>3. Write の source=null は列を NULL に設定する（行なしは false）</summary>
    [Fact(
        DisplayName = "[Binary/SqlServer] Stream: Write source=null で NULL 設定・行なしは false"
    )]
    public async Task Stream_Write_NullSource_SetsNull()
    {
        var documents = Documents();

        // 文書 1 は payload あり → null で SET NULL
        (await documents.WritePayloadAsync(1, null, cancellationToken: Ct))
            .Should()
            .BeTrue();

        var isNull = await documents.ExecuteScalarSqlAsync<int>(
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

    /// <summary>4. CanSeek でない Stream は length 指定が必須（欠落は ArgumentException・指定すれば成功）</summary>
    [Fact(
        DisplayName = "[Binary/SqlServer] Stream: 非シーク Stream は length 必須（欠落で例外・指定で成功）"
    )]
    public async Task Stream_Write_NonSeekable_RequiresLength()
    {
        var documents = Documents();

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

    /// <summary>5. 除外の FOR JSON 縮小 SELECT: GetById / GetAll で除外列は未取得状態・有界バイナリは取得される</summary>
    [Fact(
        DisplayName = "[Binary/SqlServer] 除外(FOR JSON): GetById/GetAll で除外列は未取得状態・有界は取得"
    )]
    public async Task Exclusion_ForJson_GetByIdAndGetAll()
    {
        var documents = Documents();

        var doc = await documents.GetByIdAsync(1, Ct);
        doc.Should().NotBeNull();
        doc!.Title.Should().Be("alpha", "除外対象外の列は通常どおり取得される");
        doc.Payload.Should()
            .BeNull("除外列 payload（nullable）は FOR JSON SELECT されず null のまま");
        doc.Thumb.Should().BeEmpty("除外列 thumb（非 nullable）は SELECT されず空配列のまま");
        doc.Checksum.Should().Equal(Doc1Checksum, "有界バイナリ checksum は除外対象外＝値が取れる");
        doc.RowVer.Should()
            .NotBeNull("rowversion は除外対象外＝FOR JSON SELECT に含まれ値が取れる");

        var all = await documents.GetAllAsync(Ct);
        all.Should().HaveCount(3);
        all.Should().OnlyContain(d => d.Payload == null);
        all.Should().OnlyContain(d => d.Thumb.Length == 0);
    }

    /// <summary>6. WithUnboundedBinary のプレーン全列 SELECT: 除外列の実データを返し RowState=Unchanged になる</summary>
    [Fact(
        DisplayName = "[Binary/SqlServer] WithUnboundedBinary(プレーン SELECT): 除外列の実データを返す(RowState=Unchanged)"
    )]
    public async Task WithUnboundedBinary_PlainSelect_ReturnsBinaryData()
    {
        var documents = Documents();

        var doc = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);

        doc.Should().NotBeNull();
        doc!
            .Payload.Should()
            .Equal(Doc1Payload, "WithUnboundedBinary で除外列 payload の実データが取れる");
        doc.Thumb.Should()
            .Equal(Doc1Thumb, "WithUnboundedBinary で除外列 thumb の実データが取れる");
        doc.Checksum.Should().Equal(Doc1Checksum, "除外対象外の列も通常どおり取れる");
        doc.RowState.Should()
            .Be(RowState.Unchanged, "通常取得と同等の正当なエンティティ（DB 読み込み行）である");
    }

    /// <summary>7. ファイル糖衣（Write...FromFile / Read...ToFile）でファイル⇔DB を往復できる</summary>
    [Fact(DisplayName = "[Binary/SqlServer] Stream: ファイル糖衣でファイル⇔DB を往復する")]
    public async Task Stream_FileSugar_RoundTrips()
    {
        var documents = Documents();

        var directory = Directory.CreateTempSubdirectory("quicker-sqlserver-binary-stream");

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

    /// <summary>8. 名前付きクエリ（Dsl 共有本体）が SQL Server 方言でも動く: 射影は取得・一覧は除外・件数は WHERE 参照</summary>
    [Fact(
        DisplayName = "[Binary/SqlServer] 名前付きクエリ（射影/一覧/件数）が除外の仕様どおり動く"
    )]
    public async Task NamedQueries_HonorExclusion()
    {
        var documents = Documents();

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

    // ── rowversion（store-generated 列）の書き込み経路の実 DB 検証 ──

    /// <summary>9. rowversion テーブルで InsertAsync が成功し、挿入後に rowversion を SELECT で読める</summary>
    [Fact(
        DisplayName = "[Binary/SqlServer] rowversion: InsertAsync 成功・rowversion が SELECT で読める"
    )]
    public async Task RowVersion_InsertAsync_Succeeds_AndRowVersionIsReadable()
    {
        var documents = Documents();

        // InitializeAsync のシードは InsertAsync で投入済み（rowversion 列でクラッシュしない＝修正の実証）。
        // 追加でもう 1 行を InsertAsync し、rowversion が DB 採番されて SELECT で取れることを確認する
        await documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 20,
                Title = "insert-check",
                Thumb = [7],
            },
            Ct
        );

        var doc = await documents.GetByIdAsync(20, Ct);
        doc.Should().NotBeNull();
        doc!
            .RowVer.Should()
            .NotBeNull("rowversion は DB（SQL Server）が採番し SELECT で取得できる（除外対象外）");
        doc.RowVer!.Length.Should().Be(8, "SQL Server の rowversion は 8 バイト");
    }

    /// <summary>10. UpdateAsync が成功し、UPDATE 後に rowversion の値が DB により変わっている</summary>
    [Fact(
        DisplayName = "[Binary/SqlServer] rowversion: UpdateAsync 成功・UPDATE 後に rowversion が変わる"
    )]
    public async Task RowVersion_UpdateAsync_Succeeds_AndDbBumpsRowVersion()
    {
        var documents = Documents();

        // 取得したエンティティの除外列（payload=null・thumb=空）は未取得状態のため UPDATE ガードに触れない
        var before = await documents.GetByIdAsync(1, Ct);
        before.Should().NotBeNull();
        var beforeRowVer = before!.RowVer;
        beforeRowVer.Should().NotBeNull("更新前から rowversion は読める");

        before.Title = "alpha-updated";
        (await documents.UpdateAsync(before, cancellationToken: Ct))
            .Should()
            .BeTrue(
                "rowversion 列入りテーブルでも UPDATE は成功する（rowversion は SET から除外）"
            );

        var after = await documents.GetByIdAsync(1, Ct);
        after.Should().NotBeNull();
        after!.Title.Should().Be("alpha-updated", "非 store-generated 列は更新される");
        after.RowVer.Should().NotBeNull();
        after
            .RowVer.Should()
            .NotEqual(beforeRowVer!, "UPDATE のたびに DB が rowversion を自動更新する");
    }

    /// <summary>11. rowversion テーブルで BulkInsertAsync（SqlBulkCopy）が成功する</summary>
    [Fact(DisplayName = "[Binary/SqlServer] rowversion: BulkInsertAsync（SqlBulkCopy）が成功する")]
    public async Task RowVersion_BulkInsertAsync_Succeeds()
    {
        var documents = Documents();

        var inserted = await documents.BulkInsertAsync(
            [
                new DocumentEntity
                {
                    DocumentId = 30,
                    Title = "bulk-30",
                    Thumb = [1],
                },
                new DocumentEntity
                {
                    DocumentId = 31,
                    Title = "bulk-31",
                    Thumb = [2],
                },
            ],
            Ct
        );
        inserted
            .Should()
            .Be(
                2,
                "rowversion 列入りテーブルでも BulkInsert は成功する（rowversion は列マッピング対象外）"
            );

        var doc = await documents.GetByIdAsync(30, Ct);
        doc.Should().NotBeNull();
        doc!.RowVer.Should().NotBeNull("BulkInsert 後も DB が rowversion を採番している");
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
