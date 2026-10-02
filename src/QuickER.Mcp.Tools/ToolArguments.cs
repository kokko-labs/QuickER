using System.Text.Json;

namespace QuickER.Mcp.Tools;

/// <summary>ツール引数（JSON）から値を読み取る共通処理（図ツールとクエリツールが共有する）</summary>
internal static class ToolArguments
{
    /// <summary>JSON 引数から文字列プロパティを取得する（無い・型不一致なら null）</summary>
    /// <param name="element">引数オブジェクト</param>
    /// <param name="propertyName">プロパティ名</param>
    /// <returns>文字列値。無い・文字列でないときは null</returns>
    public static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>JSON 引数から真偽プロパティを取得する（無い・型不一致なら null）</summary>
    /// <param name="element">引数オブジェクト</param>
    /// <param name="propertyName">プロパティ名</param>
    /// <returns>真偽値。無い・真偽値でないときは null</returns>
    public static bool? GetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
