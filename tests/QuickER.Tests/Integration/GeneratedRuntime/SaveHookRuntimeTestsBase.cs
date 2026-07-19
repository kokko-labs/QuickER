using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// Save フック（<see cref="ISaveHook{TEntity}"/>）の意味論を、実装先（QuickER 版 Repository の SQLite・EF Core・インメモリ）を
/// 跨いでパリティ検証する共通基底。バックエンド非依存のシナリオ（スキップ・短絡順序・insertWhenUpdateMissing の非対称・
/// サブツリー削除の per-node 発火・直接操作の素通り・未登録 no-op・IEnumerable 形態）を <c>[Fact]</c> として持ち、
/// 各派生はリポジトリ生成・シードだけを差し込む。
/// </summary>
/// <remarks>
/// <para>
/// 入力はバイナリフィクスチャ（<see cref="BinaryFixtureDefinition"/>）。<c>documents</c>（親）と子 <c>document_notes</c> を使う。
/// 状態の確認は生 SQL ではなくリポジトリ API（<c>GetByIdAsync</c> / <c>Query()</c>）で行うため、生 SQL を持たないインメモリでも
/// 同じアサーションが成立する。
/// </para>
/// <para>
/// 実装先で唯一期待が分かれるのは<b>「After 例外時の残留」</b>で、QuickER 版 Repository・EF Core は 1 トランザクションのため
/// ロールバックして残らず（<see cref="AfterExceptionLeavesResidue"/>=false）、インメモリは実トランザクションを持たず保存フェーズの
/// 変更が残る（=true）。After の同一トランザクション書き込み（除外列 blob・生 SQL）や FK 制約ロールバックは、context 操作の
/// 対応が実装先で異なるため各派生のバックエンド固有 <c>[Fact]</c> で検証する。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public abstract class SaveHookRuntimeTestsBase
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>文書 1 の本体バイナリ（payload。除外列だが INSERT では DB に書かれる）</summary>
    protected static readonly byte[] Doc1Payload = [1, 2, 3, 4];

    /// <summary>スキーマ（または空ストア）を用意し、共通のシードを投入する</summary>
    /// <remarks>documents: 1="alpha"（payload あり）・2="beta"（payload なし）・3="gamma"。document_notes: (100,1)・(101,1)</remarks>
    protected abstract Task ResetAndSeedAsync();

    /// <summary>指定した Save フック群を登録した文書リポジトリを生成する（フックなしなら状態確認用の素の実装）</summary>
    protected abstract IDocumentRepository Documents(params object[] hooks);

    /// <summary>指定した Save フック群を登録した文書メモ（子）リポジトリを生成する</summary>
    protected abstract IDocumentNoteRepository Notes(params object[] hooks);

    /// <summary>After が例外を投げたとき、保存フェーズで確定した変更が残るか（QuickER/EF Core=false・InMemory=true）</summary>
    protected abstract bool AfterExceptionLeavesResidue { get; }

    /// <summary>文書エンティティを組み立てる</summary>
    protected static DocumentEntity NewDocument(
        int id,
        string title,
        byte[]? payload,
        byte[] thumb
    ) =>
        new()
        {
            DocumentId = id,
            Title = title,
            Payload = payload,
            Thumb = thumb,
        };

    /// <summary>文書メモ（子）エンティティを組み立てる</summary>
    protected static DocumentNoteEntity NewNote(int id, int documentId, string note) =>
        new()
        {
            NoteId = id,
            DocumentId = documentId,
            Note = note,
        };

    /// <summary>文書が存在するか（バックエンド非依存＝GetById で確認）</summary>
    protected async Task<bool> DocumentExistsAsync(int id) =>
        await Documents().GetByIdAsync(id, Ct) is not null;

    /// <summary>指定文書に属する子メモの件数（バックエンド非依存＝Query().Count で確認）</summary>
    protected Task<int> NoteCountAsync(int documentId) =>
        Notes().Query().Where(n => n.DocumentId == documentId).CountAsync(Ct);

    // ── 1. Before false の単独スキップ ──

    /// <summary>1. Before が false のエンティティは操作されず（行なし・RowState 据え置き・After 未発火）、他の行は保存される</summary>
    [Fact(
        DisplayName = "[SaveHook] 1: Before false は単独スキップ（据え置き・After 未発火・他行は保存）"
    )]
    public async Task Before_False_SkipsSingleEntity_OthersSaved()
    {
        await ResetAndSeedAsync();

        var log = new List<string>();
        var hook = new RecordingHook<DocumentEntity>("h", log, e => e.DocumentId)
        {
            // 文書 10 の挿入だけをスキップする
            BeforePredicate = (e, _) => e.DocumentId != 10,
        };
        var documents = Documents(hook);

        var skipped = NewDocument(10, "skip-me", null, [1]);
        skipped.MarkAdded();
        var saved = NewDocument(11, "save-me", null, [2]);
        saved.MarkAdded();

        await documents.SaveAsync([skipped, saved], cancellationToken: Ct);

        // スキップした行は保存されず、RowState は Added のまま据え置き。After は呼ばれない
        (await DocumentExistsAsync(10))
            .Should()
            .BeFalse();
        skipped.RowState.Should().Be(RowState.Added, "スキップされた行は状態が据え置かれる");
        log.Should().Contain("h:before:Insert:10").And.NotContain("h:after:Insert:10");

        // 他の行は通常どおり保存され、状態も確定する
        (await DocumentExistsAsync(11))
            .Should()
            .BeTrue();
        saved.RowState.Should().Be(RowState.Unchanged);
        log.Should().Contain("h:before:Insert:11").And.Contain("h:after:Insert:11");
    }

    // ── 4（row 版）. After 例外時の残留（実装先で期待が分岐する唯一の点） ──

    /// <summary>After が例外を投げると、保存フェーズで行った更新は QuickER/EF Core では残らず、インメモリでは残る</summary>
    [Fact(
        DisplayName = "[SaveHook] After 例外時の残留は実装先で分岐する（QuickER/EF Core=残らない・InMemory=残る）"
    )]
    public async Task After_Throws_RowResidueDependsOnBackend()
    {
        await ResetAndSeedAsync();

        var hook = new RecordingHook<DocumentEntity>("h", [], e => e.DocumentId)
        {
            AfterAction = (_, _, _) => throw new InvalidOperationException("after-boom"),
        };
        var documents = Documents(hook);

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Title = "alpha-doomed";
        doc.MarkUpdated();

        var act = () => documents.SaveAsync(doc, cancellationToken: Ct);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*after-boom*");

        var reread = await Documents().GetByIdAsync(1, Ct);

        if (AfterExceptionLeavesResidue)
        {
            reread!
                .Title.Should()
                .Be("alpha-doomed", "インメモリは保存フェーズの変更を（ロールバックできず）残す");
        }
        else
        {
            reread!
                .Title.Should()
                .Be("alpha", "1 トランザクションのため After 例外で更新はロールバックされる");
        }
    }

    // ── 5. 複数フックの短絡と順序 ──

    /// <summary>5. Before は登録順で最初の false で短絡（残りは呼ばれない）、全 true のとき After も登録順に呼ばれる</summary>
    [Fact(DisplayName = "[SaveHook] 5: 複数フックは登録順・最初の false で短絡・After も登録順")]
    public async Task MultipleHooks_ShortCircuitAndOrder()
    {
        await ResetAndSeedAsync();

        // 短絡: s2 が Insert を止める → s3 の Before は呼ばれない・After は誰も呼ばれない
        var shortLog = new List<string>();
        var s1 = new RecordingHook<DocumentEntity>("s1", shortLog, e => e.DocumentId);
        var s2 = new RecordingHook<DocumentEntity>("s2", shortLog, e => e.DocumentId)
        {
            BeforePredicate = (_, _) => false,
        };
        var s3 = new RecordingHook<DocumentEntity>("s3", shortLog, e => e.DocumentId);
        var shortDocs = Documents(s1, s2, s3);

        var blocked = NewDocument(20, "blocked", null, [1]);
        blocked.MarkAdded();
        await shortDocs.SaveAsync(blocked, cancellationToken: Ct);

        shortLog.Should().Equal("s1:before:Insert:20", "s2:before:Insert:20");
        (await DocumentExistsAsync(20)).Should().BeFalse();

        // 順序: 全 true → Before を登録順、After を登録順に呼ぶ
        var orderLog = new List<string>();
        var o1 = new RecordingHook<DocumentEntity>("o1", orderLog, e => e.DocumentId);
        var o2 = new RecordingHook<DocumentEntity>("o2", orderLog, e => e.DocumentId);
        var o3 = new RecordingHook<DocumentEntity>("o3", orderLog, e => e.DocumentId);
        var orderDocs = Documents(o1, o2, o3);

        var passed = NewDocument(21, "passed", null, [1]);
        passed.MarkAdded();
        await orderDocs.SaveAsync(passed, cancellationToken: Ct);

        orderLog
            .Should()
            .Equal(
                "o1:before:Insert:21",
                "o2:before:Insert:21",
                "o3:before:Insert:21",
                "o1:after:Insert:21",
                "o2:after:Insert:21",
                "o3:after:Insert:21"
            );
    }

    // ── 6. insertWhenUpdateMissing の Before=Update・After=Insert ──

    /// <summary>6. 更新対象が無く INSERT へ切り替わると、Before は Update で 1 回・After は実操作 Insert で発火する</summary>
    [Fact(DisplayName = "[SaveHook] 6: insertWhenUpdateMissing は Before=Update・After=Insert")]
    public async Task InsertWhenUpdateMissing_BeforeUpdate_AfterInsert()
    {
        await ResetAndSeedAsync();

        var log = new List<string>();
        var hook = new RecordingHook<DocumentEntity>("h", log, e => e.DocumentId);
        var documents = Documents(hook);

        // 子を持たない文書 2 を取得 → 削除 → Updated として保存（更新対象なし → INSERT へ切替）
        var doc = await documents.GetByIdAsync(2, Ct);
        (await Documents().DeleteAsync(2, Ct)).Should().BeTrue();
        doc!.MarkUpdated();

        await documents.SaveAsync(doc, insertWhenUpdateMissing: true, cancellationToken: Ct);

        // Before は Update で 1 回、After は実際に行った Insert で発火
        log.Should().Equal("h:before:Update:2", "h:after:Insert:2");
        (await DocumentExistsAsync(2)).Should().BeTrue();
    }

    // ── 7. サブツリー削除の per-node 発火と D2 帰結 ──

    /// <summary>7. サブツリー削除は子ごとに Before/After(Delete) を発火し、root だけ Before=false なら「子は消え root は残る」</summary>
    [Fact(
        DisplayName = "[SaveHook] 7: サブツリー削除は per-node 発火・root だけ false で子は消え root は残る"
    )]
    public async Task SubtreeDelete_PerNodeFiring_RootSkippedChildrenDeleted()
    {
        await ResetAndSeedAsync();

        var log = new List<string>();
        // 親（DocumentEntity）の Delete だけをスキップし、子（DocumentNoteEntity）は削除する
        var docHook = new RecordingHook<DocumentEntity>("doc", log, e => e.DocumentId)
        {
            BeforePredicate = (_, op) => op != SaveOperation.Delete,
        };
        var noteHook = new RecordingHook<DocumentNoteEntity>("note", log, e => e.NoteId);
        var documents = Documents(docHook, noteHook);

        // 文書 1（子 100/101 を持つ）を Include して取得し、削除としてグラフ保存する
        var doc = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .Include(d => d.DocumentNotes)
            .FirstOrDefaultAsync(Ct);
        doc!.MarkRemoved();

        await documents.SaveAsync(doc, cancellationToken: Ct);

        // 子ごとに Before/After(Delete) が発火し、root は Before(Delete) が false でスキップ（After なし）。
        // per-node の発火順は実装先で異なる（QuickER/InMemory は子先行・EF Core は追跡順）ため Contain で検証する
        log.Should()
            .Contain("note:before:Delete:100")
            .And.Contain("note:after:Delete:100")
            .And.Contain("note:before:Delete:101")
            .And.Contain("note:after:Delete:101")
            .And.Contain("doc:before:Delete:1")
            .And.NotContain("doc:after:Delete:1");

        // 子は消え、root（文書 1）は残る（D2 の帰結）
        (await NoteCountAsync(1))
            .Should()
            .Be(0, "子は削除された");
        (await DocumentExistsAsync(1)).Should().BeTrue("root はスキップされ残る");
    }

    // ── 8. 直接操作・BulkInsert の素通り ──

    /// <summary>8. Insert/Update/Delete の直接呼び出しと BulkInsert はフックを発火しない（Save 経路のみが契約）</summary>
    [Fact(DisplayName = "[SaveHook] 8: 直接 Insert/Update/Delete・BulkInsert はフックを素通りする")]
    public async Task DirectOperations_AndBulkInsert_DoNotFireHooks()
    {
        await ResetAndSeedAsync();

        var log = new List<string>();
        var hook = new RecordingHook<DocumentEntity>("h", log, e => e.DocumentId);
        var documents = Documents(hook);

        await documents.InsertAsync(NewDocument(40, "direct-insert", null, [1]), Ct);
        var updated = await documents.GetByIdAsync(40, Ct);
        updated!.Title = "direct-update";
        await documents.UpdateAsync(updated, Ct);
        await documents.DeleteAsync(40, Ct);
        await documents.BulkInsertAsync([NewDocument(41, "bulk", null, [1])], Ct);

        log.Should().BeEmpty("直接操作・BulkInsert は Save フックを発火しない");
    }

    // ── 9. 未登録 no-op ──

    /// <summary>9. フックを 1 つも登録しなければ SaveAsync は従来どおり動く（レジストリは既定登録されるが完全 no-op）</summary>
    [Fact(DisplayName = "[SaveHook] 9: フック未登録なら SaveAsync は従来どおり（no-op）")]
    public async Task NoHooks_SaveWorksAsBefore()
    {
        await ResetAndSeedAsync();

        var documents = Documents();

        var doc = NewDocument(60, "no-hooks", null, [1]);
        doc.MarkAdded();
        (await documents.SaveAsync(doc, cancellationToken: Ct)).Should().BeGreaterThan(0);

        doc.RowState.Should().Be(RowState.Unchanged);
        (await DocumentExistsAsync(60)).Should().BeTrue();
    }

    // ── 10. SaveAsync(IEnumerable) 形態 ──

    /// <summary>10. SaveAsync(IEnumerable) でも各エンティティで Before/After が発火する（各エンティティ内で Before→After の順は保たれる）</summary>
    /// <remarks>
    /// After の発火位置は実装先で異なる（QuickER 版 Repository＝各 DML 直後の interleave・EF Core/InMemory＝バッチ後）ため、
    /// エンティティを跨いだ厳密な順序ではなく「各エンティティで Before が自身の After より前」という不変条件で検証する。
    /// </remarks>
    [Fact(
        DisplayName = "[SaveHook] 10: SaveAsync(IEnumerable) でも各エンティティでフックが発火する"
    )]
    public async Task SaveAsyncEnumerable_FiresHooksPerEntity()
    {
        await ResetAndSeedAsync();

        var log = new List<string>();
        var hook = new RecordingHook<DocumentEntity>("h", log, e => e.DocumentId);
        var documents = Documents(hook);

        var a = NewDocument(70, "a", null, [1]);
        a.MarkAdded();
        var b = NewDocument(71, "b", null, [2]);
        b.MarkAdded();

        await documents.SaveAsync([a, b], cancellationToken: Ct);

        log.Should()
            .HaveCount(4)
            .And.Contain([
                "h:before:Insert:70",
                "h:after:Insert:70",
                "h:before:Insert:71",
                "h:after:Insert:71",
            ]);

        // 各エンティティで Before が自身の After より前に発火する（実装先に依らない不変条件）
        log.IndexOf("h:before:Insert:70").Should().BeLessThan(log.IndexOf("h:after:Insert:70"));
        log.IndexOf("h:before:Insert:71").Should().BeLessThan(log.IndexOf("h:after:Insert:71"));

        (await DocumentExistsAsync(70)).Should().BeTrue();
        (await DocumentExistsAsync(71)).Should().BeTrue();
    }

    /// <summary>
    /// 呼び出しを共有ログへ記録するテスト用フック。<see cref="BeforePredicate"/> で Before の返り値（スキップ）を、
    /// <see cref="AfterAction"/> で After の副作用（context 経由の書き込み・例外）を差し込める。
    /// </summary>
    protected sealed class RecordingHook<TEntity>(
        string name,
        List<string> log,
        Func<TEntity, object> keySelector
    ) : ISaveHook<TEntity>
        where TEntity : EntityBase
    {
        /// <summary>Before の返り値を決める述語（null＝常に true＝スキップしない）</summary>
        public Func<TEntity, SaveOperation, bool>? BeforePredicate { get; init; }

        /// <summary>After の副作用（null＝何もしない）</summary>
        public Func<TEntity, SaveOperation, ISaveHookContext, Task>? AfterAction { get; init; }

        public Task<bool> BeforeSaveAsync(
            TEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            log.Add($"{name}:before:{operation}:{keySelector(entity)}");
            return Task.FromResult(BeforePredicate?.Invoke(entity, operation) ?? true);
        }

        public async Task AfterSaveAsync(
            TEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            log.Add($"{name}:after:{operation}:{keySelector(entity)}");

            if (AfterAction is not null)
            {
                await AfterAction(entity, operation, context);
            }
        }
    }
}
