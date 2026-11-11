using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の総当たりの生成器の、表と列の形の水準（乱数の列 <c>TableShapeSeed</c>）
/// </summary>
/// <remarks>
/// <para>
/// 新しい表の形の幅（複合主キー・一意制約・主キーと同じ列の一意制約・自己参照・新しい表どうしの外部キー＝<see cref="DecorateNewTable"/>）、
/// 新しく足した列への一意制約（<see cref="AddUniqueOnNewColumn"/>）、型の変更の幅（文字列から整数・整数から小数・スケールの変更・
/// 固定長と可変長の入れ替え＝<see cref="TypeBreadth"/>）、DB 側の精度なしの小数の列（<see cref="AddPrecisionlessDecimal"/>。その列の型の
/// 変更は精度なしから精度ありになる）。
/// </para>
/// <para>
/// どの水準も、本流の乱数を引き終えた後でこの列だけから引き、既存の 6 本の列を引かない。水準が図を変えなかった種は、足す前と同じケースのまま残る。
/// データはどの制約も満たすように選ぶ。SQL Server（NULL を 2 行目で重なりとみなす一意制約）の行のある表に足した列の一意制約だけは、既存の行で
/// 断られる文として宣言する。
/// </para>
/// </remarks>
internal sealed partial class SyncPlanFuzzGenerator
{
    /// <summary>
    /// DB 側の図：主キーでない小数の列を 1 つ、精度を書かない型にする（種の 8 分の 1・精度なしを覚える方言だけ）。列の情報の長さ（精度）は 0
    /// として覚え、本流の型の変更はその列を精度ありへ変える
    /// </summary>
    private void AddPrecisionlessDecimal(FuzzLiveSchema live)
    {
        if (tableShapeRng is null || dialect.PrecisionlessDecimalTypeName is not { } typeName)
        {
            return;
        }

        var candidates = live
            .Diagram.Entities.SelectMany(e =>
                e.Columns.Where(c =>
                        !c.IsPrimaryKey
                        && live.Find(e.TableName, c.Name)
                            is { Kind: FuzzTypeKind.Decimal, Computed: false }
                    )
                    .Select(c => (Entity: e, Column: c))
            )
            .ToList();

        if (candidates.Count == 0 || tableShapeRng.Next(8) != 0)
        {
            return;
        }

        var (entity, column) = candidates[tableShapeRng.Next(candidates.Count)];
        column.DataType = typeName;
        live.Find(entity.TableName, column.Name)!.Length = 0;
        live.Shapes.Add(FuzzShape.PrecisionlessDecimal);
    }

    /// <summary>
    /// 新しい表を飾る（半々で、複合主キー・一意制約・主キーと同じ列の一意制約・自己参照・新しい表どうしの外部キーの 1 つ）。変更の文へ足す札と、
    /// 新しい表を図へ足した後に 2 つ目の新しい表を足す手（無ければ null）を返す
    /// </summary>
    private (string Suffix, Action? AddSecondTable) DecorateNewTable(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live,
        Entity entity,
        Column id,
        bool referenced
    )
    {
        if (tableShapeRng is null)
        {
            return ("", null);
        }

        var value = entity.Columns.Single(c => c.Name == "v");

        switch (tableShapeRng.Next(10))
        {
            // 複合主キー（既存の表が新しい表の id を参照する外部キーを持たないときだけ）
            case 0 when !referenced:
            {
                var k = new Column
                {
                    Name = "k",
                    DataType = dialect.TypeName(FuzzTypeKind.Int, 0),
                    IsNullable = false,
                };
                entity.Columns.Add(k);
                SetPrimaryKey(entity, [id, k]);
                live.Shapes.Add(FuzzShape.NewTableCompositePrimaryKey);
                return (" (composite pk)", null);
            }
            case 1:
                entity.UniqueConstraints.Add(
                    new UniqueConstraint
                    {
                        Name = NewTableConstraintName(entities, relationships, entity, "UQ", "v"),
                        ColumnIds = [value.Id],
                    }
                );
                live.Shapes.Add(FuzzShape.NewTableUnique);
                return (" (unique v)", null);
            case 2:
                entity.UniqueConstraints.Add(
                    new UniqueConstraint
                    {
                        Name = NewTableConstraintName(
                            entities,
                            relationships,
                            entity,
                            "UQ",
                            "pk_twin"
                        ),
                        ColumnIds = [id.Id],
                    }
                );
                live.Shapes.Add(FuzzShape.NewTablePrimaryKeyTwinUnique);
                return (" (pk twin unique)", null);
            case 3:
            {
                var parentColumn = new Column
                {
                    Name = "parent_id",
                    DataType = dialect.TypeName(FuzzTypeKind.Int, 0),
                    IsForeignKey = true,
                };
                entity.Columns.Add(parentColumn);
                relationships.Add(
                    NewRelationship(
                        entity,
                        entity,
                        [(id, parentColumn)],
                        NewTableConstraintName(entities, relationships, entity, "FK", "parent")
                    )
                );
                live.Shapes.Add(FuzzShape.NewTableSelfReference);
                return (" (self reference)", null);
            }
            case 4:
            {
                var named = tableShapeRng.Next(2) == 0;
                return (
                    "",
                    () =>
                    {
                        var second = new Entity
                        {
                            TableName = UnusedTableName(entities, $"{entity.TableName}_child"),
                        };
                        var secondId = new Column
                        {
                            Name = "id",
                            DataType = dialect.TypeName(FuzzTypeKind.Int, 0),
                            IsPrimaryKey = true,
                            IsNullable = false,
                        };
                        var reference = new Column
                        {
                            Name = "new_ref",
                            DataType = dialect.TypeName(FuzzTypeKind.Int, 0),
                            IsForeignKey = true,
                        };
                        second.Columns.Add(secondId);
                        second.Columns.Add(reference);
                        second.PrimaryKeyColumnIds.Add(secondId.Id);
                        relationships.Add(
                            NewRelationship(
                                entity,
                                second,
                                [(id, reference)],
                                named
                                    ? UnusedConstraintName(
                                        $"FK_{NamePart(second.TableName)}_new",
                                        entities,
                                        relationships
                                    )
                                    : null
                            )
                        );
                        entities.Add(second);
                        live.RowCounts[second.TableName] = 0;
                        Mutations.Add($"add table {second.TableName} (fk -> {entity.TableName})");
                        live.Shapes.Add(FuzzShape.NewTablesWithForeignKey);
                    }
                );
            }
            // 一意制約の名前に、目標の図で外した DB の名前つきの制約の名前を付ける（表の名前を変えて古い表の一意制約の名前を引き継ぐ形。
            // 古い制約の削除を選ばないと、名前の範囲によっては表の作成が名前の重なりで断られる）
            case 5:
            {
                var taken = relationships
                    .Select(r => r.ConstraintName)
                    .Concat(entities.SelectMany(e => e.UniqueConstraints).Select(u => u.Name))
                    .OfType<string>()
                    .ToHashSet(SchemaNames.Comparer);
                var released = live
                    .Diagram.Relationships.Select(r => r.ConstraintName)
                    .Concat(
                        live.Diagram.Entities.SelectMany(e => e.UniqueConstraints)
                            .Select(u => u.Name)
                    )
                    .OfType<string>()
                    .Where(n => !taken.Contains(n) && !IsDeclared(live, n))
                    .Distinct(SchemaNames.Comparer)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();

                if (released.Count == 0)
                {
                    return ("", null);
                }

                var name = released[tableShapeRng.Next(released.Count)];
                entity.UniqueConstraints.Add(
                    new UniqueConstraint { Name = name, ColumnIds = [value.Id] }
                );
                live.Shapes.Add(FuzzShape.NewTableTakesReleasedName);
                return ($" (unique v takes {name})", null);
            }
            default:
                return ("", null);
        }
    }

    /// <summary>新しい表の制約名（半々で名前を付ける。付けるなら図の名前と重ならない名前）</summary>
    private string? NewTableConstraintName(
        List<Entity> entities,
        List<Relationship> relationships,
        Entity entity,
        string prefix,
        string part
    ) =>
        tableShapeRng!.Next(2) == 0
            ? UnusedConstraintName(
                $"{prefix}_{NamePart(entity.TableName)}_{part}",
                entities,
                relationships
            )
            : null;

    /// <summary>
    /// 新しく足した列に一意制約を張る（6 分の 1）。行のある表では足した列が全部 NULL なので、NULL を 2 行目で重なりとみなす方言（SQL Server）では
    /// 名前を付けて、既存の行で断られる文として宣言する
    /// </summary>
    private void AddUniqueOnNewColumn(
        List<Entity> entities,
        FuzzLiveSchema live,
        Entity entity,
        Column column
    )
    {
        if (tableShapeRng is null || tableShapeRng.Next(6) != 0)
        {
            return;
        }

        var refused = RowCount(live, entity) > 0 && !dialect.UniqueAllowsRepeatedNull;
        var named = tableShapeRng.Next(2) == 0 || refused;
        var name = named
            ? UnusedConstraintName($"UQ_newcol_{tableShapeRng.Next(1000)}", entities, [])
            : null;
        entity.UniqueConstraints.Add(new UniqueConstraint { Name = name, ColumnIds = [column.Id] });
        Mutations.Add($"add unique {entity.TableName}({column.Name}){(named ? " named" : "")}");
        live.Shapes.Add(FuzzShape.UniqueOnNewColumn);

        if (refused)
        {
            live.DataFailures.Add(
                new FuzzDataFailure(
                    FuzzDataFailureKind.AddUniqueConstraint,
                    entity.TableName,
                    name!,
                    column.Name
                )
            );
        }
    }

    /// <summary>
    /// 型の変更の幅（半々）：文字列から整数（変える列が全部 NULL か行 0）・整数から小数・小数のスケールの上げ下げ・同じ系統の固定長と
    /// 可変長の入れ替え（長さは同じ）のうち、その列に使えるものの 1 つ。行き先の種類・長さと型名を返す（使えなければ null）。文字列から整数は
    /// 値の条件で使える列が少ないので、使えるときは入れ替えの 2 倍の重みで引く
    /// </summary>
    private (FuzzTypeKind Kind, int Length, string TypeName)? TypeBreadth(
        FuzzColumnInfo info,
        List<(Entity Entity, Column Column)> partners,
        FuzzLiveSchema live
    )
    {
        if (tableShapeRng is null || tableShapeRng.Next(2) != 0)
        {
            return null;
        }

        var options = new List<(FuzzTypeKind Kind, int Length, string TypeName, string Shape)>();

        switch (info.Kind)
        {
            case FuzzTypeKind.VarChar or FuzzTypeKind.FixedChar:
                options.Add(
                    (
                        info.Kind == FuzzTypeKind.VarChar
                            ? FuzzTypeKind.FixedChar
                            : FuzzTypeKind.VarChar,
                        info.Length,
                        dialect.SwappedLengthTypeName(info.Kind, info.Length),
                        FuzzShape.FixedVariableSwap
                    )
                );

                if (
                    partners.All(p =>
                        Info(live, p.Entity, p.Column) is { } pi && pi.Values.All(v => v is null)
                    )
                )
                {
                    options.Add(
                        (
                            FuzzTypeKind.Int,
                            0,
                            dialect.TypeName(FuzzTypeKind.Int, 0),
                            FuzzShape.StringToNumber
                        )
                    );
                    options.Add(
                        (
                            FuzzTypeKind.Int,
                            0,
                            dialect.TypeName(FuzzTypeKind.Int, 0),
                            FuzzShape.StringToNumber
                        )
                    );
                }

                break;
            case FuzzTypeKind.Int:
                options.Add(
                    (
                        FuzzTypeKind.Decimal,
                        12,
                        dialect.TypeName(FuzzTypeKind.Decimal, 12),
                        FuzzShape.IntegerToDecimal
                    )
                );
                break;
            case FuzzTypeKind.Decimal when info.Length > 2:
                options.Add(
                    (
                        FuzzTypeKind.Decimal,
                        info.Length,
                        dialect.DecimalTypeName(info.Length, 3),
                        FuzzShape.DecimalScaleChange
                    )
                );
                options.Add(
                    (
                        FuzzTypeKind.Decimal,
                        info.Length,
                        dialect.DecimalTypeName(info.Length, 1),
                        FuzzShape.DecimalScaleChange
                    )
                );
                break;
        }

        if (options.Count == 0)
        {
            return null;
        }

        var (kind, length, typeName, shape) = options[tableShapeRng.Next(options.Count)];
        live.Shapes.Add(shape);
        return (kind, length, typeName);
    }
}
