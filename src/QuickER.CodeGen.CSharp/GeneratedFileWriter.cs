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
    /// <para>
    /// 層別出力（<see cref="GeneratedFile.RelativeDirectory"/> が非 null）のファイルは、出力ディレクトリ配下の
    /// 層フォルダ（必要なら作成する）へ書き出す。それ以外は出力ディレクトリ直下。
    /// </para>
    /// <para>
    /// 既存ファイルが生成後に手で編集されているかは確かめない。上書きしてよいかの判断は呼び出し側が
    /// 書き込み前に <see cref="FindModifiedFiles"/> で行う（確認の出し方が GUI・CLI・MCP で異なるため）。
    /// </para>
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
            var path = ResolveTargetPath(outputDirectory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

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

    /// <summary>
    /// 書き込み先に既にある <c>.g.cs</c> のうち、生成後に手で編集されたもの（内容ハッシュが一致しないもの）を列挙する。
    /// </summary>
    /// <param name="outputDirectory">出力先ディレクトリ（<see cref="WriteFiles"/> に渡すものと同じ）</param>
    /// <param name="result">これから書き出す生成結果</param>
    /// <returns>手で編集された既存ファイルの絶対パス一覧（書き出し順）。無ければ空</returns>
    /// <remarks>
    /// 照合は <see cref="GeneratedContentHash.Verify"/> に委ねる。内容ハッシュの行を持たないファイル
    /// （この機能より前の版の生成物・ヘッダーを消されたファイル）は判定できないため列挙しない。
    /// <c>.g.md</c> は内容ハッシュを持たないため対象外。ディスクへは何も書かない。
    /// </remarks>
    /// <exception cref="InvalidOperationException"><see cref="WriteFiles"/> と同じ条件（書き出し先の検証）</exception>
    public IReadOnlyList<string> FindModifiedFiles(
        string outputDirectory,
        CodeGenerationResult result
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(result);

        var modifiedPaths = new List<string>();

        foreach (var file in result.Files)
        {
            var path = ResolveTargetPath(outputDirectory, file);

            if (!path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                continue;
            }

            if (
                GeneratedContentHash.Verify(File.ReadAllText(path))
                == GeneratedContentHashStatus.Modified
            )
            {
                modifiedPaths.Add(path);
            }
        }

        return modifiedPaths;
    }

    /// <summary>
    /// 生成ファイルの書き出し先の絶対パスを求める（書き出しと照合が同じ規則でパスを解決するための単一の正本）。
    /// </summary>
    /// <remarks>ディレクトリは作らない（作成は書き出し側の責務）</remarks>
    /// <exception cref="InvalidOperationException"><see cref="WriteFiles"/> の例外条件を参照</exception>
    private static string ResolveTargetPath(string outputDirectory, GeneratedFile file)
    {
        // Path.GetFileName でディレクトリ要素を除去し、パストラバーサルによる出力先外への書き込みを防ぐ
        // （サブフォルダへの振り分けは検証済みの RelativeDirectory だけが担う＝FileName にパスは書けない）
        var fileName = Path.GetFileName(file.FileName);
        if (
            !fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".g.md", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidOperationException(Strings.CodeGen_Error_NonGeneratedFileOverwrite);
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
        }

        return Path.GetFullPath(Path.Combine(targetDirectory, fileName));
    }
}
