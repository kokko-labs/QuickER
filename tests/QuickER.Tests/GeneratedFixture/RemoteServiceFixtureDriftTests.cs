using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedRemoteServiceFixture;

/// <summary>
/// コミット済みのリモートサービスフィクスチャ（本体 <c>RemoteServiceFixture.g.cs</c>＋サーバー実装
/// <c>RemoteServiceFixture.RemoteServer.g.cs</c> の 2 ファイル）が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 検証・再生成の実処理は既存フィクスチャと同じ <see cref="FixtureDriftHarness"/>（複数ファイルオーバーロード）に
/// 集約している。テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </remarks>
public sealed class RemoteServiceFixtureDriftTests
{
    /// <summary>単一ソースの図・オプションから再生成した内容が、コミット済みフィクスチャ 2 ファイルと完全一致することを検証する</summary>
    [Fact(
        DisplayName = "コミット済みリモートサービスフィクスチャ（本体＋サーバー）が現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedRemoteServiceFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            RemoteServiceFixtureDefinition.Build(),
            RemoteServiceFixtureDefinition.Options,
            [
                RemoteServiceFixtureDefinition.OutputFileName,
                RemoteServiceFixtureDefinition.ServerOutputFileName,
            ],
            "コミット済みリモートサービスフィクスチャが現在のテンプレート出力と乖離しています。"
                + "RemoteServiceFixtureDefinition（SQLite 方言・自作 Repository＋EF Core・リモートサービス生成・名前付きクエリ入り）から再生成が必要です。"
        );
    }
}
