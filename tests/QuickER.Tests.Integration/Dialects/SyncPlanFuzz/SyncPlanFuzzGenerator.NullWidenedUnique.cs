using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の総当たりの生成器の、一部が NULL の重複の水準（乱数の列 <c>NullWidenedUniqueSeed</c>・形
/// <see cref="FuzzShape.NullDataWidenedUnique"/>）
/// </summary>
/// <remarks>
/// <para>
/// DB 側の図：行が 2 行以上の表の、外部キー・一意制約・主キーに入らない整数か文字列の列 c を NULL 許容にして名前つきの一意制約を張り、
/// 最初の 2 行の c を NULL にする。同じ条件の別の列 o の 2 行目を 1 行目と同じ値にする（c の一意制約の下に、一部が NULL の重複が
/// できる）。目標の図：(c, o) の名前つきの一意制約を足す（c の一意制約の上位集合）。
/// </para>
/// <para>
/// 一部が NULL の重複を重なりとみなす DB（<see cref="SyncPlanFuzzDialect.UniqueComparesPartiallyNullKeys"/>＝Oracle）では、追加が既存の
/// 行で断られる文として宣言する（3 回目の効果測定の PA1。計画は c の一意制約を「既存の行で断られない」の証拠に使っていた）。
/// 一意制約の列の NULL どうしを重なりとみなす DB（<see cref="SyncPlanFuzzDialect.UniqueAllowsRepeatedNull"/> が偽＝SQL Server）は、c の
/// 一意制約の下に NULL を 2 行持てないので作らない。種の 16 分の 1。本流が c・o を消すか型・NULL 許容を変えていたら目標の図の変更を
/// 加えない。
/// </para>
/// </remarks>
internal sealed partial class SyncPlanFuzzGenerator
{
    /// <summary>この水準が組んだ DB 側の図の表と列 c・o（組んでいなければ null）</summary>
    private (string Table, string Column, string Other)? _nullWidenedUniqueTarget;

    /// <summary>DB 側の図に、一部が NULL の重複を持つ列 c の一意制約を組む（<see cref="BuildLive"/>・データを入れる前）</summary>
    private void AddNullWidenedUniqueLive(FuzzLiveSchema live)
    {
        if (
            nullWidenedUniqueRng is null
            || nullWidenedUniqueRng.Next(16) != 0
            || !dialect.UniqueAllowsRepeatedNull
        )
        {
            return;
        }

        var pairColumns = live
            .Diagram.Relationships.SelectMany(r =>
                r.ColumnPairs.SelectMany(p => new[] { p.SourceColumnId, p.TargetColumnId })
            )
            .ToHashSet();
        var tables = live
            .Diagram.Entities.Where(e => RowCount(live, e) >= 2)
            .Select(e =>
                (
                    Entity: e,
                    Columns: e.Columns.Where(c =>
                            !c.IsPrimaryKey
                            && !pairColumns.Contains(c.Id)
                            && !e.UniqueConstraints.Any(u => u.ColumnIds.Contains(c.Id))
                            && _rowLevelTarget != (e.TableName, c.Name)
                            && Info(live, e, c)
                                is {
                                    Kind: FuzzTypeKind.Int or FuzzTypeKind.VarChar,
                                    Computed: false,
                                    Pinned: false,
                                    HasNoNull: true,
                                }
                        )
                        .ToList()
                )
            )
            .Where(t => t.Columns.Count >= 2)
            .ToList();

        if (tables.Count == 0)
        {
            return;
        }

        var (entity, columns) = tables[nullWidenedUniqueRng.Next(tables.Count)];
        var column = columns[nullWidenedUniqueRng.Next(columns.Count)];
        var others = columns.Where(c => c != column).ToList();
        var other = others[nullWidenedUniqueRng.Next(others.Count)];
        var values = Info(live, entity, column)!.Values;
        var otherValues = Info(live, entity, other)!.Values;

        column.IsNullable = true;
        values[0] = null;
        values[1] = null;
        otherValues[1] = otherValues[0];
        entity.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = UnusedConstraintName(
                    $"UQ_nullable_{column.Name}",
                    live.Diagram.Entities,
                    live.Diagram.Relationships
                ),
                ColumnIds = [column.Id],
            }
        );
        _nullWidenedUniqueTarget = (entity.TableName, column.Name, other.Name);
    }

    /// <summary>目標の図で (c, o) の一意制約を足し、断る DB では既存の行で断られる文として宣言する（<see cref="Mutate"/>）</summary>
    private void ApplyNullWidenedUniqueLevel(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        if (_nullWidenedUniqueTarget is not { } target)
        {
            return;
        }

        var entity = entities.FirstOrDefault(e => e.TableName == target.Table);
        var column = entity?.Columns.FirstOrDefault(c => c.Name == target.Column);
        var other = entity?.Columns.FirstOrDefault(c => c.Name == target.Other);
        var liveEntity = live.Diagram.Entities.First(e => e.TableName == target.Table);

        // 本流が表・列を消したか、型か NULL 許容を変えていたら足さない
        if (
            entity is null
            || column is null
            || other is null
            || !column.IsNullable
            || column.DataType != liveEntity.Columns.First(c => c.Name == target.Column).DataType
            || other.DataType != liveEntity.Columns.First(c => c.Name == target.Other).DataType
            || other.IsNullable != liveEntity.Columns.First(c => c.Name == target.Other).IsNullable
        )
        {
            return;
        }

        var name = UnusedConstraintName($"UQ_widened_{column.Name}", entities, relationships);
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = name, ColumnIds = [column.Id, other.Id] }
        );
        Mutations.Add($"add unique {entity.TableName}({column.Name}, {other.Name}) named");
        live.Shapes.Add(FuzzShape.NullDataWidenedUnique);

        if (dialect.UniqueComparesPartiallyNullKeys)
        {
            live.DataFailures.Add(
                new FuzzDataFailure(
                    FuzzDataFailureKind.AddUniqueConstraint,
                    entity.TableName,
                    name,
                    column.Name
                )
            );
        }
    }
}
