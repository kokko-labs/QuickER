using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedSyncModelFixture;

/// <summary>
/// コミット済みフィクスチャ <c>SyncModelFixture.g.cs</c>（＋<c>.RemoteServer.g.cs</c>）が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 同期エンジンのモデルベーステストが使う図（自己参照・版なしの表への FK・一意制約・NOT NULL の除外 blob・日時と文字列の
/// 主キー）の生成物を固定する。再生成手順は <see cref="FixtureDriftHarness"/> の docstring と失敗メッセージを参照。
/// </remarks>
public sealed class SyncModelFixtureDriftTests
{
    /// <summary>単一ソースの図・オプションから再生成した内容がコミット済みフィクスチャと完全一致する</summary>
    [Fact(
        DisplayName = "コミット済み同期モデルフィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedSyncModelFixture_MatchesRegeneratedOutput()
    {
        var diagram = SyncModelFixtureDefinition.Build();
        var (primary, byDialect) = SyncModelFixtureDefinition.ResolveColumnTypes(diagram);

        FixtureDriftHarness.VerifyOrRegenerate(
            diagram,
            primary,
            byDialect,
            SyncModelFixtureDefinition.Options,
            SyncModelFixtureDefinition.OutputFileNames,
            "コミット済み同期モデルフィクスチャが現在のテンプレート出力と乖離しています。"
                + "SyncModelFixtureDefinition（同期支援 × sqlserver / sqlite ＋ リモートサービス）から再生成が必要です。"
        );
    }
}
