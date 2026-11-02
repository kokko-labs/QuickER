using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 期待の行の図を組み立てる部品（DB 側の図と目標の図の両方に使う。型の表記は自由に書ける）
/// </summary>
/// <remarks>
/// 期待の行の主題ごとのファイル（<c>SyncPlanExpectationRows.*.cs</c>）は、表・列・外部キー・一意制約をこの部品だけで組み、
/// 主題ごとに組み立ての写しを持たない。表・列は名前で引く（大文字小文字を無視）。
/// </remarks>
internal sealed class ExpectationSchema
{
    /// <summary>表</summary>
    public List<Entity> Entities { get; } = [];

    /// <summary>外部キー（リレーション）</summary>
    public List<Relationship> Relationships { get; } = [];

    /// <summary>名前で表を引く</summary>
    public Entity this[string table] =>
        Entities.Single(e => e.TableName.Equals(table, StringComparison.OrdinalIgnoreCase));

    /// <summary>名前で列を引く</summary>
    public Column Col(string table, string column) =>
        this[table].Columns.Single(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// (列名, 型, 主キー) の組で表を足す（主キーの列は NOT NULL・ほかは NULL 許容。主キーの順序は宣言しない）
    /// </summary>
    public Entity Tbl(string name, params (string Name, string Type, bool Pk)[] columns)
    {
        var entity = new Entity { TableName = name };

        foreach (var (column, type, pk) in columns)
        {
            entity.Columns.Add(
                new Column
                {
                    Name = column,
                    DataType = type,
                    IsPrimaryKey = pk,
                    IsNullable = !pk,
                }
            );
        }

        Entities.Add(entity);
        return entity;
    }

    /// <summary>列を渡して表を足す</summary>
    public Entity Table(string name, params Column[] columns)
    {
        var entity = new Entity { TableName = name };
        entity.Columns.AddRange(columns);
        Entities.Add(entity);
        return entity;
    }

    /// <summary>表を足す（作ったものをそのまま使う）</summary>
    public Entity Add(Entity entity)
    {
        Entities.Add(entity);
        return entity;
    }

    /// <summary>列を渡して、どの図にも足さない表を作る（手で組む項目の目標の表）</summary>
    public static Entity Detached(string name, params Column[] columns)
    {
        var entity = new Entity { TableName = name };
        entity.Columns.AddRange(columns);
        return entity;
    }

    /// <summary>NULL 許容の INT の列だけの表を、どの図にも足さずに作る（列名の並びのまま）</summary>
    public static Entity DetachedInts(string name, params string[] columns) =>
        Detached(name, [.. columns.Select(c => Nullable(c, "INT"))]);

    /// <summary>NULL 許容の通常の列</summary>
    public static Column Nullable(string name, string type) =>
        new()
        {
            Name = name,
            DataType = type,
            IsNullable = true,
        };

    /// <summary>NOT NULL の通常の列</summary>
    public static Column NotNull(string name, string type) =>
        new()
        {
            Name = name,
            DataType = type,
            IsNullable = false,
        };

    /// <summary>主キーの列（NOT NULL）</summary>
    public static Column Pk(string name, string type) =>
        new()
        {
            Name = name,
            DataType = type,
            IsPrimaryKey = true,
            IsNullable = false,
        };

    /// <summary>主キーの順序を宣言する（列名の並び）</summary>
    public void PrimaryKeyOrder(string table, params string[] columns) =>
        this[table].PrimaryKeyColumnIds = [.. columns.Select(c => Col(table, c).Id)];

    /// <summary>一意制約を足す（名前が null なら名前なし）</summary>
    public UniqueConstraint Unique(string table, string? name, params string[] columns)
    {
        var unique = new UniqueConstraint
        {
            Name = name,
            ColumnIds = [.. columns.Select(c => Col(table, c).Id)],
        };
        this[table].UniqueConstraints.Add(unique);
        return unique;
    }

    /// <summary>外部キーを足す（列ペアは「親の列, 子の列」の並び）</summary>
    public Relationship Fk(
        string parent,
        string child,
        string? name,
        params (string Parent, string Child)[] pairs
    ) => Fk(parent, child, name, ForeignKeyReferentialAction.NoAction, pairs);

    /// <summary>参照アクションつきで外部キーを足す</summary>
    public Relationship Fk(
        string parent,
        string child,
        string? name,
        ForeignKeyReferentialAction onDelete,
        params (string Parent, string Child)[] pairs
    ) => Fk(parent, child, name, onDelete, ForeignKeyReferentialAction.NoAction, pairs);

    /// <summary>参照アクション 2 つつきで外部キーを足す</summary>
    public Relationship Fk(
        string parent,
        string child,
        string? name,
        ForeignKeyReferentialAction onDelete,
        ForeignKeyReferentialAction onUpdate,
        params (string Parent, string Child)[] pairs
    )
    {
        var relationship = new Relationship
        {
            SourceEntityId = this[parent].Id,
            TargetEntityId = this[child].Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs =
            [
                .. pairs.Select(p => new RelationshipColumnPair(
                    Col(parent, p.Parent).Id,
                    Col(child, p.Child).Id
                )),
            ],
            ConstraintName = name,
            OnDelete = onDelete,
            OnUpdate = onUpdate,
        };
        Relationships.Add(relationship);
        return relationship;
    }

    /// <summary>
    /// 別の図の複製を自分の中身にする（<paramref name="preserveIds"/> が偽なら Id を新しくし、外部キーの参照を張り替える）
    /// </summary>
    public void CopyFrom(ExpectationSchema other, bool preserveIds)
    {
        var entityMap = new Dictionary<Guid, Guid>();
        var columnMap = new Dictionary<Guid, Guid>();

        foreach (var entity in other.Entities)
        {
            var clone = entity.Clone(preserveIds);
            entityMap[entity.Id] = clone.Id;

            for (var i = 0; i < entity.Columns.Count; i++)
            {
                columnMap[entity.Columns[i].Id] = clone.Columns[i].Id;
            }

            Entities.Add(clone);
        }

        foreach (var relationship in other.Relationships)
        {
            Relationships.Add(
                new Relationship
                {
                    Id = preserveIds ? relationship.Id : Guid.NewGuid(),
                    SourceEntityId = entityMap[relationship.SourceEntityId],
                    TargetEntityId = entityMap[relationship.TargetEntityId],
                    Type = relationship.Type,
                    ColumnPairs = [.. relationship.ColumnPairs.Select(p => p.Clone(columnMap))],
                    ConstraintName = relationship.ConstraintName,
                    OnDelete = relationship.OnDelete,
                    OnUpdate = relationship.OnUpdate,
                }
            );
        }
    }
}

/// <summary>期待の行 1 回分の世界（能力・DB 側の図・目標の図）</summary>
internal sealed class ExpectationWorld(string capability, SyncDialectCapabilities capabilities)
{
    /// <summary>能力の鍵</summary>
    public string Capability { get; } = capability;

    /// <summary>能力の宣言</summary>
    public SyncDialectCapabilities Capabilities { get; } = capabilities;

    /// <summary>DB 側の図</summary>
    public ExpectationSchema Live { get; } = new();

    /// <summary>目標の図（差分の行だけが使う）</summary>
    public ExpectationSchema Target { get; } = new();

    /// <summary>DB 側の図だけを入れた計画の文脈</summary>
    public SyncPlanContext LiveContext() =>
        new() { LiveEntities = Live.Entities, LiveRelationships = Live.Relationships };
}

/// <summary>主題をまたいで使う、同じ形の図を作る関数</summary>
internal static class ExpectationScenarios
{
    /// <summary>
    /// 主キー列 id（int・NULL 許容の宣言のまま主キー）と NULL 許容の列 value（型は指定）の表を足す
    /// </summary>
    public static Entity IdValueTable(ExpectationSchema s, string name, string valueType)
    {
        var id = ExpectationSchema.Nullable("id", "int");
        id.IsPrimaryKey = true;
        return s.Table(name, id, ExpectationSchema.Nullable("value", valueType));
    }

    /// <summary>
    /// customer(id) ← orders(customer_id) の表と外部キー FK_orders_customer を足す（主キー id は INT・customer_id は NULL 許容の INT）
    /// </summary>
    public static Relationship CustomerOrders(ExpectationSchema s)
    {
        s.Tbl("customer", ("id", "INT", true));
        s.Tbl("orders", ("id", "INT", true), ("customer_id", "INT", false));
        return s.Fk("customer", "orders", "FK_orders_customer", ("id", "customer_id"));
    }

    /// <summary>
    /// 主キー 2 列（a・b・NOT NULL の INT）と NULL 許容の memo（TEXT）を持つ表 pair を、どの図にも足さずに作る
    /// （<paramref name="pkOrder"/> の列が主キーで、主キーの順序もその並び）
    /// </summary>
    public static Entity PairTable(string[] pkOrder)
    {
        var a = ExpectationSchema.NotNull("a", "INT");
        a.IsPrimaryKey = pkOrder.Contains("a");
        var b = ExpectationSchema.NotNull("b", "INT");
        b.IsPrimaryKey = pkOrder.Contains("b");
        var entity = ExpectationSchema.Detached(
            "pair",
            a,
            b,
            ExpectationSchema.Nullable("memo", "TEXT")
        );
        entity.PrimaryKeyColumnIds =
        [
            .. pkOrder.Select(name => entity.Columns.First(c => c.Name == name).Id),
        ];
        return entity;
    }
}

/// <summary>手で組む項目の部品（計画の段の行が <c>.Items(...)</c> の中で使う）</summary>
internal static class ExpectationItems
{
    /// <summary>種類・選択・表だけを持つ項目</summary>
    public static SchemaDiffItem Item(
        SchemaDiffKind kind,
        bool selected = true,
        string table = "T"
    ) =>
        new()
        {
            Kind = kind,
            TableName = table,
            IsSelected = selected,
        };

    /// <summary>主キーの変更の項目（<paramref name="target"/> が新しい主キーの構成の源）</summary>
    public static SchemaDiffItem AlterPk(string table, Entity target, bool selected = true) =>
        new()
        {
            Kind = SchemaDiffKind.AlterPrimaryKey,
            TableName = table,
            Entity = target,
            IsSelected = selected,
        };

    /// <summary>列順の変更の項目（<paramref name="target"/> が目標の列順）</summary>
    public static SchemaDiffItem Reorder(string table, Entity target, bool selected = true) =>
        new()
        {
            Kind = SchemaDiffKind.ReorderColumns,
            TableName = table,
            Entity = target,
            IsSelected = selected,
        };

    /// <summary>一意制約の追加・削除の項目</summary>
    public static SchemaDiffItem Unique(
        SchemaDiffKind kind,
        string table,
        string? name,
        string[] columns,
        bool selected = true
    ) =>
        new()
        {
            Kind = kind,
            TableName = table,
            UniqueConstraintName = name,
            UniqueConstraintColumns = columns,
            IsSelected = selected,
        };
}
