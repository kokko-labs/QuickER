using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedSyncFixture;

/// <summary>
/// コミット済みフィクスチャ <c>SyncFixture.g.cs</c> が、現在のテンプレート・型解決から再生成したコードと
/// 文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 同期支援（<c>GenerateSyncSupport</c>）× マルチターゲット（<c>["sqlserver","sqlite"]</c>）×
/// リモートサービス（<c>GenerateRemoteServices</c>）× 親子 2 テーブルの生成物を固定する。守る対象は
/// 「同期記述子の SQL が方言ごとに正しくクォートされること」「ジャーナル記録デコレータが全書き込み入口を
/// 覆うこと」「DI が FK 順（親→子）でテーブルを登録すること」「同期専用エンドポイントが RemoteServer 側へ
/// 張られること」のテキスト。
/// 再生成手順は <see cref="FixtureDriftHarness"/> の docstring と失敗メッセージを参照。
/// </remarks>
public sealed class SyncFixtureDriftTests
{
    /// <summary>単一ソースの図・オプションから再生成した内容がコミット済みフィクスチャと完全一致する</summary>
    [Fact(
        DisplayName = "コミット済み同期支援フィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedSyncFixture_MatchesRegeneratedOutput()
    {
        var diagram = SyncFixtureDefinition.Build();
        var (primary, byDialect) = SyncFixtureDefinition.ResolveColumnTypes(diagram);

        FixtureDriftHarness.VerifyOrRegenerate(
            diagram,
            primary,
            byDialect,
            SyncFixtureDefinition.Options,
            SyncFixtureDefinition.OutputFileNames,
            "コミット済み同期支援フィクスチャが現在のテンプレート出力と乖離しています。"
                + "SyncFixtureDefinition（同期支援 × sqlserver / sqlite のQuickER 版 Repository ＋ リモートサービス）"
                + "から再生成が必要です。"
        );
    }
}
