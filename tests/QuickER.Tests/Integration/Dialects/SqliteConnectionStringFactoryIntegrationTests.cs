using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// D: <see cref="SqliteProvider.BuildConnectionString"/>（<see cref="SqliteConnectionStringFactory.Build"/>）で
/// 共通接続設定から組み立てた接続文字列の振る舞いを検証する統合テスト。
/// </summary>
/// <remarks>
/// SQLite はインプロセスのため Docker / Testcontainers を使わず、CI でも常時実行される。
/// 取込専用（<c>Mode=ReadOnly</c>）の設計意図（誤パス時に空の DB を自動生成しない）をガードする。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqliteConnectionStringFactoryIntegrationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// 実在する DB ファイルへ <see cref="SqliteProvider.BuildConnectionString"/> の接続文字列で接続し、
    /// <see cref="SqliteSchemaImporter.ImportAsync(string, CancellationToken)"/> が成功して取込できることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] D: 接続文字列ファクトリの出力で実在 DB へ接続し取込が成功する"
    )]
    public async Task Build_ConnectsToExistingFileAndImports()
    {
        using var db = SqliteTempDatabase.Create();

        // 1 テーブルだけの実 DB を用意する（書き込み可能モードで作成）
        var diagram = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "widgets",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "INT",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "name",
                            DataType = "NVARCHAR(50)",
                            IsNullable = false,
                        },
                    },
                },
            },
        };
        await db.ApplyDdlAsync(diagram, Ct);

        // 共通接続設定（FilePath のみ）→ プロバイダのファクトリで接続文字列を構築する
        var settings = new DbConnectionSettings { FilePath = db.FilePath };
        var connectionString = new SqliteProvider().BuildConnectionString(settings);

        // 取込専用の接続文字列で取込が成功すること
        var result = await new SqliteSchemaImporter().ImportAsync(connectionString, Ct);

        result
            .Entities.Select(e => e.TableName)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("widgets");
        result.Entities.Single().Columns.Select(c => c.Name).Should().BeEquivalentTo("id", "name");
    }

    /// <summary>
    /// 存在しないパスに対しては接続が失敗し、かつ空の DB ファイルが生成されないことを検証する
    /// （取込専用 <c>Mode=ReadOnly</c> の設計意図を守るガードテスト）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] D: 存在しないパスは接続失敗し空 DB を生成しない（ReadOnly ガード）"
    )]
    public async Task Build_NonexistentPath_FailsAndDoesNotCreateFile()
    {
        // まだ存在しないパスを用意する（一時ディレクトリは作るがファイルは作らない）
        using var db = SqliteTempDatabase.Create();
        File.Exists(db.FilePath).Should().BeFalse("前提: この時点で DB ファイルは存在しない");

        var settings = new DbConnectionSettings { FilePath = db.FilePath };
        var connectionString = new SqliteProvider().BuildConnectionString(settings);

        // ReadOnly のため、存在しないファイルへの接続は開いた時点で失敗する
        Func<Task> act = async () =>
        {
            await using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(Ct);
        };

        await act.Should().ThrowAsync<SqliteException>("ReadOnly は存在しないファイルを開けない");

        // 接続失敗により空の DB ファイルが自動生成されていないこと（設計意図の要）
        File.Exists(db.FilePath)
            .Should()
            .BeFalse("接続失敗時に空の DB ファイルを生成してはならない");
    }
}
