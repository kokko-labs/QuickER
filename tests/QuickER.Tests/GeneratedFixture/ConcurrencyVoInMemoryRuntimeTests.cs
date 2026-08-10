using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedConcurrencyFixture;

/// <summary>
/// rowversion 列 <b>× 値オブジェクト</b>の楽観排他を、インメモリ Repository で検証する（Docker 不要＝CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// VO 有効時の rowversion プロパティは素の <c>byte[]</c> ではなく VO 型（<c>RowVerValue</c>）になる。
/// 版番号の書き戻しはリフレクション（<c>PropertyInfo.SetValue</c>）で行われるため、生の <c>byte[]</c> を
/// そのまま渡すと <c>ArgumentException</c>（「Byte[] は RowVerValue へ変換できない」）で保存が落ちていた。
/// 本テストは Insert / Update / Save の 3 経路すべてで版が VO 型として書き戻ることを固定する。
/// </para>
/// <para>
/// あわせて「DB 採番列は EditModel の入力必須にしない」も検証する。従来は rowversion 列が必須扱いのため
/// EditModel 経由の新規行保存が <c>"RowVer has no input value."</c> で必ず失敗していた。
/// </para>
/// <para>
/// 「他者による更新」は、同じストアから別途取得したもう 1 つのインスタンス経由で更新して作る
/// （ストアは取得のたびに複製を返すため、手元のインスタンスの版は古いまま残る）。
/// </para>
/// </remarks>
public sealed class ConcurrencyVoInMemoryRuntimeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>ストアと Repository を作り、gadget 1 件（メモ 1 件つき）を投入する</summary>
    private static async Task<(
        IGadgetRepository Gadgets,
        IGadgetNoteRepository Notes
    )> SeededAsync()
    {
        var store = new InMemoryDataStore();
        var gadgets = new InMemoryGadgetRepository(store);
        var notes = new InMemoryGadgetNoteRepository(store);

        await gadgets.InsertAsync(
            new GadgetEntity
            {
                GadgetId = GadgetIdValue.Create(1),
                Name = NameValue.Create("alpha"),
            },
            Ct
        );
        await notes.InsertAsync(
            new GadgetNoteEntity
            {
                NoteId = NoteIdValue.Create(100),
                GadgetId = GadgetIdValue.Create(1),
                Note = NoteValue.Create("first"),
            },
            Ct
        );

        return (gadgets, notes);
    }

    // ── 版の書き戻し（本バグの回帰本体） ──

    /// <summary>1. Insert / Update / Save の 3 経路とも、版が VO 型のまま同一インスタンスへ書き戻る</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/InMemory] 版反映: Insert / Update / Save が VO 型の版を書き戻す"
    )]
    public async Task RowVersion_IsWrittenBack_AsValueObject()
    {
        var store = new InMemoryDataStore();
        var gadgets = new InMemoryGadgetRepository(store);

        var entity = new GadgetEntity
        {
            GadgetId = GadgetIdValue.Create(1),
            Name = NameValue.Create("inserted"),
        };

        // 旧実装はここで ArgumentException（Byte[] を RowVerValue へ SetValue できない）になっていた
        await gadgets.InsertAsync(entity, Ct);

        entity.RowVer.Should().NotBeNull("INSERT 直後に採番された版が VO として入る");
        entity.RowVer.Value.Length.Should().Be(8, "擬似版も rowversion と同じ 8 バイト");
        var afterInsert = entity.RowVer;

        entity.Name = NameValue.Create("updated");
        (await gadgets.UpdateAsync(entity, cancellationToken: Ct)).Should().BeTrue();
        entity.RowVer.Should().NotBe(afterInsert, "UPDATE のたびに版が進む");
        var afterUpdate = entity.RowVer;

        entity.Name = NameValue.Create("saved");
        entity.MarkUpdated();
        (await gadgets.SaveAsync(entity, cancellationToken: Ct)).Should().Be(1);
        entity.RowVer.Should().NotBe(afterUpdate, "グラフ保存でも新版が反映される");

        // 反映された版はそのまま次の更新に使える（再取得不要＝書き戻しが正しく効いている証明）
        entity.Name = NameValue.Create("again");
        (await gadgets.UpdateAsync(entity, cancellationToken: Ct))
            .Should()
            .BeTrue("書き戻された版はストアの現在値と一致する");
    }

    /// <summary>2. グラフ保存では子（カスケード）の版も VO 型で書き戻る</summary>
    [Fact(DisplayName = "[Concurrency/VO/InMemory] 版反映: グラフ保存は子の版も書き戻す")]
    public async Task SaveAsync_WritesBackRowVersion_ForCascadedChildren()
    {
        var store = new InMemoryDataStore();
        var gadgets = new InMemoryGadgetRepository(store);

        var gadget = new GadgetEntity
        {
            GadgetId = GadgetIdValue.Create(1),
            Name = NameValue.Create("alpha"),
        };
        gadget.MarkAdded();

        var note = new GadgetNoteEntity
        {
            NoteId = NoteIdValue.Create(100),
            GadgetId = GadgetIdValue.Create(1),
            Note = NoteValue.Create("first"),
        };
        note.MarkAdded();
        gadget.GadgetNotes.Add(note);

        (await gadgets.SaveAsync(gadget, cancellationToken: Ct))
            .Should()
            .Be(2, "親 1 件＋子 1 件が保存される");

        gadget.RowVer.Should().NotBeNull("親の版が書き戻る");
        note.RowVer.Should().NotBeNull("子の版も書き戻る");

        // 2 回目: 再取得せずそのまま保存できる＝親子とも版が現在値と一致している
        gadget.Name = NameValue.Create("beta");
        gadget.MarkUpdated();
        note.Note = NoteValue.Create("second");
        note.MarkUpdated();

        (await gadgets.SaveAsync(gadget, cancellationToken: Ct))
            .Should()
            .Be(2, "書き戻された版で版チェックが通る");
    }

    // ── 競合 ──

    /// <summary>3. VO の版でも「他者が先に更新した行」は SaveConflictException（ストアは先勝ち）</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/InMemory] UpdateAsync: 版が古いと SaveConflictException・ストアは先勝ち"
    )]
    public async Task UpdateAsync_Throws_WhenRowVersionIsStale()
    {
        var (gadgets, _) = await SeededAsync();

        var first = await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct);
        var second = await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct);
        first!.RowVer.Should().NotBeNull("取得時点で版が VO として読める");
        second!.RowVer.Should().Be(first.RowVer, "同時点の取得は同じ版を持つ");

        first.Name = NameValue.Create("by-first");
        (await gadgets.UpdateAsync(first, cancellationToken: Ct)).Should().BeTrue();

        second.Name = NameValue.Create("by-second");
        var act = async () => await gadgets.UpdateAsync(second, cancellationToken: Ct);

        await act.Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage("*modified by another user*", "VO の版でも比較が効き競合として弾かれる");

        (await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!
            .Name.Value.Should()
            .Be("by-first", "競合した更新は適用されない（先勝ち）");
    }

    /// <summary>4. ForceOverwrite は版条件を外して上書きし、新しい版を VO で書き戻す</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/InMemory] UpdateAsync: ForceOverwrite は版を無視して上書きし新版を反映する"
    )]
    public async Task UpdateAsync_ForceOverwrite_OverwritesAndRefreshesRowVersion()
    {
        var (gadgets, _) = await SeededAsync();

        var first = await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct);
        var second = await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct);
        var staleVersion = second!.RowVer;

        first!.Name = NameValue.Create("by-first");
        await gadgets.UpdateAsync(first, cancellationToken: Ct);

        second.Name = NameValue.Create("forced");
        (await gadgets.UpdateAsync(second, ConcurrencyMode.ForceOverwrite, Ct))
            .Should()
            .BeTrue("版条件を外すので更新は成立する");

        (await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!
            .Name.Value.Should()
            .Be("forced", "last-write-wins で上書きされる");
        second.RowVer.Should().NotBe(staleVersion, "上書き後の新しい版が反映される");
    }

    // ── EditModel / Mapper ──

    /// <summary>
    /// 5. DB 採番の rowversion 列は EditModel の入力必須にならず、未入力のまま新規行を保存できる。
    /// </summary>
    /// <remarks>
    /// 従来は必須検証で <c>BindingRowVer</c> にエラーが立ち、Mapper の <c>ApplyToEntity</c> が
    /// <c>"RowVer has no input value."</c> を投げるため、EditModel 経由の新規保存が構造的に不可能だった。
    /// </remarks>
    [Fact(
        DisplayName = "[Concurrency/VO/InMemory] EditModel: rowversion は未入力でも検証を通り新規保存できる"
    )]
    public async Task EditModel_NewRow_SavesWithoutRowVersionInput()
    {
        var store = new InMemoryDataStore();
        var gadgets = new InMemoryGadgetRepository(store);

        var editModel = new GadgetMapper().CreateEditModel();
        editModel.BindingGadgetId = "7";
        editModel.BindingName = "from-edit-model";

        editModel.Validate().Should().BeTrue("DB 採番の rowversion は必須項目に数えない");
        editModel.BindingRowVer.Should().BeEmpty("新規行では版の入力が無いのが正常");

        var entity = new GadgetMapper().CreateEntity(editModel);

        entity.RowVer.Should().BeNull("未入力の版は代入されずエンティティの現在値（null）のまま");
        (await gadgets.SaveAsync(entity, cancellationToken: Ct))
            .Should()
            .Be(1, "版の入力が無くても新規行は保存できる");
        entity.RowVer.Should().NotBeNull("保存で採番された版が書き戻る");

        var stored = await gadgets.GetByIdAsync(GadgetIdValue.Create(7), Ct);
        stored!.Name.Value.Should().Be("from-edit-model");
    }

    /// <summary>6. 既存行を EditModel で往復しても、読み込んだ版がそのまま維持され続けて更新できる</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/InMemory] EditModel: 既存行は読み込んだ版を保ったまま更新できる"
    )]
    public async Task EditModel_ExistingRow_KeepsLoadedRowVersion()
    {
        var (gadgets, _) = await SeededAsync();

        var loaded = await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct);
        var mapper = new GadgetMapper();
        var editModel = mapper.CreateEditModel(loaded!);

        editModel.RowVer.Should().Be(loaded!.RowVer, "確定値として版が載る");

        editModel.BindingName = "renamed";
        editModel.Validate().Should().BeTrue();

        var entity = mapper.CreateEntity(editModel);
        entity.RowVer.Should().Be(loaded.RowVer, "入力された版はそのまま反映される");

        (await gadgets.SaveAsync(entity, cancellationToken: Ct))
            .Should()
            .Be(1, "読み込んだ版で版チェックが通る");
        (await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!
            .Name.Value.Should()
            .Be("renamed");
    }
}
