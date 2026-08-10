using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
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
}
