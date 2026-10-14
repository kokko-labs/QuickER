using System.Text;
using System.Text.Json;
using QuickER.Documents;

namespace QuickER.Mcp.Tools;

/// <summary>
/// 名前付きクエリ定義ツール（<c>set_query</c> / <c>list_queries</c> / <c>remove_query</c>）の MCP 面ラッパ。
/// </summary>
/// <remarks>
/// <para>
/// 実処理（検証・upsert・一覧化）は面非依存の <see cref="QueryToolCore"/> が担い、本 partial は
/// 「コア呼び出し → 英語フォーマッタ（<see cref="QueryToolEnglishFormatter"/>）→ ファイル IO」の薄い接続だけを持つ。
/// 変更系（set / remove）は <see cref="Mutate"/> に乗り、コアが図（<see cref="ErDiagram.Queries"/>）を成功時のみ
/// 更新した結果を受けて保存する。読み取り系（list）はファイル存在・新フォーマット警告をここで扱う。
/// </para>
/// <para>
/// 内蔵チャット面も同じコアを使い、表示言語の整形（<c>QueryToolLocalizedFormatter</c>）を対で持つ。
/// </para>
/// </remarks>
public static partial class DocumentErDiagramToolHost
{
    /// <summary>クエリ定義を upsert するツールの名前（正本は <see cref="QueryToolCore.SetQueryToolName"/>）</summary>
    public const string SetQueryToolName = QueryToolCore.SetQueryToolName;

    /// <summary>クエリ定義を一覧するツール（読み取り系）の名前（正本は <see cref="QueryToolCore.ListQueriesToolName"/>）</summary>
    public const string ListQueriesToolName = QueryToolCore.ListQueriesToolName;

    /// <summary>クエリ定義を 1 件削除するツールの名前（正本は <see cref="QueryToolCore.RemoveQueryToolName"/>）</summary>
    public const string RemoveQueryToolName = QueryToolCore.RemoveQueryToolName;

    /// <summary>名前付きクエリ定義を 1 件 upsert する（コアで検証・変更、英語で整形）</summary>
    private static (string, bool) SetQuery(DiagramDocument document, JsonElement args)
    {
        var outcome = QueryToolCore.SetQuery(document.Schema, args);

        return (QueryToolEnglishFormatter.FormatSetQuery(outcome), outcome.Success);
    }

    /// <summary>テーブル名＋クエリ名で 1 件削除する（コアで検証・変更、英語で整形）</summary>
    private static (string, bool) RemoveQuery(DiagramDocument document, JsonElement args)
    {
        var outcome = QueryToolCore.RemoveQuery(document.Schema, args);

        return (QueryToolEnglishFormatter.FormatRemoveQuery(outcome), outcome.Success);
    }

    /// <summary>図の名前付きクエリをエンティティ別に一覧する（読み取り系＝新フォーマットは警告付きで続行）</summary>
    private static (string, bool) ListQueries(string file)
    {
        var (document, error) = DiagramFileReader.Read(file);

        if (error is not null)
        {
            return (error, false);
        }

        var sb = new StringBuilder();
        AppendNewerFormatWarning(sb, document!);

        var outcome = QueryToolCore.ListQueries(document!.Schema);
        sb.Append(QueryToolEnglishFormatter.FormatListing(outcome.Listing!));

        return (sb.ToString().TrimEnd(), true);
    }
}
