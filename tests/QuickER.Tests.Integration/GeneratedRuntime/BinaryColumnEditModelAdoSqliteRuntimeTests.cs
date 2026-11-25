using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryVoFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 「値オブジェクト × 無制限バイナリ列の除外 × EditModel / Mapper」の往復を、QuickER 版 Repository（SQLite・一時ファイル DB・
/// Docker 不要＝CI 常時実行）で検証する派生。
/// </summary>
/// <remarks>
/// 往復の表明は基底（<see cref="BinaryColumnEditModelRuntimeTestsBase"/>）が持ち、この派生は保存先の blob の長さを生の SQL で実測する
/// （UPDATE が除外列に触れないことを DB の列で確かめる）。リポジトリを通らない新規行の必須検証は実装先に依らないので、この派生だけが持つ。
/// </remarks>
[Trait("Category", "Integration")]
[RuntimeCell(RuntimeFeatures.BinaryColumnEditModel, RuntimeTarget.AdoSqlite)]
public sealed class BinaryColumnEditModelAdoSqliteRuntimeTests
    : BinaryColumnEditModelRuntimeTestsBase,
        IDisposable
{
    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider? _provider;

    protected override IVaultItemRepository Repository() =>
        (
            _provider ??= new ServiceCollection()
                .AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString)
                .BuildServiceProvider()
        ).GetRequiredService<IVaultItemRepository>();

    protected override async Task ResetAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(BinaryVoFixtureDefinition.Build(), Ct);
    }

    protected override async Task<long> StoredLengthAsync(string column, int itemId) =>
        await Repository()
            .ExecuteScalarSqlAsync<long>(
                $"SELECT COALESCE(length({column}), -1) FROM vault_items WHERE item_id = @id",
                new { id = itemId },
                Ct
            );

    /// <summary>
    /// 新規行（<c>RowState.Added</c>）では NOT NULL の除外列が必須入力になる。未入力のままだと検証が落ち、
    /// 当該バインディングへ列名つきの必須エラーが立つ。
    /// </summary>
    /// <remarks>
    /// この入力は DB 行きが確定しており（INSERT は全列＝<c>seal</c> に NULL を送る）、そのまま流せば
    /// SQLite の NOT NULL 制約違反で必ず落ちる。画面で止められるのはここだけなので検証で捕まえる。
    /// </remarks>
    [Fact(
        DisplayName = "[BinaryVo] 新規行では NOT NULL の除外列が未入力だと必須エラーで検証が落ちる"
    )]
    public async Task NewRow_MissingNonNullableExcludedColumn_FailsValidation()
    {
        await ResetAsync();

        // seal（NOT NULL の除外列）だけ未入力の新規行
        var input = NewInput(10, "delta", seal: null, noteBlob: null);
        input.RowState.Should().Be(RowState.Added);

        input.Validate().Should().BeFalse("未入力のまま INSERT すると DB の NOT NULL 違反になる");

        input
            .CollectErrors()
            .Should()
            .ContainSingle()
            .Which.Property.Should()
            .Be(nameof(VaultItemEditModel.BindingSeal));

        input
            .GetErrors(nameof(VaultItemEditModel.BindingSeal))
            .Cast<string>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain(SealValue.DisplayName, "必須エラーは列名（表示名）つきで出る");

        // NULL 許容の除外列は従来どおり未入力で通る
        input
            .GetErrors(nameof(VaultItemEditModel.BindingNoteBlob))
            .Cast<string>()
            .Should()
            .BeEmpty();
    }

    /// <summary>使い終えた一時 DB と DI コンテナを破棄する</summary>
    public void Dispose()
    {
        _provider?.Dispose();
        _db.Dispose();
    }
}
