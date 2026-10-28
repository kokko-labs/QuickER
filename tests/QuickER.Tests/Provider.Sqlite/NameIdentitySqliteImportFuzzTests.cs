using Microsoft.Data.Sqlite;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider.Sqlite;

/// <summary>
/// SQLite の DB 取込（<see cref="SqliteSchemaImporter"/>）へ名前の総当たり（<see cref="NameFuzz"/>）を流し、重複の判定が
/// <see cref="NameIdentityRule"/> と同じ答えになることを確かめるテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 名前の組でテーブル（列）を実際に作り、取り込む。SQLite は ASCII の英字しか畳まず前後の空白も区別するので、
/// 規則が「同じ名前」と言う組（<c>Äpfel</c> と <c>äpfel</c>・<c> Orders</c> と <c>Orders</c>）が DB では共存できる。
/// 取込はそうした組を 1 つにして <see cref="SchemaImportWarningKind.TableNameCollision"/> で告げ、別の名前の組は
/// 2 つとも書いた名前のまま取り込むことを表明する（DB 自身が作成を断る組は、DB が 1 つしか持てないので流さない）。
/// </para>
/// <para>結果は既知の失敗の一覧（<see cref="NameFuzzKnownFailures"/>）と厳密に照合する。</para>
/// </remarks>
public sealed class NameIdentitySqliteImportFuzzTests
{
    [Fact(DisplayName = "SQLite の DB 取込の重複の判定は名前の規則と同じ答えになる")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public void Import_AgreesWithNameRule()
    {
        var ledger = new FuzzFailureLog(NameFuzzKnownFailures.Set);

        NameFuzzRunner.Run(
            ledger,
            "db-import:sqlite:table",
            NameKind.Table,
            name => ImportTables(name).Accepted,
            pair => ImportTables(pair.First, pair.Second).Outcome
        );
        NameFuzzRunner.Run(
            ledger,
            "db-import:sqlite:column",
            NameKind.Column,
            name => ImportColumns(name).Accepted,
            pair => ImportColumns(pair.First, pair.Second).Outcome
        );

        ledger.AssertMatchesKnown("db-import:sqlite:");
    }

    /// <summary>名前ごとにテーブルを作って取り込む</summary>
    private static (bool Accepted, PairOutcome Outcome) ImportTables(params string[] names) =>
        Import(
            names.Select(name =>
                $"CREATE TABLE {SqliteIdentifier.QuoteSimple(name)} (Id INTEGER PRIMARY KEY);"
            ),
            names.Select(name => name.Trim()).ToList(),
            result => result.Entities.Select(entity => entity.TableName).ToList(),
            SchemaImportWarningKind.TableNameCollision
        );

    /// <summary>テーブル T に名前ごとの列を作って取り込む</summary>
    private static (bool Accepted, PairOutcome Outcome) ImportColumns(params string[] names) =>
        Import(
            [
                "CREATE TABLE T ("
                    + string.Join(
                        ", ",
                        names.Select(n => SqliteIdentifier.QuoteSimple(n) + " INTEGER")
                    )
                    + ");",
            ],
            names,
            result =>
                result.Entities.SelectMany(entity => entity.Columns).Select(c => c.Name).ToList(),
            SchemaImportWarningKind.ColumnNameCollision
        );

    /// <summary>
    /// DDL を流して取り込み、取り込んだ名前が書いた名前のとおりか（組なら 2 つめを別の要素として受け付けたか）を返す
    /// </summary>
    private static (bool Accepted, PairOutcome Outcome) Import(
        IEnumerable<string> statements,
        IReadOnlyList<string> expectedNames,
        Func<SchemaImportResult, List<string>> namesOf,
        SchemaImportWarningKind? collisionWarning
    )
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        try
        {
            foreach (var statement in statements)
            {
                using var command = connection.CreateCommand();
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }
        }
        catch (SqliteException ex)
        {
            // DB 自身が 2 つめを断った（DB は 1 つしか持てない）
            return (false, PairOutcome.Of(false, "DB refused: " + ex.Message));
        }

        var result = new SqliteSchemaImporter()
            .ImportAsync(connection, TestContext.Current.CancellationToken)
            .GetAwaiter()
            .GetResult();
        var names = namesOf(result);
        var detail =
            "["
            + string.Join(", ", names.Select(NameFuzz.Describe))
            + "] warnings: "
            + string.Join(", ", result.Warnings.Select(w => w.Kind));

        var (accepted, judged) = NameFuzzRunner.JudgeImported(names, expectedNames, detail);

        // 1 つにまとめたなら、まとめたことを告げる
        var collisionNotWarned =
            !accepted
            && expectedNames.Count == 2
            && names.Count == 1
            && collisionWarning is { } kind
            && result.Warnings.All(warning => warning.Kind != kind);

        return (
            accepted,
            judged with
            {
                Violations =
                [
                    .. judged.Violations,
                    .. collisionNotWarned ? ["collision-not-warned"] : Array.Empty<string>(),
                ],
            }
        );
    }
}
