using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EditModel の照合糖衣（<c>ValidateUniqueAsync</c>）を、QuickER 版 Repository（SQLite・一時ファイル DB・Docker 不要＝CI 常時実行）で
/// 検証する派生。
/// </summary>
/// <remarks>
/// 「EditModel の確定値から Entity を組む → Repository の <c>CheckUniquenessAsync</c> → 違反の構成列を
/// バインディングプロパティへ写す」経路と、再検証で古い重複エラーが残らないことは基底
/// （<see cref="EditModelValidateUniqueRuntimeTestsBase{TOrderModel}"/>）が持つ。リポジトリを通らない違反の登録の経路
/// （構成列を持たない違反のモデルレベルエラー）は実装先に依らないので、この派生だけが持つ。
/// </remarks>
[Trait("Category", "Integration")]
[RuntimeCell(RuntimeFeatures.EditModelValidateUnique, RuntimeTarget.AdoSqlite)]
public sealed class EditModelValidateUniqueAdoSqliteRuntimeTests
    : EditModelValidateUniqueQueryFixtureRuntimeTestsBase
{
    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider? _provider;

    protected override ICustomerRepository Customers =>
        _provider!.GetRequiredService<ICustomerRepository>();

    protected override IOrderRepository Orders => _provider!.GetRequiredService<IOrderRepository>();

    protected override async Task CreateStorageAsync()
    {
        await _db.ApplyDdlAsync(QueryFixtureDefinition.Build(), Ct);

        _provider = new ServiceCollection()
            .AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString)
            .BuildServiceProvider();
    }

    protected override Task TearDownAsync()
    {
        _provider?.Dispose();
        _db.Dispose();

        return Task.CompletedTask;
    }

    /// <summary>6. 構成列を持たない違反はモデルレベルエラー（プロパティ名なし）になる</summary>
    [Fact(DisplayName = "[ValidateUnique] 6: 構成列なしの違反はモデルレベルエラーになる")]
    public void EntityLevelViolation_IsRegisteredWithoutProperty()
    {
        var model = NewOrder("99", 2, 12m, "pear");

        // 違反の登録経路そのもの（ValidateUniqueAsync が違反ごとに呼ぶ）を直接叩く
        model.RegisterDuplicateError([], "The combination is already taken.");

        model.HasErrors.Should().BeTrue();
        ((IEnumerable)model.GetErrors(string.Empty))
            .Cast<string>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("The combination is already taken.");
        ((IEnumerable)model.GetErrors(null))
            .Cast<string>()
            .Should()
            .Contain("The combination is already taken.");
    }
}
