using System.IO;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 層別フォルダ出力（<see cref="CodeGenerationOptions.LayeredOutput"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 残している検証対象: (1) 層分け ON/OFF の本文の文字列（固定 infra の可視性の名指し・API リファレンスの名前空間）、
/// (2) ライターのサブフォルダ書き出しと出力先外拒否、(3) <see cref="LayerDirectoryValidator"/> の判定規則。
/// 層フォルダの置き場・名前空間の追従・分割の自動含意・層フォルダの診断（生成の出力で判定するもの）は、
/// <c>CodegenCaseTests</c> の行 <c>files-layered-*</c> / <c>diag-layered-*</c>（<c>CodegenCases.Files.cs</c>）へ、
/// 「層分け ON/OFF で namespace 宣言・using 行以外は一致」の 2 つの生成物の比較は、<c>CodegenCaseTests</c> の行
/// <c>relation-layered-vs-split-order</c> と網羅の配列の層別の行の関係 LayeredVsSplit（<c>Judges/Relations.cs</c>）へ移した。
/// </remarks>
public sealed class LayeredOutputTests
{
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

    [Fact(
        DisplayName = "不変条件: 層分け ON/OFF で固定 infra の可視性が切り替わり、API リファレンスは層の名前空間を載せる"
    )]
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

        // 固定ランタイム（Runtime*.g.cs）は可視性が public へ切り替わる（層別出力は複数アセンブリ配置の
        // ため、パッケージ配布と同じ規則＝別層の生成物が Owner/IncludeNode 等を参照できる）。
        // 固定ランタイムの可視性の切り替えを名指しで固定（多アセンブリでの成立自体は
        // CodegenCaseTests の行 compile-layered-projects-* の 4 プロジェクト分割コンパイルが検証する）
        var layeredRuntime = layered.Files.Single(file => file.FileName == "Runtime.g.cs").Content;
        var splitRuntime = split.Files.Single(file => file.FileName == "Runtime.g.cs").Content;
        layeredRuntime.Should().Contain("public sealed class IncludeNode");
        layeredRuntime.Should().NotContain("internal sealed class IncludeNode");
        splitRuntime.Should().Contain("internal sealed class IncludeNode");

        // API リファレンス（.g.md）のファイル一覧表は実際の名前空間を載せるため、層追従がそのまま反映される
        var layeredApiDocs = layered.Files.Single(file => file.FileName == "ApiDocs.g.md").Content;
        layeredApiDocs.Should().Contain("Domain.Entities");
        layeredApiDocs.Should().NotContain("Acme.App.Entities");
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
    [InlineData("a /b", false)]
    [InlineData("a/ b", false)]
    [InlineData(" a /b ", false)]
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
