using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration;

/// <summary>
/// 無制限バイナリ列の除外（<c>ExcludeUnboundedBinaryColumns</c>）を、実 SQLite（一時ファイル DB・Docker 不要＝
/// CI 常時実行）で意味検証するパリティスイートの共通基底。DB ライフサイクル・DDL 適用・共通シードだけを持ち、
/// 観点別の <c>[Fact]</c> は「除外が効く自作 Repository 版（<see cref="BinaryColumnAdoRuntimeTests"/>）」と
/// 「除外非適用の EF Core 版（<see cref="BinaryColumnEfCoreRuntimeTests"/>）」で期待が逆になるため各派生に書く。
/// </summary>
/// <remarks>
/// 入力はバイナリフィクスチャ（<see cref="BinaryFixtureDefinition"/>）。<c>documents</c> は無制限バイナリ列
/// （<c>payload</c>＝nullable・<c>thumb</c>＝非 nullable）と除外対象外のバイナリ（<c>checksum</c>＝有界・
/// <c>row_ver</c>＝rowversion）を併せ持つ。子 <c>document_notes</c> は Include・グラフ保存の検証用。
/// </remarks>
[Trait("Category", "Integration")]
public abstract class BinaryColumnRuntimeTestsBase : IDisposable
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>書き込み可能な接続文字列（バックエンドはこの実ファイルへ読み書きする）</summary>
    protected string ConnectionString => _db.ReadWriteCreateConnectionString;

    /// <summary>文書リポジトリを生成する（自作 = AddGeneratedRepositories / EF = AddGeneratedEfCoreRepositories）</summary>
    protected abstract IDocumentRepository CreateDocumentRepository();

    /// <summary>文書メモ（子）リポジトリを生成する</summary>
    protected abstract IDocumentNoteRepository CreateDocumentNoteRepository();

    /// <summary>文書 1 の本体バイナリ（payload。除外列だが INSERT では DB に書かれる）</summary>
    protected static readonly byte[] Doc1Payload = [1, 2, 3, 4];

    /// <summary>文書 1 のサムネイル（thumb。非 nullable の除外列）</summary>
    protected static readonly byte[] Doc1Thumb = [9, 9];

    /// <summary>文書 1 のチェックサム（checksum。有界 varbinary(16)＝除外対象外）</summary>
    protected static readonly byte[] Doc1Checksum = [10, 11, 12];

    /// <summary>文書 3 の本体バイナリ（payload。CountWithPayload の 2 件目）</summary>
    protected static readonly byte[] Doc3Payload = [5, 6];

    /// <summary>スキーマを作成し、共通のシードデータを投入する</summary>
    /// <remarks>
    /// documents: 1="alpha"（payload あり・checksum あり）・2="beta"（payload なし）・3="gamma"（payload あり）。
    /// document_notes: (100, 文書1, "note-a")・(101, 文書1, "note-b")。
    /// シードは各派生のリポジトリ経由（自作は全列 INSERT・EF は除外非適用で全列）で投入する。
    /// </remarks>
    protected async Task ResetAndSeedAsync()
    {
        await using (var conn = new SqliteConnection(ConnectionString))
        {
            await conn.OpenAsync(Ct);

            await using var drop = conn.CreateCommand();
            drop.CommandText =
                "DROP TABLE IF EXISTS \"document_notes\"; DROP TABLE IF EXISTS \"documents\";";
            await drop.ExecuteNonQueryAsync(Ct);
        }

        var ddl = new SqliteDdlGenerator().Build(BinaryFixtureDefinition.Build());
        await _db.ApplyDdlAsync(ddl, Ct);

        var documents = CreateDocumentRepository();
        var notes = CreateDocumentNoteRepository();

        await documents.InsertAsync(
            NewDocument(1, "alpha", Doc1Payload, Doc1Thumb, Doc1Checksum),
            Ct
        );
        await documents.InsertAsync(NewDocument(2, "beta", null, [8], null), Ct);
        await documents.InsertAsync(NewDocument(3, "gamma", Doc3Payload, [6], null), Ct);
        await notes.InsertAsync(NewNote(100, 1, "note-a"), Ct);
        await notes.InsertAsync(NewNote(101, 1, "note-b"), Ct);
    }

    /// <summary>文書エンティティを組み立てる</summary>
    protected static DocumentEntity NewDocument(
        int id,
        string title,
        byte[]? payload,
        byte[] thumb,
        byte[]? checksum
    ) =>
        new()
        {
            DocumentId = id,
            Title = title,
            Payload = payload,
            Thumb = thumb,
            Checksum = checksum,
        };

    /// <summary>文書メモ（子）エンティティを組み立てる</summary>
    protected static DocumentNoteEntity NewNote(int id, int documentId, string note) =>
        new()
        {
            NoteId = id,
            DocumentId = documentId,
            Note = note,
        };

    // ── 読み取りオプトイン WithUnboundedBinary() の検証 ──
    // 「除外列を含めて取得する」効果は自作 Repository と EF Core で結果が同一（EF は元々全列）のため、
    // パリティを担保するべく本基底に [Fact] を置き、Ado / EF 両派生で同じテストを実行させる。

    /// <summary>WithUnboundedBinary().FirstOrDefaultAsync() は除外列（payload/thumb）の実データを返し、RowState は Unchanged になる</summary>
    [Fact(
        DisplayName = "[Binary] WithUnboundedBinary/FirstOrDefault は除外列の実データを返す（RowState=Unchanged）"
    )]
    public async Task WithUnboundedBinary_FirstOrDefault_ReturnsBinaryData()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

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

    /// <summary>WithUnboundedBinary().ToListAsync() は各エンティティの除外列（payload/thumb）を実データで返す</summary>
    [Fact(
        DisplayName = "[Binary] WithUnboundedBinary/ToList は各エンティティの除外列を実データで返す"
    )]
    public async Task WithUnboundedBinary_ToList_ReturnsBinaryData()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        var docs = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .ToListAsync(Ct);

        docs.Should().ContainSingle();
        docs[0].Payload.Should().Equal(Doc1Payload);
        docs[0].Thumb.Should().Equal(Doc1Thumb);
        docs[0].RowState.Should().Be(RowState.Unchanged);
    }

    /// <summary>WithUnboundedBinary と Include の併用は、指定順序に依らず・終端種別に依らず InvalidOperationException になる</summary>
    [Fact(
        DisplayName = "[Binary] WithUnboundedBinary と Include の併用は両順序・全終端で例外になる"
    )]
    public async Task WithUnboundedBinary_WithInclude_Throws()
    {
        await ResetAndSeedAsync();
        var documents = CreateDocumentRepository();

        // 順序 1: Include → WithUnboundedBinary
        var includeThenWith = async () =>
            await documents
                .Query()
                .Where(d => d.DocumentId == 1)
                .Include(d => d.DocumentNotes)
                .WithUnboundedBinary()
                .FirstOrDefaultAsync(Ct);
        (await includeThenWith.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("Include");

        // 順序 2: WithUnboundedBinary → Include
        var withThenInclude = async () =>
            await documents
                .Query()
                .Where(d => d.DocumentId == 1)
                .WithUnboundedBinary()
                .Include(d => d.DocumentNotes)
                .ToListAsync(Ct);
        await withThenInclude.Should().ThrowAsync<InvalidOperationException>();

        // 終端の種別に依らず併用自体が拒否される（Count でも throw＝予測可能性優先）
        var countTerminal = async () =>
            await documents
                .Query()
                .Include(d => d.DocumentNotes)
                .WithUnboundedBinary()
                .CountAsync(Ct);
        await countTerminal.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>使い終えた一時 DB を破棄する（派生の DI コンテナ破棄は派生側で行う）</summary>
    public virtual void Dispose() => _db.Dispose();
}
