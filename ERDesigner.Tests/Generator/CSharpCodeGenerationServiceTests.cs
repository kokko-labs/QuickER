using ERDesigner.Generator;
using FluentAssertions;

namespace ERDesigner.Tests.Generator;

/// <summary>
/// <see cref="CSharpCodeGenerationService"/> がダイアグラム定義から生成する C# コードの内容を検証するテストクラス
/// </summary>
public class CSharpCodeGenerationServiceTests
{
    /// <summary>
    /// 単一の生成ファイルに Entity・EditModel・EditModelBase と各種属性が出力され、using ディレクティブが重複しないことを検証する
    /// </summary>
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
        result.Files[0].Content.Split("using System.ComponentModel.DataAnnotations;").Length.Should().Be(2);
        result.Files[0].Content.Split("using System.ComponentModel.DataAnnotations.Schema;").Length.Should().Be(2);
        result.Files[0].Content.Should().Contain("public partial class CustomerEntity");
        result.Files[0].Content.Should().Contain("public partial class CustomerEditModel");
        result.Files[0].Content.Should().Contain("public abstract partial class EditModelBase");
        result.Files[0].Content.Should().Contain("[Table(\"customers\")]");
        result.Files[0].Content.Should().Contain("[Key]");
        result.Files[0].Content.Should().Contain("[MaxLength(100)]");
        // EditModel は画面バインディング用の文字列プロパティを持つ
        result.Files[0].Content.Should().Contain("public string BindingName");
        result.Files[0].Content.Should().Contain("public partial class CustomerEditModel : EditModelBase");
    }

    /// <summary>
    /// 1対多リレーションからコレクション型ナビゲーションと NavigationReference 属性が生成され、親参照プロパティに JsonIgnore が付与されることを検証する
    /// </summary>
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
        // NavigationReference 属性は (参照元テーブル, 参照元カラム, 参照先テーブル, 参照先カラム, IsCollection) の 5 引数形式
        result.Files[0].Content.Should().Contain("[NavigationReference(\"customers\", \"customer_id\", \"orders\", \"customer_id\", true)]");
        result.Files[0].Content.Should().Contain("public ICollection<OrderEntity> Orders { get; set; } = new List<OrderEntity>();");
        result.Files[0].Content.Should().Contain("[JsonIgnore]");
        result.Files[0].Content.Should().Contain("public CustomerEntity Customer { get; set; } = null!;");
    }

    /// <summary>
    /// パスカルケースのテーブル名がそのままエンティティ名・ナビゲーションプロパティ名に反映されることを検証する
    /// </summary>
    [Fact]
    public void Generate_ShouldPreservePascalCaseTableNamesInEntityAndNavigationNames()
    {
        var category = Guid.NewGuid();
        var item = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var itemCategoryId = Guid.NewGuid();
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = category,
                    TableName = "AirconditionerCategory",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = categoryId,
                            Name = "AirconditionerCategoryId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new EntityDefinition
                {
                    Id = item,
                    TableName = "Airconditioner",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "AirconditionerId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = itemCategoryId,
                            Name = "AirconditionerCategoryId",
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
                    SourceEntityId = category,
                    TargetEntityId = item,
                    Type = RelationshipMultiplicity.OneToMany,
                    SourceColumnId = categoryId,
                    TargetColumnId = itemCategoryId,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("public partial class AirconditionerCategoryEntity");
        result.Files[0].Content.Should().Contain("public ICollection<AirconditionerEntity> Airconditioners { get; set; } = new List<AirconditionerEntity>();");
        result.Files[0].Content.Should().Contain("public AirconditionerCategoryEntity AirconditionerCategory { get; set; } = null!;");
    }

    [Fact]
    public void Generate_ShouldConvertSnakeCaseTableNamesToPascalCaseEntityNames()
    {
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "airconditioner_category",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "airconditioner_category_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("public partial class AirconditionerCategoryEntity");
        result.Files[0].Content.Should().Contain("public partial class AirconditionerCategoryEditModel");
        result.Files[0].Content.Should().Contain("public sealed class AirconditionerCategoryMapper");
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
        // CommitToEntity では nullable 化された確定値に対して保存前 null チェックを行う
        result.Files[0].Content.Should().Contain("entity.ProductId = editModel.ProductId ?? throw new InvalidOperationException(\"ProductId が未入力です。\");");
        result.Files[0].Content.Should().Contain("entity.Name = editModel.Name ?? throw new InvalidOperationException(\"Name が未入力です。\");");
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
        content.Should().Contain("public int? OrderId");
        content.Should().Contain("public decimal? Amount");
        // バインディング用プロパティ (string)
        content.Should().Contain("public string BindingOrderId");
        content.Should().Contain("public string BindingAmount");
        // TryParse 検証
        content.Should().Contain("int.TryParse(value, out var parsed)");
        content.Should().Contain("decimal.TryParse(value, out var parsed)");
        // エラーメッセージは ResolveParseErrorMessage 経由で生成される
        content.Should().Contain("ResolveParseErrorMessage(nameof(BindingOrderId), value, \"int\")");
        content.Should().Contain("ResolveParseErrorMessage(nameof(BindingAmount), value, \"decimal\")");
        // EditModelBase に BuildParseErrorMessage / CustomizeParseErrorMessage が存在する
        content.Should().Contain("protected virtual string BuildParseErrorMessage(");
        content.Should().Contain("partial void CustomizeParseErrorMessage(");
        // RevertInput
        content.Should().Contain("public void RevertInput()");
        content.Should().Contain("ExecuteRevert(() =>");
    }

    [Fact]
    public void Generate_ShouldCreateRepositoryInfrastructure()
    {
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
                            Id = Guid.NewGuid(),
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
        var content = result.Files[0].Content;
        content.Should().Contain("using Microsoft.Data.SqlClient;");
        content.Should().Contain("using Microsoft.Extensions.DependencyInjection;");
        content.Should().Contain("public interface IRepository<TEntity, TKey>");
        content.Should().Contain("public abstract class SqlServerRepository<TEntity, TKey>");
        content.Should().Contain("internal sealed class SqlEntityMetadata<TEntity, TKey>");
        content.Should().Contain("public interface ICustomerRepository : IRepository<CustomerEntity, int>;");
        content.Should().Contain("public sealed class CustomerRepository(ISqlConnectionFactory connectionFactory)");
        content.Should().Contain("services.AddScoped<ICustomerRepository, CustomerRepository>();");
        content.Should().Contain("SelectByIdSql = $\"SELECT {string.Join(\", \", allColumns.Select(column => $\"[{column}]\"))} FROM {tableName} WHERE [{keyColumnName}] = @id;\"");
        content
            .Should()
            .Contain(
                "InsertSql = $\"INSERT INTO {tableName} ({string.Join(\", \", insertColumns.Select(column => $\"[{column}]\"))}) VALUES ({string.Join(\", \", properties.Select(property => $\"@{property.Name}\"))});\""
            );
        content.Should().Contain("UpdateSql = $\"UPDATE {tableName} SET {string.Join(\", \", updateAssignments)} WHERE [{keyColumnName}] = @id;\"");
        content.Should().Contain("DeleteSql = $\"DELETE FROM {tableName} WHERE [{keyColumnName}] = @id;\"");
    }

    [Fact]
    public void Generate_RepositorySql_ShouldExcludeNavigationProperties()
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
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(100)",
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
        var content = result.Files[0].Content;
        content.Should().Contain("property.GetCustomAttribute<NavigationReferenceAttribute>() is null");
        content.Should().Contain("public ICollection<OrderEntity> Orders { get; set; } = new List<OrderEntity>();");
        content.Should().Contain("public CustomerEntity Customer { get; set; } = null!;");
        content.Should().NotContain("@Orders");
        content.Should().NotContain("@Customer");
        content.Should().NotContain("[Orders]");
        content.Should().NotContain("[Customer]");
    }

    [Fact]
    public void Generate_EditModel_WithBinaryAndValueTypes_ShouldUseSafeBindingConversions()
    {
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "files",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "file_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "is_active",
                            DataType = "bit",
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "filedata",
                            DataType = "varbinary(max)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("public int? FileId");
        content.Should().Contain("public bool? IsActive");
        content.Should().Contain("public byte[] Filedata { get; set; } = Array.Empty<byte>();");
        content.Should().Contain("Filedata = Convert.FromBase64String(value);");
        content.Should().Contain("BindingFiledata = Filedata is null ? string.Empty : Convert.ToBase64String(Filedata);");
        content.Should().Contain("Filedata = Array.Empty<byte>();");
        content.Should().Contain("BindingFileId = FileId?.ToString() ?? string.Empty;");
        content.Should().Contain("editModel.BindingIsActive = entity.IsActive.ToString() ?? string.Empty;");
        content.Should().NotContain("entity.FileId?.ToString()");
        content.Should().NotContain("entity.IsActive?.ToString()");
        content.Should().NotContain("private string? _errorFiledata;");
        content.Should().Contain("private static readonly SqlEntityMetadata<TEntity, TKey> _metadata = SqlEntityMetadata<TEntity, TKey>.Create();");
        content.Should().Contain("private readonly ISqlConnectionFactory _connectionFactory = connectionFactory;");
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

    [Fact]
    public void Generate_ManyEntities_ShouldNotHitScribanLoopLimit()
    {
        var entities = Enumerable
            .Range(1, 1100)
            .Select(index => new EntityDefinition
            {
                Id = Guid.NewGuid(),
                TableName = $"items_{index}",
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
            })
            .ToList();

        var diagram = new DiagramDefinition { Entities = entities };
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = false,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("public partial class Items1Entity");
        result.Files[0].Content.Should().Contain("public partial class Items1100Entity");
    }
}
