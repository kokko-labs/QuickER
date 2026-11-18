using System.Collections.Generic;
using System.Linq;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Scheduling;

/// <summary>
/// 選んだ項目の最終形（選んだ項目・作り直しを展開した項目・同期後の状態の差として足した項目）を、項目の順のまま文へ写す
/// </summary>
/// <remarks>
/// <para>
/// 1 項目 1 文。主キーの変更だけは解除の文と付与の文の 2 つになり、新しい主キーの列を持つときは 2 つが同じ対を持つ（主キーを
/// 外すだけの変更は解除の文だけ）。作り直しを展開した項目の対は、展開が項目に付けた対をそのまま持つ。
/// </para>
/// <para>
/// 由来は項目の印から決める：印の無い項目は選んだ項目、暗黙の理由を持つ項目は暗黙の文、元の項目を持つ項目（作り直しの展開）は
/// 元の選んだ項目、置き換えの項目はその項目自身。
/// </para>
/// <para>
/// 列順の変更は文にしない（列を動かす文は別に組む）。セクションに無い種類の項目（展開していない作り直しなど）も文にしない。
/// 文に要るメンバーの無い項目があれば、理由だけを返す。
/// </para>
/// </remarks>
internal static class SelectedSteps
{
    /// <summary>項目の並びを文へ写す</summary>
    /// <param name="items">選んだ項目の最終形</param>
    /// <returns>文の並び（表せない項目があれば null と理由）</returns>
    public static (List<ScheduledStep>? Steps, string? Unrepresentable) From(
        IReadOnlyList<SchemaDiffItem> items
    )
    {
        var steps = new List<ScheduledStep>();

        foreach (var item in items)
        {
            var origin = OriginOf(item);
            var table = item.TableName;
            var pair = item.PlanMark?.Pair;

            switch (item.Kind)
            {
                case SchemaDiffKind.AddTable:
                    if (item.Entity is not { } definition)
                    {
                        return (null, Missing(item, "Entity"));
                    }

                    Add(new CreateTableStep(origin, table, definition, item.UniqueConstraintNames));
                    break;

                case SchemaDiffKind.DropTable:
                    Add(new DropTableStep(origin, table));
                    break;

                case SchemaDiffKind.AddColumn:
                    if (item.Column is not { } added)
                    {
                        return (null, Missing(item, "Column"));
                    }

                    Add(new AddColumnStep(origin, table, added));
                    break;

                case SchemaDiffKind.AlterColumn:
                    if (item.Column is not { } altered)
                    {
                        return (null, Missing(item, "Column"));
                    }

                    Add(new AlterColumnStep(origin, table, altered, item.OldColumn));
                    break;

                case SchemaDiffKind.DropColumn:
                    if (item.ColumnName is not { } dropped)
                    {
                        return (null, Missing(item, "ColumnName"));
                    }

                    Add(new DropColumnStep(origin, table, dropped));
                    break;

                case SchemaDiffKind.AlterPrimaryKey:
                    if (SyncPlanner.HasNewPrimaryKeyColumns(item))
                    {
                        var keyPair = new ReaddPair();
                        steps.Add(
                            new ScheduledStep(
                                new DropPrimaryKeyStep(origin, table) { Pair = keyPair }
                            )
                        );
                        steps.Add(
                            new ScheduledStep(
                                new AddPrimaryKeyStep(
                                    origin,
                                    table,
                                    [
                                        .. item.Entity!.GetPrimaryKeyColumnsInOrder()
                                            .Select(c => c.Name),
                                    ]
                                )
                                {
                                    Pair = keyPair,
                                }
                            )
                        );
                    }
                    else
                    {
                        steps.Add(new ScheduledStep(new DropPrimaryKeyStep(origin, table)));
                    }

                    break;

                case SchemaDiffKind.DropUniqueConstraint:
                    Add(
                        new DropUniqueConstraintStep(
                            origin,
                            table,
                            item.UniqueConstraintName,
                            item.UniqueConstraintColumns
                        )
                    );
                    break;

                case SchemaDiffKind.AddUniqueConstraint:
                    Add(
                        new AddUniqueConstraintStep(
                            origin,
                            table,
                            item.UniqueConstraintName,
                            item.UniqueConstraintColumns
                        )
                    );
                    break;

                case SchemaDiffKind.DropForeignKey:
                case SchemaDiffKind.AddForeignKey:
                    if (item.ChildEntity is not { } child)
                    {
                        return (null, Missing(item, "ChildEntity"));
                    }

                    if (item.ParentEntity is not { } parent)
                    {
                        return (null, Missing(item, "ParentEntity"));
                    }

                    if (item.Relationship is not { } relationship)
                    {
                        return (null, Missing(item, "Relationship"));
                    }

                    SyncStep foreignKeyStep =
                        item.Kind == SchemaDiffKind.DropForeignKey
                            ? new DropForeignKeyStep(
                                origin,
                                child.TableName,
                                parent.TableName,
                                item.ForeignKeyName,
                                item.ForeignKeyColumnPairs,
                                relationship.OnDelete,
                                relationship.OnUpdate
                            )
                            : new AddForeignKeyStep(
                                origin,
                                child.TableName,
                                parent.TableName,
                                item.ForeignKeyName ?? relationship.ConstraintName,
                                item.ForeignKeyColumnPairs,
                                relationship.OnDelete,
                                relationship.OnUpdate
                            );
                    steps.Add(
                        new ScheduledStep(foreignKeyStep with { Pair = pair })
                        {
                            Relationship = relationship,
                            ParentDefinition = parent,
                        }
                    );
                    break;

                case SchemaDiffKind.SetTableDescription:
                    Add(new SetTableDescriptionStep(origin, table, item.NewDescription));
                    break;

                case SchemaDiffKind.SetColumnDescription:
                    if (item.ColumnName is not { } described)
                    {
                        return (null, Missing(item, "ColumnName"));
                    }

                    Add(
                        new SetColumnDescriptionStep(origin, table, described, item.NewDescription)
                    );
                    break;

                default:
                    break;
            }

            void Add(SyncStep step) => steps.Add(new ScheduledStep(step with { Pair = pair }));
        }

        return (steps, null);
    }

    /// <summary>項目の由来</summary>
    private static StepOrigin OriginOf(SchemaDiffItem item) =>
        item.PlanMark switch
        {
            { ImplicitReason: { } reason } => new ImplicitOrigin(reason, item.Description),
            { DerivedFrom: { } source } => new SelectedOrigin(source),
            _ => new SelectedOrigin(item),
        };

    private static string Missing(SchemaDiffItem item, string member) =>
        $"{item.Kind} item without {member}";
}
