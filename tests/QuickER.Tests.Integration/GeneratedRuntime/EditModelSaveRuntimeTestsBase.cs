using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedConcurrencyFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EditModel で編集して保存し、マッパーの <c>AcceptSaved</c> で保存の結果を受け入れる流れを、実装先ごとに確かめる
/// テストの共通基底（ConcurrencyFixture＝rowversion 列・EditModel・Mapper・Save フック）。
/// </summary>
/// <remarks>
/// <para>
/// 保存できた行は、版・DB が作った値・Save フックが書いた値を EditModel へ読み戻し、次の保存が正しい版で更新できる。
/// Save フックが飛ばした行は状態を残し、次の保存で試し直す。足してから削除の印を付けた子は保存に入らない。
/// </para>
/// <para>
/// 派生は、種を入れる処理と、<see cref="Hooks"/> を Save フックとして登録したリポジトリを用意するだけ。
/// 種は gadget 1「alpha」（メモ 10「first」・11「second」）と gadget 2「beta」（メモなし）。
/// </para>
/// </remarks>
public abstract class EditModelSaveRuntimeTestsBase
{
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly GadgetMapper Mapper = new();

    /// <summary>派生が Save フックとして登録するフック（テストごとに振る舞いを決める）</summary>
    protected GadgetHooks Hooks { get; } = new();

    /// <summary>gadget のリポジトリ（リモート面＝どの実装先も持つ）</summary>
    protected abstract IGadgetRemoteRepository Gadgets { get; }

    /// <summary>メモのリポジトリ（リモート面）</summary>
    protected abstract IGadgetNoteRemoteRepository Notes { get; }

    /// <summary>保存先を空にして、種を入れる（<see cref="SeedAsync"/> を呼ぶ）</summary>
    protected abstract Task ResetAsync();

    /// <summary>gadget をメモつきで取得する</summary>
    protected abstract Task<GadgetEntity?> GetWithNotesAsync(int id);

    /// <summary>Before フックがエンティティに書いた値が、保存の後に呼び出し側のエンティティへ届くか（リモートは届かない＝区切り 40 の担当）</summary>
    protected virtual bool BeforeHookWritesReachEntity => true;

    /// <summary>種を入れる（派生の <see cref="ResetAsync"/> が保存先を空にした後に呼ぶ）</summary>
    protected async Task SeedAsync()
    {
        await Gadgets.InsertAsync(NewGadget(1, "alpha"), Ct);
        await Gadgets.InsertAsync(NewGadget(2, "beta"), Ct);
        await Notes.InsertAsync(NewNote(10, 1, "first"), Ct);
        await Notes.InsertAsync(NewNote(11, 1, "second"), Ct);
    }

    private static GadgetEntity NewGadget(int id, string name) =>
        new() { GadgetId = GadgetIdValue.Create(id), Name = NameValue.Create(name) };

    private static GadgetNoteEntity NewNote(int noteId, int gadgetId, string note) =>
        new()
        {
            NoteId = NoteIdValue.Create(noteId),
            GadgetId = GadgetIdValue.Create(gadgetId),
            Note = NoteValue.Create(note),
        };

    private static GadgetEditModel NewInput(int id, string name)
    {
        var editModel = Mapper.CreateEditModel();
        editModel.BindingGadgetId = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        editModel.BindingName = name;

        return editModel;
    }

    private async Task<string?> ReadNameAsync(int id) =>
        (await Gadgets.GetByIdAsync(GadgetIdValue.Create(id), Ct))?.Name.Value;

    private async Task<bool> NoteExistsAsync(int noteId) =>
        await Notes.GetByIdAsync(NoteIdValue.Create(noteId), Ct) is not null;

    [Fact(
        DisplayName = "EditModel の保存: 既存の行を AcceptSaved して続けて保存しても、2 回目が偽の競合にならない"
    )]
    public async Task ExistingRow_SecondSave_Succeeds()
    {
        await ResetAsync();
        var entity = (await Gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!;
        var editModel = Mapper.CreateEditModel(entity);

        editModel.BindingName = "first edit";
        Mapper.ApplyToEntity(editModel, entity, includeRemoved: true);
        await Gadgets.SaveAsync(entity, cancellationToken: Ct);
        Mapper.AcceptSaved(editModel, entity);

        editModel.RowState.Should().Be(RowState.Unchanged);
        editModel
            .RowVer!.Value.Should()
            .Equal(entity.RowVer!.Value, "保存で進んだ版が EditModel へ戻る");

        editModel.BindingName = "second edit";
        Mapper.ApplyToEntity(editModel, entity, includeRemoved: true);
        await Gadgets.SaveAsync(entity, cancellationToken: Ct);

        (await ReadNameAsync(1)).Should().Be("second edit");
    }

    [Fact(
        DisplayName = "EditModel の保存: 新しい行を AcceptSaved すると、作り直したエンティティで更新として保存できる"
    )]
    public async Task NewRow_SecondSave_UpdatesWithTheSavedVersion()
    {
        await ResetAsync();
        var editModel = NewInput(5, "new");

        var entity = Mapper.CreateEntity(editModel, includeRemoved: true);
        await Gadgets.SaveAsync(entity, cancellationToken: Ct);
        Mapper.AcceptSaved(editModel, entity);

        editModel.RowState.Should().Be(RowState.Unchanged);
        editModel.RowVer.Should().NotBeNull("保存で付いた版が EditModel へ戻る");

        editModel.BindingName = "renamed";
        await Gadgets.SaveAsync(
            Mapper.CreateEntity(editModel, includeRemoved: true),
            cancellationToken: Ct
        );

        (await ReadNameAsync(5)).Should().Be("renamed");
    }

    [Fact(
        DisplayName = "EditModel の保存: Save フックが飛ばした挿入は AcceptSaved の後も新しい行のまま残り、次の保存で入る"
    )]
    public async Task InsertSkippedByHook_StaysAddedAndIsRetried()
    {
        await ResetAsync();
        var editModel = NewInput(6, "skipped");
        Hooks.SkipInsertOfGadgets.Add(6);

        var entity = Mapper.CreateEntity(editModel, includeRemoved: true);
        await Gadgets.SaveAsync(entity, cancellationToken: Ct);
        Mapper.AcceptSaved(editModel, entity);

        editModel.RowState.Should().Be(RowState.Added, "フックが飛ばした行は保存されていない");
        (await ReadNameAsync(6)).Should().BeNull();

        Hooks.SkipInsertOfGadgets.Clear();
        await Gadgets.SaveAsync(
            Mapper.CreateEntity(editModel, includeRemoved: true),
            cancellationToken: Ct
        );

        (await ReadNameAsync(6)).Should().Be("skipped");
    }

    [Fact(
        DisplayName = "EditModel の保存: Save フックが飛ばした子の削除は、Remove でも MarkRemoved でも削除待ちのまま残る"
    )]
    public async Task ChildDeletionSkippedByHook_StaysPending()
    {
        await ResetAsync();
        var entity = (await GetWithNotesAsync(1))!;
        var editModel = Mapper.CreateEditModel(entity);
        var removed = editModel.GadgetNotes.Single(note => note.NoteId!.Value == 10);
        var marked = editModel.GadgetNotes.Single(note => note.NoteId!.Value == 11);
        editModel.GadgetNotes.Remove(removed);
        marked.MarkRemoved();
        Hooks.SkipDeleteOfNotes.UnionWith([10, 11]);

        Mapper.ApplyToEntity(editModel, entity, includeRemoved: true);
        await Gadgets.SaveAsync(entity, cancellationToken: Ct);
        Mapper.AcceptSaved(editModel, entity);

        editModel.GadgetNotes.RemovedItems.Should().Equal(removed);
        editModel.GadgetNotes.Should().Contain(marked);
        marked.IsRemoved.Should().BeTrue();
        (await NoteExistsAsync(10)).Should().BeTrue();
        (await NoteExistsAsync(11)).Should().BeTrue();

        // フックが通すようになれば、残っていた削除がそのまま保存される
        Hooks.SkipDeleteOfNotes.Clear();
        Mapper.ApplyToEntity(editModel, entity, includeRemoved: true);
        await Gadgets.SaveAsync(entity, cancellationToken: Ct);
        Mapper.AcceptSaved(editModel, entity);

        (await NoteExistsAsync(10)).Should().BeFalse();
        (await NoteExistsAsync(11)).Should().BeFalse();
        editModel.GadgetNotes.Should().BeEmpty();
        editModel.GadgetNotes.RemovedItems.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "EditModel の保存: 足してから削除の印を付けた子は保存に入らず、AcceptSaved でコレクションから外れる"
    )]
    public async Task ChildAddedThenMarkedRemoved_IsLeftOutOfTheSave()
    {
        await ResetAsync();
        var entity = (await GetWithNotesAsync(1))!;
        var editModel = Mapper.CreateEditModel(entity);
        var added = new GadgetNoteMapper().CreateEditModel();
        editModel.GadgetNotes.Add(added);
        added.MarkRemoved();

        editModel.Validate().Should().BeTrue("削除の印の付いた行は検証の対象外");
        Mapper.ApplyToEntity(editModel, entity, includeRemoved: true);
        entity.GadgetNotes.Should().HaveCount(2, "足して消した子はエンティティのグラフに入らない");

        await Gadgets.SaveAsync(entity, cancellationToken: Ct);
        Mapper.AcceptSaved(editModel, entity);

        editModel.GadgetNotes.Should().NotContain(added);
        editModel.GadgetNotes.Should().HaveCount(2);
        (await NoteExistsAsync(10)).Should().BeTrue();
        (await NoteExistsAsync(11)).Should().BeTrue();
    }

    [Fact(
        DisplayName = "EditModel の保存: Before フックが書いた値は、AcceptSaved でエンティティから EditModel へ戻る"
    )]
    public async Task ValueWrittenByBeforeHook_ReachesTheEditModel()
    {
        await ResetAsync();
        var editModel = NewInput(8, "hooked");
        Hooks.InsertNameSuffix = "*";

        var entity = Mapper.CreateEntity(editModel, includeRemoved: true);
        await Gadgets.SaveAsync(entity, cancellationToken: Ct);
        Mapper.AcceptSaved(editModel, entity);

        editModel.Name!.Value.Should().Be(entity.Name.Value);
        editModel
            .BindingName.Should()
            .Be(entity.Name.Value, "入力の文字列も読み戻した値から作り直す");

        if (BeforeHookWritesReachEntity)
        {
            editModel.Name.Value.Should().Be("hooked*");
        }
    }

    [Fact(
        DisplayName = "EditModel の保存: コレクションの AcceptSaved は、保存した行の版を戻し、削除した行の追跡を外す"
    )]
    public async Task Collection_AcceptSaved_ReadsBackVersionsAndReleasesDeletions()
    {
        await ResetAsync();
        var first = (await Gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!;
        var second = (await Gadgets.GetByIdAsync(GadgetIdValue.Create(2), Ct))!;
        var editModels = Mapper.CreateEditModels([first, second]);
        editModels[0].BindingName = "edited";
        editModels.Remove(editModels[1]);

        var entities = Mapper.CreateEntities(editModels, includeRemoved: true);
        await Gadgets.SaveAsync(entities, cancellationToken: Ct);
        Mapper.AcceptSaved(editModels, entities);

        editModels.Should().ContainSingle().Which.RowState.Should().Be(RowState.Unchanged);
        editModels.RemovedItems.Should().BeEmpty("削除は保存された");
        (await ReadNameAsync(2)).Should().BeNull();

        editModels[0].BindingName = "again";
        await Gadgets.SaveAsync(
            Mapper.CreateEntities(editModels, includeRemoved: true),
            cancellationToken: Ct
        );

        (await ReadNameAsync(1)).Should().Be("again");
    }

    /// <summary>テストごとに振る舞いを決める Save フック（gadget の挿入を飛ばす・名前に接尾辞を足す・メモの削除を飛ばす）</summary>
    protected sealed class GadgetHooks : ISaveHook<GadgetEntity>, ISaveHook<GadgetNoteEntity>
    {
        /// <summary>挿入を飛ばす gadget のキー</summary>
        public HashSet<int> SkipInsertOfGadgets { get; } = [];

        /// <summary>削除を飛ばすメモのキー</summary>
        public HashSet<int> SkipDeleteOfNotes { get; } = [];

        /// <summary>挿入する gadget の名前へ足す接尾辞（null は足さない）</summary>
        public string? InsertNameSuffix { get; set; }

        public Task<bool> BeforeSaveAsync(
            GadgetEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            if (operation != SaveOperation.Insert)
            {
                return Task.FromResult(true);
            }

            if (SkipInsertOfGadgets.Contains(entity.GadgetId.Value))
            {
                return Task.FromResult(false);
            }

            if (InsertNameSuffix is { } suffix)
            {
                entity.Name = NameValue.Create(entity.Name.Value + suffix);
            }

            return Task.FromResult(true);
        }

        public Task<bool> BeforeSaveAsync(
            GadgetNoteEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                operation != SaveOperation.Delete
                    || !SkipDeleteOfNotes.Contains(entity.NoteId.Value)
            );
    }
}
