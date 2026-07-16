using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Sqlite;
using QuickER.SqlServer;
using Xunit;

namespace QuickER.Tests.Generator;

/// <summary>
/// QuickER 版 Repository のマルチターゲット生成（複数方言）の Generator 側基盤を検証する。
/// </summary>
/// <remarks>
/// <para>
/// 本フェーズ（M1）で提供するのは「オプションの実効方言解決・後方互換」「マルチ辞書での型解決 API」
/// 「方言間 C# 型不一致の診断」「sqlserver がターゲットに含まれるときの <c>[SqlColumnType]</c> 補完」。
/// </para>
/// <para>
/// 方言別 namespace への実装分割（別ファイル／連結レイアウト）はランタイム <c>SqlQuery&lt;T&gt;</c> が
/// 方言別 SQL を型内へ埋め込む現行設計のため未着手（報告参照）。ここではその手前までの Generator 基盤を守る。
/// </para>
/// </remarks>
public sealed class MultiTargetRepositoryGenerationTests
{
    /// <summary>2 エンティティ・1対多・int/string/decimal のみ（方言可搬）の小さな ER 図</summary>
    private static ErDiagram BuildDiagram()
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
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "varchar(50)",
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
                    SourceColumnId = customerPk,
                    TargetColumnId = orderFk,
                },
            ],
        };
    }

    // ---- オプション後方互換・実効方言解決 ----

    [Fact(DisplayName = "RepositoryDialects（単一要素）指定は実効方言 1 つへ解決される")]
    public void EffectiveDialects_SingleElement_ResolvesToOne()
    {
        var options = new CodeGenerationOptions { RepositoryDialects = ["sqlite"] };
        options.EffectiveRepositoryDialects.Should().Equal("sqlite");
    }

    [Fact(DisplayName = "空リストは既定 sqlserver へフォールバックする")]
    public void EffectiveDialects_EmptyListFallsBackToSqlServer()
    {
        var options = new CodeGenerationOptions { RepositoryDialects = [] };
        options.EffectiveRepositoryDialects.Should().Equal("sqlserver");
    }

    [Fact(DisplayName = "未指定（null）は既定 sqlserver へフォールバックする")]
    public void EffectiveDialects_NullFallsBackToSqlServer()
    {
        var options = new CodeGenerationOptions();
        options.EffectiveRepositoryDialects.Should().Equal("sqlserver");
    }

    [Fact(DisplayName = "実効方言は重複を大小無視で除去し順序を保つ")]
    public void EffectiveDialects_DeduplicatesCaseInsensitive()
    {
        var options = new CodeGenerationOptions
        {
            RepositoryDialects = ["SqlServer", "sqlite", "sqlserver"],
        };
        options.EffectiveRepositoryDialects.Should().Equal("SqlServer", "sqlite");
    }

    [Fact(DisplayName = "空要素・空白は除去される")]
    public void EffectiveDialects_TrimsAndDropsBlanks()
    {
        var options = new CodeGenerationOptions { RepositoryDialects = [" sqlserver ", "", "  "] };
        options.EffectiveRepositoryDialects.Should().Equal("sqlserver");
    }

    [Fact(DisplayName = "未対応方言を指定すると ArgumentException になる")]
    public void EffectiveDialects_UnsupportedThrows()
    {
        var options = new CodeGenerationOptions { RepositoryDialects = ["sqlserver", "postgres"] };
        var act = () => _ = options.EffectiveRepositoryDialects;
        act.Should().Throw<ArgumentException>().WithMessage("*postgres*");
    }

    [Fact(DisplayName = "未対応方言指定は生成時に診断エラーへ変換される（例外を投げない）")]
    public void Generate_UnsupportedDialect_ReturnsErrorDiagnostic()
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>
        {
            ["sqlserver"] = primary,
        };
        var options = new CodeGenerationOptions { RepositoryDialects = ["bogus"] };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error && d.Message.Contains("bogus")
            );
    }

    // ---- マルチ辞書 API: 単一方言と同等の出力（後方互換） ----

    [Fact(DisplayName = "単一方言のマルチ辞書呼び出しは 3 引数版と同じ出力になる（後方互換）")]
    public void Generate_SingleDialectViaMultiApi_MatchesLegacyOverload()
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var options = new CodeGenerationOptions { NamespaceName = "Sample.Domain" };

        var legacy = new CSharpCodeGenerationService().Generate(diagram, primary, options);
        var viaMulti = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>
            {
                ["sqlserver"] = primary,
            },
            options
        );

        viaMulti.Files.Select(f => f.Content).Should().Equal(legacy.Files.Select(f => f.Content));
    }

    // ---- 型不一致診断 ----

    [Fact(DisplayName = "方言間で共有 Entity の C# 型が食い違うと診断エラーになる")]
    public void Generate_TypeMismatchBetweenDialects_ReturnsErrorDiagnostic()
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);

        // sqlite 辞書を人工的に食い違わせる（customer_id を string にする）
        var customerId = diagram.Entities[0].Columns[0].Id;
        var tampered = new Dictionary<Guid, CSharpTypeInfo>(
            SqliteCSharpTypeMapper.ResolveColumnTypes(diagram)
        )
        {
            [customerId] = new CSharpTypeInfo { TypeName = "string", IsReferenceType = true },
        };

        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = primary,
            ["sqlite"] = tampered,
        };

        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            RepositoryDialects = ["sqlserver", "sqlite"],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error
                && d.Message.Contains("customer_id")
                // 型不一致診断であることを表示言語に依存しないトークン（方言名・食い違う型名）で確認する
                && d.Message.Contains("sqlite")
                && d.Message.Contains("string")
            );
    }

    [Fact(DisplayName = "方言間で C# 型が一致する（可搬型）なら診断エラーは出ない")]
    public void Generate_MatchingTypesAcrossDialects_NoMismatchError()
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = primary,
            ["sqlite"] = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram),
        };
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            RepositoryDialects = ["sqlserver", "sqlite"],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result.HasErrors.Should().BeFalse();
    }

    // ---- [SqlColumnType] 補完（図が非 sqlserver でも sqlserver 辞書から補完） ----

    [Fact(
        DisplayName = "図の方言が sqlite でも sqlserver がターゲットに含まれれば [SqlColumnType] が Entity へ補完される"
    )]
    public void Generate_SqliteDiagramWithSqlServerTarget_EmitsSqlColumnTypeAttribute()
    {
        var diagram = BuildDiagram();

        // 主辞書＝図の方言（sqlite。SqlDbTypeName は付かない）
        var primary = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram);
        primary
            .Values.Should()
            .OnlyContain(t => t.SqlDbTypeName == null, "sqlite 辞書は SqlDbType を持たない");

        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlite"] = primary,
            ["sqlserver"] = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
        };

        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            RepositoryDialects = ["sqlite", "sqlserver"],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result.HasErrors.Should().BeFalse();
        var code = result.Files.Single().Content;
        // sqlserver 辞書由来の SqlDbType メタ情報が Entity 属性として出る（補完の証明）
        code.Should().Contain("[SqlColumnType(SqlDbType.");
    }

    [Fact(
        DisplayName = "sqlserver がターゲットに含まれないなら [SqlColumnType] は補完されない（依存排他）"
    )]
    public void Generate_SqliteOnly_DoesNotEmitSqlColumnTypeAttribute()
    {
        var diagram = BuildDiagram();
        var primary = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram);
        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlite"] = primary,
        };
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            RepositoryDialects = ["sqlite"],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result.HasErrors.Should().BeFalse();
        var code = result.Files.Single().Content;
        // 属性の定義・付与は出ない（"SqlColumnType" は sqlite 実装の AddWithValue フォールバックの doc コメントに現れるため、
        // 付与構文 "[SqlColumnType(" と属性クラス宣言で判定する）
        code.Should().NotContain("[SqlColumnType(");
        code.Should().NotContain("class SqlColumnTypeAttribute");
        code.Should().NotContain("Microsoft.Data.SqlClient");
    }

    // ---- M2b: マルチターゲットレイアウト（契約 1 回＋方言別 namespace 実装＋keyed DI） ----

    /// <summary>2 方言（sqlserver / sqlite）のマルチターゲット出力を生成する（非分割・EF Core なし）</summary>
    private static CodeGenerationResult GenerateMultiTarget(bool split = false)
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = primary,
            ["sqlite"] = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram),
        };
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            GenerateRepositories = true,
            RepositoryDialects = ["sqlserver", "sqlite"],
            SplitFilesByCategory = split,
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result.HasErrors.Should().BeFalse();

        return result;
    }

    [Fact(DisplayName = "マルチ方言（非分割）は契約を 1 回・方言別 namespace に両実装を出す")]
    public void MultiDialect_NonSplit_ContractOnceAndDialectNamespaces()
    {
        var code = GenerateMultiTarget().Files.Single().Content;

        // 契約は 1 回だけ（IRepository / SqlQuery / I{Entity}Repository）
        System
            .Text.RegularExpressions.Regex.Matches(
                code,
                @"public partial interface IRepository<TEntity, TKey>"
            )
            .Count.Should()
            .Be(1, "共通契約 IRepository は 1 回だけ出力される");
        System
            .Text.RegularExpressions.Regex.Matches(code, @"public sealed class SqlQuery<TEntity>")
            .Count.Should()
            .Be(1, "方言中立の SqlQuery は 1 回だけ出力される");
        System
            .Text.RegularExpressions.Regex.Matches(
                code,
                @"public partial interface ICustomerRepository"
            )
            .Count.Should()
            .Be(1, "エンティティ別契約インターフェイスは 1 回だけ出力される");

        // 方言別 namespace ブロックが両方出る
        code.Should().Contain("namespace Sample.Domain.Repositories.SqlServer");
        code.Should().Contain("namespace Sample.Domain.Repositories.Sqlite");

        // 各方言 namespace にエンティティ別 Repository 実装が出る（両方言に CustomerRepository）
        code.Should().Contain("class CustomerRepository");
        code.Should().Contain(": SqlServerRepository<CustomerEntity, int>(");
        code.Should().Contain(": SqliteRepository<CustomerEntity, int>(");

        // 両実装とも共有契約 I{Entity}Repository を実装する（契約は契約 namespace 側の単一型）
        System
            .Text.RegularExpressions.Regex.Matches(code, @"ICustomerRepository \{ \}")
            .Count.Should()
            .Be(2, "sqlserver / sqlite の両実装が同一の ICustomerRepository を実装する");
    }

    [Fact(DisplayName = "マルチ方言（非分割）は方言別 DI 拡張＋keyed 版を出す")]
    public void MultiDialect_NonSplit_EmitsDialectNamedDiAndKeyedOverloads()
    {
        var code = GenerateMultiTarget().Files.Single().Content;

        // 方言別名の DI 拡張クラス・メソッド（従来の AddGeneratedRepositories は出さない）
        code.Should().Contain("AddGeneratedSqlServerRepositories");
        code.Should().Contain("AddGeneratedSqliteRepositories");
        code.Should()
            .NotContain(
                "AddGeneratedRepositories(",
                "マルチ方言では方言別名の DI 拡張のみを出す（従来名は出さない）"
            );

        // keyed 版（object? serviceKey 付き）と AddKeyedScoped/AddKeyedSingleton
        code.Should().Contain("object? serviceKey");
        code.Should().Contain("AddKeyedScoped<ICustomerRepository>");
        code.Should().Contain("AddKeyedSingleton<ISqlExecutor>");
        code.Should().Contain("AddKeyedSingleton<ISqlConnectionFactory>");
    }

    [Fact(
        DisplayName = "マルチ方言（分割）は契約ファイルに ADO ゼロ・方言別ファイルに自方言 ADO のみ"
    )]
    public void MultiDialect_Split_AdoUsingsPlacedPerDialectFile()
    {
        var files = GenerateMultiTarget(split: true).Files.ToDictionary(f => f.FileName);

        // 方言別ファイルが生成される
        files.Keys.Should().Contain("Repositories.g.cs");
        files.Keys.Should().Contain("Repositories.SqlServer.g.cs");
        files.Keys.Should().Contain("Repositories.Sqlite.g.cs");

        var contract = files["Repositories.g.cs"].Content;
        var sqlServer = files["Repositories.SqlServer.g.cs"].Content;
        var sqlite = files["Repositories.Sqlite.g.cs"].Content;

        // 契約ファイルには ADO 依存が一切出ない
        contract.Should().NotContain("Microsoft.Data.SqlClient");
        contract.Should().NotContain("Microsoft.Data.Sqlite");
        contract.Should().Contain("public partial interface IRepository<TEntity, TKey>");
        contract.Should().Contain("public partial interface ICustomerRepository");

        // sqlserver ファイルには SqlClient のみ（Sqlite ゼロ）
        sqlServer.Should().Contain("using Microsoft.Data.SqlClient;");
        sqlServer.Should().NotContain("Microsoft.Data.Sqlite");
        sqlServer.Should().Contain("using Sample.Domain.Repositories;");

        // sqlite ファイルには Sqlite のみ（SqlClient ゼロ）
        sqlite.Should().Contain("using Microsoft.Data.Sqlite;");
        sqlite.Should().NotContain("Microsoft.Data.SqlClient");
        sqlite.Should().Contain("using Sample.Domain.Repositories;");

        // 契約は契約ファイルにのみ（方言ファイルには IRepository 定義を再掲しない）
        sqlServer.Should().NotContain("public partial interface IRepository<TEntity, TKey>");
        sqlite.Should().NotContain("public partial interface IRepository<TEntity, TKey>");
    }

    [Fact(
        DisplayName = "マルチ方言＋EF Core は診断エラー（QuickER マルチターゲットと EF Core は排他）"
    )]
    public void MultiDialect_WithEfCore_ReturnsErrorDiagnostic()
    {
        var diagram = BuildDiagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = primary,
            ["sqlite"] = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram),
        };
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            GenerateRepositories = true,
            RepositoryDialects = ["sqlserver", "sqlite"],
            GenerateEfCore = true,
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error && d.Message.Contains("EF Core")
            );
    }

    [Theory(
        DisplayName = "マルチ方言出力（両方言同梱・keyed 利用）がエラー・警告 0 でコンパイルできる"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiDialect_CompilesWithKeyedResolution(bool split)
    {
        var result = GenerateMultiTarget(split);

        // keyed DI で 2 接続を登録し、[FromKeyedServices] で方言別実装を解決する利用側コード断片を同梱してコンパイルする。
        // これにより契約の単一共有型・方言別実装の keyed 解決が実際に型検査を通ることを検証する。
        var usage =
            @"// <auto-generated />
#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Sample.Domain.Repositories;
using Sample.Domain.Repositories.SqlServer;
using Sample.Domain.Repositories.Sqlite;

namespace Sample.Domain.Usage;

public static class MultiTargetUsage
{
    public static ICustomerRepository Wire(IServiceCollection services)
    {
        // 非 keyed の単独登録（方言別名）
        services.AddGeneratedSqlServerRepositories(""Server=.;Database=a;Trusted_Connection=True;"");

        // keyed 登録（同一契約 ICustomerRepository を方言別に登録）
        services.AddGeneratedSqlServerRepositories(""server"", ""Server=.;Database=b;Trusted_Connection=True;"");
        services.AddGeneratedSqliteRepositories(""local"", ""Data Source=local.db"");

        var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredKeyedService<ICustomerRepository>(""server"");
        var local = provider.GetRequiredKeyedService<ICustomerRepository>(""local"");
        return server;
    }

    // [FromKeyedServices] で方言別の同一契約を注入解決できる
    public sealed class Consumer(
        [FromKeyedServices(""server"")] ICustomerRepository server,
        [FromKeyedServices(""local"")] ICustomerRepository local
    )
    {
        public ICustomerRepository Server { get; } = server;
        public ICustomerRepository Local { get; } = local;
    }
}
";

        var withUsage = new CodeGenerationResult
        {
            Files =
            [
                .. result.Files,
                new GeneratedFile { FileName = "MultiTargetUsage.g.cs", Content = usage },
            ],
            Diagnostics = result.Diagnostics,
        };

        var compilation = GeneratedCodeCompiler.Compile(
            withUsage,
            assemblyName: $"QuickER.MultiTarget.Tests.{Guid.NewGuid():N}"
        );

        compilation
            .Success.Should()
            .BeTrue(
                $"マルチ方言（split={split}）＋keyed 利用のコンパイルにエラー:{Environment.NewLine}{compilation.DescribeErrors()}"
            );
        compilation
            .Warnings.Should()
            .BeEmpty(
                $"マルチ方言（split={split}）の生成コードに警告:{Environment.NewLine}{compilation.DescribeWarnings()}"
            );
    }
}
