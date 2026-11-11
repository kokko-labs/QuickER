using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の総当たりの生成器の、属性の形の水準（乱数の列 <c>AttributeShapeSeed</c>）
/// </summary>
/// <remarks>
/// <para>
/// DB 側の図：主キーの無い表（<see cref="RemoveLivePrimaryKey"/>）・表と列の説明（<see cref="AddLiveDescriptions"/>）・既定値のある列
/// （<see cref="AddColumnDefaults"/>。図は既定値を持たないので、DDL の後に生の SQL で足し、取込と同じ形の事実を網へ渡す）。
/// 目標の図：主キーの無い表に主キーを足す・説明を足す・変える・消す・空白だけにする・BMP の外の文字を入れる・新しい表と列に説明を付ける・
/// 既定値のある列の型を広げる（<see cref="ApplyAttributeShapeLevel"/>）。
/// </para>
/// <para>
/// どの水準も、本流の乱数を引き終えた後でこの列だけから引き、既存の 7 本の列を引かない。水準が図を変えなかった種は、足す前と同じケースのまま
/// 残る。説明は説明を同期する方言だけ、既定値は ALTER で既定値を足せる方言だけで作る（SQLite はどちらも作らない）。
/// </para>
/// </remarks>
internal sealed partial class SyncPlanFuzzGenerator
{
    /// <summary>この種で既定値を足した列（表名と列名）</summary>
    private readonly List<(string Table, string Column)> _defaultColumns = [];

    /// <summary>この種で主キーを外した表の名前</summary>
    private string? _keylessTable;

    /// <summary>
    /// DB 側の図：外部キーに参照されない、整数 1 列の主キーの表から主キーを外す（種の 16 分の 1。列は NOT NULL のまま・値は一意）
    /// </summary>
    private void RemoveLivePrimaryKey(FuzzLiveSchema live)
    {
        if (attributeShapeRng is null)
        {
            return;
        }

        var candidates = live
            .Diagram.Entities.Where(e =>
                IsSingleIntPrimaryKey(e)
                && !e.TableName.Contains('.', StringComparison.Ordinal)
                && !live.Diagram.Relationships.Any(r => r.SourceEntityId == e.Id)
            )
            .ToList();

        if (candidates.Count == 0 || attributeShapeRng.Next(16) != 0)
        {
            return;
        }

        var entity = candidates[attributeShapeRng.Next(candidates.Count)];
        SetPrimaryKey(entity, []);
        _keylessTable = entity.TableName;
        live.Shapes.Add(FuzzShape.KeylessTable);
    }

    /// <summary>
    /// DB 側の図：表と列に BMP の中の説明を付ける（種の 16 分の 1・説明を同期する方言だけ。BMP の外の文字は付けない＝MySQL は保存の
    /// 時点で置き換え、網と取込が食い違う）
    /// </summary>
    private void AddLiveDescriptions(FuzzLiveSchema live)
    {
        if (
            attributeShapeRng is null
            || !dialect.Provider.SyncCapabilities.SupportsDescriptions
            || attributeShapeRng.Next(16) != 0
        )
        {
            return;
        }

        var entities = live
            .Diagram.Entities.Where(e => !e.TableName.Contains('.', StringComparison.Ordinal))
            .ToList();

        if (entities.Count == 0)
        {
            return;
        }

        var table = entities[attributeShapeRng.Next(entities.Count)];
        table.Description = $"table {BaseName(table)}";
        var column = table.Columns[attributeShapeRng.Next(table.Columns.Count)];
        column.Description = $"column {column.Name.ToLowerInvariant()} é";
        live.Shapes.Add(FuzzShape.LiveDescription);
    }

    /// <summary>
    /// DB 側の図：主キー・外部キー・計算列のどれでもない整数・小数・文字列の列 1〜2 本に既定値を足す（種の 16 分の 1・ALTER で既定値を
    /// 足せる方言だけ）。既定値は型の種類を変えても読める値（0・'0'）にする
    /// </summary>
    private void AddColumnDefaults(FuzzLiveSchema live)
    {
        if (attributeShapeRng is null || attributeShapeRng.Next(16) != 0)
        {
            return;
        }

        var candidates = live
            .Diagram.Entities.Where(e => !e.TableName.Contains('.', StringComparison.Ordinal))
            .SelectMany(e =>
                e.Columns.Where(c =>
                        !c.IsPrimaryKey
                        && !c.IsForeignKey
                        && live.Find(e.TableName, c.Name)
                            is {
                                Computed: false,
                                Pinned: false,
                                Kind: FuzzTypeKind.Int
                                    or FuzzTypeKind.Decimal
                                    or FuzzTypeKind.VarChar
                            }
                    )
                    .Select(c => (Entity: e, Column: c))
            )
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var count = Math.Min(candidates.Count, 1 + attributeShapeRng.Next(2));

        foreach (
            var (entity, column) in candidates
                .OrderBy(_ => attributeShapeRng.Next())
                .Take(count)
                .ToList()
        )
        {
            var literal =
                live.Find(entity.TableName, column.Name)!.Kind == FuzzTypeKind.VarChar
                    ? dialect.StringLiteral("0")
                    : "0";

            if (dialect.AddColumnDefaultSql(entity.TableName, column.Name, literal) is not { } sql)
            {
                return;
            }

            live.ColumnDefaultStatements.Add(sql);
            live.ColumnDefaults.Add(
                new SchemaColumnDefault(entity.TableName, column.Name, literal)
            );
            _defaultColumns.Add((entity.TableName, column.Name));
            live.Shapes.Add(FuzzShape.ColumnDefault);
        }
    }

    /// <summary>
    /// 目標の図：主キーを外した表に主キーを戻す（半々）・説明を変える（種の 12 分の 1）・既定値のある列の型を広げる（半々）
    /// </summary>
    private void ApplyAttributeShapeLevel(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        if (attributeShapeRng is null)
        {
            return;
        }

        RestoreKeylessPrimaryKey(entities, relationships, live);
        ChangeDescription(entities, live);
        WidenDefaultColumn(entities, live);
    }

    /// <summary>主キーを外した表に、元の id 列の主キーを足す（半々。目標の図で主キーがまだ無いときだけ）</summary>
    private void RestoreKeylessPrimaryKey(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        if (_keylessTable is null || attributeShapeRng!.Next(2) != 0)
        {
            return;
        }

        var entity = entities.FirstOrDefault(e =>
            e.TableName.Equals(_keylessTable, StringComparison.OrdinalIgnoreCase)
        );
        var id = entity?.Columns.FirstOrDefault(c =>
            c.Name.Equals("id", StringComparison.OrdinalIgnoreCase)
        );

        if (
            entity is null
            || id is null
            || entity.GetPrimaryKeyColumnsInOrder().Count > 0
            || id.IsComputed
            || Info(live, entity, id)?.IsDistinct == false
        )
        {
            return;
        }

        SetPrimaryKey(entity, [id]);
        Mutations.Add(
            $"extend pk {entity.TableName} + {id.Name}{ReferencingTag(entity, relationships)}"
        );
        live.Shapes.Add(FuzzShape.KeylessTableGetsPrimaryKey);
    }

    /// <summary>
    /// 説明を変える（種の 12 分の 1・説明を同期する方言だけ）：足す・変える・消す・空白だけにする・BMP の外の文字を入れる・新しい表か列に
    /// 付ける、のうち作れるものの 1 つ
    /// </summary>
    private void ChangeDescription(List<Entity> entities, FuzzLiveSchema live)
    {
        if (
            !dialect.Provider.SyncCapabilities.SupportsDescriptions
            || attributeShapeRng!.Next(12) != 0
        )
        {
            return;
        }

        var elements = entities
            .Where(e => !e.TableName.Contains('.', StringComparison.Ordinal))
            .SelectMany(e =>
                new (Entity Entity, Column? Column)[] { (e, null) }.Concat(
                    e.Columns.Where(c => !c.IsComputed).Select(c => (e, (Column?)c))
                )
            )
            .ToList();
        var described = elements.Where(x => DescriptionOf(x) != string.Empty).ToList();
        var blank = elements.Where(x => DescriptionOf(x) == string.Empty).ToList();
        var fresh = elements.Where(x => IsNewElement(live, x)).ToList();
        var options =
            new List<(
                string Shape,
                List<(Entity Entity, Column? Column)> From,
                Func<string> Text
            )>();

        if (blank.Count > 0)
        {
            options.Add((FuzzShape.DescriptionAdd, blank, () => "added note"));
            options.Add((FuzzShape.DescriptionSupplementary, blank, () => "note \U0001F600 x"));
        }

        if (described.Count > 0)
        {
            options.Add((FuzzShape.DescriptionChange, described, () => "changed note"));
            options.Add((FuzzShape.DescriptionRemove, described, () => string.Empty));
            options.Add((FuzzShape.DescriptionBlank, described, () => "   "));
        }

        if (fresh.Count > 0)
        {
            options.Add((FuzzShape.DescriptionNewElement, fresh, () => "new note"));
        }

        if (options.Count == 0)
        {
            return;
        }

        var (shape, from, text) = options[attributeShapeRng.Next(options.Count)];
        var target = from[attributeShapeRng.Next(from.Count)];
        var description = text();

        if (target.Column is { } column)
        {
            column.Description = description;
        }
        else
        {
            target.Entity.Description = description;
        }

        Mutations.Add(
            $"set description {target.Entity.TableName}{(target.Column is null ? string.Empty : "." + target.Column.Name)}"
        );
        live.Shapes.Add(shape);

        static string DescriptionOf((Entity Entity, Column? Column) element) =>
            element.Column?.Description ?? element.Entity.Description;
    }

    /// <summary>DB 側の図に無い表か列か（目標の図で足した要素）</summary>
    private static bool IsNewElement(FuzzLiveSchema live, (Entity Entity, Column? Column) element)
    {
        var liveEntity = live.Diagram.Entities.FirstOrDefault(e =>
            e.TableName.Equals(element.Entity.TableName, StringComparison.OrdinalIgnoreCase)
        );

        return liveEntity is null
            || element.Column is { } column
                && !liveEntity.Columns.Any(c =>
                    c.Name.Equals(column.Name, StringComparison.OrdinalIgnoreCase)
                );
    }

    /// <summary>
    /// 既定値のある列の型を広げる（半々。目標の図でまだ型を変えていない列だけ）：整数は 64 ビット整数へ、文字列は長さを 20 足すか方言の上限
    /// ちょうどの型へ、小数は精度を 2 足す（どれも値のあるまま通る）
    /// </summary>
    private void WidenDefaultColumn(List<Entity> entities, FuzzLiveSchema live)
    {
        if (_defaultColumns.Count == 0 || attributeShapeRng!.Next(2) != 0)
        {
            return;
        }

        var (tableName, columnName) = _defaultColumns[
            attributeShapeRng.Next(_defaultColumns.Count)
        ];
        var entity = entities.FirstOrDefault(e =>
            e.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase)
        );
        var column = entity?.Columns.FirstOrDefault(c =>
            c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase)
        );

        if (entity is null || column is null || live.Find(tableName, columnName) is not { } info)
        {
            return;
        }

        if (column.DataType != dialect.TypeName(info.Kind, info.Length))
        {
            return;
        }

        var (kind, length) = info.Kind switch
        {
            FuzzTypeKind.Int => (FuzzTypeKind.BigInt, 0),
            // 文字列の半分は、方言の上限ちょうどの型へ広げる（MySQL は固定長の char(255)）
            FuzzTypeKind.VarChar
                when dialect.LimitType is { } limit && attributeShapeRng.Next(2) == 0 => limit,
            FuzzTypeKind.VarChar => (FuzzTypeKind.VarChar, info.Length + 20),
            _ => (FuzzTypeKind.Decimal, info.Length + 2),
        };
        column.DataType = dialect.TypeName(kind, length);
        Mutations.Add(
            $"alter type {entity.TableName}.{column.Name} -> {column.DataType}{TypeTag(kind, length)}"
        );
        live.Shapes.Add(FuzzShape.ColumnDefaultTypeChange);
    }
}
