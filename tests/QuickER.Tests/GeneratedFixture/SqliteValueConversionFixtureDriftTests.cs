using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedSqliteValueConversionFixture;

/// <summary>
/// コミット済みフィクスチャ <c>SqliteValueConversionFixture.g.cs</c> が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// SQLite 方言 × 値オブジェクトの生成物を固定する。守る対象は「TEXT で返る非 IConvertible 型
/// （TimeSpan / Guid / DateTimeOffset）を復元する共有変換（<c>RawValueConverter</c>）を通すこと」と、
/// 「行マッピングが VO 列だけを Wrap し、素の列は方言変換へ回すこと」のテキスト。
/// 再生成手順は <see cref="FixtureDriftHarness"/> の docstring と失敗メッセージを参照。
/// </remarks>
public sealed class SqliteValueConversionFixtureDriftTests
{
    /// <summary>
    /// 単一ソースの図・オプション（SQLite 方言 × VO）から再生成した内容が、
    /// コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済み SQLite 値変換フィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedSqliteValueConversionFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            SqliteValueConversionFixtureDefinition.Build(),
            SqliteValueConversionFixtureDefinition.Options,
            SqliteValueConversionFixtureDefinition.OutputFileName,
            "コミット済み SQLite 値変換フィクスチャが現在のテンプレート出力と乖離しています。"
                + "SqliteValueConversionFixtureDefinition（SQLite 方言・QuickER 版 Repository・値オブジェクト）から再生成が必要です。"
        );
    }
}
