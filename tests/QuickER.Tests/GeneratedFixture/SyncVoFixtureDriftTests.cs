using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedSyncVoFixture;

/// <summary>
/// コミット済みフィクスチャ <c>SyncVoFixture.g.cs</c> が、現在のテンプレート・型解決から再生成したコードと
/// 文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 同期支援（<c>GenerateSyncSupport</c>）× マルチターゲット（<c>["sqlserver","sqlite"]</c>）×
/// 値オブジェクト（<c>GenerateValueObjects</c>）× 親子 2 テーブルの生成物を固定する。守る対象は
/// 「ミラー版の読み書き・キー整形・キー解析が VO 型を経由する式で出ること」、とりわけ
/// <b>NOT NULL 宣言の行バージョン列でもミラー版の読み出しが <c>?.Value</c> であること</b>のテキスト。
/// 再生成手順は <see cref="FixtureDriftHarness"/> の docstring と失敗メッセージを参照。
/// </remarks>
public sealed class SyncVoFixtureDriftTests
{
    /// <summary>単一ソースの図・オプションから再生成した内容がコミット済みフィクスチャと完全一致する</summary>
    [Fact(
        DisplayName = "コミット済み同期支援 × 値オブジェクトフィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedSyncVoFixture_MatchesRegeneratedOutput()
    {
        var diagram = SyncVoFixtureDefinition.Build();
        var (primary, byDialect) = SyncVoFixtureDefinition.ResolveColumnTypes(diagram);

        FixtureDriftHarness.VerifyOrRegenerate(
            diagram,
            primary,
            byDialect,
            SyncVoFixtureDefinition.Options,
            SyncVoFixtureDefinition.OutputFileName,
            "コミット済み同期支援 × 値オブジェクトフィクスチャが現在のテンプレート出力と乖離しています。"
                + "SyncVoFixtureDefinition（同期支援 × sqlserver / sqlite のQuickER 版 Repository ＋ 値オブジェクト）"
                + "から再生成が必要です。"
        );
    }
}
