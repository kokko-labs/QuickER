using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
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
    [Fact(DisplayName = "スキーマ付きのテーブル名で、生成コードの一意制約名が DDL と一致する")]
    public void GeneratedUniqueConstraintName_MatchesDdl()
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
        ddl.Should().Contain("[UQ_dbo_Orders_code]");
        generation.Files.Single().Content.Should().Contain("Name = \"UQ_dbo_Orders_code\"");
    }
}
