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
    /// <summary>ナビゲーションプロパティが生成され、親参照側に JsonIgnore 属性が付くことを検証する</summary>
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
    /// <summary>パスカルケースのテーブル名がエンティティ名・ナビゲーション名に保持されることを検証する</summary>
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

    /// <summary>スネークケースのテーブル名がパスカルケースのエンティティ名へ変換されることを検証する</summary>
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
        result.Files[0].Content.Should().Contain("public sealed partial class AirconditionerCategoryMapper");
    }

    /// <summary>多対多リレーションが警告付きで生成スキップされることを検証する</summary>
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

    /// <summary>Entity ↔ EditModel を変換する Mapper クラスが生成されることを検証する</summary>
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
        // Mapper は具象クラスのみ（インターフェースなし）。フック宣言のため partial
        result.Files[0].Content.Should().NotContain("public interface IProductMapper");
        result.Files[0].Content.Should().Contain("public sealed partial class ProductMapper");
        result.Files[0].Content.Should().NotContain(": IProductMapper");
        result.Files[0].Content.Should().Contain("ProductEditModel CreateEditModel(ProductEntity entity)");
        result.Files[0].Content.Should().Contain("void ApplyToEntity(ProductEditModel editModel, ProductEntity entity)");
        // 旧 Commit 系の名前は残っていないこと
        result.Files[0].Content.Should().NotContain("CommitToEditModel");
        result.Files[0].Content.Should().NotContain("CommitToEntity");
        // Entity ファクトリ（空生成・EditModel 反映）と初期値フックを生成する
        result.Files[0].Content.Should().Contain("public ProductEntity CreateEntity()");
        result.Files[0].Content.Should().Contain("public ProductEntity CreateEntity(ProductEditModel editModel)");
        result.Files[0].Content.Should().Contain("partial void OnEntityCreated(ProductEntity entity);");
        // ApplyToEntity では nullable 化された確定値に対して保存前 null チェックを行う
        result.Files[0].Content.Should().Contain("entity.ProductId = editModel.ProductId ?? throw new InvalidOperationException(\"ProductId が未入力です。\");");
        result.Files[0].Content.Should().Contain("entity.Name = editModel.Name ?? throw new InvalidOperationException(\"Name が未入力です。\");");
        // Entity → EditModel 反映は public な ApplyToEditModel で行い、バインディング用プロパティ経由でロードする
        result.Files[0].Content.Should().Contain("public void ApplyToEditModel(ProductEntity entity, ProductEditModel editModel)");
        result.Files[0].Content.Should().Contain("editModel.BindingName =");
        result.Files[0].Content.Should().NotContain("LoadFrom");
        // 既定ロード後の後処理フック（partial 実装で追加プロパティをロード）
        result.Files[0].Content.Should().Contain("OnEditModelLoaded(entity, editModel);");
        result.Files[0].Content.Should().Contain("partial void OnEditModelLoaded(ProductEntity entity, ProductEditModel editModel);");
        // 反映後フック（partial 実装で追加プロパティを保存）
        result.Files[0].Content.Should().Contain("OnEntityApplied(editModel, entity);");
        result.Files[0].Content.Should().Contain("partial void OnEntityApplied(ProductEditModel editModel, ProductEntity entity);");
    }

    /// <summary>EditModel にバインディング文字列を確定値へ戻す処理が生成されることを検証する</summary>
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

    /// <summary>Repository インターフェース・実装・DI 登録などの基盤コードが生成されることを検証する</summary>
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
        // カラム一覧は columnList へ抽出して SELECT 系で共用する
        content.Should().Contain("var columnList = string.Join(\", \", allColumns.Select(column => $\"[{column}]\"));");
        content.Should().Contain("SelectByIdSql = $\"SELECT {columnList} FROM {tableName} WHERE [{keyColumnName}] = @id;\"");
        content
            .Should()
            .Contain(
                "InsertSql = $\"INSERT INTO {tableName} ({string.Join(\", \", insertColumns.Select(column => $\"[{column}]\"))}) VALUES ({string.Join(\", \", properties.Select(property => $\"@{property.Name}\"))});\""
            );
        content.Should().Contain("UpdateSql = $\"UPDATE {tableName} SET {string.Join(\", \", updateAssignments)} WHERE [{keyColumnName}] = @id;\"");
        content.Should().Contain("DeleteSql = $\"DELETE FROM {tableName} WHERE [{keyColumnName}] = @id;\"");
    }

    /// <summary>Repository にラムダ式ベースのクエリビルダー（Query / Where / OrderBy / 終端メソッド）が生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldCreateLambdaQueryBuilder()
    {
        var result = new CSharpCodeGenerationService().Generate(SingleEntityDiagram(), new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 式木変換に必要な using とクエリ基盤クラス
        content.Should().Contain("using System.Linq.Expressions;");
        content.Should().Contain("public sealed class SqlQuery<TEntity>");
        content.Should().Contain("internal static class SqlExpressionTranslator");
        // リポジトリ起点のクエリ開始メソッド
        content.Should().Contain("public SqlQuery<TEntity> Query() =>");
        // ラムダ式で条件・並び順を指定するチェーンメソッド
        content.Should().Contain("public SqlQuery<TEntity> Where(Expression<Func<TEntity, bool>> predicate)");
        content.Should().Contain("public SqlQuery<TEntity> OrderBy(Expression<Func<TEntity, object?>> keySelector)");
        content.Should().Contain("public SqlQuery<TEntity> OrderByDescending(Expression<Func<TEntity, object?>> keySelector)");
        content.Should().Contain("public SqlQuery<TEntity> Take(int count)");
        content.Should().Contain("public SqlQuery<TEntity> Skip(int count)");
        // 終端メソッド一式
        content.Should().Contain("public async Task<IReadOnlyList<TEntity>> ToListAsync(CancellationToken cancellationToken = default)");
        content.Should().Contain("public async Task<TEntity?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)");
        content.Should().Contain("public async Task<int> CountAsync(CancellationToken cancellationToken = default)");
        content.Should().Contain("public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)");
        // 値はパラメータ化、OFFSET/FETCH でページング
        content.Should().Contain("AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value)");
        content.Should().Contain("FETCH NEXT {take.Value} ROWS ONLY");
    }

    /// <summary>Repository の SQL 生成でナビゲーションプロパティが列に含まれないことを検証する</summary>
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

    /// <summary>バイナリ・値型カラムで安全なバインディング変換（Base64 / TryParse）が生成されることを検証する</summary>
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

    /// <summary>Entity のみ生成設定で EditModel や Mapper が出力されないことを検証する</summary>
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

    /// <summary>多数のエンティティでも Scriban のループ上限に達せず生成が完了することを検証する</summary>
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

    /// <summary>Repository をデータアノテーション無効で生成しようとするとエラーになることを検証する</summary>
    [Fact]
    public void Generate_RepositoryWithoutDataAnnotations_ShouldFailWithError()
    {
        var diagram = SingleEntityDiagram();
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            GenerateRepositories = true,
            IncludeDataAnnotations = false,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Error && diagnostic.Message.Contains("データアノテーション"));
    }

    /// <summary>Mapper を EditModel なしで生成しようとするとエラーになることを検証する</summary>
    [Fact]
    public void Generate_MapperWithoutEditModel_ShouldFailWithError()
    {
        var diagram = SingleEntityDiagram();
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            GenerateEntityClasses = true,
            GenerateEditModels = false,
            GenerateMappers = true,
            GenerateRepositories = false,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Error && diagnostic.Message.Contains("Mapper"));
    }

    /// <summary>Repository を Entity なしで生成しようとするとエラーになることを検証する</summary>
    [Fact]
    public void Generate_RepositoryWithoutEntity_ShouldFailWithError()
    {
        var diagram = SingleEntityDiagram();
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            GenerateEntityClasses = false,
            GenerateEditModels = true,
            GenerateMappers = false,
            GenerateRepositories = true,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Error && diagnostic.Message.Contains("Repository"));
    }

    /// <summary>多対多リレーションの警告がエンティティ数に関係なく 1 回だけ追加されることを検証する（重複解消）</summary>
    [Fact]
    public void Generate_ManyToManyWarning_ShouldNotBeDuplicated()
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
                    Columns = [new ColumnDefinition { Id = Guid.NewGuid(), Name = "user_id", DataType = "int", IsPrimaryKey = true, IsNullable = false }],
                },
                new EntityDefinition
                {
                    Id = right,
                    TableName = "roles",
                    Columns = [new ColumnDefinition { Id = Guid.NewGuid(), Name = "role_id", DataType = "int", IsPrimaryKey = true, IsNullable = false }],
                },
            ],
            Relationships =
            [
                new RelationshipDefinition { Id = Guid.NewGuid(), SourceEntityId = left, TargetEntityId = right, Type = RelationshipMultiplicity.ManyToMany },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Count(diagnostic => diagnostic.Message.Contains("多対多")).Should().Be(1);
    }

    /// <summary>NULL 許容のバイナリ列が byte[]? として生成されることを検証する</summary>
    [Fact]
    public void Generate_NullableBinaryColumn_ShouldUseNullableByteArray()
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
                        new ColumnDefinition { Id = Guid.NewGuid(), Name = "file_id", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                        new ColumnDefinition { Id = Guid.NewGuid(), Name = "photo", DataType = "varbinary(max)", IsNullable = true },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // NULL 許容バイナリは byte[]? となり、初期化子は付かない（CS8618 回避は ? 注釈による）
        content.Should().Contain("public byte[]? Photo { get; set; }");
        content.Should().NotContain("public byte[] Photo { get; set; } = Array.Empty<byte>();");
        // EditModel 側でも byte[]? を確定値型とし、Base64 変換で書き戻す
        content.Should().Contain("entity.Photo = editModel.Photo;");
    }

    /// <summary>出力が 1MB（Scriban 既定の LimitToString）を超える大規模スキーマでも末尾まで生成され、切り捨てられないことを検証する</summary>
    [Fact]
    public void Generate_LargeSchemaExceeding1MB_ShouldNotTruncateOutput()
    {
        // 全生成対象を有効にした 200 エンティティで出力は 1MB を大きく超える（Scriban 既定上限超過の回帰確認）
        var entities = Enumerable
            .Range(1, 200)
            .Select(index => new EntityDefinition
            {
                Id = Guid.NewGuid(),
                TableName = $"table_{index}",
                Columns =
                [
                    new ColumnDefinition { Id = Guid.NewGuid(), Name = "id", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                    new ColumnDefinition { Id = Guid.NewGuid(), Name = "name", DataType = "nvarchar(100)", IsNullable = false },
                    new ColumnDefinition { Id = Guid.NewGuid(), Name = "amount", DataType = "decimal", IsNullable = true },
                    new ColumnDefinition { Id = Guid.NewGuid(), Name = "created_at", DataType = "datetime2", IsNullable = false },
                ],
            })
            .ToList();

        var result = new CSharpCodeGenerationService().Generate(new DiagramDefinition { Entities = entities }, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Length.Should().BeGreaterThan(1_048_576);
        // 切り捨て時に付与される省略記号で終わっていないこと
        content.TrimEnd().Should().NotEndWith("...");
        // 最後のエンティティ・リポジトリまで出力されていること
        content.Should().Contain("public partial class Table200Entity");
        content.Should().Contain("public sealed class Table200Repository(ISqlConnectionFactory connectionFactory)");
    }

    /// <summary>EditModel の確定値プロパティに変更通知パーシャルメソッド（Changing/Changed）が生成され、setter から呼ばれることを検証する</summary>
    [Fact]
    public void Generate_EditModel_ShouldGenerateChangeHookPartialMethods()
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
                        new ColumnDefinition { Id = Guid.NewGuid(), Name = "customer_id", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                        new ColumnDefinition { Id = Guid.NewGuid(), Name = "name", DataType = "nvarchar(100)", IsNullable = false },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // パーシャルメソッドの宣言（本体はユーザーが partial クラスで実装）。新値のみ／旧値・新値の両オーバーロードを生成する
        content.Should().Contain("partial void OnCustomerIdChanging(int? value);");
        content.Should().Contain("partial void OnCustomerIdChanging(int? oldValue, int? newValue);");
        content.Should().Contain("partial void OnCustomerIdChanged(int? value);");
        content.Should().Contain("partial void OnCustomerIdChanged(int? oldValue, int? newValue);");
        content.Should().Contain("partial void OnNameChanged(string? oldValue, string? newValue);");
        // 確定値 setter から代入前後で、新値版・旧値新値版の両方が呼び出される
        content.Should().Contain("var oldValue = _customerId;");
        content.Should().Contain("OnCustomerIdChanging(value);");
        content.Should().Contain("OnCustomerIdChanging(oldValue, value);");
        content.Should().Contain("OnCustomerIdChanged(value);");
        content.Should().Contain("OnCustomerIdChanged(oldValue, value);");
        // 値が変わらない場合は早期 return
        content.Should().Contain("if (EqualityComparer<int?>.Default.Equals(_customerId, value))");
    }

    /// <summary>リレーションが無くても Repository を生成する場合に NavigationReference 属性が定義され、生成コードがコンパイル可能であることを検証する</summary>
    [Fact]
    public void Generate_RepositoryWithoutRelationships_ShouldEmitNavigationReferenceAttribute()
    {
        var result = new CSharpCodeGenerationService().Generate(SingleEntityDiagram(), new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // Repository の SqlEntityMetadata が参照する属性が未定義だと CS0246 になるため、定義の存在を確認
        content.Should().Contain("public sealed class NavigationReferenceAttribute : Attribute");
        content.Should().Contain("property.GetCustomAttribute<NavigationReferenceAttribute>() is null");
    }

    /// <summary>主キー 1 列のみを持つ単純なエンティティ 1 件のダイアグラムを生成する</summary>
    private static DiagramDefinition SingleEntityDiagram() =>
        new()
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns = [new ColumnDefinition { Id = Guid.NewGuid(), Name = "item_id", DataType = "int", IsPrimaryKey = true, IsNullable = false }],
                },
            ],
        };
}
