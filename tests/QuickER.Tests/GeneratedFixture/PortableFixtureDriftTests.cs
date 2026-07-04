using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedPortableFixture;

/// <summary>
/// コミット済みの方言可搬フィクスチャ <c>PortableFixture.g.cs</c> が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// <para>
/// 基準方言は SQL Server（<see cref="PortableDialect.SqlServer"/>）。生成される C# は方言非依存のため、
/// どの方言の型表記から生成しても出力は一致する（<see cref="PortableFixtureDialectIndependenceTests"/> が保証）。
/// </para>
/// <para>
/// 検証・再生成の実処理は <see cref="FixtureDriftHarness"/> に集約している。
/// テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </para>
/// </remarks>
public sealed class PortableFixtureDriftTests
{
    /// <summary>
    /// 単一ソースの図（SQL Server 基準）・オプションから再生成した内容が、
    /// コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済み可搬フィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedPortableFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            PortableFixtureDefinition.Build(PortableDialect.SqlServer),
            PortableFixtureDefinition.Options,
            PortableFixtureDefinition.OutputFileName,
            "コミット済み可搬フィクスチャが現在のテンプレート出力と乖離しています。"
                + "PortableFixtureDefinition（SQL Server 基準）から再生成が必要です。"
        );
    }
}
