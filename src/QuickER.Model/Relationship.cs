namespace QuickER.Model;

/// <summary>
/// リレーション（外部キー）を構成する列ペア 1 組を表すモデル
/// JSON シリアライズの対象
/// </summary>
/// <remarks>
/// 複合外部キーは <see cref="Relationship.ColumnPairs"/> へ宣言順で複数組を並べて表現する。
/// 1 組だけなら従来どおりの単列外部キーになる。
/// </remarks>
public class RelationshipColumnPair
{
    /// <summary>JSON デシリアライズ用の既定コンストラクタ</summary>
    public RelationshipColumnPair() { }

    /// <summary>親列・子列を指定して列ペアを生成する</summary>
    /// <param name="sourceColumnId">起点（親・被参照）エンティティ側の列 ID</param>
    /// <param name="targetColumnId">終点（子・外部キー保有）エンティティ側の列 ID</param>
    public RelationshipColumnPair(Guid sourceColumnId, Guid targetColumnId)
    {
        SourceColumnId = sourceColumnId;
        TargetColumnId = targetColumnId;
    }

    /// <summary>起点エンティティ側（親・被参照）の列 ID</summary>
    public Guid SourceColumnId { get; set; }

    /// <summary>終点エンティティ側（子・外部キー保有）の列 ID</summary>
    public Guid TargetColumnId { get; set; }

    /// <summary>列ペアを複製する</summary>
    /// <param name="columnIdMap">
    /// 旧カラム ID → 新カラム ID の対応表。<c>null</c> または対応が無い ID はそのまま維持する
    /// </param>
    public RelationshipColumnPair Clone(IReadOnlyDictionary<Guid, Guid>? columnIdMap = null) =>
        new(MapId(SourceColumnId, columnIdMap), MapId(TargetColumnId, columnIdMap));

    /// <summary>対応表があれば ID を差し替える（無ければそのまま返す）</summary>
    private static Guid MapId(Guid id, IReadOnlyDictionary<Guid, Guid>? columnIdMap) =>
        columnIdMap is not null && columnIdMap.TryGetValue(id, out var mapped) ? mapped : id;
}

/// <summary>
/// 2 つのエンティティを結ぶリレーション（関連）を表すモデル
/// JSON シリアライズの対象
/// </summary>
public class Relationship
{
    /// <summary>リレーションの一意識別子</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>関連の起点となる <see cref="Entity"/> の ID</summary>
    public Guid SourceEntityId { get; set; }

    /// <summary>関連の終点となる <see cref="Entity"/> の ID</summary>
    public Guid TargetEntityId { get; set; }

    /// <summary>関連の種類（1対1 / 1対多 / 多対多）</summary>
    public RelationshipType Type { get; set; } = RelationshipType.OneToMany;

    /// <summary>外部キーを構成する列ペアの一覧（宣言順。複合外部キーは 2 組以上になる）</summary>
    /// <remarks>
    /// <b>列ペアがこのリレーションの外部キー定義の唯一の正本</b>で、推測によるフォールバックは行わない。
    /// 空リスト（多対多・列未設定）のリレーションは DDL の <c>FOREIGN KEY</c> 句・外部キー差分の
    /// 対象外としてスキップされる。
    /// </remarks>
    public List<RelationshipColumnPair> ColumnPairs { get; set; } = [];

    /// <summary>DB から取り込んだ外部キー制約名（手動作成のリレーションでは <c>null</c>）</summary>
    public string? ConstraintName { get; set; }

    /// <summary>親行削除時の参照アクション</summary>
    public ForeignKeyReferentialAction OnDelete { get; set; } =
        ForeignKeyReferentialAction.NoAction;

    /// <summary>親キー更新時の参照アクション</summary>
    public ForeignKeyReferentialAction OnUpdate { get; set; } =
        ForeignKeyReferentialAction.NoAction;
}
