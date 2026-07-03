using FluentAssertions;
using QuickER.Generator;
using QuickER.Model;

namespace QuickER.Tests.Generator;

/// <summary>
/// <see cref="CSharpCodeGenerationService"/> が生成する C# コードが、実際に Roslyn でコンパイル可能であることを検証するテストクラス
/// </summary>
/// <remarks>
/// <see cref="CSharpCodeGenerationServiceTests"/> は生成内容（文字列の断片）を検証するのに対し、
/// このクラスは生成結果全体を <see cref="GeneratedCodeCompiler"/> で実際にコンパイルし、
/// エラー 0 件・生成コード起因の警告 0 件（アセンブリ統一系 CS1701/CS1702 を除く）であることを検証する。
/// テスト対象の ER 図は複合主キー・1対1・1対多・自己参照・値オブジェクト対象カラム・日本語テーブル名・
/// NULL 許容混在を 1 つに収めた <see cref="FullCoverageDiagram"/> を全ケースで共通利用する。
/// </remarks>
public class GeneratedCodeCompilationTests
{
    /// <summary>マトリクスケース: 全カテゴリ有効 × Split{off,on} × VO{off,on} の 4 ケース</summary>
    public static TheoryData<string, CodeGenerationOptions> FullMatrixCases()
    {
        var data = new TheoryData<string, CodeGenerationOptions>();
        foreach (var split in new[] { false, true })
        foreach (var vo in new[] { false, true })
        {
            data.Add(
                $"全カテゴリ Split={split} VO={vo}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateValueObjects = vo,
                }
            );
        }
        return data;
    }

    /// <summary>マトリクスケース: EF Core 生成あり × Split{off,on} × VO{off,on} の 4 ケース（Repository 必須のため常に有効）</summary>
    public static TheoryData<string, CodeGenerationOptions> EfCoreMatrixCases()
    {
        var data = new TheoryData<string, CodeGenerationOptions>();
        foreach (var split in new[] { false, true })
        foreach (var vo in new[] { false, true })
        {
            data.Add(
                $"EfCore Split={split} VO={vo}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateValueObjects = vo,
                    GenerateEfCore = true,
                }
            );
        }
        return data;
    }

    /// <summary>マトリクスケース: カテゴリ削減の現実的な組み合わせ × Split{off,on}</summary>
    public static TheoryData<string, CodeGenerationOptions> ReducedCategoryCases()
    {
        var data = new TheoryData<string, CodeGenerationOptions>();
        foreach (var split in new[] { false, true })
        {
            data.Add(
                $"Entity のみ Split={split}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateEntityClasses = true,
                    GenerateEditModels = false,
                    GenerateMappers = false,
                    GenerateRepositories = false,
                }
            );
            data.Add(
                $"Entity+EditModel+Mapper Split={split}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateEntityClasses = true,
                    GenerateEditModels = true,
                    GenerateMappers = true,
                    GenerateRepositories = false,
                }
            );
            data.Add(
                $"Entity+Repository（EditModel/Mapper 抜き） Split={split}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateEntityClasses = true,
                    GenerateEditModels = false,
                    GenerateMappers = false,
                    GenerateRepositories = true,
                }
            );
        }
        return data;
    }

    /// <summary>マトリクスケース: オプション単発（各種フラグ・Namespace 上書き）</summary>
    public static TheoryData<string, CodeGenerationOptions> SingleOptionCases()
    {
        var data = new TheoryData<string, CodeGenerationOptions>
        {
            {
                "IncludeDataAnnotations=false（Repository 生成不可のため除外）",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    IncludeDataAnnotations = false,
                    GenerateRepositories = false,
                }
            },
            {
                "IncludeJsonIgnoreOnParentNavigation=false",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    IncludeJsonIgnoreOnParentNavigation = false,
                }
            },
            {
                "UseGuidKeyForStringPrimaryKey=true（VO 有効時のみ適用）",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    GenerateValueObjects = true,
                    UseGuidKeyForStringPrimaryKey = true,
                }
            },
            {
                "Namespace 上書き（Split off・単一 NamespaceName）",
                new CodeGenerationOptions { NamespaceName = "Acme.Custom.Domain" }
            },
            {
                "Namespace 上書き（Split on・カテゴリ別名前空間）",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = true,
                    RuntimeNamespace = "Acme.Shared.Runtime",
                    EntityNamespace = "Acme.Domain.Entities",
                    EditModelNamespace = "Acme.Domain.EditModels",
                    MapperNamespace = "Acme.Domain.Mappers",
                    RepositoryNamespace = "Acme.Domain.Repositories",
                }
            },
        };
        return data;
    }

    /// <summary>全カテゴリ × Split × VO の 4 ケースで、生成コードがエラー・警告なしでコンパイルできることを検証する</summary>
    [Theory]
    [MemberData(nameof(FullMatrixCases))]
    public void Generate_FullMatrix_ShouldProduceCompilableCode(
        string caseName,
        CodeGenerationOptions options
    ) => AssertCompiles(caseName, options);

    /// <summary>EF Core 生成あり（Split × VO の 4 ケース）で、生成コードがエラー・警告なしでコンパイルできることを検証する</summary>
    [Theory]
    [MemberData(nameof(EfCoreMatrixCases))]
    public void Generate_EfCoreMatrix_ShouldProduceCompilableCode(
        string caseName,
        CodeGenerationOptions options
    ) => AssertCompiles(caseName, options);

    /// <summary>カテゴリ削減の現実的な組み合わせで、生成コードがエラー・警告なしでコンパイルできることを検証する</summary>
    [Theory]
    [MemberData(nameof(ReducedCategoryCases))]
    public void Generate_ReducedCategories_ShouldProduceCompilableCode(
        string caseName,
        CodeGenerationOptions options
    ) => AssertCompiles(caseName, options);

    /// <summary>各種単発オプション（属性抑制・GuidKey・Namespace 上書き）で、生成コードがエラー・警告なしでコンパイルできることを検証する</summary>
    [Theory]
    [MemberData(nameof(SingleOptionCases))]
    public void Generate_SingleOptions_ShouldProduceCompilableCode(
        string caseName,
        CodeGenerationOptions options
    ) => AssertCompiles(caseName, options);

    /// <summary>指定オプションで生成し、Roslyn コンパイルがエラー・報告対象警告なしで成功することを検証する共通アサーション</summary>
    private static void AssertCompiles(string caseName, CodeGenerationOptions options)
    {
        var result = new CSharpCodeGenerationService().Generate(FullCoverageDiagram(), options);

        result
            .HasErrors.Should()
            .BeFalse(
                $"「{caseName}」の生成自体でエラーが発生: "
                    + string.Join(
                        " / ",
                        result
                            .Diagnostics.Where(diagnostic =>
                                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                            )
                            .Select(diagnostic => diagnostic.Message)
                    )
            );
        result.Files.Should().NotBeEmpty($"「{caseName}」は 1 ファイル以上生成されるはず");

        var compilation = GeneratedCodeCompiler.Compile(
            result,
            assemblyName: $"QuickER.Generated.Tests.{Guid.NewGuid():N}"
        );

        compilation
            .Success.Should()
            .BeTrue(
                $"「{caseName}」の生成コードにコンパイルエラーが発生:{Environment.NewLine}{compilation.DescribeErrors()}"
            );
        compilation
            .Warnings.Should()
            .BeEmpty(
                $"「{caseName}」の生成コードに生成コード起因の警告が発生:{Environment.NewLine}{compilation.DescribeWarnings()}"
            );
    }

    /// <summary>
    /// 複合主キー・1対1・1対多・自己参照・VO 対象カラム（int/string/decimal/bool/binary）・
    /// 日本語テーブル名・NULL 許容混在を 1 つに収めた、全マトリクスケース共通のフルカバレッジ ER 図
    /// </summary>
    private static ErDiagram FullCoverageDiagram()
    {
        var customer = Guid.NewGuid();
        var customerPk = Guid.NewGuid();

        var order = Guid.NewGuid();
        var orderPk = Guid.NewGuid();
        var orderCustomerFk = Guid.NewGuid();

        // 明細行: (order_id, line_no) の複合主キー。order への FK は複合 PK の一部を兼ねる
        var orderLine = Guid.NewGuid();
        var orderLineOrderFk = Guid.NewGuid();
        var orderLineNo = Guid.NewGuid();

        // 1対1: customer <-> customer_profile
        var customerProfile = Guid.NewGuid();
        var customerProfilePk = Guid.NewGuid();
        var customerProfileFk = Guid.NewGuid();

        // 自己参照: category.parent_category_id -> category.category_id
        var category = Guid.NewGuid();
        var categoryPk = Guid.NewGuid();
        var categoryParentFk = Guid.NewGuid();

        // 日本語テーブル名・日本語カラム名
        var product = Guid.NewGuid();
        var productPk = Guid.NewGuid();

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
                            Name = "balance",
                            DataType = "decimal(10,2)",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "is_active",
                            DataType = "bit",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "photo",
                            DataType = "varbinary(max)",
                            IsNullable = true,
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
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "memo",
                            DataType = "nvarchar(200)",
                            IsNullable = true,
                        },
                    ],
                },
                new Entity
                {
                    Id = orderLine,
                    TableName = "order_lines",
                    Columns =
                    [
                        // 複合主キー: order_id（FK 兼務）+ line_no
                        new Column
                        {
                            Id = orderLineOrderFk,
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = orderLineNo,
                            Name = "line_no",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "quantity",
                            DataType = "int",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = customerProfile,
                    TableName = "customer_profiles",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerProfilePk,
                            Name = "profile_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = customerProfileFk,
                            Name = "customer_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "bio",
                            DataType = "nvarchar(500)",
                            IsNullable = true,
                        },
                    ],
                },
                new Entity
                {
                    Id = category,
                    TableName = "categories",
                    Columns =
                    [
                        new Column
                        {
                            Id = categoryPk,
                            Name = "category_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = categoryParentFk,
                            Name = "parent_category_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = true,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = product,
                    TableName = "商品",
                    Columns =
                    [
                        new Column
                        {
                            Id = productPk,
                            Name = "商品ID",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "商品名",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "単価",
                            DataType = "decimal(12,2)",
                            IsNullable = true,
                        },
                    ],
                },
            ],
            Relationships =
            [
                // 1対多: customers -> orders
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    SourceColumnId = customerPk,
                    TargetColumnId = orderCustomerFk,
                },
                // 1対多: orders -> order_lines（子は複合 PK の一部が FK）
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = order,
                    TargetEntityId = orderLine,
                    SourceColumnId = orderPk,
                    TargetColumnId = orderLineOrderFk,
                },
                // 1対1: customers <-> customer_profiles
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToOne,
                    SourceEntityId = customer,
                    TargetEntityId = customerProfile,
                    SourceColumnId = customerPk,
                    TargetColumnId = customerProfileFk,
                },
                // 自己参照: categories -> categories
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = category,
                    TargetEntityId = category,
                    SourceColumnId = categoryPk,
                    TargetColumnId = categoryParentFk,
                },
            ],
        };
    }
}
