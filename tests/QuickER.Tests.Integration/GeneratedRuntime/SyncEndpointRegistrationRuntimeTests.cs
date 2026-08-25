using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickER.Tests.GeneratedSyncFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期エンドポイントの「差分ソース登録漏れ」が<b>マップ時</b>に失敗することを固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// ハンドラは差分ソースをリクエストのサービスから解決するため、登録を忘れたサーバーは正常に起動して CRUD を
/// 全部答え、最初の同期でだけ不透明な 500 を返す（原因はクライアント側でなくサーバー側にあるのに、見えるのは
/// クライアント側）。ローカル半分（<c>AddGeneratedSyncEngine</c>）は登録時点で「何をすべきか」まで書いた例外を
/// 投げており、サーバー半分だけが非対称に黙っていた。
/// </para>
/// <para>
/// 検査は <c>IServiceProviderIsService</c>（登録の有無だけを問う面＝実体を作らずスコープにも触れない）で行う。
/// ここではサーバーを起動する必要すらない＝<c>MapGeneratedRemoteEndpoints</c> の呼び出し自体が失敗することを見る。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SyncEndpointRegistrationRuntimeTests
{
    /// <summary>DI 登録だけを差し替えて WebApplication を組み立てる（起動はしない＝マップ時の挙動を見る）</summary>
    private static WebApplication BuildApp(Action<IServiceCollection> registerServices)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        registerServices(builder.Services);

        return builder.Build();
    }

    /// <summary>
    /// 差分ソースを 1 つも登録していないサーバーは、エンドポイントのマップ時点で失敗する。
    /// </summary>
    /// <remarks>
    /// 例外は「欠けている閉じた型を全部並べる」形にする（1 つずつ直させると、テーブルの数だけ起動をやり直す）。
    /// 呼ぶべきメソッド名と、そのキー引数の意味（＝ソース自身はキー無しで登録される）まで含めるのは、
    /// クライアント側 fail-fast（<c>No registration of I{Entity}Repository was found...</c>）と同じ水準。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/HTTP] 差分ソース未登録のサーバーはエンドポイントのマップ時に失敗する"
    )]
    public async Task MissingSyncSources_FailAtMappingTime()
    {
        await using var app = BuildApp(services =>
        {
            // アップロードが通る面（CRUD／保存）は揃っている＝「同期以外は全部動くサーバー」を再現する
            services.AddScoped<ISyncOrderRemoteRepository>(_ => null!);
            services.AddScoped<ISyncOrderLineRemoteRepository>(_ => null!);
        });

        Action act = () => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous);

        var exception = act.Should().Throw<InvalidOperationException>().Which;

        // 欠落は全件列挙する（片方だけ登録した構成は別テストが見る）
        exception.Message.Should().Contain("ISyncServerSource<SyncOrderEntity, int>");
        exception.Message.Should().Contain("ISyncServerSource<SyncOrderLineEntity, int>");

        // 「何をすべきか」＝呼ぶべきメソッドと、そのキー引数の意味
        exception.Message.Should().Contain("AddGeneratedDirectSyncSources");
        exception.Message.Should().Contain("serverServiceKey");
        exception.Message.Should().Contain("without a key");
    }

    /// <summary>
    /// 一部だけ登録した構成では、欠けているテーブルだけが名指しされる。
    /// </summary>
    /// <remarks>
    /// 「1 つでも欠けたら全部並べる」では、既に直した分を再び直させることになる。実際に足りない型だけが
    /// 出ることを表明しておかないと、列挙が定数文字列へ退化しても気づけない。
    /// </remarks>
    [Fact(DisplayName = "[Sync/HTTP] 一部だけ登録した構成では欠けているテーブルだけが名指しされる")]
    public async Task PartiallyRegisteredSyncSources_NameOnlyTheMissingOne()
    {
        await using var app = BuildApp(services =>
            services.AddScoped<ISyncServerSource<SyncOrderEntity, int>>(_ => null!)
        );

        Action act = () => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous);

        var exception = act.Should().Throw<InvalidOperationException>().Which;

        exception.Message.Should().Contain("ISyncServerSource<SyncOrderLineEntity, int>");
        exception.Message.Should().NotContain("ISyncServerSource<SyncOrderEntity, int>");
    }

    /// <summary>
    /// 生成された登録メソッドを呼んだサーバーでは、マップが通常どおり成立する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AddGeneratedDirectSyncSources</c> はファクトリ登録なので、この時点でソースの実体（＝サーバー側の
    /// <c>ISqlExecutor</c> とリポジトリ）は解決されない。検査が「登録の有無」だけを問う面で行われていることの
    /// 表明でもある＝実体を作りに行く実装なら、ここで解決に失敗して落ちる。
    /// </para>
    /// <para>
    /// 正しく登録したサーバーの実際の同期挙動は <see cref="SyncHttpRuntimeTests"/> 一式が回帰網として見る。
    /// ここが見るのは「検査が正常系を塞いでいないこと」。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "[Sync/HTTP] 差分ソースを登録したサーバーではマップが成立する")]
    public async Task RegisteredSyncSources_MapSuccessfully()
    {
        await using var app = BuildApp(services => services.AddGeneratedDirectSyncSources(null));

        var group = app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous);

        group.Should().NotBeNull();
    }
}
