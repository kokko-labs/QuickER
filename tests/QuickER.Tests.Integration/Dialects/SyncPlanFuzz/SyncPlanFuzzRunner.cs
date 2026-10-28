using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>1 つの種の結果</summary>
internal sealed class FuzzSeedOutcome
{
    /// <summary>種</summary>
    public required int Seed { get; init; }

    /// <summary>方言名</summary>
    public required string Dialect { get; init; }

    /// <summary>差分の選び方</summary>
    public FuzzSelection Selection { get; set; }

    /// <summary>目標の図へ加えた変更</summary>
    public List<string> Mutations { get; } = [];

    /// <summary>選んだ差分項目の説明</summary>
    public List<string> Selected { get; } = [];

    /// <summary>選ばなかった差分項目の説明</summary>
    public List<string> Unselected { get; } = [];

    /// <summary>同期スクリプト</summary>
    public string Script { get; set; } = string.Empty;

    /// <summary>実行がコミットしたか</summary>
    public bool Committed { get; set; }

    /// <summary>実行の失敗のメッセージ</summary>
    public string? Error { get; set; }

    /// <summary>実行後に取り込み直して取った差分（コミットしたときだけ）</summary>
    public List<string> AfterDiff { get; } = [];

    /// <summary>計画の警告の種類</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>破れた表明（空なら合格）</summary>
    public List<string> Violations { get; } = [];

    /// <summary>この種に出た図の形</summary>
    public HashSet<string> Shapes { get; } = [];

    /// <summary>報告用の 1 件分の文</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"--- {Dialect} seed {Seed} (selection {Selection}, committed {Committed}) ---"
        );
        text.AppendLine("violations:");
        Violations.ForEach(v => text.AppendLine("  " + v));
        text.AppendLine("mutations:");
        Mutations.ForEach(m => text.AppendLine("  " + m));
        text.AppendLine("selected:");
        Selected.ForEach(s => text.AppendLine("  " + s));
        text.AppendLine("unselected:");
        Unselected.ForEach(s => text.AppendLine("  " + s));
        text.AppendLine("after diff:");
        AfterDiff.ForEach(s => text.AppendLine("  " + s));
        text.AppendLine("warnings: " + string.Join(", ", Warnings));
        text.AppendLine("error: " + Error);
        text.AppendLine("script:");
        text.AppendLine(Script);
        return text.ToString();
    }
}

/// <summary>
/// 同期計画の総当たりの 1 種分を流す（DB を作る→取り込む→目標の図を作る→差分を選ぶ→計画・実行→表明）。
/// </summary>
/// <remarks>
/// <para>表明は次の 5 つ（どれか 1 つでも破れたら、その種は違反）:</para>
/// <list type="number">
/// <item>実行がコミットする。または、計画の <see cref="SyncPlan.Warnings"/> が空でない（失敗したのに事前の警告が無い＝違反）。
/// 警告が「本当に失敗を予言していたか」は見ない（誤った警告は個別の再現テストで固定する。例えば MySQL の正当な
/// 長さ違いの外部キーに「型の食い違い」と警告して失敗させる形は、警告が出ているのでこの表明では拾えない）。
/// 失敗したときは取り込み直し、選んでいない制約（外部キー・一意制約・主キー）が 1 つも消えていないことも確かめる
/// （DDL を暗黙コミットする方言の部分適用で制約が消えたまま残る形。警告が出ていても違反）。</item>
/// <item>コミットしたら取り込み直し、同じ目標の図で差分を取り直す。選んだ項目は消えている。
/// 同期の前の差分に無かった項目は現れない（変えていない外部キー・制約が巻き添えで消える・変わる形）。
/// ただし列を足した表の列順の差分は除く（足した列は末尾に付き、次の同期で列順の差分になるのが仕様）。
/// 計画が <see cref="SyncPlanWarningKind.SelectedItemRequiresUnselectedItem"/> で名指しして外した項目は、選ばなかった項目
/// として扱う（残ってよい。列の変更は NULL 許容化だけを外して型の変更を残すことがあるので、残ってよいが選んだ項目のまま）。
/// 警告なしに計画から消えた項目は、選んだのに残った違反になる。選択できない案内項目（同期できない差分を知らせるだけ）は
/// 表明 2・2b・3 のどれでも差分として数えない。</item>
/// <item>選ばなかった項目は残っている。ただし、選んだ項目から必然的に消えるものは比べない＝
/// (a) 選んだ AddTable / DropTable の表に関わる項目（子・親として関わる外部キーを含む。外部キーの作り直しは作り直す前の外部キーの表も）
/// (b) 選んだ AddColumn / DropColumn の列に関わる項目（その列を含む一意制約・外部キー。主キーの変更は含めない＝
/// 主キーの列の削除は、その表の主キーの変更を選んだときだけ計画に残るので、選ばなかった主キーの変更は残っている）
/// (c) 選んだ項目がある表の列順の変更（再構築・並べ替えが列の並びを作り直すため）
/// (d) 選んだ主キーの変更の新しい主キー列の列定義の変更（主キー列は NOT NULL になる）
/// (e) 主キーと同じ列集合の一意制約を持てない方言で、選んだ主キーの変更の新しい主キーと同じ列集合の一意制約の削除。</item>
/// <item>DB 側の列の属性（型・NULL 許容・計算列か・主キーの構成と順序）は、選んだ項目が述べた変更以外は変わらない
/// （型＝選んだ AlterColumn が型の変更を述べている・NULL 許容＝選んだ AlterColumn が NULL 許容の変更を述べているか、
/// 選んだ主キーの変更の新しい主キー列・計算列か＝再構築の喪失の警告がその表に出ている・主キー＝選んだ主キーの変更）。</item>
/// <item>残った表の行数は変わらない。取込の警告の種類（種別と表）は増えない。</item>
/// </list>
/// </remarks>
internal static class SyncPlanFuzzRunner
{
    /// <summary>予期しない例外を記録した違反の文の頭</summary>
    public const string SeedThrewPrefix = "seed threw:";

    /// <summary>
    /// 1 つの種を流す。生成器・ランナーの予期しない例外はその種の違反（<see cref="SeedThrewPrefix"/>）として記録し、
    /// ほかの種の結果を止めない（例外でテストごと止まると、それより前の種の違反が報告されずに隠れる）。取り消しは止める
    /// </summary>
    /// <param name="dialect">方言名</param>
    /// <param name="seed">種</param>
    /// <param name="run">1 つの種を流す本体（普段は <see cref="RunAsync"/>）</param>
    public static async Task<FuzzSeedOutcome> RunGuardedAsync(
        string dialect,
        int seed,
        Func<Task<FuzzSeedOutcome>> run
    )
    {
        try
        {
            return await run().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var outcome = new FuzzSeedOutcome
            {
                Seed = seed,
                Dialect = dialect,
                Error = ex.ToString(),
            };
            outcome.Violations.Add(
                $"{SeedThrewPrefix} {ex.GetType().Name}: {FirstLine(ex.Message)}"
            );
            return outcome;
        }
    }

    /// <summary>1 つの種を流して結果を返す（表明の違反は <see cref="FuzzSeedOutcome.Violations"/> に入る）</summary>
    public static async Task<FuzzSeedOutcome> RunAsync(
        SyncPlanFuzzDialect dialect,
        int seed,
        CancellationToken ct
    )
    {
        var outcome = new FuzzSeedOutcome { Seed = seed, Dialect = dialect.Name };
        var generator = new SyncPlanFuzzGenerator(new Random(seed), dialect);
        var provider = dialect.Provider;
        var capabilities = provider.SyncCapabilities;

        await dialect.ResetAsync(ct).ConfigureAwait(false);
        var liveSchema = generator.BuildLive();
        liveSchema.Diagram.TargetDbms = provider.Name;
        await dialect
            .ExecuteSetupAsync(provider.DdlGenerator.Build(liveSchema.Diagram), ct)
            .ConfigureAwait(false);

        var setup = liveSchema
            .InsertStatements.Concat(liveSchema.ComputedColumnStatements)
            .ToList();

        if (setup.Count > 0)
        {
            await dialect
                .ExecuteSetupAsync(string.Join(Environment.NewLine, setup), ct)
                .ConfigureAwait(false);
        }

        var before = await ImportAsync(dialect, ct).ConfigureAwait(false);
        var beforeRows = await CountRowsAsync(dialect, before, ct).ConfigureAwait(false);

        // 目標の図＝DB の図を新しい Guid 空間へ写したものに変更を加える（GUI と同じく live と別の ID）
        var (targetEntities, targetRelationships) = CloneWithFreshIds(before);
        generator.Mutate(targetEntities, targetRelationships, liveSchema);
        outcome.Mutations.AddRange(generator.Mutations);
        outcome.Shapes.UnionWith(liveSchema.Shapes);

        var diff = new SchemaDiffService().Compute(
            before.Entities,
            before.Relationships,
            targetEntities,
            targetRelationships,
            capabilities
        );
        outcome.Selection = generator.Select(diff.Items);
        var selected = diff.Items.Where(i => i.IsSelected).ToList();
        var unselected = diff.Items.Where(i => !i.IsSelected).ToList();
        outcome.Selected.AddRange(selected.Select(i => $"{i.Kind}: {i.Description}"));
        outcome.Unselected.AddRange(unselected.Select(i => $"{i.Kind}: {i.Description}"));
        outcome.Shapes.UnionWith(SelectionShapes(before, selected));

        SyncPlan plan;

        try
        {
            plan = new SyncPlanner().BuildPlan(
                diff.Items,
                capabilities,
                new SyncPlanContext
                {
                    LiveEntities = before.Entities,
                    LiveRelationships = before.Relationships,
                    AuxiliaryObjects = before.AuxiliaryObjects,
                    TableCreateSql = before.TableCreateSql,
                }
            );
            outcome.Script = provider.SyncScriptBuilder.Build(plan);
        }
        catch (Exception ex)
        {
            outcome.Violations.Add($"planning threw: {ex.GetType().Name}: {ex.Message}");
            return outcome;
        }

        outcome.Warnings.AddRange(plan.Warnings.Select(w => $"{w.Kind}({w.TableName})"));

        // 計画が「選んでいない変更に依存する」と名指しして外した項目は、選ばなかった項目として扱う（警告が出ているので
        // 残ってよい）。警告なしに計画から消えた項目は従来どおり「選んだのに残った」違反になる。列の変更は NULL 許容化だけを
        // 外して型の変更を残すことがあるので、残ってよい側にだけ入れる（型の変更は選んだ変更として表明 4 で見る）
        var plannedKeys = plan
            .Sections.SelectMany(s => s.Items)
            .Concat(plan.Rebuilds.SelectMany(r => r.SourceItems))
            .Concat(plan.Reorders.SelectMany(r => r.SourceItems))
            .SelectMany(i => Keys(i, capabilities))
            .ToHashSet();
        var dependencyWarnedTables = plan
            .Warnings.Where(w => w.Kind == SyncPlanWarningKind.SelectedItemRequiresUnselectedItem)
            .Select(w => w.TableName.Trim().ToLowerInvariant())
            .ToHashSet();
        var allowedToRemain = selected
            .Where(i =>
                dependencyWarnedTables.Contains(i.TableName.Trim().ToLowerInvariant())
                && (
                    i.Kind == SchemaDiffKind.AlterColumn
                    || !Keys(i, capabilities).Any(plannedKeys.Contains)
                )
            )
            .ToHashSet();
        var excluded = allowedToRemain
            .Where(i => !Keys(i, capabilities).Any(plannedKeys.Contains))
            .ToList();
        selected = [.. selected.Except(excluded)];
        unselected = [.. unselected, .. excluded];

        var result = await provider
            .SyncExecutor.ExecuteAsync(dialect.Settings, outcome.Script, ct)
            .ConfigureAwait(false);
        outcome.Committed = result.Committed;
        outcome.Error = result.Error;

        // 表明 1: 失敗するなら事前に警告が出ている
        if (!result.Committed)
        {
            if (plan.Warnings.Count == 0)
            {
                outcome.Violations.Add($"failed without a warning: {FirstLine(result.Error)}");
            }

            // 表明 1b: 失敗しても、選んでいない制約は消えていない（DDL を暗黙コミットする方言の部分適用で
            // 外部キー・一意制約・主キーが消えたまま残る形）。例外は、付け直しが候補キーを証明できないと事前に警告した
            // 外部キーだけ（列の集合が合わない付け直しは、取り込んでいない索引で通る構成があるので仕様として警告して実行する。
            // 並びだけの違いは計画が参照先のキーの並びへ並べ替えるので、警告の対象にならない）
            var failedState = await ImportAsync(dialect, ct).ConfigureAwait(false);
            var remaining = ConstraintKeys(failedState, capabilities);
            var warnedForeignKeys = plan
                .Warnings.Where(w =>
                    w.Kind
                        is SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey
                            or SyncPlanWarningKind.ForeignKeyNotOnCandidateKey
                )
                .Select(w =>
                    $"{w.TableName.Trim().ToLowerInvariant()}|{w.Detail.Trim().ToLowerInvariant()}"
                )
                .ToHashSet();

            foreach (
                var key in ConstraintKeys(before, capabilities)
                    .Where(k => !IsConstraintRemovalSelected(k, selected, capabilities))
                    .Where(k => !IsWarnedForeignKey(k))
            )
            {
                if (!remaining.Remove(key))
                {
                    outcome.Violations.Add($"constraint lost after a failed sync: {key}");
                }
            }

            return outcome;

            // 外部キーのキー（FK|子|親|列ペア|名前|参照アクション）が、事前に警告した外部キーか
            bool IsWarnedForeignKey(string key)
            {
                var parts = key.Split('|');
                return parts[0] == "FK" && warnedForeignKeys.Contains($"{parts[1]}|{parts[4]}");
            }
        }

        var after = await ImportAsync(dialect, ct).ConfigureAwait(false);
        var afterRows = await CountRowsAsync(dialect, after, ct).ConfigureAwait(false);
        var afterDiff = new SchemaDiffService().Compute(
            after.Entities,
            after.Relationships,
            targetEntities,
            targetRelationships,
            capabilities
        );
        // 選択できない案内項目（同期できない差分を知らせるだけ）は差分として数えない。
        // キーは件数で数える＝名前も参照アクションも同じ外部キーが 2 本ある表（SQLite）では同じキーの項目が 2 つ出て、
        // その片方だけを選ぶと 1 本だけ消えるのが正しい（集合で比べると「選んだ項目が残った」と区別できない）
        var afterCounts = CountKeys(afterDiff.Items.Where(i => i.IsSelectable), capabilities);
        outcome.AfterDiff.AddRange(
            afterDiff.Items.Select(i =>
                $"{i.Kind}: {i.Description} [{string.Join(" ", Keys(i, capabilities))}]"
            )
        );
        var beforeCounts = CountKeys(diff.Items.Where(i => i.IsSelectable), capabilities);

        // 表明 2: 選んだ項目は消えている（同じキーの項目は選んだ数だけ減っている）
        foreach (
            var (key, count) in CountKeys(
                selected.Where(i => !allowedToRemain.Contains(i)),
                capabilities
            )
        )
        {
            if (CountOf(afterCounts, key) > CountOf(beforeCounts, key) - count)
            {
                outcome.Violations.Add($"selected item remains: {key}");
            }
        }

        // 表明 2b: 同期の前には無かった差分が増えていない（変えていない外部キー・制約が巻き添えで消える形）
        // 例外: 列を足した表の列順の差分（足した列は末尾に付き、次の同期で列順の差分が出るのが仕様＝CLAUDE.md の列順同期）
        var addedColumnTables = selected
            .Where(s => s.Kind == SchemaDiffKind.AddColumn)
            .Select(s => $"{SchemaDiffKind.ReorderColumns}|{s.TableName.Trim().ToLowerInvariant()}")
            .ToHashSet();

        foreach (var (key, count) in afterCounts)
        {
            if (count > CountOf(beforeCounts, key) && !addedColumnTables.Contains(key))
            {
                outcome.Violations.Add($"new difference after sync: {key}");
            }
        }

        // 表明 3: 選ばなかった項目は残っている（選んだ項目から必然的に消えるものは除く）
        foreach (
            var (key, count) in CountKeys(
                unselected.Where(i =>
                    i.IsSelectable && !IsImpliedBySelection(i, selected, capabilities)
                ),
                capabilities
            )
        )
        {
            if (CountOf(afterCounts, key) < count)
            {
                outcome.Violations.Add($"unselected item was applied: {key}");
            }
        }

        // 表明 4: 列の属性は、選んだ項目が述べた変更以外は変わらない
        CheckColumnAttributes(before, after, selected, plan, capabilities, outcome);

        // 表明 5: 行数と取込の警告
        foreach (var (table, rows) in beforeRows)
        {
            if (afterRows.TryGetValue(table, out var rowsAfter) && rowsAfter != rows)
            {
                outcome.Violations.Add($"row count changed: {table} {rows} -> {rowsAfter}");
            }
        }

        var beforeWarnings = before
            .Warnings.Select(w => $"{w.Kind}|{w.TableName.ToLowerInvariant()}")
            .ToHashSet();

        foreach (
            var warning in after
                .Warnings.Select(w => $"{w.Kind}|{w.TableName.ToLowerInvariant()}")
                .Distinct()
                .Where(w => !beforeWarnings.Contains(w))
        )
        {
            outcome.Violations.Add($"new import warning: {warning}");
        }

        return outcome;
    }

    /// <summary>項目の照合キーを件数で数える</summary>
    private static Dictionary<string, int> CountKeys(
        IEnumerable<SchemaDiffItem> items,
        SyncDialectCapabilities capabilities
    )
    {
        var counts = new Dictionary<string, int>();

        foreach (var key in items.SelectMany(i => Keys(i, capabilities)))
        {
            counts[key] = CountOf(counts, key) + 1;
        }

        return counts;
    }

    /// <summary>キーの件数（無ければ 0）</summary>
    private static int CountOf(Dictionary<string, int> counts, string key) =>
        counts.TryGetValue(key, out var count) ? count : 0;

    /// <summary>一意制約の追加・削除の照合キー（列集合＝大文字小文字・順序を無視）</summary>
    private static string UniqueConstraintKey(
        SchemaDiffKind kind,
        string table,
        IEnumerable<string> columns
    ) =>
        $"{kind}|{table}|"
        + string.Join(",", columns.Select(c => c.ToLowerInvariant()).Order(StringComparer.Ordinal));

    /// <summary>差分項目の照合キー（大文字小文字を無視・外部キーは追加側 + と削除側 - に分ける）</summary>
    internal static IEnumerable<string> Keys(
        SchemaDiffItem item,
        SyncDialectCapabilities capabilities
    )
    {
        var table = item.TableName.Trim().ToLowerInvariant();
        var column = item.ColumnName?.Trim().ToLowerInvariant();

        switch (item.Kind)
        {
            case SchemaDiffKind.AddColumn
            or SchemaDiffKind.DropColumn
            or SchemaDiffKind.AlterColumn
            or SchemaDiffKind.SetColumnDescription:
                yield return $"{item.Kind}|{table}|{column}";
                break;
            case SchemaDiffKind.AddUniqueConstraint or SchemaDiffKind.DropUniqueConstraint:
                yield return UniqueConstraintKey(item.Kind, table, item.UniqueConstraintColumns);
                break;
            case SchemaDiffKind.AlterUniqueConstraint:
                yield return UniqueConstraintKey(
                    SchemaDiffKind.AddUniqueConstraint,
                    table,
                    item.UniqueConstraintColumns
                );

                if (item.ReplacedUniqueConstraint is { } replacedUnique)
                {
                    yield return UniqueConstraintKey(
                        SchemaDiffKind.DropUniqueConstraint,
                        table,
                        replacedUnique.UniqueConstraintColumns
                    );
                }

                break;
            case SchemaDiffKind.AddForeignKey:
                yield return ForeignKeyKey("+", item, capabilities);
                break;
            case SchemaDiffKind.DropForeignKey:
                yield return ForeignKeyKey("-", item, capabilities);
                break;
            case SchemaDiffKind.AlterForeignKey:
                yield return ForeignKeyKey("+", item, capabilities);

                if (item.ReplacedForeignKey is { } replaced)
                {
                    yield return ForeignKeyKey("-", replaced, capabilities);
                }

                break;
            default:
                yield return $"{item.Kind}|{table}";
                break;
        }
    }

    private static string ForeignKeyKey(
        string side,
        SchemaDiffItem item,
        SyncDialectCapabilities capabilities
    )
    {
        var child = item.ChildEntity!.TableName.Trim();
        var parent = item.ParentEntity!.TableName.Trim();
        var pairs = string.Join(
            ",",
            item.ForeignKeyColumnPairs.Select(p =>
                $"{p.ParentColumn.ToLowerInvariant()}>{p.ChildColumn.ToLowerInvariant()}"
            )
        );
        var name = capabilities.PersistsForeignKeyConstraintNames
            ? ConstraintNames
                .EffectiveForeignKey(
                    item.ForeignKeyName,
                    child,
                    parent,
                    capabilities.IdentifierLengthLimit
                )
                .ToLowerInvariant()
            : string.Empty;

        // 同じ列ペアに外部キーが 2 本ある図（名前を持たない SQLite）でも区別できるよう、参照アクションも含める
        var actions = item.Relationship is { } relationship
            ? $"{capabilities.EffectiveOnDelete(relationship.OnDelete)}/{capabilities.EffectiveOnUpdate(relationship.OnUpdate)}"
            : string.Empty;

        return $"FK{side}|{child.ToLowerInvariant()}|{parent.ToLowerInvariant()}|{pairs}|{name}|{actions}";
    }

    /// <summary>
    /// DB の制約（外部キー・一意制約・主キー）の照合キーの一覧（同じキーが 2 本ある外部キーも数えるため多重集合）
    /// </summary>
    private static List<string> ConstraintKeys(
        SchemaImportResult schema,
        SyncDialectCapabilities capabilities
    )
    {
        var keys = new List<string>();
        var names = ConstraintNames.ResolveForeignKeyNames(
            schema.Entities,
            schema.Relationships,
            capabilities.IdentifierLengthLimit
        );

        foreach (var relationship in schema.Relationships)
        {
            var parent = schema.Entities.FirstOrDefault(e => e.Id == relationship.SourceEntityId);
            var child = schema.Entities.FirstOrDefault(e => e.Id == relationship.TargetEntityId);

            if (
                parent is null
                || child is null
                || ForeignKeyColumnPairResolver.Resolve(relationship, parent, child)
                    is not { } pairs
            )
            {
                continue;
            }

            var name = capabilities.PersistsForeignKeyConstraintNames
                ? names[relationship].ToLowerInvariant()
                : string.Empty;
            keys.Add(
                $"FK|{child.TableName.Trim().ToLowerInvariant()}|{parent.TableName.Trim().ToLowerInvariant()}|"
                    + string.Join(
                        ",",
                        pairs.Select(p =>
                            $"{p.ParentColumn.ToLowerInvariant()}>{p.ChildColumn.ToLowerInvariant()}"
                        )
                    )
                    + $"|{name}|{capabilities.EffectiveOnDelete(relationship.OnDelete)}/{capabilities.EffectiveOnUpdate(relationship.OnUpdate)}"
            );
        }

        foreach (var entity in schema.Entities)
        {
            var table = entity.TableName.Trim().ToLowerInvariant();
            var key = entity.GetPrimaryKeyColumnsInOrder();

            if (key.Count > 0)
            {
                keys.Add(
                    $"PK|{table}|{string.Join(",", key.Select(c => c.Name.ToLowerInvariant()))}"
                );
            }

            foreach (var unique in entity.UniqueConstraints)
            {
                var columns = unique
                    .ColumnIds.Select(id => entity.Columns.FirstOrDefault(c => c.Id == id)?.Name)
                    .OfType<string>()
                    .Select(n => n.ToLowerInvariant())
                    .Order(StringComparer.Ordinal);
                keys.Add($"UQ|{table}|{string.Join(",", columns)}");
            }
        }

        return keys;
    }

    /// <summary>
    /// 制約の照合キーが、選んだ項目で消えてよいものか（その制約の削除・作り直し・主キー変更を選んだ、または
    /// その制約の表・列の削除を選んだ）
    /// </summary>
    private static bool IsConstraintRemovalSelected(
        string key,
        List<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities
    )
    {
        var parts = key.Split('|');
        var tables = new HashSet<string> { parts[1] };
        var columns = new HashSet<string>();

        if (parts[0] == "FK")
        {
            tables.Add(parts[2]);

            foreach (var pair in parts[3].Split(','))
            {
                var ends = pair.Split('>');
                columns.Add($"{parts[2]}|{ends[0]}");
                columns.Add($"{parts[1]}|{ends[1]}");
            }
        }
        else
        {
            foreach (var column in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                columns.Add($"{parts[1]}|{column}");
            }
        }

        foreach (var item in selected)
        {
            var table = item.TableName.Trim().ToLowerInvariant();

            switch (item.Kind)
            {
                case SchemaDiffKind.DropTable when tables.Contains(table):
                    return true;
                case SchemaDiffKind.DropColumn
                    when columns.Contains($"{table}|{item.ColumnName!.Trim().ToLowerInvariant()}"):
                    return true;
                case SchemaDiffKind.AlterPrimaryKey when parts[0] == "PK" && parts[1] == table:
                    return true;
                case SchemaDiffKind.DropUniqueConstraint
                or SchemaDiffKind.AlterUniqueConstraint
                    when parts[0] == "UQ"
                        && Keys(item, capabilities)
                            .Any(k => k == $"DropUniqueConstraint|{parts[1]}|{parts[2]}"):
                    return true;
                case SchemaDiffKind.DropForeignKey
                or SchemaDiffKind.AlterForeignKey
                    when parts[0] == "FK"
                        && Keys(item, capabilities).Any(k => k == "FK-" + key[2..]):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 選び方の形（主キー・一意制約の構成列の削除を、それを前提にする変更を選ばずに選んだか）を返す
    /// </summary>
    private static IEnumerable<string> SelectionShapes(
        SchemaImportResult before,
        List<SchemaDiffItem> selected
    )
    {
        foreach (var drop in selected.Where(i => i.Kind == SchemaDiffKind.DropColumn))
        {
            var table = before.Entities.FirstOrDefault(e =>
                e.TableName.Trim().Equals(drop.TableName.Trim(), StringComparison.OrdinalIgnoreCase)
            );
            var column = table?.Columns.FirstOrDefault(c =>
                c.Name.Equals(drop.ColumnName, StringComparison.OrdinalIgnoreCase)
            );

            if (table is null || column is null)
            {
                continue;
            }

            if (
                column.IsPrimaryKey
                && !selected.Any(s =>
                    s.Kind == SchemaDiffKind.AlterPrimaryKey
                    && s.TableName.Trim()
                        .Equals(table.TableName.Trim(), StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                yield return FuzzShape.DropPrimaryKeyColumnAlone;
            }

            if (
                table.UniqueConstraints.Any(u => u.ColumnIds.Contains(column.Id))
                && !selected.Any(s =>
                    s.Kind == SchemaDiffKind.DropUniqueConstraint
                    && s.TableName.Trim()
                        .Equals(table.TableName.Trim(), StringComparison.OrdinalIgnoreCase)
                    && s.UniqueConstraintColumns.Any(c =>
                        c.Equals(column.Name, StringComparison.OrdinalIgnoreCase)
                    )
                )
            )
            {
                yield return FuzzShape.DropUniqueMemberAlone;
            }
        }
    }

    /// <summary>選ばなかった項目が、選んだ項目から必然的に消えるものか（規則は <see cref="SyncPlanFuzzRunner"/> の remarks）</summary>
    private static bool IsImpliedBySelection(
        SchemaDiffItem item,
        List<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities
    )
    {
        var tables = TablesOf(item);
        var touchedTables = selected
            .Where(s => s.Kind is SchemaDiffKind.AddTable or SchemaDiffKind.DropTable)
            .Select(s => s.TableName.Trim().ToLowerInvariant())
            .ToHashSet();

        if (tables.Any(touchedTables.Contains))
        {
            return true;
        }

        var touchedColumns = selected
            .Where(s => s.Kind is SchemaDiffKind.AddColumn or SchemaDiffKind.DropColumn)
            .Select(s =>
                $"{s.TableName.Trim().ToLowerInvariant()}|{s.ColumnName!.Trim().ToLowerInvariant()}"
            )
            .ToHashSet();

        if (ColumnsOf(item).Any(touchedColumns.Contains))
        {
            return true;
        }

        var table = item.TableName.Trim().ToLowerInvariant();

        // 主キーと同じ列集合の一意制約を持てない方言では、選んだ主キーの変更で主キーと同じ列集合になる一意制約は消える
        if (
            item.Kind == SchemaDiffKind.DropUniqueConstraint
            && !capabilities.SupportsUniqueConstraintOnPrimaryKey
            && selected.Any(s =>
                s.Kind == SchemaDiffKind.AlterPrimaryKey
                && s.TableName.Trim()
                    .Equals(item.TableName.Trim(), StringComparison.OrdinalIgnoreCase)
                && s.Entity!.GetPrimaryKeyColumnsInOrder()
                    .Select(c => c.Name.Trim().ToLowerInvariant())
                    .ToHashSet()
                    .SetEquals(
                        item.UniqueConstraintColumns.Select(c => c.Trim().ToLowerInvariant())
                    )
            )
        )
        {
            return true;
        }

        if (
            item.Kind == SchemaDiffKind.ReorderColumns
            && selected.Any(s => TablesOf(s).Contains(table))
        )
        {
            return true;
        }

        if (item.Kind == SchemaDiffKind.AlterColumn)
        {
            foreach (var pk in selected.Where(s => s.Kind == SchemaDiffKind.AlterPrimaryKey))
            {
                if (
                    pk.TableName.Trim()
                        .Equals(item.TableName.Trim(), StringComparison.OrdinalIgnoreCase)
                    && pk.Entity!.Columns.Any(c =>
                        c.IsPrimaryKey
                        && c.Name.Equals(item.ColumnName, StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static HashSet<string> TablesOf(SchemaDiffItem item)
    {
        var tables = new HashSet<string> { item.TableName.Trim().ToLowerInvariant() };

        if (item.ChildEntity is { } child)
        {
            tables.Add(child.TableName.Trim().ToLowerInvariant());
        }

        if (item.ParentEntity is { } parent)
        {
            tables.Add(parent.TableName.Trim().ToLowerInvariant());
        }

        // 外部キーの作り直しは、作り直す前の外部キー（DB の定義）の表にも関わる
        if (item.ReplacedForeignKey is { } replaced)
        {
            tables.UnionWith(TablesOf(replaced));
        }

        return tables;
    }

    private static IEnumerable<string> ColumnsOf(SchemaDiffItem item)
    {
        var table = item.TableName.Trim().ToLowerInvariant();

        if (item.ColumnName is { } column)
        {
            yield return $"{table}|{column.Trim().ToLowerInvariant()}";
        }

        foreach (var c in item.UniqueConstraintColumns)
        {
            yield return $"{table}|{c.Trim().ToLowerInvariant()}";
        }

        foreach (var c in item.ReplacedUniqueConstraint?.UniqueConstraintColumns ?? [])
        {
            yield return $"{table}|{c.Trim().ToLowerInvariant()}";
        }

        if (item.ChildEntity is { } child && item.ParentEntity is { } parent)
        {
            foreach (var pair in item.ForeignKeyColumnPairs)
            {
                yield return $"{child.TableName.Trim().ToLowerInvariant()}|{pair.ChildColumn.Trim().ToLowerInvariant()}";
                yield return $"{parent.TableName.Trim().ToLowerInvariant()}|{pair.ParentColumn.Trim().ToLowerInvariant()}";
            }
        }

        // 外部キーの作り直しは、作り直す前の外部キー（DB の定義）の列にも関わる
        if (item.ReplacedForeignKey is { } replaced)
        {
            foreach (var replacedColumn in ColumnsOf(replaced))
            {
                yield return replacedColumn;
            }
        }
    }

    /// <summary>表明 4（列の属性は、選んだ項目が述べた変更以外は変わらない）</summary>
    private static void CheckColumnAttributes(
        SchemaImportResult before,
        SchemaImportResult after,
        List<SchemaDiffItem> selected,
        SyncPlan plan,
        SyncDialectCapabilities capabilities,
        FuzzSeedOutcome outcome
    )
    {
        var afterTables = after.Entities.ToDictionary(
            e => e.TableName.Trim(),
            StringComparer.OrdinalIgnoreCase
        );
        var rebuildWarned = plan
            .Warnings.Where(w => w.Kind == SyncPlanWarningKind.TableRebuildDropsColumnAttribute)
            .Select(w => w.TableName.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var oldTable in before.Entities)
        {
            if (!afterTables.TryGetValue(oldTable.TableName.Trim(), out var newTable))
            {
                continue;
            }

            var tableName = oldTable.TableName.Trim();
            var pkChange = selected.FirstOrDefault(s =>
                s.Kind == SchemaDiffKind.AlterPrimaryKey
                && s.TableName.Trim().Equals(tableName, StringComparison.OrdinalIgnoreCase)
            );

            var oldKey = string.Join(
                ",",
                oldTable.GetPrimaryKeyColumnsInOrder().Select(c => c.Name.ToLowerInvariant())
            );
            var newKey = string.Join(
                ",",
                newTable.GetPrimaryKeyColumnsInOrder().Select(c => c.Name.ToLowerInvariant())
            );

            if (pkChange is null && oldKey != newKey)
            {
                outcome.Violations.Add(
                    $"primary key changed without selection: {tableName} ({oldKey}) -> ({newKey})"
                );
            }

            foreach (var oldColumn in oldTable.Columns)
            {
                var newColumn = newTable.Columns.FirstOrDefault(c =>
                    c.Name.Equals(oldColumn.Name, StringComparison.OrdinalIgnoreCase)
                );

                if (newColumn is null)
                {
                    continue;
                }

                var alter = selected.FirstOrDefault(s =>
                    s.Kind == SchemaDiffKind.AlterColumn
                    && s.TableName.Trim().Equals(tableName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        s.ColumnName,
                        oldColumn.Name,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
                var where = $"{tableName}.{oldColumn.Name}";

                if (!capabilities.AreSameType(oldColumn.DataType, newColumn.DataType))
                {
                    var declared =
                        alter is not null
                        && !capabilities.AreSameType(
                            alter.OldColumn!.DataType,
                            alter.Column!.DataType
                        );

                    if (!declared)
                    {
                        outcome.Violations.Add(
                            $"type changed without selection: {where} {oldColumn.DataType} -> {newColumn.DataType}"
                        );
                    }
                }

                if (oldColumn.IsNullable != newColumn.IsNullable)
                {
                    var declaredByAlter =
                        alter is not null
                        && !alter.Column!.IsComputed
                        && alter.OldColumn!.IsNullable != alter.Column.IsNullable;
                    var declaredByKey =
                        pkChange?.Entity!.Columns.Any(c =>
                            c.IsPrimaryKey
                            && c.Name.Equals(oldColumn.Name, StringComparison.OrdinalIgnoreCase)
                        ) == true
                        && !newColumn.IsNullable;

                    if (!declaredByAlter && !declaredByKey)
                    {
                        outcome.Violations.Add(
                            $"nullability changed without selection: {where} {oldColumn.IsNullable} -> {newColumn.IsNullable}"
                        );
                    }
                }

                if (
                    oldColumn.IsComputed != newColumn.IsComputed
                    && !rebuildWarned.Contains(tableName)
                )
                {
                    outcome.Violations.Add(
                        $"computed flag changed without a warning: {where} {oldColumn.IsComputed} -> {newColumn.IsComputed}"
                    );
                }
            }
        }
    }

    private static async Task<SchemaImportResult> ImportAsync(
        SyncPlanFuzzDialect dialect,
        CancellationToken ct
    ) =>
        await dialect
            .Provider.SchemaImporter.ImportAsync(
                dialect.ImportConnectionString,
                DbCommands.DefaultTimeoutSeconds,
                ct
            )
            .ConfigureAwait(false);

    private static async Task<Dictionary<string, long>> CountRowsAsync(
        SyncPlanFuzzDialect dialect,
        SchemaImportResult schema,
        CancellationToken ct
    )
    {
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        foreach (var entity in schema.Entities)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {dialect.Quote(entity.TableName)}";
            var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            counts[entity.TableName.Trim()] = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        return counts;
    }

    /// <summary>取り込んだ図を新しい Guid 空間へ写す（リレーションの列ペアも張り替える）</summary>
    internal static (List<Entity>, List<Relationship>) CloneWithFreshIds(SchemaImportResult live)
    {
        var entityIdMap = new Dictionary<Guid, Guid>();
        var columnIdMap = new Dictionary<Guid, Guid>();
        var entities = new List<Entity>();

        foreach (var entity in live.Entities)
        {
            var clone = entity.Clone(preserveId: false);
            entityIdMap[entity.Id] = clone.Id;

            for (var i = 0; i < entity.Columns.Count; i++)
            {
                columnIdMap[entity.Columns[i].Id] = clone.Columns[i].Id;
            }

            entities.Add(clone);
        }

        var relationships = live
            .Relationships.Select(r => new Relationship
            {
                SourceEntityId = entityIdMap[r.SourceEntityId],
                TargetEntityId = entityIdMap[r.TargetEntityId],
                Type = r.Type,
                ColumnPairs = [.. r.ColumnPairs.Select(p => p.Clone(columnIdMap))],
                ConstraintName = r.ConstraintName,
                OnDelete = r.OnDelete,
                OnUpdate = r.OnUpdate,
            })
            .ToList();

        return (entities, relationships);
    }

    private static string FirstLine(string? text) => text?.Split('\n', 2)[0].Trim() ?? string.Empty;
}
