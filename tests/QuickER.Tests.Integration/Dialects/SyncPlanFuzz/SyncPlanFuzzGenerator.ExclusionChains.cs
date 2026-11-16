using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>除外の連鎖の形で、起きる選び方の 1 項目（種類・表・名前）</summary>
/// <param name="Kind">差分の種類</param>
/// <param name="Table">表（外部キーは子の表）</param>
/// <param name="Name">列・外部キー・一意制約の名前（null なら名前を問わない）</param>
internal sealed record FuzzChainItem(SchemaDiffKind Kind, string Table, string? Name = null);

/// <summary>
/// 除外の連鎖の形（<see cref="FuzzLiveSchema.ExclusionChains"/>）。<see cref="Selected"/> の項目を全部選び、<see cref="Unselected"/> の項目を
/// どれも選ばなかったとき、形 <see cref="Shape"/> が起きる
/// </summary>
/// <param name="Shape">起きた形の名前（<see cref="FuzzShape"/>）</param>
/// <param name="Selected">選ぶ項目</param>
/// <param name="Unselected">選ばない項目</param>
internal sealed record FuzzExclusionChain(
    string Shape,
    IReadOnlyList<FuzzChainItem> Selected,
    IReadOnlyList<FuzzChainItem> Unselected
);

/// <summary>
/// 同期計画の総当たりの生成器の、除外の連鎖の水準（乱数の列 <c>ExclusionChainSeed</c>）
/// </summary>
/// <remarks>
/// <para>
/// ある除外が外した項目を、別の除外の判定が前提にしていた形を作る（製品は除外を何も外れなくなるまで繰り返す周
/// <c>SyncPlanner.ExcludeUntilStable</c> の中で呼ぶ＝<c>src/QuickER.Provider/CLAUDE.md</c>「除外の周」）。形は 3 つで、どれを組むかは
/// 方言の宣言で決める（方言名で分岐しない）。
/// </para>
/// <list type="bullet">
///   <item>列が残らない表と同じ名前の一意制約（一意制約の名前の範囲が表より広い方言＝<see cref="SyncDialectCapabilities.ConstraintNamesCollide"/>）：
///   主キーの無い 1 列の表の列を消し、その列の名前つきの一意制約と同じ名前の一意制約を別の表へ足す。</item>
///   <item>作り直さない表の主キーの変更とそれを前提にする外部キー（再構築の方言）：取込が捨てる物（欠けた表への外部キーの列）を持つ表の
///   主キーを移し、別の表から新しい主キーへ外部キーを足す。</item>
///   <item>型の食い違いで外れる作り直しと同じ定義の外部キー（同じ定義の外部キーを 2 本持てない方言＝
///   <see cref="SyncPlanFuzzDialect.AllowDuplicateForeignKeys"/> が偽）：行 0 の子の名前つきの外部キーの親を、型の種類を変える別の表へ
///   付け替え、古い定義と同じ外部キーを別の名前で足す（半々で SET NULL）。</item>
/// </list>
/// <para>
/// DB 側の部品は新しい表で組み（<see cref="BuildLive"/> の属性の形の水準の直後）、目標の図の変更は <see cref="Mutate"/> の属性の形の水準の
/// 直後に加える。本流の変更が部品を変えていたら、目標の図の変更は加えない。この列だけから引き、既存の 8 本の列も本流の乱数も引かない
/// （水準が出なかった種は足す前と同じケース）。連鎖が起きる選び方は <see cref="FuzzLiveSchema.ExclusionChains"/> に宣言し、総当たりと網が
/// 選んだ項目から起きた形を決める（<see cref="SyncPlanFuzzRunner.ExclusionChainShapes"/>）。
/// </para>
/// </remarks>
internal sealed partial class SyncPlanFuzzGenerator
{
    /// <summary>除外の連鎖の形</summary>
    private enum ExclusionChainForm
    {
        None,
        LastColumnUnique,
        UnrebuiltPrimaryKeyForeignKey,
        TypeMismatchForeignKey,
    }

    /// <summary>この種で DB 側の部品を組んだ形</summary>
    private ExclusionChainForm _exclusionChain;

    /// <summary>
    /// 部品の表（列が残らない表：その表・作り直さない表：主キーを移す表と外部キーを足す表・型の食い違い：子の表・古い親・新しい親）
    /// </summary>
    private readonly List<string> _chainTables = [];

    /// <summary>部品の制約の名前（列が残らない表：一意制約・型の食い違い：付け替える外部キー）</summary>
    private string? _chainName;

    /// <summary>型の食い違いの形で、足す外部キーを SET NULL にするか</summary>
    private bool _chainSetNull;

    /// <summary>この方言で組める形（方言の宣言で決める）</summary>
    private List<ExclusionChainForm> ExclusionChainForms()
    {
        var capabilities = dialect.Provider.SyncCapabilities;
        var forms = new List<ExclusionChainForm>();

        // 一意制約の名前が別の表の一意制約の名前とぶつかる方言（列が残らない表の列の削除が外れると、残る一意制約と名前が重なる）
        if (capabilities.ConstraintNamesCollide(false, "a", false, "b"))
        {
            forms.Add(ExclusionChainForm.LastColumnUnique);
        }

        // 再構築の方言（取込が捨てた物のある表は作り直さない）
        if (!capabilities.SupportsAlterColumn || !capabilities.SupportsForeignKeyAlter)
        {
            forms.Add(ExclusionChainForm.UnrebuiltPrimaryKeyForeignKey);
        }

        // 同じ定義の外部キーを 2 本持てない方言（作り直しが外れて残る古い外部キーと、同じ定義の追加が重なる）
        if (!dialect.AllowDuplicateForeignKeys)
        {
            forms.Add(ExclusionChainForm.TypeMismatchForeignKey);
        }

        return forms;
    }

    /// <summary>DB 側の図：この方言で組める形ごとに種の 8 分の 1 で、その形の DB 側の部品を組む（1 種に 1 つ・新しい表だけ）</summary>
    private void AddExclusionChainLive(FuzzLiveSchema live)
    {
        if (exclusionChainRng is null)
        {
            return;
        }

        var forms = ExclusionChainForms();

        if (forms.Count == 0)
        {
            return;
        }

        // 形ごとに種の 8 分の 1（組める形が 2 つの方言で、形ごとの種の数を半分にしない）
        var pick = exclusionChainRng.Next(8);

        if (pick >= forms.Count)
        {
            return;
        }

        _exclusionChain = forms[pick];

        switch (_exclusionChain)
        {
            case ExclusionChainForm.LastColumnUnique:
                BuildLastColumnUniqueLive(live);
                break;
            case ExclusionChainForm.UnrebuiltPrimaryKeyForeignKey:
                BuildUnrebuiltPrimaryKeyLive(live);
                break;
            case ExclusionChainForm.TypeMismatchForeignKey:
                BuildTypeMismatchLive(live);
                break;
        }
    }

    /// <summary>主キーの無い 1 列の表（NOT NULL の整数・行は 0〜2 で値は一意）と、その列の名前つきの一意制約</summary>
    private void BuildLastColumnUniqueLive(FuzzLiveSchema live)
    {
        var table = UnusedTableName(live.Diagram.Entities, "chain_left");
        var entity = new Entity { TableName = table };
        live.RowCounts[table] = exclusionChainRng!.Next(3);
        var column = AddColumn(live, entity, "u", FuzzTypeKind.Int, 0, false, r => Int(r));
        _chainName = UnusedConstraintName(
            "UQ_chain_left",
            live.Diagram.Entities,
            live.Diagram.Relationships
        );
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = _chainName, ColumnIds = [column.Id] }
        );
        live.Diagram.Entities.Add(entity);
        _chainTables.Add(table);
    }

    /// <summary>
    /// 表 P（整数の主キー id・NOT NULL で値が一意の整数の列 k）と、その表に生の文で足す欠けた表への外部キーの列（取込が捨てる物）、
    /// 外部キーを足す表 C（行 0・整数の列 1 つ）
    /// </summary>
    /// <remarks>
    /// 外部キーの列は DDL の図に無く、データを入れた後の生の文で足す（SQLite は欠けた表への外部キーを作れる）。取込は外部キーを捨てて
    /// <see cref="SchemaImportWarningKind.ForeignKeyOutsideScope"/> で告げる。表 P はほかに外部キーを持たないので、取込が付ける名前の番号は 0。
    /// 網は取込の代わりに、列を DB 側の図に足し（<see cref="FuzzLiveSchema.StatementColumns"/>）、同じ警告を計画の文脈へ渡す
    /// （<see cref="FuzzLiveSchema.ImportWarnings"/>）。
    /// </remarks>
    private void BuildUnrebuiltPrimaryKeyLive(FuzzLiveSchema live)
    {
        var parentTable = UnusedTableName(live.Diagram.Entities, "chain_p");
        var parent = new Entity { TableName = parentTable };
        var rows = exclusionChainRng!.Next(2) == 0 ? 0 : 2;
        live.RowCounts[parentTable] = rows;
        AddColumn(live, parent, "id", FuzzTypeKind.Int, 0, false, r => Int(r), pk: true);
        AddColumn(live, parent, "k", FuzzTypeKind.Int, 0, false, r => Int(100 + r));
        live.Diagram.Entities.Add(parent);

        var missing = UnusedTableName(live.Diagram.Entities, "chain_missing");
        var discarded = UnusedColumnName(parent, "zid");
        var info = new FuzzColumnInfo { Kind = FuzzTypeKind.Int };

        for (var r = 0; r < rows; r++)
        {
            info.Values.Add(null);
        }

        live.Columns[FuzzLiveSchema.Key(parentTable, discarded)] = info;
        live.StatementColumns.Add((parentTable, discarded));
        live.ComputedColumnStatements.Add(
            $"ALTER TABLE {dialect.QuoteTable(parentTable)} ADD COLUMN {dialect.Quote(discarded)} {dialect.TypeName(FuzzTypeKind.Int, 0)} NULL REFERENCES {dialect.Quote(missing)} ({dialect.Quote("id")});"
        );
        live.ImportWarnings.Add(
            new SchemaImportWarning(
                SchemaImportWarningKind.ForeignKeyOutsideScope,
                parentTable,
                $"FK_{parentTable}_{missing}_0",
                missing
            )
        );

        var childTable = UnusedTableName(live.Diagram.Entities, "chain_c");
        var child = new Entity { TableName = childTable };
        live.RowCounts[childTable] = 0;
        AddColumn(live, child, "id", FuzzTypeKind.Int, 0, false, r => Int(r), pk: true);
        AddColumn(live, child, "p_ref", FuzzTypeKind.Int, 0, true, _ => null);
        live.Diagram.Entities.Add(child);
        _chainTables.Add(parentTable);
        _chainTables.Add(childTable);
    }

    /// <summary>行 0 の表 3 つ：子 c（NOT NULL の整数の列 pid）・古い親・新しい親（どちらも整数の主キー id）と、c から古い親への名前つきの外部キー</summary>
    private void BuildTypeMismatchLive(FuzzLiveSchema live)
    {
        var oldTable = UnusedTableName(live.Diagram.Entities, "chain_p_old");
        var oldParent = new Entity { TableName = oldTable };
        live.RowCounts[oldTable] = 0;
        var oldKey = AddColumn(
            live,
            oldParent,
            "id",
            FuzzTypeKind.Int,
            0,
            false,
            r => Int(r),
            pk: true
        );
        live.Diagram.Entities.Add(oldParent);

        var newTable = UnusedTableName(live.Diagram.Entities, "chain_p_new");
        var newParent = new Entity { TableName = newTable };
        live.RowCounts[newTable] = 0;
        AddColumn(live, newParent, "id", FuzzTypeKind.Int, 0, false, r => Int(r), pk: true);
        live.Diagram.Entities.Add(newParent);

        var childTable = UnusedTableName(live.Diagram.Entities, "chain_c");
        var child = new Entity { TableName = childTable };
        live.RowCounts[childTable] = 0;
        AddColumn(live, child, "id", FuzzTypeKind.Int, 0, false, r => Int(r), pk: true);
        var pid = AddColumn(live, child, "pid", FuzzTypeKind.Int, 0, false, r => Int(r));
        pid.IsForeignKey = true;
        live.Diagram.Entities.Add(child);

        _chainName = UnusedConstraintName(
            "FK_chain_x",
            live.Diagram.Entities,
            live.Diagram.Relationships
        );
        live.Diagram.Relationships.Add(
            NewRelationship(oldParent, child, [(oldKey, pid)], name: _chainName)
        );
        _chainSetNull = exclusionChainRng!.Next(2) == 0;
        _chainTables.Add(childTable);
        _chainTables.Add(oldTable);
        _chainTables.Add(newTable);
    }

    /// <summary>目標の図：DB 側の部品を組んだ形の目標の図の変更を加える（本流の変更が部品を変えていたら加えない）</summary>
    private void ApplyExclusionChainLevel(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        switch (_exclusionChain)
        {
            case ExclusionChainForm.LastColumnUnique:
                ApplyLastColumnUnique(entities, relationships, live);
                break;
            case ExclusionChainForm.UnrebuiltPrimaryKeyForeignKey:
                ApplyUnrebuiltPrimaryKey(entities, relationships, live);
                break;
            case ExclusionChainForm.TypeMismatchForeignKey:
                ApplyTypeMismatch(entities, relationships, live);
                break;
        }
    }

    /// <summary>
    /// 列が残らない表の列を消し（一意制約も図から消える）、同じ名前の一意制約を別の表の一意制約の候補の列へ足す
    /// （候補の選び方は <see cref="MoveUniqueToOtherTable"/> と同じ）
    /// </summary>
    private void ApplyLastColumnUnique(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var name = _chainName!;
        var entity = FindTable(entities, _chainTables[0]);
        var column = entity?.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, "u"));

        if (
            entity is null
            || column is null
            || entity.Columns.Count != 1
            || !entity.UniqueConstraints.Any(u =>
                u.Name is not null && SchemaNames.AreSame(u.Name, name)
            )
            || relationships.Any(r =>
                r.ColumnPairs.Any(p =>
                    p.SourceColumnId == column.Id || p.TargetColumnId == column.Id
                )
            )
            || entities
                .Where(e => e != entity)
                .SelectMany(e => e.UniqueConstraints)
                .Any(u => u.Name is not null && SchemaNames.AreSame(u.Name, name))
            || relationships.Any(r =>
                r.ConstraintName is not null && SchemaNames.AreSame(r.ConstraintName, name)
            )
        )
        {
            return;
        }

        var candidates = entities
            .Where(e => e != entity && !e.TableName.Contains('.', StringComparison.Ordinal))
            .SelectMany(e =>
                e.Columns.Where(c =>
                        IsUniqueCandidate(live, e, c)
                        && !e.UniqueConstraints.Any(u => u.ColumnIds.SequenceEqual([c.Id]))
                    )
                    .Select(c => (Entity: e, Column: c))
            )
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var (destination, target) = candidates[exclusionChainRng!.Next(candidates.Count)];
        entity.Columns.Remove(column);
        entity.UniqueConstraints.RemoveAll(u => u.ColumnIds.Contains(column.Id));
        destination.UniqueConstraints.Add(
            new UniqueConstraint { Name = name, ColumnIds = [target.Id] }
        );
        Mutations.Add($"drop column {entity.TableName}.{column.Name}");
        Mutations.Add($"drop unique {entity.TableName} {name}");
        Mutations.Add($"add unique {destination.TableName}({target.Name}) named");
        live.Shapes.Add(FuzzShape.ExclusionChainLastColumnUniqueReady);
        live.ExclusionChains.Add(
            new FuzzExclusionChain(
                FuzzShape.ExclusionChainLastColumnUnique,
                [
                    new FuzzChainItem(SchemaDiffKind.DropColumn, entity.TableName, column.Name),
                    new FuzzChainItem(
                        SchemaDiffKind.AddUniqueConstraint,
                        destination.TableName,
                        name
                    ),
                ],
                [new FuzzChainItem(SchemaDiffKind.DropUniqueConstraint, entity.TableName, name)]
            )
        );
    }

    /// <summary>取込が捨てる物を持つ表 P の主キーを id から k へ移し、表 C の列から P.k への外部キーを足す</summary>
    private void ApplyUnrebuiltPrimaryKey(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var intType = dialect.TypeName(FuzzTypeKind.Int, 0);
        var parent = FindTable(entities, _chainTables[0]);
        var child = FindTable(entities, _chainTables[1]);
        var id = parent?.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, "id"));
        var key = parent?.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, "k"));
        var reference = child?.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, "p_ref"));

        if (
            parent is null
            || child is null
            || id is null
            || key is null
            || reference is null
            || !parent.GetPrimaryKeyColumnsInOrder().SequenceEqual([id])
            || key.IsComputed
            || reference.IsComputed
            || !string.Equals(key.DataType, intType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(reference.DataType, intType, StringComparison.OrdinalIgnoreCase)
            || Info(live, parent, key) is not { IsDistinct: true }
            || relationships.Any(r => r.ColumnPairs.Any(p => p.TargetColumnId == reference.Id))
        )
        {
            return;
        }

        SetPrimaryKey(parent, [key]);
        Mutations.Add(
            $"move pk {parent.TableName} -> {key.Name}{ReferencingTag(parent, relationships)}"
        );
        var name = UnusedConstraintName("FK_chain_c_p", entities, relationships);
        relationships.Add(NewRelationship(parent, child, [(key, reference)], name: name));
        Mutations.Add($"add fk {child.TableName}.{reference.Name} -> {parent.TableName}");
        live.Shapes.Add(FuzzShape.ExclusionChainUnrebuiltPrimaryKeyReady);
        live.ExclusionChains.Add(
            new FuzzExclusionChain(
                FuzzShape.ExclusionChainUnrebuiltPrimaryKey,
                [
                    new FuzzChainItem(SchemaDiffKind.AlterPrimaryKey, parent.TableName),
                    new FuzzChainItem(SchemaDiffKind.AddForeignKey, child.TableName, name),
                ],
                []
            )
        );
    }

    /// <summary>
    /// 子 c の外部キーの親を新しい親へ付け替え（作り直し）、新しい親の列の型の種類を文字列へ変え、古い定義と同じ外部キーを別の名前で足す
    /// （半々で SET NULL）
    /// </summary>
    private void ApplyTypeMismatch(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var intType = dialect.TypeName(FuzzTypeKind.Int, 0);
        var name = _chainName!;
        var child = FindTable(entities, _chainTables[0]);
        var oldParent = FindTable(entities, _chainTables[1]);
        var newParent = FindTable(entities, _chainTables[2]);
        var pid = child?.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, "pid"));
        var oldKey = oldParent?.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, "id"));
        var newKey = newParent?.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, "id"));
        var foreignKey = relationships.FirstOrDefault(r =>
            r.ConstraintName is not null && SchemaNames.AreSame(r.ConstraintName, name)
        );

        if (
            child is null
            || oldParent is null
            || newParent is null
            || pid is null
            || oldKey is null
            || newKey is null
            || foreignKey is null
            || foreignKey.SourceEntityId != oldParent.Id
            || foreignKey.TargetEntityId != child.Id
            || foreignKey.ColumnPairs.Count != 1
            || foreignKey.ColumnPairs[0].SourceColumnId != oldKey.Id
            || foreignKey.ColumnPairs[0].TargetColumnId != pid.Id
            || !oldParent.GetPrimaryKeyColumnsInOrder().SequenceEqual([oldKey])
            || !newParent.GetPrimaryKeyColumnsInOrder().SequenceEqual([newKey])
            || new[] { pid, oldKey, newKey }.Any(c =>
                c.IsComputed
                || !string.Equals(c.DataType, intType, StringComparison.OrdinalIgnoreCase)
            )
            || relationships.Any(r =>
                r != foreignKey && r.ColumnPairs.Any(p => p.TargetColumnId == pid.Id)
            )
        )
        {
            return;
        }

        newKey.DataType = dialect.TypeName(FuzzTypeKind.VarChar, 10);
        Mutations.Add(
            $"alter type {newParent.TableName}.{newKey.Name} -> {newKey.DataType}{TypeTag(FuzzTypeKind.VarChar, 10)}"
        );
        foreignKey.SourceEntityId = newParent.Id;
        foreignKey.ColumnPairs = [new RelationshipColumnPair(newKey.Id, pid.Id)];
        Mutations.Add($"retarget fk {name} {child.TableName} parent -> {newParent.TableName}");
        var added = UnusedConstraintName("FK_chain_y", entities, relationships);
        var relationship = NewRelationship(oldParent, child, [(oldKey, pid)], name: added);
        relationship.OnDelete = _chainSetNull
            ? ForeignKeyReferentialAction.SetNull
            : ForeignKeyReferentialAction.NoAction;
        relationships.Add(relationship);
        Mutations.Add($"add fk {child.TableName}.{pid.Name} -> {oldParent.TableName}");
        live.Shapes.Add(FuzzShape.ExclusionChainTypeMismatchReady);
        live.ExclusionChains.Add(
            new FuzzExclusionChain(
                FuzzShape.ExclusionChainTypeMismatch,
                [
                    new FuzzChainItem(SchemaDiffKind.AlterColumn, newParent.TableName, newKey.Name),
                    new FuzzChainItem(SchemaDiffKind.AlterForeignKey, child.TableName, name),
                    new FuzzChainItem(SchemaDiffKind.AddForeignKey, child.TableName, added),
                ],
                []
            )
        );
    }

    /// <summary>目標の図の表を名前（<see cref="SchemaNames"/> の規則）で引く（無ければ null）</summary>
    private static Entity? FindTable(List<Entity> entities, string table) =>
        entities.FirstOrDefault(e => SchemaNames.AreSame(e.TableName, table));
}
