using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedSqlServerBinaryFixture;

/// <summary>
/// コミット済みの SQL Server 方言バイナリフィクスチャ（本体 <c>SqlServerBinaryFixture.g.cs</c>＋サーバー実装
/// <c>SqlServerBinaryFixture.RemoteServer.g.cs</c> の 2 ファイル）が、現在のテンプレート・型解決から再生成した
/// コードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 検証・再生成の実処理は <see cref="FixtureDriftHarness"/> に集約している。
/// テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </remarks>
public sealed class SqlServerBinaryFixtureDriftTests
{
    /// <summary>
    /// 単一ソースの図・オプションから再生成した内容が、コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済み SQL Server バイナリフィクスチャ（本体＋サーバー）が現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedSqlServerBinaryFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            SqlServerBinaryFixtureDefinition.Build(),
            SqlServerBinaryFixtureDefinition.Options,
            [
                SqlServerBinaryFixtureDefinition.OutputFileName,
                SqlServerBinaryFixtureDefinition.ServerOutputFileName,
            ],
            "コミット済み SQL Server バイナリフィクスチャが現在のテンプレート出力と乖離しています。"
                + "SqlServerBinaryFixtureDefinition（SQL Server 方言・QuickER 版 Repository＋リモートサービス・無制限バイナリ除外・名前付きクエリ入り）から再生成が必要です。"
        );
    }
}
