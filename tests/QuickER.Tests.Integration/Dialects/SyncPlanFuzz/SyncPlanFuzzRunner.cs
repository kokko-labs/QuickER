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

    /// <summary>同期の前に取り込んだ図（取り込めたときだけ。期待の行の実 DB の段が読む）</summary>
    public SchemaImportResult? Before { get; set; }

    /// <summary>同期の後（失敗なら失敗の後）に取り込み直した図（期待の行の実 DB の段が読む）</summary>
    public SchemaImportResult? After { get; set; }

    /// <summary>同期の前の差分の項目（選び方を当てた後の選択のまま）</summary>
    public IReadOnlyList<SchemaDiffItem>? DiffItems { get; set; }

    /// <summary>同期の後に取り込み直した図と目標の図の差分の項目（コミットしたときだけ）</summary>
    public IReadOnlyList<SchemaDiffItem>? AfterDiffItems { get; set; }

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
        text.AppendLine("shapes: " + string.Join(", ", Shapes.Order(StringComparer.Ordinal)));
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
/// 計画が名指しして外した項目（<see cref="SyncPlan.ExcludedItems"/>＝警告と一緒に積む。項目そのもので引く）は、選ばなかった項目
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
/// <item>残った表の行数は変わらない。取込の警告の種類（種別と表）は増えない。残った列の NULL の行の数は変わらない
/// （表明 5b・<see cref="NullCountChanges"/>）。残った列の既定値の式は変わらない（表明 5c・<see cref="ColumnDefaultChanges"/>）。
/// 同期の前に発火できたトリガーは、同期の後も発火できる（表明 5d・<see cref="NewlyBrokenTriggers"/>）。</item>
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
            SyncPlanFuzzGenerator.ForSeed(seed, dialect),
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

        foreach (var admin in liveSchema.AdminStatements)
        {
            await dialect.ExecuteAdminAsync(admin, ct).ConfigureAwait(false);
        }

        if (liveSchema.PrepareStatements.Count > 0)
        {
            await dialect
                .ExecuteSetupAsync(
                    string.Join(Environment.NewLine, liveSchema.PrepareStatements),
                    ct
                )
                .ConfigureAwait(false);
        }

        // 表の無い図（期待の行の実 DB の段で、DB を生の SQL だけで作る行）は DDL を流さない
        if (liveSchema.Diagram.Entities.Count > 0)
        {
            await dialect
                .ExecuteSetupAsync(provider.DdlGenerator.Build(liveSchema.Diagram), ct)
                .ConfigureAwait(false);
        }

        var setup = liveSchema
            .InsertStatements.Concat(liveSchema.ComputedColumnStatements)
            .Concat(liveSchema.ColumnDefaultStatements)
            .ToList();

        if (setup.Count > 0)
        {
            await dialect
                .ExecuteSetupAsync(string.Join(Environment.NewLine, setup), ct)
                .ConfigureAwait(false);
        }

        foreach (var raw in liveSchema.RawStatements)
        {
            await dialect.ExecuteRawAsync(raw, ct).ConfigureAwait(false);
        }

        // 表明 5d の同期の前の状態（もとから発火できないトリガーは数えない）
        var brokenTriggersBefore = dialect.BrokenTriggers();
        var before = await ImportAsync(dialect, ct).ConfigureAwait(false);
        outcome.Before = before;
        await generator.OnImportedAsync(dialect, before, ct).ConfigureAwait(false);
        var beforeRows = await ProfileRowsAsync(dialect, before, ct).ConfigureAwait(false);

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
        outcome.DiffItems = diff.Items;
        var selected = diff.Items.Where(i => i.IsSelected).ToList();
        var unselected = diff.Items.Where(i => !i.IsSelected).ToList();
        outcome.Selected.AddRange(selected.Select(i => $"{i.Kind}: {i.Description}"));
        outcome.Unselected.AddRange(unselected.Select(i => $"{i.Kind}: {i.Description}"));
        outcome.Shapes.UnionWith(SelectionShapes(before, selected));

        SyncPlan plan;

        try
        {
            // 同期の画面と同じ写し方で取込の結果から計画の入力を作る（写す項目を画面と割らない）
            plan = new SyncPlanner().BuildPlan(
                diff.Items,
                capabilities,
                SyncPlanContext.FromImport(before)
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

        // 計画が名指しして外した項目（SyncPlan.ExcludedItems＝外す箇所は警告と一緒に積む）は、選ばなかった項目として扱う
        // （警告が出ているので残ってよい）。警告なしに計画から消えた項目は従来どおり「選んだのに残った」違反になる。列の変更は
        // NULL 許容化だけを外して型の変更を残すことがあるので、残ってよい側にだけ入れる（型の変更は選んだ変更として表明 4 で見る）
        var plannedKeys = plan
            .Sections.SelectMany(s => s.Items)
            .Concat(plan.Rebuilds.SelectMany(r => r.SourceItems))
            .Concat(plan.Reorders.SelectMany(r => r.SourceItems))
            .SelectMany(i => Keys(i, capabilities))
            .ToHashSet();
        var allowedToRemain = selected
            .Where(i => plan.ExcludedItems.Contains(i, ReferenceEqualityComparer.Instance))
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
            capabilities,
            liveSchema.DataFailures
        );

        // 静的な規則のうち DB に依らない規則（警告の正しさ W1・前寄せ M1・新しい外部キーの追加と付け直しの並び）の違反は、実 DB の
        // 結果にも加える（DB の断る文の並びの違反は実 DB の表明 1・1b が見るので加えない）
        outcome.Violations.AddRange(
            staticVerdict.Violations.Where(v =>
                v.StartsWith(SyncPlanStaticRules.W1Prefix, StringComparison.Ordinal)
                || v.StartsWith(SyncPlanStaticRules.M1Prefix, StringComparison.Ordinal)
                || v.StartsWith(SyncPlanStaticRules.D9OrderPrefix, StringComparison.Ordinal)
            )
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

        // 表明 1: 失敗するなら事前に警告が出ている（ケースが宣言した、既存の行で断られる文そのものの失敗は除く）
        if (!result.Committed)
        {
            if (
                plan.Warnings.Count == 0
                && !IsDeclaredDataFailure(
                    result,
                    liveSchema.DataFailures,
                    statementwise: !capabilities.TransactionalDdl
                )
            )
            {
                outcome.Violations.Add($"failed without a warning: {FirstLine(result.Error)}");
            }

            // 表明 1b: 失敗しても、制約は消えてよい理由のあるものだけが消えている（DDL を暗黙コミットする方言の部分適用で
            // 外部キー・一意制約・主キーが消えたまま残る形）。規則は FailedSyncConstraintLosses
            var failedState = await ImportAsync(dialect, ct).ConfigureAwait(false);
            outcome.After = failedState;
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
        outcome.After = after;
        var afterRows = await ProfileRowsAsync(dialect, after, ct).ConfigureAwait(false);
        var afterDiff = new SchemaDiffService().Compute(
            after.Entities,
            after.Relationships,
            targetEntities,
            targetRelationships,
            capabilities
        );
        outcome.AfterDiffItems = afterDiff.Items;
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

        // 例外: 再構築が式を落として普通の列にした計算列（計画が TableRebuildDropsColumnAttribute で警告した表＝表明 4 の「計算列の
        // 印」と同じ扱い）の列の変更。差分は計算列かを DB の列で決めるので、図の計算列は普通の列として比べられる
        var rebuildWarnedTables = plan
            .Warnings.Where(w => w.Kind == SyncPlanWarningKind.TableRebuildDropsColumnAttribute)
            .Select(w => w.TableName.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var computedLost = before
            .Entities.SelectMany(e =>
                e.Columns.Where(c => c.IsComputed)
                    .Select(c =>
                        (
                            Table: e.TableName.Trim().ToLowerInvariant(),
                            Column: c.Name.ToLowerInvariant()
                        )
                    )
            )
            .Where(c =>
                rebuildWarnedTables.Contains(c.Table)
                && after.Entities.Any(e =>
                    e.TableName.Trim().ToLowerInvariant() == c.Table
                    && e.Columns.Any(a => a.Name.ToLowerInvariant() == c.Column && !a.IsComputed)
                )
            )
            .Select(c => $"{SchemaDiffKind.AlterColumn}|{c.Table}|{c.Column}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (key, count) in afterCounts)
        {
            if (
                count > CountOf(beforeCounts, key)
                && !addedColumnTables.Contains(key)
                && !computedLost.Contains(key)
            )
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
            if (afterRows.TryGetValue(table, out var rowsAfter) && rowsAfter.Rows != rows.Rows)
            {
                outcome.Violations.Add(
                    $"row count changed: {table} {rows.Rows} -> {rowsAfter.Rows}"
                );
            }
        }

        // 表明 5b: 残った列の NULL の数は変わらない
        outcome.Violations.AddRange(
            NullCountChanges(before, after, beforeRows, afterRows, selected)
        );

        // 表明 5c: 残った列の既定値の式は変わらない
        outcome.Violations.AddRange(ColumnDefaultChanges(before, after, selected, plan.Warnings));

        // 表明 5d: 同期の前に発火できたトリガーは、コミットした同期の後も発火できる
        outcome.Violations.AddRange(
            NewlyBrokenTriggers(brokenTriggersBefore, dialect.BrokenTriggers())
        );

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

        outcome.Violations.AddRange(LostImportWarnings(before, after, selected, plan.Warnings));
        return outcome;
    }

    /// <summary>違反の文の頭（表明 5d の発火できなくなったトリガー）</summary>
    public const string BrokenTriggerPrefix =
        "trigger can no longer fire after the committed sync:";

    /// <summary>
    /// 表明 5d（発火できなくなったトリガー）：同期の前には発火できたのに、コミットした同期の後に発火できないトリガーを違反の文にする
    /// </summary>
    /// <remarks>
    /// テーブル再構築（SQLite）はトリガーの本文を検証しないので、消した列・表を参照するトリガーがあっても同期はコミットし、後で表へ
    /// 書いたときに初めて断られる。判定は方言（<see cref="SyncPlanFuzzDialect.BrokenTriggers"/>＝製品の実行器と同じ部品）に任せ、
    /// 判定を持たない方言（<see langword="null"/>）は比べない。消えたトリガー（表の削除で一緒に消えた）は数えない。
    /// </remarks>
    internal static IEnumerable<string> NewlyBrokenTriggers(
        IReadOnlySet<string>? before,
        IReadOnlySet<string>? after
    )
    {
        if (before is null || after is null)
        {
            return [];
        }

        return after
            .Where(name => !before.Contains(name))
            .Order(StringComparer.Ordinal)
            .Select(name => $"{BrokenTriggerPrefix} {name}");
    }

    /// <summary>違反の文の頭（表明 5 の消えた取込警告）</summary>
    public const string LostImportWarningPrefix = "import warning lost:";

    /// <summary>
    /// 表明 5（消えた取込警告）：同期の前の取込警告のうち、選んだ項目で説明できないものが同期の後に消えていたら違反の文にする
    /// </summary>
    /// <remarks>
    /// <para>
    /// 取込が捨てた表の中の物（<see cref="SchemaImportWarningKinds.RebuildLosesTableContent"/> が真の種別＝図に無い列・外部キー）は、
    /// 同期の後の取込でも警告が残るはず。消えたなら、捨てた物そのものが DB から消えている（テーブル再構築が live の図から表を作り直して
    /// データごと消した形）。説明できるのは、その表の削除か、警告が名指しした列（Subject）の削除を選んだときだけ。
    /// </para>
    /// <para>
    /// ほかの種別（列の属性・取込で補正した物など）は、その表に触れる選んだ項目があるか、作り直しで列の属性を落とすと計画が警告した
    /// （<see cref="SyncPlanWarningKind.TableRebuildDropsColumnAttribute"/>。暗黙の外し直しで作り直す表を含む）なら説明できるとみなす。
    /// </para>
    /// <para>
    /// 限界：外部キーの警告（範囲外・除外した表への外部キー）の Subject は外部キーの名前で列ではないので、その外部キーの子の列の削除を
    /// 選んで外部キーが正当に消えた形は説明できず、違反の文になる。生成器・期待の行はこの形（取込が捨てた外部キーの子の列の削除）を
    /// 作らないので今は起きない。作るときは、警告の外部キーの子の列を取込の結果から引いて説明に足す。
    /// </para>
    /// </remarks>
    internal static IEnumerable<string> LostImportWarnings(
        SchemaImportResult before,
        SchemaImportResult after,
        IReadOnlyCollection<SchemaDiffItem> selected,
        IReadOnlyCollection<SyncPlanWarning> planWarnings
    )
    {
        // 再構築で列の属性を落とすと計画が警告した表
        var attributeDroppedTables = planWarnings
            .Where(w => w.Kind == SyncPlanWarningKind.TableRebuildDropsColumnAttribute)
            .Select(w => w.TableName.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var remaining = after
            .Warnings.Select(w =>
                $"{w.Kind}|{w.TableName.Trim().ToLowerInvariant()}|{w.Subject.Trim().ToLowerInvariant()}"
            )
            .ToHashSet(StringComparer.Ordinal);

        foreach (var warning in before.Warnings)
        {
            var table = warning.TableName.Trim().ToLowerInvariant();
            var subject = warning.Subject.Trim().ToLowerInvariant();

            if (remaining.Contains($"{warning.Kind}|{table}|{subject}"))
            {
                continue;
            }

            var tableItems = selected
                .Where(i => i.TableName.Trim().ToLowerInvariant() == table)
                .ToList();
            var explained = SchemaImportWarningKinds.RebuildLosesTableContent(warning.Kind)
                ? tableItems.Any(i =>
                    i.Kind == SchemaDiffKind.DropTable
                    || i.Kind == SchemaDiffKind.DropColumn
                        && i.ColumnName?.Trim().ToLowerInvariant() == subject
                )
                : tableItems.Count > 0 || attributeDroppedTables.Contains(table);

            if (!explained)
            {
                yield return $"{LostImportWarningPrefix} {warning.Kind}({warning.TableName}: {warning.Subject}) disappeared without a selected item that removes it";
            }
        }
    }

    /// <summary>
    /// 失敗した実行の断られた文が、ケースが宣言した「既存の行で断られる文」（<see cref="FuzzLiveSchema.DataFailures"/>）のどれかか
    /// </summary>
    /// <param name="result">実行の結果</param>
    /// <param name="declared">ケースが宣言した文</param>
    /// <param name="statementwise">
    /// 文ごとに流す方言か（DDL を暗黙コミットする MySQL・Oracle）。真なら失敗したバッチの文で、偽ならエラー文で見る（SQL Server・
    /// PostgreSQL は失敗したバッチの文を持たず、SQLite は失敗したバッチがスクリプト全体なので、文では見分けられない）
    /// </param>
    /// <remarks>
    /// <para>
    /// 文で見るとき：文の形（外部キー・一意制約・主キーの追加、列の変更、列の追加）と、宣言の名前（外部キー・一意制約の名前、表と列の
    /// 名前）を含むこと（大文字小文字を無視・識別子として区切られた語）。MySQL の列の変更は動的 SQL（<c>SET @sql = …</c> →
    /// <c>PREPARE</c> → <c>EXECUTE</c>）で、断られるのは名前を持たない <c>EXECUTE</c> なので、その前の最後の <c>SET @sql</c> の文で見る。
    /// </para>
    /// <para>
    /// エラー文で見るとき：断られた理由の語（重複・NULL・長さ・式の依存）と、宣言の名前（一意制約・主キー・外部キーの名前、表と列の
    /// 名前）か、SQLite の再構築の表の列（<c>{表}_quicker_rebuild.{列}</c>）を含むこと。PostgreSQL の式の依存のエラー文は名前を
    /// 持たないので、理由の語だけで見る。SQLite の外部キー違反は、違反した子の表がすべて宣言した追加の子の表であること。
    /// </para>
    /// <para>
    /// ほかの文の失敗まで表明 1 の外へ出さないため、文の形・理由の語と名前の両方で決める（規則表 <see cref="SyncPlanFuzzOracleTests"/>
    /// の自己検査の行が、名前だけが偶然一致する別の文の失敗を一致させないことも固定する）。
    /// </para>
    /// </remarks>
    internal static bool IsDeclaredDataFailure(
        SchemaSyncResult result,
        IReadOnlyCollection<FuzzDataFailure> declared,
        bool statementwise
    )
    {
        if (result.Committed || declared.Count == 0)
        {
            return false;
        }

        var failedIndex = result.Batches.FindIndex(b => !b.Success);
        var failed = failedIndex < 0 ? null : result.Batches[failedIndex];
        var statement = statementwise && failed is not null ? failed.Sql : string.Empty;
        var error = result.Error ?? failed?.Error ?? string.Empty;

        // MySQL の動的 SQL：名前を持つのは EXECUTE の前の最後の SET @sql の文
        if (
            statement.TrimStart().StartsWith("EXECUTE", StringComparison.OrdinalIgnoreCase)
            || statement.TrimStart().StartsWith("PREPARE", StringComparison.OrdinalIgnoreCase)
        )
        {
            statement =
                result
                    .Batches.Take(failedIndex)
                    .LastOrDefault(b =>
                        b.Sql.TrimStart().StartsWith("SET @sql", StringComparison.OrdinalIgnoreCase)
                    )
                    ?.Sql
                ?? statement;
        }

        // SQLite の実行器は再構築の後の外部キー違反を、違反した子の表の一覧で知らせる（文言は実行の UI カルチャの resx。表の一覧の
        // 前までを書式から取る＝英語の固定文だと ja-JP の実行で一致しない）
        var violationTables = QuickER
            .Provider.Resources.Strings.Sync_Error_SqliteForeignKeyViolation.Split("{0}")[0]
            .TrimEnd();

        if (error.Contains(violationTables, StringComparison.Ordinal))
        {
            var violated = error[
                (
                    error.IndexOf(violationTables, StringComparison.Ordinal)
                    + violationTables.Length
                )..
            ]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            return violated.Length > 0
                && violated.All(t =>
                    declared.Any(d =>
                        d.Kind == FuzzDataFailureKind.AddForeignKey
                        && string.Equals(t, d.Table, StringComparison.OrdinalIgnoreCase)
                    )
                );
        }

        return declared.Any(d =>
            statement.Length > 0 ? MatchesStatement(d, statement) : MatchesError(d, error)
        );

        // 文の形と宣言の名前
        static bool MatchesStatement(FuzzDataFailure d, string statement) =>
            d.Kind switch
            {
                FuzzDataFailureKind.AddForeignKey => Mentions(statement, d.Name)
                    && Has(statement, "FOREIGN KEY"),
                FuzzDataFailureKind.AlterColumn or FuzzDataFailureKind.AlterComputedSource => !Has(
                    statement,
                    "FOREIGN KEY"
                )
                    && (Has(statement, "MODIFY") || Has(statement, "ALTER COLUMN"))
                    && Mentions(statement, d.Table)
                    && Mentions(statement, d.Name),
                FuzzDataFailureKind.AddUniqueConstraint => Has(statement, "UNIQUE")
                    && !Has(statement, "DROP")
                    && Mentions(statement, d.Name),
                FuzzDataFailureKind.AddPrimaryKey => System.Text.RegularExpressions.Regex.IsMatch(
                    statement,
                    @"\bADD\s+(CONSTRAINT\s+\S+\s+)?PRIMARY\s+KEY\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        | System.Text.RegularExpressions.RegexOptions.CultureInvariant
                )
                    && !Has(statement, "DROP")
                    && Mentions(statement, d.Table),
                FuzzDataFailureKind.AddColumn => Has(statement, "ADD")
                    && !Has(statement, "CONSTRAINT")
                    && !Has(statement, "MODIFY")
                    && Mentions(statement, d.Table)
                    && Mentions(statement, d.Name),
                _ => false,
            };

        // 断られた理由の語と宣言の名前（SQLite は再構築の表の列）
        static bool MatchesError(FuzzDataFailure d, string error) =>
            d.Kind switch
            {
                FuzzDataFailureKind.AddForeignKey => Mentions(error, d.Name),
                FuzzDataFailureKind.AddUniqueConstraint or FuzzDataFailureKind.AddPrimaryKey =>
                    IsDuplicate(error)
                        && (
                            Mentions(error, d.Name)
                            || d.Column is not null && Rebuilt(error, d.Table, d.Column)
                        ),
                FuzzDataFailureKind.AlterColumn or FuzzDataFailureKind.AddColumn => IsNullOrLength(
                    error
                )
                    && (
                        // ガードの失敗は NOT NULL の文言とガードの列の名前だけで見る（表と列の名前の語の照合は「.」を含む名前で別の列に
                        // 当たり、理由の語「null」はガードの一時表の名前にも含まれる）
                        IsGuard(error)
                            ? Has(error, "NOT NULL constraint failed")
                                && Guarded(error, d.Table, d.Name)
                            : Mentions(error, d.Table) && Mentions(error, d.Name)
                                || Rebuilt(error, d.Table, d.Name)
                    )
                    // 名前を持たないエラー文（PostgreSQL の長さ・SQLite の ADD COLUMN）は、その文の種類だけが出す文言で見る
                    || d.Kind == FuzzDataFailureKind.AlterColumn
                        && Has(error, "value too long for type")
                    || d.Kind == FuzzDataFailureKind.AddColumn
                        && Has(error, "Cannot add a NOT NULL column"),
                FuzzDataFailureKind.AlterComputedSource => Has(error, "used by a generated column")
                    || Has(error, "is dependent on column") && Mentions(error, d.Name),
                _ => false,
            };

        static bool Has(string text, string word) =>
            text.Contains(word, StringComparison.OrdinalIgnoreCase);

        // 重複で断られた（SQL Server・PostgreSQL・SQLite の文言）
        static bool IsDuplicate(string error) =>
            Has(error, "duplicate")
            || Has(error, "could not create unique index")
            || Has(error, "UNIQUE constraint failed");

        // NULL を含む・値が長すぎる・行のある表で断られた（SQL Server・PostgreSQL・SQLite の文言）
        static bool IsNullOrLength(string error) =>
            Has(error, "null")
            || Has(error, "truncated")
            || Has(error, "too long")
            || Has(error, "non-empty");

        // SQLite の再構築の表の列（エラー文は「{表}_quicker_rebuild.{列}」で名指しする）
        static bool Rebuilt(string error, string table, string column) =>
            Has(error, $"{table}_quicker_rebuild.{column}");

        // SQLite の rowid の別名へ写す列の NULL のガード（エラー文は「quicker_rowid_null_guard.{表}.{列}」で名指しする）
        static bool IsGuard(string error) =>
            Has(
                error,
                QuickER.Provider.Sqlite.SqliteSyncScriptBuilder.RowidAliasNullGuardTable + "."
            );

        // 名前に「.」を含んでも前から全体で照合し、後ろに名前の続き（英数字・下線・ドット）が来る形は別の列として断る
        static bool Guarded(string error, string table, string column) =>
            System.Text.RegularExpressions.Regex.IsMatch(
                error,
                System.Text.RegularExpressions.Regex.Escape(
                    $"{QuickER.Provider.Sqlite.SqliteSyncScriptBuilder.RowidAliasNullGuardTable}.{table}.{column}"
                ) + @"(?![\p{L}\p{Nd}_.])",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.CultureInvariant
            );

        // 識別子として名前を含む（引用符・括弧・空白・ドットで区切られた語として現れる）
        static bool Mentions(string text, string name) =>
            System.Text.RegularExpressions.Regex.IsMatch(
                text,
                $@"(?<![\p{{L}}\p{{Nd}}_]){System.Text.RegularExpressions.Regex.Escape(name)}(?![\p{{L}}\p{{Nd}}_])",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.CultureInvariant
            );
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

    /// <summary>列が空でないと通らない変更の警告が名指しした制約の消失を受け入れた注</summary>
    public const string WarnedEmptyColumnChangeNote = "warned-empty-column-change-exempt";

    /// <summary>既存の行で断られうる文の警告が名指しした制約の消失を受け入れた注</summary>
    public const string WarnedRowDependentChangeNote = "warned-row-dependent-change-exempt";

    /// <summary>
    /// 表明 1b：失敗した実行の後で取り込み直した DB について、同期の前にあった制約（外部キー・一意制約・主キー）のうち、
    /// 消えてよい理由の無いまま消えたものを違反の文にする。
    /// </summary>
    /// <remarks>
    /// <para>制約ごとの規則（番号は違反の文に書く）：</para>
    /// <list type="number">
    ///   <item>その制約の削除（DropForeignKey・DropUniqueConstraint）を選んだ制約は、消えていてよい。</item>
    ///   <item>作り直し（AlterForeignKey・AlterUniqueConstraint）を選んだ制約は、旧か新の形のどちらかで残っている。主キーが置き換える一意制約（計画の警告
    ///   <see cref="SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey"/> が名指しした表・列集合）も作り直しとみなし、新しい形＝同じ列集合の
    ///   主キーが残っていればよい（警告の無い一意制約・列集合の違う主キーは数えない）。</item>
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

        // 主キーが置き換えると計画が名指しした一意制約（警告 UniqueConstraintReplacedByPrimaryKey の表と「名前 (列, …)」の列集合）
        var replacedUniques = plan
            .Warnings.Where(w => w.Kind == SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey)
            .Select(w =>
            {
                var open = w.Detail.LastIndexOf('(');
                var columns = w.Detail[(open + 1)..]
                    .TrimEnd(')')
                    .Split(
                        ',',
                        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                    )
                    .Select(c => c.ToLowerInvariant())
                    .Order(StringComparer.Ordinal);
                return $"UQ|{w.TableName.Trim().ToLowerInvariant()}|{string.Join(",", columns)}";
            })
            .ToHashSet(StringComparer.Ordinal);

        // 列が空でないと通らない変更・既存の行で断られうる文の前後で外して付け直すと名指しした制約（「表|制約名」。主キーの制約名は
        // PRIMARY KEY）と、そのうち一意制約の照合キー（計画が外す一意制約の項目の名前で引く）
        var (emptyColumnWarnedNames, emptyColumnWarned) = WarnedConstraints(
            SyncPlanWarningKind.ConstraintRebuildAroundEmptyColumnChange
        );
        var (rowDependentWarnedNames, rowDependentWarned) = WarnedConstraints(
            SyncPlanWarningKind.ConstraintRebuildAroundRowDependentChange
        );

        foreach (var key in ConstraintKeys(before, capabilities))
        {
            // 残っていれば、どの規則でも違反にならない（同じキーの 2 本は 1 本ずつ消費する）
            if (remaining.Remove(key))
            {
                continue;
            }

            var rule = RuleFor(key, selected, capabilities, out var newForm);

            // 主キーが置き換える一意制約（計画の警告が名指しした表・列集合）は、作り直しと同じく新しい形＝同じ列集合の主キーで
            // 残っていればよい（規則 2）
            var replacedByPrimaryKey = rule == 5 && replacedUniques.Contains(key);

            if (replacedByPrimaryKey)
            {
                rule = 2;
            }

            var allowed = rule switch
            {
                1 => true,
                2 when replacedByPrimaryKey => remainingSet.Any(k =>
                    IsPrimaryKeyOnSameColumns(k, key)
                ),
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

            // 列が空でないと通らない変更の前後で外して付け直すと名指しした制約（警告 ConstraintRebuildAroundEmptyColumnChange）は、
            // データの都合で列の変更が断られると外れたまま残る（docs の約束どおりの部分適用）。名指しした制約だけを受け入れる
            if (emptyColumnWarned.Contains(key) || IsNamed(key, emptyColumnWarnedNames))
            {
                notes.Add(WarnedEmptyColumnChangeNote);
                continue;
            }

            // 既存の行で断られうる文の前後で外して付け直すと名指しした制約（警告 ConstraintRebuildAroundRowDependentChange）も、
            // データの都合でその文が断られると外れたまま残る（docs の約束どおりの部分適用）
            if (rowDependentWarned.Contains(key) || IsNamed(key, rowDependentWarnedNames))
            {
                notes.Add(WarnedRowDependentChangeNote);
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

        // 警告の種類が名指しした制約（「表|制約名」）と、そのうち計画が外す一意制約の照合キー
        (HashSet<string> Names, HashSet<string> UniqueKeys) WarnedConstraints(
            SyncPlanWarningKind kind
        )
        {
            var names = plan
                .Warnings.Where(w => w.Kind == kind)
                .Select(w =>
                {
                    var open = w.Detail.IndexOf(" (", StringComparison.Ordinal);
                    var name = open < 0 ? w.Detail : w.Detail[..open];
                    return $"{w.TableName.Trim().ToLowerInvariant()}|{name.Trim().ToLowerInvariant()}";
                })
                .ToHashSet(StringComparer.Ordinal);
            var uniqueKeys = plan
                .Sections.Where(s => s.Kind == SchemaDiffKind.DropUniqueConstraint)
                .SelectMany(s => s.Items)
                .Where(i =>
                    names.Contains(
                        $"{i.TableName.Trim().ToLowerInvariant()}|{UniqueConstraintNaming.Resolve(i.UniqueConstraintName, i.TableName, i.UniqueConstraintColumns, capabilities.IdentifierLengthLimit).Trim().ToLowerInvariant()}"
                    )
                )
                .SelectMany(i => Keys(i, capabilities))
                .Select(k => "UQ|" + string.Join('|', k.Split('|').Skip(1)))
                .ToHashSet(StringComparer.Ordinal);
            return (names, uniqueKeys);
        }

        // 外部キー（FK|子|親|列ペア|名前|参照アクション）・主キー（PK|表|列）が、名指しの集合（「表|制約名」）に入るか
        static bool IsNamed(string key, HashSet<string> names)
        {
            var parts = key.Split('|');
            return parts[0] switch
            {
                "FK" => names.Contains($"{parts[1]}|{parts[4]}"),
                "PK" => names.Contains($"{parts[1]}|primary key"),
                _ => false,
            };
        }

        // 外部キーのキー（FK|子|親|列ペア|名前|参照アクション）が、事前に警告した外部キーか
        bool IsWarnedForeignKey(string key)
        {
            var parts = key.Split('|');
            return parts[0] == "FK" && warnedForeignKeys.Contains($"{parts[1]}|{parts[4]}");
        }
    }

    /// <summary>主キーのキー（PK|表|列の並び）が、一意制約のキー（UQ|表|並べ替えた列）と同じ表・同じ列集合か</summary>
    private static bool IsPrimaryKeyOnSameColumns(string primaryKey, string uniqueKey)
    {
        var pk = primaryKey.Split('|');
        var uq = uniqueKey.Split('|');
        return pk[0] == "PK"
            && pk[1] == uq[1]
            && string.Join(",", pk[2].Split(',').Order(StringComparer.Ordinal)) == uq[2];
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
                        // DB の列が計算列なら NULL 許容は運ばない（図だけが計算列なら普通の列の変更＝製品の差分と同じく DB の列で決める）
                        && !alter.OldColumn!.IsComputed
                        && alter.OldColumn.IsNullable != alter.Column!.IsNullable;
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

    /// <summary>違反の文の頭（表明 5b の列の NULL の数）</summary>
    public const string NullCountChangedPrefix = "null count changed:";

    /// <summary>
    /// 表明 5b（残った列の NULL の数は変わらない）：コミットした同期の前後で、両方にある表の両方にある列（計算列を除く）の NULL の
    /// 行の数が違えば違反の文にする
    /// </summary>
    /// <remarks>
    /// 同期はどの方言でも値を書き換えない。型の変更も NULL を NULL のまま写し、NOT NULL 化は NULL があれば DB が断る。NULL の数が
    /// 変わるのは、値が黙って書き換わったとき（SQLite の再構築で rowid の別名になった列へ写した NULL が採番の値に変わる形）だけ。
    /// 型を変えた列も比べる（型の変更と同時に別名にする形を拾うため）。比べないのは、選んだ表の追加・削除の表（同じ名前で作り直すと
    /// 中身が別物になる）、選んだ列の追加・削除の列（同じ名前で足し直すと NULL で埋まる）、前後どちらかで計算列の列（値は式が決める）。
    /// </remarks>
    internal static IEnumerable<string> NullCountChanges(
        SchemaImportResult before,
        SchemaImportResult after,
        IReadOnlyDictionary<string, RowProfile> beforeRows,
        IReadOnlyDictionary<string, RowProfile> afterRows,
        IReadOnlyList<SchemaDiffItem> selected
    )
    {
        var rebuiltTables = selected
            .Where(i => i.Kind is SchemaDiffKind.AddTable or SchemaDiffKind.DropTable)
            .Select(i => i.TableName.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var replacedColumns = selected
            .Where(i =>
                i.Kind is SchemaDiffKind.AddColumn or SchemaDiffKind.DropColumn
                && i.ColumnName is not null
            )
            .Select(i => (i.TableName.Trim(), i.ColumnName!.Trim()))
            .ToHashSet(TableColumnComparer.Instance);

        foreach (var entity in before.Entities)
        {
            var table = entity.TableName.Trim();
            var afterEntity = after.Entities.FirstOrDefault(e =>
                string.Equals(e.TableName.Trim(), table, StringComparison.OrdinalIgnoreCase)
            );

            if (
                afterEntity is null
                || rebuiltTables.Contains(table)
                || !beforeRows.TryGetValue(table, out var rowsBefore)
                || !afterRows.TryGetValue(table, out var rowsAfter)
            )
            {
                continue;
            }

            foreach (var column in entity.Columns.Where(c => !c.IsComputed))
            {
                var name = column.Name.Trim();
                var afterColumn = afterEntity.Columns.FirstOrDefault(c =>
                    string.Equals(c.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)
                );

                if (
                    afterColumn is null
                    || afterColumn.IsComputed
                    || replacedColumns.Contains((table, name))
                    || !rowsBefore.Nulls.TryGetValue(name, out var nullsBefore)
                    || !rowsAfter.Nulls.TryGetValue(name, out var nullsAfter)
                    || nullsBefore == nullsAfter
                )
                {
                    continue;
                }

                yield return $"{NullCountChangedPrefix} {table}.{name} {nullsBefore} -> {nullsAfter}";
            }
        }
    }

    /// <summary>違反の文の頭（表明 5c の列の既定値）</summary>
    public const string ColumnDefaultChangedPrefix = "column default changed:";

    /// <summary>
    /// 表明 5c（残った列の既定値の式は変わらない）：コミットした同期の前後で、両方にある表の両方にある列の、取込の既定値の式
    /// （<see cref="SchemaImportResult.ColumnDefaults"/>）が違えば違反の文にする（消えたか現れたかも含む）
    /// </summary>
    /// <remarks>
    /// 同期の列の変更は、図に無い既定値を実行時のカタログから読み直して保つ（<c>src/QuickER.Provider/CLAUDE.md</c>「列の変更が保つ属性」）。
    /// 型を変えた列も比べる。比べないのは、選んだ表の追加・削除の表と、選んだ列の追加・削除の列（表明 5b と同じ）と、計画が
    /// <see cref="SyncPlanWarningKind.TableRebuildDropsColumnAttribute"/> で名指しした表（SQLite の再構築は既定値を落とすと告げて進める）。
    /// </remarks>
    internal static IEnumerable<string> ColumnDefaultChanges(
        SchemaImportResult before,
        SchemaImportResult after,
        IReadOnlyList<SchemaDiffItem> selected,
        IReadOnlyList<SyncPlanWarning> warnings
    )
    {
        var skippedTables = selected
            .Where(i => i.Kind is SchemaDiffKind.AddTable or SchemaDiffKind.DropTable)
            .Select(i => i.TableName.Trim())
            .Concat(
                warnings
                    .Where(w => w.Kind == SyncPlanWarningKind.TableRebuildDropsColumnAttribute)
                    .Select(w => w.TableName.Trim())
            )
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var replacedColumns = selected
            .Where(i =>
                i.Kind is SchemaDiffKind.AddColumn or SchemaDiffKind.DropColumn
                && i.ColumnName is not null
            )
            .Select(i => (i.TableName.Trim(), i.ColumnName!.Trim()))
            .ToHashSet(TableColumnComparer.Instance);
        var beforeDefaults = Defaults(before);
        var afterDefaults = Defaults(after);

        foreach (var entity in before.Entities)
        {
            var table = entity.TableName.Trim();
            var afterEntity = after.Entities.FirstOrDefault(e =>
                string.Equals(e.TableName.Trim(), table, StringComparison.OrdinalIgnoreCase)
            );

            if (afterEntity is null || skippedTables.Contains(table))
            {
                continue;
            }

            foreach (var column in entity.Columns)
            {
                var name = column.Name.Trim();

                if (
                    replacedColumns.Contains((table, name))
                    || !afterEntity.Columns.Any(c =>
                        string.Equals(c.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    continue;
                }

                var expressionBefore = beforeDefaults.GetValueOrDefault((table, name));
                var expressionAfter = afterDefaults.GetValueOrDefault((table, name));

                if (!string.Equals(expressionBefore, expressionAfter, StringComparison.Ordinal))
                {
                    yield return $"{ColumnDefaultChangedPrefix} {table}.{name} {expressionBefore ?? "(none)"} -> {expressionAfter ?? "(none)"}";
                }
            }
        }

        static Dictionary<(string, string), string> Defaults(SchemaImportResult schema)
        {
            var defaults = new Dictionary<(string, string), string>(TableColumnComparer.Instance);

            foreach (var d in schema.ColumnDefaults)
            {
                defaults[(d.TableName.Trim(), d.ColumnName.Trim())] = d.Expression;
            }

            return defaults;
        }
    }

    /// <summary>表 1 つの行の数と、列ごとの NULL の行の数（表明 5・5b）</summary>
    internal sealed record RowProfile(long Rows, IReadOnlyDictionary<string, long> Nulls);

    /// <summary>（表, 列）の組を名前の大文字小文字に依らず比べる</summary>
    private sealed class TableColumnComparer : IEqualityComparer<(string Table, string Column)>
    {
        public static readonly TableColumnComparer Instance = new();

        public bool Equals((string Table, string Column) x, (string Table, string Column) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Table, y.Table)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Column, y.Column);

        public int GetHashCode((string Table, string Column) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Column)
            );
    }

    /// <summary>表ごとに行の数と列ごとの NULL の行の数を 1 つの問い合わせで数える（計算列も数えるが、表明 5b は比べない）</summary>
    private static async Task<Dictionary<string, RowProfile>> ProfileRowsAsync(
        SyncPlanFuzzDialect dialect,
        SchemaImportResult schema,
        CancellationToken ct
    )
    {
        var profiles = new Dictionary<string, RowProfile>(StringComparer.OrdinalIgnoreCase);
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        foreach (var entity in schema.Entities)
        {
            var columns = entity.Columns.Select(c => c.Name).ToList();
            await using var command = connection.CreateCommand();
            // NULL は IS NULL で数える（SQL Server の COUNT(列) は text・ntext・image を受けない）。行が 0 なら SUM は NULL
            command.CommandText =
                "SELECT COUNT(*)"
                + string.Concat(
                    columns.Select(c =>
                        $", SUM(CASE WHEN {dialect.Quote(c)} IS NULL THEN 1 ELSE 0 END)"
                    )
                )
                + $" FROM {dialect.QuoteTable(entity.TableName)}";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            var rows = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
            var nulls = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < columns.Count; i++)
            {
                nulls[columns[i].Trim()] = reader.IsDBNull(i + 1)
                    ? 0
                    : Convert.ToInt64(reader.GetValue(i + 1), CultureInfo.InvariantCulture);
            }

            profiles[entity.TableName.Trim()] = new RowProfile(rows, nulls);
        }

        return profiles;
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
