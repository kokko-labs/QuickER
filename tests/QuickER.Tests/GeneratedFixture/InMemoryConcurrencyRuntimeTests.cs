using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace QuickER.Tests.GeneratedBinaryFixture;

/// <summary>
/// rowversion 列を持つテーブルの楽観排他（<c>ConcurrencyMode</c>）を、<b>インメモリ Repository</b>で検証する
/// （実 DB を使わないため Docker 不要＝CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// インメモリは DB を持たないため、ストアが単調増加する 8 バイトの擬似版番号を採番して SQL Server の rowversion を
/// 模す。挙動は SQL Server 方言のQuickER 版 Repository（<c>SqlServerConcurrencyRuntimeTests</c>）と同じ規則で揃える:
/// </para>
/// <list type="bullet">
///   <item>INSERT / UPDATE のたびに版が進み、保存したエンティティ自身へ新しい版が載る</item>
///   <item>UPDATE / DELETE は版一致を条件に成立し、他者が先に更新していれば <c>SaveConflictException</c></item>
///   <item><c>ConcurrencyMode.ForceOverwrite</c> は版条件を外す（明示的な last-write-wins）</item>
///   <item>単一 <c>UpdateAsync</c> の「行なし」は競合ではなく従来契約の <c>false</c></item>
///   <item><c>insertWhenUpdateMissing</c> は「行なし→INSERT／版が古い→競合」を区別する</item>
/// </list>
/// <para>
/// 「他者による更新」は、同じストアから別途取得したもう 1 つのインスタンス経由で更新して作る
/// （ストアは取得のたびに複製を返すため、手元のインスタンスの版は古いまま残る）。
/// </para>
/// <para>
/// 除外列（payload / thumb）は値を持ったままだと UPDATE が拒否される既存仕様のため、本テストは一貫して
/// 未取得状態（null / 空配列）のまま扱う。
/// </para>
/// </remarks>
public sealed class InMemoryConcurrencyRuntimeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>データストアと文書リポジトリを生成し、文書 1 件（メモ 1 件つき）を投入する</summary>
    private static async Task<(
        InMemoryDataStore Store,
        IDocumentRepository Documents,
        IDocumentNoteRepository Notes
    )> SeededAsync()
    {
        var store = new InMemoryDataStore();
        var documents = new InMemoryDocumentRepository(store);
        var notes = new InMemoryDocumentNoteRepository(store);

        await documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Thumb = [],
            },
            Ct
        );
        await notes.InsertAsync(
            new DocumentNoteEntity
            {
                NoteId = 100,
                DocumentId = 1,
                Note = "first",
            },
            Ct
        );

        return (store, documents, notes);
    }

    // ── 版の採番 ──

    /// <summary>1. INSERT で版が載り、UPDATE のたびに版が進む（いずれも同一インスタンスへ反映される）</summary>
    [Fact(DisplayName = "[Concurrency/InMemory] 版反映: Insert で版が載り Update のたびに版が進む")]
    public async Task RowVersion_IsAssignedOnInsert_AndAdvancesOnEveryUpdate()
    {
        var store = new InMemoryDataStore();
        var documents = new InMemoryDocumentRepository(store);

        var entity = new DocumentEntity
        {
            DocumentId = 1,
            Title = "inserted",
            Thumb = [],
        };
        await documents.InsertAsync(entity, Ct);

        entity.RowVer.Should().NotBeNull("INSERT 直後に採番された版が入る");
        entity.RowVer!.Length.Should().Be(8, "SQL Server の rowversion と同じ 8 バイト");
        var afterInsert = entity.RowVer;

        entity.Title = "updated";
        (await documents.UpdateAsync(entity, cancellationToken: Ct)).Should().BeTrue();
        entity.RowVer.Should().NotEqual(afterInsert, "UPDATE のたびに版が進む");
        var afterUpdate = entity.RowVer;

        entity.Title = "saved";
        entity.MarkUpdated();
        (await documents.SaveAsync(entity, cancellationToken: Ct)).Should().Be(1);
        entity.RowVer.Should().NotEqual(afterUpdate, "グラフ保存でも新版が反映される");

        // 反映された版はそのまま次の更新に使える（再取得不要）
        entity.Title = "again";
        (await documents.UpdateAsync(entity, cancellationToken: Ct))
            .Should()
            .BeTrue("反映された版はストアの現在値と一致する");
    }

    /// <summary>rowversion 列を持たない型（document_notes）は版チェックの対象外＝取得せずに更新できる</summary>
    [Fact(DisplayName = "[Concurrency/InMemory] rowversion 列のない型は版チェックの対象外になる")]
    public async Task TypeWithoutRowVersion_IsNotGuarded()
    {
        var (_, _, notes) = await SeededAsync();

        // 取得せずに組み立てたインスタンス（版を持ちようがない）でも更新は通る
        var detached = new DocumentNoteEntity
        {
            NoteId = 100,
            DocumentId = 1,
            Note = "rewritten",
        };

        (await notes.UpdateAsync(detached, cancellationToken: Ct)).Should().BeTrue();
        (await notes.GetByIdAsync(100, Ct))!.Note.Should().Be("rewritten");
    }

    // ── 単一 UpdateAsync ──

    /// <summary>2. 他者が先に更新した行を古いインスタンスで更新すると SaveConflictException（ストアは先勝ち）</summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] UpdateAsync: 版が古いと SaveConflictException・ストアは先勝ち"
    )]
    public async Task UpdateAsync_Throws_WhenRowVersionIsStale()
    {
        var (_, documents, _) = await SeededAsync();

        var first = await documents.GetByIdAsync(1, Ct);
        var second = await documents.GetByIdAsync(1, Ct);
        first!.RowVer.Should().NotBeNull("取得時点で版が読める");
        second!.RowVer.Should().Equal(first.RowVer, "同時点の取得は同じ版を持つ");

        first.Title = "by-first";
        (await documents.UpdateAsync(first, cancellationToken: Ct)).Should().BeTrue();

        second.Title = "by-second";
        var act = async () => await documents.UpdateAsync(second, cancellationToken: Ct);

        await act.Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage("*modified by another user*", "版が一致せず競合として弾かれる");

        (await documents.GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("by-first", "競合した更新は適用されない（先勝ち）");
    }

    /// <summary>3. ForceOverwrite は版条件を外して上書きし、新しい版を反映する</summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] UpdateAsync: ForceOverwrite は版を無視して上書きし新版を反映する"
    )]
    public async Task UpdateAsync_ForceOverwrite_OverwritesAndRefreshesRowVersion()
    {
        var (_, documents, _) = await SeededAsync();

        var first = await documents.GetByIdAsync(1, Ct);
        var second = await documents.GetByIdAsync(1, Ct);
        var staleVersion = second!.RowVer;

        first!.Title = "by-first";
        await documents.UpdateAsync(first, cancellationToken: Ct);

        second.Title = "forced";
        (await documents.UpdateAsync(second, ConcurrencyMode.ForceOverwrite, Ct))
            .Should()
            .BeTrue("版条件を外すので更新は成立する");

        (await documents.GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("forced", "last-write-wins で上書きされる");
        second.RowVer.Should().NotEqual(staleVersion, "上書き後の新しい版が反映される");
    }

    /// <summary>4. 存在しない行の更新は従来どおり false（競合ではない）</summary>
    [Fact(DisplayName = "[Concurrency/InMemory] UpdateAsync: 行が存在しなければ従来どおり false")]
    public async Task UpdateAsync_ReturnsFalse_WhenRowIsMissing()
    {
        var (_, documents, _) = await SeededAsync();

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

    // ── グラフ保存 ──

    /// <summary>5-1. グラフ保存の更新も版条件で守られる</summary>
    [Fact(DisplayName = "[Concurrency/InMemory] SaveAsync: 更新は版が古いと SaveConflictException")]
    public async Task SaveAsync_Update_Throws_WhenRowVersionIsStale()
    {
        var (_, documents, _) = await SeededAsync();

        var first = await documents.GetByIdAsync(1, Ct);
        var second = await documents.GetByIdAsync(1, Ct);

        first!.Title = "by-first";
        first.MarkUpdated();
        (await documents.SaveAsync(first, cancellationToken: Ct)).Should().Be(1);

        second!.Title = "by-second";
        second.MarkUpdated();
        var act = async () => await documents.SaveAsync(second, cancellationToken: Ct);

        await act.Should().ThrowAsync<SaveConflictException>().WithMessage("*modified*");
        (await documents.GetByIdAsync(1, Ct))!.Title.Should().Be("by-first");
    }

    /// <summary>5-2. グラフ保存の削除も版条件で守られ、ForceOverwrite なら通る</summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: 削除も版条件で守られ ForceOverwrite なら通る"
    )]
    public async Task SaveAsync_Delete_IsGuardedByRowVersion()
    {
        var (_, documents, _) = await SeededAsync();

        var first = await documents.GetByIdAsync(1, Ct);
        var second = await documents.GetByIdAsync(1, Ct);

        first!.Title = "by-first";
        first.MarkUpdated();
        await documents.SaveAsync(first, cancellationToken: Ct);

        second!.MarkRemoved();
        var act = async () => await documents.SaveAsync(second, cancellationToken: Ct);

        await act.Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage("*delete was modified by another user*", "版の古い行は削除できない");
        (await documents.GetByIdAsync(1, Ct)).Should().NotBeNull("削除は行われない");

        (
            await documents.SaveAsync(
                second,
                mode: ConcurrencyMode.ForceOverwrite,
                cancellationToken: Ct
            )
        )
            .Should()
            .BeGreaterThan(0);
        (await documents.GetByIdAsync(1, Ct)).Should().BeNull("版条件を外せば削除される");
    }

    /// <summary>5-3. ForceOverwrite のグラフ更新は版を無視して成立する</summary>
    [Fact(DisplayName = "[Concurrency/InMemory] SaveAsync: ForceOverwrite の更新は版を無視する")]
    public async Task SaveAsync_ForceOverwrite_Update_Succeeds()
    {
        var (_, documents, _) = await SeededAsync();

        var first = await documents.GetByIdAsync(1, Ct);
        var second = await documents.GetByIdAsync(1, Ct);

        first!.Title = "by-first";
        first.MarkUpdated();
        await documents.SaveAsync(first, cancellationToken: Ct);

        second!.Title = "forced";
        second.MarkUpdated();
        (
            await documents.SaveAsync(
                second,
                mode: ConcurrencyMode.ForceOverwrite,
                cancellationToken: Ct
            )
        )
            .Should()
            .Be(1);

        (await documents.GetByIdAsync(1, Ct))!.Title.Should().Be("forced");
    }

    /// <summary>6. insertWhenUpdateMissing は「行なし→INSERT」「版が古い→競合」を区別する</summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: insertWhenUpdateMissing は行なしと版違いを区別する"
    )]
    public async Task SaveAsync_InsertWhenUpdateMissing_DistinguishesMissingRowFromStaleVersion()
    {
        var (_, documents, _) = await SeededAsync();

        // 行なし: INSERT へ切り替わる
        var missing = new DocumentEntity
        {
            DocumentId = 3,
            Title = "switched-to-insert",
            Thumb = [],
        };
        missing.MarkUpdated();

        (await documents.SaveAsync(missing, insertWhenUpdateMissing: true, cancellationToken: Ct))
            .Should()
            .Be(1, "行が無いので INSERT へ切り替わる");
        (await documents.GetByIdAsync(3, Ct)).Should().NotBeNull();
        missing.RowVer.Should().NotBeNull("切り替わった INSERT でも版が採番される");

        // 版が古い: INSERT へ倒さず競合として弾く（倒すと主キー重複になる）
        var first = await documents.GetByIdAsync(1, Ct);
        var second = await documents.GetByIdAsync(1, Ct);
        first!.Title = "by-first";
        first.MarkUpdated();
        await documents.SaveAsync(first, cancellationToken: Ct);

        second!.Title = "by-second";
        second.MarkUpdated();
        var act = async () =>
            await documents.SaveAsync(second, insertWhenUpdateMissing: true, cancellationToken: Ct);

        await act.Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage(
                "*modified by another user*",
                "版違いは INSERT へ倒さず競合として報告する（主キー重複に化けない）"
            );
    }

    // ── 未定義の ConcurrencyMode ──

    /// <summary>
    /// 7. 列挙に無い値は入口で ArgumentOutOfRangeException（内部の 2 値分岐は「Optimistic なら版ガード・
    /// さもなくば強制」のため、検証しないと未定義値が黙って強制上書き側へ落ちて版チェックが無効化される）。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] 未定義の ConcurrencyMode は ArgumentOutOfRangeException"
    )]
    public async Task UndefinedConcurrencyMode_IsRejected()
    {
        var (_, documents, _) = await SeededAsync();

        var first = await documents.GetByIdAsync(1, Ct);
        var stale = await documents.GetByIdAsync(1, Ct);

        first!.Title = "by-first";
        await documents.UpdateAsync(first, cancellationToken: Ct);

        // 版は古いまま＝未定義値が強制上書きへ落ちれば「更新が通ってしまう」
        stale!.Title = "by-undefined";
        var update = async () => await documents.UpdateAsync(stale, (ConcurrencyMode)99, Ct);

        await update.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("mode");

        stale.MarkUpdated();
        var save = async () =>
            await documents.SaveAsync(stale, mode: (ConcurrencyMode)99, cancellationToken: Ct);

        await save.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("mode");

        (await documents.GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("by-first", "未定義値の保存は 1 件も適用されない");
    }

    // ── copy-on-write ステージングによる保存単位の all-or-nothing ──

    /// <summary>
    /// 8. グラフ保存の途中で競合が起きると、それより前に適用済みの書き込み（親の更新・子の追加）も巻き戻る。
    /// ストアだけでなく<b>呼び出し元エンティティの版</b>も保存前のまま残るため、競合の原因を直してそのまま再保存できる。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: 途中競合はストアも呼び出し元の版も保存前へ戻す"
    )]
    public async Task SaveAsync_MidGraphConflict_RollsBackEarlierWrites()
    {
        var (_, documents, notes) = await SeededAsync();

        // 子（document_notes）にも版を持たせるため、rowversion を持つ親を 2 つ使って
        // 「1 つ目は成功・2 つ目で競合」というグラフを作る
        var stale = await documents.GetByIdAsync(1, Ct);
        var current = await documents.GetByIdAsync(1, Ct);

        // 他者が先に更新して stale の版を古くする
        current!.Title = "by-another-user";
        (await documents.UpdateAsync(current, cancellationToken: Ct)).Should().BeTrue();

        // 版が新しい親（文書 2）と、版が古い親（文書 1）を 1 回の保存単位にまとめる
        var fresh = new DocumentEntity
        {
            DocumentId = 2,
            Title = "inserted-in-same-unit",
            Thumb = [],
        };
        fresh.MarkAdded();

        var staleVersion = stale!.RowVer;
        stale.Title = "by-me";
        stale.MarkUpdated();

        var act = async () => await documents.SaveAsync([fresh, stale], cancellationToken: Ct);
        await act.Should().ThrowAsync<SaveConflictException>();

        // 競合より前に適用された INSERT も巻き戻る（部分適用が残らない）
        (await documents.GetByIdAsync(2, Ct))
            .Should()
            .BeNull("競合 1 件で保存単位ごと巻き戻る");
        (await documents.GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("by-another-user", "競合した行は先勝ちのまま");

        // 呼び出し元エンティティの版も保存前のまま＝幻の版が残らない
        stale.RowVer.Should().Equal(staleVersion, "失敗した保存は新しい版を配らない");
        fresh.RowVer.Should().BeNull("巻き戻された INSERT の版も配られない");

        // 競合の原因（古い版）を解消すれば、同じインスタンスでそのまま再保存できる
        stale.RowVer = (await documents.GetByIdAsync(1, Ct))!.RowVer;
        stale.MarkUpdated();
        fresh.MarkAdded();
        (await documents.SaveAsync([fresh, stale], cancellationToken: Ct)).Should().Be(2);
        (await documents.GetByIdAsync(1, Ct))!.Title.Should().Be("by-me");
        (await documents.GetByIdAsync(2, Ct)).Should().NotBeNull();
        (await notes.GetByIdAsync(100, Ct)).Should().NotBeNull("無関係な行は影響を受けない");
    }

    /// <summary>
    /// 9. After フックが例外を投げると保存フェーズごと巻き戻り、ストアも呼び出し元エンティティの版も保存前へ戻る
    /// （幻の版が残ると、同じインスタンスの再保存が偽の競合になる）。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: After 例外でストアも版も巻き戻り再保存できる"
    )]
    public async Task SaveAsync_AfterThrows_RollsBackStoreAndRowVersion()
    {
        var store = new InMemoryDataStore();
        var hook = new RowVersionCapturingHook();
        var documents = new InMemoryDocumentRepository(store, new SaveHookRegistry().Add(hook));

        await new InMemoryDocumentRepository(store).InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Thumb = [],
            },
            Ct
        );

        var document = await documents.GetByIdAsync(1, Ct);
        var beforeSave = document!.RowVer;
        beforeSave.Should().NotBeNull("取得時点で版が読める");

        document.Title = "by-me";
        document.MarkUpdated();

        var act = async () => await documents.SaveAsync(document, cancellationToken: Ct);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*after-boom*");

        document.RowVer.Should().Equal(beforeSave, "巻き戻された保存は新しい版を配らない");
        (await new InMemoryDocumentRepository(store).GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("alpha", "行の更新も巻き戻る");

        // 幻の版が残っていれば、同一インスタンスのこの再保存は偽の競合になる
        hook.ThrowOnAfter = false;
        (await documents.SaveAsync(document, cancellationToken: Ct)).Should().Be(1);

        document.RowVer.Should().NotEqual(beforeSave, "成功後は新しい版が反映される");
        (await new InMemoryDocumentRepository(store).GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("by-me");
    }

    /// <summary>10. After フックが見る版は「保存前の版」（新しい版は全フェーズ成功後に配られる＝コミット前の見え方）</summary>
    [Fact(DisplayName = "[Concurrency/InMemory] SaveAsync: After はコミット前の旧版を見る")]
    public async Task SaveAsync_AfterHook_SeesPreSaveRowVersion()
    {
        var store = new InMemoryDataStore();
        var hook = new RowVersionCapturingHook { ThrowOnAfter = false };
        var documents = new InMemoryDocumentRepository(store, new SaveHookRegistry().Add(hook));

        await new InMemoryDocumentRepository(store).InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Thumb = [],
            },
            Ct
        );

        var document = await documents.GetByIdAsync(1, Ct);
        var beforeSave = document!.RowVer;

        document.Title = "by-me";
        document.MarkUpdated();
        (await documents.SaveAsync(document, cancellationToken: Ct)).Should().Be(1);

        hook.SeenRowVersion.Should()
            .Equal(beforeSave, "After はコミット前に走るので保存前の版が見える");
        document.RowVer.Should().NotEqual(beforeSave, "保存が完了した時点で新しい版が反映される");
    }

    // ── copy-on-write の公開（After がロック外で走っている間の並行更新） ──

    /// <summary>
    /// 11. After フックの待機中に別インスタンスが同じ行を正常更新し、その後 After が例外を投げても、
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
        var store = new InMemoryDataStore();
        var hook = new GatedAfterHook<DocumentEntity> { ThrowOnAfter = true };
        var documents = new InMemoryDocumentRepository(store, new SaveHookRegistry().Add(hook));
        var other = new InMemoryDocumentRepository(store);

        await other.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Thumb = [],
            },
            Ct
        );

        var mine = await documents.GetByIdAsync(1, Ct);
        var beforeSave = mine!.RowVer;
        mine.Title = "by-me";
        mine.MarkUpdated();

        // After に入った時点では保存はまだ 1 バイトもストアへ書いていない
        var saving = documents.SaveAsync(mine, cancellationToken: Ct);
        await hook.Entered.Task;

        // 別インスタンスの正常な更新（ストアの現在値は保存前のままなので成立する）
        var theirs = await other.GetByIdAsync(1, Ct);
        theirs!.Title = "by-another-user";
        (await other.UpdateAsync(theirs, cancellationToken: Ct))
            .Should()
            .BeTrue("公開前なので他者から見た行は保存前のまま＝更新が通る");

        hook.Release.TrySetResult();
        var act = async () => await saving;
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*after-boom*");

        var stored = await other.GetByIdAsync(1, Ct);
        stored!.Title.Should().Be("by-another-user", "失敗した保存は他者の更新を消さない");
        stored.RowVer.Should().Equal(theirs.RowVer, "他者が受け取った版もそのまま有効なまま残る");
        mine.RowVer.Should().Equal(beforeSave, "失敗した保存は新しい版を配らない");
    }

    /// <summary>
    /// 12. After フックの待機中に別インスタンスが同じ行を更新すると、After が正常終了しても公開時の検証で
    /// <c>SaveConflictException</c> になる（並行更新は無傷のまま残る）。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: After 待機中の並行更新は公開時に競合として弾かれる"
    )]
    public async Task SaveAsync_ConcurrentUpdateDuringAfter_IsRejectedAtPublish()
    {
        var store = new InMemoryDataStore();
        var hook = new GatedAfterHook<DocumentEntity>();
        var documents = new InMemoryDocumentRepository(store, new SaveHookRegistry().Add(hook));
        var other = new InMemoryDocumentRepository(store);

        await other.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Thumb = [],
            },
            Ct
        );

        var mine = await documents.GetByIdAsync(1, Ct);
        var beforeSave = mine!.RowVer;
        mine.Title = "by-me";
        mine.MarkUpdated();

        var saving = documents.SaveAsync(mine, cancellationToken: Ct);
        await hook.Entered.Task;

        var theirs = await other.GetByIdAsync(1, Ct);
        theirs!.Title = "by-another-user";
        (await other.UpdateAsync(theirs, cancellationToken: Ct)).Should().BeTrue();

        hook.Release.TrySetResult();
        var act = async () => await saving;

        await act.Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage(
                "*modified by another user*",
                "保存フェーズを通過した後でも、公開時に他者の書き込みを検出する"
            );

        (await other.GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("by-another-user", "競合した保存は並行更新を上書きしない");
        mine.RowVer.Should().Equal(beforeSave, "公開されなかった保存は新しい版を配らない");
    }

    /// <summary>
    /// 13. rowversion 列を持たない型は公開時検証の対象外＝After 待機中に並行更新があっても競合にならず、
    /// 保存が後勝ち（last-write-wins）で公開される。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: rowversion なしの型は公開時検証の対象外（後勝ち）"
    )]
    public async Task SaveAsync_TypeWithoutRowVersion_IsNotVerifiedAtPublish()
    {
        var store = new InMemoryDataStore();
        var hook = new GatedAfterHook<DocumentNoteEntity>();
        var notes = new InMemoryDocumentNoteRepository(
            store,
            new SaveHookRegistry().Add<DocumentNoteEntity>(hook)
        );
        var other = new InMemoryDocumentNoteRepository(store);

        await new InMemoryDocumentRepository(store).InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Thumb = [],
            },
            Ct
        );
        await other.InsertAsync(
            new DocumentNoteEntity
            {
                NoteId = 100,
                DocumentId = 1,
                Note = "first",
            },
            Ct
        );

        var mine = await notes.GetByIdAsync(100, Ct);
        mine!.Note = "by-me";
        mine.MarkUpdated();

        var saving = notes.SaveAsync(mine, cancellationToken: Ct);
        await hook.Entered.Task;

        var theirs = await other.GetByIdAsync(100, Ct);
        theirs!.Note = "by-another-user";
        (await other.UpdateAsync(theirs, cancellationToken: Ct)).Should().BeTrue();

        hook.Release.TrySetResult();
        (await saving).Should().Be(1, "版を持たない型は公開時検証を行わない＝競合にならない");

        (await other.GetByIdAsync(100, Ct))!
            .Note.Should()
            .Be("by-me", "版のない型の契約どおり後勝ちで公開される");
    }

    /// <summary>
    /// 14. After が同じ行へ blob を書くと版がもう一段進むが、呼び出し元エンティティには
    /// <b>公開された最終版</b>が反映される（保存フェーズ時点の版を配ると、次の保存が偽の競合になる）。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/InMemory] SaveAsync: After の blob 書き込み後の最終版が呼び出し元へ反映される"
    )]
    public async Task SaveAsync_AfterWritesBinaryColumn_HandsBackPublishedRowVersion()
    {
        var store = new InMemoryDataStore();
        var payload = new byte[128];
        new Random(3).NextBytes(payload);

        var documents = new InMemoryDocumentRepository(
            store,
            new SaveHookRegistry().Add(new BinaryWritingHook(payload))
        );
        var plain = new InMemoryDocumentRepository(store);

        await plain.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Thumb = [],
            },
            Ct
        );

        var document = await documents.GetByIdAsync(1, Ct);
        document!.Title = "by-me";
        document.MarkUpdated();
        (await documents.SaveAsync(document, cancellationToken: Ct)).Should().Be(1);

        var stored = await plain
            .Query()
            .Where(d => d.DocumentId == 1)
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
