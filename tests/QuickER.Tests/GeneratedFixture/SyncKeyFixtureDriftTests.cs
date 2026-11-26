using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedSyncKeyFixture;

/// <summary>
/// コミット済みフィクスチャ <c>SyncKeyFixture.g.cs</c> が、現在のテンプレート・型解決から再生成したコードと
/// 文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 同期支援（<c>GenerateSyncSupport</c>）× マルチターゲット（<c>["sqlserver","sqlite"]</c>）× 主キーの型の違う版なしの表
/// 9 つの生成物を固定する。守る対象は版なしの表の記述子のキー順ページングの SQL（<c>ServerPageAfterSql</c>）・
/// キーの整形と解析の式のテキスト。再生成手順は <see cref="FixtureDriftHarness"/> の docstring と失敗メッセージを参照。
/// </remarks>
public sealed class SyncKeyFixtureDriftTests
{
    /// <summary>単一ソースの図・オプションから再生成した内容がコミット済みフィクスチャと完全一致する</summary>
    [Fact(
        DisplayName = "コミット済み同期支援 × 主キーの型の表フィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedSyncKeyFixture_MatchesRegeneratedOutput()
    {
        var diagram = SyncKeyFixtureDefinition.Build();
        var (primary, byDialect) = SyncKeyFixtureDefinition.ResolveColumnTypes(diagram);

        FixtureDriftHarness.VerifyOrRegenerate(
            diagram,
            primary,
            byDialect,
            SyncKeyFixtureDefinition.Options,
            SyncKeyFixtureDefinition.OutputFileNames,
            "コミット済み同期支援 × 主キーの型の表フィクスチャが現在のテンプレート出力と乖離しています。"
                + "SyncKeyFixtureDefinition（同期支援 × sqlserver / sqlite の QuickER 版 Repository・版なしの表 9 つ）"
                + "から再生成が必要です。"
        );
    }
}
