using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedMultiTargetRowVersionFixture;

/// <summary>
/// コミット済みフィクスチャ <c>MultiTargetRowVersionFixture.g.cs</c> が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// rowversion 列 × マルチターゲット（<c>RepositoryDialects=["sqlserver","sqlite"]</c>）の生成物を固定する。
/// 守る対象は「共有 Entity の <c>byte[]</c> への型統一」と「SQL Server 実装だけが版ガード SQL を持ち、
/// SQLite 実装は通常のバイナリ列として書き込む」という方言間の非対称のテキスト。
/// 再生成手順は <see cref="FixtureDriftHarness"/> の docstring と失敗メッセージを参照。
/// </remarks>
public sealed class MultiTargetRowVersionFixtureDriftTests
{
    /// <summary>
    /// 単一ソースの図・オプション（rowversion × マルチターゲット）から再生成した内容が、
    /// コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済み rowversion マルチターゲットフィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedMultiTargetRowVersionFixture_MatchesRegeneratedOutput()
    {
        var diagram = MultiTargetRowVersionFixtureDefinition.Build();
        var (primary, byDialect) = MultiTargetRowVersionFixtureDefinition.ResolveColumnTypes(
            diagram
        );

        FixtureDriftHarness.VerifyOrRegenerate(
            diagram,
            primary,
            byDialect,
            MultiTargetRowVersionFixtureDefinition.Options,
            MultiTargetRowVersionFixtureDefinition.OutputFileName,
            "コミット済み rowversion マルチターゲットフィクスチャが現在のテンプレート出力と乖離しています。"
                + "MultiTargetRowVersionFixtureDefinition（rowversion 列 × sqlserver / sqlite のQuickER 版 Repository）から再生成が必要です。"
        );
    }
}
