using System.IO;
using QuickER.Documents;

namespace QuickER.Mcp.Tools;

/// <summary>図を読む MCP ツールの、読み込みの決まり文句（不在の案内・形式の検証・新しい版の警告）</summary>
/// <remarks>
/// ER 図操作ツール（<see cref="DocumentErDiagramToolHost"/>）とコード生成ツール（CLI）が共有する。
/// 文言を 1 か所に置き、ツールどうしで食い違わないようにする。
/// </remarks>
public static class DiagramFileReader
{
    /// <summary>図ファイルを読み込む（不在・形式の不正はエラー文。新しい版は読み込んで返し、扱いは呼び出し側が決める）</summary>
    /// <param name="file">図ファイルのパス</param>
    /// <returns>読み込んだ文書と、失敗したときのエラー文</returns>
    public static (DiagramDocument? Document, string? Error) Read(string file)
    {
        if (!File.Exists(file))
        {
            return (
                null,
                $"Diagram file not found: {file}. To create a new diagram, call {DocumentErDiagramToolHost.CreateDiagramToolName} first."
            );
        }

        return JsonStorageService.TryLoad(file, out var document, out var error, out var exception)
            ? (document, null)
            : (null, DiagramLoadErrors.Describe(file, error, exception));
    }

    /// <summary>読み取り系のツールが応答の先頭へ付ける警告（新しい版の図のときだけ。そうでなければ null）</summary>
    /// <param name="document">読み込んだ文書</param>
    public static string? NewerFormatWarning(DiagramDocument document) =>
        document.IsNewerFormat
            ? $"Warning: this diagram was saved in a newer format (version {document.Version} > supported {DiagramDocument.CurrentVersion}); unknown data may be omitted."
            : null;
}
