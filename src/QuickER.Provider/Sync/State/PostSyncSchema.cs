using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.Sync.TableRebuilds;

namespace QuickER.Provider.Sync.State;

/// <summary>同期計画の段が同期後の事実を求めるときの、項目の並びの時点（どの段まで項目を足した並びか）</summary>
/// <remarks>
/// 計算を変えない名札で、<see cref="PostSyncSchema"/> の求め方は時点に依らない。時点は、呼び出し元がどの並びから求めたかを書くために持つ。
/// </remarks>
internal enum PlanStage
{
    /// <summary>除外の周の途中の並びと、周が止まった後の並び（作り直しは未展開・暗黙の項目なし）</summary>
    ExclusionRound,

    /// <summary><see cref="ExclusionRound"/> の作り直しを削除と追加へ展開した並び</summary>
    ExclusionRoundExpanded,

    /// <summary>周が止まった後の並びを展開し、主キーの変更が足す NOT NULL 化を足した並び</summary>
    Expanded,

    /// <summary><see cref="Expanded"/> に、端が消える外部キーの暗黙の削除を足した並び</summary>
    WithVanishingForeignKeyDrops,

    /// <summary><see cref="WithVanishingForeignKeyDrops"/> に、構成列が消える一意制約の暗黙の削除を足した並び</summary>
    WithVanishingUniqueDrops,

    /// <summary>
    /// <see cref="WithVanishingUniqueDrops"/> に、主キーが置き換える一意制約の暗黙の削除（Oracle だけ）を足した並び
    /// （計画の組み立ての選んだ項目の最終形）
    /// </summary>
    Selected,
}

/// <summary>
/// 同期計画の段が読む同期後の事実（live の図に、ある時点の項目の並びを当てた結果）。材料は初めて読まれたときに求めて覚える
/// </summary>
/// <remarks>
/// <para>
/// 受けた項目の並びは、作るときに読み取り専用の写し（配列）にして持つ。作った後に元の並びへ項目を足しても、中身と求めた材料は変わらない。
/// </para>
/// <para>
/// 材料の求め方は時点（<see cref="Stage"/>）に依らない。同じ材料でも、時点が違えば項目が違うので答えが違いうる
/// （外す一意制約・同期後の一意制約・項目が外す外部キー）。
/// </para>
/// </remarks>
internal sealed class PostSyncSchema
{
    private readonly LiveSchema? _liveSchema;

    private readonly SyncDialectCapabilities _capabilities;

    private DroppedObjects? _dropped;

    private IReadOnlyDictionary<string, Entity>? _addedTables;

    private IReadOnlyDictionary<string, IReadOnlyList<string>>? _primaryKeyChanges;

    private IReadOnlySet<UniqueConstraintId>? _droppedUniqueConstraints;

    private IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>>? _uniqueKeys;

    private IReadOnlyDictionary<string, IReadOnlySet<string>>? _uniqueColumnSets;

    private Func<Entity, string, string, bool>? _allowsNull;

    private IReadOnlyDictionary<ColumnId, string>? _typeDecidingColumns;

    private IReadOnlySet<ForeignKeyId>? _droppedForeignKeys;

    private IReadOnlyList<(string Table, IReadOnlyList<string>? Columns)>? _changedTableColumnNames;

    private PostSyncSchema(
        PlanStage stage,
        SchemaDiffItem[] items,
        LiveSchema? liveSchema,
        SyncDialectCapabilities capabilities
    )
    {
        Stage = stage;
        Items = items;
        _liveSchema = liveSchema;
        _capabilities = capabilities;
    }

    /// <summary>ある時点の項目の並びから同期後の事実を作る</summary>
    /// <param name="stage">項目の並びの時点</param>
    /// <param name="items">項目の並び（読み取り専用の写しにして持つ）</param>
    /// <param name="liveSchema">DB の今の図（無ければ live に依る材料は空か図の値で答える）</param>
    /// <param name="capabilities">方言のケーパビリティ（型の比較と外部キーの実効名の上限に使う）</param>
    public static PostSyncSchema From(
        PlanStage stage,
        IEnumerable<SchemaDiffItem> items,
        LiveSchema? liveSchema,
        SyncDialectCapabilities capabilities
    ) => new(stage, [.. items], liveSchema, capabilities);

    /// <summary>項目の並びの時点</summary>
    public PlanStage Stage { get; }

    /// <summary>受けた項目の並び（作ったときの写し）</summary>
    public IReadOnlyList<SchemaDiffItem> Items { get; }

    /// <summary>項目が削除する表・列</summary>
    public DroppedObjects Dropped => _dropped ??= DroppedObjects.From(Items);

    /// <summary>追加する表（表名 → 図のエンティティ。同じ表の項目が 2 つあれば先の項目）</summary>
    public IReadOnlyDictionary<string, Entity> AddedTables =>
        _addedTables ??= CollectAddedTables(Items);

    /// <summary>主キーの変更（表名 → 新しい主キーの列名を実効順に。同じ表の項目が 2 つあれば後の項目）</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> PrimaryKeyChanges =>
        _primaryKeyChanges ??= CollectPrimaryKeyChanges(Items);

    /// <summary>項目が外す DB の一意制約の照合キー</summary>
    public IReadOnlySet<UniqueConstraintId> DroppedUniqueConstraints =>
        _droppedUniqueConstraints ??= ExplicitlyDroppedUniqueConstraints(Items);

    /// <summary>
    /// 同期後に在る一意制約の構成列（表名 → 構成列。live の宣言順、続いて追加する表の一意制約、一意制約の追加の項目の順）
    /// </summary>
    /// <remarks>live の図が無ければ空。表・列の削除の項目は見ない（消える表・列の一意制約は、その削除の項目が無い時点では残る）。</remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>> UniqueKeys =>
        _uniqueKeys ??= BuildPostSyncUniqueKeys(Items, _liveSchema);

    /// <summary>同期後に在る一意制約の列集合（表名 → <see cref="UniqueConstraintNaming.ColumnSetSignature"/> の集合）</summary>
    /// <remarks>合成は <see cref="UniqueKeys"/> と同じ。</remarks>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> UniqueColumnSets =>
        _uniqueColumnSets ??= BuildPostSyncUniqueConstraints(Items, _liveSchema);

    /// <summary>項目が外す外部キーの照合キー</summary>
    /// <remarks>
    /// 同期後に無い外部キーとは限らない（作り直しを展開した削除の外部キーは、追加の側で定義を変えて付け直す）。
    /// </remarks>
    public IReadOnlySet<ForeignKeyId> DroppedForeignKeys =>
        _droppedForeignKeys ??= CollectExplicitlyDroppedForeignKeys(
            Items,
            _capabilities.IdentifierLengthLimit
        );

    /// <summary>この同期で型が決まる列（列の照合キー → 同期後の型）</summary>
    /// <remarks>live の図が要る（無ければ <see cref="InvalidOperationException"/>）。</remarks>
    public IReadOnlyDictionary<ColumnId, string> TypeDecidingColumns =>
        _typeDecidingColumns ??= CollectTypeDecidingColumns(Items, LiveTables, _capabilities);

    /// <summary>
    /// 項目で列の名前の集合が変わる live の表と、その同期後の列名（表の削除は <c>null</c>）。項目に最初に現れた表の順
    /// </summary>
    /// <remarks>live の図が要る（無ければ <see cref="InvalidOperationException"/>）。</remarks>
    public IReadOnlyList<(string Table, IReadOnlyList<string>? Columns)> ChangedTableColumnNames =>
        _changedTableColumnNames ??= PostSyncColumnNames(Items, LiveTables);

    private NameLookup<Entity> LiveTables =>
        (
            _liveSchema
            ?? throw new InvalidOperationException(
                "The post-sync schema needs the live schema for this fact."
            )
        ).Tables;

    /// <summary>同期後の表の主キー（実効順）。主キーの変更の項目・追加する表・live の表の順に引く。どこにも無い表は <c>null</c></summary>
    /// <param name="table">表名</param>
    /// <remarks>live の図が要る（無ければ <see cref="InvalidOperationException"/>）。</remarks>
    public IReadOnlyList<string>? PrimaryKeyOf(string table) =>
        PostSyncPrimaryKeyOf(table, PrimaryKeyChanges, AddedTables, LiveTables);

    /// <summary>同期後に列が NULL を持てるか</summary>
    /// <param name="entity">列の表のエンティティ（live に無い列はこの値で見る）</param>
    /// <param name="table">正規化した表名</param>
    /// <param name="column">列名</param>
    /// <remarks>
    /// 主キーの変更で主キーに入る列は NOT NULL、列の変更があればその値、live に在る列は live の値、live に無い列は渡したエンティティ（図）の値。
    /// 主キーの列は NULL を許さない。
    /// </remarks>
    public bool AllowsNull(Entity entity, string table, string column) =>
        (_allowsNull ??= PostSyncNullability(Items, _liveSchema))(entity, table, column);

    /// <summary>
    /// 同期後の表の主キー（実効順）。主キーの変更を選んだ表はその新しい主キー、追加を選んだ表は図の主キー、
    /// それ以外は live の主キー。どこにも無い表は <c>null</c>。
    /// </summary>
    /// <typeparam name="TKey">主キーの変更の列名の並びの型</typeparam>
    /// <param name="table">表名</param>
    /// <param name="pkChangedTables">主キーの変更（表名 → 新しい主キーの列名）</param>
    /// <param name="addedTables">追加する表（表名 → 図のエンティティ）</param>
    /// <param name="liveByName">live の表の索引</param>
    internal static IReadOnlyList<string>? PostSyncPrimaryKeyOf<TKey>(
        string table,
        IReadOnlyDictionary<string, TKey> pkChangedTables,
        IReadOnlyDictionary<string, Entity> addedTables,
        NameLookup<Entity> liveByName
    )
        where TKey : IReadOnlyList<string>
    {
        if (pkChangedTables.TryGetValue(table, out var changedKey))
        {
            return changedKey;
        }

        if (addedTables.TryGetValue(table, out var addedTable))
        {
            return [.. addedTable.GetPrimaryKeyColumnsInOrder().Select(c => c.Name)];
        }

        return liveByName.TryGet(table, out var liveTable)
            ? [.. liveTable.GetPrimaryKeyColumnsInOrder().Select(c => c.Name)]
            : null;
    }

    /// <summary>外す項目（DropUniqueConstraint）が外す DB の一意制約の照合キー（<see cref="UniqueConstraintId"/>）</summary>
    /// <param name="selected">項目（一意制約の削除以外は読まない）</param>
    internal static HashSet<UniqueConstraintId> ExplicitlyDroppedUniqueConstraints(
        IEnumerable<SchemaDiffItem> selected
    ) =>
        selected
            .Where(i => i.Kind == SchemaDiffKind.DropUniqueConstraint)
            .Select(i =>
                UniqueConstraintId.Of(
                    i.TableName,
                    i.UniqueConstraintName,
                    i.UniqueConstraintColumns
                )
            )
            .ToHashSet();

    /// <summary>追加を選んだ表（表名 → 図のエンティティ）</summary>
    private static Dictionary<string, Entity> CollectAddedTables(
        IReadOnlyList<SchemaDiffItem> selected
    ) =>
        selected
            .Where(i => i.Kind == SchemaDiffKind.AddTable && i.Entity is not null)
            .GroupBy(i => SchemaNames.ToStoredForm(i.TableName), SchemaNames.Comparer)
            .ToDictionary(g => g.Key, g => g.First().Entity!, SchemaNames.Comparer);

    /// <summary>選択済みの主キー変更を「テーブル名 → 新しい主キー列名の集合」へまとめる</summary>
    private static Dictionary<string, IReadOnlyList<string>> CollectPrimaryKeyChanges(
        IReadOnlyList<SchemaDiffItem> selected
    )
    {
        var pkChangedTables = new Dictionary<string, IReadOnlyList<string>>(SchemaNames.Comparer);

        foreach (var item in selected.Where(i => i.Kind == SchemaDiffKind.AlterPrimaryKey))
        {
            // 新しい主キーの列を実効順で持つ（候補キーの証明は列の集合・外部キーの並べ替えは並びを使う）
            pkChangedTables[SchemaNames.ToStoredForm(item.TableName)] =
            [
                .. item.Entity?.GetPrimaryKeyColumnsInOrder().Select(c => c.Name) ?? [],
            ];
        }

        return pkChangedTables;
    }

    /// <summary>
    /// 選んだ項目で列の名前の集合が変わる live の表と、その同期後の列名（表の削除は <c>null</c>）を、項目に最初に現れた順に返す
    /// </summary>
    /// <remarks>
    /// 同期後の列名は「live の列 − 選んだ列の削除 ＋ 選んだ列の追加」（<see cref="SyncPlanner.ExcludeDropsLeavingNoColumns"/> と同じ数え方）。
    /// 列が 1 本も残らない表は数えない（その削除は <see cref="SyncPlanner.ExcludeDropsLeavingNoColumns"/> が外す）。live に無い表（追加する表）は
    /// ビューが読んでいないので数えない。表の名前は live の綴りで返す。
    /// </remarks>
    private static List<(string Table, IReadOnlyList<string>? Columns)> PostSyncColumnNames(
        IReadOnlyList<SchemaDiffItem> selected,
        NameLookup<Entity> liveByName
    )
    {
        var changes = new List<(string Table, IReadOnlyList<string>? Columns)>();

        foreach (
            var items in selected
                .Where(i => PlanFacts.IsColumnNameSetChange(i.Kind))
                .GroupBy(i => SchemaNames.ToStoredForm(i.TableName), SchemaNames.Comparer)
        )
        {
            if (!liveByName.TryGet(items.Key, out var live))
            {
                continue;
            }

            if (items.Any(i => i.Kind == SchemaDiffKind.DropTable))
            {
                changes.Add((live.TableName, null));
                continue;
            }

            var dropped = items
                .Where(i => i.Kind == SchemaDiffKind.DropColumn && i.ColumnName is not null)
                .Select(i => i.ColumnName!)
                .ToList();
            var columns = live
                .Columns.Select(c => c.Name)
                .Where(name => !dropped.Any(d => SchemaNames.AreSame(d, name)))
                .ToList();

            foreach (
                var added in items
                    .Where(i => i.Kind == SchemaDiffKind.AddColumn)
                    .Select(i => i.Column?.Name ?? i.ColumnName)
            )
            {
                if (added is not null && !columns.Any(c => SchemaNames.AreSame(c, added)))
                {
                    columns.Add(added);
                }
            }

            if (columns.Count > 0)
            {
                changes.Add((live.TableName, columns));
            }
        }

        return changes;
    }

    /// <summary>
    /// この同期で型が決まる列（列の照合キー → 同期後の型）を集める：列の変更の項目のうち、live に無い列か、live の型と
    /// <see cref="SyncDialectCapabilities.AreSameType"/> で同じでない型へ変える列
    /// </summary>
    /// <param name="items">項目（列の変更以外は読まない）</param>
    /// <param name="liveByName">live の表の索引</param>
    /// <param name="capabilities">型の比較に使う方言のケーパビリティ</param>
    /// <remarks>
    /// 外部キーの両端の型の照合（<see cref="PlanFacts.HasPostSyncTypeMismatch"/>・<see cref="SyncPlanner.ExcludeForeignKeysWithPostSyncTypeMismatch"/>）は、
    /// この関数が返す列だけを「この同期で型が決まる」とみなす。NULL 許容だけを変える列の変更は型を決めない：DB はその外部キーを
    /// 今の両端の型のまま受け入れているので、型を変えずに外して付け直せば同じ組の型で通る。数えると、DB が受け入れている型の違う
    /// 外部キー（PostgreSQL の integer → bigint など）を「付け直せない」と名指しして外し直さず、その外部キーに阻まれる主キーの
    /// 解除が必ず失敗した。
    /// </remarks>
    private static Dictionary<ColumnId, string> CollectTypeDecidingColumns(
        IEnumerable<SchemaDiffItem> items,
        NameLookup<Entity> liveByName,
        SyncDialectCapabilities capabilities
    )
    {
        var decided = new Dictionary<ColumnId, string>();

        foreach (
            var alter in items.Where(i =>
                i.Kind == SchemaDiffKind.AlterColumn && i.ColumnName is not null
            )
        )
        {
            var newType = alter.Column?.DataType ?? string.Empty;
            var table = SchemaNames.ToStoredForm(alter.TableName);

            if (
                liveByName.TryGet(table, out var live)
                && live.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, alter.ColumnName))
                    is { } liveColumn
                && capabilities.AreSameType(liveColumn.DataType, newType)
            )
            {
                continue;
            }

            decided[ColumnId.Of(alter.TableName, alter.ColumnName!)] = newType;
        }

        return decided;
    }

    /// <summary>
    /// 同期後の列の NULL 許容を引く関数を作る（引数は列の表のエンティティ・正規化した表名・列名）。
    /// </summary>
    /// <remarks>
    /// 選んだ主キーの変更で主キーに入る列は NOT NULL、選んだ列の変更があればその値、live に在る列は live の値、
    /// live に無い列は渡したエンティティ（図）の値。主キーの列は NULL を許さない。
    /// </remarks>
    private static Func<Entity, string, string, bool> PostSyncNullability(
        IReadOnlyList<SchemaDiffItem> selected,
        LiveSchema? liveSchema
    )
    {
        var altered = new Dictionary<ColumnId, bool>();
        var newKeyColumns = new HashSet<ColumnId>();

        foreach (var item in selected)
        {
            if (
                item.Kind == SchemaDiffKind.AlterColumn
                && item.ColumnName is not null
                && item.Column is not null
            )
            {
                altered[ColumnId.Of(item.TableName, item.ColumnName)] =
                    ColumnNullability.AllowsNull(item.Column);
            }
            else if (item.Kind == SchemaDiffKind.AlterPrimaryKey && item.Entity is not null)
            {
                foreach (var column in item.Entity.GetPrimaryKeyColumnsInOrder())
                {
                    newKeyColumns.Add(ColumnId.Of(item.TableName, column.Name));
                }
            }
        }

        var liveByName = liveSchema?.Tables;

        return (entity, table, column) =>
        {
            var key = ColumnId.Of(table, column);

            if (newKeyColumns.Contains(key))
            {
                return false;
            }

            if (altered.TryGetValue(key, out var nullable))
            {
                return nullable;
            }

            var source =
                liveByName is not null
                && liveByName.TryGet(table, out var live)
                && live.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, column))
                    is { } liveColumn
                    ? liveColumn
                    : entity.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, column));

            return source is null || ColumnNullability.AllowsNull(source);
        };
    }

    /// <summary>明示的に削除が選ばれた外部キー（選択済み DropForeignKey）の照合キーを集める</summary>
    private static HashSet<ForeignKeyId> CollectExplicitlyDroppedForeignKeys(
        IReadOnlyList<SchemaDiffItem> selected,
        IdentifierLengthLimit limit
    )
    {
        var explicitlyDropped = new HashSet<ForeignKeyId>();

        foreach (var dropFk in selected.Where(i => i.Kind == SchemaDiffKind.DropForeignKey))
        {
            var signature = TableRebuildForeignKeys.ResolveDroppedSignature(dropFk);

            if (signature is null || dropFk.ChildEntity is null)
            {
                continue;
            }

            var (parentTable, columnPairs) = signature.Value;
            explicitlyDropped.Add(
                PlanIdentity.ForeignKeyKey(
                    SchemaDiffService.NormalizeTable(dropFk.ChildEntity),
                    parentTable,
                    columnPairs,
                    dropFk.Relationship,
                    limit
                )
            );
        }

        return explicitlyDropped;
    }

    /// <summary>
    /// 「同期後に存在する一意制約」をテーブルごとに合成する
    /// （live の一意制約 − 選択済み <see cref="SchemaDiffKind.DropUniqueConstraint"/> ＋
    /// 選択済み <see cref="SchemaDiffKind.AddUniqueConstraint"/>）。
    /// </summary>
    /// <returns>テーブル名 → 列集合シグネチャ（<see cref="UniqueConstraintNaming.ColumnSetSignature"/>）の集合</returns>
    /// <remarks>
    /// 図（target）の定義を直接見ないのは、未選択の追加をあてにしてしまうため。合成は選択済み差分だけで行い、
    /// 「実行後に確実に存在する」ものだけを候補キーの証明材料にする。
    /// </remarks>
    private static Dictionary<string, IReadOnlySet<string>> BuildPostSyncUniqueConstraints(
        IReadOnlyList<SchemaDiffItem> selected,
        LiveSchema? liveSchema
    ) =>
        BuildPostSyncUniqueKeys(selected, liveSchema)
            .ToDictionary(
                pair => pair.Key,
                pair =>
                    (IReadOnlySet<string>)
                        pair
                            .Value.Select(UniqueConstraintNaming.ColumnSetSignature)
                            .ToHashSet(StringComparer.Ordinal),
                SchemaNames.Comparer
            );

    /// <summary>
    /// 「同期後に存在する一意制約」の構成列を、列の並びのままテーブルごとに合成する
    /// （<see cref="BuildPostSyncUniqueConstraints"/> と同じ合成。外部キーの列ペアを一意制約の並びへそろえるのに使う）。
    /// </summary>
    /// <returns>テーブル名 → 一意制約の構成列（live の宣言順、続いて追加を選んだ表の一意制約、選択済みの追加の順）</returns>
    /// <remarks>
    /// 削除は DB の一意制約の照合キー（<see cref="UniqueConstraintId"/>＝名前と列集合）で照合し、外す制約だけを落とす
    /// （同じ列集合の一意制約が 2 つある表で、1 つを外しても、もう 1 つは同期後のキーとして残る）。
    /// </remarks>
    private static Dictionary<string, IReadOnlyList<IReadOnlyList<string>>> BuildPostSyncUniqueKeys(
        IReadOnlyList<SchemaDiffItem> selected,
        LiveSchema? liveSchema
    )
    {
        // 構成列と、DB の一意制約なら照合キー（外す項目と照合する。図から来た制約は null）
        var byTable = new Dictionary<
            string,
            List<(IReadOnlyList<string> Columns, UniqueConstraintId? LiveKey)>
        >(SchemaNames.Comparer);

        if (liveSchema is null)
        {
            return [];
        }

        // live の一意制約を土台にする
        foreach (var entity in liveSchema.Entities)
        {
            var table = SchemaDiffService.NormalizeTable(entity);

            foreach (var constraint in entity.UniqueConstraints)
            {
                if (
                    !UniqueConstraintNaming.TryResolveColumnNames(
                        entity,
                        constraint,
                        out var columns
                    )
                )
                {
                    continue;
                }

                Keys(table).Add((columns, UniqueConstraintId.Of(table, constraint.Name, columns)));
            }
        }

        // 追加を選んだ表の一意制約を足す（表の作成と一緒に作られる）
        foreach (
            var item in selected.Where(i =>
                i.Kind == SchemaDiffKind.AddTable && i.Entity is not null
            )
        )
        {
            foreach (var constraint in item.Entity!.UniqueConstraints)
            {
                if (
                    UniqueConstraintNaming.TryResolveColumnNames(
                        item.Entity,
                        constraint,
                        out var columns
                    )
                )
                {
                    Keys(SchemaNames.ToStoredForm(item.TableName)).Add((columns, null));
                }
            }
        }

        // 選択済みの削除を落とす（外す DB の一意制約だけ）
        var dropped = ExplicitlyDroppedUniqueConstraints(selected);

        foreach (var keys in byTable.Values)
        {
            keys.RemoveAll(key => key.LiveKey is not null && dropped.Contains(key.LiveKey));
        }

        // 選択済みの追加を足す
        foreach (var item in selected.Where(i => i.Kind == SchemaDiffKind.AddUniqueConstraint))
        {
            if (item.UniqueConstraintColumns.Count == 0)
            {
                continue;
            }

            Keys(SchemaNames.ToStoredForm(item.TableName))
                .Add((item.UniqueConstraintColumns, null));
        }

        return byTable.ToDictionary(
            pair => pair.Key,
            pair =>
                (IReadOnlyList<IReadOnlyList<string>>)
                    pair.Value.Select(key => key.Columns).ToList(),
            SchemaNames.Comparer
        );

        List<(IReadOnlyList<string> Columns, UniqueConstraintId? LiveKey)> Keys(string table)
        {
            if (!byTable.TryGetValue(table, out var keys))
            {
                keys = [];
                byTable[table] = keys;
            }

            return keys;
        }
    }

    /// <summary>選んだ表・列の削除（制約の端が同じ計画で消えるかの判定に使う）</summary>
    internal sealed class DroppedObjects
    {
        private readonly HashSet<string> _tables = new(SchemaNames.Comparer);

        private readonly HashSet<ColumnId> _columns = [];

        /// <summary>削除する表</summary>
        public IReadOnlySet<string> Tables => _tables;

        /// <summary>削除する列（<see cref="ColumnId"/> の照合キー）</summary>
        public IReadOnlySet<ColumnId> Columns => _columns;

        /// <summary>表・列の削除が 1 つも無いか</summary>
        public bool IsEmpty => Tables.Count == 0 && Columns.Count == 0;

        /// <summary>選んだ項目から削除する表・列を集める</summary>
        public static DroppedObjects From(IEnumerable<SchemaDiffItem> selected)
        {
            var dropped = new DroppedObjects();

            foreach (var item in selected)
            {
                if (item.Kind == SchemaDiffKind.DropTable)
                {
                    dropped._tables.Add(SchemaNames.ToStoredForm(item.TableName));
                }
                else if (item.Kind == SchemaDiffKind.DropColumn && item.ColumnName is not null)
                {
                    dropped._columns.Add(ColumnId.Of(item.TableName, item.ColumnName));
                }
            }

            return dropped;
        }

        /// <summary>外部キーの子表・親表・構成列のどれかが消えるか</summary>
        public bool Touches(
            ResolvedForeignKeyRelationship fk,
            string childTable,
            string parentTable
        ) =>
            Tables.Contains(childTable)
            || Tables.Contains(parentTable)
            || fk.ColumnPairs.Any(p =>
                Columns.Contains(ColumnId.Of(childTable, p.ChildColumn))
                || Columns.Contains(ColumnId.Of(parentTable, p.ParentColumn))
            );

        /// <summary>表の列のどれかが消えるか</summary>
        public bool TouchesColumns(string table, IEnumerable<string> columns) =>
            columns.Any(c => Columns.Contains(ColumnId.Of(table, c)));
    }
}
