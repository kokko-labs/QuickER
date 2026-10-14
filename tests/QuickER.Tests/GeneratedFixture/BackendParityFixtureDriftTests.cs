using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedBackendParityFixture;

/// <summary>
/// コミット済みの実装先一致フィクスチャ（本体 <c>BackendParityFixture.g.cs</c>＋サーバー実装
/// <c>BackendParityFixture.RemoteServer.g.cs</c> の 2 ファイル）が、現在のテンプレート・型解決から再生成した
/// コードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 検証・再生成の実処理は既存フィクスチャと同じ <see cref="FixtureDriftHarness"/>（複数ファイルオーバーロード）に
/// 集約している。テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </remarks>
public sealed class BackendParityFixtureDriftTests
{
    /// <summary>単一ソースの図・オプションから再生成した内容が、コミット済みフィクスチャ 2 ファイルと完全一致することを検証する</summary>
    [Fact(
        DisplayName = "コミット済み実装先一致フィクスチャ（本体＋サーバー）が現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedBackendParityFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            BackendParityFixtureDefinition.Build(),
            BackendParityFixtureDefinition.Options,
            [
                BackendParityFixtureDefinition.OutputFileName,
                BackendParityFixtureDefinition.ServerOutputFileName,
            ],
            "コミット済み実装先一致フィクスチャが現在のテンプレート出力と乖離しています。"
                + "BackendParityFixtureDefinition（SQLite 方言・QuickER 版 Repository＋EF Core＋インメモリ＋リモートサービス・VO・無制限バイナリ除外）から再生成が必要です。"
        );
    }
}
