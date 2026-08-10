using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedConcurrencyFixture;

/// <summary>
/// コミット済みの並行性フィクスチャ（本体 <c>ConcurrencyFixture.g.cs</c>＋サーバー実装
/// <c>ConcurrencyFixture.RemoteServer.g.cs</c> の 2 ファイル）が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 検証・再生成の実処理は既存フィクスチャと同じ <see cref="FixtureDriftHarness"/>（複数ファイルオーバーロード）に
/// 集約している。テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </remarks>
public sealed class ConcurrencyFixtureDriftTests
{
    /// <summary>単一ソースの図・オプションから再生成した内容が、コミット済みフィクスチャ 2 ファイルと完全一致することを検証する</summary>
    [Fact(
        DisplayName = "コミット済み並行性フィクスチャ（本体＋サーバー）が現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedConcurrencyFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            ConcurrencyFixtureDefinition.Build(),
            ConcurrencyFixtureDefinition.Options,
            [
                ConcurrencyFixtureDefinition.OutputFileName,
                ConcurrencyFixtureDefinition.ServerOutputFileName,
            ],
            "コミット済み並行性フィクスチャが現在のテンプレート出力と乖離しています。"
                + "ConcurrencyFixtureDefinition（SQL Server 方言・QuickER 版 Repository＋EF Core＋インメモリ＋リモートサービス・VO 有効・rowversion 列あり）から再生成が必要です。"
        );
    }
}
