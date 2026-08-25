using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Provider;

/// <summary>
/// スキーマ同期実行器のロールバックが、必ず <c>DbTransactions.RollbackQuietlyAsync</c> を通ることを構造的に固定する。
/// </summary>
/// <remarks>
/// <para>
/// 同期実行器の catch 節が素の <c>RollbackAsync(ct)</c> を呼ぶと、(1) キャンセル済みトークンが後始末そのものを
/// 中断する、(2) 完了済みトランザクションへの Rollback が投げる <c>InvalidOperationException</c> が伝播中の
/// 元の例外を置き換える、の 2 つの形で「後始末が本来の失敗原因を壊す」。生成コード側は
/// <c>SqlTransactions.RollbackQuietlyAsync</c>（常に <c>CancellationToken.None</c>）で同じ問題を解いており、
/// アプリ側の Provider 層だけが素の呼び出しへ戻ると非対称が静かに復活する。ビルドでも型検査でも出ない性質のため、
/// テストで守る（<c>DbCommands.Create</c> を守る <see cref="SchemaCommandTimeoutGuardTests"/> と同じ流儀）。
/// </para>
/// </remarks>
public class SchemaSyncRollbackGuardTests
{
    /// <summary>素のロールバックを禁止する検査対象（5 方言の同期実行器）</summary>
    /// <remarks>
    /// MySQL / Oracle は DDL が暗黙コミットされるためトランザクションを持たないが、
    /// 将来トランザクションを導入したときに素の呼び出しで始めないよう禁止検査には含める。
    /// </remarks>
    private static readonly string[] TargetFiles =
    [
        @"src\QuickER.SqlServer\SqlServerSchemaSyncExecutor.cs",
        @"src\QuickER.PostgreSql\PostgreSqlSchemaSyncExecutor.cs",
        @"src\QuickER.MySql\MySqlSchemaSyncExecutor.cs",
        @"src\QuickER.Oracle\OracleSchemaSyncExecutor.cs",
        @"src\QuickER.Sqlite\SqliteSchemaSyncExecutor.cs",
    ];

    /// <summary>トランザクションを実際に持つ実行器（共有ヘルパーの使用を必須にする）</summary>
    private static readonly string[] TransactionalFiles =
    [
        @"src\QuickER.SqlServer\SqlServerSchemaSyncExecutor.cs",
        @"src\QuickER.PostgreSql\PostgreSqlSchemaSyncExecutor.cs",
        @"src\QuickER.Sqlite\SqliteSchemaSyncExecutor.cs",
    ];

    /// <summary>ヘルパーを迂回する素のロールバック（現れたら失敗。<c>RollbackQuietlyAsync</c> は名前が異なるため一致しない）</summary>
    private static readonly Regex BypassPattern = new(@"\.RollbackAsync\(", RegexOptions.Compiled);

    /// <summary>素のロールバック呼び出しが 1 箇所も残っていないこと</summary>
    [Fact(
        DisplayName = "同期実行器のロールバックは DbTransactions.RollbackQuietlyAsync のみを使う"
    )]
    public void SyncExecutors_NeverCallRawRollback()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();
        var offenders = new List<string>();

        foreach (var relative in TargetFiles)
        {
            var path = Path.Combine(root, relative);
            File.Exists(path).Should().BeTrue($"検査対象 {relative} が存在すること");

            var lines = File.ReadAllLines(path);

            for (var i = 0; i < lines.Length; i++)
            {
                if (BypassPattern.IsMatch(lines[i]))
                {
                    offenders.Add($"{relative}({i + 1}): {lines[i].Trim()}");
                }
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "ロールバックは DbTransactions.RollbackQuietlyAsync で行うこと"
                    + "（素の RollbackAsync(ct) はキャンセルで後始末が中断し、完了済みへの Rollback が元の例外を置き換える）"
            );
    }

    /// <summary>トランザクションを持つ全実行器がヘルパーを実際に使っていること（対象の付け替えで空検査にならないようにする）</summary>
    [Fact(
        DisplayName = "トランザクションを持つ同期実行器は全て DbTransactions.RollbackQuietlyAsync を使っている"
    )]
    public void SyncExecutors_UseHelperInEveryTransactionalFile()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();

        var missing = TransactionalFiles
            .Where(relative =>
                !File.ReadAllText(Path.Combine(root, relative))
                    .Contains("DbTransactions.RollbackQuietlyAsync", StringComparison.Ordinal)
            )
            .ToList();

        missing.Should().BeEmpty("トランザクションを持つ同期実行器が後始末ヘルパーを通ること");
    }
}
