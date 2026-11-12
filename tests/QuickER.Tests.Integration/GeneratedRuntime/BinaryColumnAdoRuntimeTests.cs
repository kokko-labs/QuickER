using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 無制限バイナリ列の除外を<b>QuickER の <c>SqliteRepository</c> 版</b>（<c>AddGeneratedSqliteRepositories</c>）で検証する派生。
/// 除外の読み書き・<c>WithUnboundedBinary()</c>・射影・生 SQL・Stream アクセサの契約は、実装先どうしの一致のシナリオ（<c>blob-*</c>）が
/// 見る。ここに残すのは、その図に無いもの（有界バイナリ <c>checksum</c>・EditModel・名前付きクエリ）と、SQLite の rowid を使う単独の Read。
/// </summary>
[RuntimeCell(RuntimeFeatures.BinaryColumn, RuntimeTarget.AdoSqlite)]
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

    /// <summary>
    /// 1. GetById で有界バイナリ（除外対象外）は値が取れる（除外列が未取得になることは実装先どうしの一致の
    /// <c>blob-excluded-columns-read-and-write-guard</c> が見る。その図は有界バイナリを持たない）
    /// </summary>
    [Fact(DisplayName = "[Binary/Ado] 1: GetById で有界バイナリは除外されずに取得される")]
    public async Task GetById_KeepsBoundedBinary()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var doc = await documents.GetByIdAsync(1, Ct);
        doc.Should().NotBeNull();
        doc!.Payload.Should().BeNull("除外列 payload（nullable）は SELECT されず null のまま");
        doc.Checksum.Should().Equal(Doc1Checksum, "有界バイナリ checksum は除外対象外＝値が取れる");
    }

    /// <summary>
    /// 5b. 新しい行の EditModel では、NOT NULL の除外列は空配列のまま残り（未入力に読まない）、Validate を通る。
    /// 空配列の INSERT の後に Stream で書き込む 2 段の書き込みが EditModel からも成り立つ。
    /// </summary>
    [Fact(
        DisplayName = "[Binary/Ado] 5b: 新しい行の EditModel は NOT NULL の除外列を空配列のまま渡し、INSERT の後に書き込める"
    )]
    public async Task NewEditModel_KeepsEmptyExcludedBlob_AndTwoStepWriteWorks()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();
        var mapper = new DocumentMapper();

        var model = mapper.CreateEditModel();
        model
            .Thumb.Should()
            .Equal([], "除外列の空配列は 2 段の書き込みのための値で、未入力ではない");

        model.BindingDocumentId = "9";
        model.BindingTitle = "new";
        model.BindingIsPublished = "false";
        model.Validate().Should().BeTrue();

        var entity = mapper.CreateEntity(model, includeRemoved: true);
        entity.Thumb.Should().Equal([]);
        await documents.SaveAsync(entity, cancellationToken: Ct);

        using var source = new MemoryStream([7, 7, 7]);
        (await documents.WriteThumbAsync(9, source, cancellationToken: Ct)).Should().BeTrue();

        using var destination = new MemoryStream();
        (await documents.ReadThumbAsync(9, destination, Ct)).Should().BeTrue();
        destination.ToArray().Should().Equal([7, 7, 7]);
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

    // ── Stream アクセサ（Read/Write{Column}Async）の実 DB 検証 ──

    /// <summary>
    /// 単独（トランザクションなし）の Read は、rowid の照会から blob の読み取りまでを 1 つの読み取りトランザクションで行う。
    /// 照会が終わった瞬間に別の接続が「最大 rowid の行を消して別の行を挿入する」（＝同じ rowid を再利用する）と、
    /// トランザクションの外では blob が別の行から読まれてしまう。
    /// WAL モードで確かめる：WAL では読み取り中でも別の接続の書き込みがコミットできるので、入れ替えは必ず成功し、
    /// 差は「照会と blob が同じスナップショットを読むか」だけに出る（rollback ジャーナルでは、フックが呼ばれる時点で
    /// 照会側がまだ共有ロックを持っており、トランザクションの有無に関わらず入れ替えが失敗して差が出ない）
    /// </summary>
    [Fact(
        DisplayName = "[Binary/Ado] Stream: 単独の Read は rowid の照会の直後に行が入れ替わっても別の行の blob を読まない"
    )]
    public async Task Stream_Read_Standalone_DoesNotReadBlobOfRowThatReusedRowid()
    {
        await ResetAndSeedAsync();

        await using (var connection = new SqliteConnection(ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var walCommand = connection.CreateCommand();
            walCommand.CommandText = "PRAGMA journal_mode = WAL;";
            (await walCommand.ExecuteScalarAsync(Ct)).Should().Be("wal");
        }

        // 文書 3 は最大の rowid を持つ。消してから挿入した行は同じ rowid を受け取る
        var intrusion = new RowidReuseIntrusion(ConnectionString);
        var documents = new DocumentRepository(
            new ProfilingConnectionFactory(ConnectionString, intrusion.OnStatementFinished)
        );

        using var destination = new MemoryStream();
        (await documents.ReadPayloadAsync(3, destination, Ct)).Should().BeTrue();

        intrusion.Succeeded.Should().BeTrue("rowid の照会の直後に行を入れ替えている");
        destination.ToArray().Should().Equal(Doc3Payload, "読むのは照会した行の blob だけ");
    }

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// 開いた接続に SQLite の profile フック（文の実行が終わるたびに呼ばれる）を付ける接続ファクトリ。
    /// フックが残った接続をプールへ返さないよう、プールは使わない
    /// </summary>
    private sealed class ProfilingConnectionFactory(
        string connectionString,
        Action<string> onStatementFinished
    ) : ISqlConnectionFactory
    {
        /// <summary>ネイティブ側が呼ぶデリゲート（GC に回収されないようフィールドで保持する）</summary>
        private readonly SQLitePCL.strdelegate_profile _profile = (_, sql, _) =>
            onStatementFinished(sql);

        public SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder(connectionString) { Pooling = false }.ToString()
            );
            connection.StateChange += (_, e) =>
            {
                if (e.CurrentState == System.Data.ConnectionState.Open)
                {
                    SQLitePCL.raw.sqlite3_profile(connection.Handle, _profile, null);
                }
            };
            return connection;
        }
    }

    /// <summary>
    /// 単独の Read の rowid の照会が終わった直後に、別の接続から文書 3 を消して別の文書を挿入する（rowid 3 を再利用させる）。
    /// 待ちは 1 秒で打ち切る（WAL では待たずにコミットできる）
    /// </summary>
    private sealed class RowidReuseIntrusion(string connectionString)
    {
        /// <summary>入れ替えを試みたか</summary>
        private bool _attempted;

        /// <summary>入れ替えがコミットされたか</summary>
        public bool Succeeded { get; private set; }

        /// <summary>profile フックの本体。ネイティブのコールバックの中なので例外は外へ出さない</summary>
        public void OnStatementFinished(string sql)
        {
            if (_attempted || !sql.StartsWith("SELECT rowid", StringComparison.Ordinal))
            {
                return;
            }

            _attempted = true;

            try
            {
                using var other = new SqliteConnection(
                    new SqliteConnectionStringBuilder(connectionString)
                    {
                        Pooling = false,
                        DefaultTimeout = 1,
                    }.ToString()
                );
                other.Open();
                using var command = other.CreateCommand();
                command.CommandText =
                    "BEGIN; DELETE FROM documents WHERE document_id = 3; "
                    + "INSERT INTO documents (document_id, title, is_published, payload, thumb) "
                    + "VALUES (4, 'intruder', 0, x'EEEEEEEE', x'01'); COMMIT;";
                command.ExecuteNonQuery();
                Succeeded = true;
            }
            catch (SqliteException)
            {
                // 書き込めなかった（Succeeded が false のままなのでテストが失敗として知らせる）
            }
        }
    }
}
