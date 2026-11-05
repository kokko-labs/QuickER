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

    /// <summary>Files から .g.md を 1 つだけ取り出す（無ければ null）</summary>
    private static GeneratedFile? MarkdownFile(CodeGenerationResult result) =>
        result.Files.SingleOrDefault(file =>
            file.FileName.EndsWith(".g.md", StringComparison.OrdinalIgnoreCase)
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

    /// <summary>
    /// 表セルのテーブル名・説明が C# エスケープ済みの値でなく図の生の値（Markdown 安全化のみ）で
    /// 載ることを検証する。従来はテーブル名セルへ [Table] 用の C# リテラルエスケープ（\"）が漏れ、
    /// 説明セルも C# エスケープ済みの値を Markdown 安全化していた
    /// </summary>
    /// <summary>
    /// 説明に含まれる NEL・FF・LINE SEPARATOR・PARAGRAPH SEPARATOR が表の行を割らないことを検証する（英日とも）。
    /// 描画後の ReplaceLineEndings はこれらも改行にするので、CR / LF だけを畳んでいた頃は表の行が分断されていた
    /// </summary>
    [Fact(DisplayName = ".g.md の表セルは CR / LF 以外の改行文字でも行が割れない")]
    public void Markdown_Cells_FoldEveryLineBreakCharacter()
    {
        var diagram = BuildDiagram();
        diagram.Entities[0].Columns[1].Description =
            $"a{(char)0x85}b{(char)0x0C}c{(char)0x2028}d{(char)0x2029}e";

        var result = Generate(
            diagram,
            new CodeGenerationOptions
            {
                GenerateApiDocs = true,
                ApiDocsLanguage = ApiDocsLanguage.Both,
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        foreach (var file in new[] { EnglishMarkdownFile(result)!, JapaneseMarkdownFile(result)! })
        {
            file.Content.Should().Contain("a b c d e", file.FileName);
            file.Content.Split(Environment.NewLine)
                .Should()
                .Contain(line => line.StartsWith('|') && line.Contains("a b c d e"), file.FileName);
        }
    }

    /// <summary>
    /// 例（CRUD の使用例）は Repository を持つ最初のエンティティを使い、Repository の無いエンティティ（複合主キー）を使わないことを検証する
    /// </summary>
    [Fact(DisplayName = "使用例は Repository を持つ最初のエンティティを使う")]
    public void Example_UsesFirstEntityWithRepository()
    {
        var diagram = BuildDiagram();

        // 先頭の customers に複合主キーを与える＝Repository が生成されない
        diagram.Entities[0].Columns[1].IsPrimaryKey = true;

        var result = Generate(
            diagram,
            new CodeGenerationOptions { GenerateApiDocs = true, GenerateRepositories = true }
        );
        var content = EnglishMarkdownFile(result)!.Content;

        content.Should().Contain("new OrderEntity()").And.NotContain("new CustomerEntity()");
    }

    [Fact(DisplayName = ".g.md の表セルは生の名前・説明（Markdown 安全化のみ）で載る")]
    public void Markdown_Cells_UseRawValuesWithMarkdownEscaping()
    {
        var diagram = BuildDiagram();
        diagram.Entities[0].Description = "say \"hi\" | pipe";
        diagram.Entities[1].TableName = "or\"ders";
        diagram.Entities[0].Columns[1].Description = "col \"desc\" | pipe";

        var result = Generate(diagram, new CodeGenerationOptions { GenerateApiDocs = true });

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));
        var content = MarkdownFile(result)!.Content;

        // テーブル名は生のまま（C# リテラルエスケープの \" が読者に見えない）
        content.Should().Contain("or\"ders").And.NotContain("or\\\"ders");

        // 説明は生の値へ Markdown 安全化（| のエスケープ）だけが掛かる
        content.Should().Contain("say \"hi\" \\| pipe").And.NotContain("say \\\"hi\\\"");
        content.Should().Contain("col \"desc\" \\| pipe").And.NotContain("col \\\"desc\\\"");
    }

    [Fact(
        DisplayName = "単一方言既定構成の内容: エンティティ名・プロパティ名・型トークン・AddGeneratedSqlServerRepositories を含む"
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
        // 単一方言（sqlserver）既定はエンジン別名 AddGeneratedSqlServerRepositories
        markdown.Should().Contain("AddGeneratedSqlServerRepositories");
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
            GenerateEfCoreRepositories = true,
        };

        var markdown = MarkdownFile(Generate(BuildDiagram(), options))!.Content;

        markdown.Should().Contain("AddGeneratedEfCoreRepositories");
        markdown.Should().NotContain("AddGeneratedSqlServerRepositories(");
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
            GenerateEfCoreRepositories = false,
            GenerateInMemoryRepositories = false,
        };

        var result = Generate(BuildDiagram(), options);

        result.HasErrors.Should().BeFalse();
        var markdown = MarkdownFile(result)!.Content;

        // エンティティ一覧・プロパティ表は出るが、データアクセス・使い方の節は丸ごと省略される（英語正本）
        markdown.Should().Contain("CustomerEntity");
        markdown.Should().NotContain("## Data access API");
        markdown.Should().NotContain("## Usage");
        markdown.Should().NotContain("AddGenerated");
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

    [Fact(DisplayName = "日本語だけのとき、.ja.g.md は両方出すときと同じ日本語の内容になる")]
    public void JapaneseOnly_ContentMatchesJapaneseOfBoth()
    {
        var japaneseOnly = Generate(
            BuildDiagram(),
            new CodeGenerationOptions
            {
                GenerateApiDocs = true,
                ApiDocsLanguage = ApiDocsLanguage.Japanese,
                GenerateRepositories = true,
            }
        );
        var both = Generate(
            BuildDiagram(),
            new CodeGenerationOptions
            {
                GenerateApiDocs = true,
                ApiDocsLanguage = ApiDocsLanguage.Both,
                GenerateRepositories = true,
            }
        );

        japaneseOnly.HasErrors.Should().BeFalse();
        var markdown = JapaneseMarkdownFile(japaneseOnly)!.Content;
        markdown.Should().Contain("## データアクセス API");
        markdown.Should().Be(JapaneseMarkdownFile(both)!.Content);
    }

    [Fact(DisplayName = "日本語版（.ja.g.md）は日本語の見出しと DI 説明を含む")]
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

        markdown.Should().Contain("## データアクセス API");
        markdown.Should().Contain("## 使い方");
        markdown.Should().Contain("QuickER 版 Repository（SqlServer）を DI コンテナへ登録します。");
        // 拡張メソッド名（コード）は言語非依存で共通に出る
        markdown.Should().Contain("AddGeneratedSqlServerRepositories");
        // 英語の見出しは日本語版には出ない
        markdown.Should().NotContain("## Data access API");
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
