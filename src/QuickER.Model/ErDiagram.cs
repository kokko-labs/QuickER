using QuickER.Dialects;

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
    public string TargetDbms { get; set; } = DialectNames.SqlServer;

    /// <summary>ER 図に含まれる全エンティティ</summary>
    public List<Entity> Entities { get; set; } = new();

    /// <summary>ER 図に含まれる全リレーション</summary>
    public List<Relationship> Relationships { get; set; } = new();

    /// <summary>ER 図に定義された名前付きクエリ（コード生成で Repository メソッドになる）</summary>
    public List<QueryDefinition> Queries { get; set; } = new();

    /// <summary>エンティティの一覧だけを差し替えた浅い複製を返す（ほかのプロパティは同じ値・同じインスタンスを共有する）</summary>
    /// <param name="entities">複製に持たせるエンティティの一覧</param>
    /// <remarks>
    /// 処理の途中だけで使う作業用の図を作るためのもの（例：C# 生成が主キーの列を NOT NULL とみなす図）。
    /// プロパティを手で並べて写さず <see cref="object.MemberwiseClone"/> で写すので、この型にプロパティを足しても複製から落ちない。
    /// </remarks>
    public ErDiagram WithEntities(List<Entity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var copy = (ErDiagram)MemberwiseClone();
        copy.Entities = entities;
        return copy;
    }
}
