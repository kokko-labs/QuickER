using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.GeneratedSqliteFixture;

/// <summary>
/// コミット済みの SQLite 方言フィクスチャ <c>SqlitePortableFixture.g.cs</c> が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// <para>
/// 入力の図は方言可搬フィクスチャと同一だが、オプションが SQLite 方言の自作 Repository＋EF Core 併存
/// （<see cref="SqlitePortableFixtureDefinition.Options"/>）である点が異なる。SQLite 方言ランタイム
/// （<c>SqliteRepository</c>・<c>IncludeLoader</c>・LIMIT/OFFSET・strftime）のテキストがテンプレート変更で
/// 乖離していないことを守る。
/// </para>
/// <para>
/// 検証・再生成の実処理は既存 2 フィクスチャと同じ <see cref="FixtureDriftHarness"/> に集約している。
/// テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </para>
/// </remarks>
public sealed class SqlitePortableFixtureDriftTests
{
    /// <summary>
    /// 単一ソースの図・オプション（SQLite 方言）から再生成した内容が、
    /// コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済み SQLite フィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedSqliteFixture_MatchesRegeneratedOutput()
    {
        FixtureDriftHarness.VerifyOrRegenerate(
            SqlitePortableFixtureDefinition.Build(),
            SqlitePortableFixtureDefinition.Options,
            SqlitePortableFixtureDefinition.OutputFileName,
            "コミット済み SQLite フィクスチャが現在のテンプレート出力と乖離しています。"
                + "SqlitePortableFixtureDefinition（SQLite 方言・自作 Repository＋EF Core）から再生成が必要です。"
        );
    }
}
