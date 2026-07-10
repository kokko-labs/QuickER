using System.IO;
using System.Linq;
using FluentAssertions;
using QuickER.Cli;
using QuickER.Documents;
using QuickER.Model;

namespace QuickER.Tests.Cli;

/// <summary><see cref="CliApp"/> の generate コマンドが ER 図 JSON から実際にコードを書き出すことを検証するテストクラス</summary>
public class CliAppTests
{
    /// <summary>一時フォルダに保存形式の ER 図 JSON を作成する</summary>
    private static (string schemaPath, string outDir, string root) CreateSampleSchema()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QuickERCliTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        var schemaPath = Path.Combine(root, "schema.json");

        var document = new DiagramDocument();
        var entity = new Entity { TableName = "Customer" };
        entity.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(new Column { Name = "Name", DataType = "nvarchar(100)" });
        document.Schema.Entities.Add(entity);
        JsonStorageService.Save(schemaPath, document);

        return (schemaPath, Path.Combine(root, "out"), root);
    }

    /// <summary>generate が JSON スキーマから .g.cs を出力し終了コード 0 を返すことを検証する</summary>
    [Fact(DisplayName = "generate は JSON スキーマからコードを出力する")]
    public async Task Generate_WritesCode_FromSchemaJson()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--namespace",
                "Test.Ns",
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            files.Should().NotBeEmpty();
            File.ReadAllText(files[0]).Should().Contain("namespace Test.Ns");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>名前付きクエリ定義入りのスキーマから、クエリメソッドがコード生成されることを検証する（CLI の end-to-end）</summary>
    [Fact(DisplayName = "generate は名前付きクエリ定義からクエリメソッドを生成する")]
    public async Task Generate_WithNamedQueries_EmitsQueryMethod()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();

        // 保存済みスキーマへ名前付きクエリを追加して上書きする（GUI で定義した図を CLI へ渡す想定）
        var document = JsonStorageService.Load(schemaPath);
        var entity = document.Schema.Entities[0];
        document.Schema.Queries.Add(
            new QueryDefinition
            {
                EntityId = entity.Id,
                Name = "SearchByName",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "keyword", Type = "string(50)" },
                },
                Condition = "Name LIKE @keyword",
            }
        );
        JsonStorageService.Save(schemaPath, document);

        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(configPath, """{ "GenerateRepositories": true }""");

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--namespace",
                "Test.Ns",
                "--config",
                configPath,
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            files.Should().NotBeEmpty();
            var content = string.Join("\n", files.Select(File.ReadAllText));
            content
                .Should()
                .Contain("SearchByNameAsync(string keyword,")
                .And.Contain("Query().Where(e => e.Name!.Contains(keyword))");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>新しいフォーマットバージョンのスキーマは標準エラーへ警告を出しつつ生成を続行することを検証する</summary>
    [Fact(DisplayName = "generate は新しいフォーマットのスキーマで警告を出して続行する")]
    public async Task Generate_NewerFormatSchema_WarnsAndContinues()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();

        // 保存済みスキーマを CurrentVersion より新しいフォーマットバージョンへ書き換える
        var document = JsonStorageService.Load(schemaPath);
        document.Version = DiagramDocument.CurrentVersion + 1;
        JsonStorageService.Save(schemaPath, document);

        var originalError = Console.Error;
        var stderr = new StringWriter();
        Console.SetError(stderr);

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--namespace",
                "Test.Ns",
            ]);

            exit.Should().Be(0);
            Directory.GetFiles(outDir, "*.g.cs").Should().NotBeEmpty();

            // 警告文言はロケール依存のため、埋め込まれるバージョン番号で検証する
            var warning = stderr.ToString();
            warning.Should().Contain($"v{DiagramDocument.CurrentVersion + 1}");
            warning.Should().Contain($"v{DiagramDocument.CurrentVersion}");
        }
        finally
        {
            Console.SetError(originalError);

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>未対応プロバイダを指定すると終了コード 1 を返すことを検証する</summary>
    [Fact(DisplayName = "未対応プロバイダ指定は終了コード 1")]
    public async Task Generate_UnknownProvider_ReturnsError()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--provider",
                "no_such_provider",
            ]);

            exit.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 自作 Repository の生成が要求されているが（quicker.json の GenerateRepositories=true）、
    /// プロバイダが未対応方言（postgresql / mysql / oracle）の場合は終了コード 1 でエラーメッセージを出すことを検証する
    /// </summary>
    [Theory(DisplayName = "未対応方言＋GenerateRepositories 要求は終了コード 1")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("oracle")]
    public async Task Generate_UnsupportedDialectWithRepositories_ReturnsError(string providerName)
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(configPath, """{ "GenerateRepositories": true }""");

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--provider",
                providerName,
                "--config",
                configPath,
            ]);

            exit.Should().Be(1);
            Directory
                .Exists(outDir)
                .Should()
                .BeFalse("生成前にエラーで中止するため出力は作られない");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 対応方言（sqlite）＋ GenerateRepositories=true では生成が成功し、
    /// RepositoryDialect が --provider の値（sqlite）で確定していることを検証する
    /// </summary>
    [Fact(DisplayName = "対応方言（sqlite）＋GenerateRepositories は生成が成功する")]
    public async Task Generate_SupportedDialectWithRepositories_Succeeds()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(configPath, """{ "GenerateRepositories": true }""");

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--provider",
                "sqlite",
                "--config",
                configPath,
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            var code = string.Join("\n", files.Select(File.ReadAllText));
            code.Should().Contain("Microsoft.Data.Sqlite");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// --repository-dialects に複数方言（sqlserver,sqlite）を指定すると、両方言の namespace が
    /// 同じ生成物に出力されることを検証する（マルチターゲット生成の CLI 経路）
    /// </summary>
    [Fact(DisplayName = "--repository-dialects 複数指定で両方言 namespace が出力される")]
    public async Task Generate_MultipleRepositoryDialects_EmitsBothNamespaces()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(configPath, """{ "GenerateRepositories": true }""");

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--provider",
                "sqlserver",
                "--config",
                configPath,
                "--repository-dialects",
                "sqlserver,sqlite",
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            var code = string.Join("\n", files.Select(File.ReadAllText));
            code.Should().Contain("Microsoft.Data.SqlClient");
            code.Should().Contain("Microsoft.Data.Sqlite");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// --repository-dialects に未対応方言（postgresql）を含めると、生成前に終了コード 1 でエラーになることを検証する
    /// </summary>
    [Fact(DisplayName = "--repository-dialects に未対応方言を含めるとエラーになる")]
    public async Task Generate_RepositoryDialects_WithUnsupportedDialect_ReturnsError()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(configPath, """{ "GenerateRepositories": true }""");

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--provider",
                "sqlserver",
                "--config",
                configPath,
                "--repository-dialects",
                "sqlserver,postgresql",
            ]);

            exit.Should().Be(1);
            Directory
                .Exists(outDir)
                .Should()
                .BeFalse("生成前にエラーで中止するため出力は作られない");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// --repository-dialects 未指定時は従来どおり --provider から単一方言が導出されることを検証する
    /// （後方互換：既存の Generate_SupportedDialectWithRepositories_Succeeds と同じ経路で sqlserver 実装のみが出る）
    /// </summary>
    [Fact(DisplayName = "--repository-dialects 未指定時は --provider から単一導出する")]
    public async Task Generate_WithoutRepositoryDialects_DerivesSingleDialectFromProvider()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(configPath, """{ "GenerateRepositories": true }""");

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--provider",
                "sqlserver",
                "--config",
                configPath,
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            var code = string.Join("\n", files.Select(File.ReadAllText));
            code.Should().Contain("Microsoft.Data.SqlClient");
            code.Should().NotContain("Microsoft.Data.Sqlite");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// --runtime-packages 指定時は生成コードにランタイム（固定コード）の EntityBase が含まれず、
    /// QuickER.Runtime への using 参照に切り替わり、PackageReference 案内が標準出力へ表示されることを検証する
    /// </summary>
    [Fact(DisplayName = "--runtime-packages 指定でランタイム非同梱＋案内が出力される")]
    public async Task Generate_WithRuntimePackages_OmitsRuntimeAndPrintsGuidance()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--namespace",
                "Test.Pkg",
                "--runtime-packages",
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            var code = string.Join("\n", files.Select(File.ReadAllText));
            code.Should().NotContain("class EntityBase");
            code.Should().Contain("using QuickER.Runtime;");

            var stdout = writer.ToString();
            stdout.Should().Contain("PackageReference");
            stdout.Should().Contain("QuickER.Runtime");
        }
        finally
        {
            Console.SetOut(originalOut);

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// --runtime-packages 未指定時は従来どおりランタイム（固定コード）が生成物に含まれることを検証する
    /// （バイト不変の回帰確認を兼ねる軽量チェック。厳密なバイト一致は GeneratedFixtureDriftTests が担保）
    /// </summary>
    [Fact(DisplayName = "--runtime-packages 未指定時は従来どおりランタイムが同梱される")]
    public async Task Generate_WithoutRuntimePackages_IncludesRuntimeAsBefore()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--namespace",
                "Test.Inline",
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            var code = string.Join("\n", files.Select(File.ReadAllText));
            code.Should().Contain("class EntityBase");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// --runtime-packages と GenerateEfCore=true の併用は解禁されており、生成が成功（終了コード 0）して
    /// 案内に EF パッケージ（QuickER.Runtime.EntityFrameworkCore）が含まれることを検証する
    /// </summary>
    [Fact(DisplayName = "--runtime-packages と EF Core の併用は成功し EF パッケージが案内される")]
    public async Task Generate_RuntimePackagesWithEfCore_SucceedsWithEfPackageGuidance()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        // EF 単独（自作 Repository なし）でパッケージ参照モードにする
        File.WriteAllText(
            configPath,
            """{ "GenerateRepositories": false, "GenerateEfCore": true }"""
        );
        var originalOut = Console.Out;
        var outWriter = new StringWriter();
        Console.SetOut(outWriter);

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--config",
                configPath,
                "--runtime-packages",
            ]);

            exit.Should().Be(0);
            Directory.Exists(outDir).Should().BeTrue("生成成功のため出力が作られる");
            var stdout = outWriter.ToString();
            stdout
                .Should()
                .Contain(
                    "QuickER.Runtime.EntityFrameworkCore",
                    "EF パッケージが PackageReference 案内に含まれる"
                );
            // EF 単独では自作方言パッケージ（SqlServer / Sqlite）は案内されない
            stdout.Should().NotContain("QuickER.Runtime.SqlServer");
            stdout.Should().NotContain("QuickER.Runtime.Sqlite");
        }
        finally
        {
            Console.SetOut(originalOut);

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// --api-docs 指定時は出力ディレクトリに API リファレンス Markdown（.g.md）が 1 つ書き出されることを検証する
    /// </summary>
    [Fact(DisplayName = "--api-docs 指定で .g.md が出力される")]
    public async Task Generate_WithApiDocs_WritesMarkdown()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--namespace",
                "Test.Docs",
                "--api-docs",
            ]);

            exit.Should().Be(0);
            var markdownFiles = Directory.GetFiles(outDir, "*.g.md");
            markdownFiles.Should().ContainSingle("--api-docs 指定で .g.md が 1 つ出力される");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>--api-docs 未指定時は API リファレンス Markdown（.g.md）が出力されないことを検証する</summary>
    [Fact(DisplayName = "--api-docs 未指定では .g.md が出力されない")]
    public async Task Generate_WithoutApiDocs_DoesNotWriteMarkdown()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--namespace",
                "Test.NoDocs",
            ]);

            exit.Should().Be(0);
            var markdownFiles = Directory.GetFiles(outDir, "*.g.md");
            markdownFiles.Should().BeEmpty("--api-docs 未指定では .g.md を出力しない");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// マルチ方言（--repository-dialects 2 つ以上）＋ GenerateEfCore=true は生成器側の診断エラーとなり、
    /// CLI が通常のエラー出力経路（終了コード 1・出力なし）でそれを表示できることを検証する
    /// （生成器の排他検証は MultiTargetRepositoryGenerationTests で担保済みで、ここでは CLI 経路の伝播のみ確認する）
    /// </summary>
    [Fact(DisplayName = "マルチ方言＋GenerateEfCore は CLI でも終了コード 1 になる")]
    public async Task Generate_MultiDialectWithEfCore_ReturnsError()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(
            configPath,
            """{ "GenerateRepositories": true, "GenerateEfCore": true }"""
        );

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--provider",
                "sqlserver",
                "--config",
                configPath,
                "--repository-dialects",
                "sqlserver,sqlite",
            ]);

            exit.Should().Be(1);
            Directory.Exists(outDir).Should().BeFalse("生成エラーのため出力は作られない");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
