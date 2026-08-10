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
/// rowversion 列を持つテーブルの楽観排他（<c>ConcurrencyMode</c>）を、
/// <b>SQL Server 方言</b>のQuickER 版 Repository で実 SQL Server（Testcontainers・Docker 依存）に流して検証する。
/// </summary>
/// <remarks>
/// <para>
/// 入力は <see cref="SqlServerBinaryFixtureDefinition"/>（<c>documents</c>＝rowversion <c>row_ver</c> を持つ・
/// 子 <c>document_notes</c> は 1対多カスケード）。「rowversion 列あり＝楽観排他」の確定仕様を、単一 CRUD と
/// グラフ保存の両方で固定する:
/// </para>
/// <list type="bullet">
///   <item>UPDATE / DELETE は版条件（<c>WHERE ... AND row_ver = @originalRowVersion</c>）付きで走り、
///     他者が先に更新していれば <c>SaveConflictException</c></item>
///   <item><c>ConcurrencyMode.ForceOverwrite</c> は版条件を外す（明示的な last-write-wins）</item>
///   <item>単一 <c>UpdateAsync</c> の 0 件は実在確認で「行なし=false（従来契約）／版が古い=throw」を区別する</item>
///   <item><c>insertWhenUpdateMissing</c> も同じ実在確認で「行なし→INSERT／版が古い→throw」を区別する
///     （版の古い行を INSERT へ倒すと主キー重複になるため）</item>
///   <item>保存成功後は DB が採番した新しい版番号が<b>同一インスタンス</b>へ反映される（再読込不要）</item>
/// </list>
/// <para>
/// 「他者による更新」は生 SQL（<c>ExecuteSqlAsync</c>）で直接 UPDATE して作る＝Repository を経由しないため
/// 手元のエンティティの <c>RowVer</c> は古いまま残り、実際の競合と同じ状態を決定的に再現できる。
/// </para>
/// <para>
/// Docker 不在時は <see cref="SqlServerContainerFixture"/> の検出でスキップされる（CI では常にスキップ）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerConcurrencyRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>QuickER の SQL Server リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider _provider = null!;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>スキーマを作成し、文書 1 件（メモ 1 件つき）をシードする</summary>
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

        await Documents()
            .InsertAsync(
                new DocumentEntity
                {
                    DocumentId = 1,
                    Title = "alpha",
                    Thumb = [1],
                },
                Ct
            );
        await Notes()
            .InsertAsync(
                new DocumentNoteEntity
                {
                    NoteId = 1,
                    DocumentId = 1,
                    Note = "first",
                },
                Ct
            );
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>文書リポジトリを解決する</summary>
    private IDocumentRepository Documents() => _provider.GetRequiredService<IDocumentRepository>();

    /// <summary>メモリポジトリを解決する</summary>
    private IDocumentNoteRepository Notes() =>
        _provider.GetRequiredService<IDocumentNoteRepository>();

    /// <summary>
    /// Repository を経由せず生 SQL で直接 1 行更新して「他者による更新」を作る
    /// （手元のエンティティの RowVer は古いまま残る）。
    /// </summary>
    private Task BumpByAnotherUserAsync(int documentId, string title) =>
        Documents()
            .ExecuteSqlAsync(
                "UPDATE documents SET title = @title WHERE document_id = @id",
                new { title, id = documentId },
                Ct
            );

    /// <summary>DB 上の現在のタイトルを読む（先勝ち／上書きの確認用）</summary>
    private Task<string?> ReadTitleAsync(int documentId) =>
        Documents()
            .ExecuteScalarSqlAsync<string>(
                "SELECT title FROM documents WHERE document_id = @id",
                new { id = documentId },
                Ct
            );

    // ── 単一 UpdateAsync ──

    /// <summary>1. 他者が先に更新した行を古いエンティティで更新すると SaveConflictException（DB は先勝ちのまま）</summary>
    [Fact(
        DisplayName = "[Concurrency/SqlServer] UpdateAsync: 版が古いと SaveConflictException・DB は先勝ちのまま"
    )]
    public async Task UpdateAsync_Throws_WhenRowVersionIsStale()
    {
        var documents = Documents();

        var stale = await documents.GetByIdAsync(1, Ct);
        stale.Should().NotBeNull();
        stale!.RowVer.Should().NotBeNull("取得時点で版番号が読める");

        await BumpByAnotherUserAsync(1, "by-another-user");

        stale.Title = "by-me";
        var act = async () => await documents.UpdateAsync(stale, cancellationToken: Ct);

        await act.Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage("*modified by another user*", "版条件に一致せず競合として弾かれる");

        (await ReadTitleAsync(1))
            .Should()
            .Be("by-another-user", "競合した更新は適用されない（先勝ち）");
    }

    /// <summary>2. ForceOverwrite は版条件を外して上書きし、新しい版番号をエンティティへ反映する</summary>
    [Fact(
        DisplayName = "[Concurrency/SqlServer] UpdateAsync: ForceOverwrite は版を無視して上書きし新版を反映する"
    )]
    public async Task UpdateAsync_ForceOverwrite_OverwritesAndRefreshesRowVersion()
    {
        var documents = Documents();

        var stale = await documents.GetByIdAsync(1, Ct);
        stale.Should().NotBeNull();
        var staleVersion = stale!.RowVer;

        await BumpByAnotherUserAsync(1, "by-another-user");

        stale.Title = "forced";
        (await documents.UpdateAsync(stale, ConcurrencyMode.ForceOverwrite, Ct))
            .Should()
            .BeTrue("版条件を外すので更新は成立する");

        (await ReadTitleAsync(1)).Should().Be("forced", "last-write-wins で上書きされる");
        stale.RowVer.Should().NotEqual(staleVersion, "上書き後の新しい版番号が反映される");
    }

    /// <summary>3. 存在しない行の更新は従来どおり false（競合ではない）</summary>
    [Fact(DisplayName = "[Concurrency/SqlServer] UpdateAsync: 行が存在しなければ従来どおり false")]
    public async Task UpdateAsync_ReturnsFalse_WhenRowIsMissing()
    {
        var documents = Documents();

        var missing = new DocumentEntity
        {
            DocumentId = 999,
            Title = "ghost",
            Thumb = [],
            RowVer = [0, 0, 0, 0, 0, 0, 0, 1],
        };

        (await documents.UpdateAsync(missing, cancellationToken: Ct))
            .Should()
            .BeFalse("行なしは競合ではなく「更新対象なし」＝従来契約の false");
    }

    /// <summary>4. Insert / Update / Save の成功後に同一インスタンスの版番号が更新される</summary>
    [Fact(
        DisplayName = "[Concurrency/SqlServer] 版反映: Insert / Update / Save 後に同一インスタンスの RowVer が変化する"
    )]
    public async Task RowVersion_IsWrittenBack_ToTheSameInstance()
    {
        var documents = Documents();

        // INSERT: OUTPUT INSERTED で採番された版がそのまま入る
        var entity = new DocumentEntity
        {
            DocumentId = 2,
            Title = "inserted",
            Thumb = [2],
        };
        await documents.InsertAsync(entity, Ct);

        // 除外列（thumb）は INSERT では渡せるが UPDATE 対象外＝値を持ったままだと更新が拒否される既存仕様のため、
        // 挿入後に未取得状態（空）へ戻す
        entity.Thumb = [];
        entity.RowVer.Should().NotBeNull("INSERT 直後に採番された版が入る");
        entity.RowVer!.Length.Should().Be(8, "SQL Server の rowversion は 8 バイト");
        var afterInsert = entity.RowVer;

        // UPDATE: 版が進む
        entity.Title = "updated";
        (await documents.UpdateAsync(entity, cancellationToken: Ct)).Should().BeTrue();
        entity.RowVer.Should().NotEqual(afterInsert, "UPDATE のたびに版が進む");
        var afterUpdate = entity.RowVer;

        // SaveAsync（グラフ保存）: コミット後に版が進む
        entity.Title = "saved";
        entity.MarkUpdated();
        (await documents.SaveAsync(entity, cancellationToken: Ct)).Should().Be(1);
        entity.RowVer.Should().NotEqual(afterUpdate, "グラフ保存でも新版が反映される");

        // 反映された版がそのまま次の更新に使える（再読込せずに更新が通る）
        entity.Title = "again";
        (await documents.UpdateAsync(entity, cancellationToken: Ct))
            .Should()
            .BeTrue("反映された版は DB の現在値と一致するので再読込なしで更新できる");
    }

    // ── グラフ保存 ──

    /// <summary>5. 競合ノードを含むグラフ保存は SaveConflictException で全ロールバックする</summary>
    [Fact(DisplayName = "[Concurrency/SqlServer] SaveAsync: 競合ノードがあると全ロールバックする")]
    public async Task SaveAsync_RollsBackEverything_OnConflict()
    {
        var documents = Documents();

        var root = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .Include(d => d.DocumentNotes)
            .FirstOrDefaultAsync(Ct);
        root.Should().NotBeNull();
        root!.DocumentNotes.Should().HaveCount(1, "シードしたメモ 1 件が読める");

        await BumpByAnotherUserAsync(1, "by-another-user");

        // 子（メモ）は競合していないが、親が競合するため全体が巻き戻る
        root.Title = "by-me";
        root.MarkUpdated();
        var note = root.DocumentNotes.First();
        note.Note = "note-by-me";
        note.MarkUpdated();

        var act = async () => await documents.SaveAsync(root, cancellationToken: Ct);

        await act.Should().ThrowAsync<SaveConflictException>();

        (await ReadTitleAsync(1)).Should().Be("by-another-user", "親は先勝ちのまま");
        var reloadedNote = await Notes().GetByIdAsync(1, Ct);
        reloadedNote!
            .Note.Should()
            .Be("first", "競合していない子の更新もロールバックされる（all-or-nothing）");
    }

    /// <summary>6. insertWhenUpdateMissing は「行なし→INSERT」「版が古い→競合」を区別する</summary>
    [Fact(
        DisplayName = "[Concurrency/SqlServer] SaveAsync: insertWhenUpdateMissing は行なしと版違いを区別する"
    )]
    public async Task SaveAsync_InsertWhenUpdateMissing_DistinguishesMissingRowFromStaleVersion()
    {
        var documents = Documents();

        // 行なし: INSERT へ切り替わる
        var missing = new DocumentEntity
        {
            DocumentId = 3,
            Title = "switched-to-insert",
            // 除外列は UPDATE 対象外＝値を持つと更新が拒否されるため空にする（この保存は UPDATE から INSERT へ切り替わる）
            Thumb = [],
        };
        missing.MarkUpdated();

        (await documents.SaveAsync(missing, insertWhenUpdateMissing: true, cancellationToken: Ct))
            .Should()
            .Be(1, "行が無いので INSERT へ切り替わる");
        (await documents.GetByIdAsync(3, Ct)).Should().NotBeNull();

        // 版が古い: INSERT へ倒さず競合として弾く（倒すと主キー重複になる）
        var stale = await documents.GetByIdAsync(1, Ct);
        await BumpByAnotherUserAsync(1, "by-another-user");
        stale!.Title = "by-me";
        stale.MarkUpdated();

        var act = async () =>
            await documents.SaveAsync(stale, insertWhenUpdateMissing: true, cancellationToken: Ct);

        await act.Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage(
                "*modified by another user*",
                "版違いは INSERT へ倒さず競合として報告する（主キー重複に化けない）"
            );
    }

    /// <summary>7. 削除も版条件で守られ、ForceOverwrite なら通る</summary>
    [Fact(
        DisplayName = "[Concurrency/SqlServer] SaveAsync: 削除も版条件で守られ ForceOverwrite なら通る"
    )]
    public async Task SaveAsync_Delete_IsGuardedByRowVersion()
    {
        var documents = Documents();

        var stale = await documents.GetByIdAsync(1, Ct);
        stale.Should().NotBeNull();

        await BumpByAnotherUserAsync(1, "by-another-user");

        stale!.MarkRemoved();
        var act = async () => await documents.SaveAsync(stale, cancellationToken: Ct);

        await act.Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage("*modified by another user*", "版の古い行は削除できない");
        (await documents.GetByIdAsync(1, Ct)).Should().NotBeNull("削除はロールバックされる");

        // ForceOverwrite は版条件を外すので削除が通る
        (
            await documents.SaveAsync(
                stale,
                mode: ConcurrencyMode.ForceOverwrite,
                cancellationToken: Ct
            )
        )
            .Should()
            .BeGreaterThan(0);
        (await documents.GetByIdAsync(1, Ct)).Should().BeNull("版条件を外せば削除される");
    }
}
