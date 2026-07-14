using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedSqlServerBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration;

/// <summary>
/// 無制限バイナリ列の除外（<c>ExcludeUnboundedBinaryColumns</c>）と Stream アクセサ（<c>Read/Write{Column}Async</c>）を、
/// <b>SQL Server 方言</b>のRepository (QuickER) で実 SQL Server（Testcontainers・Docker 依存）に流して検証する。
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
/// シードは <b>生 SQL のパラメータ付き INSERT</b> で行う（<c>row_ver</c>＝<c>rowversion</c> 列を列指定から外し、
/// SQL Server に自動採番させる）。これは store-generated な rowversion 列を持つテーブルへの正しい投入方法である。
/// なお、生成された Repository の <c>InsertAsync</c>（全列 INSERT）は rowversion 列へ明示値を書こうとして
/// SQL Server が拒否するため、本テストの投入には用いない（この制約は本タスクの検証対象＝Stream/除外 SELECT とは
/// 独立した既存の課題。詳細は報告参照）。
/// </para>
/// <para>
/// SQL Server 側は Docker（Testcontainers）依存のため、Docker 不在時は <see cref="SqlServerContainerFixture"/> の
/// 検出でスキップされる（CI では常にスキップ）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
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

    /// <summary>スキーマを作成し、生 SQL でシードデータを投入する（rowversion 列は SQL Server が自動採番）。</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ExecuteAsync(
            new SqlServerDdlGenerator().Build(SqlServerBinaryFixtureDefinition.Build()),
            Ct
        );

        _provider = new ServiceCollection()
            .AddGeneratedRepositories(_fixture.ConnectionString)
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
    /// 生 SQL のパラメータ付き INSERT で 1 行投入する。rowversion（store-generated）の <c>row_ver</c> は
    /// 列指定から外し、SQL Server に自動採番させる（rowversion 列を持つテーブルへの正しい投入方法）。
    /// </summary>
    private async Task SeedDocumentAsync(
        int id,
        string title,
        byte[]? payload,
        byte[] thumb,
        byte[]? checksum
    )
    {
        await using var connection = await _fixture.OpenConnectionAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO [documents] ([document_id], [title], [payload], [thumb], [checksum]) "
            + "VALUES (@id, @title, @payload, @thumb, @checksum);";
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
        command.Parameters.Add(
            new SqlParameter("@title", SqlDbType.NVarChar, 50) { Value = title }
        );
        command.Parameters.Add(
            new SqlParameter("@payload", SqlDbType.VarBinary, -1)
            {
                Value = (object?)payload ?? DBNull.Value,
            }
        );
        command.Parameters.Add(
            new SqlParameter("@thumb", SqlDbType.VarBinary, -1) { Value = thumb }
        );
        command.Parameters.Add(
            new SqlParameter("@checksum", SqlDbType.VarBinary, 16)
            {
                Value = (object?)checksum ?? DBNull.Value,
            }
        );
        await command.ExecuteNonQueryAsync(Ct);
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
            .Contain("CanSeek");

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
