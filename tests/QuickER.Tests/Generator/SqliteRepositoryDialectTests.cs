using FluentAssertions;
using QuickER.Generator;
using QuickER.Model;

namespace QuickER.Tests.Generator;

/// <summary>
/// 自作 Repository の SQLite 方言生成（<see cref="CodeGenerationOptions.RepositoryDialect"/> = "sqlite"）が、
/// SQLite 固有の実行経路（プレーン SELECT・LIMIT/OFFSET・マルチクエリ Include・SqliteXxx 型）を出力し、
/// SQL Server 依存（Microsoft.Data.SqlClient / FOR JSON / SqlDbType / SqlServerRepository）を一切含まないことを検証する。
/// </summary>
/// <remarks>
/// 逆方向（sqlserver 生成物に Microsoft.Data.Sqlite が漏れない）も併せて検証し、依存排他を双方向で守る。
/// 実 DB でのランタイム検証は別フェーズ（統合テスト）で行い、ここでは生成テキストの静的検証に絞る。
/// </remarks>
public class SqliteRepositoryDialectTests
{
    /// <summary>SQLite 方言で生成した全ファイルを 1 本の文字列に連結する</summary>
    private static string GenerateSqlite(bool valueObjects = false)
    {
        var result = new CSharpCodeGenerationService().Generate(
            SampleDiagram(),
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                RepositoryDialect = "sqlite",
                GenerateValueObjects = valueObjects,
            }
        );

        result.HasErrors.Should().BeFalse();
        return string.Join("\n", result.Files.Select(file => file.Content));
    }

    /// <summary>SQLite 生成物に SQLite 固有の型・句・エンジンが含まれることを検証する</summary>
    [Fact]
    public void SqliteDialect_ShouldEmitSqliteSpecificRuntime()
    {
        var code = GenerateSqlite();

        code.Should().Contain("using Microsoft.Data.Sqlite;");
        code.Should().Contain("SqliteConnection CreateConnection()");
        code.Should().Contain("new SqliteCommand(");
        code.Should().Contain("SqliteRepository<");
        // ページングは OFFSET-FETCH ではなく LIMIT/OFFSET
        code.Should().Contain("LIMIT ");
        // マルチクエリ Include エンジンと非ジェネリック実体化
        code.Should().Contain("class IncludeLoader");
        code.Should().Contain("MaterializeRootsAsync");
        code.Should().Contain("MapEntityObject");
        // 式木トランスレータの日付部品は strftime へ吐き分け
        code.Should().Contain("strftime(");
    }

    /// <summary>SQLite 生成物に SQL Server 依存（コードトークン）が一切含まれないことを検証する</summary>
    [Fact]
    public void SqliteDialect_ShouldNotEmitSqlServerDependencies()
    {
        var code = GenerateSqlite();

        // ADO / DI の SQL Server クライアント依存（接続ファクトリ名 SqlConnectionFactory は方言中立で保つため
        // "new SqlConnection(" のように開き括弧まで含めて SQL Server ADO 型のインスタンス化のみを検出する）
        code.Should().NotContain("Microsoft.Data.SqlClient");
        code.Should().NotContain("new SqlConnection(");
        code.Should().NotContain("new SqlCommand(");
        code.Should().NotContain("new SqlParameter(");
        code.Should().NotContain("SqlDataReader");
        // FOR JSON 実行経路と JSON プランナ
        code.Should().NotContain("FOR JSON");
        code.Should().NotContain("JsonQueryPlanner");
        // SqlBulkCopy による一括挿入（インスタンス化）
        code.Should().NotContain("new SqlBulkCopy");
        // [SqlColumnType] / SqlDbType（SQL Server 専用の列型メタ）
        code.Should().NotContain("SqlDbType");
        code.Should().NotContain("[SqlColumnType(");
        // SQL Server 版の基底クラス名
        code.Should().NotContain("SqlServerRepository");
        // OFFSET-FETCH ページング構文
        code.Should().NotContain("FETCH NEXT");
    }

    /// <summary>SQLite 方言（VO 有効）でも SQL Server 依存が漏れないことを検証する</summary>
    [Fact]
    public void SqliteDialect_WithValueObjects_ShouldNotEmitSqlServerDependencies()
    {
        var code = GenerateSqlite(valueObjects: true);

        code.Should().Contain("using Microsoft.Data.Sqlite;");
        code.Should().NotContain("Microsoft.Data.SqlClient");
        code.Should().NotContain("SqlDbType");
        code.Should().NotContain("FOR JSON");
        code.Should().NotContain("SqlServerRepository");
    }

    /// <summary>SQL Server 方言（既定）の生成物に SQLite 依存が漏れないことを検証する（逆方向の排他）</summary>
    [Fact]
    public void SqlServerDialect_ShouldNotEmitSqliteDependencies()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SampleDiagram(),
            new CodeGenerationOptions { NamespaceName = "Sample.Domain" }
        );
        result.HasErrors.Should().BeFalse();
        var code = string.Join("\n", result.Files.Select(file => file.Content));

        code.Should().NotContain("Microsoft.Data.Sqlite");
        code.Should().NotContain("SqliteConnection");
        code.Should().NotContain("SqliteRepository");
        code.Should().NotContain("IncludeLoader");
        // SQL Server 版は従来どおり FOR JSON / SqlServerRepository を維持
        code.Should().Contain("SqlServerRepository<");
        code.Should().Contain("FOR JSON");
    }

    /// <summary>親（customers）1 対多 子（orders）の関係を持つ、Include 検証用の最小 ER 図</summary>
    private static ErDiagram SampleDiagram()
    {
        var customer = Guid.NewGuid();
        var customerPk = Guid.NewGuid();
        var order = Guid.NewGuid();
        var orderPk = Guid.NewGuid();
        var orderCustomerFk = Guid.NewGuid();

        return new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = customer,
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerPk,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "created_at",
                            DataType = "datetime2",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = orderPk,
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = orderCustomerFk,
                            Name = "customer_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    SourceColumnId = customerPk,
                    TargetColumnId = orderCustomerFk,
                },
            ],
        };
    }
}
