using System.IO;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成物のファイル名は 1 区間 255 文字までを受け付け、その長さの名前へ実際に書き出せることを固定する
/// </summary>
/// <remarks>
/// 判定（<c>PathSegments</c>）は 255 文字までを通すので、原子的な書き込みの一時ファイルの名前が上限を超えると、
/// 受け付けた名前の書き出しが必ず失敗する。
/// </remarks>
public sealed class GeneratedFileWriterLongNameTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "QuickERTests",
        "writer-long-" + Guid.NewGuid().ToString("N")
    );

    public GeneratedFileWriterLongNameTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // 後始末の失敗は無視する
        }
    }

    [Theory(DisplayName = "受け付ける長さの生成物のファイル名へ書き出せる")]
    [InlineData(230)]
    [InlineData(250)]
    public void WriteFiles_LongFileName_Succeeds(int length)
    {
        var name = new string('a', length) + ".g.cs";
        var result = new CodeGenerationResult
        {
            Files = [new GeneratedFile { FileName = name, Content = "// x" }],
        };

        new GeneratedFileWriter().WriteFiles(_folder, result);

        File.ReadAllText(Path.Combine(_folder, name)).Should().Be("// x");
    }
}
