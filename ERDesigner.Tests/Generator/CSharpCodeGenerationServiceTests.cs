using ERDesigner.Generator;
using FluentAssertions;

namespace ERDesigner.Tests.Generator;

public class CSharpCodeGenerationServiceTests
{
    [Fact]
    public void Generate_ShouldCreateSingleGeneratedFileWithEntityAndEditModel()
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
                        new ColumnDefinition
                        {
                            Id = customerId,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
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
        result.Files[0].Content.Should().Contain("public partial class CustomerEditModel");
        result.Files[0].Content.Should().Contain("public abstract class EditModelBase");
        result.Files[0].Content.Should().Contain("[Table(\"customers\")]");
        result.Files[0].Content.Should().Contain("[Key]");
        result.Files[0].Content.Should().Contain("[MaxLength(100)]");
        // EditModel はバインディング用プロパティを持つ
        result.Files[0].Content.Should().Contain("public string BindingName");
        result.Files[0].Content.Should().Contain("public partial class CustomerEditModel : EditModelBase");
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
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = customerId,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new EntityDefinition
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = orderCustomerId,
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
        // Entity の navigation プロパティに独自属性が付く
        result.Files[0].Content.Should().Contain("[NavigationReference(");
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
                new EntityDefinition
                {
                    Id = left,
                    TableName = "users",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "user_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new EntityDefinition
                {
                    Id = right,
                    TableName = "roles",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "role_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new RelationshipDefinition
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = left,
                    TargetEntityId = right,
                    Type = RelationshipMultiplicity.ManyToMany,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Warning && diagnostic.Message.Contains("多対多"));
        result.Files[0].Content.Should().NotContain("ICollection<RoleEntity>");
    }

    [Fact]
    public void Generate_ShouldCreateMapperClass()
    {
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "products",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "product_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(200)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        // Mapper は具象クラスのみ（インターフェースなし）
        result.Files[0].Content.Should().NotContain("public interface IProductMapper");
        result.Files[0].Content.Should().Contain("public sealed class ProductMapper");
        result.Files[0].Content.Should().NotContain(": IProductMapper");
        result.Files[0].Content.Should().Contain("ProductEditModel CommitToEditModel(ProductEntity entity)");
        result.Files[0].Content.Should().Contain("void CommitToEntity(ProductEditModel editModel, ProductEntity entity)");
        // CommitToEntity では通常プロパティを Entity に代入する
        result.Files[0].Content.Should().Contain("entity.Name = editModel.Name;");
        // LoadFrom ではバインディング用プロパティ経由でロードする
        result.Files[0].Content.Should().Contain("editModel.BindingName =");
    }

    [Fact]
    public void Generate_EditModel_ShouldContainRevertInputMethod()
    {
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "orders",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "amount",
                            DataType = "decimal",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 通常プロパティ (private set)
        content.Should().Contain("public int OrderId");
        content.Should().Contain("public decimal Amount");
        // バインディング用プロパティ (string)
        content.Should().Contain("public string BindingOrderId");
        content.Should().Contain("public string BindingAmount");
        // TryParse 検証
        content.Should().Contain("int.TryParse(value, out var parsed)");
        content.Should().Contain("decimal.TryParse(value, out var parsed)");
        // RevertInput
        content.Should().Contain("public void RevertInput()");
        content.Should().Contain("ExecuteRevert(() =>");
    }

    [Fact]
    public void Generate_EntityOnly_ShouldNotContainUiModelOrMapper()
    {
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            GenerateEntityClasses = true,
            GenerateEditModels = false,
            GenerateMappers = false,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("public partial class ItemEntity");
        result.Files[0].Content.Should().NotContain("ItemEditModel");
        result.Files[0].Content.Should().NotContain("ItemMapper");
    }
}
