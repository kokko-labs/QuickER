using System.Threading.Tasks;
using QuickER.Tests.GeneratedQueryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 名前付きクエリのランタイムスイート（DSL・manual 部）を<b>インメモリ Repository</b>で実行する派生。
/// 実 DB を使わないため Docker 不要＝CI 常時実行。
/// </summary>
/// <remarks>
/// <para>
/// ミニ DSL の条件は「単一の C# ラムダ式」へエミットされ、QuickER 版 Repository と EF Core は式木を SQL へ翻訳し、
/// インメモリは式木をコンパイルして C# の意味論で評価する。同一のアサーション集合を 3 実装先すべてで緑にすることで、
/// <b>翻訳経路と直接評価経路の観測結果が一致する</b>ことを固定する（文字列一致・IN・NULL 列を含む射影は、
/// 過去にインメモリだけ挙動が割れた前科のある経路）。
/// </para>
/// <para>
/// 自由 SQL 由来の戻り形はインメモリでは実装が生成されない（manual＝テスト側 partial）ため、
/// <see cref="NamedQueryRawSqlRuntimeTestsBase"/> の階層には入らない。
/// </para>
/// </remarks>
public sealed class NamedQueryInMemoryRuntimeTests : NamedQueryRuntimeTestsBase
{
    /// <summary>全リポジトリで共有するインメモリストア（実 DB のファイルに相当する永続点）</summary>
    private readonly InMemoryDataStore _store = new();

    protected override ICustomerRepository CreateCustomerRepository() =>
        new InMemoryCustomerRepository(_store);

    protected override IOrderRepository CreateOrderRepository() =>
        new InMemoryOrderRepository(_store);

    /// <summary>ストアを空にして共通のシードデータを投入する</summary>
    protected override Task ResetAndSeedAsync()
    {
        _store.Clear();

        return SeedAsync();
    }
}
