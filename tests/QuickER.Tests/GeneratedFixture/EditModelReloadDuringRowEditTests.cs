using AwesomeAssertions;
using Xunit;

// 生成フィクスチャ自身の namespace に置く＝RowState 等が他フィクスチャの同名型と曖昧にならないようにする
namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// 行編集中（BeginEdit 済み）に Mapper のロードが走ったときの意味論を検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// 固定する規則: ロードは進行中の行編集を「破棄」する（スナップショットへの復元はしない）。
/// BeginEdit のスナップショットはロード前の値なので、ロード後に届く CancelEdit が
/// ロード済みの最新値を編集前の古い値へ巻き戻してはいけない（旧: 巻き戻っていた）。
/// </remarks>
public sealed class EditModelReloadDuringRowEditTests
{
    private static CustomerEntity BuildCustomerEntity(decimal balance)
    {
        var entity = new CustomerEntity
        {
            CustomerId = 1,
            Name = "customer",
            Balance = balance,
        };

        entity.MarkUnchanged();
        return entity;
    }

    [Fact]
    public void 行編集中のロード後のCancelEditはロード済みの値を巻き戻さない()
    {
        var mapper = new CustomerMapper();
        var model = mapper.CreateEditModel(BuildCustomerEntity(100m));

        // DataGrid の行編集が始まり、編集途中の値がある状態
        model.BeginEdit();
        model.BindingBalance = "50";
        model.Balance.Should().Be(50m);

        // 行編集の最中に外部要因（再取得など）でロードが走る
        mapper.ApplyToEditModel(BuildCustomerEntity(99m), model);
        model.Balance.Should().Be(99m);

        // その後に届く CancelEdit は no-op＝ロード前のスナップショット（100）にも編集途中（50）にも戻らない
        model.CancelEdit();

        model
            .Balance.Should()
            .Be(99m, "ロードが行編集を破棄済みなので CancelEdit は何も復元しない");
        model.RowState.Should().Be(RowState.Unchanged, "ロード直後の状態が保たれる");
    }

    [Fact]
    public void ロード後に改めて始めた行編集のCancelEditは従来どおり復元する()
    {
        var mapper = new CustomerMapper();
        var model = mapper.CreateEditModel(BuildCustomerEntity(100m));

        model.BeginEdit();
        model.BindingBalance = "50";
        mapper.ApplyToEditModel(BuildCustomerEntity(99m), model);

        // ロード後に新しく始めた行編集は、通常どおりキャンセルで開始時点へ戻る
        model.BeginEdit();
        model.BindingBalance = "70";
        model.Balance.Should().Be(70m);

        model.CancelEdit();

        model.Balance.Should().Be(99m, "新しい行編集の開始時点（ロード済みの値）へ戻る");
    }
}
