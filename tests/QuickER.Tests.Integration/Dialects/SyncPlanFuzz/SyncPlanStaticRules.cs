using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>静的な規則の判定の結果</summary>
/// <param name="PredictedFailure">計画の項目の並びのうち、DB が断ると規則が予言した最初の文（無ければ null）</param>
/// <param name="Violations">規則の違反（空なら合格）</param>
internal sealed record SyncPlanStaticVerdict(
    string? PredictedFailure,
    IReadOnlyList<string> Violations
)
{
    /// <summary>予言も違反も無い</summary>
    public static SyncPlanStaticVerdict None { get; } = new(null, []);
}

/// <summary>
/// 同期計画を DB を使わずに確かめる規則（能力の組の網＝<see cref="SyncPlanCapabilityNetTestsBase"/> と、実 DB の総当たりの
/// 突き合わせ＝<see cref="SyncPlanFuzzRunner"/> が使う）
/// </summary>
/// <remarks>
/// <para>
/// 計画のセクションの項目を並び（<see cref="SyncPlan.Sections"/> の順）に沿って、DB 側の制約（主キー・一意制約・外部キー）と
/// 列の型の状態を追い、各項目を当てる直前の状態で、DB がその文を断るかを DB の規則で判定する。最初に断られる文までを見る
/// （実行はそこで止まる）。
/// </para>
/// <list type="bullet">
///   <item>R1：主キー・一意制約・表を消す前に、それを参照する外部キーがすべて外れている（SQL Server 3725・PostgreSQL 2BP01・
///   Oracle ORA-02273 / ORA-02449・MySQL 1553）。MySQL（<see cref="SyncDialectCapabilities.ForeignKeyRequiresLeadingIndex"/>）は、
///   参照する列が消す索引の先頭に並ぶ外部キーも、ほかの索引が同じ先頭の列を持たなければ数える。</item>
///   <item>R2：SQL Server（<see cref="SyncDialectCapabilities.AlterColumnRequiresKeyConstraintRebuild"/>）の型の変更の
///   ALTER COLUMN の前に、その列を含む一意制約・主キーがすべて外れている（Msg 5074）。外部キー（両端）は
///   <see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/> の方言（SQL Server・MySQL）で同じく外れている。
///   NULL 許容だけの変更は数えない（SQL Server は外部キーを張ったまま子の列を NOT NULL にできる＝製品の宣言どおり）。</item>
///   <item>R3：MySQL の索引の削除（一意制約の削除・主キーの解除）の前に、子の列がその索引の先頭に並ぶ外部キーが外れているか、
///   同じ先頭の列を持つ別の索引（主キー・一意制約・外部キーが作った索引）が残っている（MySQL 1553）。</item>
///   <item>R4：方言の名前の範囲で、足す制約の名前が残っている制約と重ならない（SQL Server・Oracle＝スキーマの全制約、
///   PostgreSQL＝主キー・一意制約はスキーマ・外部キーは表、MySQL＝外部キーはスキーマ・一意制約は表）。</item>
///   <item>R5：外部キーを足すとき、参照する列の集合が親の主キーか一意制約と一致する（候補キーの無い付け直しは DB が断る）。</item>
/// </list>
/// <para>
/// 違反になるのは、断られる文があるのに次のどちらでもないとき：(a) 失敗を予告する警告がある（<see cref="NonPredictingWarnings"/> 以外）
/// うえで、DDL が暗黙コミットされる方言（<see cref="SyncDialectCapabilities.TransactionalDdl"/> が偽）では、断られる文より前に
/// 「後ろで付け直す制約」を外していない（D9）。(b)「表・列の削除のために先に外した制約」が、断られる文より後ろの表・列の削除を待っていない
/// （D9 の規則 4 の形）。トランザクションの方言は失敗しても巻き戻るので、警告があれば (a) の外し方は問わない。利用者が選んだ制約の
/// 削除（付け直さない削除）は、失敗の前に走っても違反にしない（実 DB の表明 1b の規則 1 と同じ）。
/// 「予告の警告がある」は計画全体のどれかの警告（<see cref="NonPredictingWarnings"/> 以外）で判定し、断られる文と警告を結び付けない
/// （実 DB の表明 1 と同じ粒度）。
/// </para>
/// <para>
/// 状態の初期値は渡された DB 側の図（実 DB なら取込の結果、能力の網なら生成器が作った図）。名前の無い制約は DDL と同じ実効名
/// （<see cref="ConstraintNames"/>・<see cref="UniqueConstraintNaming"/>）で置く。再構築の方言（SQLite）は計画が文の並びを持たないので、
/// 何も判定しない。
/// </para>
/// </remarks>
internal static class SyncPlanStaticRules
{
    /// <summary>
    /// 失敗を予告しない種類の警告（項目を計画から外した・再構築の属性の喪失・列の削除に伴う一意制約の削除・説明の文字の置き換え）
    /// </summary>
    public static readonly IReadOnlySet<SyncPlanWarningKind> NonPredictingWarnings =
        new HashSet<SyncPlanWarningKind>
        {
            SyncPlanWarningKind.SelectedItemRequiresUnselectedItem,
            SyncPlanWarningKind.TableRebuildDropsColumnAttribute,
            SyncPlanWarningKind.UniqueConstraintDroppedWithColumn,
            SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey,
            SyncPlanWarningKind.DescriptionCharactersReplaced,
        };

    /// <summary>計画を規則で判定する</summary>
    /// <param name="dialect">方言名（名前の範囲の規則 R4 に使う）</param>
    /// <param name="liveEntities">DB 側の表</param>
    /// <param name="liveRelationships">DB 側の外部キー</param>
    /// <param name="selected">利用者が選んだ差分項目（計画が足した項目と見分ける）</param>
    /// <param name="plan">計画</param>
    /// <param name="capabilities">方言の能力</param>
    public static SyncPlanStaticVerdict Evaluate(
        string dialect,
        IReadOnlyList<Entity> liveEntities,
        IReadOnlyList<Relationship> liveRelationships,
        IReadOnlyCollection<SchemaDiffItem> selected,
        SyncPlan plan,
        SyncDialectCapabilities capabilities
    )
    {
        if (!capabilities.SupportsAlterColumn || !capabilities.SupportsForeignKeyAlter)
        {
            return SyncPlanStaticVerdict.None;
        }

        var steps = plan
            .Sections.SelectMany(section => section.Items.Select(item => new Step(section, item)))
            .ToList();
        var state = new State(dialect, capabilities);
        state.Load(liveEntities, liveRelationships);
        var doomedAt = -1;
        string? reason = null;

        for (var k = 0; k < steps.Count; k++)
        {
            reason = state.Check(steps[k]);

            if (reason is not null)
            {
                doomedAt = k;
                break;
            }

            state.Apply(steps[k]);
        }

        if (reason is null)
        {
            return SyncPlanStaticVerdict.None;
        }

        var predicted = $"{steps[doomedAt].Describe()}: {reason}";
        var violations = new List<string>();
        var warned = plan.Warnings.Any(w => !NonPredictingWarnings.Contains(w.Kind));

        if (!warned)
        {
            violations.Add($"static rule: {predicted} (no warning predicts the failure)");
        }

        if (!capabilities.TransactionalDdl)
        {
            violations.AddRange(
                DroppedBeforeFailure(steps, doomedAt, selected, plan, capabilities, predicted)
            );
        }

        return new SyncPlanStaticVerdict(predicted, violations);
    }

    /// <summary>
    /// D9：断られる文より前に外し、その後ろで付け直す制約（作り直し・主キーの変更・暗黙の外し直し）と、
    /// 断られる文より後ろの表・列の削除のために先に外した制約
    /// </summary>
    private static IEnumerable<string> DroppedBeforeFailure(
        List<Step> steps,
        int doomedAt,
        IReadOnlyCollection<SchemaDiffItem> selected,
        SyncPlan plan,
        SyncDialectCapabilities capabilities,
        string predicted
    )
    {
        var limit = capabilities.IdentifierLengthLimit;
        var selectedSet = new HashSet<SchemaDiffItem>(selected, ReferenceEqualityComparer.Instance);
        var readdedAfter = steps
            .Skip(doomedAt + 1)
            .Select(s => s.AddIdentity(capabilities))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var removalsAfter = steps.Skip(doomedAt + 1).Where(s => s.IsRemoval).ToList();

        // 付け直しが候補キーを証明できないと事前に警告した外部キー自身は、消えても違反にしない（製品の契約＝警告して実行する。
        // 実 DB の表明 1b の例外と同じ。巻き添えで消える別の制約は数える）
        var warnedForeignKeys = plan
            .Warnings.Where(w =>
                w.Kind
                    is SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey
                        or SyncPlanWarningKind.ForeignKeyNotOnCandidateKey
            )
            .Select(w => $"{Lower(w.TableName)}|{Lower(w.Detail)}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var step in steps.Take(doomedAt))
        {
            if (step.DropIdentity(capabilities) is not { } identity)
            {
                continue;
            }

            if (
                step.Kind == SchemaDiffKind.DropForeignKey
                && warnedForeignKeys.Contains(
                    $"{Lower(step.Item.ChildEntity!.TableName)}|{Lower(step.ForeignKeyName(limit))}"
                )
            )
            {
                continue;
            }

            if (readdedAfter.Contains(identity))
            {
                yield return $"static D9: {identity} is dropped before the failing statement and re-added after it ({predicted})";
                continue;
            }

            // 利用者が選んだ削除は、付け直さないので失敗の前に走ってもよい
            if (selectedSet.Contains(step.Item))
            {
                continue;
            }

            if (removalsAfter.FirstOrDefault(r => step.IsJustifiedBy(r)) is { } removal)
            {
                yield return $"static D9 (rule 4): {identity} is dropped for {removal.Describe()}, which runs after the failing statement ({predicted})";
            }
        }
    }

    /// <summary>計画の 1 項目（セクションと項目）</summary>
    private sealed record Step(SyncPlanSection Section, SchemaDiffItem Item)
    {
        public SchemaDiffKind Kind => Section.Kind;

        public string Table => Lower(Item.TableName);

        public string Describe() =>
            $"{Kind}{(Section.PrimaryKeyPhase == PrimaryKeyPhase.None ? "" : $":{Section.PrimaryKeyPhase}")} {Item.TableName}"
            + (Item.ColumnName is { } column ? $".{column}" : "")
            + (Item.ForeignKeyName is { } name ? $" ({name})" : "")
            + (Item.UniqueConstraintName is { } unique ? $" ({unique})" : "");

        /// <summary>表・列の削除か</summary>
        public bool IsRemoval => Kind is SchemaDiffKind.DropTable or SchemaDiffKind.DropColumn;

        /// <summary>外す制約の身元（外さない項目は null）</summary>
        public string? DropIdentity(SyncDialectCapabilities capabilities) =>
            Kind switch
            {
                SchemaDiffKind.DropForeignKey => ForeignKeyIdentity(capabilities),
                SchemaDiffKind.DropUniqueConstraint => UniqueIdentity(capabilities),
                SchemaDiffKind.AlterPrimaryKey
                    when Section.PrimaryKeyPhase == PrimaryKeyPhase.Drop => $"PK {Table}",
                _ => null,
            };

        /// <summary>足す制約の身元（足さない項目は null）</summary>
        public string? AddIdentity(SyncDialectCapabilities capabilities) =>
            Kind switch
            {
                SchemaDiffKind.AddForeignKey => ForeignKeyIdentity(capabilities),
                SchemaDiffKind.AddUniqueConstraint => UniqueIdentity(capabilities),
                SchemaDiffKind.AlterPrimaryKey
                    when Section.PrimaryKeyPhase == PrimaryKeyPhase.Add => $"PK {Table}",
                _ => null,
            };

        /// <summary>外した制約が、表・列の削除 <paramref name="removal"/> のためのものか</summary>
        public bool IsJustifiedBy(Step removal)
        {
            var columns = Kind switch
            {
                SchemaDiffKind.DropForeignKey => Item
                    .ForeignKeyColumnPairs.Select(p =>
                        $"{Lower(Item.ChildEntity!.TableName)}|{Lower(p.ChildColumn)}"
                    )
                    .Concat(
                        Item.ForeignKeyColumnPairs.Select(p =>
                            $"{Lower(Item.ParentEntity!.TableName)}|{Lower(p.ParentColumn)}"
                        )
                    ),
                SchemaDiffKind.DropUniqueConstraint => Item.UniqueConstraintColumns.Select(c =>
                    $"{Table}|{Lower(c)}"
                ),
                _ => [],
            };
            var tables =
                Kind == SchemaDiffKind.DropForeignKey
                    ? new[]
                    {
                        Lower(Item.ChildEntity!.TableName),
                        Lower(Item.ParentEntity!.TableName),
                    }
                    : [Table];

            return removal.Kind == SchemaDiffKind.DropTable
                ? tables.Contains(removal.Table)
                : columns.Contains($"{removal.Table}|{Lower(removal.Item.ColumnName!)}");
        }

        /// <summary>外す・足す外部キーの実効名（DDL・同期スクリプトが使う名前）</summary>
        public string ForeignKeyName(IdentifierLengthLimit limit) =>
            SyncScriptBuilderHelper.ForeignKeyNameToAdd(Item, limit);

        /// <summary>
        /// 外部キーの身元（名前を保存する方言は子の表と実効名＝作り直しで列や親が変わっても同じ外部キー。保存しない方言は子の列と親）
        /// </summary>
        private string ForeignKeyIdentity(SyncDialectCapabilities capabilities) =>
            capabilities.PersistsForeignKeyConstraintNames
                ? $"FK {Lower(Item.ChildEntity!.TableName)}.{Lower(ForeignKeyName(capabilities.IdentifierLengthLimit))}"
                : $"FK {Lower(Item.ChildEntity!.TableName)}({string.Join(",", Item.ForeignKeyColumnPairs.Select(p => Lower(p.ChildColumn)))})"
                    + $" -> {Lower(Item.ParentEntity!.TableName)}";

        /// <summary>一意制約の身元（表と実効名＝作り直しで構成列が変わっても同じ一意制約）</summary>
        private string UniqueIdentity(SyncDialectCapabilities capabilities) =>
            $"UQ {Table}.{Lower(UniqueConstraintNaming.Resolve(Item.UniqueConstraintName, Item.TableName, Item.UniqueConstraintColumns, capabilities.IdentifierLengthLimit))}";
    }

    /// <summary>外部キーの状態</summary>
    private sealed record ForeignKey(
        string Child,
        IReadOnlyList<string> ChildColumns,
        string Parent,
        IReadOnlyList<string> ParentColumns,
        string Name
    );

    /// <summary>主キー・一意制約の状態（索引になる）</summary>
    private sealed record Key(
        string Table,
        IReadOnlyList<string> Columns,
        string Name,
        bool Primary
    );

    /// <summary>DB 側の制約と列の型の状態</summary>
    private sealed class State(string dialect, SyncDialectCapabilities capabilities)
    {
        private readonly List<ForeignKey> _foreignKeys = [];
        private readonly List<Key> _keys = [];

        /// <summary>外部キーが作った索引（MySQL・子の表と列）</summary>
        private readonly List<(string Table, IReadOnlyList<string> Columns)> _foreignKeyIndexes =
        [];
        private readonly Dictionary<string, string> _types = new(StringComparer.Ordinal);

        private IdentifierLengthLimit Limit => capabilities.IdentifierLengthLimit;

        private bool IsMySql => dialect == DialectNames.MySql;

        public void Load(IReadOnlyList<Entity> entities, IReadOnlyList<Relationship> relationships)
        {
            foreach (var entity in entities)
            {
                AddTable(entity);
            }

            var names = ConstraintNames.ResolveForeignKeyNames(entities, relationships, Limit);

            foreach (var relationship in relationships)
            {
                var parent = entities.FirstOrDefault(e => e.Id == relationship.SourceEntityId);
                var child = entities.FirstOrDefault(e => e.Id == relationship.TargetEntityId);

                if (
                    parent is null
                    || child is null
                    || ForeignKeyColumnPairResolver.Resolve(relationship, parent, child)
                        is not { } pairs
                )
                {
                    continue;
                }

                AddForeignKey(
                    new ForeignKey(
                        Lower(child.TableName),
                        [.. pairs.Select(p => Lower(p.ChildColumn))],
                        Lower(parent.TableName),
                        [.. pairs.Select(p => Lower(p.ParentColumn))],
                        names[relationship]
                    )
                );
            }
        }

        /// <summary>項目を当てる直前の状態で、DB がその文を断るか（断るなら理由・断らないなら null）</summary>
        public string? Check(Step step) =>
            step.Kind switch
            {
                SchemaDiffKind.DropUniqueConstraint => CheckKeyDrop(
                    FindUnique(
                        step.Table,
                        step.Item.UniqueConstraintName,
                        step.Item.UniqueConstraintColumns
                    )
                ),
                SchemaDiffKind.DropTable => CheckTableDrop(step.Table),
                SchemaDiffKind.AlterPrimaryKey
                    when step.Section.PrimaryKeyPhase == PrimaryKeyPhase.Drop => CheckKeyDrop(
                    _keys.FirstOrDefault(k => k.Primary && k.Table == step.Table)
                ),
                SchemaDiffKind.AlterColumn => CheckAlterColumn(step),
                SchemaDiffKind.AddUniqueConstraint => CheckName(
                    step.Table,
                    UniqueName(step),
                    isForeignKey: false
                ),
                SchemaDiffKind.AlterPrimaryKey
                    when step.Section.PrimaryKeyPhase == PrimaryKeyPhase.Add => IsMySql
                    ? null
                    : CheckName(step.Table, PrimaryKeyName(step.Item.TableName), false),
                SchemaDiffKind.AddForeignKey => CheckForeignKeyAdd(step),
                SchemaDiffKind.AddTable => CheckTableAdd(step),
                _ => null,
            };

        public void Apply(Step step)
        {
            var item = step.Item;

            switch (step.Kind)
            {
                case SchemaDiffKind.DropForeignKey:
                    RemoveForeignKey(step);
                    break;
                case SchemaDiffKind.DropUniqueConstraint:
                    if (
                        FindUnique(
                            step.Table,
                            item.UniqueConstraintName,
                            item.UniqueConstraintColumns
                        ) is
                        { } unique
                    )
                    {
                        _keys.Remove(unique);
                    }

                    break;
                case SchemaDiffKind.DropTable:
                    _keys.RemoveAll(k => k.Table == step.Table);
                    _foreignKeys.RemoveAll(f => f.Child == step.Table);
                    _foreignKeyIndexes.RemoveAll(i => i.Table == step.Table);
                    break;
                case SchemaDiffKind.AddTable when item.Entity is { } entity:
                    AddTable(entity);
                    break;
                case SchemaDiffKind.AddColumn when item.Column is { } column:
                    _types[ColumnKey(step.Table, column.Name)] = column.DataType;
                    break;
                case SchemaDiffKind.AlterPrimaryKey
                    when step.Section.PrimaryKeyPhase == PrimaryKeyPhase.Drop:
                    _keys.RemoveAll(k => k.Primary && k.Table == step.Table);
                    break;
                case SchemaDiffKind.AlterPrimaryKey
                    when step.Section.PrimaryKeyPhase == PrimaryKeyPhase.Add
                        && item.Entity is { } entity:
                    var key = entity.GetPrimaryKeyColumnsInOrder();

                    if (key.Count > 0)
                    {
                        _keys.Add(
                            new Key(
                                step.Table,
                                [.. key.Select(c => Lower(c.Name))],
                                PrimaryKeyName(item.TableName),
                                true
                            )
                        );
                    }

                    break;
                case SchemaDiffKind.AlterColumn when item.Column is { } altered:
                    _types[ColumnKey(step.Table, item.ColumnName!)] = altered.DataType;
                    break;
                case SchemaDiffKind.DropColumn:
                    var dropped = Lower(item.ColumnName!);
                    _keys.RemoveAll(k => k.Table == step.Table && k.Columns.Contains(dropped));
                    _foreignKeys.RemoveAll(f =>
                        (f.Child == step.Table && f.ChildColumns.Contains(dropped))
                        || (f.Parent == step.Table && f.ParentColumns.Contains(dropped))
                    );
                    _types.Remove(ColumnKey(step.Table, dropped));
                    break;
                case SchemaDiffKind.AddUniqueConstraint:
                    _keys.Add(
                        new Key(
                            step.Table,
                            [.. item.UniqueConstraintColumns.Select(Lower)],
                            UniqueName(step),
                            false
                        )
                    );
                    break;
                case SchemaDiffKind.AddForeignKey:
                    AddForeignKey(
                        new ForeignKey(
                            Lower(item.ChildEntity!.TableName),
                            [.. item.ForeignKeyColumnPairs.Select(p => Lower(p.ChildColumn))],
                            Lower(item.ParentEntity!.TableName),
                            [.. item.ForeignKeyColumnPairs.Select(p => Lower(p.ParentColumn))],
                            SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, Limit)
                        )
                    );
                    break;
            }
        }

        // ---- 判定 ----

        /// <summary>R1・R3：主キー・一意制約を消す前に、それに支えられた外部キーが外れている</summary>
        private string? CheckKeyDrop(Key? key)
        {
            if (key is null)
            {
                return null;
            }

            var others = _keys
                .Where(k => !ReferenceEquals(k, key) && k.Table == key.Table)
                .ToList();

            foreach (var fk in _foreignKeys)
            {
                // R1：参照する列の集合がこのキーと一致する外部キー（MySQL は先頭に並ぶ外部キーも、ほかの索引が支えなければ）
                if (fk.Parent == key.Table)
                {
                    var exact = SameSet(fk.ParentColumns, key.Columns);
                    var leading =
                        capabilities.ForeignKeyRequiresLeadingIndex
                        && IsPrefix(fk.ParentColumns, key.Columns)
                        && !others.Any(o => IsPrefix(fk.ParentColumns, o.Columns));

                    if (exact && !others.Any(o => SameSet(o.Columns, key.Columns)) || leading)
                    {
                        return $"R1: {Describe(key)} is still referenced by {fk.Name}";
                    }
                }

                // R3：MySQL は子の列がこの索引の先頭に並ぶ外部キーの索引も、ほかの索引が支えなければ要る
                if (
                    IsMySql
                    && fk.Child == key.Table
                    && IsPrefix(fk.ChildColumns, key.Columns)
                    && !others.Any(o => IsPrefix(fk.ChildColumns, o.Columns))
                    && !_foreignKeyIndexes.Any(i =>
                        i.Table == key.Table && IsPrefix(fk.ChildColumns, i.Columns)
                    )
                )
                {
                    return $"R3: the index of {Describe(key)} is needed by {fk.Name}";
                }
            }

            return null;
        }

        /// <summary>R1：表を消す前に、ほかの表からの外部キーが外れている</summary>
        private string? CheckTableDrop(string table) =>
            _foreignKeys.FirstOrDefault(f => f.Parent == table && f.Child != table) is { } fk
                ? $"R1: table {table} is still referenced by {fk.Name}"
                : null;

        /// <summary>R2：型の変更の前に、列を含む一意制約・主キー・外部キーが外れている</summary>
        private string? CheckAlterColumn(Step step)
        {
            var column = Lower(step.Item.ColumnName!);
            var newType = step.Item.Column?.DataType;

            if (
                newType is null
                || !_types.TryGetValue(ColumnKey(step.Table, column), out var oldType)
                || capabilities.AreSameType(oldType, newType)
            )
            {
                return null;
            }

            if (
                capabilities.AlterColumnRequiresKeyConstraintRebuild
                && _keys.FirstOrDefault(k => k.Table == step.Table && k.Columns.Contains(column))
                    is { } key
            )
            {
                return $"R2: {step.Table}.{column} is altered while {Describe(key)} contains it";
            }

            if (
                capabilities.AlterColumnRequiresForeignKeyRebuild
                && _foreignKeys.FirstOrDefault(f =>
                    (f.Child == step.Table && f.ChildColumns.Contains(column))
                    || (f.Parent == step.Table && f.ParentColumns.Contains(column))
                )
                    is { } fk
            )
            {
                return $"R2: {step.Table}.{column} is altered while {fk.Name} uses it";
            }

            return null;
        }

        /// <summary>R4・R5：足す外部キーの名前と、参照する列の集合が親の候補キーか</summary>
        private string? CheckForeignKeyAdd(Step step)
        {
            var item = step.Item;
            var parent = Lower(item.ParentEntity!.TableName);
            var parentColumns = item
                .ForeignKeyColumnPairs.Select(p => Lower(p.ParentColumn))
                .ToList();

            if (!_keys.Any(k => k.Table == parent && SameSet(k.Columns, parentColumns)))
            {
                return $"R5: {SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, Limit)} references {parent}({string.Join(",", parentColumns)}), which is not a key";
            }

            return CheckName(
                Lower(item.ChildEntity!.TableName),
                SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, Limit),
                isForeignKey: true
            );
        }

        /// <summary>R4：作る表の主キー・一意制約の名前</summary>
        private string? CheckTableAdd(Step step)
        {
            if (step.Item.Entity is not { } entity)
            {
                return null;
            }

            if (
                !IsMySql
                && entity.GetPrimaryKeyColumnsInOrder().Count > 0
                && CheckName(step.Table, PrimaryKeyName(entity.TableName), false) is { } pk
            )
            {
                return pk;
            }

            return UniqueConstraintNaming
                .ResolveAll(entity, Limit)
                .Select(u => CheckName(step.Table, u.Name, false))
                .FirstOrDefault(r => r is not null);
        }

        /// <summary>R4：名前の範囲で重ならないか</summary>
        private string? CheckName(string table, string name, bool isForeignKey)
        {
            var comparison = dialect is DialectNames.SqlServer or DialectNames.MySql
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            IEnumerable<(string Table, string Name)> taken = dialect switch
            {
                DialectNames.SqlServer or DialectNames.Oracle => AllNames(),
                DialectNames.PostgreSql => isForeignKey
                    ? AllNames().Where(n => n.Table == table)
                    : _keys
                        .Select(k => (k.Table, k.Name))
                        .Concat(
                            _foreignKeys.Where(f => f.Child == table).Select(f => (f.Child, f.Name))
                        ),
                DialectNames.MySql => isForeignKey
                    ? _foreignKeys.Select(f => (f.Child, f.Name))
                    : _keys
                        .Where(k => !k.Primary && k.Table == table)
                        .Select(k => (k.Table, k.Name)),
                _ => [],
            };

            return taken.Any(n => string.Equals(n.Name, name, comparison))
                ? $"R4: the name {name} is already used"
                : null;
        }

        private IEnumerable<(string Table, string Name)> AllNames() =>
            _keys
                .Select(k => (k.Table, k.Name))
                .Concat(_foreignKeys.Select(f => (f.Child, f.Name)));

        // ---- 状態の更新 ----

        private void AddTable(Entity entity)
        {
            var table = Lower(entity.TableName);

            foreach (var column in entity.Columns)
            {
                _types[ColumnKey(table, column.Name)] = column.DataType;
            }

            var key = entity.GetPrimaryKeyColumnsInOrder();

            if (key.Count > 0)
            {
                _keys.Add(
                    new Key(
                        table,
                        [.. key.Select(c => Lower(c.Name))],
                        PrimaryKeyName(entity.TableName),
                        true
                    )
                );
            }

            foreach (var unique in UniqueConstraintNaming.ResolveAll(entity, Limit))
            {
                _keys.Add(
                    new Key(table, [.. unique.ColumnNames.Select(Lower)], unique.Name, false)
                );
            }
        }

        private void AddForeignKey(ForeignKey fk)
        {
            _foreignKeys.Add(fk);

            // MySQL は、子の列を先頭に持つ索引が無ければ外部キーの名前で索引を作る
            if (
                IsMySql
                && !_keys.Any(k => k.Table == fk.Child && IsPrefix(fk.ChildColumns, k.Columns))
                && !_foreignKeyIndexes.Any(i =>
                    i.Table == fk.Child && IsPrefix(fk.ChildColumns, i.Columns)
                )
            )
            {
                _foreignKeyIndexes.Add((fk.Child, fk.ChildColumns));
            }
        }

        private void RemoveForeignKey(Step step)
        {
            var item = step.Item;
            var child = Lower(item.ChildEntity!.TableName);
            var parent = Lower(item.ParentEntity!.TableName);
            var childColumns = item
                .ForeignKeyColumnPairs.Select(p => Lower(p.ChildColumn))
                .ToList();
            var candidates = _foreignKeys
                .Where(f =>
                    f.Child == child
                    && f.Parent == parent
                    && f.ChildColumns.SequenceEqual(childColumns)
                )
                .ToList();
            var named = item.ForeignKeyName is { } name
                ? candidates.FirstOrDefault(f =>
                    string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)
                )
                : null;

            if ((named ?? candidates.FirstOrDefault()) is { } removed)
            {
                _foreignKeys.Remove(removed);
            }
        }

        private Key? FindUnique(string table, string? name, IReadOnlyList<string> columns)
        {
            var candidates = _keys.Where(k => !k.Primary && k.Table == table).ToList();

            return (
                    name is null
                        ? null
                        : candidates.FirstOrDefault(k =>
                            string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase)
                        )
                )
                ?? candidates.FirstOrDefault(k =>
                    SameSet(k.Columns, columns.Select(Lower).ToList())
                );
        }

        private string UniqueName(Step step) =>
            UniqueConstraintNaming.Resolve(
                step.Item.UniqueConstraintName,
                step.Item.TableName,
                step.Item.UniqueConstraintColumns,
                Limit
            );

        private string PrimaryKeyName(string table) => ConstraintNames.PrimaryKey(table, Limit);

        private static string Describe(Key key) =>
            $"{(key.Primary ? "primary key" : "unique constraint")} {key.Name} of {key.Table}({string.Join(",", key.Columns)})";
    }

    private static bool SameSet(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b) =>
        a.Count == b.Count && a.ToHashSet(StringComparer.Ordinal).SetEquals(b);

    /// <summary><paramref name="columns"/> が <paramref name="index"/> の先頭に同じ並びで並ぶか</summary>
    private static bool IsPrefix(IReadOnlyList<string> columns, IReadOnlyList<string> index) =>
        columns.Count > 0
        && columns.Count <= index.Count
        && columns.Select((c, i) => index[i] == c).All(x => x);

    private static string ColumnKey(string table, string column) => $"{table}|{Lower(column)}";

    private static string Lower(string name) => name.Trim().ToLowerInvariant();
}
