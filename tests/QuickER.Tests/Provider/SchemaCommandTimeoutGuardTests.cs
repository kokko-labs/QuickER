using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Provider;

/// <summary>
/// スキーマ取込・スキーマ同期のコマンド生成が、必ず <c>DbCommands.Create</c> を通ることを構造的に固定する。
/// </summary>
/// <remarks>
/// <para>
/// コマンド生成箇所は 5 方言 × 取込 3〜7 箇所 ＋ 同期 1〜3 箇所に散在する。個々の生成箇所で
/// <c>CommandTimeout</c> を書く運用では、クエリを 1 本足したときに設定が静かに漏れる
/// （実際、SQLite の同期実行器だけ 3 箇所とも未設定＝ADO 既定 30 秒のままだった）。
/// </para>
/// <para>
/// そこで「素のコマンド生成（<c>connection.CreateCommand()</c> / <c>new XxxCommand(...)</c>）を
/// これらのファイルに書かない」を検査する。ビルドでも型検査でも出ない性質のため、テストで守る。
/// </para>
/// </remarks>
public class SchemaCommandTimeoutGuardTests
{
    /// <summary>検査対象（5 方言の取込＋同期実行器）</summary>
    private static readonly string[] TargetFiles =
    [
        @"src\QuickER.SqlServer\SqlServerSchemaImporter.cs",
        @"src\QuickER.SqlServer\SqlServerSchemaSyncExecutor.cs",
        @"src\QuickER.PostgreSql\PostgreSqlSchemaImporter.cs",
        @"src\QuickER.PostgreSql\PostgreSqlSchemaSyncExecutor.cs",
        @"src\QuickER.MySql\MySqlSchemaImporter.cs",
        @"src\QuickER.MySql\MySqlSchemaSyncExecutor.cs",
        @"src\QuickER.Oracle\OracleSchemaImporter.cs",
        @"src\QuickER.Oracle\OracleSchemaSyncExecutor.cs",
        @"src\QuickER.Sqlite\SqliteSchemaImporter.cs",
        @"src\QuickER.Sqlite\SqliteSchemaSyncExecutor.cs",
    ];

    /// <summary>ヘルパーを迂回するコマンド生成（このいずれかが現れたら失敗）</summary>
    private static readonly Regex BypassPattern = new(
        @"\.CreateCommand\(\)|new\s+(?:Sql|Npgsql|MySql|Oracle|Sqlite)Command\s*\(",
        RegexOptions.Compiled
    );

    /// <summary>素のコマンド生成が 1 箇所も残っていないこと</summary>
    [Fact(DisplayName = "取込・同期のコマンド生成は DbCommands.Create のみを使う")]
    public void SchemaCommands_AreAlwaysCreatedThroughHelper()
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
                "コマンドは DbCommands.Create で作ること（素の生成はコマンドタイムアウトの設定漏れを生む）"
            );
    }

    /// <summary>全対象ファイルがヘルパーを実際に使っていること（対象の付け替えで空検査にならないようにする）</summary>
    [Fact(DisplayName = "取込・同期の全ファイルが DbCommands.Create を使っている")]
    public void SchemaCommands_UseHelperInEveryFile()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();

        var missing = TargetFiles
            .Where(relative =>
                !File.ReadAllText(Path.Combine(root, relative))
                    .Contains("DbCommands.Create", StringComparison.Ordinal)
            )
            .ToList();

        missing.Should().BeEmpty("全方言の取込・同期がコマンド生成ヘルパーを通ること");
    }
}
