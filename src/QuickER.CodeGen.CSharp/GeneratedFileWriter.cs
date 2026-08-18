using System.Text;
using QuickER.CodeGen.CSharp.Resources;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 生成結果のファイルをディスクへ書き出すライター
/// </summary>
/// <remarks>誤って手書きコードを上書きしないよう、出力対象を ".g.cs"（生成コード）・".g.md"（生成 API リファレンス）拡張子のファイルに限定する</remarks>
public sealed class GeneratedFileWriter
{
    /// <summary>
    /// 生成ファイル群を指定ディレクトリへ書き出す
    /// </summary>
    /// <param name="outputDirectory">出力先ディレクトリ。存在しない場合は作成する</param>
    /// <param name="result">書き出す生成結果</param>
    /// <returns>書き出したファイルの絶対パス一覧</returns>
    /// <remarks>
    /// 層別出力（<see cref="GeneratedFile.RelativeDirectory"/> が非 null）のファイルは、出力ディレクトリ配下の
    /// 層フォルダ（必要なら作成する）へ書き出す。それ以外は従来どおり出力ディレクトリ直下。
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// ファイル名が ".g.cs" または ".g.md" で終わらない場合（手書きファイル保護のため上書きを拒否する）、
    /// または層フォルダが出力ディレクトリ内に収まる相対パスでない場合（出力先外への書き込み防止）
    /// </exception>
    public IReadOnlyList<string> WriteFiles(string outputDirectory, CodeGenerationResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(result);

        Directory.CreateDirectory(outputDirectory);
        var writtenPaths = new List<string>();

        foreach (var file in result.Files)
        {
            // Path.GetFileName でディレクトリ要素を除去し、パストラバーサルによる出力先外への書き込みを防ぐ
            // （サブフォルダへの振り分けは検証済みの RelativeDirectory だけが担う＝FileName にパスは書けない）
            var fileName = Path.GetFileName(file.FileName);
            if (
                !fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                && !fileName.EndsWith(".g.md", StringComparison.OrdinalIgnoreCase)
            )
            {
                throw new InvalidOperationException(
                    Strings.CodeGen_Error_NonGeneratedFileOverwrite
                );
            }

            // 層別出力の層フォルダ。生成前の診断が検証済みだが、ここでも同じ規則（LayerDirectoryValidator）で
            // 防御し、出力ディレクトリ外への書き込みを構造的に不可能にする（診断を素通りする呼び出し経路への備え）
            var targetDirectory = outputDirectory;

            if (!string.IsNullOrWhiteSpace(file.RelativeDirectory))
            {
                if (!LayerDirectoryValidator.IsValid(file.RelativeDirectory))
                {
                    throw new InvalidOperationException(
                        string.Format(
                            Strings.CodeGen_Error_LayerDirectoryOutsideOutput,
                            file.RelativeDirectory
                        )
                    );
                }

                targetDirectory = Path.Combine(
                    outputDirectory,
                    LayerDirectoryValidator.Normalize(file.RelativeDirectory)
                );
                Directory.CreateDirectory(targetDirectory);
            }

            var path = Path.Combine(targetDirectory, fileName);
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
