using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 生成 SQL の <c>--</c> 行コメントへ載せたユーザーデータが、1 行に収まることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// <c>--</c> は行末までの行コメントなので、改行がそのまま乗るとコメントが途中で終わり、2 行目以降が
/// 実行される SQL として解釈される。この防壁は 3 層で張ってある。
/// </para>
/// <list type="number">
///   <item><b>入口</b>: <c>SqlNameText</c> が名前（テーブル・列・制約）の改行・制御文字を拒否する
///   （方言の DDL の表の行 <c>ddl-*-name-with-newline-is-rejected</c>・<c>sync-*-name-with-newline-is-rejected</c>）。名前は出力へ到達しない</item>
///   <item><b>構造</b>: SourceRuleTests の規則 <c>sql-comment-unsanitized-holes</c> が「<c>--</c> コメントへ埋める式は
///   <see cref="SqlComment.Sanitize"/> を通る」をソース上で全数検査する（埋め込み箇所の漏れを検知する）</item>
///   <item><b>出力</b>: 入口で止めない<b>説明</b>（<see cref="Entity.Description"/> /
///   <see cref="Column.Description"/>）については、実際の生成 SQL でコメント行が 1 行に収まることを方言の DDL の表の行
///   （<c>DialectDdlRows</c> の <c>ddl-description-comment-folded-one-line</c>）が固定する</item>
/// </list>
/// <para>
/// 説明を入口で止めないのは、説明が「DB のコメント欄へそのまま載せる自由記述」であり、改行を含むのが
/// 正当だから（DDL では文字列リテラルとしてエスケープされ、<c>--</c> コメントとしてはここで畳まれる）。
/// </para>
/// </remarks>
public class SqlCommentInjectionTests
{
    /// <summary>コメント行を突き破ろうとする説明（改行の後に実行可能な SQL を置く）</summary>
    private const string Injected = "a\nDROP TABLE users; --";

    /// <summary>サニタイズ後の期待形（改行が空白 1 つへ畳まれる）</summary>
    private const string Sanitized = "a DROP TABLE users; --";

    /// <summary>
    /// 5 方言が共有するスキップコメントのヘルパーが、テーブル名を畳んでから載せることを検証する。
    /// </summary>
    /// <remarks>
    /// 入口の名前検証があるため実際の生成では到達しないが、ヘルパー自体は公開 API で、埋め込み規則の
    /// 正本としてここで固定しておく（入口を将来ゆるめたときに気付けるようにする）。
    /// </remarks>
    [Fact(DisplayName = "一意制約のスキップコメントはテーブル名を 1 行へ畳む")]
    public void UniqueConstraintSkipComment_FoldsNewLines()
    {
        var comment = SyncScriptBuilderHelper.BuildUniqueConstraintSkipComment(
            new SchemaDiffItem { Kind = SchemaDiffKind.AddUniqueConstraint, TableName = Injected }
        );

        comment
            .Should()
            .Be(
                $"-- Skipped 'AddUniqueConstraint' on {Sanitized}: "
                    + "the unique constraint has no resolvable columns."
            );
        comment.Should().NotContain("\n");
    }

    /// <inheritdoc cref="UniqueConstraintSkipComment_FoldsNewLines" />
    [Fact(DisplayName = "外部キーのスキップコメントは親子のテーブル名を 1 行へ畳む")]
    public void ForeignKeySkipComment_FoldsNewLines()
    {
        var comment = SyncScriptBuilderHelper.BuildForeignKeySkipComment(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddForeignKey,
                TableName = Injected,
                ParentEntity = new Entity { TableName = "customer" },
                ChildEntity = new Entity { TableName = Injected },
            }
        );

        comment.Should().Contain($"({Sanitized} -> customer)");
        comment.Should().NotContain("\n");
    }
}
