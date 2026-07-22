namespace QuickER.Model;

/// <summary>
/// ER 図全体を表すルートモデル
/// ファイル保存・自動保存における JSON シリアライズの単位
/// </summary>
public class ErDiagram
{
    /// <summary>この図のターゲット DBMS（プロバイダ識別名。例: sqlserver）</summary>
    /// <remarks>
    /// 宣言順が JSON の出力順になるため先頭に置く（ファイルを開いたとき対象 DB がすぐ分かるようにする。
    /// 読み込みはキー順に依存しないため、旧順序で保存されたファイルもそのまま読める）
    /// </remarks>
    public string TargetDbms { get; set; } = "sqlserver";

    /// <summary>ER 図に含まれる全エンティティ</summary>
    public List<Entity> Entities { get; set; } = new();

    /// <summary>ER 図に含まれる全リレーション</summary>
    public List<Relationship> Relationships { get; set; } = new();

    /// <summary>ER 図に定義された名前付きクエリ（コード生成で Repository メソッドになる）</summary>
    public List<QueryDefinition> Queries { get; set; } = new();
}
