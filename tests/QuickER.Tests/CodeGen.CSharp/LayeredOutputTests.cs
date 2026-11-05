using System.IO;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 層別フォルダ出力（<see cref="CodeGenerationOptions.LayeredOutput"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 残している検証対象: (1) ライターのサブフォルダ書き出しと出力先外拒否、(2) <see cref="LayerDirectoryValidator"/> の判定規則。
/// 層分け ON/OFF での固定 infra の可視性（<c>IncludeNode</c> の public / internal）と API リファレンスの名前空間は、
/// <c>CodegenCaseTests</c> の行 <c>code-layered-fixed-infra-is-public-and-api-docs-follow-layers</c> / <c>code-split-fixed-infra-is-internal</c>
/// （<c>CodegenCases.ColumnText.cs</c>）へ、
/// 層フォルダの置き場・名前空間の追従・分割の自動含意・層フォルダの診断（生成の出力で判定するもの）は、
/// <c>CodegenCaseTests</c> の行 <c>files-layered-*</c> / <c>diag-layered-*</c>（<c>CodegenCases.Files.cs</c>）へ、
/// 「層分け ON/OFF で namespace 宣言・using 行以外は一致」の 2 つの生成物の比較は、<c>CodegenCaseTests</c> の行
/// <c>relation-layered-vs-split-order</c> と網羅の配列の層別の行の関係 LayeredVsSplit（<c>Judges/Relations.cs</c>）へ移した。
/// </remarks>
public sealed class LayeredOutputTests
{
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
