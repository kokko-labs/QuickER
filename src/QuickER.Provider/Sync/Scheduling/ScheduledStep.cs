using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Scheduling;

/// <summary>組み立ての途中の文 1 つ（文と、並べるときにだけ要る値）</summary>
/// <param name="Step">文</param>
internal sealed record ScheduledStep(SyncStep Step)
{
    /// <summary>
    /// 外部キーの文のリレーション（外部キーを外す文の DB の実効名を、DB の今の図の外部キーの名前から引く。ほかの文は null）
    /// </summary>
    public Relationship? Relationship { get; init; }

    /// <summary>
    /// 外部キーを付ける文の参照先の表の定義（参照先の表の同期後の主キーが分からないときに、列の組をこの表のキーへそろえる。
    /// ほかの文は null）
    /// </summary>
    public Entity? ParentDefinition { get; init; }
}

/// <summary>逐次 DDL の方言の組み立ての結果</summary>
/// <param name="Steps">文の並び（文で表せない項目があれば null）</param>
/// <param name="Unrepresentable">文で表せない理由（表せれば null）</param>
/// <param name="Required">
/// 外し直しの宣言が「選んだ文を流す間、外れていなければならない」と答えた DB の今の制約（同じ計画が明示に外す制約と、
/// 付け直せないので外さない制約も含む。DB の今の図が無いか、文で表せなければ null）
/// </param>
internal sealed record SequentialSchedule(
    IReadOnlyList<SyncStep>? Steps,
    string? Unrepresentable,
    RequiredConstraints? Required = null
);

/// <summary>外し直しを決める段の入力</summary>
/// <param name="Steps">選んだ項目の最終形から写した文</param>
/// <param name="PostSync">選んだ項目の最終形の同期後の事実</param>
/// <param name="Live">DB の今の図</param>
/// <param name="Capabilities">方言の宣言</param>
internal sealed record SchedulingInput(
    IReadOnlyList<ScheduledStep> Steps,
    PostSyncSchema PostSync,
    LiveSchema Live,
    SyncDialectCapabilities Capabilities
);

/// <summary>DB の今の図にある制約 1 つ（外し直しの宣言が「外れていなければならない」と答える単位）</summary>
internal abstract record LiveConstraint;

/// <summary>DB の今の図にある外部キー</summary>
/// <param name="ForeignKey">親子の表と列の組まで解決した外部キー（<see cref="LiveSchema.ForeignKeys"/> の要素）</param>
internal sealed record LiveForeignKey(ResolvedForeignKeyRelationship ForeignKey) : LiveConstraint;

/// <summary>DB の今の図にある一意制約</summary>
/// <param name="Table">一意制約を持つ表</param>
/// <param name="Constraint">一意制約（<paramref name="Table"/> の <see cref="Entity.UniqueConstraints"/> の要素）</param>
internal sealed record LiveUniqueConstraint(Entity Table, UniqueConstraint Constraint)
    : LiveConstraint;

/// <summary>DB の今の図にある主キー</summary>
/// <param name="Table">主キーを持つ表（<see cref="LiveSchema.Entities"/> の要素）</param>
internal sealed record LivePrimaryKey(Entity Table) : LiveConstraint;

/// <summary>宣言が「外れていなければならない」と答えた制約（種類ごとの集合。どれも参照で比べる）</summary>
/// <param name="ForeignKeys">外部キー</param>
/// <param name="UniqueConstraints">一意制約</param>
/// <param name="PrimaryKeys">主キーを持つ表</param>
internal sealed record RequiredConstraints(
    HashSet<ResolvedForeignKeyRelationship> ForeignKeys,
    HashSet<UniqueConstraint> UniqueConstraints,
    HashSet<Entity> PrimaryKeys
);
