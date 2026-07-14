using FluentAssertions;
using QuickER.CodeGen.CSharp;
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

    /// <summary>マトリクスケース: EF Core＋Repository (QuickER) の両方生成（パリティ構成）× Split{off,on} × VO{off,on} の 4 ケース</summary>
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

        // Repository (QuickER) + EF Core 併用で無制限バイナリ列除外を ON（属性定義・付与が両バケット併存でも成立すること）
        data.Add(
            "EfCore + Repository (QuickER) + ExcludeUnboundedBinaryColumns",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateEfCore = true,
                ExcludeUnboundedBinaryColumns = true,
            }
        );

        return data;
    }

    /// <summary>マトリクスケース: EF 単独出力（QuickER の SQL Server 実装なし）× Split{off,on} × VO{off,on} の 4 ケース</summary>
    public static TheoryData<string, CodeGenerationOptions> EfCoreOnlyMatrixCases()
    {
        var data = new TheoryData<string, CodeGenerationOptions>();
        foreach (var split in new[] { false, true })
        foreach (var vo in new[] { false, true })
        {
            data.Add(
                $"EfCore 単独 Split={split} VO={vo}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateValueObjects = vo,
                    GenerateEfCore = true,
                    GenerateRepositories = false,
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

    /// <summary>マトリクスケース: SQLite 方言のRepository (QuickER)（Split{off,on} × VO{off,on} の 4 ケース）</summary>
    public static TheoryData<string, CodeGenerationOptions> SqliteRepositoryMatrixCases()
    {
        var data = new TheoryData<string, CodeGenerationOptions>();
        foreach (var split in new[] { false, true })
        foreach (var vo in new[] { false, true })
        {
            data.Add(
                $"SQLite Repository Split={split} VO={vo}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateValueObjects = vo,
                    RepositoryDialect = "sqlite",
                }
            );
        }

        // EF Core と SQLite Repository (QuickER) の併存（パリティ検証相当の構成）でも生成が通ることを確認する
        data.Add(
            "SQLite Repository + EF Core",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                RepositoryDialect = "sqlite",
                GenerateEfCore = true,
            }
        );
        return data;
    }

    /// <summary>マトリクスケース: インメモリ Repository（Split{off,on} × VO{off,on} の 4 ケース）＋ EF・QuickER sqlserver 併存</summary>
    public static TheoryData<string, CodeGenerationOptions> InMemoryRepositoryMatrixCases()
    {
        var data = new TheoryData<string, CodeGenerationOptions>();
        foreach (var split in new[] { false, true })
        foreach (var vo in new[] { false, true })
        {
            data.Add(
                $"InMemory 単独 Split={split} VO={vo}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateValueObjects = vo,
                    GenerateRepositories = false,
                    GenerateEfCore = false,
                    GenerateInMemoryRepositories = true,
                }
            );
        }

        // インメモリ＋EF Core 併存（契約は共有・二重定義にならないこと）
        data.Add(
            "InMemory + EF Core",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateRepositories = false,
                GenerateEfCore = true,
                GenerateInMemoryRepositories = true,
            }
        );

        // インメモリ＋QuickER sqlserver Repository 併存
        data.Add(
            "InMemory + QuickER sqlserver Repository",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialect = "sqlserver",
                GenerateInMemoryRepositories = true,
            }
        );

        // インメモリ＋QuickER マルチターゲット（sqlserver/sqlite）併存（方言非依存のインメモリはマルチと共存可）
        data.Add(
            "InMemory + QuickER マルチターゲット(sqlserver/sqlite)",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver", "sqlite"],
                GenerateInMemoryRepositories = true,
            }
        );

        return data;
    }

    /// <summary>マトリクスケース: リモート契約生成（GenerateRemoteContracts）の実装先横断ケース</summary>
    public static TheoryData<string, CodeGenerationOptions> RemoteContractMatrixCases()
    {
        var data = new TheoryData<string, CodeGenerationOptions>();
        foreach (var split in new[] { false, true })
        foreach (var vo in new[] { false, true })
        {
            data.Add(
                $"remote QuickER sqlserver Split={split} VO={vo}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateValueObjects = vo,
                    GenerateRemoteContracts = true,
                }
            );
        }

        // EF 単独（ローカル面・両面 DI が EF 実装だけでも成立すること）
        data.Add(
            "remote EF 単独",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateRepositories = false,
                GenerateEfCore = true,
                GenerateRemoteContracts = true,
            }
        );

        // SQLite QuickER＋EF 併存（パリティ構成）
        data.Add(
            "remote SQLite + EF Core",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                RepositoryDialect = "sqlite",
                GenerateEfCore = true,
                GenerateRemoteContracts = true,
            }
        );

        // インメモリ単独（両面 DI・ローカル面実装）
        data.Add(
            "remote InMemory 単独",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateRepositories = false,
                GenerateInMemoryRepositories = true,
                GenerateRemoteContracts = true,
            }
        );

        // マルチターゲット（中立契約 1 回＋方言別実装・keyed 両面 DI）
        data.Add(
            "remote マルチターゲット(sqlserver/sqlite)",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                RepositoryDialects = ["sqlserver", "sqlite"],
                GenerateRemoteContracts = true,
            }
        );

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
                "ExcludeUnboundedBinaryColumns=true（varbinary(max) の photo 列にマーカー付与）",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    ExcludeUnboundedBinaryColumns = true,
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

    /// <summary>EF Core＋Repository (QuickER)（Split × VO の 4 ケース）で、生成コードがエラー・警告なしでコンパイルできることを検証する</summary>
    [Theory]
    [MemberData(nameof(EfCoreMatrixCases))]
    public void Generate_EfCoreMatrix_ShouldProduceCompilableCode(
        string caseName,
        CodeGenerationOptions options
    ) => AssertCompiles(caseName, options);

    /// <summary>EF 単独出力（Split × VO の 4 ケース）で、生成コードがエラー・警告なしでコンパイルできることを検証する</summary>
    [Theory]
    [MemberData(nameof(EfCoreOnlyMatrixCases))]
    public void Generate_EfCoreOnlyMatrix_ShouldProduceCompilableCode(
        string caseName,
        CodeGenerationOptions options
    ) => AssertCompiles(caseName, options);

    /// <summary>SQLite 方言のRepository (QuickER)（Split × VO の 4 ケース）で、生成コードがエラー・警告なしでコンパイルできることを検証する</summary>
    [Theory]
    [MemberData(nameof(SqliteRepositoryMatrixCases))]
    public void Generate_SqliteRepositoryMatrix_ShouldProduceCompilableCode(
        string caseName,
        CodeGenerationOptions options
    ) => AssertCompiles(caseName, options);

    /// <summary>インメモリ Repository（Split × VO の 4 ケース）＋ EF・QuickER 併存で、生成コードがエラー・警告なしでコンパイルできることを検証する</summary>
    [Theory]
    [MemberData(nameof(InMemoryRepositoryMatrixCases))]
    public void Generate_InMemoryRepositoryMatrix_ShouldProduceCompilableCode(
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

    /// <summary>リモート契約生成（GenerateRemoteContracts）の実装先横断ケースで、生成コードがエラー・警告なしでコンパイルできることを検証する</summary>
    [Theory]
    [MemberData(nameof(RemoteContractMatrixCases))]
    public void Generate_RemoteContractMatrix_ShouldProduceCompilableCode(
        string caseName,
        CodeGenerationOptions options
    ) => AssertCompiles(caseName, options);

    /// <summary>マトリクスケース: リモートサービス生成（HTTP クライアント同梱）の横断ケース</summary>
    public static TheoryData<string, CodeGenerationOptions> RemoteServiceMatrixCases()
    {
        var data = new TheoryData<string, CodeGenerationOptions>();
        foreach (var vo in new[] { false, true })
        {
            data.Add(
                $"remote-services QuickER sqlserver VO={vo}",
                new CodeGenerationOptions
                {
                    NamespaceName = "Sample.Domain",
                    GenerateValueObjects = vo,
                    GenerateRemoteServices = true,
                }
            );
        }

        data.Add(
            "remote-services SQLite + EF Core",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                RepositoryDialect = "sqlite",
                GenerateEfCore = true,
                GenerateRemoteServices = true,
            }
        );
        data.Add(
            "remote-services EF 単独",
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateRepositories = false,
                GenerateEfCore = true,
                GenerateRemoteServices = true,
            }
        );

        return data;
    }

    /// <summary>
    /// リモートサービス生成の本体ファイル（クライアント実装同梱）がエラー・警告なしでコンパイルできることを検証する。
    /// </summary>
    /// <remarks>
    /// サーバーファイル（{ベース名}.RemoteServer.g.cs）は ASP.NET Core の FrameworkReference を要するため
    /// Roslyn マトリクスの参照集合ではコンパイルせず除外する。サーバーファイルの実コンパイル検証は、
    /// チェックイン済みフィクスチャ（RemoteServiceFixture.RemoteServer.g.cs）が本テストプロジェクトの
    /// コンパイル対象に含まれることで担保する。
    /// </remarks>
    [Theory]
    [MemberData(nameof(RemoteServiceMatrixCases))]
    public void Generate_RemoteServiceMatrix_MainOutputShouldCompile(
        string caseName,
        CodeGenerationOptions options
    )
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

        var mainFiles = new CodeGenerationResult
        {
            Files = result
                .Files.Where(file =>
                    !file.FileName.EndsWith(".RemoteServer.g.cs", StringComparison.Ordinal)
                )
                .ToList(),
            Diagnostics = result.Diagnostics,
        };
        mainFiles.Files.Should().NotBeEmpty($"「{caseName}」は本体ファイルが生成されるはず");

        var compilation = GeneratedCodeCompiler.Compile(
            mainFiles,
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
