using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// パッケージ参照モード（<see cref="CodeGenerationOptions.UseRuntimePackages"/>）の生成コードが、
/// 「案内ビルダー（<see cref="RuntimePackageReferenceGuidance"/>）が指示するパッケージ集合だけを参照して」
/// Roslyn でコンパイルできることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// ランタイム側は <see cref="RuntimePackageSourceRenderer"/> の出力（Core / SqlServer / Sqlite / EfCore /
/// InMemory / AspNetCore）を
/// in-memory アセンブリへ順にコンパイル（Core → 方言/EF Core の参照順）して <see cref="MetadataReference"/> 化し、
/// 生成コードのコンパイルへ「案内が指示するパッケージだけ」を渡す。これにより案内の十分性（不足なくコンパイル可能）と
/// 依存最小性（余計なパッケージなしで成立）を同時に証明する。
/// </para>
/// <para>
/// 生成コードが直接参照する外部依存は BCL＋<c>Microsoft.Extensions.DependencyInjection</c>（DI 登録拡張）のみ。
/// 方言 ADO（SqlClient / Sqlite）・EF Core は<b>パッケージ側だけ</b>が参照するため、生成コードのコンパイル参照には含めない。
/// </para>
/// </remarks>
public class RuntimePackageModeCompilationTests
{
    private static readonly RuntimePackageSourceRenderer PackageRenderer = new();

    /// <summary>代表構成×分割の生成→案内どおりのパッケージ参照のみでコンパイル成功を検証する</summary>
    [Theory]
    [MemberData(nameof(PackageModeCases))]
    public void Generate_PackageMode_CompilesWithGuidedPackagesOnly(
        string caseName,
        CodeGenerationOptions baseOptions
    )
    {
        var options = WithPackageMode(baseOptions);
        var diagram = Diagram();

        var (primary, byDialect) = ResolveTypes(diagram, options);
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result
            .HasErrors.Should()
            .BeFalse(
                $"「{caseName}」の生成自体でエラー: "
                    + string.Join(
                        " / ",
                        result
                            .Diagnostics.Where(d =>
                                d.Severity == GenerationDiagnosticSeverity.Error
                            )
                            .Select(d => d.Message)
                    )
            );
        result.Files.Should().NotBeEmpty();

        // 生成コードには固定 infra の定義が一切出ない（パッケージが提供する）
        var allContent = string.Join(Environment.NewLine, result.Files.Select(f => f.Content));
        allContent.Should().NotContain("abstract partial class EntityBase");
        allContent.Should().NotContain("interface IRepository<TEntity");
        allContent.Should().NotContain("class SqlServerRepository<");
        allContent.Should().NotContain("class SqliteRepository<");
        allContent.Should().NotContain("class InMemoryDataStore");

        // サーバー実装ファイルが出る構成では、それが確かにコンパイル対象へ入っていることを明示的に固定する
        // （かつては ASP.NET Core 依存を理由に除外していた。撤廃が空振りにならないよう存在を表明する。
        //   ファイル名は非分割が {ベース名}.RemoteServer.g.cs・分割が RemoteServer.g.cs）
        if (options.GenerateRemoteServices)
        {
            result
                .Files.Should()
                .Contain(f => f.FileName.EndsWith("RemoteServer.g.cs", StringComparison.Ordinal));
        }

        // 案内が指示するパッケージ集合だけを参照集合として構築し、生成コードをコンパイルする。
        // サーバー実装ファイル（.RemoteServer.g.cs）も対象に含める＝固定部は QuickER.Runtime.AspNetCore が
        // 提供し、ASP.NET Core 本体はテストホストの共有フレームワーク（TPA）から解決される。
        var packages = RuntimePackageReferenceGuidance.Compute(options);
        var packageRefs = BuildPackageReferences(packages);

        var compilation = CompileGenerated(result, packages, packageRefs);

        compilation
            .Success.Should()
            .BeTrue(
                $"「{caseName}」の生成コードが案内どおりのパッケージ参照でコンパイル不能:{Environment.NewLine}{compilation.Describe()}"
            );
    }

    /// <summary>Entity のみ（Repository/EF Core なし）は Core だけで成立し、参照集合に ADO/EF Core が含まれない</summary>
    [Fact]
    public void EntityOnly_GuidanceIsCoreOnly_NoAdoOrEf()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEditModels = true,
                GenerateMappers = true,
                GenerateRepositories = false,
            }
        );

        var packages = RuntimePackageReferenceGuidance.Compute(options);

        packages.Should().Equal(RuntimePackages.Core);
        packages.Should().NotContain(RuntimePackages.SqlServer);
        packages.Should().NotContain(RuntimePackages.Sqlite);
        packages.Should().NotContain(RuntimePackages.EntityFrameworkCore);
    }

    /// <summary>SQLite 単独の生成物に "Microsoft.Data.SqlClient" が現れない（依存排他の文字列ガード）</summary>
    [Fact]
    public void SqliteOnly_GeneratedCode_DoesNotReferenceSqlClient()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlite"],
            }
        );
        var diagram = Diagram();
        var (primary, byDialect) = ResolveTypes(diagram, options);

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result.HasErrors.Should().BeFalse();

        var allContent = string.Join(Environment.NewLine, result.Files.Select(f => f.Content));
        allContent.Should().NotContain("Microsoft.Data.SqlClient");

        // 案内は Core＋Sqlite のみ（SqlServer / EF Core を含まない）
        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.Sqlite);
    }

    /// <summary>マルチターゲット（sqlserver+sqlite）の案内は Core＋両方言（EF Core なし）になる</summary>
    [Fact]
    public void MultiTarget_Guidance_IsCoreAndBothDialects()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver", "sqlite"],
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.SqlServer, RuntimePackages.Sqlite);
    }

    /// <summary>EF Core 単独×パッケージの案内は Core＋EF Core のみ（ADO を含まない）になる</summary>
    [Fact]
    public void EfCoreOnly_Guidance_IsCoreAndEfCore_NoAdo()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                GenerateEfCore = true,
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.EntityFrameworkCore);
    }

    /// <summary>QuickER sqlserver＋EF Core 併存×パッケージの案内は Core＋SqlServer＋EF Core になる</summary>
    [Fact]
    public void RepositoryPlusEfCore_Guidance_IsCoreSqlServerAndEfCore()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                GenerateEfCore = true,
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(
                RuntimePackages.Core,
                RuntimePackages.SqlServer,
                RuntimePackages.EntityFrameworkCore
            );
    }

    /// <summary>インメモリ×パッケージの案内は Core＋InMemory のみ（ADO / EF Core を含まない）になる</summary>
    [Fact]
    public void InMemoryOnly_Guidance_IsCoreAndInMemory_NoAdoOrEf()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                GenerateInMemoryRepositories = true,
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.InMemory);
    }

    /// <summary>リモートサービス生成×パッケージの案内は Core＋方言＋AspNetCore になる（サーバー固定部の所在）</summary>
    [Fact]
    public void RemoteServices_Guidance_IncludesAspNetCore()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                GenerateRemoteServices = true,
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.SqlServer, RuntimePackages.AspNetCore);
    }

    /// <summary>リモート契約のみ（サーバー実装なし）の案内には AspNetCore が入らない</summary>
    /// <remarks>
    /// <c>GenerateRemoteContracts</c> はインターフェイスを足すだけで、サーバー実装ファイル
    /// （<c>RemoteServer.g.cs</c>）を出さない＝ASP.NET Core は要らない。
    /// </remarks>
    [Fact]
    public void RemoteContractsOnly_Guidance_DoesNotIncludeAspNetCore()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                GenerateRemoteContracts = true,
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.SqlServer);
    }

    /// <summary>PackageReference 行の案内が指定バージョンで組み立てられる</summary>
    [Fact]
    public void BuildPackageReferenceLines_ProducesVersionedLines()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        var lines = RuntimePackageReferenceGuidance.BuildPackageReferenceLines(options, "9.9.9");

        lines
            .Should()
            .Contain("<PackageReference Include=\"QuickER.Runtime\" Version=\"9.9.9\" />");
        lines
            .Should()
            .Contain(
                "<PackageReference Include=\"QuickER.Runtime.SqlServer\" Version=\"9.9.9\" />"
            );
    }

    // ---- テストデータ ----

    /// <summary>代表構成×分割: (a) Entity+EditModel+Mapper+VO のみ (b) QuickER sqlserver (c) QuickER sqlite (d) マルチターゲット</summary>
    public static TheoryData<string, CodeGenerationOptions> PackageModeCases()
    {
        var data = new TheoryData<string, CodeGenerationOptions>();

        foreach (var split in new[] { false, true })
        {
            data.Add(
                $"Entity+EditModel+Mapper+VO のみ Split={split}",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateValueObjects = true,
                    GenerateRepositories = false,
                }
            );
            data.Add(
                $"QuickER sqlserver 単独 Split={split}",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateRepositories = true,
                }
            );
            data.Add(
                $"QuickER sqlite 単独 Split={split}",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateRepositories = true,
                    RepositoryDialects = ["sqlite"],
                }
            );
            data.Add(
                $"マルチターゲット sqlserver+sqlite Split={split}",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateRepositories = true,
                    RepositoryDialects = ["sqlserver", "sqlite"],
                }
            );
            data.Add(
                $"EF Core 単独 Split={split}",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateRepositories = false,
                    GenerateEfCore = true,
                }
            );
            data.Add(
                $"QuickER sqlserver＋EF Core 併存 Split={split}",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateRepositories = true,
                    GenerateEfCore = true,
                }
            );
            // インメモリ×パッケージ参照モード（併用可能化の実証。インメモリ基盤の固定 infra は
            // QuickER.Runtime.InMemory が提供し、per-entity 実装・シーダー・DI 登録は生成側に残る）
            data.Add(
                $"インメモリ単独 Split={split}",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateRepositories = false,
                    GenerateInMemoryRepositories = true,
                }
            );
            data.Add(
                $"QuickER sqlserver＋インメモリ併存 Split={split}",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateRepositories = true,
                    GenerateInMemoryRepositories = true,
                }
            );
            // インメモリ×VO×無制限バイナリ除外（per-entity 実装が固定 infra の protected/公開メンバーだけで
            // 成立することを最大構成で確認する）
            data.Add(
                $"インメモリ＋VO＋バイナリ除外 Split={split}",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    SplitFilesByCategory = split,
                    GenerateRepositories = true,
                    GenerateInMemoryRepositories = true,
                    GenerateValueObjects = true,
                    ExcludeUnboundedBinaryColumns = true,
                }
            );
        }

        // リモート契約生成×パッケージ参照モード（IRemoteRepository は Core パッケージが提供する）
        data.Add(
            "remote QuickER sqlserver",
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                GenerateRemoteContracts = true,
            }
        );
        data.Add(
            "remote マルチターゲット sqlserver+sqlite",
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver", "sqlite"],
                GenerateRemoteContracts = true,
            }
        );
        data.Add(
            "remote QuickER sqlite＋EF Core 併存",
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlite"],
                GenerateEfCore = true,
                GenerateRemoteContracts = true,
            }
        );

        // リモートサービス生成×パッケージ参照モード（クライアント固定 infra＝HttpRemoteRepository 等は Core パッケージが提供し、
        // サーバー固定 infra＝RemoteServerEngine 等は AspNetCore パッケージが提供する。per-entity クライアント・
        // エンドポイント・DI 登録は生成側に残り、サーバーファイルもそのままコンパイル検証の対象になる）
        data.Add(
            "remote-services QuickER sqlserver",
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                GenerateRemoteServices = true,
            }
        );
        data.Add(
            "remote-services QuickER sqlserver Split=true",
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                SplitFilesByCategory = true,
                GenerateRepositories = true,
                GenerateRemoteServices = true,
            }
        );
        // 無制限バイナリ除外との併用（バイナリ転送エンドポイント＝サーバー固定部の
        // ExecuteDownload/Upload/Delete・DeferredOctetStreamBody まで参照するケース）
        data.Add(
            "remote-services QuickER sqlserver＋バイナリ除外",
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                GenerateRemoteServices = true,
                ExcludeUnboundedBinaryColumns = true,
            }
        );

        return data;
    }

    /// <summary>ケース定義のオプションをパッケージ参照モードへ切り替える（他の設定はそのまま引き継ぐ）</summary>
    /// <remarks>
    /// 全プロパティを手書きで複製すると、オプションが増えたときに写し漏れてその構成が黙って未検証になる
    /// （実際に GenerateRemoteServices など 5 つが欠落していた）。<c>with</c> 式で 1 項目だけ差し替える。
    /// </remarks>
    private static CodeGenerationOptions WithPackageMode(CodeGenerationOptions options) =>
        options with
        {
            UseRuntimePackages = true,
        };

    /// <summary>実効方言に応じ、主辞書＋方言別辞書を用意する（マルチターゲットは byDialect 必須）</summary>
    private static (
        IReadOnlyDictionary<Guid, CSharpTypeInfo> Primary,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> ByDialect
    ) ResolveTypes(ErDiagram diagram, CodeGenerationOptions options)
    {
        var sqlServer = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var sqlite = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram);

        var primaryDialect = options.EffectiveRepositoryDialects[0];
        var primary = string.Equals(primaryDialect, "sqlite", StringComparison.OrdinalIgnoreCase)
            ? sqlite
            : sqlServer;

        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = sqlServer,
            ["sqlite"] = sqlite,
        };

        return (primary, byDialect);
    }

    /// <summary>2 エンティティ・1対多・int/string/decimal（方言可搬）の小さな ER 図</summary>
    private static ErDiagram Diagram()
    {
        var customer = Guid.NewGuid();
        var customerPk = Guid.NewGuid();
        var order = Guid.NewGuid();
        var orderPk = Guid.NewGuid();
        var orderFk = Guid.NewGuid();

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
                        // 文字列列は Unicode で統一（Ansi/Unicode 差で canonical トークンが割れるため）
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
                            Id = orderFk,
                            Name = "customer_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "amount",
                            DataType = "decimal(10,2)",
                            IsNullable = false,
                        },
                        // 無制限バイナリ列（ExcludeUnboundedBinaryColumns のケースで Stream アクセサ・
                        // 除外ガードの固定 infra 参照までコンパイル検証させる）
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "attachment",
                            DataType = "varbinary(max)",
                            IsNullable = true,
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
                    ColumnPairs = [new(customerPk, orderFk)],
                },
            ],
        };
    }

    // ---- ランタイムパッケージのアセンブリ化と参照集合の構築 ----

    /// <summary>
    /// 案内パッケージ集合を、実際の in-memory アセンブリ参照へ変換する。
    /// </summary>
    /// <remarks>
    /// Core を先にコンパイルし、方言/EF Core は Core アセンブリを参照してコンパイルする（参照順を守る）。
    /// 各パッケージのコンパイルは、そのパッケージが正当に必要とする外部依存だけを許して行い（依存最小性の担保）、
    /// 生成コードのコンパイルには「案内された各パッケージ＋その参照」を渡す。
    /// </remarks>
    private static IReadOnlyList<MetadataReference> BuildPackageReferences(
        IReadOnlyList<string> packages
    )
    {
        var references = new List<MetadataReference>();

        // Core は必ず先に組む（他パッケージが参照する）
        var coreImage = CompilePackageAssembly(
            RuntimePackages.Core,
            [PackageRenderer.RenderCore()],
            RuntimeReferenceSet.CoreOnly,
            corePeImage: null
        );
        references.Add(MetadataReference.CreateFromImage(coreImage));

        if (packages.Contains(RuntimePackages.SqlServer))
        {
            var image = CompilePackageAssembly(
                RuntimePackages.SqlServer,
                [PackageRenderer.RenderCore(), PackageRenderer.RenderSqlServer()],
                RuntimeReferenceSet.SqlServer,
                corePeImage: coreImage
            );
            references.Add(MetadataReference.CreateFromImage(image));
        }

        if (packages.Contains(RuntimePackages.Sqlite))
        {
            var image = CompilePackageAssembly(
                RuntimePackages.Sqlite,
                [PackageRenderer.RenderCore(), PackageRenderer.RenderSqlite()],
                RuntimeReferenceSet.Sqlite,
                corePeImage: coreImage
            );
            references.Add(MetadataReference.CreateFromImage(image));
        }

        if (packages.Contains(RuntimePackages.EntityFrameworkCore))
        {
            var image = CompilePackageAssembly(
                RuntimePackages.EntityFrameworkCore,
                [PackageRenderer.RenderCore(), PackageRenderer.RenderEfCore()],
                RuntimeReferenceSet.EfCore,
                corePeImage: coreImage
            );
            references.Add(MetadataReference.CreateFromImage(image));
        }

        if (packages.Contains(RuntimePackages.InMemory))
        {
            var image = CompilePackageAssembly(
                RuntimePackages.InMemory,
                [PackageRenderer.RenderCore(), PackageRenderer.RenderInMemory()],
                RuntimeReferenceSet.InMemory,
                corePeImage: coreImage
            );
            references.Add(MetadataReference.CreateFromImage(image));
        }

        if (packages.Contains(RuntimePackages.AspNetCore))
        {
            var image = CompilePackageAssembly(
                RuntimePackages.AspNetCore,
                [PackageRenderer.RenderCore(), PackageRenderer.RenderAspNetCore()],
                RuntimeReferenceSet.AspNetCore,
                corePeImage: coreImage
            );
            references.Add(MetadataReference.CreateFromImage(image));
        }

        return references;
    }

    /// <summary>
    /// パッケージソースを 1 アセンブリへコンパイルし、PE イメージ（バイト列）を返す。
    /// </summary>
    /// <remarks>
    /// 方言/EF Core パッケージは Core の型（EntityBase・IRepository 等）を参照するため、Core の PE イメージを
    /// メタデータ参照として渡す。ソースにも Core を含めているのは、Core を単独アセンブリにするための便宜
    /// （Core アセンブリを渡す構成では方言/EF Core ソースだけを渡すと二重定義になるため、方言/EF Core は Core を参照に持ち
    /// 方言/EF Core ソースのみをコンパイルする）。
    /// </remarks>
    private static byte[] CompilePackageAssembly(
        string assemblyName,
        IReadOnlyList<string> allSources,
        RuntimeReferenceSet referenceSet,
        byte[]? corePeImage
    )
    {
        // Core アセンブリを参照する構成では、方言/EF Core ソースだけをコンパイルする（Core は参照から解決）。
        var sources = corePeImage is null ? allSources : allSources.Skip(1).ToArray();

        var references = referenceSet.Build();

        if (corePeImage is not null)
        {
            references = [.. references, MetadataReference.CreateFromImage(corePeImage)];
        }

        var syntaxTrees = sources
            .Select(
                (source, index) =>
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.Latest),
                        path: $"{assemblyName}.{index}.g.cs"
                    )
            )
            .ToArray();

        var compilation = CSharpCompilation.Create(
            $"{assemblyName}.{Guid.NewGuid():N}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);

        emit.Success.Should()
            .BeTrue(
                $"ランタイムパッケージ '{assemblyName}' のアセンブリ化に失敗:{Environment.NewLine}"
                    + string.Join(
                        Environment.NewLine,
                        emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                            .Select(d => d.GetMessage())
                    )
            );

        return peStream.ToArray();
    }

    /// <summary>生成コードを「案内されたパッケージ参照＋BCL＋DI（＋EF Core 生成時のみ EF Core）」だけでコンパイルする</summary>
    /// <remarks>
    /// 生成コードが直接使うのは BCL＋DI（登録拡張）＋案内パッケージ。方言 ADO（SqlClient / Sqlite）はパッケージ側だけが
    /// 参照するため含めない。ただし EF Core 生成時は、生成側の QuickErDbContext（<c>: DbContext</c>）や
    /// AddGeneratedEfCoreRepositories（<c>DbContextOptionsBuilder</c> / <c>AddDbContextFactory</c>）が EF Core の型を
    /// 直接参照するため、EF Core パッケージが案内に含まれるときは EF Core 参照も生成コードのコンパイルへ渡す。
    /// </remarks>
    private static GeneratedCompileResult CompileGenerated(
        CodeGenerationResult result,
        IReadOnlyList<string> packages,
        IReadOnlyList<MetadataReference> packageReferences
    )
    {
        var referenceSet = packages.Contains(RuntimePackages.EntityFrameworkCore)
            ? RuntimeReferenceSet.GeneratedCodeWithEfCore
            : RuntimeReferenceSet.GeneratedCode;
        var references = new List<MetadataReference>(referenceSet.Build());
        references.AddRange(packageReferences);

        var syntaxTrees = result
            .Files.Select(file =>
                CSharpSyntaxTree.ParseText(
                    file.Content,
                    new CSharpParseOptions(LanguageVersion.Latest),
                    path: file.FileName
                )
            )
            .ToArray();

        var compilation = CSharpCompilation.Create(
            $"QuickER.PackageMode.Tests.{Guid.NewGuid():N}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);

        var errors = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

        return new GeneratedCompileResult
        {
            Success = emit.Success && errors.Length == 0,
            Errors = errors,
        };
    }

    private sealed class GeneratedCompileResult
    {
        public required bool Success { get; init; }

        public required IReadOnlyList<Diagnostic> Errors { get; init; }

        public string Describe() =>
            string.Join(
                Environment.NewLine,
                Errors.Select(d =>
                {
                    var span = d.Location.GetLineSpan();
                    return $"[{d.Id}] {span.Path}:{span.StartLinePosition.Line + 1} {d.GetMessage()}";
                })
            );
    }
}

/// <summary>
/// パッケージ／生成コードのコンパイルに渡す参照集合を、許可依存だけを含めて構築するヘルパー。
/// </summary>
/// <remarks>
/// TPA（BCL 全体）をベースに、排他対象アセンブリ（SqlClient / Sqlite / EF Core / DI）をファイル名で除外し、
/// 各構成が許すものだけを明示的に戻す（<see cref="RuntimePackageSourceRendererTests"/> と同じ流儀）。
/// </remarks>
internal sealed class RuntimeReferenceSet
{
    private readonly bool _sqlClient;
    private readonly bool _sqlite;
    private readonly bool _efCore;
    private readonly bool _di;

    private RuntimeReferenceSet(bool sqlClient, bool sqlite, bool efCore, bool di)
    {
        _sqlClient = sqlClient;
        _sqlite = sqlite;
        _efCore = efCore;
        _di = di;
    }

    /// <summary>Core パッケージ: BCL のみ（ADO / EF Core / DI なし）</summary>
    public static RuntimeReferenceSet CoreOnly { get; } = new(false, false, false, false);

    /// <summary>SqlServer パッケージ: BCL＋SqlClient＋DI</summary>
    public static RuntimeReferenceSet SqlServer { get; } = new(true, false, false, true);

    /// <summary>Sqlite パッケージ: BCL＋Microsoft.Data.Sqlite＋DI</summary>
    public static RuntimeReferenceSet Sqlite { get; } = new(false, true, false, true);

    /// <summary>EfCore パッケージ: BCL＋EF Core＋DI</summary>
    public static RuntimeReferenceSet EfCore { get; } = new(false, false, true, true);

    /// <summary>InMemory パッケージ: BCL のみ（ADO / EF Core / DI なし＝DB 非依存を参照集合で証明する）</summary>
    public static RuntimeReferenceSet InMemory { get; } = new(false, false, false, false);

    /// <summary>
    /// AspNetCore パッケージ: BCL（ASP.NET Core 共有フレームワークを含む）＋DI。ADO / EF Core は参照しない
    /// </summary>
    /// <remarks>
    /// ASP.NET Core のアセンブリはテストホストの共有フレームワーク経由で TPA に含まれるため除外対象に入れていない。
    /// DI はサーバー固定エンジンがリポジトリ・<c>ILoggerFactory</c> を解決するために必要（実プロジェクトでは
    /// <c>Microsoft.AspNetCore.App</c> の FrameworkReference が推移的に提供する）。
    /// </remarks>
    public static RuntimeReferenceSet AspNetCore { get; } = new(false, false, false, true);

    /// <summary>生成コード: BCL＋DI（登録拡張）のみ。方言 ADO / EF Core はパッケージ側だけが参照する</summary>
    public static RuntimeReferenceSet GeneratedCode { get; } = new(false, false, false, true);

    /// <summary>生成コード（EF Core 生成時）: BCL＋DI＋EF Core。生成側の QuickErDbContext / DI 拡張が EF Core 型を直接参照する</summary>
    public static RuntimeReferenceSet GeneratedCodeWithEfCore { get; } =
        new(false, false, true, true);

    private static readonly IReadOnlyList<string> ExclusiveAssemblyFileNames =
    [
        "Microsoft.Data.SqlClient",
        "Microsoft.Data.Sqlite",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.EntityFrameworkCore.Abstractions",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        // 配布ランタイム 7 パッケージ。テストプロジェクトは公開 API 面のスナップショット検証
        // （PublicApi/）のためだけにこれらを参照しており、その実アセンブリが TPA に載る。
        // 本テストは同じ型をソースからその場でコンパイルするため、混ざると同名型が 2 アセンブリに
        // 存在して CS0433（型があいまい）になる。参照集合からは常に外す。
        "QuickER.Runtime",
        "QuickER.Runtime.SqlServer",
        "QuickER.Runtime.Sqlite",
        "QuickER.Runtime.EntityFrameworkCore",
        "QuickER.Runtime.InMemory",
        "QuickER.Runtime.AspNetCore",
        "QuickER.Runtime.Sync",
    ];

    public IReadOnlyList<MetadataReference> Build()
    {
        var referencesByPath = new Dictionary<string, MetadataReference>(
            StringComparer.OrdinalIgnoreCase
        );

        void AddPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            referencesByPath.TryAdd(path, MetadataReference.CreateFromFile(path));
        }

        var trustedAssembliesPaths = (
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
        )?.Split(Path.PathSeparator);

        if (trustedAssembliesPaths is not null)
        {
            foreach (var path in trustedAssembliesPaths)
            {
                var fileName = Path.GetFileNameWithoutExtension(path);

                if (ExclusiveAssemblyFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddPath(path);
            }
        }

        if (_di)
        {
            AddPath(
                typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection)
                    .Assembly
                    .Location
            );
            AddPath(
                typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)
                    .Assembly
                    .Location
            );
        }

        if (_sqlClient)
        {
            AddPath(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location);
        }

        if (_sqlite)
        {
            AddPath(typeof(Microsoft.Data.Sqlite.SqliteConnection).Assembly.Location);
        }

        if (_efCore)
        {
            AddPath(typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly.Location);
            AddPath(
                typeof(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions)
                    .Assembly
                    .Location
            );
            AddPath(typeof(Microsoft.EntityFrameworkCore.DeleteBehavior).Assembly.Location);
        }

        return referencesByPath.Values.ToArray();
    }
}
