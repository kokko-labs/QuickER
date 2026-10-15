using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedIdentityColumnFixture;

/// <summary>
/// コミット済みフィクスチャ <c>IdentityColumnFixture.g.cs</c> が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// SQL Server 方言 × EF Core の生成物を固定する。守る対象は「非主キーの自動採番列へ <c>[IdentityColumn]</c> を刻むこと」と
/// 「EF Core の Fluent が <c>ValueGeneratedOnAddOrUpdate()</c> を出すこと」のテキスト。
/// 再生成手順は <see cref="FixtureDriftHarness"/> の docstring と失敗メッセージを参照。
/// </remarks>
public sealed class IdentityColumnFixtureDriftTests
{
    /// <summary>
    /// 単一ソースの図・オプション（SQL Server 方言 × EF Core）から再生成した内容が、
    /// コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済み自動採番列フィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedIdentityColumnFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            IdentityColumnFixtureDefinition.Build(),
            IdentityColumnFixtureDefinition.Options,
            IdentityColumnFixtureDefinition.OutputFileName,
            "コミット済み自動採番列フィクスチャが現在のテンプレート出力と乖離しています。"
                + "IdentityColumnFixtureDefinition（SQL Server 方言・QuickER 版 Repository・EF Core）から再生成が必要です。"
        );
    }
}
