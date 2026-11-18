using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.State;

/// <summary>
/// 計画の文の並びのある位置に在る制約（主キー・一意制約・外部キー）
/// </summary>
/// <remarks>
/// <para>
/// DB の今の図の制約（<see cref="From"/>）から始め、文を 1 つずつ当てて（<see cref="Apply"/>）次の位置の状態を作る。状態は作った後に
/// 変わらない（<see cref="Apply"/> は新しい状態を返す）。列・型・NULL 許容は持たない（位置で読む規則が無い。同期後の事実は
/// <see cref="PostSyncSchema"/> が持つ）。
/// </para>
/// <para>
/// 制約の識別は計画の照合と同じ型（<see cref="UniqueConstraintId"/>・<see cref="ForeignKeyId"/>）で持つ。外部キーの識別の名前は実効名
/// （DB の外部キーは <see cref="LiveSchema.ForeignKeyNames"/>、付ける文は <see cref="ForeignKeyStepNames.Added"/>）。
/// </para>
/// </remarks>
internal sealed class ConstraintState
{
    private readonly IdentifierLengthLimit _limit;

    private ConstraintState(
        IdentifierLengthLimit limit,
        IReadOnlyDictionary<string, IReadOnlyList<string>> primaryKeys,
        IReadOnlyList<UniqueConstraintEntry> uniqueConstraints,
        IReadOnlyList<ForeignKeyEntry> foreignKeys
    )
    {
        _limit = limit;
        PrimaryKeys = primaryKeys;
        UniqueConstraints = uniqueConstraints;
        ForeignKeys = foreignKeys;
    }

    /// <summary>主キー（保存する形の表名 → 主キーの列名を実効順に。表名は <see cref="SchemaNames.Comparer"/> で引く）</summary>
    /// <remarks>主キーの無い表は含まない。</remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> PrimaryKeys { get; }

    /// <summary>一意制約（DB の今の図の宣言の順、続いて付けた順）</summary>
    public IReadOnlyList<UniqueConstraintEntry> UniqueConstraints { get; }

    /// <summary>外部キー（DB の今の図のリレーションの順、続いて付けた順）</summary>
    public IReadOnlyList<ForeignKeyEntry> ForeignKeys { get; }

    /// <summary>DB の今の図の制約から状態を作る</summary>
    /// <param name="live">DB の今の図</param>
    /// <param name="capabilities">方言の宣言（外部キーの実効名の上限に使う）</param>
    /// <remarks>
    /// 外部キーは親子の表と列の組まで解決できたもの（<see cref="LiveSchema.ForeignKeys"/>）、一意制約は構成列を解決できたものだけを持つ。
    /// </remarks>
    public static ConstraintState From(LiveSchema live, SyncDialectCapabilities capabilities)
    {
        var limit = capabilities.IdentifierLengthLimit;
        var primaryKeys = new Dictionary<string, IReadOnlyList<string>>(SchemaNames.Comparer);
        var uniques = new List<UniqueConstraintEntry>();

        foreach (var entity in live.Entities)
        {
            var table = SchemaDiffService.NormalizeTable(entity);
            var keyColumns = entity.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList();

            if (keyColumns.Count > 0)
            {
                primaryKeys.TryAdd(table, keyColumns);
            }

            foreach (var constraint in entity.UniqueConstraints)
            {
                if (
                    UniqueConstraintNaming.TryResolveColumnNames(
                        entity,
                        constraint,
                        out var columns
                    )
                )
                {
                    uniques.Add(UniqueConstraintEntry.Of(table, constraint.Name, columns));
                }
            }
        }

        var names = live.ForeignKeyNames(limit);
        var foreignKeys = new List<ForeignKeyEntry>();

        foreach (var fk in live.ForeignKeys)
        {
            var child = SchemaDiffService.NormalizeTable(fk.Child);
            var parent = SchemaDiffService.NormalizeTable(fk.Parent);
            var effectiveName = names.TryGetValue(fk.Relationship, out var resolved)
                ? resolved
                : ConstraintNames.EffectiveForeignKey(
                    fk.Relationship.ConstraintName,
                    child,
                    parent,
                    limit
                );
            foreignKeys.Add(
                new ForeignKeyEntry(
                    ForeignKeyId.Of(
                        child,
                        parent,
                        fk.ColumnPairs,
                        effectiveName,
                        fk.Relationship.OnDelete,
                        fk.Relationship.OnUpdate
                    ),
                    child,
                    parent,
                    fk.ColumnPairs,
                    fk.Relationship.ConstraintName,
                    fk.Relationship.OnDelete,
                    fk.Relationship.OnUpdate,
                    fk
                )
            );
        }

        return new ConstraintState(limit, primaryKeys, uniques, foreignKeys);
    }

    /// <summary>文を 1 つ当てた次の位置の状態を返す（この状態は変えない）</summary>
    /// <param name="step">当てる文</param>
    /// <returns>次の状態。制約を変えない文はこの状態そのもの</returns>
    /// <remarks>
    /// <para>当てる文と状態の変化：</para>
    /// <list type="bullet">
    /// <item>主キーを外す文：その表の主キーを除く。付ける文：その表の主キーを文の列にする。</item>
    /// <item>一意制約を外す文：識別（表・DB の名前・列集合）が同じ一意制約を 1 つ除く。付ける文：一意制約を足す。</item>
    /// <item>
    /// 外部キーを外す文：外す外部キー（<see cref="FindDropped"/>）を 1 つ除く。付ける文：外部キーを足す。
    /// </item>
    /// <item>表を作る文：その表の主キーと一意制約を足す。表を消す文：その表の主キーと一意制約を除く。</item>
    /// </list>
    /// <para>
    /// ほかの文（列の追加・変更・削除・説明・列を動かす文・表の作り直し）は制約を変えない。同じ計画で端・構成列が消える制約は、組み立てが
    /// 先に外す文にしている。
    /// </para>
    /// </remarks>
    public ConstraintState Apply(SyncStep step) =>
        step switch
        {
            DropPrimaryKeyStep drop => WithPrimaryKey(drop.TableName, null),
            AddPrimaryKeyStep add => WithPrimaryKey(add.TableName, add.Columns),
            DropUniqueConstraintStep drop => WithoutUnique(
                UniqueConstraintId.Of(
                    SchemaNames.ToStoredForm(drop.TableName),
                    drop.Name,
                    drop.Columns
                )
            ),
            AddUniqueConstraintStep add => new ConstraintState(
                _limit,
                PrimaryKeys,
                [
                    .. UniqueConstraints,
                    UniqueConstraintEntry.Of(
                        SchemaNames.ToStoredForm(add.TableName),
                        add.Name,
                        add.Columns
                    ),
                ],
                ForeignKeys
            ),
            DropForeignKeyStep drop => WithoutForeignKey(drop),
            AddForeignKeyStep add => new ConstraintState(
                _limit,
                PrimaryKeys,
                UniqueConstraints,
                [.. ForeignKeys, ForeignKeyEntry.Of(add, _limit)]
            ),
            CreateTableStep create => WithCreatedTable(create),
            DropTableStep drop => WithoutTable(drop.TableName),
            _ => this,
        };

    /// <summary>外す外部キーの文が外す、この状態の外部キー（無ければ <c>null</c>）</summary>
    /// <param name="drop">外部キーを外す文</param>
    /// <remarks>
    /// 同期スクリプトが外す外部キーと同じ選び方：文が DB の名前を持てば、子の表が同じで実効名がその名前の外部キー。持たなければ、子の表・
    /// 親の表・列の組の集合・参照アクションが同じ外部キーのうち、DB の名前の無い外部キー、それも無ければ最初の外部キー。
    /// </remarks>
    public ForeignKeyEntry? FindDropped(DropForeignKeyStep drop)
    {
        var child = SchemaNames.ToStoredForm(drop.ChildTable);

        // 名前のある文は、同期スクリプトがその名前で外す（子の表の、実効名が同じ外部キー）
        if (drop.Name is not null)
        {
            var name = SchemaNames.Key(drop.Name);
            return ForeignKeys.FirstOrDefault(fk =>
                SchemaNames.AreSame(fk.ChildTable, child) && fk.Id.Name == name
            );
        }

        // 名前の無い文は、同期スクリプトが DB から引く（親の表・列の組の集合・参照アクションが同じ外部キー。DB の名前の無いものを先に）
        var parent = SchemaNames.ToStoredForm(drop.ParentTable);
        var pairs = PairSet(child, parent, drop.ColumnPairs);
        var candidates = ForeignKeys
            .Where(fk =>
                SchemaNames.AreSame(fk.ChildTable, child)
                && SchemaNames.AreSame(fk.ParentTable, parent)
                && fk.OnDelete == drop.OnDelete
                && fk.OnUpdate == drop.OnUpdate
                && PairSet(fk.ChildTable, fk.ParentTable, fk.ColumnPairs).SetEquals(pairs)
            )
            .ToList();

        return candidates.FirstOrDefault(fk => fk.Name is null) ?? candidates.FirstOrDefault();
    }

    private ConstraintState WithPrimaryKey(string tableName, IReadOnlyList<string>? columns)
    {
        var primaryKeys = new Dictionary<string, IReadOnlyList<string>>(
            PrimaryKeys,
            SchemaNames.Comparer
        );
        var table = SchemaNames.ToStoredForm(tableName);
        primaryKeys.Remove(table);

        if (columns is not null && columns.Count > 0)
        {
            primaryKeys[table] = [.. columns];
        }

        return new ConstraintState(_limit, primaryKeys, UniqueConstraints, ForeignKeys);
    }

    private ConstraintState WithoutUnique(UniqueConstraintId id)
    {
        var index = -1;

        for (var i = 0; i < UniqueConstraints.Count; i++)
        {
            if (UniqueConstraints[i].Id == id)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return this;
        }

        var uniques = UniqueConstraints.ToList();
        uniques.RemoveAt(index);
        return new ConstraintState(_limit, PrimaryKeys, uniques, ForeignKeys);
    }

    private ConstraintState WithoutForeignKey(DropForeignKeyStep drop)
    {
        var dropped = FindDropped(drop);

        if (dropped is null)
        {
            return this;
        }

        var foreignKeys = ForeignKeys.Where(fk => !ReferenceEquals(fk, dropped)).ToList();
        return new ConstraintState(_limit, PrimaryKeys, UniqueConstraints, foreignKeys);
    }

    private ConstraintState WithCreatedTable(CreateTableStep create)
    {
        var table = SchemaNames.ToStoredForm(create.TableName);
        var primaryKeys = new Dictionary<string, IReadOnlyList<string>>(
            PrimaryKeys,
            SchemaNames.Comparer
        );
        var keyColumns = create
            .Definition.GetPrimaryKeyColumnsInOrder()
            .Select(c => c.Name)
            .ToList();
        primaryKeys.Remove(table);

        if (keyColumns.Count > 0)
        {
            primaryKeys[table] = keyColumns;
        }

        var uniques = UniqueConstraints.ToList();

        foreach (var constraint in create.Definition.UniqueConstraints)
        {
            if (
                UniqueConstraintNaming.TryResolveColumnNames(
                    create.Definition,
                    constraint,
                    out var columns
                )
            )
            {
                var name =
                    create.UniqueConstraintNames is not null
                    && create.UniqueConstraintNames.TryGetValue(constraint, out var resolved)
                        ? resolved
                        : constraint.Name;
                uniques.Add(UniqueConstraintEntry.Of(table, name, columns));
            }
        }

        return new ConstraintState(_limit, primaryKeys, uniques, ForeignKeys);
    }

    private ConstraintState WithoutTable(string tableName)
    {
        var table = SchemaNames.ToStoredForm(tableName);
        var primaryKeys = new Dictionary<string, IReadOnlyList<string>>(
            PrimaryKeys,
            SchemaNames.Comparer
        );
        primaryKeys.Remove(table);
        var uniques = UniqueConstraints.Where(u => !SchemaNames.AreSame(u.Table, table)).ToList();
        return new ConstraintState(_limit, primaryKeys, uniques, ForeignKeys);
    }

    /// <summary>外部キーの列の組を、組の識別の集合にする（並びは見ない）</summary>
    private static HashSet<ForeignKeyPairId> PairSet(
        string child,
        string parent,
        IEnumerable<ForeignKeyColumnNamePair> pairs
    ) => [.. pairs.Select(p => ForeignKeyPairId.Of(child, p.ChildColumn, parent, p.ParentColumn))];
}

/// <summary>位置の状態に在る一意制約</summary>
/// <param name="Id">識別（表・DB の名前・列集合）</param>
/// <param name="Table">保存する形の表名</param>
/// <param name="Name">DB の名前（無ければ <c>null</c>）</param>
/// <param name="Columns">構成列（宣言の順）</param>
internal sealed record UniqueConstraintEntry(
    UniqueConstraintId Id,
    string Table,
    string? Name,
    IReadOnlyList<string> Columns
)
{
    /// <summary>表・名前・構成列から一意制約を作る</summary>
    /// <param name="table">保存する形の表名</param>
    /// <param name="name">DB の名前（無ければ <c>null</c>）</param>
    /// <param name="columns">構成列（宣言の順）</param>
    public static UniqueConstraintEntry Of(
        string table,
        string? name,
        IReadOnlyList<string> columns
    ) => new(UniqueConstraintId.Of(table, name, columns), table, name, [.. columns]);
}

/// <summary>位置の状態に在る外部キー</summary>
/// <param name="Id">識別（子の表・親の表・列の組の並び・実効名・参照アクション）</param>
/// <param name="ChildTable">保存する形の子の表名</param>
/// <param name="ParentTable">保存する形の親の表名</param>
/// <param name="ColumnPairs">列の組（DB の外部キーは宣言の順、付けた外部キーは文の順）</param>
/// <param name="Name">DB の名前か、付ける文の名前（無ければ <c>null</c>）</param>
/// <param name="OnDelete">親の行の削除のときの参照アクション</param>
/// <param name="OnUpdate">親のキーの更新のときの参照アクション</param>
/// <param name="Live">DB の今の図から来た外部キーなら、その解決済みの外部キー（付けた外部キーは <c>null</c>）</param>
internal sealed record ForeignKeyEntry(
    ForeignKeyId Id,
    string ChildTable,
    string ParentTable,
    IReadOnlyList<ForeignKeyColumnNamePair> ColumnPairs,
    string? Name,
    ForeignKeyReferentialAction OnDelete,
    ForeignKeyReferentialAction OnUpdate,
    ResolvedForeignKeyRelationship? Live
)
{
    /// <summary>付ける文から外部キーを作る</summary>
    /// <param name="add">外部キーを付ける文</param>
    /// <param name="limit">既定名を縮める方言の上限</param>
    public static ForeignKeyEntry Of(AddForeignKeyStep add, IdentifierLengthLimit limit)
    {
        var child = SchemaNames.ToStoredForm(add.ChildTable);
        var parent = SchemaNames.ToStoredForm(add.ParentTable);
        return new(
            ForeignKeyId.Of(
                child,
                parent,
                add.ColumnPairs,
                ForeignKeyStepNames.Added(add, limit),
                add.OnDelete,
                add.OnUpdate
            ),
            child,
            parent,
            [.. add.ColumnPairs],
            add.Name,
            add.OnDelete,
            add.OnUpdate,
            null
        );
    }
}
