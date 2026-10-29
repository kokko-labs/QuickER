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

    /// <summary>決定的な行の Id（乱数の種なら null。そのとき <see cref="Seed"/> は使わない）</summary>
    public string? ScenarioId { get; init; }

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

    /// <summary>計画（計画を作れたときだけ。警告の内訳を数えるのに使う）</summary>
    public SyncPlan? Plan { get; set; }

    /// <summary>違反ではないが数えておく注（表明の例外が当たった回数など）</summary>
    public List<string> Notes { get; } = [];

    /// <summary>報告用の 1 件分の文</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"--- {Dialect} {(ScenarioId is null ? $"seed {Seed}" : $"scenario {ScenarioId}")} (selection {Selection}, committed {Committed}) ---"
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

    /// <summary>静的な規則が失敗を予言したのにコミットした違反の文の頭</summary>
    public const string StaticRuleCommittedPrefix =
        "static rule predicted a failure but the sync committed:";

    /// <summary>静的な規則が予言し、実際に失敗した注</summary>
    public const string StaticRulePredictedNote = "static-rule-predicted";

    /// <summary>静的な規則が予言せず、実際に失敗した注</summary>
    public const string StaticRuleMissedNote = "static-rule-missed";

    /// <summary>静的な規則が D9（失敗の前に外して後ろで付け直す制約）を予言した注（実 DB で消失が出たかを添える）</summary>
    public const string StaticD9Note = "static-d9";

    /// <summary>
    /// 1 つの種を流す。生成器・ランナーの予期しない例外はその種の違反（<see cref="SeedThrewPrefix"/>）として記録し、
    /// ほかの種の結果を止めない（例外でテストごと止まると、それより前の種の違反が報告されずに隠れる）。取り消しは止める
    /// </summary>
    /// <param name="dialect">方言名</param>
    /// <param name="seed">種</param>
    /// <param name="run">1 つの種を流す本体（普段は <see cref="RunAsync"/>）</param>
    /// <param name="scenarioId">決定的な行の Id（乱数の種なら null）</param>
    public static async Task<FuzzSeedOutcome> RunGuardedAsync(
        string dialect,
        int seed,
        Func<Task<FuzzSeedOutcome>> run,
        string? scenarioId = null
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
                ScenarioId = scenarioId,
                Error = ex.ToString(),
            };
            outcome.Violations.Add(
                $"{SeedThrewPrefix} {ex.GetType().Name}: {FirstLine(ex.Message)}"
            );
            return outcome;
        }
    }

    /// <summary>1 つの種を流して結果を返す（表明の違反は <see cref="FuzzSeedOutcome.Violations"/> に入る）</summary>
    public static Task<FuzzSeedOutcome> RunAsync(
        SyncPlanFuzzDialect dialect,
        int seed,
        CancellationToken ct
    ) =>
        RunCaseAsync(
            dialect,
            new SyncPlanFuzzGenerator(new Random(seed), dialect),
            new FuzzSeedOutcome { Seed = seed, Dialect = dialect.Name },
            ct
        );

    /// <summary>
    /// 1 つのケース（乱数の種か決定的な行）を流して結果を返す（表明の違反は <see cref="FuzzSeedOutcome.Violations"/> に入る）
    /// </summary>
    /// <param name="dialect">方言</param>
    /// <param name="generator">ケースの作り方</param>
    /// <param name="outcome">結果を書き込む先（種・方言・行の Id を入れて渡す）</param>
    /// <param name="ct">取り消し</param>
    public static async Task<FuzzSeedOutcome> RunCaseAsync(
        SyncPlanFuzzDialect dialect,
        ISyncPlanFuzzCase generator,
        FuzzSeedOutcome outcome,
        CancellationToken ct
    )
    {
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

        outcome.Plan = plan;
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

        // 静的な規則（SyncPlanStaticRules）を取り込んだ DB の図と実際の計画に当てる（規則の正しさの突き合わせ）
        var staticVerdict = SyncPlanStaticRules.Evaluate(
            dialect.Name,
            before.Entities,
            before.Relationships,
            selected,
            plan,
            capabilities
        );

        var result = await provider
            .SyncExecutor.ExecuteAsync(dialect.Settings, outcome.Script, ct)
            .ConfigureAwait(false);
        outcome.Committed = result.Committed;
        outcome.Error = result.Error;

        // 突き合わせ：規則が DB の断る文を予言したのにコミットしたら、規則か計画の読み方が誤っている（違反にする）。
        // 予言せずに失敗した種は注として数えるだけ（規則が見ない失敗は多い）
        if (staticVerdict.PredictedFailure is { } predicted)
        {
            if (result.Committed)
            {
                outcome.Violations.Add($"{StaticRuleCommittedPrefix} {predicted}");
            }
            else
            {
                outcome.Notes.Add(StaticRulePredictedNote);
            }
        }
        else if (
            !result.Committed
            && capabilities.SupportsAlterColumn
            && capabilities.SupportsForeignKeyAlter
        )
        {
            // 再構築の方言（SQLite）は規則が文の並びを判定しないので数えない
            outcome.Notes.Add(StaticRuleMissedNote);
        }

        // 表明 1: 失敗するなら事前に警告が出ている
        if (!result.Committed)
        {
            if (plan.Warnings.Count == 0)
            {
                outcome.Violations.Add($"failed without a warning: {FirstLine(result.Error)}");
            }

            // 表明 1b: 失敗しても、制約は消えてよい理由のあるものだけが消えている（DDL を暗黙コミットする方言の部分適用で
            // 外部キー・一意制約・主キーが消えたまま残る形）。規則は FailedSyncConstraintLosses
            var failedState = await ImportAsync(dialect, ct).ConfigureAwait(false);
            var (losses, notes) = FailedSyncConstraintLosses(
                before,
                failedState,
                selected,
                plan,
                capabilities,
                result
            );
            outcome.Violations.AddRange(losses);
            outcome.Notes.AddRange(notes);

            // 突き合わせ（D9）：静的な規則が「失敗の文より前に外し、後ろで付け直す制約」を予言したら、実 DB でも表明 1b の消失が
            // 出ているかを数える（測るだけ）
            if (
                staticVerdict.Violations.Any(v =>
                    v.StartsWith("static D9", StringComparison.Ordinal)
                )
            )
            {
                outcome.Notes.Add(
                    losses.Count > 0 ? $"{StaticD9Note}:observed" : $"{StaticD9Note}:not-observed"
                );
            }

            return outcome;
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

    /// <summary>
    /// 外部キーの列ペアの対応の集合の文（「親の列&gt;子の列」の組を並べ替えて連ねる）。製品の差分は複合外部キーの列ペアを並びを見ずに
    /// 比べ、参照先のキーの並びを要する方言は付け直しをキーの並びにする（CLAUDE.md）ので、キーは並びでなく対応の集合で比べる。
    /// 親の列の集合と子の列の集合を別々に比べると対応の取り違えを見逃すので、組のまま並べ替える
    /// </summary>
    internal static string PairSetText(IEnumerable<(string Parent, string Child)> pairs) =>
        string.Join(
            ",",
            pairs
                .Select(p => $"{p.Parent.ToLowerInvariant()}>{p.Child.ToLowerInvariant()}")
                .Order(StringComparer.Ordinal)
        );

    private static string ForeignKeyKey(
        string side,
        SchemaDiffItem item,
        SyncDialectCapabilities capabilities
    )
    {
        var child = item.ChildEntity!.TableName.Trim();
        var parent = item.ParentEntity!.TableName.Trim();
        var pairs = PairSetText(
            item.ForeignKeyColumnPairs.Select(p => (p.ParentColumn, p.ChildColumn))
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
                    + PairSetText(pairs.Select(p => (p.ParentColumn, p.ChildColumn)))
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

    /// <summary>違反の文の頭（表明 1b）</summary>
    public const string ConstraintLostPrefix = "constraint lost after a failed sync:";

    /// <summary>作り直しを選んだ外部キーに、候補キーの警告の例外が重なった注</summary>
    public const string WarnedAlterForeignKeyNote = "warned-alter-foreign-key-exempt";

    /// <summary>
    /// 表明 1b：失敗した実行の後で取り込み直した DB について、同期の前にあった制約（外部キー・一意制約・主キー）のうち、
    /// 消えてよい理由の無いまま消えたものを違反の文にする。
    /// </summary>
    /// <remarks>
    /// <para>制約ごとの規則（番号は違反の文に書く）：</para>
    /// <list type="number">
    ///   <item>その制約の削除（DropForeignKey・DropUniqueConstraint）を選んだ制約は、消えていてよい。</item>
    ///   <item>作り直し（AlterForeignKey・AlterUniqueConstraint）を選んだ制約は、旧か新の形のどちらかで残っている。</item>
    ///   <item>主キーの変更を選んだ表の主キーは、旧か新の形のどちらかで残っている（目標が主キーなしなら、無いのも新の形）。</item>
    ///   <item>表・列の削除（DropTable・DropColumn）でしか消えてよい理由の無い制約は、その表・列が失敗の後の DB から
    ///   実際に消えているときだけ、消えていてよい。</item>
    ///   <item>それ以外の制約は残っている。</item>
    /// </list>
    /// <para>
    /// 例外は、付け直しが候補キーを証明できないと事前に警告した外部キーだけ（列の集合が合わない付け直しは、取り込んでいない
    /// 索引で通る構成があるので仕様として警告して実行する。並びだけの違いは計画が参照先のキーの並びへ並べ替えるので、警告の
    /// 対象にならない）。この例外が作り直しを選んだ外部キー（規則 2）に重なったものは、違反にせず
    /// <see cref="WarnedAlterForeignKeyNote"/> の注として数える（消えた理由＝失敗の文がその外部キー自身を名指ししたか
    /// 〔own-failure〕、別の失敗の巻き添えか〔collateral〕を添える）。P2-2 の夜間の MySQL 1,500 種で重なりは 7 種
    /// （172・358・708・719・764・1091・1301）で、すべて own-failure＝受け入れた部分適用の範囲なので、規則 2 は掛けない
    /// （扱いの見直しは修正 A のときに判断する）。
    /// </para>
    /// <para>
    /// 違反の文には、計画の警告がその制約の表を名指ししていたか（<c>warned</c>）と、失敗した文より前に成功した文が
    /// その制約の名前（外部キー）か表（それ以外）を DROP していたか（<c>dropped-before-failure</c>）を添える
    /// （部分適用で消えた形を、失敗すると前もって分かっている文が先に DROP を走らせた形＝4m-P6 と見分けるため）。
    /// </para>
    /// </remarks>
    internal static (List<string> Violations, List<string> Notes) FailedSyncConstraintLosses(
        SchemaImportResult before,
        SchemaImportResult failedState,
        IReadOnlyList<SchemaDiffItem> selected,
        SyncPlan plan,
        SyncDialectCapabilities capabilities,
        SchemaSyncResult result
    )
    {
        var violations = new List<string>();
        var notes = new List<string>();
        var remaining = ConstraintKeys(failedState, capabilities);
        var remainingSet = remaining.ToHashSet(StringComparer.Ordinal);
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
        var warnedTables = plan
            .Warnings.Select(w => w.TableName.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var tablesAfter = failedState
            .Entities.Select(e => e.TableName.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var columnsAfter = failedState
            .Entities.SelectMany(e =>
                e.Columns.Select(c =>
                    $"{e.TableName.Trim().ToLowerInvariant()}|{c.Name.ToLowerInvariant()}"
                )
            )
            .ToHashSet(StringComparer.Ordinal);
        var succeededSql = result.Batches.TakeWhile(b => b.Success).Select(b => b.Sql).ToList();

        foreach (var key in ConstraintKeys(before, capabilities))
        {
            // 残っていれば、どの規則でも違反にならない（同じキーの 2 本は 1 本ずつ消費する）
            if (remaining.Remove(key))
            {
                continue;
            }

            var rule = RuleFor(key, selected, capabilities, out var newForm);
            var allowed = rule switch
            {
                1 => true,
                2 => newForm is not null && remainingSet.Contains(newForm),
                3 => newForm is not null
                    ? remainingSet.Contains(newForm)
                    : !remainingSet.Any(k =>
                        k.StartsWith(PrimaryKeyPrefix(key), StringComparison.Ordinal)
                    ),
                4 => RemovalApplied(key, selected, tablesAfter, columnsAfter),
                _ => false,
            };

            if (allowed)
            {
                continue;
            }

            if (IsWarnedForeignKey(key))
            {
                if (rule == 2)
                {
                    // 消えた理由：失敗の文がその外部キー自身（警告を出した付け直し）を名指ししているか、別の失敗の巻き添えか
                    var name = key.Split('|')[4];
                    var cause =
                        name.Length == 0 ? "unnamed"
                        : result.Error?.Contains(name, StringComparison.OrdinalIgnoreCase) == true
                            ? "own-failure"
                        : "collateral";
                    notes.Add($"{WarnedAlterForeignKeyNote}:{cause}");
                }

                continue;
            }

            var parts = key.Split('|');
            // 名前の部分一致で見る（`t0_items` は `t0_items_ref` にも当たる）。違反の注記と既知の失敗の述語の補助なので、厳密には照合しない
            var dropTarget = parts[0] == "FK" && parts[4].Length > 0 ? parts[4] : parts[1];
            var dropped = succeededSql.Any(sql =>
                sql.Contains("DROP", StringComparison.OrdinalIgnoreCase)
                && sql.Contains(dropTarget, StringComparison.OrdinalIgnoreCase)
            );
            violations.Add(
                $"{ConstraintLostPrefix} {key} (rule {rule}; "
                    + $"warned={(warnedTables.Contains(parts[1]) ? "yes" : "no")}; "
                    + $"dropped-before-failure={(dropped ? "yes" : "no")})"
            );
        }

        return (violations, notes);

        // 外部キーのキー（FK|子|親|列ペア|名前|参照アクション）が、事前に警告した外部キーか
        bool IsWarnedForeignKey(string key)
        {
            var parts = key.Split('|');
            return parts[0] == "FK" && warnedForeignKeys.Contains($"{parts[1]}|{parts[4]}");
        }
    }

    /// <summary>主キーのキー（PK|表|列）の、表までの頭</summary>
    private static string PrimaryKeyPrefix(string key) =>
        string.Join('|', key.Split('|').Take(2)) + "|";

    /// <summary>
    /// 制約の照合キーに当てる表明 1b の規則の番号（<see cref="FailedSyncConstraintLosses"/>）。作り直し・主キーの変更なら
    /// 新しい形のキー（主キーを外す変更なら null）を返す
    /// </summary>
    private static int RuleFor(
        string key,
        IReadOnlyList<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        out string? newForm
    )
    {
        newForm = null;
        var parts = key.Split('|');
        var rebuild = 0;

        foreach (var item in selected)
        {
            var table = item.TableName.Trim().ToLowerInvariant();
            var keys = Keys(item, capabilities).ToList();

            switch (item.Kind)
            {
                case SchemaDiffKind.DropUniqueConstraint
                    when parts[0] == "UQ"
                        && keys.Contains($"DropUniqueConstraint|{parts[1]}|{parts[2]}"):
                    return 1;
                case SchemaDiffKind.DropForeignKey
                    when parts[0] == "FK" && keys.Contains("FK-" + key[2..]):
                    return 1;
                case SchemaDiffKind.AlterUniqueConstraint
                    when parts[0] == "UQ"
                        && keys.Contains($"DropUniqueConstraint|{parts[1]}|{parts[2]}"):
                    newForm =
                        "UQ|"
                        + keys.First(k =>
                            k.StartsWith("AddUniqueConstraint|", StringComparison.Ordinal)
                        )["AddUniqueConstraint|".Length..];
                    rebuild = 2;
                    break;
                case SchemaDiffKind.AlterForeignKey
                    when parts[0] == "FK" && keys.Contains("FK-" + key[2..]):
                    newForm =
                        "FK" + keys.First(k => k.StartsWith("FK+", StringComparison.Ordinal))[3..];
                    rebuild = 2;
                    break;
                case SchemaDiffKind.AlterPrimaryKey
                    when parts[0] == "PK" && parts[1] == table && rebuild == 0:
                    var columns = item.Entity?.GetPrimaryKeyColumnsInOrder() ?? [];
                    newForm =
                        columns.Count == 0
                            ? null
                            : $"PK|{table}|{string.Join(",", columns.Select(c => c.Name.ToLowerInvariant()))}";
                    rebuild = 3;
                    break;
            }
        }

        if (rebuild != 0)
        {
            return rebuild;
        }

        return RemovalTargets(key, selected).Any() ? 4 : 5;
    }

    /// <summary>選んだ表・列の削除のうち、その制約の表・列に当たるもの（表＝「表」・列＝「表|列」）</summary>
    private static IEnumerable<string> RemovalTargets(
        string key,
        IReadOnlyList<SchemaDiffItem> selected
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

            if (item.Kind == SchemaDiffKind.DropTable && tables.Contains(table))
            {
                yield return table;
            }
            else if (
                item.Kind == SchemaDiffKind.DropColumn
                && columns.Contains($"{table}|{item.ColumnName!.Trim().ToLowerInvariant()}")
            )
            {
                yield return $"{table}|{item.ColumnName!.Trim().ToLowerInvariant()}";
            }
        }
    }

    /// <summary>その制約に当たる選んだ表・列の削除が、失敗の後の DB で 1 つでも実際に消えているか</summary>
    private static bool RemovalApplied(
        string key,
        IReadOnlyList<SchemaDiffItem> selected,
        HashSet<string> tablesAfter,
        HashSet<string> columnsAfter
    ) =>
        RemovalTargets(key, selected)
            .Any(target =>
                target.Contains('|', StringComparison.Ordinal)
                    ? !columnsAfter.Contains(target)
                    : !tablesAfter.Contains(target)
            );

    /// <summary>
    /// 選び方の形（主キー・一意制約の構成列の削除を、それを前提にする変更を選ばずに選んだか・同じ列の並びの一意制約 2 つの
    /// 片方の削除と、その列の型の変更を一緒に選んだか）を返す（網も使う）
    /// </summary>
    internal static IEnumerable<string> SelectionShapes(
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

        foreach (var drop in selected.Where(i => i.Kind == SchemaDiffKind.DropUniqueConstraint))
        {
            var table = before.Entities.FirstOrDefault(e =>
                e.TableName.Trim().Equals(drop.TableName.Trim(), StringComparison.OrdinalIgnoreCase)
            );
            var members = drop.UniqueConstraintColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sameSet = table
                ?.UniqueConstraints.Select(u =>
                    u.ColumnIds.Select(id => table.Columns.FirstOrDefault(c => c.Id == id)?.Name)
                        .OfType<string>()
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                )
                .Count(set => set.SetEquals(members));

            if (
                table is not null
                && sameSet >= 2
                && selected.Any(s =>
                    s.Kind == SchemaDiffKind.AlterColumn
                    && s.TableName.Trim()
                        .Equals(table.TableName.Trim(), StringComparison.OrdinalIgnoreCase)
                    && members.Contains(s.ColumnName ?? string.Empty)
                )
            )
            {
                yield return FuzzShape.DropDuplicatedUniqueWithTypeChange;
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
            command.CommandText = $"SELECT COUNT(*) FROM {dialect.QuoteTable(entity.TableName)}";
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
