using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成コードの <c>[UniqueConstraint(…, Name = …)]</c> に刻む合成名が、DDL の一意制約名と一致することを検証するテストクラス
/// </summary>
/// <remarks>
/// 生成側は素のテーブル名から合成名を作っていたため、<c>dbo.Orders</c> のようなスキーマ付きの名前では
/// DDL（安全化してから合成）と名前が割れていた。しかも C# リバースは生成コードの <c>Name</c> を明示の制約名として
/// 復元するので、往復すると名前なしだった制約が割れた実名を持ってしまう。
/// 合成名は <see cref="UniqueConstraint.SynthesizeName"/> 自身が安全化する。
/// </remarks>
public class UniqueConstraintNameGenerationTests
{
    /// <summary>
    /// 行は 2 つ＝ぶつからない図（安全化した合成名）と、ほかの表の名前を持つ一意制約が合成名と同じ図（DDL も生成コードも
    /// 図全体で決めた番号つきの名前）
    /// </summary>
    [Theory(DisplayName = "スキーマ付きのテーブル名で、生成コードの一意制約名が DDL と一致する")]
    [InlineData(false, "UQ_dbo_Orders_code")]
    [InlineData(true, "UQ_dbo_Orders_code_2")]
    public void GeneratedUniqueConstraintName_MatchesDdl(bool collides, string expected)
    {
        var entity = new Entity
        {
            TableName = "dbo.Orders",
            Columns =
            {
                new Column
                {
                    Name = "order_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "code",
                    DataType = "nvarchar(20)",
                    IsNullable = false,
                },
            },
        };
        entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [entity.Columns[1].Id] });
        var diagram = new ErDiagram { Entities = { entity } };

        if (collides)
        {
            var other = new Entity
            {
                TableName = "dbo.Other",
                Columns =
                {
                    new Column
                    {
                        Name = "other_id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "label",
                        DataType = "nvarchar(20)",
                        IsNullable = false,
                    },
                },
            };
            other.UniqueConstraints.Add(
                new UniqueConstraint
                {
                    Name = "UQ_dbo_Orders_code",
                    ColumnIds = [other.Columns[1].Id],
                }
            );
            diagram.Entities.Add(other);
        }

        var provider = new SqlServerProvider();
        var generation = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "QuickER.Tests.UniqueConstraintName",
                SplitFilesByCategory = false,
            }
        );
        var ddl = new SqlServerDdlGenerator().Build(diagram);

        generation.HasErrors.Should().BeFalse();
        ddl.Should().Contain($"[{expected}]");
        generation.Files.Single().Content.Should().Contain($"Name = \"{expected}\"");
    }

    [Fact(
        DisplayName = "既定の一意制約名が上限を超える図では、生成コードの一意制約名も図の対象 DBMS の DDL と同じ縮めた名前になる"
    )]
    public void GeneratedUniqueConstraintName_IsShortenedLikeDdl()
    {
        var tableName = new string('t', 60);
        var entity = new Entity
        {
            TableName = tableName,
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "integer",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "code",
                    DataType = "varchar(20)",
                    IsNullable = false,
                },
            },
        };
        entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [entity.Columns[1].Id] });
        var diagram = new ErDiagram { TargetDbms = "postgresql", Entities = { entity } };
        var expected = UniqueConstraint.SynthesizeName(
            tableName,
            ["code"],
            IdentifierLengthLimits.PostgreSql
        );

        var provider = new PostgreSqlProvider();
        var generation = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "QuickER.Tests.UniqueConstraintName",
                SplitFilesByCategory = false,
            }
        );
        var ddl = provider.DdlGenerator.Build(diagram);

        generation.HasErrors.Should().BeFalse();
        expected.Should().NotBe("UQ_" + tableName + "_code", "上限（63 バイト）を超えるので縮む");
        ddl.Should().Contain("\"" + expected + "\"");
        generation.Files.Single().Content.Should().Contain("Name = \"" + expected + "\"");
    }
}
