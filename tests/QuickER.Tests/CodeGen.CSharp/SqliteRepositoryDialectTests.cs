using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// QuickER 版 Repository の SQLite 方言生成（<see cref="CodeGenerationOptions.RepositoryDialects"/> = ["sqlite"]）と SQL Server 方言の生成が、
/// 互いの依存（Microsoft.Data.Sqlite / Microsoft.Data.SqlClient・FOR JSON・SqlDbType・SqlServerRepository）を含まないことの検証。
/// </summary>
/// <remarks>
/// 型・参照・属性・依存の不在・SQLite での実行は、決まった行（<c>CodegenCaseTests</c>）の <c>*-sqlite-dialect-*</c> /
/// <c>*-sqlserver-dialect-*</c> へ移した。ここには、生成器の固定の英文（<c>ExecuteSqlAsync</c> の XmlDoc）と、
/// SQL Server の SQL の断片（FOR JSON・OFFSET-FETCH・OUTPUT INSERTED の有無）だけを残す。
/// </remarks>
public class SqliteRepositoryDialectTests
{
    /// <summary>
    /// ExecuteSqlAsync の説明は、SQLite の利用者に T-SQL の <c>BEGIN TRAN ... COMMIT</c> を勧めない
    /// （SQLite では <c>near "TRAN": syntax error</c>）。1 回の呼び出しの <c>BEGIN; ... COMMIT;</c> も勧めず（文が失敗すると、開いた
    /// トランザクションのまま接続がプールへ戻る）、SQLite の実行器が実装する <c>ISqlBatchExecutor</c> を勧める
    /// </summary>
    // 残す理由（P3-4b）：XmlDoc の固定の英文（生成器の固定の英文）
    [Fact(
        DisplayName = "SQLite の ExecuteSqlAsync の説明は複数の文の原子的な実行に ISqlBatchExecutor を勧める"
    )]
    public void SqliteDialect_ExecuteSqlDoc_RecommendsBatchExecutor()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SampleDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlite"],
            }
        );
        result.HasErrors.Should().BeFalse();
        var code = string.Join("\n", result.Files.Select(file => file.Content));

        code.Should().NotContain("write <c>BEGIN TRAN ... COMMIT</c>");
        code.Should()
            .NotContain("write <c>BEGIN; ... COMMIT;</c> in the SQL within a single call.");
        code.Should()
            .Contain(
                "use <see cref=\"ISqlBatchExecutor\"/>, which the <see cref=\"SqlExecutor\"/> behind this method implements"
            );
    }

    /// <summary>
    /// SQLite 方言（VO の有無の両方）の生成物に、SQL Server の SQL の構文（FOR JSON・OFFSET-FETCH・OUTPUT INSERTED）が現れない
    /// （動かさない経路に残っても見落とさないよう、出力の全体で見る）
    /// </summary>
    // 残す理由（P3-4b）：SQL Server の SQL の断片の不在。構造の表明（SQL Server の型・属性・参照の不在）は行 code-sqlite-dialect-* と
    // 参照を絞ったコンパイルへ、SQLite で動くことは行 exec-sqlite-dialect-* へ移した
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SqliteDialect_ShouldNotEmitSqlServerSqlSyntax(bool valueObjects)
    {
        var result = new CSharpCodeGenerationService().Generate(
            SampleDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlite"],
                GenerateValueObjects = valueObjects,
            }
        );
        result.HasErrors.Should().BeFalse();
        var code = string.Join("\n", result.Files.Select(file => file.Content));

        code.Should().NotContain("FOR JSON");

        if (!valueObjects)
        {
            code.Should().NotContain("FETCH NEXT");
            code.Should().NotContain("OUTPUT INSERTED");
        }
    }

    /// <summary>SQL Server 方言（既定）の生成物に SQLite 依存が漏れないことを検証する（逆方向の排他）</summary>
    // 残す理由（P3-4b）：SQL Server の SQL の断片（FOR JSON・OUTPUT INSERTED）。構造の表明（型・参照・DDL 適用ヘルパー・SQLite 依存の不在）は行 code-sqlserver-dialect-has-no-sqlite-dependencies へ移した
    [Fact]
    public void SqlServerDialect_ShouldNotEmitSqliteDependencies()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SampleDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );
        result.HasErrors.Should().BeFalse();
        var code = string.Join("\n", result.Files.Select(file => file.Content));

        // SQL Server 版は FOR JSON を維持
        code.Should().Contain("FOR JSON");
        // 楽観排他の版返却句（rowversion 列の有無に依らず固定 infra として存在する）
        code.Should().Contain("OUTPUT INSERTED.");
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
                    ColumnPairs = [new(customerPk, orderCustomerFk)],
                },
            ],
        };
    }
}
