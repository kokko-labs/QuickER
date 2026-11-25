using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Resources;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Scheduling;

/// <summary>
/// 宣言が「外れていなければならない」と答えた制約を、外す文と付け直す文の対にする
/// </summary>
/// <remarks>
/// <para>
/// 外す文の並びは DB の今の図の制約の並び。同じ計画が明示に外す制約（選んだ削除・作り直しの展開・端が消える外部キー）は、
/// その文が外すので対象にしない（付け直しもしない）。付け直せない制約（<see cref="UnreaddableConstraints"/>）は外さない。
/// </para>
/// <para>
/// どの制約も DB の今の定義のまま付け直す。外部キーは、外す文は DB の名前（無ければ null）、付け直す文は DB の実効名
/// （<see cref="State.LiveSchema.ForeignKeyNames"/>）を持つ。2 つの文は同じ対を持つ。外す文と付け直す文は別々の並びで返し、
/// 呼び出し側が、外す文を選んだ文より前、付け直す文を選んだ文より後ろへ置く。
/// </para>
/// </remarks>
internal static class Readds
{
    /// <summary>外部キーの外す文と付け直す文を作る</summary>
    /// <param name="required">宣言が外れていなければならないと答えた外部キー（参照で比べる）</param>
    /// <param name="input">外し直しを決める段の入力</param>
    /// <returns>外す文の並びと、付け直す文の並び（どちらも DB の今の図の外部キーの並び）</returns>
    public static (List<ScheduledStep> Drops, List<ScheduledStep> Adds) ForeignKeys(
        HashSet<ResolvedForeignKeyRelationship> required,
        SchedulingInput input
    )
    {
        var drops = new List<ScheduledStep>();
        var adds = new List<ScheduledStep>();
        var limit = input.Capabilities.IdentifierLengthLimit;
        var explicitlyDropped = input.PostSync.DroppedForeignKeys;

        foreach (var fk in input.Live.ForeignKeys)
        {
            if (!required.Contains(fk))
            {
                continue;
            }

            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                explicitlyDropped.Contains(
                    PlanIdentity.ForeignKeyKey(
                        childTable,
                        parentTable,
                        fk.ColumnPairs,
                        fk.Relationship,
                        limit
                    )
                ) || UnreaddableConstraints.IsUnreaddable(fk, input)
            )
            {
                continue;
            }

            var name = PlanIdentity.ResolveForeignKeyName(
                fk.Relationship,
                childTable,
                parentTable,
                limit,
                input.Live
            );
            var origin = new ImplicitOrigin(
                ImplicitStepReason.ForeignKeyRebuild,
                string.Format(Strings.Diff_AutoForeignKeyRebuild, name)
            );
            var pair = new ReaddPair();

            drops.Add(
                new ScheduledStep(
                    new DropForeignKeyStep(
                        origin,
                        fk.Child.TableName,
                        fk.Parent.TableName,
                        fk.Relationship.ConstraintName,
                        fk.ColumnPairs,
                        fk.Relationship.OnDelete,
                        fk.Relationship.OnUpdate
                    )
                    {
                        Pair = pair,
                    }
                )
                {
                    Relationship = fk.Relationship,
                }
            );

            adds.Add(
                new ScheduledStep(
                    new AddForeignKeyStep(
                        origin,
                        fk.Child.TableName,
                        fk.Parent.TableName,
                        name,
                        fk.ColumnPairs,
                        fk.Relationship.OnDelete,
                        fk.Relationship.OnUpdate
                    )
                    {
                        Pair = pair,
                    }
                )
                {
                    Relationship = fk.Relationship,
                    ParentDefinition = fk.Parent,
                }
            );
        }

        return (drops, adds);
    }

    /// <summary>一意制約の外す文と付け直す文を作る</summary>
    /// <param name="required">宣言が外れていなければならないと答えた一意制約（参照で比べる）</param>
    /// <param name="input">外し直しを決める段の入力</param>
    /// <returns>外す文の並びと、付け直す文の並び（どちらも DB の今の図の表と一意制約の並び）</returns>
    /// <remarks>
    /// 同じ計画が明示に外す一意制約（選んだ削除・作り直しの展開・同期後の状態の差として足した削除）は対象にしない。表か構成列が
    /// 同じ計画で消える一意制約は、外すだけで付け直さない（外す文は対を持たない）。名前と構成列の並びは DB の今のまま。
    /// </remarks>
    public static (List<ScheduledStep> Drops, List<ScheduledStep> Adds) UniqueConstraints(
        HashSet<UniqueConstraint> required,
        SchedulingInput input
    )
    {
        var drops = new List<ScheduledStep>();
        var adds = new List<ScheduledStep>();
        var explicitlyDropped = input.PostSync.DroppedUniqueConstraints;
        var dropped = input.PostSync.Dropped;

        foreach (var table in input.Live.Entities)
        {
            var tableName = SchemaDiffService.NormalizeTable(table);

            foreach (var constraint in table.UniqueConstraints)
            {
                if (!required.Contains(constraint))
                {
                    continue;
                }

                if (
                    !UniqueConstraintNaming.TryResolveColumnNames(
                        table,
                        constraint,
                        out var columns
                    )
                )
                {
                    continue;
                }

                if (
                    explicitlyDropped.Contains(
                        PlanIdentity.UniqueConstraintKey(tableName, constraint.Name, columns)
                    )
                )
                {
                    continue;
                }

                var origin = new ImplicitOrigin(
                    ImplicitStepReason.UniqueConstraintRebuild,
                    string.Format(
                        Strings.Diff_AutoUniqueConstraintRebuild,
                        string.Join(", ", columns)
                    )
                );
                // 同期後に在るかの判定。構成列が消える一意制約は、列と一緒に消える一意制約として同じ計画が明示に外す
                // （上の explicitlyDropped で除かれる）ので、構成列の条件に届く入力は今は無い。同期後に在るかを、その前の段の
                // 振る舞いに頼らずここで決めるために置いている
                var readded =
                    !dropped.Tables.Contains(tableName)
                    && !dropped.TouchesColumns(tableName, columns);
                var pair = readded ? new ReaddPair() : null;

                drops.Add(
                    new ScheduledStep(
                        new DropUniqueConstraintStep(origin, tableName, constraint.Name, columns)
                        {
                            Pair = pair,
                        }
                    )
                );

                if (readded)
                {
                    adds.Add(
                        new ScheduledStep(
                            new AddUniqueConstraintStep(origin, tableName, constraint.Name, columns)
                            {
                                Pair = pair,
                            }
                        )
                    );
                }
            }
        }

        return (drops, adds);
    }

    /// <summary>主キーの外す文と付け直す文を作る</summary>
    /// <param name="required">宣言が外れていなければならないと答えた主キーの表（参照で比べる）</param>
    /// <param name="input">外し直しを決める段の入力</param>
    /// <returns>表ごとに、外す文・付け直す文の順（DB の今の図の表の並び）</returns>
    /// <remarks>
    /// 主キーの変更を選んだ表は対象にしない（その変更が外して付ける）。主キーの列の並びは DB の今のまま付け直す。
    /// </remarks>
    public static List<ScheduledStep> PrimaryKeys(HashSet<Entity> required, SchedulingInput input)
    {
        var steps = new List<ScheduledStep>();
        var primaryKeyChanges = input.PostSync.PrimaryKeyChanges;

        foreach (var table in input.Live.Entities)
        {
            var tableName = SchemaDiffService.NormalizeTable(table);

            if (!required.Contains(table) || primaryKeyChanges.ContainsKey(tableName))
            {
                continue;
            }

            IReadOnlyList<string> columns =
            [
                .. table.GetPrimaryKeyColumnsInOrder().Select(c => c.Name),
            ];
            var origin = new ImplicitOrigin(
                ImplicitStepReason.PrimaryKeyRebuild,
                string.Format(Strings.Diff_AutoPrimaryKeyRebuild, string.Join(", ", columns))
            );
            var pair = new ReaddPair();

            steps.Add(new ScheduledStep(new DropPrimaryKeyStep(origin, tableName) { Pair = pair }));
            steps.Add(
                new ScheduledStep(new AddPrimaryKeyStep(origin, tableName, columns) { Pair = pair })
            );
        }

        return steps;
    }
}
