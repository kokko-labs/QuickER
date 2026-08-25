using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Services;

/// <summary>ER 図のエクスポート形式</summary>
internal enum DiagramExportFormat
{
    /// <summary>PNG 画像</summary>
    Png,

    /// <summary>SVG 画像</summary>
    Svg,

    /// <summary>SQL（DDL）スクリプト</summary>
    Sql,

    /// <summary>Mermaid 記法</summary>
    Mermaid,

    /// <summary>DBML 記法</summary>
    Dbml,

    /// <summary>Excel テーブル定義書</summary>
    Excel,

    /// <summary>HTML テーブル定義書</summary>
    Html,

    /// <summary>スキーマのみ JSON（レイアウトなし・可逆）</summary>
    SchemaJson,
}

/// <summary>ER 図のインポート形式</summary>
internal enum DiagramImportFormat
{
    /// <summary>Mermaid 記法</summary>
    Mermaid,

    /// <summary>DBML 記法</summary>
    Dbml,

    /// <summary>Excel テーブル定義書</summary>
    Excel,
}

/// <summary>
/// エクスポート／インポートのコマンドサービス（<see cref="DiagramExportService"/> /
/// <see cref="DiagramImportService"/>）が MainViewModel から借りる能力の内部シーム。
/// </summary>
/// <remarks>
/// サービスを VM 本体でなくこの面へ依存させることで、入出力の判断ロジック（形式解決・置換確認・
/// 欠落告知）を VM を組み立てずに単体検証できる。SVG 描画だけは VM のエンティティ表示状態から
/// ベクタ描画する構造のため、描画呼び出しをホスト側に置く。
/// </remarks>
internal interface IDiagramTransferHost
{
    /// <summary>現在の ER 図を意味モデル（クエリ込み・視覚情報なし）へ変換する</summary>
    ErDiagram BuildModel();

    /// <summary>現在の対象 DBMS プロバイダ（DDL 生成に使う）</summary>
    IDatabaseProvider CurrentProvider { get; }

    /// <summary>未保存の変更があるか（置換確認の警告水準の選択に使う）</summary>
    bool IsDirty { get; }

    /// <summary>図が空で失うものが何も無いか（置換を無確認で通す条件）</summary>
    bool HasNothingToLose { get; }

    /// <summary>名前付きクエリの件数（Mermaid / DBML 置換で失われるクエリの告知に使う）</summary>
    int QueryCount { get; }

    /// <summary>現在の図を SVG としてファイルへ書き出す（VM の表示状態からのベクタ描画）</summary>
    void RenderSvg(string path);

    /// <summary>図を丸ごと置換する（履歴なし・全体自動整列＝Mermaid / DBML 取込用）</summary>
    void ReplaceWholesale(
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships
    );

    /// <summary>マージ済みの図で置換する（Guid 引継＝レイアウト・クエリ温存・方言採用＝Excel 取込用）</summary>
    void ReplaceMerged(ErDiagram diagram);
}
