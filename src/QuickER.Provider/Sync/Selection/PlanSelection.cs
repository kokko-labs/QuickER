using System.Globalization;
using QuickER.Model;
using QuickER.Provider.Sync.Assessment;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;
using QuickER.Provider.Sync.TableRebuilds;

namespace QuickER.Provider.Sync.Selection;

/// <summary>
/// 同期の計画の選択の前半（<see cref="PrepareSelection"/>＝除外の周・作り直しの展開・同期後の状態の差の項目・同期後の事実）
/// </summary>
/// <remarks>
/// 呼ぶのは計画器の入口（<see cref="StepPlanner"/>）。同期後の状態の差の項目を足す関数は <see cref="PostSyncDifferenceItems"/> が持つ。
/// </remarks>
internal static class PlanSelection
{
    /// <summary>
    /// 計画の前半：選んだ項目を外し終え（<see cref="ExcludeUntilStable"/>）、作り直しを展開し、同期後の状態の差の項目を足して、
    /// 選んだ項目の最終形とその同期後の事実（<see cref="PlanStage.Selected"/>）を作る
    /// </summary>
    /// <param name="items">全差分項目（未選択の項目は読まない）</param>
    /// <param name="capabilities">対象方言の同期ケーパビリティ</param>
    /// <param name="context">計画の入力（無ければ <c>null</c>）</param>
    /// <param name="liveSchema">DB の今の図（<paramref name="context"/> から作ったもの）</param>
    /// <param name="warnings">外した項目の名指しの警告を積む先</param>
    /// <param name="excluded">外した項目を積む先</param>
    /// <returns>
    /// 選んだ項目の最終形と、評価の規則が読む事実（テーブル再構築の方言かは <see cref="AssessmentFacts.IsTableRebuildDialect"/>）
    /// </returns>
    /// <remarks>
    /// 返した後に、選んだ項目の最終形へ項目を足さない（同期後の事実は作ったときの並びの写しを持つ）。項目を足す段を入れるなら、
    /// この関数の中で、同期後の事実を作る前に足す。
    /// </remarks>
    internal static PreparedSelection PrepareSelection(
        IEnumerable<SchemaDiffItem> items,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        LiveSchema? liveSchema,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        // ALTER COLUMN も FK の後付けもできない方言はテーブル再構築が必要になる
        var isRebuildDialect = TableRebuildDialects.Requires(capabilities);

        // 選んだ項目を外す規則が読む文脈（選んだ項目に依らないので、除外の周の前に 1 回だけ作る）。テーブル再構築の方言では、
        // 作り直さない表も求める：取込が捨てた表の中の物（図に無い列・外部キー）がある表（作り直すと捨てた物がデータごと黙って
        // 消える）と、DB の外で登録した照合順序・関数を使う物があって作り直せない表（QuickER の接続では作り直しが必ず巻き戻る）
        var selectionContext = new SelectionContext
        {
            Capabilities = capabilities,
            PlanContext = context,
            LiveSchema = liveSchema,
            IsRebuildDialect = isRebuildDialect,
            ImportDiscardedTables = isRebuildDialect
                ? AsReadOnlyTables(CollectImportDiscardedContent(context))
                : EmptyTables,
            RebuildBlockedTables = isRebuildDialect
                ? AsReadOnlyTables(CollectRebuildBlockedTables(context, liveSchema))
                : EmptyTables,
        };

        // 選択済みの項目のみを対象にする（未選択は完全に除外）。計画に載せられない項目を、何も外れなくなるまで繰り返して外し、
        // 名指しする（ExcludeUntilStable）。外部キー・一意制約の作り直しはその後で DROP と ADD の 2 項目へ展開し、以降は通常の差分
        // として扱う（展開の前に判定する＝作り直しを外すときは削除と追加をまとめて外し、削除だけが走って制約が消えたまま残らない）
        var settled = ExcludeUntilStable(
            [.. items.Where(i => i.IsSelected)],
            selectionContext,
            warnings,
            excluded
        );
        var selected = ExpandAlters(settled.Items);

        // 主キーの変更で新しく主キーに加わる NULL 許容の列は、NOT NULL 化を選んでいなくても暗黙に NOT NULL にする
        selected = PostSyncDifferenceItems.InjectPrimaryKeyNotNullChanges(selected, liveSchema);

        // 選んだ表・列の削除で端が消える live の外部キーは、削除を選んでいなくても暗黙に外す
        // （以降の段はこれらを「明示的に外す外部キー」として扱う）
        selected = PostSyncDifferenceItems.InjectVanishingForeignKeyDrops(
            selected,
            liveSchema,
            includeDroppedChildTables: !isRebuildDialect,
            capabilities
        );

        // 選んだ列の削除で構成列が消える live の一意制約も、削除を選んでいなくても暗黙に外す
        // （名指しは評価の規則 AssessmentRules.WarnUniqueConstraintsDroppedWithColumns）
        selected = PostSyncDifferenceItems.InjectVanishingUniqueConstraintDrops(
            selected,
            liveSchema,
            capabilities
        );

        // 主キーと同じ列集合の一意制約を持てない逐次 DDL の方言（Oracle）では、選んだ主キーの変更の新しい主キーと同じ列集合の
        // live の一意制約を、削除を選んでいなくても外す（名指しは評価の規則 AssessmentRules.WarnUniqueConstraintsReplacedByPrimaryKey。
        // 再構築の方言は合成がその一意制約を省く）
        if (PostSyncDifferenceItems.ReplacesUniqueConstraintsWithPrimaryKey(capabilities))
        {
            selected = PostSyncDifferenceItems.InjectUniqueConstraintsReplacedByPrimaryKey(
                selected,
                liveSchema,
                capabilities
            );
        }

        // 選んだ項目の最終形の同期後の事実を 1 つ作り、警告・外し直しの注入・並べ替え・再構築の合成で共有する
        var postSync = PostSyncSchema.From(PlanStage.Selected, selected, liveSchema, capabilities);

        return new PreparedSelection(
            new AssessmentFacts
            {
                Settled = settled,
                PostSync = postSync,
                Live = liveSchema,
                Capabilities = capabilities,
                Context = context,
            },
            selected
        );
    }

    /// <summary>計画の前半（<see cref="PrepareSelection"/>）の結果</summary>
    /// <param name="Facts">評価の規則が読む事実（周が止まったときの状態・選んだ項目の最終形の同期後の事実ほか）</param>
    /// <param name="Selected">選んだ項目の最終形（作り直しを展開し、同期後の状態の差の項目を足した並び）</param>
    internal sealed record PreparedSelection(AssessmentFacts Facts, List<SchemaDiffItem> Selected);

    /// <summary>
    /// 選択済みの作り直し項目を、外す側と付ける側の 2 項目へ展開する（それ以外の項目は順序を保ってそのまま返す）。
    /// <see cref="SchemaDiffKind.AlterForeignKey"/> は DropForeignKey と AddForeignKey へ、
    /// <see cref="SchemaDiffKind.AlterUniqueConstraint"/> は DropUniqueConstraint と AddUniqueConstraint へ展開する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 計画の文（<see cref="SyncStep"/>）にも方言別レンダラーにも作り直しの種別は無いので、ここで展開しないと
    /// 選んだ作り直しが黙って計画から消える。
    /// </para>
    /// <para>
    /// 展開した外部キーの DROP は「明示的に選んだ削除」として扱われるため、暗黙の外し直し（主キー変更・列定義変更に伴う
    /// 自動 DROP / 再 ADD）とは重複せず、旧い定義を付け直すこともない。展開した一意制約の DROP も、明示的に選んだ削除と
    /// 同じく、外部キーを支える索引の喪失（MySQL）・候補キーの喪失の警告・同期後の一意制約の集合の計算に乗る。
    /// 再構築の方言では同じ表の外部キー・一意制約の集合の合成（live − DROP ＋ ADD）にそのまま乗る。
    /// </para>
    /// </remarks>
    internal static List<SchemaDiffItem> ExpandAlters(IEnumerable<SchemaDiffItem> selected)
    {
        var expanded = new List<SchemaDiffItem>();

        foreach (var item in selected)
        {
            if (item.Kind == SchemaDiffKind.AlterUniqueConstraint)
            {
                // 削除と追加の両方へ展開したときだけ、2 つが対になる（外す側の定義の無い作り直しは追加だけ）
                var uniquePair = item.ReplacedUniqueConstraint is null ? null : new ReaddPair();

                if (item.ReplacedUniqueConstraint is { } replaced)
                {
                    expanded.Add(
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.DropUniqueConstraint,
                            TableName = replaced.TableName,
                            Entity = replaced.Entity,
                            UniqueConstraintName = replaced.UniqueConstraintName,
                            UniqueConstraintColumns = replaced.UniqueConstraintColumns,
                            Description = item.Description,
                            PlanMark = PlanItemMark.Expanded(item, uniquePair),
                        }
                    );
                }

                expanded.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.AddUniqueConstraint,
                        TableName = item.TableName,
                        Entity = item.Entity,
                        UniqueConstraintName = item.UniqueConstraintName,
                        UniqueConstraintColumns = item.UniqueConstraintColumns,
                        Description = item.Description,
                        PlanMark = PlanItemMark.Expanded(item, uniquePair),
                    }
                );
                continue;
            }

            if (item.Kind != SchemaDiffKind.AlterForeignKey)
            {
                expanded.Add(item);
                continue;
            }

            var foreignKeyPair = item.ReplacedForeignKey is null ? null : new ReaddPair();

            if (item.ReplacedForeignKey is { } old)
            {
                expanded.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.DropForeignKey,
                        TableName = old.TableName,
                        Entity = old.Entity,
                        ParentEntity = old.ParentEntity,
                        ChildEntity = old.ChildEntity,
                        Relationship = old.Relationship,
                        ForeignKeyName = old.ForeignKeyName,
                        ForeignKeyColumnPairs = old.ForeignKeyColumnPairs,
                        Description = item.Description,
                        PlanMark = PlanItemMark.Expanded(item, foreignKeyPair),
                    }
                );
            }

            expanded.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddForeignKey,
                    TableName = item.TableName,
                    ColumnName = item.ColumnName,
                    Entity = item.Entity,
                    ParentEntity = item.ParentEntity,
                    ChildEntity = item.ChildEntity,
                    Relationship = item.Relationship,
                    ForeignKeyName = item.ForeignKeyName,
                    ForeignKeyColumnPairs = item.ForeignKeyColumnPairs,
                    Description = item.Description,
                    PlanMark = PlanItemMark.Expanded(item, foreignKeyPair),
                }
            );
        }

        return expanded;
    }

    /// <summary>
    /// 選んだ項目のうち、計画に載せられない項目を、何も外れなくなるまで繰り返して外し、名指しする。計画が選んだ項目を外すのは
    /// ここだけ（除外の関数をこの周の外から呼ばない）。
    /// </summary>
    /// <param name="selected">選択済みの項目（外部キー・一意制約の作り直しは展開前）</param>
    /// <param name="context">選んだ項目に依らない文脈（選んだ項目に依らないので呼び出し側が 1 回だけ作る）</param>
    /// <param name="warnings">名指しの警告を積む先</param>
    /// <param name="excluded">外した項目を積む先</param>
    /// <returns>周が止まったときの状態（残す項目と、その並びの同期後の事実）</returns>
    /// <remarks>
    /// <para>
    /// 規則は 2 つの表で宣言し、表の順に流す。規則は取り下げを返すだけで、周が次の規則の前に取り下げを当てる
    /// （<see cref="SelectionWithdrawals.Apply"/>。後ろの規則は前の規則が外した後の並びを受ける）。周の前の表
    /// （<see cref="SelectionRules.BeforeRounds"/>）は 1 回だけ、周ごとの表（<see cref="SelectionRules.EachRound"/>）は何も外れなく
    /// なるまで繰り返す。
    /// </para>
    /// <para>
    /// 規則が読む同期後の事実は、周が状態（<see cref="SelectionState"/>）として渡す。状態は並びごとに 1 つで、取り下げを当てて並びが
    /// 変わるたびに作り直す（周ごとに 1 つにしない＝同じ周の後ろの規則が、前の規則が外す前の事実を読むと、外す項目が変わる）。
    /// </para>
    /// <para>
    /// 列の無い表の追加（<see cref="SelectionRules.ExcludeTablesWithoutColumns"/>）は項目そのものだけで決まるので 1 回だけ外す。外した表の追加は
    /// 選ばなかった項目と同じに扱い、続く段がそれを前提にする項目を外す。
    /// </para>
    /// <para>
    /// 残りの除外は、この順で何も外れなくなるまで繰り返す：名前の重なる表の追加
    /// （<see cref="SelectionRules.ExcludeTablesWithCollidingConstraintNames"/>）・DB のビュー・トリガー・索引を壊す変更
    /// （<see cref="SelectionRules.ExcludeChangesBreakingLiveDependents"/>）・選んでいない変更を前提にする項目
    /// （<see cref="SelectionRules.ExcludeItemsRequiringUnselected"/>）・同期後に列が 1 本も残らない表の列の削除
    /// （<see cref="SelectionRules.ExcludeDropsLeavingNoColumns"/>。依存の除外の後の項目で数えるので、外れた項目は列の追加にも削除にも数えない）・
    /// 作り直さない表を作り直す項目（取込が捨てた物のある表＝<see cref="SelectionRules.ExcludeRebuildsLosingImportDiscardedContent"/>、
    /// DB の外の物で作り直せない表＝<see cref="SelectionRules.ExcludeRebuildsBlockedByExternalDefinition"/>。再構築の方言だけ）・同期後に両端の型が
    /// 食い違う外部キーの追加・作り直し（<see cref="SelectionRules.ExcludeForeignKeysWithPostSyncTypeMismatch"/>。逐次 DDL の方言だけ）・
    /// 子の列が同期後に NOT NULL になる SET NULL の外部キーの追加・作り直し
    /// （<see cref="SelectionRules.ExcludeSetNullForeignKeysOnNotNullColumns"/>。作れない方言だけ）。
    /// </para>
    /// <para>
    /// 繰り返す理由：どの除外の判定も、ほかの選んだ項目を材料にする。名前の重なりの判定（<see cref="RemainingLiveConstraints"/>）は
    /// 選んだ表・列の削除と作り直しで消える制約を「残らない」と数え、依存の除外は選んだ追加と主キーの変更を「同期後に在る」と
    /// 数える。その項目自身が別の除外で外されると、判定が古くなる（主キーの変更を選ばない主キーの列の削除・列が残らない表の
    /// 列の削除・作り直さない表の主キーの変更・型の食い違う外部キーの作り直し）。1 回で止めると、外された削除の制約が残るのに
    /// その名前と重なる追加が、外された主キーの変更を前提にする外部キーの追加が、外されずに DB で断られる。ビューの判定は、
    /// 外した列の追加を前提にする項目を同じ周の依存の除外が外せるよう、その前に置く（依存の除外が列の追加だけを外すと残った
    /// 列の削除がビューを読めなくしうるので、次の周でもう一度試す）。
    /// </para>
    /// <para>
    /// 繰り返しは必ず止まる：周ごとに少なくとも 1 つの選んだ項目を外したときだけ次の周へ進み、外すほど残る制約と足りない前提は
    /// 増えるだけ（外した項目が戻ることはない）で、選んだ項目の数は有限。NULL 許容化だけを外して型の変更を残す列の変更は、
    /// 残した項目が次の周で外れない（NULL 許容化を含まない）。周の数が選んだ項目の数を超えたら、止まる理由が崩れたので例外にする
    /// （途中の計画を返さない）。どの周も、その周で外した項目だけを名指しする。同じ値の名指し（同じ表の項目を 2 つ外したときなど）は
    /// 1 件にまとめ（<see cref="SelectionWithdrawals.Apply"/>）、並びは周の順で決まる。
    /// 1 周目の呼び出しの並びは、除外を 1 回ずつ流すときと同じなので、2 周目に入らない入力では警告の並びも変わらない。
    /// </para>
    /// <para>
    /// 周の中で積む警告は、規則が返した取り下げの名指し（<see cref="SelectionWithdrawals.Apply"/>）だけにする。外した項目は戻らないので、その名指しは
    /// 古くならない。項目を外さずに積む警告（<see cref="AssessmentRules.WarnSetNullForeignKeysOnNotNullColumns"/> など）は、後の周で外れた項目の
    /// 警告が残るので、周の後に、外し終えた項目で 1 回だけ決める。
    /// </para>
    /// </remarks>
    private static SelectionState ExcludeUntilStable(
        List<SchemaDiffItem> selected,
        SelectionContext context,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        var current = selected;
        var state = SelectionState.Of(current, context);

        foreach (var rule in SelectionRules.BeforeRounds)
        {
            Run(rule);
        }

        for (var round = 0; round <= selected.Count; round++)
        {
            var excludedBefore = excluded.Count;

            foreach (var rule in SelectionRules.EachRound)
            {
                Run(rule);
            }

            if (excluded.Count == excludedBefore)
            {
                return state;
            }
        }

        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Excluding the selected items did not settle within {selected.Count + 1} rounds."
            )
        );

        // 規則を 1 つ流す。取り下げがあれば当て、変わった並びから状態を作り直す（次の規則は、外した後の並びとその同期後の事実を読む）。
        // 取り下げが無ければ、次の規則も同じ状態を読む
        void Run(SelectionRule rule)
        {
            var withdrawals = rule(state, context);

            if (withdrawals.Count == 0)
            {
                return;
            }

            current = SelectionWithdrawals.Apply(current, withdrawals, warnings, excluded);
            state = SelectionState.Of(current, context);
        }
    }

    /// <summary>
    /// 取込が図へ入れずに捨てた表の中の物を、表ごとに集める（取込の警告のうち <see cref="SchemaImportWarningKinds.RebuildLosesTableContent"/>
    /// が真の種別。値は警告が名指しした物＝列名・外部キー名、無ければ種別名）
    /// </summary>
    private static Dictionary<string, List<string>> CollectImportDiscardedContent(
        SyncPlanContext? context
    )
    {
        var discarded = new Dictionary<string, List<string>>(SchemaNames.Comparer);

        if (context is null)
        {
            return discarded;
        }

        foreach (
            var warning in context.ImportWarnings.Where(w =>
                SchemaImportWarningKinds.RebuildLosesTableContent(w.Kind)
            )
        )
        {
            var table = SchemaNames.ToStoredForm(warning.TableName);

            if (!discarded.TryGetValue(table, out var subjects))
            {
                subjects = [];
                discarded[table] = subjects;
            }

            var subject = string.IsNullOrEmpty(warning.Subject)
                ? warning.Kind.ToString()
                : warning.Subject;

            if (!subjects.Contains(subject, StringComparer.Ordinal))
            {
                subjects.Add(subject);
            }
        }

        return discarded;
    }

    /// <summary>
    /// DB の外で登録した照合順序・関数を使う物があって作り直せない表を、表ごとに集める（<see cref="SyncPlanContext.RebuildBlockers"/>。
    /// 値は作り直しを止める物の名指し）
    /// </summary>
    /// <remarks>
    /// 局所の物（索引・仮想の生成列）はその表だけを、大域の物（照合順序を使う索引）はその索引の表以外の live の表すべてを止める。
    /// 大域の索引の表自身は大域の規則で止めない（その表の作り直しは古い表を消してから RENAME するので、索引は読み直されない。
    /// 索引の文が照合順序を書いていれば、作り直しで索引を作れないので局所の物として別に止まる）。同じ同期でその索引の表を消しても外さない
    /// （同期スクリプトは表の削除を作り直しより後に置く）。
    /// </remarks>
    private static Dictionary<string, List<string>> CollectRebuildBlockedTables(
        SyncPlanContext? context,
        LiveSchema? liveSchema
    )
    {
        var blocked = new Dictionary<string, List<string>>(SchemaNames.Comparer);

        if (context is null || liveSchema is null || context.RebuildBlockers.Count == 0)
        {
            return blocked;
        }

        foreach (var entity in liveSchema.Entities)
        {
            var table = SchemaNames.ToStoredForm(entity.TableName);
            var details = context
                .RebuildBlockers.Where(b =>
                    b.BlocksOtherTables != SchemaNames.AreSame(b.Table, table)
                )
                .Select(b => b.Detail)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (details.Count > 0)
            {
                blocked[table] = details;
            }
        }

        return blocked;
    }

    /// <summary>作り直さない表が無いことを表す空の一覧（再構築の方言でないとき）</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyTables =
        new Dictionary<string, IReadOnlyList<string>>(SchemaNames.Comparer);

    /// <summary>
    /// 表ごとに集めた一覧（<see cref="CollectImportDiscardedContent"/>・<see cref="CollectRebuildBlockedTables"/>）を、同じ比べ方と
    /// 同じ並びの読み取り専用の一覧にする
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> AsReadOnlyTables(
        Dictionary<string, List<string>> tables
    ) =>
        tables.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<string>)entry.Value,
            SchemaNames.Comparer
        );
}
