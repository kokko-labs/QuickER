using System.IO;
using System.Linq;
using AwesomeAssertions;
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
                "--root-namespace",
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
                "--root-namespace",
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
                .And.Contain("Query().Where(e => (e.Name != null && e.Name!.Contains(keyword)))");
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

        // 出力は注入版オーバーロードで捕捉する（Console を差し替えるとクラス並列実行で競合するため）
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await CliApp.InvokeAsync(
                [
                    "generate",
                    "--schema",
                    schemaPath,
                    "--out",
                    outDir,
                    "--root-namespace",
                    "Test.Ns",
                ],
                stdout,
                stderr
            );

            exit.Should().Be(0);
            Directory.GetFiles(outDir, "*.g.cs").Should().NotBeEmpty();

            // 警告文言はロケール依存のため、埋め込まれるバージョン番号で検証する
            var warning = stderr.ToString();
            warning.Should().Contain($"v{DiagramDocument.CurrentVersion + 1}");
            warning.Should().Contain($"v{DiagramDocument.CurrentVersion}");
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
    /// <c>--config</c> に存在しないパス（タイプミス）を渡すと、既定値へ黙ってフォールバックせず
    /// 終了コード 1 で中止し、指定パスを含むメッセージを標準エラーへ出すことを検証する
    /// </summary>
    [Fact(DisplayName = "--config 不在パスは終了コード 1（パス入りメッセージ）")]
    public async Task Generate_ConfigFileNotFound_ReturnsError()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        // quicker.json のタイプミス（ファイルは作らない）
        var missingConfigPath = Path.Combine(root, "quciker.json");
        // 出力は注入版オーバーロードで捕捉する（Console を差し替えるとクラス並列実行で競合するため）
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await CliApp.InvokeAsync(
                [
                    "generate",
                    "--schema",
                    schemaPath,
                    "--out",
                    outDir,
                    "--config",
                    missingConfigPath,
                ],
                stdout,
                stderr
            );

            exit.Should().Be(1);
            Directory
                .Exists(outDir)
                .Should()
                .BeFalse("設定読解エラーで生成前に中止するため出力は作られない");
            // 文言はロケール依存のため、埋め込まれる設定ファイルのパスで検証する
            stderr.ToString().Should().Contain(missingConfigPath);
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
    /// <c>--config</c> の JSON が壊れている場合は、スタックトレースで落ちず終了コード 1 と
    /// 整形メッセージ（パス入り）になることを検証する
    /// </summary>
    [Fact(DisplayName = "--config 不正 JSON は終了コード 1（整形メッセージ）")]
    public async Task Generate_ConfigFileInvalidJson_ReturnsError()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        // 閉じ括弧が欠けた不正 JSON
        File.WriteAllText(configPath, """{ "GenerateRepositories": true""");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await CliApp.InvokeAsync(
                ["generate", "--schema", schemaPath, "--out", outDir, "--config", configPath],
                stdout,
                stderr
            );

            exit.Should().Be(1);
            Directory
                .Exists(outDir)
                .Should()
                .BeFalse("設定読解エラーで生成前に中止するため出力は作られない");
            stderr.ToString().Should().Contain(configPath);
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
    /// <c>--config</c> の内容が JSON リテラル <c>null</c> の場合は、空オブジェクト（＝全キー既定値のまま
    /// 終了コード 0）へ黙って化けず、終了コード 1 と整形メッセージ（パス入り）になることを検証する
    /// </summary>
    [Fact(DisplayName = "--config が JSON リテラル null は終了コード 1（整形メッセージ）")]
    public async Task Generate_ConfigFileNullLiteral_ReturnsError()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        // JsonNode.Parse は例外でなく null を返すため、素通りすると既定値のまま成功してしまう
        File.WriteAllText(configPath, "null");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await CliApp.InvokeAsync(
                ["generate", "--schema", schemaPath, "--out", outDir, "--config", configPath],
                stdout,
                stderr
            );

            exit.Should().Be(1);
            Directory
                .Exists(outDir)
                .Should()
                .BeFalse("設定読解エラーで生成前に中止するため出力は作られない");
            stderr.ToString().Should().Contain(configPath);
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
    /// 設定キーの値の型が違う（bool キーへ文字列）場合は、System.CommandLine の既定ハンドラへ抜けて
    /// スタックトレースが露出せず、終了コード 1 とキー名入りの整形メッセージになることを検証する
    /// </summary>
    [Fact(DisplayName = "--config の値の型違いは終了コード 1（キー名入り・スタックトレースなし）")]
    public async Task Generate_ConfigValueTypeMismatch_ReturnsError()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        // bool キーへ文字列を書いた（デシリアライズ時に JsonException になる）
        File.WriteAllText(configPath, """{ "GenerateRepositories": "yes" }""");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await CliApp.InvokeAsync(
                ["generate", "--schema", schemaPath, "--out", outDir, "--config", configPath],
                stdout,
                stderr
            );

            exit.Should().Be(1);
            Directory
                .Exists(outDir)
                .Should()
                .BeFalse("設定読解エラーで生成前に中止するため出力は作られない");

            var error = stderr.ToString();
            // 文言はロケール依存のため、埋め込まれるパスと失敗キー名で検証する
            error.Should().Contain(configPath);
            error.Should().Contain("GenerateRepositories");
            error.Should().NotContain("   at ", "整形メッセージのみでスタックトレースは出さない");
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
    /// <c>RepositoryDialects</c> を配列でなく文字列で書いた場合は、黙って破棄して図の方言で上書きせず
    /// （意図と違う方言のコードが無警告で出るのを防ぐ）、終了コード 1 でエラーになることを検証する
    /// </summary>
    [Fact(DisplayName = "--config の RepositoryDialects が配列でないと終了コード 1")]
    public async Task Generate_ConfigRepositoryDialectsNotArray_ReturnsError()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        // 配列 ["sqlserver"] と書くべきところを文字列で書いた
        File.WriteAllText(
            configPath,
            """{ "GenerateRepositories": true, "RepositoryDialects": "sqlserver" }"""
        );
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await CliApp.InvokeAsync(
                ["generate", "--schema", schemaPath, "--out", outDir, "--config", configPath],
                stdout,
                stderr
            );

            exit.Should().Be(1);
            Directory
                .Exists(outDir)
                .Should()
                .BeFalse("設定読解エラーで生成前に中止するため出力は作られない");
            // 文言はロケール依存のため、埋め込まれるキー名とパスで検証する
            stderr.ToString().Should().Contain("RepositoryDialects").And.Contain(configPath);
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
    /// 設定ファイルに未知のキーがある場合は、前方互換のためエラーにせず標準エラーへ警告を出し、
    /// 終了コード 0 で生成を続行することを検証する
    /// </summary>
    [Fact(DisplayName = "--config の未知キーは警告のみ（終了コード 0 で生成続行）")]
    public async Task Generate_ConfigUnknownKey_WarnsAndContinues()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(configPath, """{ "unknownKey": 1, "RootNamespace": "Test.Ns" }""");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await CliApp.InvokeAsync(
                ["generate", "--schema", schemaPath, "--out", outDir, "--config", configPath],
                stdout,
                stderr
            );

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            files.Should().NotBeEmpty();
            File.ReadAllText(files[0]).Should().Contain("namespace Test.Ns");
            // 文言はロケール依存のため、埋め込まれるキー名で検証する
            stderr.ToString().Should().Contain("unknownKey");
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
    /// GUI が書き出す別名キー <c>OutputPath</c>（<c>OutputFileName</c> への橋渡し）は正当なキーとして
    /// 未知キー警告の対象にならず、出力ファイル名として効くことを検証する
    /// </summary>
    [Fact(DisplayName = "--config の OutputPath は警告なしで出力ファイル名になる")]
    public async Task Generate_ConfigOutputPathAlias_DoesNotWarn()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(configPath, """{ "OutputPath": "Custom.g.cs" }""");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await CliApp.InvokeAsync(
                ["generate", "--schema", schemaPath, "--out", outDir, "--config", configPath],
                stdout,
                stderr
            );

            exit.Should().Be(0);
            File.Exists(Path.Combine(outDir, "Custom.g.cs")).Should().BeTrue();
            stderr
                .ToString()
                .Should()
                .NotContain("OutputPath", "OutputPath は許可集合に含む正当な別名キー");
        }
        finally
        {
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
    /// QuickER 版 Repository の生成が要求されているが（quicker.json の GenerateRepositories=true）、
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
    /// RepositoryDialects が --provider の値（sqlite）で確定していることを検証する
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
    /// --repository-dialects 未指定時は --provider から単一方言が導出されることを検証する
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
    /// --use-runtime-packages 指定時は生成コードにランタイム（固定コード）の EntityBase が含まれず、
    /// QuickER.Runtime への using 参照に切り替わり、PackageReference 案内が標準出力へ表示されることを検証する
    /// </summary>
    [Fact(DisplayName = "--use-runtime-packages 指定でランタイム非同梱＋案内が出力される")]
    public async Task Generate_WithRuntimePackages_OmitsRuntimeAndPrintsGuidance()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        // 出力は注入版オーバーロードで捕捉する（Console を差し替えるとクラス並列実行で競合するため）
        var writer = new StringWriter();
        var stderrWriter = new StringWriter();

        try
        {
            var exit = await CliApp.InvokeAsync(
                [
                    "generate",
                    "--schema",
                    schemaPath,
                    "--out",
                    outDir,
                    "--root-namespace",
                    "Test.Pkg",
                    "--use-runtime-packages",
                ],
                writer,
                stderrWriter
            );

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
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// --use-runtime-packages 未指定時はランタイム（固定コード）が生成物に含まれることを検証する
    /// （軽量チェック。厳密なバイト一致は GeneratedFixtureDriftTests が担保）
    /// </summary>
    [Fact(DisplayName = "--use-runtime-packages 未指定時はランタイムが同梱される")]
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
                "--root-namespace",
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
    /// --use-runtime-packages と GenerateEfCore=true の併用は解禁されており、生成が成功（終了コード 0）して
    /// 案内に EF Core パッケージ（QuickER.Runtime.EntityFrameworkCore）が含まれることを検証する
    /// </summary>
    [Fact(
        DisplayName = "--use-runtime-packages と EF Core の併用は成功し EF Core パッケージが案内される"
    )]
    public async Task Generate_RuntimePackagesWithEfCore_SucceedsWithEfPackageGuidance()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        // EF Core 単独（QuickER 版 Repository なし）でパッケージ参照モードにする
        File.WriteAllText(
            configPath,
            """{ "GenerateRepositories": false, "GenerateEfCore": true }"""
        );
        // 出力は注入版オーバーロードで捕捉する（Console を差し替えるとクラス並列実行で競合するため）
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();

        try
        {
            var exit = await CliApp.InvokeAsync(
                [
                    "generate",
                    "--schema",
                    schemaPath,
                    "--out",
                    outDir,
                    "--config",
                    configPath,
                    "--use-runtime-packages",
                ],
                outWriter,
                errWriter
            );

            exit.Should().Be(0);
            Directory.Exists(outDir).Should().BeTrue("生成成功のため出力が作られる");
            var stdout = outWriter.ToString();
            stdout
                .Should()
                .Contain(
                    "QuickER.Runtime.EntityFrameworkCore",
                    "EF Core パッケージが PackageReference 案内に含まれる"
                );
            // EF Core 単独ではQuickER の方言パッケージ（SqlServer / Sqlite）は案内されない
            stdout.Should().NotContain("QuickER.Runtime.SqlServer");
            stdout.Should().NotContain("QuickER.Runtime.Sqlite");
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
    /// --generate-remote-contracts 指定時は、リモート面 I{Entity}RemoteRepository（ここでは ICustomerRemoteRepository）が
    /// 追加生成されることを検証する（CLI の end-to-end）
    /// </summary>
    [Fact(
        DisplayName = "--generate-remote-contracts 指定でリモート面インターフェイスが追加生成される"
    )]
    public async Task Generate_WithRemoteContracts_EmitsRemoteRepositoryInterface()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        // リモート面は Repository 契約が前提のため、DB アクセス（QuickER 版 Repository）を明示的に有効にする
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
                "--root-namespace",
                "Test.Remote",
                "--config",
                configPath,
                "--generate-remote-contracts",
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            var code = string.Join("\n", files.Select(File.ReadAllText));
            code.Should().Contain("public partial interface ICustomerRemoteRepository");
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
    /// remote-contracts 未指定（既定 OFF）のとき、エンティティ別のリモート面が生成されないことを検証する
    /// </summary>
    [Fact(DisplayName = "remote-contracts 未指定（既定 OFF）ではリモート面が生成されない")]
    public async Task Generate_WithoutRemoteContracts_DoesNotEmitRemoteRepositoryInterface()
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
                "--root-namespace",
                "Test.Full",
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            var code = string.Join("\n", files.Select(File.ReadAllText));
            // 既定 OFF ではエンティティ別のリモート面は生成されない
            // （ランタイム基底 IRemoteRepository は常時出力されるため、エンティティ別のインターフェイス名で判定する）
            code.Should().NotContain("ICustomerRemoteRepository");
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
    /// quicker.json の GenerateRemoteContracts=true 指定でも、リモート面が追加生成されることを検証する
    /// </summary>
    [Fact(DisplayName = "quicker.json の GenerateRemoteContracts=true でリモート面が生成される")]
    public async Task Generate_WithRemoteContractsFromConfig_EmitsRemoteRepositoryInterface()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(
            configPath,
            """{ "GenerateRepositories": true, "GenerateRemoteContracts": true }"""
        );

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--root-namespace",
                "Test.RemoteConfig",
                "--config",
                configPath,
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            var code = string.Join("\n", files.Select(File.ReadAllText));
            code.Should().Contain("public partial interface ICustomerRemoteRepository");
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
    /// --generate-remote-services 指定時は、リモート面のサーバー実装ファイル（*.RemoteServer.g.cs）が
    /// 追加出力され、その中に ASP.NET Core のエンドポイントマッピング（MapGeneratedRemoteEndpoints）が含まれ、
    /// 本体には HTTP クライアント実装（HttpCustomerRemoteRepository）が同梱されることを検証する（CLI の end-to-end）
    /// </summary>
    [Fact(
        DisplayName = "--generate-remote-services 指定でサーバー実装ファイルと HTTP クライアント実装が生成される"
    )]
    public async Task Generate_WithRemoteServices_EmitsServerFileAndHttpClient()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        // リモート面は Repository 契約が前提のため、DB アクセス（QuickER 版 Repository）を明示的に有効にする
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
                "--root-namespace",
                "Test.RemoteServices",
                "--config",
                configPath,
                "--generate-remote-services",
            ]);

            exit.Should().Be(0);

            // サーバー実装は専用ファイル（*.RemoteServer.g.cs）へ出力される
            var serverFiles = Directory.GetFiles(outDir, "*.RemoteServer.g.cs");
            serverFiles
                .Should()
                .NotBeEmpty("--generate-remote-services 指定でサーバー実装ファイルが出力される");
            var serverCode = string.Join("\n", serverFiles.Select(File.ReadAllText));
            serverCode.Should().Contain("MapGeneratedRemoteEndpoints");

            // HTTP クライアント実装は本体（通常の .g.cs）へ同梱される
            var code = string.Join(
                "\n",
                Directory.GetFiles(outDir, "*.g.cs").Select(File.ReadAllText)
            );
            code.Should().Contain("HttpCustomerRemoteRepository");
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
    /// quicker.json の GenerateRemoteServices=true 指定でも、サーバー実装ファイルと HTTP クライアント実装が
    /// 生成されることを検証する
    /// </summary>
    [Fact(
        DisplayName = "quicker.json の GenerateRemoteServices=true でサーバー実装と HTTP クライアントが生成される"
    )]
    public async Task Generate_WithRemoteServicesFromConfig_EmitsServerFileAndHttpClient()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(
            configPath,
            """{ "GenerateRepositories": true, "GenerateRemoteServices": true }"""
        );

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--root-namespace",
                "Test.RemoteServicesConfig",
                "--config",
                configPath,
            ]);

            exit.Should().Be(0);

            var serverFiles = Directory.GetFiles(outDir, "*.RemoteServer.g.cs");
            serverFiles
                .Should()
                .NotBeEmpty("GenerateRemoteServices=true でサーバー実装ファイルが出力される");
            var serverCode = string.Join("\n", serverFiles.Select(File.ReadAllText));
            serverCode.Should().Contain("MapGeneratedRemoteEndpoints");

            var code = string.Join(
                "\n",
                Directory.GetFiles(outDir, "*.g.cs").Select(File.ReadAllText)
            );
            code.Should().Contain("HttpCustomerRemoteRepository");
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
    /// --generate-api-docs 指定時は出力ディレクトリに API リファレンス Markdown（.g.md）が 1 つ書き出されることを検証する
    /// </summary>
    [Fact(DisplayName = "--generate-api-docs 指定で .g.md が出力される")]
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
                "--root-namespace",
                "Test.Docs",
                "--generate-api-docs",
            ]);

            exit.Should().Be(0);
            var markdownFiles = Directory.GetFiles(outDir, "*.g.md");
            markdownFiles
                .Should()
                .ContainSingle("--generate-api-docs 指定で .g.md が 1 つ出力される");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>--generate-api-docs 未指定時は API リファレンス Markdown（.g.md）が出力されないことを検証する</summary>
    [Fact(DisplayName = "--generate-api-docs 未指定では .g.md が出力されない")]
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
                "--root-namespace",
                "Test.NoDocs",
            ]);

            exit.Should().Be(0);
            var markdownFiles = Directory.GetFiles(outDir, "*.g.md");
            markdownFiles.Should().BeEmpty("--generate-api-docs 未指定では .g.md を出力しない");
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

    /// <summary>varbinary(max) 列を持つ ER 図 JSON を一時フォルダに作成する（無制限バイナリ列の除外検証用）</summary>
    private static (string schemaPath, string outDir, string root) CreateSchemaWithBinaryColumn()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QuickERCliTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        var schemaPath = Path.Combine(root, "schema.json");

        var document = new DiagramDocument();
        var entity = new Entity { TableName = "Document" };
        entity.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(new Column { Name = "Payload", DataType = "varbinary(max)" });
        document.Schema.Entities.Add(entity);
        JsonStorageService.Save(schemaPath, document);

        return (schemaPath, Path.Combine(root, "out"), root);
    }

    /// <summary>
    /// --exclude-unbounded-binary-columns 指定時は、無制限バイナリ列（varbinary(max)）の Entity プロパティへ
    /// マーカー属性 [UnboundedBinaryColumn] が付与されることを検証する（CLI がオプションを生成器へ伝播している証跡）
    /// </summary>
    [Fact(
        DisplayName = "--exclude-unbounded-binary-columns 指定で [UnboundedBinaryColumn] が付与される"
    )]
    public async Task Generate_WithExcludeUnboundedBinary_MarksColumn()
    {
        var (schemaPath, outDir, root) = CreateSchemaWithBinaryColumn();

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--root-namespace",
                "Test.ExcludeBinary",
                "--exclude-unbounded-binary-columns",
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            var code = string.Join("\n", files.Select(File.ReadAllText));
            code.Should()
                .Contain(
                    "[UnboundedBinaryColumn]",
                    "--exclude-unbounded-binary-columns 指定で無制限バイナリ列にマーカー属性が付く"
                );
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
    /// --exclude-unbounded-binary-columns 未指定（既定 OFF）のときは、マーカー属性 [UnboundedBinaryColumn] が
    /// 付与されないことを検証する
    /// </summary>
    [Fact(
        DisplayName = "--exclude-unbounded-binary-columns 未指定では [UnboundedBinaryColumn] が付かない"
    )]
    public async Task Generate_WithoutExcludeUnboundedBinary_DoesNotMarkColumn()
    {
        var (schemaPath, outDir, root) = CreateSchemaWithBinaryColumn();

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--root-namespace",
                "Test.NoExcludeBinary",
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            var code = string.Join("\n", files.Select(File.ReadAllText));
            code.Should()
                .NotContain(
                    "[UnboundedBinaryColumn]",
                    "既定 OFF では無制限バイナリ列にマーカー属性を付けない"
                );
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
    /// bool フラグの三値: 設定ファイルで true のキー（GenerateRepositories）を <c>--generate-repositories false</c> で
    /// OFF に上書きでき、QuickER 版 Repository（ADO 依存）が生成されないことを検証する（CLI ＞ 設定ファイル）
    /// </summary>
    [Fact(
        DisplayName = "--generate-repositories false は設定ファイルの true を上書きして OFF にできる"
    )]
    public async Task Generate_BoolFlagFalse_OverridesConfigTrue()
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
                "--generate-repositories",
                "false",
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            var code = string.Join("\n", files.Select(File.ReadAllText));
            code.Should()
                .NotContain(
                    "Microsoft.Data.Sqlite",
                    "--generate-repositories false で設定ファイルの true を打ち消すため ADO 実装は出ない"
                );
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
    /// bool フラグ単独（値なし）は true として解釈され、設定ファイルなしでも
    /// <c>--generate-repositories</c> だけで QuickER 版 Repository が生成されることを検証する
    /// </summary>
    [Fact(DisplayName = "--generate-repositories 単独（config なし）で Repository が生成される")]
    public async Task Generate_BoolFlagAlone_EnablesGenerationWithoutConfig()
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
                "sqlite",
                "--generate-repositories",
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
    /// <c>--root-namespace</c> が設定ファイルの RootNamespace を上書きすることを検証する（CLI ＞ 設定ファイル）
    /// </summary>
    [Fact(DisplayName = "--root-namespace は設定ファイルの RootNamespace を上書きする")]
    public async Task Generate_RootNamespaceFlag_OverridesConfig()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(configPath, """{ "RootNamespace": "From.Config" }""");

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
                "--root-namespace",
                "Override.Ns",
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            var code = string.Join("\n", files.Select(File.ReadAllText));
            code.Should().Contain("namespace Override.Ns");
            code.Should().NotContain("namespace From.Config");
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
    /// <c>--output-path</c> に名前のみのパスを渡すと、そのファイル名部分が出力ファイル名として使われることを検証する
    /// （出力先ディレクトリは常に <c>--out</c>。OutputPath → OutputFileName の導出）
    /// </summary>
    [Fact(DisplayName = "--output-path のファイル名部分が出力ファイル名になる")]
    public async Task Generate_OutputPathFlag_DerivesOutputFileName()
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
                "--root-namespace",
                "Test.OutputPath",
                "--output-path",
                "Custom.g.cs",
            ]);

            exit.Should().Be(0);
            var fileNames = Directory.GetFiles(outDir, "*.g.cs").Select(Path.GetFileName).ToArray();
            fileNames.Should().Contain("Custom.g.cs");
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
    /// --layered-output 指定で生成ファイルが層別サブフォルダ（Domain / Presentation）へ配置され、
    /// 出力ディレクトリ直下には出ないことを検証する（既定フォルダ名。CLI の end-to-end）
    /// </summary>
    [Fact(DisplayName = "--layered-output で生成ファイルが層別サブフォルダへ出力される")]
    public async Task Generate_LayeredOutputFlag_PlacesFilesUnderLayerFolders()
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
                "--root-namespace",
                "Test.Layered",
                "--layered-output",
            ]);

            exit.Should().Be(0);

            // Entity はドメイン層・EditModel はプレゼンテーション層の既定フォルダへ入る（GenerateEditModels は既定 true）
            File.Exists(Path.Combine(outDir, "Domain", "Entities.g.cs")).Should().BeTrue();
            File.Exists(Path.Combine(outDir, "Presentation", "EditModels.g.cs")).Should().BeTrue();

            // 出力ディレクトリ直下（層フォルダの外）には .g.cs が出ない
            Directory.GetFiles(outDir, "*.g.cs", SearchOption.TopDirectoryOnly).Should().BeEmpty();
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
    /// --layered-output と 4 つの層フォルダ上書きフラグ（--domain-layer-dir 等）が
    /// 実際の出力先フォルダへ反映されることを検証する（サーバー層は --generate-remote-services で有効化。CLI の end-to-end）
    /// </summary>
    [Fact(DisplayName = "層フォルダ上書きフラグ 4 つが実際の出力先へ反映される")]
    public async Task Generate_LayerDirectoryFlags_OverrideOutputFolders()
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
                "--root-namespace",
                "Test.LayeredDirs",
                "--layered-output",
                "--generate-repositories",
                "--generate-remote-services",
                "--domain-layer-dir",
                "MyDomain",
                "--infrastructure-layer-dir",
                "MyInfra",
                "--presentation-layer-dir",
                "MyUi",
                "--server-layer-dir",
                "MyServer",
            ]);

            exit.Should().Be(0);
            File.Exists(Path.Combine(outDir, "MyDomain", "Entities.g.cs")).Should().BeTrue();
            File.Exists(Path.Combine(outDir, "MyInfra", "Repositories.SqlServer.g.cs"))
                .Should()
                .BeTrue();
            File.Exists(Path.Combine(outDir, "MyUi", "EditModels.g.cs")).Should().BeTrue();
            File.Exists(Path.Combine(outDir, "MyServer", "RemoteServer.g.cs")).Should().BeTrue();
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
    /// quicker.json 側で LayeredOutput と層フォルダ 4 キーを指定しても、CLI フラグと同様に
    /// 実際の出力先フォルダへ反映されることを検証する（quicker.json → CodeGenerationOptions の読込経路）
    /// </summary>
    [Fact(DisplayName = "quicker.json の LayeredOutput と層フォルダキーが出力先へ反映される")]
    public async Task Generate_LayeredOutputFromConfig_OverridesOutputFolders()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(
            configPath,
            """
            {
                "GenerateRepositories": true,
                "GenerateRemoteServices": true,
                "LayeredOutput": true,
                "DomainLayerDirectory": "ConfigDomain",
                "PresentationLayerDirectory": "ConfigUi",
                "InfrastructureLayerDirectory": "ConfigInfra",
                "ServerLayerDirectory": "ConfigServer"
            }
            """
        );

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--root-namespace",
                "Test.LayeredConfig",
                "--config",
                configPath,
            ]);

            exit.Should().Be(0);
            File.Exists(Path.Combine(outDir, "ConfigDomain", "Entities.g.cs")).Should().BeTrue();
            File.Exists(Path.Combine(outDir, "ConfigInfra", "Repositories.SqlServer.g.cs"))
                .Should()
                .BeTrue();
            File.Exists(Path.Combine(outDir, "ConfigUi", "EditModels.g.cs")).Should().BeTrue();
            File.Exists(Path.Combine(outDir, "ConfigServer", "RemoteServer.g.cs"))
                .Should()
                .BeTrue();
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
    /// --api-docs-dir で API リファレンス Markdown がサブフォルダへ出力され、生成コードの配置は
    /// 影響を受けないことを検証する（CLI の end-to-end）
    /// </summary>
    [Fact(DisplayName = "--api-docs-dir で API リファレンスがサブフォルダへ出力される")]
    public async Task Generate_ApiDocsDirFlag_PlacesMarkdownUnderSubfolder()
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
                "--root-namespace",
                "Test.ApiDocsDir",
                "--split-files-by-category",
                "--generate-api-docs",
                "--api-docs-dir",
                "docs",
            ]);

            exit.Should().Be(0);
            File.Exists(Path.Combine(outDir, "docs", "ApiDocs.g.md")).Should().BeTrue();
            File.Exists(Path.Combine(outDir, "ApiDocs.g.md")).Should().BeFalse();
            // 生成コード（.g.cs）は出力ディレクトリ直下のまま
            File.Exists(Path.Combine(outDir, "Entities.g.cs")).Should().BeTrue();
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
