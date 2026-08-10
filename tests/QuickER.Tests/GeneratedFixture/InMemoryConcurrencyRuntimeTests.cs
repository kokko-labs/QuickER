using System;
using System.Collections.Generic;
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
public sealed class InMemoryConcurrencyRuntimeTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>Save フック登録用に作った DI コンテナ（テスト終了時にまとめて破棄する）</summary>
    private readonly List<ServiceProvider> _providers = [];

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

    // ── undo ジャーナルによる保存単位の all-or-nothing ──

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
        var documents = new InMemoryDocumentRepository(store, SingleHookRegistry(hook));

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
        var documents = new InMemoryDocumentRepository(store, SingleHookRegistry(hook));

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

    /// <summary>Save フックを 1 つだけ登録したレジストリを組み立てる</summary>
    private ISaveHookRegistry SingleHookRegistry(ISaveHook<DocumentEntity> hook)
    {
        var provider = new ServiceCollection().AddSingleton(hook).BuildServiceProvider();
        _providers.Add(provider);
        return new ServiceProviderSaveHookRegistry(provider);
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

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }
}
