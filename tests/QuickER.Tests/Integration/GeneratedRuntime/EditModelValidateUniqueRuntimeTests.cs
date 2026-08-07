using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EditModel の DB 照合糖衣（<c>ValidateUniqueAsync</c>）を、実 SQLite（一時ファイル DB・Docker 不要＝CI 常時実行）で検証する。
/// </summary>
/// <remarks>
/// 「EditModel の確定値から Entity を組む → Repository の <c>CheckUniquenessAsync</c> → 違反の構成列を
/// バインディングプロパティへ写す」経路と、再検証で古い重複エラーが残らないことを固定する。
/// ユーザー定義フックが返す「図の列に対応しない違反」がモデルレベルエラー（プロパティ名なし）になることも確かめる。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class EditModelValidateUniqueRuntimeTests : IAsyncLifetime
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider? _provider;

    /// <summary>注文リポジトリ（生成された全機能面）</summary>
    private IOrderRepository Orders => _provider!.GetRequiredService<IOrderRepository>();

    /// <summary>スキーマを作成し、注文 10（顧客 1・100・apple pie）を投入する</summary>
    public async ValueTask InitializeAsync()
    {
        var ddl = new SqliteDdlGenerator().Build(QueryFixtureDefinition.Build());
        await _db.ApplyDdlAsync(ddl, Ct);

        _provider = new ServiceCollection()
            .AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString)
            .BuildServiceProvider();

        await _provider
            .GetRequiredService<ICustomerRepository>()
            .InsertAsync(
                new CustomerEntity
                {
                    CustomerId = CustomerIdValue.Create(1),
                    Name = NameValue.Create("Alice"),
                },
                Ct
            );
        await Orders.InsertAsync(
            new OrderEntity
            {
                OrderId = OrderIdValue.Create(10),
                CustomerId = CustomerIdValue.Create(1),
                Amount = AmountValue.Create(100m),
                Memo = MemoValue.Create("apple pie"),
            },
            Ct
        );
    }

    /// <summary>入力済みの注文 EditModel を組み立てる</summary>
    private static OrderEditModel NewOrder(
        int orderId,
        int customerId,
        decimal amount,
        string memo
    ) =>
        new()
        {
            BindingOrderId = orderId.ToString(),
            BindingCustomerId = customerId.ToString(),
            BindingAmount = amount.ToString(),
            BindingMemo = memo,
        };

    /// <summary>指定プロパティのエラー一覧を取り出す</summary>
    private static string[] GetErrors(EditModelBase model, string propertyName) =>
        ((IEnumerable)model.GetErrors(propertyName)).Cast<string>().ToArray();

    /// <summary>1. DB に同じ値の行があると false を返し、構成列のバインディングプロパティへエラーが載る</summary>
    [Fact(DisplayName = "[ValidateUnique] 1: DB 上の重複でエラーが登録される")]
    public async Task Duplicate_RegistersBindingError()
    {
        var model = NewOrder(99, 1, 12m, "apple pie");

        (await model.ValidateUniqueAsync(Orders, Ct)).Should().BeFalse();

        GetErrors(model, nameof(OrderEditModel.BindingMemo)).Should().ContainSingle();
        GetErrors(model, nameof(OrderEditModel.BindingAmount)).Should().BeEmpty();
    }

    /// <summary>2. 自分自身（同一主キーの行）は除外されるため、既存行の編集は違反にならない</summary>
    [Fact(DisplayName = "[ValidateUnique] 2: 同一主キーの行は除外される")]
    public async Task SameKey_IsExcluded()
    {
        var model = NewOrder(10, 1, 100m, "apple pie");

        (await model.ValidateUniqueAsync(Orders, Ct)).Should().BeTrue();
        model.HasErrors.Should().BeFalse();
    }

    /// <summary>3. 重複を解消して再検証すると前回の重複エラーが消える</summary>
    [Fact(DisplayName = "[ValidateUnique] 3: 再検証で古い重複エラーが消える")]
    public async Task Revalidation_ClearsPreviousErrors()
    {
        var model = NewOrder(99, 1, 12m, "apple pie");

        (await model.ValidateUniqueAsync(Orders, Ct)).Should().BeFalse();

        model.BindingMemo = "pear";

        (await model.ValidateUniqueAsync(Orders, Ct)).Should().BeTrue();
        GetErrors(model, nameof(OrderEditModel.BindingMemo)).Should().BeEmpty();
        model.HasErrors.Should().BeFalse();
    }

    /// <summary>4. 複合制約の違反は全構成列のバインディングプロパティへ載る</summary>
    [Fact(DisplayName = "[ValidateUnique] 4: 複合制約の違反が全構成列へ載る")]
    public async Task CompositeViolation_RegistersOnAllMembers()
    {
        var model = NewOrder(99, 1, 100m, "pear");

        (await model.ValidateUniqueAsync(Orders, Ct)).Should().BeFalse();

        GetErrors(model, nameof(OrderEditModel.BindingCustomerId)).Should().ContainSingle();
        GetErrors(model, nameof(OrderEditModel.BindingAmount)).Should().ContainSingle();
        GetErrors(model, nameof(OrderEditModel.BindingMemo)).Should().BeEmpty();
    }

    /// <summary>5. ユーザー定義フックの違反はメッセージが優先され、構成列がある限り該当列へ載る</summary>
    [Fact(DisplayName = "[ValidateUnique] 5: ユーザー定義フックのメッセージが優先される")]
    public async Task CustomViolation_UsesItsOwnMessage()
    {
        var model = NewOrder(99, 2, OrderUniquenessCustomCheck.ReservedAmount, "pear");

        (await model.ValidateUniqueAsync(Orders, Ct)).Should().BeFalse();

        GetErrors(model, nameof(OrderEditModel.BindingAmount))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(OrderUniquenessCustomCheck.Message);
    }

    /// <summary>6. 構成列を持たない違反はモデルレベルエラー（プロパティ名なし）になる</summary>
    [Fact(DisplayName = "[ValidateUnique] 6: 構成列なしの違反はモデルレベルエラーになる")]
    public void EntityLevelViolation_IsRegisteredWithoutProperty()
    {
        var model = NewOrder(99, 2, 12m, "pear");

        // 違反の登録経路そのもの（ValidateUniqueAsync が違反ごとに呼ぶ）を直接叩く
        model.RegisterDuplicateError([], "The combination is already taken.");

        model.HasErrors.Should().BeTrue();
        GetErrors(model, string.Empty)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("The combination is already taken.");
        ((IEnumerable)model.GetErrors(null))
            .Cast<string>()
            .Should()
            .Contain("The combination is already taken.");
    }

    /// <summary>DI コンテナと一時 DB を破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();
        _db.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
