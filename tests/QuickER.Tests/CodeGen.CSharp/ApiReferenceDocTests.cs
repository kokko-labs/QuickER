using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// API リファレンス Markdown（<c>.g.md</c>）同梱出力のコア機能を検証するテストクラス。
/// </summary>
/// <remarks>
/// 既定 OFF での不出力、ON での 1 ファイル出力・命名対称・内容、生成モード追従（EF Core / マルチ方言）、
/// Entity のみ構成での節省略、検証エラー時の不出力、<see cref="GeneratedFileWriter"/> の拡張子ガードを守る。
/// 決定性（同じ入力の 2 回の生成の一致）は <c>CodegenCaseTests</c> の行 <c>relation-deterministic-api-docs</c>（関係 Deterministic）へ移した。
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
                    ColumnPairs = [new(customerPk, orderFk)],
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

    /// <summary>Files から英語版（.g.md だが .ja.g.md ではない）を 1 つだけ取り出す（無ければ null）</summary>
    private static GeneratedFile? EnglishMarkdownFile(CodeGenerationResult result) =>
        result.Files.SingleOrDefault(file =>
            file.FileName.EndsWith(".g.md", StringComparison.OrdinalIgnoreCase)
            && !file.FileName.EndsWith(".ja.g.md", StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>Files から日本語版（.ja.g.md）を 1 つだけ取り出す（無ければ null）</summary>
    private static GeneratedFile? JapaneseMarkdownFile(CodeGenerationResult result) =>
        result.Files.SingleOrDefault(file =>
            file.FileName.EndsWith(".ja.g.md", StringComparison.OrdinalIgnoreCase)
        );

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

    [Fact(DisplayName = "GeneratedFileWriter は .g なしの .md を拒否する")]
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

    [Fact(DisplayName = "英語版（既定）は英語の見出しと DI 説明を含み、日本語見出しを含まない")]
    public void EnglishDoc_ContainsEnglishHeadingsAndDi()
    {
        var options = new CodeGenerationOptions
        {
            GenerateApiDocs = true,
            GenerateRepositories = true,
        };

        var markdown = EnglishMarkdownFile(Generate(BuildDiagram(), options))!.Content;

        markdown.Should().Contain("## Data access API");
        markdown.Should().Contain("## Usage");
        markdown
            .Should()
            .Contain("Registers the QuickER Repository (SqlServer) with the DI container.");
        // 英語正本には日本語の見出しは出ない
        markdown.Should().NotContain("## データアクセス API");
    }

    // 残す理由（P3-4b）：DI 登録の説明文（生成器の固定の文）。日本語の見出し・拡張メソッド名のコード・英語の見出しが出ないことは行 markdown-api-docs-japanese-headings-and-code へ移した
    [Fact(DisplayName = "日本語版（.ja.g.md）は DI 説明を含む")]
    public void JapaneseDoc_ContainsJapaneseHeadingsAndDi()
    {
        var options = new CodeGenerationOptions
        {
            GenerateApiDocs = true,
            ApiDocsLanguage = ApiDocsLanguage.Both,
            GenerateRepositories = true,
        };

        var result = Generate(BuildDiagram(), options);

        result.HasErrors.Should().BeFalse();
        var markdown = JapaneseMarkdownFile(result)!.Content;

        markdown.Should().Contain("QuickER 版 Repository（SqlServer）を DI コンテナへ登録します。");
    }

    [Fact(DisplayName = "GeneratedFileWriter は .g.md をサブフォルダへ書き出せる")]
    public void Writer_WritesMarkdownIntoSubfolder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quicker_apidoc_" + Guid.NewGuid());

        try
        {
            var result = new CodeGenerationResult
            {
                Files =
                [
                    new GeneratedFile
                    {
                        FileName = "ApiDocs.g.md",
                        RelativeDirectory = "docs",
                        Content = "# doc",
                    },
                ],
            };

            var written = new GeneratedFileWriter().WriteFiles(directory, result);

            written.Should().ContainSingle();
            File.ReadAllText(Path.Combine(directory, "docs", "ApiDocs.g.md")).Should().Be("# doc");
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
