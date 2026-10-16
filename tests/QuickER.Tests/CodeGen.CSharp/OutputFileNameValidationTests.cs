using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider.SqlServer;
using CodeGenStrings = QuickER.CodeGen.CSharp.Resources.Strings;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 非分割の出力ファイル名（<see cref="CodeGenerationOptions.OutputFileName"/>）を、生成前の診断で断ることを固定する
/// </summary>
/// <remarks>
/// 診断がないと、<c>sub/Foo.g.cs</c> は書き出しがディレクトリ要素を落として黙って <c>Foo.g.cs</c> になり、予約名・8.3 の別名は
/// 書き込みの時点まで通って例外になる（API リファレンスのファイル名には元から生成前の診断があった）。
/// </remarks>
public sealed class OutputFileNameValidationTests
{
    [Theory(
        DisplayName = "ファイル名として断る出力ファイル名は、生成前の診断でエラーになり何も出力しない"
    )]
    [InlineData("sub/Foo.g.cs")]
    [InlineData(@"sub\Foo.g.cs")]
    [InlineData("CON")]
    [InlineData("a~1")]
    [InlineData(".g.cs")]
    [InlineData(".cs")]
    [InlineData("Foo.")]
    public void InvalidOutputFileName_IsReportedBeforeGeneration(string outputFileName)
    {
        var result = Generate(new CodeGenerationOptions { OutputFileName = outputFileName });

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error
                && d.Message.StartsWith(Prefix(outputFileName.Trim()), StringComparison.Ordinal)
            );
        result.Files.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "付け足すと 1 区間の上限を超える名前（サーバー実装の .RemoteServer.g.cs）も断る"
    )]
    public void DerivedFileNameTooLong_IsReported()
    {
        var name = new string('a', 245) + ".g.cs";

        Generate(new CodeGenerationOptions { OutputFileName = name }).HasErrors.Should().BeFalse();
        Generate(
            new CodeGenerationOptions
            {
                OutputFileName = name,
                GenerateRepositories = true,
                GenerateRemoteServices = true,
            }
        )
            .Diagnostics.Should()
            .Contain(d => d.Message.StartsWith(Prefix(name), StringComparison.Ordinal));
    }

    [Fact(DisplayName = "分割出力では出力ファイル名を使わないので、断る名前でも生成を止めない")]
    public void SplitOutput_IgnoresOutputFileName()
    {
        Generate(
            new CodeGenerationOptions
            {
                OutputFileName = "sub/Foo.g.cs",
                SplitFilesByCategory = true,
            }
        )
            .HasErrors.Should()
            .BeFalse();
    }

    [Theory(DisplayName = "ファイル名だけの指定は受け付ける（拡張子は .g.cs へ正規化）")]
    [InlineData("MyApp.g.cs", "MyApp.g.cs")]
    [InlineData("MyApp.cs", "MyApp.g.cs")]
    [InlineData(" MyApp ", "MyApp.g.cs")]
    public void ValidOutputFileName_IsAccepted(string outputFileName, string expected)
    {
        var result = Generate(new CodeGenerationOptions { OutputFileName = outputFileName });

        result.HasErrors.Should().BeFalse();
        result.Files.Select(f => f.FileName).Should().Contain(expected);
    }

    private static string Prefix(string value) =>
        string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                CodeGenStrings.CodeGen_Error_InvalidOutputFileName,
                value,
                "\u0000"
            )
            .Split('\u0000')[0];

    private static CodeGenerationResult Generate(CodeGenerationOptions options)
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        return new CSharpCodeGenerationService().Generate(
            diagram,
            SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            options
        );
    }
}
