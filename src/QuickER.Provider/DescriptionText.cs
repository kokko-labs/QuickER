using System.Diagnostics.CodeAnalysis;

namespace QuickER.Provider;

/// <summary>
/// 図と DB の説明（テーブル・列）の空欄の判定。DDL・差分・同期スクリプトはこの判定だけを使う
/// </summary>
/// <remarks>
/// 空白だけの説明は「説明なし」として扱う（DDL は書かず、差分は空と同じとみなし、同期は削除として書く）。
/// 判定を経路ごとに持つと、DDL が書かない空白だけの説明を差分が「説明あり」と比べ、適用した直後に偽の差分が出る。
/// 空白以外を含む説明は書き換えない（前後の空白も DB へそのまま書き、そのまま比べる）。
/// </remarks>
public static class DescriptionText
{
    /// <summary>説明があるか（null・空・空白だけでない）</summary>
    /// <param name="description">説明</param>
    public static bool IsPresent([NotNullWhen(true)] string? description) =>
        !string.IsNullOrWhiteSpace(description);

    /// <summary>説明の比較・書き出しに使う形（説明が無ければ空文字、あればそのまま）</summary>
    /// <param name="description">説明</param>
    public static string Normalize(string? description) =>
        IsPresent(description) ? description : string.Empty;
}
