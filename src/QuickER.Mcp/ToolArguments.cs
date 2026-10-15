using System.Text.Json;

namespace QuickER.Mcp;

/// <summary>
/// ツール引数（JSON）から値を読み取る共通処理（MCP のツールホスト・内蔵チャットのツール・コード生成のツール・モック生成のツールが共有する）
/// </summary>
public static class ToolArguments
{
    /// <summary>JSON 引数から文字列プロパティを取得する（無い・型不一致なら null）</summary>
    /// <param name="element">引数オブジェクト（オブジェクトでなければ null を返す）</param>
    /// <param name="propertyName">プロパティ名</param>
    /// <returns>文字列値。無い・文字列でないときは null</returns>
    /// <remarks>
    /// 形の検査は入口（<see cref="ToolArgumentShape"/>）が済ませるが、<see cref="JsonElement.TryGetProperty(string, out JsonElement)"/> は
    /// オブジェクトでない要素で例外を投げるので、ここでも要素の種類を見る（入口を通らない呼び出しで生の例外にしない）。
    /// </remarks>
    public static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>JSON 引数から真偽プロパティを取得する（無い・型不一致なら null）</summary>
    /// <param name="element">引数オブジェクト（オブジェクトでなければ null を返す）</param>
    /// <param name="propertyName">プロパティ名</param>
    /// <returns>真偽値。無い・真偽値でないときは null</returns>
    public static bool? GetBool(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
