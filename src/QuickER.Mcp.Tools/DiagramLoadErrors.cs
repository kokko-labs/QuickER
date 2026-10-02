using QuickER.Documents;

namespace QuickER.Mcp.Tools;

/// <summary>図ファイルの読込失敗を MCP ツールの応答文言（英語正本）へ翻訳する</summary>
/// <remarks>
/// 図を読む MCP ツールは 2 か所にある＝ER 図操作ツール（<see cref="DocumentErDiagramToolHost"/>）と、
/// 合成ルート（CLI）側のコード生成ツール。どちらも同じ失敗に同じ文言を返すよう、翻訳はここだけに置く。
/// </remarks>
public static class DiagramLoadErrors
{
    /// <summary>読込失敗の種別を、ファイル名を含む英語の 1 文へ翻訳する</summary>
    /// <param name="file">読もうとした図ファイルのパス</param>
    /// <param name="error">失敗の種別（<see cref="JsonStorageService.TryLoad(string, out DiagramDocument?, out DocumentLoadError, out Exception?)"/> の結果）</param>
    /// <param name="exception">失敗の原因となった例外（種別によっては null）</param>
    /// <returns>ツールの結果としてそのまま返せる文言</returns>
    public static string Describe(string file, DocumentLoadError error, Exception? exception) =>
        error switch
        {
            DocumentLoadError.ReadFailed =>
                $"Failed to read diagram file '{file}': {exception!.Message}",
            DocumentLoadError.InvalidJson =>
                $"Diagram file '{file}' is not valid JSON: {exception!.Message}",
            DocumentLoadError.NotDiagramDocument =>
                $"Diagram file '{file}' is not a DiagramDocument (expected an object with 'Version' and 'Schema'). Refusing to treat unrelated JSON as a diagram.",
            DocumentLoadError.DuplicateId =>
                $"Diagram file '{file}' uses the same identifier for more than one table or column, so it cannot be loaded: {exception!.Message}",
            DocumentLoadError.MissingRequiredText =>
                $"Diagram file '{file}' leaves a required name or type empty (null), so it cannot be loaded: {exception!.Message}",
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
        };
}
