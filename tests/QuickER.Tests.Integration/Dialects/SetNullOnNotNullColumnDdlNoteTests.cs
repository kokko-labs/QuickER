using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// NOT NULL の子の列への SET NULL の外部キーを作れる方言（PostgreSQL・Oracle・SQLite）の DDL が、外部キーの前に注記することを
/// 確かめる（DDL の文字列だけ・DB を使わない）。DDL を当てて親の削除が失敗することは、同期計画の期待の行
/// <c>realdb-set-null-not-null-child-ddl-applies-delete-fails</c>（<see cref="SyncPlanExpectationRows"/>）が実 DB で確かめる。
/// </summary>
[Trait("Category", "Integration")]
public sealed class SetNullOnNotNullColumnDdlNoteTests
{
    [Theory(
        DisplayName = "[Integration] NOT NULL の子の列への SET NULL の外部キーを作れる方言の DDL は、外部キーの前に注記する"
    )]
    [InlineData(DialectNames.PostgreSql, "integer")]
    [InlineData(DialectNames.Oracle, "NUMBER(10)")]
    [InlineData(DialectNames.Sqlite, "INTEGER")]
    public void SetNullOnNotNullColumn_DdlHasNote(string dialect, string keyType)
    {
        IDdlGenerator generator = dialect switch
        {
            DialectNames.PostgreSql => new PostgreSqlDdlGenerator(),
            DialectNames.Oracle => new OracleDdlGenerator(),
            _ => new SqliteDdlGenerator(),
        };

        generator
            .Build(SetNullOnNotNullColumnSchema.Build(keyType))
            .Should()
            .Contain(SetNullOnNotNullColumnSchema.Note);
    }
}
