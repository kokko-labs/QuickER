using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedMultiTargetFixture;

/// <summary>
/// コミット済みのマルチターゲットフィクスチャ <c>MultiTargetPortableFixture.g.cs</c> が、現在のテンプレート・
/// 型解決から再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// <para>
/// 入力の図は方言可搬フィクスチャと同一だが、オプションが自作 Repository のマルチターゲット
/// （<c>RepositoryDialects=["sqlserver","sqlite"]</c>・EF Core なし）である点が異なる
/// （<see cref="MultiTargetPortableFixtureDefinition.Options"/>）。契約 1 回＋方言別 namespace 実装
/// （<c>.SqlServer</c> / <c>.Sqlite</c>）＋方言別 DI（keyed 版含む）のテキストがテンプレート変更で
/// 乖離していないことを守る。
/// </para>
/// <para>
/// 検証・再生成の実処理は既存 3 フィクスチャと同じ <see cref="FixtureDriftHarness"/> に集約している
/// （マルチ辞書オーバーロード）。テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </para>
/// </remarks>
public sealed class MultiTargetPortableFixtureDriftTests
{
    /// <summary>
    /// 単一ソースの図・オプション（マルチターゲット）から再生成した内容が、
    /// コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済みマルチターゲットフィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedMultiTargetFixture_MatchesRegeneratedOutput()
    {
        var diagram = MultiTargetPortableFixtureDefinition.Build();
        var (primary, byDialect) = MultiTargetPortableFixtureDefinition.ResolveColumnTypes(diagram);

        FixtureDriftHarness.VerifyOrRegenerate(
            diagram,
            primary,
            byDialect,
            MultiTargetPortableFixtureDefinition.Options,
            MultiTargetPortableFixtureDefinition.OutputFileName,
            "コミット済みマルチターゲットフィクスチャが現在のテンプレート出力と乖離しています。"
                + "MultiTargetPortableFixtureDefinition（sqlserver / sqlite の自作 Repository・EF なし）から再生成が必要です。"
        );
    }
}
