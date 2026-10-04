using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedTranslatorParityFixture;

/// <summary>
/// コミット済みフィクスチャ <c>TranslatorParityFixture.g.cs</c> が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 式の翻訳・識別子の引用・Include の照合・UPDATE 文の組み立てを 3 つの実装先（SQL Server・SQLite・インメモリ）で
/// 突き合わせるための生成物を固定する。再生成手順は <see cref="FixtureDriftHarness"/> の docstring と失敗メッセージを参照。
/// </remarks>
public sealed class TranslatorParityFixtureDriftTests
{
    /// <summary>
    /// 単一ソースの図・オプション（sqlserver / sqlite のマルチターゲット＋インメモリ）から再生成した内容が、
    /// コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済み翻訳器パリティフィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedTranslatorParityFixture_MatchesRegeneratedOutput()
    {
        var diagram = TranslatorParityFixtureDefinition.Build();
        var (primary, byDialect) = TranslatorParityFixtureDefinition.ResolveColumnTypes(diagram);

        FixtureDriftHarness.VerifyOrRegenerate(
            diagram,
            primary,
            byDialect,
            TranslatorParityFixtureDefinition.Options,
            TranslatorParityFixtureDefinition.OutputFileName,
            "コミット済み翻訳器パリティフィクスチャが現在のテンプレート出力と乖離しています。"
                + "TranslatorParityFixtureDefinition（sqlserver / sqlite のQuickER 版 Repository＋インメモリ）から再生成が必要です。"
        );
    }
}
