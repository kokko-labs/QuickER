using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// EF Core の Fluent 構成が、スキーマで修飾された図のテーブル名（<c>sales.Orders</c>）をスキーマとテーブルに分けて
/// <c>ToTable("Orders", "sales")</c> を出し、QuickER 版 Repository と同じテーブル（<c>[sales].[Orders]</c>）を指すことを固定する
/// </summary>
/// <remarks>
/// EF Core の <c>ToTable(name)</c> は名前を <c>.</c> で分けないので、分けずに渡すと既定のスキーマの、名前に <c>.</c> を含む
/// テーブル <c>[sales.Orders]</c> を指していた。分け方は QuickER 版 Repository・DDL と同じ <c>SchemaQualifiedName</c>（最初の <c>.</c>）。
/// スキーマの無いテーブルの出力は変わらない（フィクスチャのドリフトテストが固定する）。
/// </remarks>
public sealed class EfCoreSchemaQualifiedTableTests
{
    private const string Probe = """
        using System;
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        namespace Sample.Domain;

        public static class TableProbe
        {
            public static string[] Run(Func<DbContextOptionsBuilder<QuickErDbContext>, DbContextOptionsBuilder<QuickErDbContext>> configure)
            {
                using var context = new QuickErDbContext(configure(new DbContextOptionsBuilder<QuickErDbContext>()).Options);

                return context
                    .Model.GetEntityTypes()
                    .Select(type => (string)typeof(TableProbe).GetMethod(nameof(Query))!.MakeGenericMethod(type.ClrType).Invoke(null, [context])!)
                    .ToArray();
            }

            public static string Query<T>(DbContext context) where T : class => context.Set<T>().ToQueryString();
        }
        """;

    [Fact(
        DisplayName = "スキーマで修飾されたテーブル名は ToTable(テーブル, スキーマ) を出し、素のテーブル名は ToTable(テーブル) のまま"
    )]
    public void ToTable_SplitsSchemaQualifiedName()
    {
        var code = string.Concat(Generate().Files.Select(file => file.Content));

        code.Should().Contain("entity.ToTable(\"orders\", \"sales\");");
        code.Should()
            .Contain(
                "entity.ToTable(\"line.items\", \"sales\");",
                "2 つ目以降の . はテーブル名の一部"
            );
        code.Should().Contain("entity.ToTable(\"notes\");");
    }

    [Fact(DisplayName = "ToTable のテーブルとスキーマは、どちらも C# リテラルとしてエスケープする")]
    public void ToTable_EscapesBothArguments()
    {
        var diagram = Diagram("q\"s.t\\x", "lines", "notes");

        var code = string.Concat(Generate(diagram).Files.Select(file => file.Content));

        code.Should().Contain("entity.ToTable(\"t\\\\x\", \"q\\\"s\");");
    }

    [Fact(
        DisplayName = "生成した DbContext は、スキーマで修飾されたテーブルを [スキーマ].[テーブル] として読む（QuickER 版 Repository と同じテーブル）"
    )]
    public void GeneratedDbContext_ReadsSchemaQualifiedTable()
    {
        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            Generate(),
            $"QuickER.EfCoreSchema.Tests.{Guid.NewGuid():N}",
            Probe
        );
        var run = assembly.GetType("Sample.Domain.TableProbe")!.GetMethod("Run")!;
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(
            assembly.GetType("Sample.Domain.QuickErDbContext")!
        );
        var configure = typeof(EfCoreSchemaQualifiedTableTests)
            .GetMethod(
                nameof(Configure),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            )!
            .MakeGenericMethod(assembly.GetType("Sample.Domain.QuickErDbContext")!)
            .CreateDelegate(typeof(Func<,>).MakeGenericType(builderType, builderType));

        var sql = (string[])run.Invoke(null, [configure])!;

        sql.Should().HaveCount(3);
        sql.Should().ContainSingle(s => s.Contains("FROM [sales].[orders]"));
        sql.Should().ContainSingle(s => s.Contains("FROM [sales].[line.items]"));
        sql.Should().ContainSingle(s => s.Contains("FROM [notes]"));
    }

    private static DbContextOptionsBuilder<TContext> Configure<TContext>(
        DbContextOptionsBuilder<TContext> builder
    )
        where TContext : DbContext =>
        builder
            .UseSqlServer("Server=localhost;Database=unused;Trusted_Connection=True")
            .IgnoreServiceProviderCacheLimit();

    private static CodeGenerationResult Generate(ErDiagram? diagram = null)
    {
        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram ?? Diagram("sales.orders", "sales.line.items", "notes"),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEfCoreRepositories = true,
                GenerateEditModels = false,
                GenerateMappers = false,
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));
        return result;
    }

    /// <summary>主キー 1 列だけのテーブルを 3 つ持つ図</summary>
    private static ErDiagram Diagram(string orders, string lines, string notes)
    {
        Entity Table(string name) =>
            new()
            {
                TableName = name,
                Columns =
                [
                    new Column
                    {
                        Name = "id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                ],
            };

        return new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities = [Table(orders), Table(lines), Table(notes)],
        };
    }
}
