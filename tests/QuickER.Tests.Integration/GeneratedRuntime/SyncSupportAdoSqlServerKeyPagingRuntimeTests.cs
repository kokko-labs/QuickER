using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedSyncKeyFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 版なし（<c>rowversion</c> 列の無い）同期の表のキー順ページングを、主キーの型ごとに実 SQL Server で流す統合テスト
/// （行の表は <see cref="SyncKeyPagingRows"/>）。
/// </summary>
/// <remarks>
/// <para>
/// サーバーへ DDL と行を入れ、生成の直結の差分ソース <c>VersionlessDirectSyncSource</c>（<see cref="GeneratedSyncTables"/> の記述子と
/// QuickER 版 SQL Server の <c>SqlExecutor</c> で組む）で <c>GetFirstPageAsync</c> と <c>GetPageAfterAsync</c> を batchSize 1（読み飛ばしを
/// 見る 1 行だけ 2）で最後まで呼ぶ。行の表は種を使わない決定的な表なので、作らない形の宣言は持たない。
/// </para>
/// <para>
/// 作らない形と理由:
/// </para>
/// <list type="bullet">
///   <item>値オブジェクトの主キー：前のページの最後のキーは記述子の <c>ReadKey</c> が値オブジェクトの中の値を取り出して束縛するので、
///   束縛の型を決めるのは中の値の型で、この表の素の型の行と同じ経路を通る。値オブジェクトのキーの生成の形は、後のフェーズで
///   生成の行（同期の構成の行列の値オブジェクトのキーのフィクスチャ）として見る。</item>
///   <item>HTTP の差分ソース：サーバーの同期専用のエンドポイントは、サーバーの中で同じ直結の差分ソースを呼ぶ（同期の HTTP は差分ソースの
///   薄い remoting）。キーのテキストの往復は <c>SyncSupportAdoSqliteKeyTextRuntimeTests</c> が見るので、ページの境目の束縛はここで
///   直結を流せば足りる。</item>
///   <item>端末（SQLite）までの同期：取得の守り（再開点が進まないときの例外）は差分ソースの外の <c>SyncTable</c> にあり、読み飛ばしは
///   差分ソースが返す行の時点で決まる。この表は差分ソースの返す行だけを表明する。</item>
/// </list>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.AdoSqlServer)]
public sealed class SyncSupportAdoSqlServerKeyPagingRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _fixture = fixture;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>サーバーのスキーマを空にして、図（主キーの型の違う版なしの表 9 つ）の DDL を当てる</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(SyncKeyFixtureDefinition.Build(), Ct);
    }

    /// <summary>後片付けは無い（次のテストの初期化がスキーマを空にする）</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>版なしの表のキー順ページングは、主キーの型と境目の値に依らずサーバーの全行を順にちょうど 1 回ずつ読む</summary>
    [Theory(
        DisplayName = "[Sync/SqlServer] 版なしの表のキー順ページングは、主キーの型と境目の値に依らずサーバーの全行を順に 1 回ずつ読む"
    )]
    [MemberData(nameof(SyncKeyPagingRows.Ids), MemberType = typeof(SyncKeyPagingRows))]
    public async Task KeyPaging_ReadsEveryRowOnceInOrder(string id)
    {
        var harness = new SyncKeyPagingHarness(_fixture.ConnectionString);
        await SyncKeyPagingRows.Table[id].Input.Run(harness, Ct);
    }
}
