using QuickER.Provider;

namespace QuickER.Services;

/// <summary>行指向のテキスト形式（Mermaid / DBML）へ図の名前・説明を載せるときのサニタイズ</summary>
/// <remarks>
/// Mermaid の <c>erDiagram</c> も DBML も「1 行 1 要素」の記法で、テーブル名・列名・制約名に改行が混じると
/// 行が途中で終わり、残りが別の宣言として解釈される（＝出力の構造がユーザーデータで壊せる）。
/// 規則は SQL の <c>--</c> 行コメントと同一のため <see cref="SqlComment.Sanitize"/> へ委譲し、判定を二重に持たない。
/// </remarks>
internal static class ExportTextSanitizer
{
    /// <summary>改行・制御文字を空白 1 つへ畳む（<c>null</c> は空文字）</summary>
    public static string Sanitize(string? text) => SqlComment.Sanitize(text);
}
