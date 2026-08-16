using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.Integration.GeneratedRuntime;
using Xunit;

namespace QuickER.Tests.GeneratedConcurrencyFixture;

/// <summary>
/// 楽観排他のランタイムスイートを <b>rowversion 列 × 値オブジェクト</b>の図で、<b>インメモリ Repository</b>から
/// 流す派生（Docker 不要＝CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// VO 有効時の rowversion プロパティは素の <c>byte[]</c> ではなく VO 型（<c>RowVerValue</c>）になる。
/// 版番号の書き戻しはリフレクション（<c>PropertyInfo.SetValue</c>）で行われるため、生の <c>byte[]</c> を
/// そのまま渡すと <c>ArgumentException</c>（「Byte[] は RowVerValue へ変換できない」）で保存が落ちていた。
/// バックエンド非依存のシナリオは基底 <see cref="ConcurrencyRuntimeTestsBase{TEntity, TConflictException}"/> が持ち、
/// 本クラスは VO 固有の観点（子の版・EditModel・バイナリ VO の並び替えと等値）を持つ。
/// </para>
/// <para>
/// 「他者による更新」は、同じストアを共有する別のリポジトリインスタンス経由で更新して作る
/// （ストアは取得のたびに複製を返すため、手元のインスタンスの版は古いまま残る）。
/// </para>
/// </remarks>
public sealed class ConcurrencyVoInMemoryRuntimeTests
    : ConcurrencyRuntimeTestsBase<GadgetEntity, SaveConflictException>
{
    /// <summary>基底のシナリオが共有するインメモリストア</summary>
    private readonly InMemoryDataStore _store = new();

    /// <summary>gadget リポジトリ（シード済みストアを共有する）</summary>
    private InMemoryGadgetRepository Gadgets => new(_store);

    /// <summary>メモリポジトリ（シード済みストアを共有する）</summary>
    private InMemoryGadgetNoteRepository Notes => new(_store);

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

        await Gadgets.InsertAsync(NewEntity(SeededRootId, "alpha"), Ct);
        await Gadgets.InsertAsync(NewEntity(SeededChildlessRootId, "beta"), Ct);
        await Notes.InsertAsync(NewNote(SeededChildId, SeededRootId, "first"), Ct);
    }

    /// <summary>gadget を組み立てる（キー・名前とも VO へ包む）</summary>
    protected override GadgetEntity NewEntity(int id, string title) =>
        new() { GadgetId = GadgetIdValue.Create(id), Name = NameValue.Create(title) };

    /// <summary>メモ（子）を組み立てる</summary>
    private static GadgetNoteEntity NewNote(int noteId, int gadgetId, string note) =>
        new()
        {
            NoteId = NoteIdValue.Create(noteId),
            GadgetId = GadgetIdValue.Create(gadgetId),
            Note = NoteValue.Create(note),
        };

    protected override Task InsertAsync(GadgetEntity entity) => Gadgets.InsertAsync(entity, Ct);

    protected override Task<GadgetEntity?> GetAsync(int id) =>
        Gadgets.GetByIdAsync(GadgetIdValue.Create(id), Ct);

    protected override Task<GadgetEntity?> GetWithChildrenAsync(int id)
    {
        var key = GadgetIdValue.Create(id);

        return Gadgets
            .Query()
            .Where(g => g.GadgetId == key)
            .Include(g => g.GadgetNotes)
            .FirstOrDefaultAsync(Ct);
    }

    protected override string GetTitle(GadgetEntity entity) => entity.Name.Value;

    protected override void SetTitle(GadgetEntity entity, string title) =>
        entity.Name = NameValue.Create(title);

    protected override byte[]? GetRowVersion(GadgetEntity entity) => entity.RowVer?.Value;

    protected override void SetRowVersion(GadgetEntity entity, byte[] rowVersion) =>
        entity.RowVer = RowVerValue.Create(rowVersion);

    protected override void MarkAdded(GadgetEntity entity) => entity.MarkAdded();

    protected override void MarkUpdated(GadgetEntity entity) => entity.MarkUpdated();

    protected override void MarkRemoved(GadgetEntity entity) => entity.MarkRemoved();

    protected override Task<bool> UpdateAsync(
        GadgetEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic
    ) => Gadgets.UpdateAsync(entity, Translate(mode), Ct);

    protected override Task<int> SaveAsync(
        GadgetEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic,
        bool insertWhenUpdateMissing = false
    ) =>
        Gadgets.SaveAsync(
            entity,
            insertWhenUpdateMissing: insertWhenUpdateMissing,
            mode: Translate(mode),
            cancellationToken: Ct
        );

    /// <summary>同じストアを共有する別インスタンス経由で更新する（手元のインスタンスの版は古いまま残る）</summary>
    protected override async Task BumpByAnotherUserAsync(int id, string title)
    {
        var other = Gadgets;
        var row = await other.GetByIdAsync(GadgetIdValue.Create(id), Ct);
        row.Should().NotBeNull();

        row!.Name = NameValue.Create(title);
        (await other.UpdateAsync(row, cancellationToken: Ct)).Should().BeTrue();
    }

    protected override void EditFirstChild(GadgetEntity root, string note)
    {
        var child = root.GadgetNotes.First();
        child.Note = NoteValue.Create(note);
        child.MarkUpdated();
    }

    protected override async Task<string?> ReadChildNoteAsync(int noteId) =>
        (await Notes.GetByIdAsync(NoteIdValue.Create(noteId), Ct))?.Note.Value;

    // ── VO 固有 1: 子の版も VO 型で書き戻る ──

    /// <summary>グラフ保存では子（カスケード）の版も VO 型で書き戻り、そのまま続けて保存できる</summary>
    [Fact(DisplayName = "[Concurrency/VO/InMemory] 版反映: グラフ保存は子の版も VO 型で書き戻す")]
    public async Task SaveAsync_WritesBackRowVersion_ForCascadedChildren()
    {
        await ResetAndSeedAsync();

        var gadget = NewEntity(8, "parent");
        gadget.MarkAdded();

        var note = NewNote(80, 8, "child");
        note.MarkAdded();
        gadget.GadgetNotes.Add(note);

        (await Gadgets.SaveAsync(gadget, cancellationToken: Ct))
            .Should()
            .Be(2, "親 1 件＋子 1 件が保存される");

        gadget.RowVer.Should().NotBeNull("親の版が書き戻る");
        gadget.RowVer.Value.Length.Should().Be(8, "擬似版も rowversion と同じ 8 バイト");
        note.RowVer.Should().NotBeNull("子の版も書き戻る");

        // 2 回目: 再取得せずそのまま保存できる＝親子とも版が現在値と一致している
        gadget.Name = NameValue.Create("parent-2");
        gadget.MarkUpdated();
        note.Note = NoteValue.Create("child-2");
        note.MarkUpdated();

        (await Gadgets.SaveAsync(gadget, cancellationToken: Ct))
            .Should()
            .Be(2, "書き戻された版で版チェックが通る");
    }

    // ── VO 固有 2: EditModel / Mapper ──

    /// <summary>DB 採番の rowversion 列は EditModel の入力必須にならず、未入力のまま新規行を保存できる</summary>
    /// <remarks>
    /// 従来は必須検証で <c>BindingRowVer</c> にエラーが立ち、Mapper の <c>ApplyToEntity</c> が
    /// <c>"RowVer has no input value."</c> を投げるため、EditModel 経由の新規保存が構造的に不可能だった。
    /// </remarks>
    [Fact(
        DisplayName = "[Concurrency/VO/InMemory] EditModel: rowversion は未入力でも検証を通り新規保存できる"
    )]
    public async Task EditModel_NewRow_SavesWithoutRowVersionInput()
    {
        await ResetAndSeedAsync();

        var editModel = new GadgetMapper().CreateEditModel();
        editModel.BindingGadgetId = "7";
        editModel.BindingName = "from-edit-model";

        editModel.Validate().Should().BeTrue("DB 採番の rowversion は必須項目に数えない");
        editModel.BindingRowVer.Should().BeEmpty("新規行では版の入力が無いのが正常");

        var entity = new GadgetMapper().CreateEntity(editModel);

        entity.RowVer.Should().BeNull("未入力の版は代入されずエンティティの現在値（null）のまま");
        (await Gadgets.SaveAsync(entity, cancellationToken: Ct))
            .Should()
            .Be(1, "版の入力が無くても新規行は保存できる");
        entity.RowVer.Should().NotBeNull("保存で採番された版が書き戻る");

        (await GetAsync(7)).Should().NotBeNull();
        GetTitle((await GetAsync(7))!).Should().Be("from-edit-model");
    }

    /// <summary>既存行を EditModel で往復しても、読み込んだ版がそのまま維持され続けて更新できる</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/InMemory] EditModel: 既存行は読み込んだ版を保ったまま更新できる"
    )]
    public async Task EditModel_ExistingRow_KeepsLoadedRowVersion()
    {
        await ResetAndSeedAsync();

        var loaded = await GetAsync(SeededRootId);
        var mapper = new GadgetMapper();
        var editModel = mapper.CreateEditModel(loaded!);

        editModel.RowVer.Should().Be(loaded!.RowVer, "確定値として版が載る");

        editModel.BindingName = "renamed";
        editModel.Validate().Should().BeTrue();

        var entity = mapper.CreateEntity(editModel);
        entity.RowVer.Should().Be(loaded.RowVer, "入力された版はそのまま反映される");

        (await Gadgets.SaveAsync(entity, cancellationToken: Ct))
            .Should()
            .Be(1, "読み込んだ版で版チェックが通る");
        (await ReadTitleAsync(SeededRootId)).Should().Be("renamed");
    }

    // ── VO 固有 3: バイナリ VO の並び替えと等値 ──

    /// <summary>バイナリ VO 列（版）の OrderBy が例外にならず、包んでいる byte[] の辞書式順に並ぶ</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/InMemory] OrderBy: バイナリ VO の版が辞書式バイト順で並ぶ"
    )]
    public async Task OrderBy_BinaryValueObject_SortsLexicographically()
    {
        // 版は単調増加＝採番順がそのまま昇順になる。ValueObjectBinaryBase は IComparable を実装しないため、
        // 既定の比較子では ArgumentException になっていた
        foreach (var id in new[] { 1, 2, 3 })
        {
            await Gadgets.InsertAsync(NewEntity(id, $"gadget{id}"), Ct);
        }

        var ascending = await Gadgets.Query().OrderBy(g => g.RowVer).ToListAsync(Ct);
        ascending.Select(g => g.GadgetId.Value).Should().Equal(1, 2, 3);

        var descending = await Gadgets.Query().OrderByDescending(g => g.RowVer).ToListAsync(Ct);
        descending.Select(g => g.GadgetId.Value).Should().Equal(3, 2, 1);
    }

    /// <summary>バイナリ VO の等値・ハッシュは配列を要素ごとに比較する（参照同一性ではない）</summary>
    /// <remarks>
    /// <see cref="ValueObjectBase{TSelf, TValue}"/> の等値は boxing 回避のため
    /// <c>EqualityComparer&lt;TValue&gt;.Default</c> 基準だが、<c>byte[]</c> ではそれが参照比較になってしまう。
    /// <see cref="ValueObjectBinaryBase{TSelf}"/> 側の override が構造比較を保つことを固定する。
    /// </remarks>
    [Fact(DisplayName = "[Concurrency/VO] バイナリ VO の等値・ハッシュは要素ごとの構造比較")]
    public void BinaryValueObject_UsesStructuralEquality()
    {
        var a = RowVerValue.Create([1, 2, 3]);
        var b = RowVerValue.Create([1, 2, 3]);
        var c = RowVerValue.Create([1, 2, 4]);

        // 内容が同じ「別インスタンスの配列」同士が等しい（参照比較なら false になる）
        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());

        a.Equals(c).Should().BeFalse();
        (a != c).Should().BeTrue();
    }
}
