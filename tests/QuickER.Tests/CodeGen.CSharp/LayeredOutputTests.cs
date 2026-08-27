using System.IO;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 層別フォルダ出力（<see cref="CodeGenerationOptions.LayeredOutput"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 検証対象: (1) バケット→層の固定対応（計画の RelativeDirectory 付与）、(2) 分割の自動含意、
/// (3) フォルダ上書きと空白フォールバック、(4) 名前空間の層フォルダ追従（既定導出・明示優先・固定部の統合）、
/// (5) 「層分け ON/OFF で namespace 宣言・using 行以外は一致」の不変条件、
/// (6) 層フォルダパスの診断検証（パス妥当性＋名前空間導出可否・使われる層のみ）、
/// (7) ライターのサブフォルダ書き出しと出力先外拒否、(8) <see cref="LayerDirectoryValidator"/> の判定規則。
/// </remarks>
public sealed class LayeredOutputTests
{
    /// <summary>全層が埋まる代表構成（QuickER 版 Repository＋リモートサービス＋EditModel/Mapper＋VO＋インメモリ＋同期）</summary>
    private static CodeGenerationOptions FullLayeredOptions() =>
        new()
        {
            RootNamespace = "Acme.App",
            LayeredOutput = true,
            GenerateRepositories = true,
            GenerateRemoteServices = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateValueObjects = true,
            GenerateInMemoryRepositories = true,
            GenerateSyncSupport = true,
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

    [Fact(DisplayName = "計画: バケット→層の固定対応で全ファイルへ層フォルダが付く")]
    public void Plan_Layered_AssignsFixedLayerDirectories()
    {
        var plan = GeneratedFilePlanner.Plan(FullLayeredOptions());

        var directories = plan.ToDictionary(spec => spec.FileName, spec => spec.RelativeDirectory);

        directories
            .Should()
            .Equal(
                new Dictionary<string, string?>
                {
                    // ドメイン層: Entity / VO / Repository 契約 / Runtime コア
                    ["Entities.g.cs"] = "Domain",
                    ["ValueObjects.g.cs"] = "Domain",
                    ["Repositories.g.cs"] = "Domain",
                    ["Runtime.g.cs"] = "Domain",
                    // プレゼンテーション層: EditModel / Mapper
                    ["EditModels.g.cs"] = "Presentation",
                    ["Mappers.g.cs"] = "Presentation",
                    // インフラ層: 方言別実装 / インメモリ / 同期 / HTTP クライアントと各固定 infra
                    ["Repositories.SqlServer.g.cs"] = "Infrastructure",
                    ["Repositories.Http.g.cs"] = "Infrastructure",
                    ["Repositories.InMemory.g.cs"] = "Infrastructure",
                    ["Repositories.Sync.g.cs"] = "Infrastructure",
                    ["Runtime.SqlServer.g.cs"] = "Infrastructure",
                    ["Runtime.InMemory.g.cs"] = "Infrastructure",
                    ["Runtime.Sync.g.cs"] = "Infrastructure",
                    // サーバー層: リモートサーバー実装＋ASP.NET Core 固定部（FrameworkReference を要する）
                    ["RemoteServer.g.cs"] = "Server",
                    ["Runtime.AspNetCore.g.cs"] = "Server",
                }
            );
    }

    [Fact(DisplayName = "計画: EF Core 構成では DbContext 系がインフラ層・契約はドメイン層に付く")]
    public void Plan_Layered_EfCore_MapsToInfrastructure()
    {
        var plan = GeneratedFilePlanner.Plan(
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                LayeredOutput = true,
                GenerateEfCore = true,
            }
        );

        plan.Single(spec => spec.FileName == "Repositories.EntityFrameworkCore.g.cs")
            .RelativeDirectory.Should()
            .Be("Infrastructure");
        plan.Single(spec => spec.FileName == "Runtime.EntityFrameworkCore.g.cs")
            .RelativeDirectory.Should()
            .Be("Infrastructure");
        plan.Single(spec => spec.FileName == "Repositories.g.cs")
            .RelativeDirectory.Should()
            .Be("Domain");
    }

    [Fact(DisplayName = "計画: LayeredOutput は SplitFilesByCategory を自動含意する")]
    public void Plan_Layered_ImpliesSplit()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Acme.App",
            LayeredOutput = true,
            SplitFilesByCategory = false,
            GenerateRepositories = true,
        };

        var plan = GeneratedFilePlanner.Plan(options);

        // 分割レイアウト（カテゴリ別ファイル）で計画される＝単一ファイルにならない
        options.EffectiveSplitFilesByCategory.Should().BeTrue();
        plan.Select(spec => spec.FileName).Should().Contain("Entities.g.cs");
        plan.Select(spec => spec.FileName).Should().NotContain("QuickEREntities.g.cs");
    }

    [Fact(DisplayName = "計画: フォルダ上書きは複数階層可・空白は既定へフォールバック")]
    public void Plan_Layered_UsesOverridesAndFallsBackWhenBlank()
    {
        var plan = GeneratedFilePlanner.Plan(
            FullLayeredOptions() with
            {
                DomainLayerDirectory = "MyApp.Domain/Generated",
                PresentationLayerDirectory = "MyApp.Wpf",
                InfrastructureLayerDirectory = "   ",
            }
        );

        plan.Single(spec => spec.FileName == "Entities.g.cs")
            .RelativeDirectory.Should()
            .Be("MyApp.Domain/Generated");
        plan.Single(spec => spec.FileName == "Repositories.SqlServer.g.cs")
            .RelativeDirectory.Should()
            .Be("Infrastructure");
        plan.Single(spec => spec.FileName == "EditModels.g.cs")
            .RelativeDirectory.Should()
            .Be("MyApp.Wpf");
        plan.Single(spec => spec.FileName == "RemoteServer.g.cs")
            .RelativeDirectory.Should()
            .Be("Server");
    }

    [Fact(DisplayName = "計画: 層別出力でなければ RelativeDirectory は全て null（直下）")]
    public void Plan_NonLayered_LeavesRelativeDirectoryNull()
    {
        var plan = GeneratedFilePlanner.Plan(
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                SplitFilesByCategory = true,
                GenerateRepositories = true,
                GenerateRemoteServices = true,
            }
        );

        plan.Should().OnlyContain(spec => spec.RelativeDirectory == null);
    }

    [Fact(DisplayName = "不変条件: 層分け ON/OFF で名前空間・using 以外の生成テキストは一致する")]
    public void Generate_Layered_KeepsContentIdenticalExceptNamespaces()
    {
        var baseOptions = new CodeGenerationOptions
        {
            RootNamespace = "Acme.App",
            GenerateRepositories = true,
            GenerateRemoteServices = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateValueObjects = true,
            GenerateApiDocs = true,
        };

        var split = Generate(CreateDiagram(), baseOptions with { SplitFilesByCategory = true });
        var layered = Generate(CreateDiagram(), baseOptions with { LayeredOutput = true });

        split.HasErrors.Should().BeFalse();
        layered.HasErrors.Should().BeFalse();

        // ファイル名集合は完全一致。内容は「名前空間の層フォルダ追従」と「固定 infra の可視性」の分だけ変わる:
        // スキーマ依存ファイル（per-entity）は namespace 宣言と using 行を除けば一致＝コード本体は不変。
        // 固定ランタイム（Runtime*.g.cs）はさらに可視性が public へ切り替わる（層別出力は複数アセンブリ配置の
        // ため、パッケージ配布と同じ規則＝別層の生成物が Owner/IncludeNode 等を参照できる）。
        layered
            .Files.Select(file => file.FileName)
            .Should()
            .Equal(split.Files.Select(file => file.FileName));

        foreach (var (layeredFile, splitFile) in layered.Files.Zip(split.Files))
        {
            if (
                !layeredFile.FileName.EndsWith(".g.cs", StringComparison.Ordinal)
                || layeredFile.FileName.StartsWith("Runtime", StringComparison.Ordinal)
            )
            {
                continue;
            }

            StripNamespaceLines(layeredFile.Content)
                .Should()
                .Be(StripNamespaceLines(splitFile.Content), layeredFile.FileName);
        }

        // 固定ランタイムの可視性の切り替えを名指しで固定（多アセンブリでの成立自体は
        // GeneratedCodeCompilationTests の 4 プロジェクト分割コンパイルが検証する）
        var layeredRuntime = layered.Files.Single(file => file.FileName == "Runtime.g.cs").Content;
        var splitRuntime = split.Files.Single(file => file.FileName == "Runtime.g.cs").Content;
        layeredRuntime.Should().Contain("public sealed class IncludeNode");
        layeredRuntime.Should().NotContain("internal sealed class IncludeNode");
        splitRuntime.Should().Contain("internal sealed class IncludeNode");

        // API リファレンス（.g.md）のファイル一覧表は実際の名前空間を載せるため、層追従がそのまま反映される
        var layeredApiDocs = layered.Files.Single(file => file.FileName == "ApiDocs.g.md").Content;
        layeredApiDocs.Should().Contain("Domain.Entities");
        layeredApiDocs.Should().NotContain("Acme.App.Entities");

        // 分割のみでは配置なし・層別では .g.cs に層フォルダ・.g.md は出力ディレクトリ直下のまま
        split.Files.Should().OnlyContain(file => file.RelativeDirectory == null);
        layered
            .Files.Where(file => file.FileName.EndsWith(".g.cs", StringComparison.Ordinal))
            .Should()
            .OnlyContain(file => file.RelativeDirectory != null);
        layered
            .Files.Where(file => file.FileName.EndsWith(".g.md", StringComparison.Ordinal))
            .Should()
            .OnlyContain(file => file.RelativeDirectory == null);
    }

    /// <summary>namespace 宣言と using 行を取り除く（層分け ON/OFF のコード本体一致を比較するため）</summary>
    private static string StripNamespaceLines(string content) =>
        string.Join(
            '\n',
            content
                .Split('\n')
                .Where(line =>
                    !line.TrimStart().StartsWith("using ", StringComparison.Ordinal)
                    && !line.TrimStart().StartsWith("namespace ", StringComparison.Ordinal)
                )
        );

    [Fact(
        DisplayName = "計画: 名前空間の既定は層フォルダに追従する（固定部は per-entity と同一名前空間へ統合）"
    )]
    public void Plan_Layered_NamespacesFollowLayerFolders()
    {
        var plan = GeneratedFilePlanner.Plan(FullLayeredOptions());

        var namespaces = plan.ToDictionary(spec => spec.FileName, spec => spec.NamespaceName);

        namespaces
            .Should()
            .Equal(
                new Dictionary<string, string>
                {
                    // ドメイン層: {Domain}.{種別サフィックス}
                    ["Entities.g.cs"] = "Domain.Entities",
                    ["ValueObjects.g.cs"] = "Domain.ValueObjects",
                    ["Repositories.g.cs"] = "Domain.Repositories",
                    ["Runtime.g.cs"] = "Domain.Runtime",
                    // プレゼンテーション層
                    ["EditModels.g.cs"] = "Presentation.EditModels",
                    ["Mappers.g.cs"] = "Presentation.Mappers",
                    // インフラ層: 固定部（Runtime.{X}）と per-entity（Repositories.{X}）は同一サブ名前空間へ統合
                    ["Repositories.SqlServer.g.cs"] = "Infrastructure.SqlServer",
                    ["Runtime.SqlServer.g.cs"] = "Infrastructure.SqlServer",
                    ["Repositories.Http.g.cs"] = "Infrastructure.Http",
                    ["Repositories.InMemory.g.cs"] = "Infrastructure.InMemory",
                    ["Runtime.InMemory.g.cs"] = "Infrastructure.InMemory",
                    ["Repositories.Sync.g.cs"] = "Infrastructure.Sync",
                    ["Runtime.Sync.g.cs"] = "Infrastructure.Sync",
                    // サーバー層
                    ["RemoteServer.g.cs"] = "Server.RemoteServer",
                    ["Runtime.AspNetCore.g.cs"] = "Server.AspNetCore",
                }
            );
    }

    [Fact(DisplayName = "計画: フォルダ変更で名前空間も追従し、明示指定はそれより優先される")]
    public void Plan_Layered_NamespaceFollowsFolderAndExplicitWins()
    {
        var plan = GeneratedFilePlanner.Plan(
            FullLayeredOptions() with
            {
                DomainLayerDirectory = "MyApp.Domain/Generated",
                InfrastructureLayerDirectory = "MyApp.Infrastructure",
                EntityNamespace = "MyApp.Model",
            }
        );

        // 明示指定が最優先
        plan.Single(spec => spec.FileName == "Entities.g.cs")
            .NamespaceName.Should()
            .Be("MyApp.Model");
        // 複数階層フォルダは区切りを . に変換して名前空間ルートになる
        plan.Single(spec => spec.FileName == "ValueObjects.g.cs")
            .NamespaceName.Should()
            .Be("MyApp.Domain.Generated.ValueObjects");
        // 方言実装は契約でなくインフラ層ルートの下に導出される（固定部も同一）
        plan.Single(spec => spec.FileName == "Repositories.SqlServer.g.cs")
            .NamespaceName.Should()
            .Be("MyApp.Infrastructure.SqlServer");
        plan.Single(spec => spec.FileName == "Runtime.SqlServer.g.cs")
            .NamespaceName.Should()
            .Be("MyApp.Infrastructure.SqlServer");
    }

    [Fact(
        DisplayName = "診断: 名前空間になれない層フォルダ（ハイフン等）は導出が使われる層でエラーになる"
    )]
    public void Generate_LayerFolderNotNamespace_ReportsError()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                LayeredOutput = true,
                GenerateRepositories = true,
                DomainLayerDirectory = "my-domain",
            }
        );

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Select(diagnostic => diagnostic.Message)
            .Should()
            .Contain(message =>
                message.Contains("DomainLayerDirectory") && message.Contains("my-domain")
            );
    }

    [Fact(
        DisplayName = "診断: 層の全バケットを明示名前空間で賄えば、名前空間になれないフォルダでも許容する"
    )]
    public void Generate_LayerFolderNotNamespace_AllowedWhenAllNamespacesExplicit()
    {
        // ドメイン層の有効バケットは Entity / Repository 契約 / Runtime の 3 つ（EditModel/Mapper/VO は無効化）
        // ＝3 つとも明示指定すればフォルダ由来の導出が使われず、パスとして合法な "my-domain" は許容される
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                LayeredOutput = true,
                GenerateRepositories = true,
                GenerateEditModels = false,
                GenerateMappers = false,
                DomainLayerDirectory = "my-domain",
                EntityNamespace = "Acme.Domain.Model",
                RepositoryNamespace = "Acme.Domain.Repositories",
                RuntimeNamespace = "Acme.Domain.Runtime",
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(
                string.Join(" / ", result.Diagnostics.Select(diagnostic => diagnostic.Message))
            );
    }

    [Fact(DisplayName = "診断: 不正な層フォルダ（.. / 絶対パス）は生成時エラーになる")]
    public void Generate_InvalidLayerDirectory_ReportsError()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                LayeredOutput = true,
                GenerateRepositories = true,
                DomainLayerDirectory = @"..\evil",
                InfrastructureLayerDirectory = @"C:\abs",
            }
        );

        result.HasErrors.Should().BeTrue();
        var messages = result.Diagnostics.Select(diagnostic => diagnostic.Message).ToList();
        messages.Should().Contain(message => message.Contains("DomainLayerDirectory"));
        messages.Should().Contain(message => message.Contains("InfrastructureLayerDirectory"));
    }

    /// <summary>
    /// 層別出力 OFF なら、層フォルダの値が不正でも検証されないことを検証する（ゲートの成功側）。
    /// </summary>
    /// <remarks>
    /// 層フォルダオプションは層別出力 ON のときしか読まれないため、検証は早期 return で丸ごとゲートされている。
    /// ゲートが外れると、層別出力を使っていない構成が「設定しただけで一度も使われない値」で落ちるようになる。
    /// </remarks>
    [Fact(DisplayName = "診断: 層別出力 OFF なら不正な層フォルダでも検証しない")]
    public void Generate_InvalidLayerDirectory_WithoutLayeredOutput_ReportsNoError()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                LayeredOutput = false,
                GenerateRepositories = true,
                DomainLayerDirectory = @"..\evil",
                InfrastructureLayerDirectory = @"C:\abs",
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(
                string.Join(" / ", result.Diagnostics.Select(diagnostic => diagnostic.Message))
            );
        var messages = result.Diagnostics.Select(diagnostic => diagnostic.Message).ToList();
        messages.Should().NotContain(message => message.Contains("DomainLayerDirectory"));
        messages.Should().NotContain(message => message.Contains("InfrastructureLayerDirectory"));
    }

    [Fact(DisplayName = "診断: 使われない層（リモートなしのサーバー層）は不正値でも検証しない")]
    public void Generate_UnusedServerLayerDirectory_IsNotValidated()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Acme.App",
                LayeredOutput = true,
                GenerateRepositories = true,
                ServerLayerDirectory = @"..\unused",
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(
                string.Join(" / ", result.Diagnostics.Select(diagnostic => diagnostic.Message))
            );
    }

    [Fact(
        DisplayName = "ライター: 層フォルダ（複数階層含む）を作成して書き出し .g.md は直下に残る"
    )]
    public void Writer_WritesIntoLayerDirectories()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quicker_layered_" + Guid.NewGuid());

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
                        FileName = "Repositories.SqlServer.g.cs",
                        RelativeDirectory = "Infrastructure",
                        Content = "// infra",
                    },
                    new GeneratedFile { FileName = "ApiDocs.g.md", Content = "# doc" },
                ],
            };

            var written = new GeneratedFileWriter().WriteFiles(directory, result);

            written.Should().HaveCount(3);
            File.ReadAllText(Path.Combine(directory, "MyApp.Domain", "Generated", "Entities.g.cs"))
                .Should()
                .Be("// domain");
            File.ReadAllText(
                    Path.Combine(directory, "Infrastructure", "Repositories.SqlServer.g.cs")
                )
                .Should()
                .Be("// infra");
            File.ReadAllText(Path.Combine(directory, "ApiDocs.g.md")).Should().Be("# doc");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact(DisplayName = "ライター: 出力ディレクトリ外へ出る層フォルダは拒否する（防御の二重化）")]
    public void Writer_RejectsEscapingLayerDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quicker_layered_" + Guid.NewGuid());

        try
        {
            var result = new CodeGenerationResult
            {
                Files =
                [
                    new GeneratedFile
                    {
                        FileName = "Entities.g.cs",
                        RelativeDirectory = @"..\evil",
                        Content = "// x",
                    },
                ],
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

    [Theory(DisplayName = "バリデータ: 出力ディレクトリ内に収まる相対パスだけを許可する")]
    [InlineData("Domain", true)]
    [InlineData("MyApp.Domain/Generated", true)]
    [InlineData(@"MyApp.Domain\Generated", true)]
    [InlineData("Domain/", true)]
    [InlineData(" Domain ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("..", false)]
    [InlineData("a/../b", false)]
    [InlineData("./a", false)]
    [InlineData(@"C:\abs", false)]
    [InlineData("C:relative", false)]
    [InlineData("/rooted", false)]
    [InlineData(@"\rooted", false)]
    [InlineData("a//b", false)]
    public void Validator_AcceptsOnlyContainedRelativePaths(string value, bool expected)
    {
        LayerDirectoryValidator.IsValid(value).Should().Be(expected);
    }

    [Fact(DisplayName = "バリデータ: 正規化は区切りを OS の区切り文字へ揃える")]
    public void Validator_NormalizesSeparators()
    {
        LayerDirectoryValidator
            .Normalize("MyApp.Domain/Generated/")
            .Should()
            .Be(Path.Combine("MyApp.Domain", "Generated"));
    }
}
