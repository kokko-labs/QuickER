using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;
using Xunit;

namespace QuickER.Tests.Generator;

/// <summary>
/// API リファレンス Markdown（<c>.g.md</c>）同梱出力のコア機能を検証するテストクラス。
/// </summary>
/// <remarks>
/// 既定 OFF での不出力、ON での 1 ファイル出力・命名対称・内容・決定性、生成モード追従（EF Core / マルチ方言）、
/// Entity のみ構成での節省略、検証エラー時の不出力、<see cref="GeneratedFileWriter"/> の拡張子ガードを守る。
/// </remarks>
public sealed class ApiReferenceDocTests
{
    /// <summary>2 エンティティ・1対多・int/string/decimal のみ（方言可搬）の小さな ER 図（EcOrder 相当）</summary>
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

    /// <summary>SQL Server 型解決で単一辞書生成を行うヘルパ</summary>
    private static CodeGenerationResult Generate(
        ErDiagram diagram,
        CodeGenerationOptions options
    ) =>
        new CSharpCodeGenerationService().Generate(
            diagram,
            SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            options
        );

    /// <summary>Files から .g.md を 1 つだけ取り出す（無ければ null）</summary>
    private static GeneratedFile? MarkdownFile(CodeGenerationResult result) =>
        result.Files.SingleOrDefault(file =>
            file.FileName.EndsWith(".g.md", StringComparison.OrdinalIgnoreCase)
        );

    [Fact(DisplayName = "既定（GenerateApiDocs=false）では .g.md を出力しない")]
    public void Default_DoesNotEmitMarkdown()
    {
        var result = Generate(BuildDiagram(), new CodeGenerationOptions());

        result.HasErrors.Should().BeFalse();
        result
            .Files.Should()
            .NotContain(file =>
                file.FileName.EndsWith(".g.md", StringComparison.OrdinalIgnoreCase)
            );
    }

    [Fact(
        DisplayName = "ON で .g.md が 1 つ出力され、ファイル名が OutputFileName のベース名と対称になる"
    )]
    public void On_EmitsSingleMarkdown_WithSymmetricFileName()
    {
        var options = new CodeGenerationOptions
        {
            OutputFileName = "Foo.g.cs",
            GenerateApiDocs = true,
        };

        var result = Generate(BuildDiagram(), options);

        result.HasErrors.Should().BeFalse();
        result
            .Files.Count(file =>
                file.FileName.EndsWith(".g.md", StringComparison.OrdinalIgnoreCase)
            )
            .Should()
            .Be(1);
        MarkdownFile(result)!.FileName.Should().Be("Foo.g.md");
    }

    [Fact(DisplayName = "分割生成でも Markdown は 1 ファイルのみ出力される")]
    public void SplitFiles_EmitsSingleMarkdown()
    {
        var options = new CodeGenerationOptions
        {
            OutputFileName = "EcOrder.g.cs",
            GenerateApiDocs = true,
            SplitFilesByCategory = true,
        };

        var result = Generate(BuildDiagram(), options);

        result.HasErrors.Should().BeFalse();
        result
            .Files.Count(file =>
                file.FileName.EndsWith(".g.md", StringComparison.OrdinalIgnoreCase)
            )
            .Should()
            .Be(1);
        MarkdownFile(result)!.FileName.Should().Be("EcOrder.g.md");
    }

    [Fact(
        DisplayName = "単一方言既定構成の内容: エンティティ名・プロパティ名・型トークン・AddGeneratedRepositories を含む"
    )]
    public void SingleDialect_Content_ContainsSchemaAndDefaultDi()
    {
        var options = new CodeGenerationOptions
        {
            GenerateApiDocs = true,
            GenerateRepositories = true,
        };

        // CanonicalTypeToken はプロバイダ層の後処理（CanonicalTypeTokenAttacher）で付加されるため、
        // 実生成と同じく DiagramCodeGenerator（型解決＋トークン付加）を経由して内容を検証する。
        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            BuildDiagram(),
            options
        );

        result.HasErrors.Should().BeFalse();
        var markdown = MarkdownFile(result)!.Content;

        markdown.Should().Contain("CustomerEntity");
        markdown.Should().Contain("OrderEntity");
        markdown.Should().Contain("CustomerId");
        markdown.Should().Contain("Amount");
        // CanonicalTypeToken（方言中立の型トークン）。int / decimal は方言に依らず同一トークン
        markdown.Should().Contain("int32");
        markdown.Should().Contain("decimal(10,2)");
        // 単一方言既定は AddGeneratedRepositories
        markdown.Should().Contain("AddGeneratedRepositories");
        // docs/code-generation.md へのリンク
        markdown
            .Should()
            .Contain("https://github.com/kokko-labs/QuickER/blob/main/docs/code-generation.md");
    }

    [Fact(DisplayName = "EF Core 構成では AddGeneratedEfCoreRepositories を含む")]
    public void EfCore_Content_ContainsEfCoreDi()
    {
        var options = new CodeGenerationOptions
        {
            GenerateApiDocs = true,
            GenerateRepositories = false,
            GenerateEfCore = true,
        };

        var markdown = MarkdownFile(Generate(BuildDiagram(), options))!.Content;

        markdown.Should().Contain("AddGeneratedEfCoreRepositories");
        markdown.Should().NotContain("AddGeneratedRepositories(");
    }

    [Fact(DisplayName = "マルチ方言構成では方言別拡張名（SqlServer / Sqlite）を含む")]
    public void MultiDialect_Content_ContainsDialectSpecificDi()
    {
        var diagram = BuildDiagram();
        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            ["sqlite"] = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram),
        };
        var options = new CodeGenerationOptions
        {
            GenerateApiDocs = true,
            GenerateRepositories = true,
            RepositoryDialects = ["sqlserver", "sqlite"],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            byDialect["sqlserver"],
            byDialect,
            options
        );

        result.HasErrors.Should().BeFalse();
        var markdown = MarkdownFile(result)!.Content;

        markdown.Should().Contain("AddGeneratedSqlServerRepositories");
        markdown.Should().Contain("AddGeneratedSqliteRepositories");
    }

    [Fact(
        DisplayName = "Entity のみ生成（Repository/EF Core/InMemory すべて false）ではデータアクセス節が出ない"
    )]
    public void EntityOnly_OmitsDataAccessSections()
    {
        var options = new CodeGenerationOptions
        {
            GenerateApiDocs = true,
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = false,
            GenerateEfCore = false,
            GenerateInMemoryRepositories = false,
        };

        var result = Generate(BuildDiagram(), options);

        result.HasErrors.Should().BeFalse();
        var markdown = MarkdownFile(result)!.Content;

        // エンティティ一覧・プロパティ表は出るが、データアクセス・使い方の節は丸ごと省略される
        markdown.Should().Contain("CustomerEntity");
        markdown.Should().NotContain("## データアクセス API");
        markdown.Should().NotContain("## 使い方");
        markdown.Should().NotContain("AddGenerated");
    }

    [Fact(DisplayName = "決定性: 同一入力で 2 回生成した Markdown はバイト一致する")]
    public void Deterministic_SameInputProducesIdenticalMarkdown()
    {
        var options = new CodeGenerationOptions { GenerateApiDocs = true };

        var first = MarkdownFile(Generate(BuildDiagram(), options))!.Content;
        var second = MarkdownFile(Generate(BuildDiagram(), options))!.Content;

        second.Should().Be(first);
    }

    [Fact(DisplayName = "検証エラー時（エンティティ 0 件）は .g.md も出力しない")]
    public void ValidationError_DoesNotEmitMarkdown()
    {
        var options = new CodeGenerationOptions { GenerateApiDocs = true };

        var result = new CSharpCodeGenerationService().Generate(
            new ErDiagram(),
            new Dictionary<Guid, CSharpTypeInfo>(),
            options
        );

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
    }

    [Fact(DisplayName = "GeneratedFileWriter は .g.md を書き出せる")]
    public void Writer_WritesMarkdownFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quicker_apidoc_" + Guid.NewGuid());

        try
        {
            var result = new CodeGenerationResult
            {
                Files = [new GeneratedFile { FileName = "Foo.g.md", Content = "# doc" }],
            };

            var written = new GeneratedFileWriter().WriteFiles(directory, result);

            written.Should().ContainSingle();
            File.ReadAllText(written[0]).Should().Be("# doc");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact(DisplayName = "GeneratedFileWriter は .g なしの .md を従来どおり拒否する")]
    public void Writer_RejectsPlainMarkdown()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quicker_apidoc_" + Guid.NewGuid());

        try
        {
            var result = new CodeGenerationResult
            {
                Files = [new GeneratedFile { FileName = "Foo.md", Content = "# doc" }],
            };

            var act = () => new GeneratedFileWriter().WriteFiles(directory, result);

            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
