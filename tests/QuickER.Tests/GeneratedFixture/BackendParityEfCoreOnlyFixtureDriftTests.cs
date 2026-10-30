using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedBackendParityEfCoreOnlyFixture;

/// <summary>
/// コミット済みの実装先一致の EF Core 単独フィクスチャ（本体 <c>BackendParityEfCoreOnlyFixture.g.cs</c>＋サーバー実装
/// <c>BackendParityEfCoreOnlyFixture.RemoteServer.g.cs</c> の 2 ファイル）が、現在のテンプレート・型解決から再生成した
/// コードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 検証・再生成の実処理は既存フィクスチャと同じ <see cref="FixtureDriftHarness"/>（複数ファイルオーバーロード）に集約している。
/// </remarks>
public sealed class BackendParityEfCoreOnlyFixtureDriftTests
{
    /// <summary>単一ソースの図・オプションから再生成した内容が、コミット済みフィクスチャ 2 ファイルと完全一致することを検証する</summary>
    [Fact(
        DisplayName = "コミット済み実装先一致の EF Core 単独フィクスチャ（本体＋サーバー）が現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedBackendParityEfCoreOnlyFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            BackendParityEfCoreOnlyFixtureDefinition.Build(),
            BackendParityEfCoreOnlyFixtureDefinition.Options,
            [
                BackendParityEfCoreOnlyFixtureDefinition.OutputFileName,
                BackendParityEfCoreOnlyFixtureDefinition.ServerOutputFileName,
            ],
            "コミット済み実装先一致の EF Core 単独フィクスチャが現在のテンプレート出力と乖離しています。"
                + "BackendParityEfCoreOnlyFixtureDefinition（実装先一致フィクスチャと同じ図・EF Core＋リモートサービスだけ・VO・無制限バイナリ除外）から再生成が必要です。"
        );
    }
}
