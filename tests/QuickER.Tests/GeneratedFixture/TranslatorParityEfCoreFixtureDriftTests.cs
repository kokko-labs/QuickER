using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedTranslatorParityEfCoreFixture;

/// <summary>
/// コミット済みフィクスチャ <c>TranslatorParityEfCoreFixture.g.cs</c> が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 述語の表を SQL Server の EF Core 版で流すための生成物を固定する。
/// 再生成手順は <see cref="FixtureDriftHarness"/> の docstring と失敗メッセージを参照。
/// </remarks>
public sealed class TranslatorParityEfCoreFixtureDriftTests
{
    /// <summary>
    /// 単一ソースの図・オプション（SQL Server の EF Core 版 Repository 単独）から再生成した内容が、
    /// コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済み翻訳器パリティ EF Core フィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedTranslatorParityEfCoreFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            TranslatorParityEfCoreFixtureDefinition.Build(),
            TranslatorParityEfCoreFixtureDefinition.Options,
            TranslatorParityEfCoreFixtureDefinition.OutputFileName,
            "コミット済み翻訳器パリティ EF Core フィクスチャが現在のテンプレート出力と乖離しています。"
                + "TranslatorParityEfCoreFixtureDefinition（SQL Server の EF Core 版 Repository 単独）から再生成が必要です。"
        );
    }
}
