using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedQueryFixture;

/// <summary>
/// コミット済みの名前付きクエリ入りフィクスチャ <c>QueryFixture.g.cs</c> が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 検証・再生成の実処理は既存フィクスチャと同じ <see cref="FixtureDriftHarness"/> に集約している
/// （名前付きクエリの型トークン解決もハーネスが実生成経路と同じく行う）。
/// テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </remarks>
public sealed class QueryFixtureDriftTests
{
    /// <summary>単一ソースの図・オプションから再生成した内容が、コミット済みフィクスチャと完全一致することを検証する</summary>
    [Fact(
        DisplayName = "コミット済みクエリフィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedQueryFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            QueryFixtureDefinition.Build(),
            QueryFixtureDefinition.Options,
            QueryFixtureDefinition.OutputFileName,
            "コミット済みクエリフィクスチャが現在のテンプレート出力と乖離しています。"
                + "QueryFixtureDefinition（SQLite 方言・自作 Repository＋EF Core・名前付きクエリ入り）から再生成が必要です。"
        );
    }
}
