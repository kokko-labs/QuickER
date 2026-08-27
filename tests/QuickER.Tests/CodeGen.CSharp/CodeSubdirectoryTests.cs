using System.IO;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成コードの出力先サブフォルダ（<see cref="CodeGenerationOptions.CodeSubdirectory"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 検証対象: (1) 全出力モード（層別・分割・非分割）での配置、(2) <b>名前空間に一切現れない</b>という要の不変条件、
/// (3) API リファレンス Markdown が追随しないこと、(4) 未指定なら配置が従来どおりであること、
/// (5) 診断（パス妥当性は検証する／C# 識別子としての妥当性は問わない）、(6) ライターの結合パス書き出し。
/// </remarks>
public sealed class CodeSubdirectoryTests
{
    /// <summary>層別出力で全層が埋まる代表構成</summary>
    private static CodeGenerationOptions LayeredOptions(string? subdirectory) =>
        new()
        {
            RootNamespace = "Acme.App",
            LayeredOutput = true,
            GenerateRepositories = true,
            GenerateRemoteServices = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            CodeSubdirectory = subdirectory,
        };

    /// <summary>Order エンティティ 1 つの最小図を作る</summary>
    private static ErDiagram CreateDiagram()
    {
        var order = new Entity { TableName = "Order" };
        order.Columns.Add(
            new Column
            {
                Name = "OrderId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        order.Columns.Add(new Column { Name = "Memo", DataType = "nvarchar(200)" });

        return new ErDiagram { Entities = { order } };
    }

    /// <summary>実経路（SqlServer プロバイダ）で生成する</summary>
    private static CodeGenerationResult Generate(ErDiagram diagram, CodeGenerationOptions options)
    {
        var provider = new SqlServerProvider();
        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            options
        );
    }

    [Fact(DisplayName = "計画: 層別出力ではサブフォルダが層フォルダの 1 段下に付く")]
    public void Plan_Layered_AppendsSubdirectoryBelowLayerDirectory()
    {
        var plan = GeneratedFilePlanner.Plan(LayeredOptions("Generated"));

        plan.Should().NotBeEmpty();
        plan.Should()
            .OnlyContain(spec =>
                spec.RelativeDirectory == "Domain/Generated"
                || spec.RelativeDirectory == "Presentation/Generated"
                || spec.RelativeDirectory == "Infrastructure/Generated"
                || spec.RelativeDirectory == "Server/Generated"
            );

        // 層フォルダを明示指定した場合も同じ規則（複数階層の層フォルダの下へ 1 段）
        var custom = GeneratedFilePlanner.Plan(
            LayeredOptions("Generated") with
            {
                DomainLayerDirectory = "MyApp.Domain/src",
            }
        );

        custom
            .Single(spec => spec.FileName == "Entities.g.cs")
            .RelativeDirectory.Should()
            .Be("MyApp.Domain/src/Generated");
    }

    [Fact(DisplayName = "計画: 層別出力 OFF（分割）ではサブフォルダだけが付く")]
    public void Plan_SplitWithoutLayeredOutput_UsesSubdirectoryOnly()
    {
        var plan = GeneratedFilePlanner.Plan(
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                SplitFilesByCategory = true,
                GenerateRepositories = true,
                CodeSubdirectory = "Generated",
            }
        );

        plan.Should().NotBeEmpty();
        plan.Should().OnlyContain(spec => spec.RelativeDirectory == "Generated");
    }

    /// <summary>
    /// 非分割（単一ファイル）でもサブフォルダが効くことを検証する。
    /// </summary>
    /// <remarks>
    /// 計画は非分割で早期 return するため、付与を分割パスの末尾だけで行うとここが素通りする
    /// （合流点を <see cref="GeneratedFilePlanner.Plan"/> の出口 1 箇所へ置いている理由）。
    /// 非分割でも別ファイルで出るリモートサーバー実装も同じサブフォルダへ入る。
    /// </remarks>
    [Fact(DisplayName = "計画: 非分割（単一ファイル）でもサブフォルダが効く")]
    public void Plan_SingleFile_AppliesSubdirectory()
    {
        var plan = GeneratedFilePlanner.Plan(
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                OutputFileName = "Acme.g.cs",
                GenerateRepositories = true,
                GenerateRemoteServices = true,
                CodeSubdirectory = "Generated",
            }
        );

        plan.Should().NotBeEmpty();
        plan.Should().OnlyContain(spec => spec.RelativeDirectory == "Generated");
        plan.Should().Contain(spec => spec.FileName.EndsWith(".RemoteServer.g.cs"));
    }

    /// <summary>
    /// サブフォルダが名前空間へ一切現れないことを検証する（本オプションの要）。
    /// </summary>
    /// <remarks>
    /// 表明の形は「サブフォルダの有無で生成テキストが 1 バイトも変わらない」。
    /// 名前空間の導出（<see cref="GeneratedFilePlanner.LayerNamespaceRoot"/>）へサブフォルダを混ぜると
    /// namespace 宣言とクロス using が両方変わるため、この比較 1 つで両方の回帰を捕まえられる。
    /// </remarks>
    [Fact(DisplayName = "生成: サブフォルダは名前空間・using に一切現れない（配置だけが変わる）")]
    public void Generate_Subdirectory_ChangesPlacementOnly()
    {
        var diagram = CreateDiagram();
        var withoutSubdirectory = Generate(diagram, LayeredOptions(subdirectory: null));
        var withSubdirectory = Generate(diagram, LayeredOptions("Generated"));

        var baseline = withoutSubdirectory.Files.ToDictionary(file => file.FileName);

        withSubdirectory.Files.Should().HaveCount(baseline.Count);

        foreach (var file in withSubdirectory.Files)
        {
            baseline.Should().ContainKey(file.FileName);
            file.Content.Should()
                .Be(
                    baseline[file.FileName].Content,
                    $"{file.FileName} はサブフォルダの有無で内容が変わってはならない"
                );
            file.RelativeDirectory.Should()
                .Be($"{baseline[file.FileName].RelativeDirectory}/Generated");
        }
    }

    /// <summary>
    /// API リファレンス Markdown がサブフォルダへ追随しないことを検証する。
    /// </summary>
    /// <remarks>
    /// ドキュメントの置き場を決めるのは <see cref="CodeGenerationOptions.ApiDocsSubdirectory"/> だけ、という
    /// 独立軸の取り決め（暗黙のフォールバックを作らない）。
    /// </remarks>
    [Fact(DisplayName = "生成: API リファレンス Markdown はサブフォルダに追随しない")]
    public void Generate_ApiDocs_DoNotFollowSubdirectory()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                SplitFilesByCategory = true,
                GenerateRepositories = true,
                GenerateApiDocs = true,
                CodeSubdirectory = "Generated",
            }
        );

        var markdown = result.Files.Where(file => file.FileName.EndsWith(".g.md")).ToList();
        markdown.Should().NotBeEmpty();
        markdown.Should().OnlyContain(file => file.RelativeDirectory == null);

        // 明示指定があればそちらへ（コード側のサブフォルダとは独立に決まる）
        var withApiDocsSubdirectory = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                SplitFilesByCategory = true,
                GenerateRepositories = true,
                GenerateApiDocs = true,
                CodeSubdirectory = "Generated",
                ApiDocsSubdirectory = "docs",
            }
        );

        withApiDocsSubdirectory
            .Files.Where(file => file.FileName.EndsWith(".g.md"))
            .Should()
            .OnlyContain(file => file.RelativeDirectory == "docs");
    }

    [Fact(DisplayName = "計画: サブフォルダ未指定なら配置は従来どおり（層のみ／直下）")]
    public void Plan_WithoutSubdirectory_KeepsExistingPlacement()
    {
        GeneratedFilePlanner
            .Plan(LayeredOptions(subdirectory: null))
            .Should()
            .OnlyContain(spec => !spec.RelativeDirectory!.Contains('/'));

        GeneratedFilePlanner
            .Plan(
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    SplitFilesByCategory = true,
                    GenerateRepositories = true,
                }
            )
            .Should()
            .OnlyContain(spec => spec.RelativeDirectory == null);

        // 空白は未指定と同じ扱い（サブフォルダなしへフォールバック）
        GeneratedFilePlanner
            .Plan(
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    SplitFilesByCategory = true,
                    GenerateRepositories = true,
                    CodeSubdirectory = "   ",
                }
            )
            .Should()
            .OnlyContain(spec => spec.RelativeDirectory == null);
    }

    [Fact(
        DisplayName = "診断: 不正なサブフォルダ（.. / 絶対パス）は出力モードに依らずエラーになる"
    )]
    public void Generate_InvalidSubdirectory_ReportsError()
    {
        foreach (var invalid in new[] { @"..\evil", @"C:\abs" })
        {
            var result = Generate(
                CreateDiagram(),
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    GenerateRepositories = true,
                    CodeSubdirectory = invalid,
                }
            );

            result.HasErrors.Should().BeTrue($"'{invalid}' は出力先の外へ出る");
            result
                .Diagnostics.Select(diagnostic => diagnostic.Message)
                .Should()
                .Contain(message =>
                    message.Contains(nameof(CodeGenerationOptions.CodeSubdirectory))
                );
        }
    }

    /// <summary>
    /// C# 識別子になれない名前でもサブフォルダとして許すことを検証する。
    /// </summary>
    /// <remarks>
    /// 層フォルダは名前空間の導出に使われるため識別子検証を受けるが、サブフォルダは名前空間に現れないため
    /// 受けない（「名前空間導出に実際に使われる値だけを検証する」という既存の一般則の帰結）。
    /// </remarks>
    [Fact(DisplayName = "診断: 名前空間になれない名前（ハイフン等）もサブフォルダなら許容する")]
    public void Generate_NonIdentifierSubdirectory_IsAllowed()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                LayeredOutput = true,
                GenerateRepositories = true,
                CodeSubdirectory = "generated-code",
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(
                string.Join(" / ", result.Diagnostics.Select(diagnostic => diagnostic.Message))
            );
        result
            .Files.Should()
            .OnlyContain(file => file.RelativeDirectory!.EndsWith("/generated-code"));
    }

    [Fact(DisplayName = "ライター: 層フォルダ＋サブフォルダの結合パスへ書き出す")]
    public void Writer_WritesIntoCombinedDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quicker_subdir_" + Guid.NewGuid());

        try
        {
            var result = new CodeGenerationResult
            {
                Files =
                [
                    new GeneratedFile
                    {
                        FileName = "Entities.g.cs",
                        RelativeDirectory = "MyApp.Domain/Generated",
                        Content = "// domain",
                    },
                    new GeneratedFile
                    {
                        FileName = "Acme.g.cs",
                        RelativeDirectory = "Generated",
                        Content = "// single",
                    },
                ],
            };

            new GeneratedFileWriter().WriteFiles(directory, result).Should().HaveCount(2);

            File.ReadAllText(Path.Combine(directory, "MyApp.Domain", "Generated", "Entities.g.cs"))
                .Should()
                .Be("// domain");
            File.ReadAllText(Path.Combine(directory, "Generated", "Acme.g.cs"))
                .Should()
                .Be("// single");
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
