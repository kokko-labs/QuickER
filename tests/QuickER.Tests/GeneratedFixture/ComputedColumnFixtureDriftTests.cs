using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedComputedColumnFixture;

/// <summary>
/// コミット済みフィクスチャ <c>ComputedColumnFixture.g.cs</c> が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 計算列 × マルチターゲット（<c>RepositoryDialects=["sqlserver","sqlite"]</c>）の生成物を固定する。
/// 守る対象は「<c>[ComputedColumn]</c> の付与」と「両方言の実装がその列を INSERT / UPDATE から外す
/// （行バージョン列と違い方言で切り替わらない）」というテキスト。
/// 再生成手順は <see cref="FixtureDriftHarness"/> の docstring と失敗メッセージを参照。
/// </remarks>
public sealed class ComputedColumnFixtureDriftTests
{
    /// <summary>
    /// 単一ソースの図・オプション（計算列 × マルチターゲット）から再生成した内容が、
    /// コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済み計算列マルチターゲットフィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedComputedColumnFixture_MatchesRegeneratedOutput()
    {
        var diagram = ComputedColumnFixtureDefinition.Build();
        var (primary, byDialect) = ComputedColumnFixtureDefinition.ResolveColumnTypes(diagram);

        FixtureDriftHarness.VerifyOrRegenerate(
            diagram,
            primary,
            byDialect,
            ComputedColumnFixtureDefinition.Options,
            ComputedColumnFixtureDefinition.OutputFileName,
            "コミット済み計算列マルチターゲットフィクスチャが現在のテンプレート出力と乖離しています。"
                + "ComputedColumnFixtureDefinition（計算列 × sqlserver / sqlite のQuickER 版 Repository）から再生成が必要です。"
        );
    }
}
