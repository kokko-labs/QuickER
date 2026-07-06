using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// コミット済みのインメモリフィクスチャ <c>InMemoryFixture.g.cs</c> が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 検証・再生成の実処理は <see cref="FixtureDriftHarness"/> に集約している。
/// テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </remarks>
public sealed class InMemoryFixtureDriftTests
{
    /// <summary>
    /// 単一ソースの図・オプションから再生成した内容が、コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済みインメモリフィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedInMemoryFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            InMemoryFixtureDefinition.Build(),
            InMemoryFixtureDefinition.Options,
            InMemoryFixtureDefinition.OutputFileName,
            "コミット済みインメモリフィクスチャが現在のテンプレート出力と乖離しています。"
                + "InMemoryFixtureDefinition から再生成が必要です。"
        );
    }
}
