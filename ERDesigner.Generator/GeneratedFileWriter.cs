using System.Text;

namespace ERDesigner.Generator;

public sealed class GeneratedFileWriter
{
    public IReadOnlyList<string> WriteFiles(string outputDirectory, CodeGenerationResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(result);

        Directory.CreateDirectory(outputDirectory);
        var writtenPaths = new List<string>();

        foreach (var file in result.Files)
        {
            var fileName = Path.GetFileName(file.FileName);
            if (!fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("生成ファイル以外は上書きできません。出力ファイル名は .g.cs で終わる必要があります。");
            }

            var path = Path.Combine(outputDirectory, fileName);
            File.WriteAllText(path, file.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writtenPaths.Add(path);
        }

        return writtenPaths;
    }
}
