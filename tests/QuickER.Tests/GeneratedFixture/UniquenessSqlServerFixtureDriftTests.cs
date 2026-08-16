using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedUniquenessSqlServerFixture;

/// <summary>
/// コミット済みの SQL Server 方言・重複事前チェックフィクスチャ <c>UniquenessSqlServerFixture.g.cs</c> が、
/// 現在のテンプレート・型解決から再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 検証・再生成の実処理は <see cref="FixtureDriftHarness"/> に集約している。
/// テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </remarks>
public sealed class UniquenessSqlServerFixtureDriftTests
{
    /// <summary>
    /// 単一ソースの図・オプションから再生成した内容が、コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済み SQL Server 重複チェックフィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedUniquenessSqlServerFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            UniquenessSqlServerFixtureDefinition.Build(),
            UniquenessSqlServerFixtureDefinition.Options,
            UniquenessSqlServerFixtureDefinition.OutputFileName,
            "コミット済み SQL Server 重複チェックフィクスチャが現在のテンプレート出力と乖離しています。"
                + "UniquenessSqlServerFixtureDefinition から再生成が必要です。"
        );
    }
}
