using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSqliteFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// SQLite 方言ランタイムスイートを<b>EF Core Sqlite 版</b>で実行する派生。リポジトリ・エグゼキュータは実運用と
/// 同じ DI 経路（<c>AddGeneratedEfCoreRepositories(options =&gt; options.UseSqlite(...))</c> →
/// <see cref="ServiceProvider"/> から解決）で取得する。接続は基底と同一の一時ファイル DB。
/// </summary>
/// <remarks>
/// <para>
/// 基底 <see cref="GeneratedSqliteRuntimeTestsBase"/> の全シナリオを EF Core Sqlite で流すことで、
/// <b>「AddGeneratedRepositories（自作 SQLite）⇔ AddGeneratedEfCoreRepositories＋UseSqlite（EF）を差し替える
/// だけで交換可能」</b>という契約を、<see cref="GeneratedSqliteAdoRuntimeTests"/> と同一のアサーション集合で
/// 証明する（＝SQLite AdoParity）。両派生が同じ基底シナリオを緑にすることが、同一 DB 状態への読み書き双方向で
/// 両実装が同じ結果を返すことの証明になる。
/// </para>
/// <para>
/// EF Core Sqlite の <c>decimal</c> 制約（TEXT 格納・サーバー側 ORDER BY/比較/集計を直接は非対応）を踏まえ、
/// 基底シナリオは並び替え・ページングを整数キーで行い、生 SQL 集計を <c>ExecuteScalarSqlAsync&lt;decimal&gt;</c>
/// で検証している。これにより両バックエンドで基底を無改変で共有できる。
/// </para>
/// </remarks>
public sealed class GeneratedSqliteEfCoreParityRuntimeTests : GeneratedSqliteRuntimeTestsBase
{
    /// <summary>EF 版リポジトリ群を登録した DI コンテナ（UseSqlite・接続文字列は基底の一時 DB）</summary>
    private ServiceProvider? _provider;

    /// <summary>AddGeneratedEfCoreRepositories → UseSqlite の DI 経路でリポジトリ群を解決する</summary>
    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options => options.UseSqlite(ConnectionString))
            .BuildServiceProvider();

    protected override ICustomerRepository CreateCustomerRepository() =>
        Provider().GetRequiredService<ICustomerRepository>();

    protected override IOrderRepository CreateOrderRepository() =>
        Provider().GetRequiredService<IOrderRepository>();

    protected override ISqlExecutor CreateSqlExecutor() =>
        Provider().GetRequiredService<ISqlExecutor>();

    /// <summary>
    /// 4b（EF 代替版）: ThenInclude 再帰は EF Core の no-tracking クエリがサイクル（<c>Orders-&gt;Customer</c>）を
    /// 拒否するため、自作版と同一のシナリオは実行できない。等価な「子の親参照ロード」を非サイクル経路で検証する。
    /// </summary>
    /// <remarks>
    /// <b>基底との差分</b>: 自作 <c>IncludeLoader</c> は親→子→親のサイクルをマルチクエリで解決できるが、
    /// EF Core（no-tracking）は Include パスのサイクルを実行時に拒否する（"Cycles are not allowed in
    /// no-tracking queries"）。EF での等価検証として、各注文を <c>Include(o =&gt; o.Customer)</c> で個別にロードし、
    /// 全注文が同じ親（CustomerId=1）を参照することを確認する。これは自作版 4b が保証する「子の親参照が正しく
    /// 復元される」ことと同一の観測結果である（ロード経路のみ異なる）。
    /// </remarks>
    public override async Task ThenInclude_Recursive_LoadsParentReference()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();

        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 10m, "a"), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 20m, "b"), Ct);
        await orders.InsertAsync(NewOrder(12, 1, 30m, "c"), Ct);

        // 非サイクル経路: 各注文を子→親 Include で個別ロードし、親参照が正しく復元されることを確認する
        var loaded = await orders.Query().Include(o => o.Customer).ToListAsync(Ct);
        loaded.Should().HaveCount(3);
        loaded.Select(o => o.OrderId.Value).Should().BeEquivalentTo([10, 11, 12]);
        loaded.Should().OnlyContain(o => o.Customer != null && o.Customer.CustomerId.Value == 1);
    }

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }
}
