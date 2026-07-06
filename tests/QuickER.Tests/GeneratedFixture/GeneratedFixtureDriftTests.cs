using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// コミット済みの固定フィクスチャ <c>GeneratedFixture.g.cs</c> が、
/// 現在のテンプレート・型解決から再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// <para>
/// 実行時テスト (<c>GeneratedRuntimeIntegrationTests</c>) はコミット済みフィクスチャの生成型を直接呼ぶ。
/// テンプレートを変更するとフィクスチャが古くなり得るため、このテストで乖離を検出する。
/// </para>
/// <para>
/// 図・オプションは <see cref="GeneratedFixtureDefinition"/>（単一ソース）を共有しており、
/// 検証・再生成の実処理は <see cref="FixtureDriftHarness"/> に集約している。
/// テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </para>
/// </remarks>
public sealed class GeneratedFixtureDriftTests
{
    /// <summary>
    /// 単一ソースの図・オプションから再生成した内容が、コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済みフィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            GeneratedFixtureDefinition.Build(),
            GeneratedFixtureDefinition.Options,
            GeneratedFixtureDefinition.Options.OutputFileName,
            "コミット済みフィクスチャが現在のテンプレート出力と乖離しています。"
                + "テンプレート（QuickER.CodeGen.CSharp/Templates/CSharpRuntime.scriban 等）を変更した場合はフィクスチャの再生成が必要です。"
        );
    }
}
