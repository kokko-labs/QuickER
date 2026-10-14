namespace QuickER.Mcp;

/// <summary>ER 図操作ツール（<see cref="ErDiagramToolCatalog"/> の 15 ツール）の名前の正本</summary>
/// <remarks>
/// カタログ（公開する名前）と、ツールを振り分ける側（MCP のファイルホスト・内蔵チャットのアダプタ）が同じ定数を読む
/// （名前がずれると、公開しているツールが Unsupported tool を返す）。GUI のツール実行（<c>ErDiagramDynamicTools</c>）は
/// このプロジェクトを参照しないので文字列で振り分けるが、パリティテストが両ホストの結果の一致を見る。
/// </remarks>
public static class ErDiagramToolNames
{
    /// <summary>図の要約</summary>
    public const string GetDiagramSummary = "get_diagram_summary";

    /// <summary>テーブルの追加</summary>
    public const string AddEntity = "add_entity";

    /// <summary>テーブルの削除</summary>
    public const string RemoveEntity = "remove_entity";

    /// <summary>列の追加</summary>
    public const string AddColumn = "add_column";

    /// <summary>列の削除</summary>
    public const string RemoveColumn = "remove_column";

    /// <summary>テーブルのプロパティの変更</summary>
    public const string SetEntityProperty = "set_entity_property";

    /// <summary>列のプロパティの変更</summary>
    public const string SetColumnProperty = "set_column_property";

    /// <summary>リレーションの追加</summary>
    public const string AddRelationship = "add_relationship";

    /// <summary>リレーションの削除</summary>
    public const string RemoveRelationship = "remove_relationship";

    /// <summary>主キーの設定</summary>
    public const string SetPrimaryKey = "set_primary_key";

    /// <summary>一意制約の定義（upsert）</summary>
    public const string SetUniqueConstraint = "set_unique_constraint";

    /// <summary>一意制約の削除</summary>
    public const string RemoveUniqueConstraint = "remove_unique_constraint";

    /// <summary>名前付きクエリの定義（upsert）</summary>
    public const string SetQuery = "set_query";

    /// <summary>名前付きクエリの一覧</summary>
    public const string ListQueries = "list_queries";

    /// <summary>名前付きクエリの削除</summary>
    public const string RemoveQuery = "remove_query";
}
