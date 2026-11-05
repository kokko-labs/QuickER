using System.IO;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成コードの出力先サブフォルダ（<see cref="CodeGenerationOptions.CodeSubdirectory"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 残している検証対象は、ライターの結合パス書き出し。サブフォルダの置き場（全出力モード）・API リファレンスが追随しないこと・未指定の配置・診断
/// （パス妥当性は検証する／C# 識別子としての妥当性は問わない）は、<c>CodegenCaseTests</c> の行 <c>files-codesubdir-*</c> /
/// <c>diag-codesubdir-*</c>（<c>CodegenCases.Files.cs</c>）へ、<b>名前空間に一切現れない</b>という要の不変条件
/// （サブフォルダの有無で 2 つの生成物の本文が 1 バイトも変わらない比較）は、<c>CodegenCaseTests</c> の行
/// <c>relation-code-subdirectory-layered</c> と網羅の配列の全行の関係 CodeSubdirectory（<c>Judges/Relations.cs</c>）へ移した。
/// </remarks>
public sealed class CodeSubdirectoryTests
{
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
