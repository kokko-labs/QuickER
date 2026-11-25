using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedBinaryVoFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 「値オブジェクト × 無制限バイナリ列の除外 × EditModel / Mapper」の往復の共通基底（QuickER 版 SQLite とインメモリが同じ表明で流す）。
/// </summary>
/// <remarks>
/// <para>
/// 除外列は通常フェッチで読まれず、VO 非 NULL の実体プロパティは初期化子が <c>= null!</c> のため
/// 未取得状態＝null で届く。EditModel の必須検証と Mapper の <c>?? throw</c> がそのままだと
/// 「取得 → 通常列だけ編集 → 保存」という素通しの往復が成立しない（＝除外機能と EditModel が併用できない）。
/// ここはその往復と、保存先の blob が温存されることを確かめる。
/// </para>
/// <para>
/// 入力は <see cref="BinaryVoFixtureDefinition"/>（1 テーブル <c>vault_items</c>・非 NULL の除外列
/// <c>seal</c> と NULL 許容の除外列 <c>note_blob</c>）。派生はリポジトリの作り方・保存先の初期化・保存先の blob の長さの読み方だけを
/// 差し込む。
/// </para>
/// </remarks>
public abstract class BinaryColumnEditModelRuntimeTestsBase
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>非 NULL の除外列 seal に入れる blob</summary>
    private static readonly byte[] Seal = [1, 2, 3, 4, 5];

    /// <summary>NULL 許容の除外列 note_blob に入れる blob</summary>
    private static readonly byte[] NoteBlob = [9, 8, 7];

    /// <summary>実装先の vault_items のリポジトリ</summary>
    protected abstract IVaultItemRepository Repository();

    /// <summary>保存先を空にする（シードは各テストが EditModel 経由で行う）</summary>
    protected abstract Task ResetAsync();

    /// <summary>保存先の指定列（<c>seal</c> / <c>note_blob</c>）のバイト長（NULL は -1）</summary>
    protected abstract Task<long> StoredLengthAsync(string column, int itemId);

    /// <summary>新規入力の EditModel を組み立てる（除外列は Base64 のバインディング文字列で入力する）</summary>
    protected static VaultItemEditModel NewInput(
        int itemId,
        string label,
        byte[]? seal,
        byte[]? noteBlob
    )
    {
        var model = new VaultItemMapper().CreateEditModel();
        model.BindingItemId = itemId.ToString();
        model.BindingLabel = label;

        if (seal is not null)
        {
            model.BindingSeal = Convert.ToBase64String(seal);
        }

        if (noteBlob is not null)
        {
            model.BindingNoteBlob = Convert.ToBase64String(noteBlob);
        }

        return model;
    }

    /// <summary>
    /// EditModel で blob 込みの新規行を作り（INSERT は全列）、通常フェッチ（除外列は未取得）した実体を
    /// Mapper で EditModel へ戻し、通常列だけ編集して保存できる。保存先の blob は温存される。
    /// </summary>
    [Fact(
        DisplayName = "[BinaryVo] EditModel 往復: 未取得の除外列を素通しして保存でき保存先の blob が温存される"
    )]
    public async Task EditModelRoundTrip_KeepsUnfetchedBlob()
    {
        await ResetAsync();
        var items = Repository();
        var mapper = new VaultItemMapper();

        // 1. EditModel から新規作成（INSERT は全列＝blob が保存先に書かれる）
        var input = NewInput(1, "alpha", Seal, NoteBlob);
        input.Validate().Should().BeTrue("除外列も含めて入力済みなので検証は通る");
        await items.InsertAsync(mapper.CreateEntity(input, includeRemoved: true), Ct);

        (await StoredLengthAsync("seal", 1)).Should().Be(Seal.Length);
        (await StoredLengthAsync("note_blob", 1)).Should().Be(NoteBlob.Length);

        // 2. 通常フェッチ（除外列は読まれない＝VO 非 NULL は null のまま届く）
        var fetched = await items.GetByIdAsync(ItemIdValue.Create(1), Ct);
        fetched.Should().NotBeNull();
        fetched!.Seal.Should().BeNull("非 NULL の除外列は未取得状態（初期化子 = null!）で届く");
        fetched.NoteBlob.Should().BeNull("NULL 許容の除外列も未取得状態で届く");

        // 3. Mapper で EditModel 化 → 通常列だけ編集 → 検証
        var editModel = mapper.CreateEditModel(fetched);
        editModel.Seal.Should().BeNull("未取得の除外列は EditModel でも未入力のまま");
        editModel.BindingLabel = "alpha-2";
        editModel
            .Validate()
            .Should()
            .BeTrue("除外列は必須検証の対象外（未取得のまま保存できなければ往復が成立しない）");

        // 4. ApplyToEntity（保存用に includeRemoved: true）→ UpdateAsync
        mapper.ApplyToEntity(editModel, fetched, includeRemoved: true);
        fetched
            .Seal.Should()
            .BeNull("未入力の除外列は実体の未取得状態のまま＝更新の対象から外れる");
        (await items.UpdateAsync(fetched, cancellationToken: Ct)).Should().BeTrue();

        // 5. 通常列は更新され、保存先の blob は温存されている
        (await items.GetByIdAsync(ItemIdValue.Create(1), Ct))!
            .Label.Value.Should()
            .Be("alpha-2");
        (await StoredLengthAsync("seal", 1))
            .Should()
            .Be(Seal.Length, "更新は除外列に触れないため保存先の blob はそのまま残る");
        (await StoredLengthAsync("note_blob", 1)).Should().Be(NoteBlob.Length);
    }

    /// <summary>blob つきの新しい行を AcceptSaved すると、エンティティを使い回しても作り直しても次の保存が blob を送らず温存する</summary>
    [Fact(
        DisplayName = "[BinaryVo] EditModel 往復: blob つきの新しい行を AcceptSaved すると、エンティティを使い回しても作り直しても次の保存が blob を送らず温存する"
    )]
    public async Task AcceptSaved_NewRowWithBlobs_NextSaveKeepsStoredBlobs()
    {
        await ResetAsync();
        var items = Repository();
        var mapper = new VaultItemMapper();

        var input = NewInput(3, "gamma", Seal, NoteBlob);
        var entity = mapper.CreateEntity(input, includeRemoved: true);
        await items.SaveAsync(entity, cancellationToken: Ct);
        mapper.AcceptSaved(input, entity);

        input.RowState.Should().Be(RowState.Unchanged);
        input
            .Seal.Should()
            .BeNull("保存の後の除外列は、取得したときと同じ未取得の状態として読み戻す");
        input.NoteBlob.Should().BeNull();
        entity
            .Seal.Should()
            .BeNull("エンティティも未取得の状態へ戻す（使い回すと blob を送り直さない）");

        // 使い回したエンティティで通常列を更新しても、更新のガードに当たらず blob を温存する
        input.BindingLabel = "gamma-2";
        mapper.ApplyToEntity(input, entity, includeRemoved: true);
        await items.SaveAsync(entity, cancellationToken: Ct);
        mapper.AcceptSaved(input, entity);

        // 作り直したエンティティでも同じ
        input.BindingLabel = "gamma-3";
        await items.SaveAsync(
            mapper.CreateEntity(input, includeRemoved: true),
            cancellationToken: Ct
        );

        (await items.GetByIdAsync(ItemIdValue.Create(3), Ct))!.Label.Value.Should().Be("gamma-3");
        (await StoredLengthAsync("seal", 3)).Should().Be(Seal.Length);
        (await StoredLengthAsync("note_blob", 3)).Should().Be(NoteBlob.Length);
    }

    /// <summary>NULL 許容の除外列が値を持たない行（note_blob=NULL）でも同じ往復が成立する</summary>
    [Fact(DisplayName = "[BinaryVo] EditModel 往復: NULL 許容の除外列（値なし）でも成立する")]
    public async Task EditModelRoundTrip_WithNullableExcludedColumn()
    {
        await ResetAsync();
        var items = Repository();
        var mapper = new VaultItemMapper();

        // note_blob は未入力（NULL）のまま新規作成する
        var input = NewInput(2, "beta", Seal, noteBlob: null);
        input.Validate().Should().BeTrue();
        await items.InsertAsync(mapper.CreateEntity(input, includeRemoved: true), Ct);

        (await StoredLengthAsync("note_blob", 2)).Should().Be(-1, "未入力なので NULL で挿入される");

        var fetched = await items.GetByIdAsync(ItemIdValue.Create(2), Ct);
        var editModel = mapper.CreateEditModel(fetched!);
        editModel.BindingLabel = "beta-2";
        editModel.Validate().Should().BeTrue();

        mapper.ApplyToEntity(editModel, fetched!, includeRemoved: true);
        (await items.UpdateAsync(fetched!, cancellationToken: Ct)).Should().BeTrue();

        (await items.GetByIdAsync(ItemIdValue.Create(2), Ct))!.Label.Value.Should().Be("beta-2");
        (await StoredLengthAsync("seal", 2))
            .Should()
            .Be(Seal.Length, "非 NULL の blob は温存される");
        (await StoredLengthAsync("note_blob", 2)).Should().Be(-1, "NULL のままで変わらない");
    }

    /// <summary>
    /// <c>WithUnboundedBinary</c> で除外列を含めて取得した実体を Mapper 経由で保存すると、従来どおり
    /// 除外列の更新のガードで例外になる（設計維持の固定）。
    /// </summary>
    [Fact(
        DisplayName = "[BinaryVo] WithUnboundedBinary 取得の Mapper 経由保存は従来どおり更新のガードで例外"
    )]
    public async Task WithUnboundedBinary_ThenMapperSave_StillThrows()
    {
        await ResetAsync();
        var items = Repository();
        var mapper = new VaultItemMapper();

        var input = NewInput(3, "gamma", Seal, NoteBlob);
        await items.InsertAsync(mapper.CreateEntity(input, includeRemoved: true), Ct);

        // 除外列を含めて取得すると blob が実体に載る（＝未取得状態ではない）
        var fetched = await items
            .Query()
            .Where(item => item.ItemId == ItemIdValue.Create(3))
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);
        fetched.Should().NotBeNull();
        fetched!.Seal.Should().NotBeNull("WithUnboundedBinary では除外列の実データが取れる");

        var editModel = mapper.CreateEditModel(fetched);
        editModel.BindingLabel = "gamma-2";
        editModel.Validate().Should().BeTrue();

        // Mapper は「入力があるときだけ代入」なので blob はそのまま残り、更新ガードが働く
        mapper.ApplyToEntity(editModel, fetched, includeRemoved: true);
        var act = async () => await items.UpdateAsync(fetched, cancellationToken: Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("ExecuteSqlAsync");
    }

    /// <summary>
    /// 空の値（VO は <c>Create([])</c>）を入れれば新規行の検証を通り、正当な 2 段方式
    /// （INSERT → <c>Write{列}Async</c>）が成立する。
    /// </summary>
    [Fact(
        DisplayName = "[BinaryVo] 新規行に空の値を入れれば検証を通り INSERT → Write の 2 段が成立する"
    )]
    public async Task NewRow_WithEmptyValue_PassesValidationAndTwoStepInsertWorks()
    {
        await ResetAsync();
        var items = Repository();
        var mapper = new VaultItemMapper();

        // 空 VO は非 null なので必須検証を素通りする（2 段方式の入口）
        var input = NewInput(11, "epsilon", seal: null, noteBlob: null);
        input.Seal = SealValue.Create([]);
        input.Validate().Should().BeTrue("空の値は非 null なので新規行の必須検証を通る");

        await items.InsertAsync(mapper.CreateEntity(input, includeRemoved: true), Ct);
        (await StoredLengthAsync("seal", 11)).Should().Be(0, "空配列で INSERT される");

        // 2 段目: ストリームアクセサで blob を流し込む
        using (var source = new MemoryStream(Seal, writable: false))
        {
            (await items.WriteSealAsync(ItemIdValue.Create(11), source, cancellationToken: Ct))
                .Should()
                .BeTrue();
        }

        (await StoredLengthAsync("seal", 11)).Should().Be(Seal.Length);

        using var destination = new MemoryStream();
        (await items.ReadSealAsync(ItemIdValue.Create(11), destination, Ct)).Should().BeTrue();
        destination.ToArray().Should().Equal(Seal, "書き込んだ blob がそのまま読み戻せる");
    }
}
