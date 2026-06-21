using System.Text;

namespace ERDesigner.Generator;

/// <summary>
/// 生成結果のファイルをディスクへ書き出すライター
/// </summary>
/// <remarks>誤って手書きコードを上書きしないよう、出力対象を ".g.cs" 拡張子のファイルに限定する</remarks>
public sealed class GeneratedFileWriter
{
    /// <summary>
    /// 生成ファイル群を指定ディレクトリへ書き出す
    /// </summary>
    /// <param name="outputDirectory">出力先ディレクトリ。存在しない場合は作成する</param>
    /// <param name="result">書き出す生成結果</param>
    /// <returns>書き出したファイルの絶対パス一覧</returns>
    /// <exception cref="InvalidOperationException">ファイル名が ".g.cs" で終わらない場合（手書きファイル保護のため上書きを拒否する）</exception>
    public IReadOnlyList<string> WriteFiles(string outputDirectory, CodeGenerationResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(result);

        Directory.CreateDirectory(outputDirectory);
        var writtenPaths = new List<string>();

        foreach (var file in result.Files)
        {
            // Path.GetFileName でディレクトリ要素を除去し、パストラバーサルによる出力先外への書き込みを防ぐ
            var fileName = Path.GetFileName(file.FileName);
            if (!fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "生成ファイル以外は上書きできません。出力ファイル名は .g.cs で終わる必要があります。"
                );
            }

            var path = Path.Combine(outputDirectory, fileName);
            // BOM なし UTF-8 で出力する（git 差分やツール間の互換性を考慮）
            File.WriteAllText(
                path,
                file.Content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
            writtenPaths.Add(path);
        }

        return writtenPaths;
    }
}
