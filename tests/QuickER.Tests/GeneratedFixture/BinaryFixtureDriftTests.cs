using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedBinaryFixture;

/// <summary>
/// コミット済みの無制限バイナリ除外フィクスチャ（本体 <c>BinaryFixture.g.cs</c>＋サーバー実装
/// <c>BinaryFixture.RemoteServer.g.cs</c> の 2 ファイル）が、現在のテンプレート・型解決から再生成した
/// コードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 検証・再生成の実処理は既存フィクスチャと同じ <see cref="FixtureDriftHarness"/>（複数ファイルオーバーロード）に
/// 集約している。テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </remarks>
public sealed class BinaryFixtureDriftTests
{
    /// <summary>単一ソースの図・オプションから再生成した内容が、コミット済みフィクスチャ 2 ファイルと完全一致することを検証する</summary>
    [Fact(
        DisplayName = "コミット済みバイナリ除外フィクスチャ（本体＋サーバー）が現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedBinaryFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            BinaryFixtureDefinition.Build(),
            BinaryFixtureDefinition.Options,
            [BinaryFixtureDefinition.OutputFileName, BinaryFixtureDefinition.ServerOutputFileName],
            "コミット済みバイナリ除外フィクスチャが現在のテンプレート出力と乖離しています。"
                + "BinaryFixtureDefinition（SQLite 方言・QuickER 版 Repository＋EF Core＋インメモリ＋リモートサービス・無制限バイナリ除外・名前付きクエリ入り）から再生成が必要です。"
        );
    }
}
