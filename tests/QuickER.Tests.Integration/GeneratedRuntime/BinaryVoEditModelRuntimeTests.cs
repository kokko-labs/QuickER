using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryVoFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 「値オブジェクト × 無制限バイナリ列の除外 × EditModel / Mapper」の往復を、実 SQLite（一時ファイル DB・
/// Docker 不要＝CI 常時実行）で検証する。
/// </summary>
/// <remarks>
/// <para>
/// 除外列は通常フェッチの SELECT に含まれず、VO 非 NULL の実体プロパティは初期化子が <c>= null!</c> のため
/// 未取得状態＝null で届く。EditModel の必須検証と Mapper の <c>?? throw</c> がそのままだと
/// 「取得 → 通常列だけ編集 → 保存」という素通しの往復が成立しない（＝除外機能と EditModel が併用できない）。
/// ここはその往復と、DB の blob が温存されることを実測で固定する。
/// </para>
/// <para>
/// 入力は <see cref="BinaryVoFixtureDefinition"/>（1 テーブル <c>vault_items</c>・非 NULL の除外列
/// <c>seal</c> と NULL 許容の除外列 <c>note_blob</c>）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class BinaryVoEditModelRuntimeTests : IDisposable
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider? _provider;

    /// <summary>非 NULL の除外列 seal に入れる blob</summary>
    private static readonly byte[] Seal = [1, 2, 3, 4, 5];

    /// <summary>NULL 許容の除外列 note_blob に入れる blob</summary>
    private static readonly byte[] NoteBlob = [9, 8, 7];

    private IVaultItemRepository Repository() =>
        (
            _provider ??= new ServiceCollection()
                .AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString)
                .BuildServiceProvider()
        ).GetRequiredService<IVaultItemRepository>();

    /// <summary>スキーマを作り直す（シードは各テストが EditModel 経由で行う）</summary>
    private async Task ResetAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(BinaryVoFixtureDefinition.Build(), Ct);
    }

    /// <summary>新規入力の EditModel を組み立てる（除外列は Base64 のバインディング文字列で入力する）</summary>
    private static VaultItemEditModel NewInput(
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

    /// <summary>DB 上の指定列のバイト長を実測する（NULL は -1 で返す）</summary>
    private async Task<long> ColumnLengthAsync(string column, int itemId) =>
        await Repository()
            .ExecuteScalarSqlAsync<long>(
                $"SELECT COALESCE(length({column}), -1) FROM vault_items WHERE item_id = @id",
                new { id = itemId },
                Ct
            );

    /// <summary>
    /// EditModel で blob 込みの新規行を作り（INSERT は全列）、通常フェッチ（除外列は未取得）した実体を
    /// Mapper で EditModel へ戻し、通常列だけ編集して保存できる。DB の blob は温存される。
    /// </summary>
    [Fact(
        DisplayName = "[BinaryVo] EditModel 往復: 未取得の除外列を素通しして保存でき DB の blob が温存される"
    )]
    public async Task EditModelRoundTrip_KeepsUnfetchedBlob()
    {
        await ResetAsync();
        var items = Repository();
        var mapper = new VaultItemMapper();

        // 1. EditModel から新規作成（INSERT は全列＝blob が DB に書かれる）
        var input = NewInput(1, "alpha", Seal, NoteBlob);
        input.Validate().Should().BeTrue("除外列も含めて入力済みなので検証は通る");
        await items.InsertAsync(mapper.CreateEntity(input, includeRemoved: true), Ct);

        (await ColumnLengthAsync("seal", 1)).Should().Be(Seal.Length);
        (await ColumnLengthAsync("note_blob", 1)).Should().Be(NoteBlob.Length);

        // 2. 通常フェッチ（除外列は SELECT されない＝VO 非 NULL は null のまま届く）
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
            .BeNull("未入力の除外列は実体の未取得状態のまま＝UPDATE 対象から外れる");
        (await items.UpdateAsync(fetched, cancellationToken: Ct)).Should().BeTrue();

        // 5. 通常列は更新され、DB の blob は温存されている
        (await items.GetByIdAsync(ItemIdValue.Create(1), Ct))!
            .Label.Value.Should()
            .Be("alpha-2");
        (await ColumnLengthAsync("seal", 1))
            .Should()
            .Be(Seal.Length, "UPDATE は除外列に触れないため DB の blob はそのまま残る");
        (await ColumnLengthAsync("note_blob", 1)).Should().Be(NoteBlob.Length);
    }

    /// <summary>NULL 許容の除外列だけを持つ行（note_blob=NULL）でも同じ往復が成立する</summary>
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

        (await ColumnLengthAsync("note_blob", 2)).Should().Be(-1, "未入力なので NULL で挿入される");

        var fetched = await items.GetByIdAsync(ItemIdValue.Create(2), Ct);
        var editModel = mapper.CreateEditModel(fetched!);
        editModel.BindingLabel = "beta-2";
        editModel.Validate().Should().BeTrue();

        mapper.ApplyToEntity(editModel, fetched!, includeRemoved: true);
        (await items.UpdateAsync(fetched!, cancellationToken: Ct)).Should().BeTrue();

        (await items.GetByIdAsync(ItemIdValue.Create(2), Ct))!.Label.Value.Should().Be("beta-2");
        (await ColumnLengthAsync("seal", 2))
            .Should()
            .Be(Seal.Length, "非 NULL の blob は温存される");
        (await ColumnLengthAsync("note_blob", 2)).Should().Be(-1, "NULL のままで変わらない");
    }

    /// <summary>
    /// <c>WithUnboundedBinary</c> で除外列を含めて取得した実体を Mapper 経由で保存すると、従来どおり
    /// 除外列の UPDATE ガードで例外になる（設計維持の固定）。
    /// </summary>
    [Fact(
        DisplayName = "[BinaryVo] WithUnboundedBinary 取得の Mapper 経由保存は従来どおり UPDATE ガードで例外"
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

    /// <summary>使い終えた一時 DB と DI コンテナを破棄する</summary>
    public void Dispose()
    {
        _provider?.Dispose();
        _db.Dispose();
    }
}
