using ERDesigner.Generator;
using FluentAssertions;

namespace ERDesigner.Tests.Generator;

public class CSharpCodeGenerationServiceTests
{
    [Fact]
    public void Generate_ShouldCreateSingleGeneratedFileWithEntityAndBindingModel()
    {
        var customerId = Guid.NewGuid();
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new ColumnDefinition { Id = customerId, Name = "customer_id", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                        new ColumnDefinition { Id = Guid.NewGuid(), Name = "name", DataType = "nvarchar(100)", IsNullable = false },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        result.Files.Should().ContainSingle();
        result.Files[0].FileName.Should().Be("ErDesignerEntities.g.cs");
        result.Files[0].Content.Should().Contain("namespace Sample.Domain;");
        result.Files[0].Content.Should().Contain("public partial class CustomerEntity");
        result.Files[0].Content.Should().Contain("public partial class CustomerBindingModel");
        result.Files[0].Content.Should().Contain("[Table(\"customers\")]");
        result.Files[0].Content.Should().Contain("[Key]");
        result.Files[0].Content.Should().Contain("[MaxLength(100)]");
        result.Files[0].Content.Should().Contain("public string Name { get; set; } = string.Empty;");
    }

    [Fact]
    public void Generate_ShouldCreateNavigationAndJsonIgnoreOnParentReference()
    {
        var customer = Guid.NewGuid();
        var order = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderCustomerId = Guid.NewGuid();
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = customer,
                    TableName = "customers",
                    Columns = [new ColumnDefinition { Id = customerId, Name = "customer_id", DataType = "int", IsPrimaryKey = true, IsNullable = false }],
                },
                new EntityDefinition
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
                        new ColumnDefinition { Id = Guid.NewGuid(), Name = "order_id", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                        new ColumnDefinition { Id = orderCustomerId, Name = "customer_id", DataType = "int", IsForeignKey = true, IsNullable = false },
                    ],
                },
            ],
            Relationships =
            [
                new RelationshipDefinition
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    Type = RelationshipMultiplicity.OneToMany,
                    SourceColumnId = customerId,
                    TargetColumnId = orderCustomerId,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("public ICollection<OrderEntity> Orders { get; set; } = new List<OrderEntity>();");
        result.Files[0].Content.Should().Contain("[JsonIgnore]");
        result.Files[0].Content.Should().Contain("public CustomerEntity Customer { get; set; } = null!;");
    }

    [Fact]
    public void Generate_ShouldWarnAndSkipManyToManyRelationship()
    {
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition { Id = left, TableName = "users", Columns = [new ColumnDefinition { Id = Guid.NewGuid(), Name = "user_id", DataType = "int", IsPrimaryKey = true, IsNullable = false }] },
                new EntityDefinition { Id = right, TableName = "roles", Columns = [new ColumnDefinition { Id = Guid.NewGuid(), Name = "role_id", DataType = "int", IsPrimaryKey = true, IsNullable = false }] },
            ],
            Relationships = [new RelationshipDefinition { Id = Guid.NewGuid(), SourceEntityId = left, TargetEntityId = right, Type = RelationshipMultiplicity.ManyToMany }],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Warning && diagnostic.Message.Contains("多対多"));
        result.Files[0].Content.Should().NotContain("ICollection<RoleEntity>");
    }
}
