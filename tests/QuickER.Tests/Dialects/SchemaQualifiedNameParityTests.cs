using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using SqliteRuntime = QuickER.Tests.GeneratedSqliteFixture.EntitySaveMetadata;
using SqlServerRuntime = QuickER.Tests.GeneratedFixture.EntitySaveMetadata;

namespace QuickER.Tests.Dialects;

/// <summary>
/// テーブル名を最初の <c>.</c> で分ける規則（<see cref="SchemaQualifiedName"/>）と、その写しを持つ生成ランタイムの
/// <c>EntitySaveMetadata.QuoteTableName</c> が、多ドット・空の部分を含む名前で同じ分け方をすることを固定する
/// </summary>
/// <remarks>
/// 生成ランタイム（テンプレートとランタイムのパッケージ）は依存ゼロで QuickER.Dialects を参照できないので、規則の写しを持つ。
/// DDL・同期（方言の識別子クラス）と生成コードの CRUD が別々の分け方をすると、同じ図の表を別々の表として指す。
/// 生成ランタイムは固定フィクスチャのもの（SQL Server・SQLite）を使う（パッケージ用のソースとはドリフトのテストで一致する）。
/// </remarks>
public sealed class SchemaQualifiedNameParityTests
{
    /// <summary>分け方が食い違いやすい名前（多ドット・空の部分・終端の引用符を含む部分）</summary>
    public static TheoryData<string> Names =>
        ["orders", "sales.orders", "a.b.c", ".a", "a.", "..", "...", "", "x]y.z]w", "x\"y.z\"w"];

    [Theory(DisplayName = "Split は最初の . で分け、2 つ目以降の . と空の部分はそのまま残す")]
    [InlineData("orders", null, "orders")]
    [InlineData("sales.orders", "sales", "orders")]
    [InlineData("a.b.c", "a", "b.c")]
    [InlineData(".a", "", "a")]
    [InlineData("a.", "a", "")]
    [InlineData("..", "", ".")]
    [InlineData("", null, "")]
    public void Split_UsesTheFirstDot(string tableName, string? schema, string name)
    {
        SchemaQualifiedName.Split(tableName).Should().Be((schema, name));
        SchemaQualifiedName.IsQualified(tableName).Should().Be(schema is not null);
        SchemaQualifiedName
            .TrySplit(tableName, out var trySchema, out var tryName)
            .Should()
            .Be(schema is not null);
        (trySchema, tryName).Should().Be((schema, name));
    }

    [Theory(DisplayName = "SQL Server の生成ランタイムは DDL と同じ分け方でテーブル名を引用する")]
    [MemberData(nameof(Names))]
    public void SqlServerRuntime_QuotesLikeTheDialect(string tableName)
    {
        SqlServerRuntime.QuoteTableName(tableName).Should().Be(SqlIdentifier.Bracket(tableName));
    }

    [Theory(DisplayName = "SQLite の生成ランタイムは DDL と同じ分け方でテーブル名を引用する")]
    [MemberData(nameof(Names))]
    public void SqliteRuntime_QuotesLikeTheDialect(string tableName)
    {
        SqliteRuntime.QuoteTableName(tableName).Should().Be(SqliteIdentifier.Quote(tableName));
    }
}
