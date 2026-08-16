using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.Integration.GeneratedRuntime;
using Xunit;

namespace QuickER.Tests.GeneratedBinaryFixture;

/// <summary>
/// 楽観排他のランタイムスイートを<b>インメモリ Repository</b>で実行する派生（実 DB を使わないため Docker 不要＝CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// インメモリは DB を持たないため、ストアが単調増加する 8 バイトの擬似版番号を採番して SQL Server の rowversion を
/// 模す。バックエンド非依存のシナリオは基底 <see cref="ConcurrencyRuntimeTestsBase{TEntity, TConflictException}"/> が
/// 持ち、本クラスはインメモリ固有の<b>copy-on-write ステージング</b>の意味論——保存単位の all-or-nothing・
/// After がロック外で走っている間の並行更新・公開時検証の順序（存否→版）・版を持たない型の後勝ち——を検証する。
/// </para>
/// <para>
/// 「他者による更新」は、同じストアを共有する別のリポジトリインスタンス経由で更新して作る
/// （ストアは取得のたびに複製を返すため、手元のインスタンスの版は古いまま残る）。
/// </para>
/// <para>
/// 除外列（payload / thumb）は値を持ったままだと UPDATE が拒否される既存仕様のため、本テストは一貫して
/// 未取得状態（null / 空配列）のまま扱う。
/// </para>
/// </remarks>
public sealed class InMemoryConcurrencyRuntimeTests
    : ConcurrencyRuntimeTestsBase<DocumentEntity, SaveConflictException>
{
    /// <summary>基底のシナリオが共有するインメモリストア（実 DB のファイルに相当する永続点）</summary>
    private readonly InMemoryDataStore _store = new();

    /// <summary>文書リポジトリ（シード済みストアを共有する）</summary>
    private InMemoryDocumentRepository Documents => new(_store);

    /// <summary>メモリポジトリ（シード済みストアを共有する）</summary>
    private InMemoryDocumentNoteRepository Notes => new(_store);

    /// <summary>中立表現の楽観排他ポリシーを、このフィクスチャの <c>ConcurrencyMode</c> へ翻訳する</summary>
    private static ConcurrencyMode Translate(ConcurrencyChoice choice) =>
        choice switch
        {
            ConcurrencyChoice.Optimistic => ConcurrencyMode.Optimistic,
            ConcurrencyChoice.ForceOverwrite => ConcurrencyMode.ForceOverwrite,
            _ => (ConcurrencyMode)99,
        };

    protected override async Task ResetAndSeedAsync()
    {
        _store.Clear();

        await Documents.InsertAsync(NewEntity(SeededRootId, "alpha"), Ct);
        await Documents.InsertAsync(NewEntity(SeededChildlessRootId, "beta"), Ct);
        await Notes.InsertAsync(
            new DocumentNoteEntity
            {
                NoteId = SeededChildId,
                DocumentId = SeededRootId,
                Note = "first",
            },
            Ct
        );
    }

    protected override DocumentEntity NewEntity(int id, string title) =>
        new()
        {
            DocumentId = id,
            Title = title,
            Thumb = [],
        };

    protected override Task InsertAsync(DocumentEntity entity) => Documents.InsertAsync(entity, Ct);

    protected override Task<DocumentEntity?> GetAsync(int id) => Documents.GetByIdAsync(id, Ct);

    protected override Task<DocumentEntity?> GetWithChildrenAsync(int id) =>
        Documents
            .Query()
            .Where(d => d.DocumentId == id)
            .Include(d => d.DocumentNotes)
            .FirstOrDefaultAsync(Ct);

    protected override string GetTitle(DocumentEntity entity) => entity.Title;

    protected override void SetTitle(DocumentEntity entity, string title) => entity.Title = title;

    protected override byte[]? GetRowVersion(DocumentEntity entity) => entity.RowVer;

    protected override void SetRowVersion(DocumentEntity entity, byte[] rowVersion) =>
        entity.RowVer = rowVersion;

    protected override void MarkAdded(DocumentEntity entity) => entity.MarkAdded();

    protected override void MarkUpdated(DocumentEntity entity) => entity.MarkUpdated();

    protected override void MarkRemoved(DocumentEntity entity) => entity.MarkRemoved();

    protected override Task<bool> UpdateAsync(
        DocumentEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic
    ) => Documents.UpdateAsync(entity, Translate(mode), Ct);

    protected override Task<int> SaveAsync(
        DocumentEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic,
        bool insertWhenUpdateMissing = false
    ) =>
        Documents.SaveAsync(
            entity,
            insertWhenUpdateMissing: insertWhenUpdateMissing,
            mode: Translate(mode),
            cancellationToken: Ct
        );

    /// <summary>同じストアを共有する別インスタンス経由で更新する（手元のインスタンスの版は古いまま残る）</summary>
    protected override async Task BumpByAnotherUserAsync(int id, string title)
    {
        var other = Documents;
        var row = await other.GetByIdAsync(id, Ct);
        row.Should().NotBeNull();

        row!.Title = title;
        (await other.UpdateAsync(row, cancellationToken: Ct)).Should().BeTrue();
    }

    protected override void EditFirstChild(DocumentEntity root, string note)
    {
        var child = root.DocumentNotes.First();
        child.Note = note;
        child.MarkUpdated();
    }

    protected override async Task<string?> ReadChildNoteAsync(int noteId) =>
        (await Notes.GetByIdAsync(noteId, Ct))?.Note;

    // ── InMemory 固有 1: 版を持たない型 ──

    /// <summary>rowversion 列を持たない型（document_notes）は版チェックの対象外＝取得せずに更新できる</summary>
    [Fact(DisplayName = "[Concurrency/InMemory] rowversion 列のない型は版チェックの対象外になる")]
    public async Task TypeWithoutRowVersion_IsNotGuarded()
    {
        await ResetAndSeedAsync();

        // 取得せずに組み立てたインスタンス（版を持ちようがない）でも更新は通る
        var detached = new DocumentNoteEntity
        {
            NoteId = SeededChildId,
            DocumentId = SeededRootId,
            Note = "rewritten",
        };

        (await Notes.UpdateAsync(detached, cancellationToken: Ct)).Should().BeTrue();
        (await ReadChildNoteAsync(SeededChildId)).Should().Be("rewritten");
    }

    // ── InMemory 固有 2: copy-on-write ステージングによる保存単位の all-or-nothing ──

    /// <summary>
    /// グラフ保存の途中で競合が起きると、それより前に適用済みの書き込み（親の更新・別ルートの追加）も巻き戻る。
    /// ストアだけでなく<b>呼び出し元エンティティの版</b>も保存前のまま残るため、競合の原因を直してそのまま再保存できる。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: 途中競合はストアも呼び出し元の版も保存前へ戻す"
    )]
    public async Task SaveAsync_MidGraphConflict_RollsBackEarlierWrites()
    {
        await ResetAndSeedAsync();

        var stale = await Documents.GetByIdAsync(SeededRootId, Ct);

        // 他者が先に更新して stale の版を古くする
        await BumpByAnotherUserAsync(SeededRootId, "by-another-user");

        // 版が新しいルート（新規挿入）と、版が古いルート（文書 1）を 1 回の保存単位にまとめる
        var fresh = NewEntity(9, "inserted-in-same-unit");
        fresh.MarkAdded();

        var staleVersion = stale!.RowVer;
        stale.Title = "by-me";
        stale.MarkUpdated();

        var act = async () => await Documents.SaveAsync([fresh, stale], cancellationToken: Ct);
        await act.Should().ThrowAsync<SaveConflictException>();

        // 競合より前に適用された INSERT も巻き戻る（部分適用が残らない）
        (await Documents.GetByIdAsync(9, Ct))
            .Should()
            .BeNull("競合 1 件で保存単位ごと巻き戻る");
        (await ReadTitleAsync(SeededRootId))
            .Should()
            .Be("by-another-user", "競合した行は先勝ちのまま");

        // 呼び出し元エンティティの版も保存前のまま＝幻の版が残らない
        stale.RowVer.Should().Equal(staleVersion, "失敗した保存は新しい版を配らない");
        fresh.RowVer.Should().BeNull("巻き戻された INSERT の版も配られない");

        // 競合の原因（古い版）を解消すれば、同じインスタンスでそのまま再保存できる
        stale.RowVer = (await Documents.GetByIdAsync(SeededRootId, Ct))!.RowVer;
        stale.MarkUpdated();
        fresh.MarkAdded();
        (await Documents.SaveAsync([fresh, stale], cancellationToken: Ct)).Should().Be(2);
        (await ReadTitleAsync(SeededRootId)).Should().Be("by-me");
        (await Documents.GetByIdAsync(9, Ct)).Should().NotBeNull();
        (await ReadChildNoteAsync(SeededChildId))
            .Should()
            .Be("first", "無関係な行は影響を受けない");
    }

    /// <summary>
    /// After フックが例外を投げると保存フェーズごと巻き戻り、ストアも呼び出し元エンティティの版も保存前へ戻る
    /// （幻の版が残ると、同じインスタンスの再保存が偽の競合になる）。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: After 例外でストアも版も巻き戻り再保存できる"
    )]
    public async Task SaveAsync_AfterThrows_RollsBackStoreAndRowVersion()
    {
        await ResetAndSeedAsync();

        var hook = new RowVersionCapturingHook();
        var documents = new InMemoryDocumentRepository(_store, new SaveHookRegistry().Add(hook));

        var document = await documents.GetByIdAsync(SeededRootId, Ct);
        var beforeSave = document!.RowVer;
        beforeSave.Should().NotBeNull("取得時点で版が読める");

        document.Title = "by-me";
        document.MarkUpdated();

        var act = async () => await documents.SaveAsync(document, cancellationToken: Ct);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*after-boom*");

        document.RowVer.Should().Equal(beforeSave, "巻き戻された保存は新しい版を配らない");
        (await ReadTitleAsync(SeededRootId)).Should().Be("alpha", "行の更新も巻き戻る");

        // 幻の版が残っていれば、同一インスタンスのこの再保存は偽の競合になる
        hook.ThrowOnAfter = false;
        (await documents.SaveAsync(document, cancellationToken: Ct)).Should().Be(1);

        document.RowVer.Should().NotEqual(beforeSave, "成功後は新しい版が反映される");
        (await ReadTitleAsync(SeededRootId)).Should().Be("by-me");
    }

    /// <summary>After フックが見る版は「保存前の版」（新しい版は全フェーズ成功後に配られる＝コミット前の見え方）</summary>
    [Fact(DisplayName = "[Concurrency/InMemory] SaveAsync: After はコミット前の旧版を見る")]
    public async Task SaveAsync_AfterHook_SeesPreSaveRowVersion()
    {
        await ResetAndSeedAsync();

        var hook = new RowVersionCapturingHook { ThrowOnAfter = false };
        var documents = new InMemoryDocumentRepository(_store, new SaveHookRegistry().Add(hook));

        var document = await documents.GetByIdAsync(SeededRootId, Ct);
        var beforeSave = document!.RowVer;

        document.Title = "by-me";
        document.MarkUpdated();
        (await documents.SaveAsync(document, cancellationToken: Ct)).Should().Be(1);

        hook.SeenRowVersion.Should()
            .Equal(beforeSave, "After はコミット前に走るので保存前の版が見える");
        document.RowVer.Should().NotEqual(beforeSave, "保存が完了した時点で新しい版が反映される");
    }

    // ── InMemory 固有 3: copy-on-write の公開（After がロック外で走っている間の並行更新） ──

    /// <summary>
    /// After フックの待機中に別インスタンスが同じ行を正常更新し、その後 After が例外を投げても、
    /// <b>他者の更新は消えない</b>（保存は 1 度もストアへ書いていないため、失敗しても巻き戻すものが無い）。
    /// </summary>
    /// <remarks>
    /// 旧実装（undo ジャーナル）は失敗時に「保存前のスナップショット」を無条件で書き戻していたため、
    /// After 待機中に割り込んだ他者の更新をまるごと消していた。
    /// </remarks>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: After 例外の失敗は並行更新を巻き添えにしない"
    )]
    public async Task SaveAsync_AfterThrows_LeavesConcurrentUpdateIntact()
    {
        await ResetAndSeedAsync();

        var hook = new GatedAfterHook<DocumentEntity> { ThrowOnAfter = true };
        var documents = new InMemoryDocumentRepository(_store, new SaveHookRegistry().Add(hook));
        var other = Documents;

        var mine = await documents.GetByIdAsync(SeededRootId, Ct);
        var beforeSave = mine!.RowVer;
        mine.Title = "by-me";
        mine.MarkUpdated();

        // After に入った時点では保存はまだ 1 バイトもストアへ書いていない
        var saving = documents.SaveAsync(mine, cancellationToken: Ct);
        await hook.Entered.Task;

        // 別インスタンスの正常な更新（ストアの現在値は保存前のままなので成立する）
        var theirs = await other.GetByIdAsync(SeededRootId, Ct);
        theirs!.Title = "by-another-user";
        (await other.UpdateAsync(theirs, cancellationToken: Ct))
            .Should()
            .BeTrue("公開前なので他者から見た行は保存前のまま＝更新が通る");

        hook.Release.TrySetResult();
        var act = async () => await saving;
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*after-boom*");

        var stored = await other.GetByIdAsync(SeededRootId, Ct);
        stored!.Title.Should().Be("by-another-user", "失敗した保存は他者の更新を消さない");
        stored.RowVer.Should().Equal(theirs.RowVer, "他者が受け取った版もそのまま有効なまま残る");
        mine.RowVer.Should().Equal(beforeSave, "失敗した保存は新しい版を配らない");
    }

    /// <summary>
    /// After フックの待機中に別インスタンスが同じ行を更新すると、After が正常終了しても公開時の検証で
    /// <c>SaveConflictException</c> になる（並行更新は無傷のまま残る）。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: After 待機中の並行更新は公開時に競合として弾かれる"
    )]
    public async Task SaveAsync_ConcurrentUpdateDuringAfter_IsRejectedAtPublish()
    {
        await ResetAndSeedAsync();

        var hook = new GatedAfterHook<DocumentEntity>();
        var documents = new InMemoryDocumentRepository(_store, new SaveHookRegistry().Add(hook));
        var other = Documents;

        var mine = await documents.GetByIdAsync(SeededRootId, Ct);
        var beforeSave = mine!.RowVer;
        mine.Title = "by-me";
        mine.MarkUpdated();

        var saving = documents.SaveAsync(mine, cancellationToken: Ct);
        await hook.Entered.Task;

        var theirs = await other.GetByIdAsync(SeededRootId, Ct);
        theirs!.Title = "by-another-user";
        (await other.UpdateAsync(theirs, cancellationToken: Ct)).Should().BeTrue();

        hook.Release.TrySetResult();
        var act = async () => await saving;

        await act.Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage(
                ConflictMessage,
                "保存フェーズを通過した後でも、公開時に他者の書き込みを検出する"
            );

        (await ReadTitleAsync(SeededRootId))
            .Should()
            .Be("by-another-user", "競合した保存は並行更新を上書きしない");
        mine.RowVer.Should().Equal(beforeSave, "公開されなかった保存は新しい版を配らない");
    }

    /// <summary>
    /// rowversion 列を持たない型は公開時検証の対象外＝After 待機中に並行更新があっても競合にならず、
    /// 保存が後勝ち（last-write-wins）で公開される。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: rowversion なしの型は公開時検証の対象外（後勝ち）"
    )]
    public async Task SaveAsync_TypeWithoutRowVersion_IsNotVerifiedAtPublish()
    {
        await ResetAndSeedAsync();

        var hook = new GatedAfterHook<DocumentNoteEntity>();
        var notes = new InMemoryDocumentNoteRepository(
            _store,
            new SaveHookRegistry().Add<DocumentNoteEntity>(hook)
        );
        var other = Notes;

        var mine = await notes.GetByIdAsync(SeededChildId, Ct);
        mine!.Note = "by-me";
        mine.MarkUpdated();

        var saving = notes.SaveAsync(mine, cancellationToken: Ct);
        await hook.Entered.Task;

        var theirs = await other.GetByIdAsync(SeededChildId, Ct);
        theirs!.Note = "by-another-user";
        (await other.UpdateAsync(theirs, cancellationToken: Ct)).Should().BeTrue();

        hook.Release.TrySetResult();
        (await saving).Should().Be(1, "版を持たない型は公開時検証を行わない＝競合にならない");

        (await ReadChildNoteAsync(SeededChildId))
            .Should()
            .Be("by-me", "版のない型の契約どおり後勝ちで公開される");
    }

    /// <summary>
    /// 後勝ちは「復活」までは含まない: After 待機中に他者が行を<b>削除</b>すると、版を持たない型でも
    /// <c>SaveConflictException</c>（<see cref="SaveConflictReason.NotFound"/>）になる。
    /// </summary>
    /// <remarks>
    /// 黙って staged 更新を捨てると「戻り値 1 件・エンティティは Unchanged・ストアに行なし」という、保存できたと
    /// 報告しながら行が存在しない組合せが成立してしまう（実 DB の UPDATE は対象行が無ければ 0 行更新で終わる）。
    /// </remarks>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: After 待機中の他者削除は NotFound の競合になる"
    )]
    public async Task SaveAsync_RowDeletedDuringAfter_IsRejectedAsNotFound()
    {
        await ResetAndSeedAsync();

        var hook = new GatedAfterHook<DocumentNoteEntity>();
        var notes = new InMemoryDocumentNoteRepository(
            _store,
            new SaveHookRegistry().Add<DocumentNoteEntity>(hook)
        );
        var other = Notes;

        var mine = await notes.GetByIdAsync(SeededChildId, Ct);
        mine!.Note = "by-me";
        mine.MarkUpdated();

        var saving = notes.SaveAsync(mine, cancellationToken: Ct);
        await hook.Entered.Task;

        (await other.DeleteAsync(SeededChildId, Ct)).Should().BeTrue();

        hook.Release.TrySetResult();
        var act = async () => await saving;

        (await act.Should().ThrowAsync<SaveConflictException>())
            .Which.Reason.Should()
            .Be(
                SaveConflictReason.NotFound,
                "消えた行への更新は「保存できた」ではなく行なしの競合として報告される"
            );

        (await ReadChildNoteAsync(SeededChildId)).Should().BeNull("削除された行は復活しない");
        mine.RowState.Should().Be(RowState.Updated, "失敗した保存は RowState を確定させない");
    }

    /// <summary>
    /// 版を持つ型でも同じ状況は <see cref="SaveConflictReason.NotFound"/>。存否の判定は版の比較より先に
    /// 行うため、分類は版の有無に依らず揃う。
    /// </summary>
    /// <remarks>
    /// 旧実装は版比較が先で、版を持つ型に限って <see cref="SaveConflictReason.Modified"/> を返していた
    /// （消えた行に対して版を比べても何も言えないうえ、再取得しても行が無いので「再読込して再試行」という
    /// 指示が空振りする）。SQL Server が UPDATE 0 行のとき実在確認で「消えた」と「変わった」を分ける二分と
    /// 同じ意味論へ揃えた。
    /// </remarks>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: 版あり型の他者削除も NotFound（版の有無で分類が割れない）"
    )]
    public async Task SaveAsync_VersionedRowDeletedDuringAfter_ReportsNotFound()
    {
        await ResetAndSeedAsync();

        var hook = new GatedAfterHook<DocumentEntity>();
        var documents = new InMemoryDocumentRepository(_store, new SaveHookRegistry().Add(hook));
        var other = Documents;

        var mine = await documents.GetByIdAsync(SeededChildlessRootId, Ct);
        mine!.Title = "by-me";
        mine.MarkUpdated();

        var saving = documents.SaveAsync(mine, cancellationToken: Ct);
        await hook.Entered.Task;

        (await other.DeleteAsync(SeededChildlessRootId, Ct)).Should().BeTrue();

        hook.Release.TrySetResult();
        var act = async () => await saving;

        (await act.Should().ThrowAsync<SaveConflictException>())
            .Which.Reason.Should()
            .Be(
                SaveConflictReason.NotFound,
                "版を持っていても、無くなった行への更新は「変更された」ではなく「無くなった」"
            );

        (await GetAsync(SeededChildlessRootId)).Should().BeNull("削除された行は復活しない");
        mine.RowState.Should().Be(RowState.Updated, "失敗した保存は RowState を確定させない");
    }

    /// <summary>
    /// After が同じ行へ blob を書くと版がもう一段進むが、呼び出し元エンティティには
    /// <b>公開された最終版</b>が反映される（保存フェーズ時点の版を配ると、次の保存が偽の競合になる）。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: After の blob 書き込み後の最終版が呼び出し元へ反映される"
    )]
    public async Task SaveAsync_AfterWritesBinaryColumn_HandsBackPublishedRowVersion()
    {
        await ResetAndSeedAsync();

        var payload = new byte[128];
        new Random(3).NextBytes(payload);

        var documents = new InMemoryDocumentRepository(
            _store,
            new SaveHookRegistry().Add(new BinaryWritingHook(payload))
        );
        var plain = Documents;

        var document = await documents.GetByIdAsync(SeededRootId, Ct);
        document!.Title = "by-me";
        document.MarkUpdated();
        (await documents.SaveAsync(document, cancellationToken: Ct)).Should().Be(1);

        var stored = await plain
            .Query()
            .Where(d => d.DocumentId == SeededRootId)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);
        stored!.Payload.Should().Equal(payload, "After が書いた blob が公開されている");
        stored.Title.Should().Be("by-me", "保存フェーズの更新も同じ単位で公開される");
        document
            .RowVer.Should()
            .Equal(stored.RowVer, "呼び出し元の版は blob 書き込み後の最終版と一致する");

        // 古い版を配っていれば、この再保存は偽の競合になる
        document.Title = "again";
        document.MarkUpdated();
        (await documents.SaveAsync(document, cancellationToken: Ct))
            .Should()
            .Be(1, "反映された版はそのまま次の保存に使える（再取得不要）");
    }

    /// <summary>
    /// After で合図を出してからテスト側の解放を待つ Save フック（ロック外で走る After の最中に、
    /// 別スレッドの更新を決定的に差し込むためのゲート）。
    /// </summary>
    private sealed class GatedAfterHook<TEntity> : ISaveHook<TEntity>
        where TEntity : EntityBase
    {
        /// <summary>After に到達したことを知らせる合図</summary>
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>After を先へ進めてよいことをテスト側が知らせる合図</summary>
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>解放されたあとに例外を投げるか</summary>
        public bool ThrowOnAfter { get; init; }

        public async Task AfterSaveAsync(
            TEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            Entered.TrySetResult();
            await Release.Task;

            if (ThrowOnAfter)
            {
                throw new InvalidOperationException("after-boom");
            }
        }
    }

    /// <summary>After で除外列 blob を書き込む Save フック（保存フェーズの後に版がもう一段進む経路の再現用）</summary>
    private sealed class BinaryWritingHook(byte[] payload) : ISaveHook<DocumentEntity>
    {
        public async Task AfterSaveAsync(
            DocumentEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) =>
            await context.WriteBinaryColumnAsync(
                nameof(DocumentEntity.Payload),
                entity.DocumentId,
                new MemoryStream(payload),
                cancellationToken: cancellationToken
            );
    }

    /// <summary>After が見た版を記録し、任意で例外を投げる Save フック（版の反映タイミング検証用）</summary>
    private sealed class RowVersionCapturingHook : ISaveHook<DocumentEntity>
    {
        /// <summary>After で例外を投げるか（true の間は保存が丸ごと巻き戻る）</summary>
        public bool ThrowOnAfter { get; set; } = true;

        /// <summary>After が呼ばれた時点でエンティティが持っていた版</summary>
        public byte[]? SeenRowVersion { get; private set; }

        public Task AfterSaveAsync(
            DocumentEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            SeenRowVersion = entity.RowVer;

            if (ThrowOnAfter)
            {
                throw new InvalidOperationException("after-boom");
            }

            return Task.CompletedTask;
        }
    }
}
