using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedBinaryVoFixture;

/// <summary>
/// コミット済みの「値オブジェクト × 無制限バイナリ除外」フィクスチャ（<c>BinaryVoFixture.g.cs</c>）が、
/// 現在のテンプレート・型解決から再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 検証・再生成の実処理は既存フィクスチャと同じ <see cref="FixtureDriftHarness"/> に集約している。
/// テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </remarks>
public sealed class BinaryVoFixtureDriftTests
{
    /// <summary>単一ソースの図・オプションから再生成した内容が、コミット済みフィクスチャと完全一致することを検証する</summary>
    [Fact(
        DisplayName = "コミット済み VO×バイナリ除外フィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedBinaryVoFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            BinaryVoFixtureDefinition.Build(),
            BinaryVoFixtureDefinition.Options,
            BinaryVoFixtureDefinition.OutputFileName,
            "コミット済み VO×バイナリ除外フィクスチャが現在のテンプレート出力と乖離しています。"
                + "BinaryVoFixtureDefinition（SQLite 方言・QuickER 版 Repository＋値オブジェクト＋EditModel/Mapper・無制限バイナリ除外）から再生成が必要です。"
        );
    }
}
