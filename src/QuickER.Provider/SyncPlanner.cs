using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.Resources;
using QuickER.Provider.Sync;
using QuickER.Provider.Sync.Assessment;
using QuickER.Provider.Sync.Scheduling;
using QuickER.Provider.Sync.Selection;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;
using QuickER.Provider.Sync.TableRebuilds;

namespace QuickER.Provider;

/// <summary>
/// テーブル再構築（rebuild）方言で、live スキーマと選択差分から合成計画を組み立てるための入力。
/// </summary>
/// <remarks>
/// rebuild は「DB 現状（live）＋選択された差分のみ」を合成した定義でテーブルを作り直す。図の定義を直接
/// 使うと未選択の変更が紛れ込みデータ破壊を招くため、live スキーマ（＝差分計算に用いたもの）を必須入力とする。
/// </remarks>
public sealed class SyncPlanContext
{
    /// <summary>取込の結果から、計画の入力（live のスキーマと図の外の情報）を作る</summary>
    /// <remarks>
    /// 取込の結果から計画へ渡す項目（表・リレーション・補助オブジェクト・<c>CREATE TABLE</c> の原文・列の既定値・取込の警告・ビューの判定）は
    /// ここで 1 回だけ写す。呼び出し側（同期の画面・同期計画の総当たり）ごとに写すと、項目を足したときに写し忘れた側だけが
    /// 黙ってその入力の無い計画を作る（警告・除外が画面とテストで割れる）。
    /// </remarks>
    /// <param name="live">DB の取込の結果</param>
    public static SyncPlanContext FromImport(SchemaImportResult live)
    {
        ArgumentNullException.ThrowIfNull(live);

        return new SyncPlanContext
        {
            LiveEntities = live.Entities,
            LiveRelationships = live.Relationships,
            AuxiliaryObjects = live.AuxiliaryObjects,
            TableCreateSql = live.TableCreateSql,
            ColumnDefaults = live.ColumnDefaults,
            ImportWarnings = live.Warnings,
            LiveDependentCheck = live.LiveDependentCheck,
            RebuildBlockers = live.RebuildBlockers,
        };
    }

    /// <summary>DB から取得した現在のエンティティ（合成の土台）。</summary>
    public IReadOnlyList<Entity> LiveEntities { get; init; } = [];

    /// <summary>DB から取得した現在のリレーション（既存 FK 集合の復元に用いる）。</summary>
    public IReadOnlyList<Relationship> LiveRelationships { get; init; } = [];

    /// <summary>DB から取得した補助オブジェクト（再構築で温存するインデックス・トリガー）。</summary>
    /// <remarks>
    /// 一意制約は意味モデル（<see cref="Entity.UniqueConstraints"/>）が正本のため、ここには含まれない
    /// （<see cref="LiveEntities"/> から合成する）。
    /// </remarks>
    public IReadOnlyList<SchemaAuxiliaryObject> AuxiliaryObjects { get; init; } = [];

    /// <summary>
    /// DB から取得したテーブルの <c>CREATE TABLE</c> 文全文（テーブル名がキー・大文字小文字非依存）。
    /// </summary>
    /// <remarks>
    /// 合成には使わない（合成の正本はあくまで <see cref="LiveEntities"/>）。再構築で失われる属性
    /// （列の属性・制約の句・テーブルオプション。一覧は <see cref="TableRebuildAttributeDetector"/>）を検出して
    /// 警告するためだけに参照する。取得できない方言では空のまま＝警告も出ない。
    /// </remarks>
    public IReadOnlyDictionary<string, string> TableCreateSql { get; init; } =
        new Dictionary<string, string>();

    /// <summary>DB から取得した列の既定値（取込の結果＝<see cref="SchemaImportResult.ColumnDefaults"/>）。</summary>
    /// <remarks>
    /// 合成には使わない（図は既定値を持たない）。既定値のある列の型の種類の変更を事前に警告するためだけに参照する
    /// （<see cref="SyncPlanWarningKind.DefaultOnColumnTypeKindChange"/>）。
    /// </remarks>
    public IReadOnlyList<SchemaColumnDefault> ColumnDefaults { get; init; } = [];

    /// <summary>DB の取込の警告（取込の結果＝<see cref="SchemaImportResult.Warnings"/>）。</summary>
    /// <remarks>
    /// 合成には使わない。取込が図へ入れずに捨てた表の中の物（<see cref="SchemaImportWarningKinds.RebuildLosesTableContent"/>）がある表を、
    /// テーブル再構築の方言で作り直さないためだけに参照する（<see cref="SyncPlanWarningKind.TableRebuildLosesImportDiscardedContent"/>）。
    /// </remarks>
    public IReadOnlyList<SchemaImportWarning> ImportWarnings { get; init; } = [];

    /// <summary>
    /// DB のビュー・トリガーが同期後の表の形でも読める・発火できるかを試す入口（取込の結果＝<see cref="SchemaImportResult.LiveDependentCheck"/>）
    /// </summary>
    /// <remarks>
    /// 合成には使わない。ビュー・トリガーが参照する表の列を消す・足す変更と表の削除のうち、ビューを読めなくするもの・トリガーを発火できなく
    /// するものを外すためだけに参照する（<see cref="SyncPlanWarningKind.TableChangeBreaksDependentView"/>・
    /// <see cref="SyncPlanWarningKind.TableChangeBreaksDependentTrigger"/>）。<c>null</c> なら何もしない。
    /// </remarks>
    public ILiveDependentCheck? LiveDependentCheck { get; init; }

    /// <summary>テーブル再構築の作り直しを QuickER の接続では必ず止める DB の物（取込の結果＝<see cref="SchemaImportResult.RebuildBlockers"/>）</summary>
    /// <remarks>
    /// 合成には使わない。DB の外で登録した照合順序・関数を使う索引・仮想の生成列がある DB で、作り直しの項目を外すためだけに参照する
    /// （<see cref="SyncPlanWarningKind.TableRebuildBlockedByExternalDefinition"/>）。空なら何もしない。
    /// </remarks>
    public IReadOnlyList<TableRebuildBlocker> RebuildBlockers { get; init; } = [];
}

/// <summary>
/// 差分項目から方言中立の実行計画（<see cref="SyncPlan"/>）を組み立てるサービス。
/// </summary>
/// <remarks>
/// <para>
/// 入口は <see cref="BuildPlan"/> の 1 つ。計画は計画器（<see cref="StepPlanner"/>）が文の並びとして作り、
/// <see cref="StepLowering.ToSyncPlan"/> が計画の形（セクション・再構築・列順）へ写す。方言別レンダラー
/// （<see cref="ISyncScriptBuilder"/> の実装）はこの計画を SQL へ変換するだけでよく、選択フィルタ・
/// 出力順序・種別グループ化の知識を各方言に重複させない。
/// </para>
/// <para>
/// このクラスが持つのは、計画の前半（<see cref="PrepareSelection"/>＝除外の周・作り直しの展開・同期後の状態の差の項目・同期後の事実）と、
/// 選択・組み立て・評価が共有する、制約 1 つと状態で決まる事実の述語（internal static）。文の種類の順は
/// <see cref="Sync.Scheduling.StepOrder"/> が持つ。
/// </para>
/// <para>
/// テーブル再構築方言（SQLite＝<see cref="SyncDialectCapabilities.SupportsAlterColumn"/> または
/// <see cref="SyncDialectCapabilities.SupportsForeignKeyAlter"/> が <c>false</c>）では、逐次 DDL で表現できない
/// 変更（列型変更・列削除・FK 変更・新規テーブルへの後付け FK）をテーブル単位の <see cref="TableRebuildPlan"/> へ
/// 集約する（合成には live スキーマが必須＝<see cref="SyncPlanContext"/> を要求する）。
/// </para>
/// </remarks>
public sealed class SyncPlanner
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
        selected = InjectPrimaryKeyNotNullChanges(selected, liveSchema);

        // 選んだ表・列の削除で端が消える live の外部キーは、削除を選んでいなくても暗黙に外す
        // （以降の段はこれらを「明示的に外す外部キー」として扱う）
        selected = InjectVanishingForeignKeyDrops(
            selected,
            liveSchema,
            includeDroppedChildTables: !isRebuildDialect,
            capabilities
        );

        // 選んだ列の削除で構成列が消える live の一意制約も、削除を選んでいなくても暗黙に外す
        // （名指しは評価の規則 AssessmentRules.WarnUniqueConstraintsDroppedWithColumns）
        selected = InjectVanishingUniqueConstraintDrops(selected, liveSchema, capabilities);

        // 主キーと同じ列集合の一意制約を持てない逐次 DDL の方言（Oracle）では、選んだ主キーの変更の新しい主キーと同じ列集合の
        // live の一意制約を、削除を選んでいなくても外す（名指しは評価の規則 AssessmentRules.WarnUniqueConstraintsReplacedByPrimaryKey。
        // 再構築の方言は合成がその一意制約を省く）
        if (!isRebuildDialect && !capabilities.SupportsUniqueConstraintOnPrimaryKey)
        {
            selected = InjectUniqueConstraintsReplacedByPrimaryKey(
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

    /// <summary>選択済みの差分項目から実行計画を組み立てる</summary>
    /// <param name="items">全差分項目（未選択・情報表示専用の項目を含んでよい）</param>
    /// <param name="capabilities">対象方言の同期ケーパビリティ（rebuild への振り分けに用いる）</param>
    /// <param name="context">
    /// live スキーマ。rebuild 方言は合成に、列順を同期する逐次 DDL の方言（<see cref="SyncDialectCapabilities.ColumnReorder"/> が
    /// <see cref="ColumnReorderMode.Native"/>＝MySQL）は列順の項目の移動の組み立てに用いる。ほかの逐次 DDL 方言では省いてよい
    /// （<c>null</c> 可）。rebuild 方言で <c>null</c> を渡すか、Native の方言で
    /// 列順の項目を選んで <c>null</c> を渡すと <see cref="InvalidOperationException"/> を投げる（呼び出し側のバグ）。
    /// </param>
    /// <returns>
    /// 選択済み項目のみを固定順序でセクション化した計画（空セクションは含まない）。文の並びの計画
    /// （<see cref="StepPlanner"/>）を <see cref="StepLowering.ToSyncPlan"/> で計画の形（セクション・再構築・列順）へ写したもの
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// 文に要るメンバーの無い項目を渡したとき（項目の種類と無いメンバーを名指しする。差分〔<see cref="SchemaDiffService.Compute"/>〕が
    /// 作る項目はどれも文で表せる）・再構築の方言で計画の入力か DB の今の図が無いとき・除外の周か取り下げの輪が上限を超えたとき
    /// （どれも呼び出し側のバグ）
    /// </exception>
    public SyncPlan BuildPlan(
        IEnumerable<SchemaDiffItem> items,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context = null
    )
    {
        var planned = StepPlanner.Plan(items, capabilities, context);

        if (planned.Plan is null)
        {
            throw new InvalidOperationException(
                $"The sync plan cannot be built from an incomplete item: {planned.Unrepresentable}. "
                    + "Pass the items that SchemaDiffService.Compute produced."
            );
        }

        return StepLowering.ToSyncPlan(planned.Plan);
    }

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

    /// <summary>表の列の名前の集合か表の有無を変える項目の種別（列の削除・列の追加・表の削除）か</summary>
    internal static bool IsColumnNameSetChange(SchemaDiffKind kind) =>
        kind is SchemaDiffKind.DropColumn or SchemaDiffKind.AddColumn or SchemaDiffKind.DropTable;

    /// <summary>
    /// 選んだ主キーの変更で新しく主キーに加わる列のうち、live で NULL 許容のものを、暗黙の
    /// <see cref="SchemaDiffKind.AlterColumn"/>（live の型のまま NOT NULL）として足す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 主キーの列は NOT NULL でなければならず、図でも主キーの列は NOT NULL になっている。主キーの変更を選んだ時点で
    /// その意図に含まれるので、列の NOT NULL 化（差分では別の項目＝既定で未選択）を選んでいなくても行う。
    /// 選んでいないと、逐次 DDL の方言では主キーの付与が NULL 許容の列で失敗した。
    /// </para>
    /// <para>
    /// 型は live のまま（選んでいない型の変更は持ち込まない）。同じ列の変更を利用者が選んでいれば足さない
    /// （その項目が図の NOT NULL を運ぶ）。live に無い列（同じ計画で足す列）は追加の定義が運ぶので対象外。
    /// 足した項目は以降の段から選んだ列の変更と同じに見える（逐次 DDL では列の変更のセクション・
    /// SQLite では再構築の合成）。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> InjectPrimaryKeyNotNullChanges(
        List<SchemaDiffItem> selected,
        LiveSchema? liveSchema
    )
    {
        if (liveSchema is null)
        {
            return selected;
        }

        var liveByName = liveSchema.Tables;
        var injected = new List<SchemaDiffItem>();

        foreach (
            var change in selected.Where(i =>
                i.Kind == SchemaDiffKind.AlterPrimaryKey && i.Entity is not null
            )
        )
        {
            var table = SchemaNames.ToStoredForm(change.TableName);

            if (!liveByName.TryGet(table, out var live))
            {
                continue;
            }

            foreach (var keyColumn in change.Entity!.GetPrimaryKeyColumnsInOrder())
            {
                var liveColumn = live.Columns.FirstOrDefault(c =>
                    SchemaNames.AreSame(c.Name, keyColumn.Name)
                );

                if (
                    liveColumn is null
                    || !ColumnNullability.AllowsNull(liveColumn)
                    || selected.Any(i =>
                        i.Kind == SchemaDiffKind.AlterColumn
                        && SchemaNames.AreSame(SchemaNames.ToStoredForm(i.TableName), table)
                        && SchemaNames.AreSame(i.ColumnName, liveColumn.Name)
                    )
                )
                {
                    continue;
                }

                var notNull = liveColumn.Clone(preserveId: true);
                notNull.IsNullable = false;
                injected.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.AlterColumn,
                        TableName = table,
                        ColumnName = liveColumn.Name,
                        Entity = live,
                        Column = notNull,
                        OldColumn = liveColumn,
                        Description = string.Format(
                            Strings.Diff_AutoPrimaryKeyNotNull,
                            table,
                            liveColumn.Name
                        ),
                        PlanMark = PlanItemMark.Implicit(
                            ImplicitStepReason.PrimaryKeyColumnNotNull
                        ),
                    }
                );
            }
        }

        return injected.Count == 0 ? selected : [.. selected, .. injected];
    }

    /// <summary>
    /// 選んだ表・列の削除（<see cref="SchemaDiffKind.DropTable"/> / <see cref="SchemaDiffKind.DropColumn"/>）で、
    /// 子表・親表・構成列のどれかが消える live の外部キーを、暗黙の <see cref="SchemaDiffKind.DropForeignKey"/>
    /// として先頭へ足す（付け直さない）。
    /// </summary>
    /// <param name="selected">選択済みの項目（外部キーの作り直しは展開済み）</param>
    /// <param name="liveSchema">live スキーマ（無ければ何もしない）</param>
    /// <param name="includeDroppedChildTables">
    /// 子表そのものを消す外部キーも外すか。逐次 DDL の方言では外す（外部キーの削除は表の削除より前のセクションに
    /// 並ぶので、親と子を一緒に消すときに親を先に消して失敗する順序の問題も消える）。再構築の方言（SQLite）では
    /// 外さない（消す表を作り直す意味が無く、表の削除が外部キーごと消す）。
    /// </param>
    /// <param name="capabilities">方言のケーパビリティ（外部キーの実効名を求める識別子の上限）</param>
    /// <remarks>
    /// <para>
    /// 端が消える外部キーは同期後に残れないので、差分の外部キーの削除（既定で未選択）を選んでいなくても外す。
    /// 外さないと、逐次 DDL の方言では参照されている表・列の削除が拒否され（DDL が暗黙コミットされる方言では
    /// その前の文が適用されたまま止まる）、SQLite では存在しない表・列を指す外部キーが残る（行があれば
    /// <c>foreign_key_check</c> で失敗し、無ければ黙って壊れた外部キーが残る）。
    /// </para>
    /// <para>
    /// 利用者が同じ外部キーの削除を選んでいれば足さない。足した項目はそれ以降の段（暗黙の外し直し・候補キーの警告・
    /// 再構築の外部キーの合成）から、明示的に外す外部キーと同じに見える。SQLite では子表の再構築の項目として
    /// 畳まれ、作り直した表から外部キーが外れる。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> InjectVanishingForeignKeyDrops(
        List<SchemaDiffItem> selected,
        LiveSchema? liveSchema,
        bool includeDroppedChildTables,
        SyncDialectCapabilities capabilities
    )
    {
        var limit = capabilities.IdentifierLengthLimit;

        if (liveSchema is null)
        {
            return selected;
        }

        var postSync = PostSyncSchema.From(PlanStage.Expanded, selected, liveSchema, capabilities);
        var dropped = postSync.Dropped;

        if (dropped.IsEmpty)
        {
            return selected;
        }

        var explicitlyDropped = postSync.DroppedForeignKeys;
        var drops = new List<SchemaDiffItem>();

        foreach (var fk in liveSchema.ForeignKeys)
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                !dropped.Touches(fk, childTable, parentTable)
                || (dropped.Tables.Contains(childTable) && !includeDroppedChildTables)
                || explicitlyDropped.Contains(
                    ForeignKeyKey(childTable, parentTable, fk.ColumnPairs, fk.Relationship, limit)
                )
            )
            {
                continue;
            }

            drops.Add(
                BuildImplicitForeignKeyDrop(
                    fk,
                    childTable,
                    string.Format(
                        Strings.Diff_AutoForeignKeyDrop,
                        ResolveForeignKeyName(
                            fk.Relationship,
                            childTable,
                            parentTable,
                            limit,
                            liveSchema
                        )
                    ),
                    PlanItemMark.Implicit(ImplicitStepReason.VanishingForeignKey)
                )
            );
        }

        return drops.Count == 0 ? selected : [.. drops, .. selected];
    }

    /// <summary>
    /// 選んだ列の削除で構成列が消える live の一意制約を、暗黙の <see cref="SchemaDiffKind.DropUniqueConstraint"/> として
    /// 先頭へ足す（付け直さない）。名指し（<see cref="SyncPlanWarningKind.UniqueConstraintDroppedWithColumn"/>）は評価の規則
    /// <see cref="AssessmentRules.WarnUniqueConstraintsDroppedWithColumns"/> が、足した項目から作る。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 構成列が消える一意制約は同期後に残れないが、差分の一意制約の削除は既定で未選択なので、列の削除だけを選ぶと
    /// 方言で結果が割れる（SQL Server は削除が断られる・Oracle は複数列の制約で断られる・PostgreSQL は黙って消す・
    /// MySQL は残りの列の別の制約へ縮める・SQLite の再構築は黙って消す）。そこで外部キーの消える端と同じく暗黙に外す。
    /// 外部キーの消える端と違って警告を出すのは、外部キーは参照する列が消えれば意味を失うのに対し、一意制約は残りの列で
    /// 作り直せる（その判断を利用者に委ねる）ため。
    /// </para>
    /// <para>
    /// 表を消す計画の表は対象外（表の削除が制約ごと消す）。利用者が同じ一意制約の削除を選んでいれば足さない。
    /// 足した項目はそれ以降の段（暗黙の外し直し・候補キーの警告・索引の喪失・再構築の合成）から、明示的に外す一意制約と
    /// 同じに見える。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> InjectVanishingUniqueConstraintDrops(
        List<SchemaDiffItem> selected,
        LiveSchema? liveSchema,
        SyncDialectCapabilities capabilities
    )
    {
        if (liveSchema is null)
        {
            return selected;
        }

        var postSync = PostSyncSchema.From(
            PlanStage.WithVanishingForeignKeyDrops,
            selected,
            liveSchema,
            capabilities
        );
        var dropped = postSync.Dropped;

        if (dropped.Columns.Count == 0)
        {
            return selected;
        }

        var explicitlyDropped = postSync.DroppedUniqueConstraints;
        var drops = new List<SchemaDiffItem>();

        foreach (var entity in liveSchema.Entities)
        {
            var table = SchemaDiffService.NormalizeTable(entity);

            if (dropped.Tables.Contains(table))
            {
                continue;
            }

            foreach (var constraint in entity.UniqueConstraints)
            {
                if (
                    !UniqueConstraintNaming.TryResolveColumnNames(
                        entity,
                        constraint,
                        out var columns
                    )
                    || !dropped.TouchesColumns(table, columns)
                    || explicitlyDropped.Contains(
                        UniqueConstraintKey(table, constraint.Name, columns)
                    )
                )
                {
                    continue;
                }

                drops.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.DropUniqueConstraint,
                        TableName = table,
                        Entity = entity,
                        UniqueConstraintName = constraint.Name,
                        UniqueConstraintColumns = columns,
                        Description = string.Format(
                            Strings.Diff_AutoUniqueConstraintDrop,
                            string.Join(", ", columns)
                        ),
                        PlanMark = PlanItemMark.Implicit(
                            ImplicitStepReason.VanishingUniqueConstraint
                        ),
                    }
                );
            }
        }

        return drops.Count == 0 ? selected : [.. drops, .. selected];
    }

    /// <summary>
    /// 選んだ主キーの変更の新しい主キーと同じ列集合の live の一意制約を、暗黙の <see cref="SchemaDiffKind.DropUniqueConstraint"/>
    /// として先頭へ足す（付け直さない）。名指し（<see cref="SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey"/>）は評価の規則
    /// <see cref="AssessmentRules.WarnUniqueConstraintsReplacedByPrimaryKey"/> が、足した項目から作る。
    /// 主キーと同じ列集合の一意制約を持てない逐次 DDL の方言（Oracle）でだけ呼ぶ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Oracle は同じ列に主キーと一意制約を両方は持てず、残すと主キーの付与が ORA-02261 で断られ、DDL が暗黙コミットされるので
    /// 旧主キーを外したまま主キーが消えた。差分は図の主キーと同じ列集合の一意制約を比較から外し、DB 側の削除も出さないので、
    /// 利用者は一意制約の削除を選べない。値の一意性は新しい主キーが保ち、図からの DDL もその一意制約を省くので、同期の後に差分は
    /// 残らない。利用者が同じ一意制約の削除を選んでいれば足さない。
    /// </para>
    /// <para>
    /// その一意制約を参照する外部キーは、主キーの変更の外し直し（外し直しの宣言
    /// <c>RebuildDeclarations.RequireForeignKeysOffUniquesReplacedByPrimaryKey</c>）が外して、新しい主キーへ付け直す
    /// （外さないと一意制約の削除が断られる）。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> InjectUniqueConstraintsReplacedByPrimaryKey(
        List<SchemaDiffItem> selected,
        LiveSchema? liveSchema,
        SyncDialectCapabilities capabilities
    )
    {
        if (liveSchema is null)
        {
            return selected;
        }

        var postSync = PostSyncSchema.From(
            PlanStage.WithVanishingUniqueDrops,
            selected,
            liveSchema,
            capabilities
        );
        var pkChangedTables = postSync.PrimaryKeyChanges;

        if (pkChangedTables.Count == 0)
        {
            return selected;
        }

        var explicitlyDropped = postSync.DroppedUniqueConstraints;
        var liveByName = liveSchema.Tables;
        var drops = new List<SchemaDiffItem>();

        foreach (var (table, newKey) in pkChangedTables)
        {
            if (newKey.Count == 0)
            {
                continue;
            }

            if (!liveByName.TryGet(table, out var live))
            {
                continue;
            }

            foreach (var constraint in live.UniqueConstraints)
            {
                if (
                    !UniqueConstraintNaming.TryResolveColumnNames(live, constraint, out var columns)
                    || !IsSameColumnSet(columns, newKey)
                    || explicitlyDropped.Contains(
                        UniqueConstraintKey(table, constraint.Name, columns)
                    )
                )
                {
                    continue;
                }

                drops.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.DropUniqueConstraint,
                        TableName = table,
                        Entity = live,
                        UniqueConstraintName = constraint.Name,
                        UniqueConstraintColumns = columns,
                        Description = string.Format(
                            Strings.Diff_AutoUniqueConstraintReplacedByPrimaryKey,
                            string.Join(", ", columns)
                        ),
                        PlanMark = PlanItemMark.Implicit(
                            ImplicitStepReason.UniqueConstraintReplacedByPrimaryKey
                        ),
                    }
                );
            }
        }

        return drops.Count == 0 ? selected : [.. drops, .. selected];
    }

    /// <summary>
    /// 列集合が live のキー（主キー・一意制約）の列集合を含み、その列がどれも live にあって型を変えないか（含む列集合の値の組は、
    /// 含まれる側が一意なので一意）
    /// </summary>
    /// <param name="liveTable">live の表（無ければ証明できない）</param>
    /// <param name="table">保存する形の表名</param>
    /// <param name="columns">新しい主キー・一意制約の構成列</param>
    /// <param name="typeDecidingColumns">この同期で型が決まる列（<see cref="PostSyncSchema.TypeDecidingColumns"/>）</param>
    internal static bool HoldsInLiveRows(
        Entity? liveTable,
        string table,
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<ColumnId, string> typeDecidingColumns
    )
    {
        if (
            liveTable is null
            || columns.Any(c =>
                !liveTable.Columns.Any(l => SchemaNames.AreSame(l.Name, c))
                || typeDecidingColumns.ContainsKey(ColumnKey(table, c))
            )
        )
        {
            return false;
        }

        var set = columns.Select(SchemaNames.Key).ToHashSet(StringComparer.Ordinal);
        var namesById = liveTable.Columns.ToDictionary(c => c.Id, c => SchemaNames.Key(c.Name));
        IEnumerable<IReadOnlyList<string>> keys =
        [
            [.. liveTable.GetPrimaryKeyColumnsInOrder().Select(c => SchemaNames.Key(c.Name))],
            .. liveTable
                .UniqueConstraints.Where(u => u.ColumnIds.All(namesById.ContainsKey))
                .Select(u => (IReadOnlyList<string>)[.. u.ColumnIds.Select(id => namesById[id])]),
        ];

        return keys.Any(key => key.Count > 0 && key.All(set.Contains));
    }

    /// <summary>この主キー変更項目が新しい主キー列を持つか（＝付与フェーズを要するか）</summary>
    internal static bool HasNewPrimaryKeyColumns(SchemaDiffItem item) =>
        item.Entity?.Columns.Any(c => c.IsPrimaryKey) == true;

    // ---------------- 依存 FK の自動 DROP → 再 ADD（逐次 DDL 方言） ----------------

    /// <summary>
    /// 外部キーが参照先の旧主キー（live の主キー）に支えられているか（主キーを外すと巻き込まれるか）。
    /// </summary>
    /// <remarks>
    /// 判定は <see cref="CanSupportForeignKey"/>（同じ列集合か、MySQL では旧主キーの先頭の列だけを参照する非標準キーの外部キーも）。
    /// 一意制約を参照する外部キーは巻き込まれない。参照先が live に無ければ支えられているとみなす。
    /// </remarks>
    internal static bool DependsOnOldPrimaryKey(
        NameLookup<Entity> liveByName,
        SyncDialectCapabilities capabilities,
        string parentTable,
        IReadOnlyList<string> referencedColumns
    )
    {
        if (!liveByName.TryGet(parentTable, out var liveParent))
        {
            return true;
        }

        return CanSupportForeignKey(
            capabilities,
            [.. liveParent.GetPrimaryKeyColumnsInOrder().Select(c => c.Name)],
            referencedColumns
        );
    }

    /// <summary>live の外部キーを外す暗黙の DropForeignKey 項目を作る（差分計算の FK 差分と同じフィールド規則）</summary>
    private static SchemaDiffItem BuildImplicitForeignKeyDrop(
        ResolvedForeignKeyRelationship fk,
        string childTable,
        string description,
        PlanItemMark mark
    ) =>
        new()
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = childTable,
            Entity = fk.Child,
            ParentEntity = fk.Parent,
            ChildEntity = fk.Child,
            Relationship = fk.Relationship,
            ForeignKeyName = fk.Relationship.ConstraintName,
            ForeignKeyColumnPairs = fk.ColumnPairs,
            Description = description,
            PlanMark = mark,
        };

    /// <summary>外部キーの子列が、索引の先頭の列とちょうど同じ集合か</summary>
    /// <remarks>
    /// MySQL は外部キーの子列を先頭に持つ索引を要し、それが消える索引しか無いと索引を外せない（エラー 1553）。
    /// 別の索引が支えている場合も外し直すことになるが、外部キーを外して付け直すだけなので害は無い。
    /// </remarks>
    internal static bool IsLeadingPrefix(
        IReadOnlyList<string> indexColumns,
        IReadOnlyList<ForeignKeyColumnNamePair> columnPairs
    )
    {
        if (indexColumns.Count < columnPairs.Count)
        {
            return false;
        }

        var leading = indexColumns.Take(columnPairs.Count).ToHashSet(SchemaNames.Comparer);
        return ForeignKeyColumnPairResolver.ChildColumns(columnPairs).All(leading.Contains);
    }

    /// <summary>
    /// 端の型がこの同期で決まる組のうち、同期後（その型を当てた後）に両端の列の型が並べられない組がある外部キーか
    /// </summary>
    /// <param name="fk">live の外部キー</param>
    /// <param name="childTable">子表</param>
    /// <param name="parentTable">親表</param>
    /// <param name="typeDecidingColumns">この同期で型が決まる列（<see cref="PostSyncSchema.TypeDecidingColumns"/>）</param>
    /// <param name="capabilities">型の比較に使う方言のケーパビリティ</param>
    /// <param name="column">null でなければ、この列（列の照合キー）を端に持つ組だけを見る</param>
    /// <remarks>
    /// 判定は組単位（<see cref="IsMismatchedUnacceptedPair"/>）。型の比較は差分の型比較と同じ規則
    /// （<see cref="SyncDialectCapabilities.AreForeignKeyCompatibleTypes"/>＝方言の取込表記へ寄せる）。live の表記と図の表記が
    /// 混ざって比べられるため、正規化を通さないと綴りだけ違う同じ型を「食い違う」と判定し、外し直しを止めてしまう。
    /// 型の決まらない端は live の型で比べる。どちらの端の型も決まらない組は数えない（DB が今の両端の型のまま受け入れている）。
    /// 外部キー単位で見ると、複合外部キーのほかの組の型を変えたとき、DB が受け入れている型の違う組まで食い違いに数え、外部キーを
    /// 外し直さずに主キーの解除・列の変更を必ず失敗させた。
    /// </remarks>
    internal static bool HasPostSyncTypeMismatch(
        ResolvedForeignKeyRelationship fk,
        string childTable,
        string parentTable,
        IReadOnlyDictionary<ColumnId, string> typeDecidingColumns,
        SyncDialectCapabilities capabilities,
        ColumnId? column = null
    )
    {
        return fk.ColumnPairs.Any(pair =>
            (
                column is null
                || ColumnKey(childTable, pair.ChildColumn) == column
                || ColumnKey(parentTable, pair.ParentColumn) == column
            )
            // live の外部キーの組は DB が今受け入れている組
            && IsMismatchedUnacceptedPair(
                PostSyncType(fk.Child, childTable, pair.ChildColumn),
                PostSyncType(fk.Parent, parentTable, pair.ParentColumn),
                liveHasPair: true,
                capabilities
            )
        );

        (string Type, bool Decided) PostSyncType(Entity entity, string table, string name) =>
            typeDecidingColumns.TryGetValue(ColumnKey(table, name), out var decided)
                ? (decided, true)
                : (ColumnTypeOf(entity, name), false);
    }

    /// <summary>
    /// 外部キーの 1 組が、DB が受け入れた組でなく、同期後に両端が並べられないか（型の食い違いの判定の正本）
    /// </summary>
    /// <param name="child">子の端の同期後の型と、その型がこの同期で決まるか</param>
    /// <param name="parent">親の端の同期後の型と、その型がこの同期で決まるか</param>
    /// <param name="liveHasPair">live にこの組（子の表と列・親の表と列）を持つ外部キーがあるか</param>
    /// <param name="capabilities">型の比較に使う方言のケーパビリティ</param>
    /// <remarks>
    /// 「DB が受け入れた組」は、どちらの端の型もこの同期で決まらず、live の外部キーが同じ組を持つ組（DB が今その両端の型のまま
    /// 受け入れている）。端の型が決まる組と、DB にまだ無い組（選んだ外部キーの追加・列ペアを変える作り直しの新しい組）は数える。
    /// DB にまだ無い組まで「型が決まらないから受け入れている」とみなすと、live の列どうしで型の違う外部キーの追加
    /// （親 int・子 bigint など）が名指しされずに MySQL（エラー 3780）・SQL Server（Msg 1778）で断られる。
    /// </remarks>
    internal static bool IsMismatchedUnacceptedPair(
        (string Type, bool Decided) child,
        (string Type, bool Decided) parent,
        bool liveHasPair,
        SyncDialectCapabilities capabilities
    ) =>
        (child.Decided || parent.Decided || !liveHasPair)
        && !capabilities.AreForeignKeyCompatibleTypes(child.Type, parent.Type);

    /// <summary>外部キーの 1 組の照合キー（子の列と親の列。名前の比べ方は列の照合キーと同じ）</summary>
    internal static ForeignKeyPairId ForeignKeyPairKey(
        string childTable,
        string childColumn,
        string parentTable,
        string parentColumn
    ) => ForeignKeyPairId.Of(childTable, childColumn, parentTable, parentColumn);

    // ---------------- 主キーの暗黙の外し直し（SQL Server） ----------------

    /// <summary>主キーが変わる表を参照する外部キーの被参照列が、同期後も候補キーであることを証明できるか</summary>
    /// <remarks>
    /// 根拠は「同期後の主キーが被参照列集合とちょうど一致」か「同期後に同じ列集合の一意制約が在る」のいずれか。
    /// 被参照列が新主キーに含まれていても他の列と複合になっていれば主キーは根拠にならない
    /// （(id) → (id, code) の拡張は 4 方言中 3 方言で再 ADD が失敗する）。
    /// 一意制約はモデルの正本（Entity.UniqueConstraints）から同期後の集合を厳密に合成して判定するため、
    /// 「自然キー UNIQUE を持つ表の主キー付け替え」は誤警告しない。証明できない構成は
    /// （一意インデックス等で実際には通ることがあっても）警告する＝実行は止めない安全側へ倒す。
    /// 比べるのは列の集合で、どの方言でも並びを問わない（参照する列の並びがキーの並びと一致しないと外部キーを作れない
    /// 方言では、計画が付け直しを参照先の同期後のキーの並びへ並べ替える＝<see cref="ForeignKeyPairOrder"/>）。
    /// </remarks>
    internal static bool StaysCandidateKey(
        IReadOnlyList<string>? newPkColumns,
        IReadOnlyDictionary<string, IReadOnlySet<string>> postSyncUniques,
        string parentTable,
        IReadOnlyList<string> referencedColumns
    ) =>
        (
            newPkColumns is not null
            && string.Equals(
                UniqueConstraintNaming.ColumnSetSignature(newPkColumns),
                UniqueConstraintNaming.ColumnSetSignature(referencedColumns),
                StringComparison.Ordinal
            )
        )
        || (
            postSyncUniques.TryGetValue(parentTable, out var signatures)
            && signatures.Contains(UniqueConstraintNaming.ColumnSetSignature(referencedColumns))
        );

    /// <summary>
    /// 作れない方言で、この外部キーの付け直しが必ず失敗するか（参照アクション <c>SET NULL</c> で、子の列が同期後に NOT NULL）
    /// </summary>
    internal static bool BlocksSetNullRebuild(
        ResolvedForeignKeyRelationship fk,
        string childTable,
        SyncDialectCapabilities capabilities,
        Func<Entity, string, string, bool> postSyncNullable
    ) =>
        capabilities.SetNullRequiresNullableChildColumn
        && UsesSetNull(fk.Relationship, capabilities)
        && PostSyncNotNullChildColumns(fk, childTable, postSyncNullable).Count > 0;

    /// <summary>live の外部キーの子の列のうち、同期後に NOT NULL のもの（宣言順）</summary>
    internal static List<string> PostSyncNotNullChildColumns(
        ResolvedForeignKeyRelationship fk,
        string childTable,
        Func<Entity, string, string, bool> postSyncNullable
    ) =>
        [
            .. fk
                .ColumnPairs.Select(p => p.ChildColumn)
                .Where(column => !postSyncNullable(fk.Child, childTable, column)),
        ];

    /// <summary>方言が書く参照アクション（ON DELETE か ON UPDATE）が <c>SET NULL</c> か</summary>
    internal static bool UsesSetNull(
        Relationship relationship,
        SyncDialectCapabilities capabilities
    ) =>
        capabilities.EffectiveOnDelete(relationship.OnDelete) == ForeignKeyReferentialAction.SetNull
        || capabilities.EffectiveOnUpdate(relationship.OnUpdate)
            == ForeignKeyReferentialAction.SetNull;

    /// <summary><see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/> の警告（Detail は「制約名 (同期後に NOT NULL の子の列, …)」）</summary>
    internal static SyncPlanWarning SetNullWarning(
        string childTable,
        string constraintName,
        IReadOnlyList<string> notNullColumns
    ) =>
        new(
            SyncPlanWarningKind.SetNullOnNotNullColumn,
            childTable,
            $"{constraintName} ({string.Join(", ", notNullColumns)})"
        );

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

    /// <summary>2 つの列名集合が（順序・大文字小文字を無視して）ちょうど一致するか</summary>
    internal static bool IsSameColumnSet(IEnumerable<string> left, IEnumerable<string> right) =>
        string.Equals(
            UniqueConstraintNaming.ColumnSetSignature(left),
            UniqueConstraintNaming.ColumnSetSignature(right),
            StringComparison.Ordinal
        );

    /// <summary>エンティティの列の型（列名は <see cref="SchemaNames"/> の規則で引く。列が無ければ空文字列）</summary>
    /// <remarks>
    /// live の列の型（DB が今受け入れている型）・図の列の型を読む箇所が通す。この同期で型が決まる列の新しい型は
    /// <see cref="PostSyncSchema.TypeDecidingColumns"/> が決め、ここでは読まない。
    /// </remarks>
    internal static string ColumnTypeOf(Entity entity, string column) =>
        entity.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, column))?.DataType
        ?? string.Empty;

    /// <summary>親のキー（主キー・一意制約）が、その列を参照する外部キーを支えうるか（DB がそのキーの削除を外部キーで断るか）</summary>
    /// <param name="capabilities">方言のケーパビリティ</param>
    /// <param name="keyColumns">キーの構成列（主キーは実効順・一意制約は宣言順）</param>
    /// <param name="referencedColumns">外部キーが参照する親の列</param>
    /// <remarks>
    /// <para>
    /// 参照する列と同じ列の集合を持つキーは、どの方言でも支える。外部キーが参照先の索引の先頭の列を要する方言
    /// （<see cref="SyncDialectCapabilities.ForeignKeyRequiresLeadingIndex"/>＝MySQL）では、参照する列を先頭に並べるもっと長いキーも
    /// 支えうる（8.0 系や restrict_fk_on_non_standard_key=OFF で作れる非標準キーの外部キー。ほかに支える索引が無いと、そのキーの
    /// 削除がエラー 1553 になる）。
    /// </para>
    /// <para>
    /// 判定はここの 1 か所で、前寄せ（<see cref="Refusals.FindCertain"/>）・一意制約の削除の警告
    /// （<see cref="AssessmentRules.WarnUniqueConstraintDropsBreakingForeignKeys"/>）・主キーの変更の外し直し（<see cref="DependsOnOldPrimaryKey"/> と、
    /// 新しい主キーへ付け直す一意制約の枝）が通す。写しが割れると、前寄せした削除を警告が名指しせず、警告を飛ばした外部キーを
    /// 外し直しも付け直さない。付け直せるか（候補キーの証明＝<see cref="StaysCandidateKey"/>）は
    /// 別の判定で、列の集合の完全一致のまま（非標準キーへの付け直しは証明しない）。
    /// </para>
    /// </remarks>
    internal static bool CanSupportForeignKey(
        SyncDialectCapabilities capabilities,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<string> referencedColumns
    ) =>
        IsSameColumnSet(keyColumns, referencedColumns)
        || capabilities.ForeignKeyRequiresLeadingIndex
            && keyColumns.Count > referencedColumns.Count
            && IsSameColumnSet(keyColumns.Take(referencedColumns.Count), referencedColumns);

    /// <summary>
    /// DB の一意制約の照合キー（テーブル＋DB の名前＋列集合シグネチャ。名前は <see cref="SchemaNames.Key"/> で畳み、列順を無視する）
    /// </summary>
    /// <param name="table">テーブル</param>
    /// <param name="name">DB の名前（名前の無い制約＝SQLite の自動名は <c>null</c>）</param>
    /// <param name="columnNames">構成列</param>
    /// <remarks>
    /// <para>
    /// 照合するのは、DB の一意制約（live）と、それを外す項目（差分の DropUniqueConstraint・作り直しを展開した削除＝どちらも
    /// live の制約の名前をそのまま運ぶ）だけ。図の一意制約（追加の項目）はこのキーで照合しない。名前の無い制約どうしは
    /// 列集合だけで組む（合成名は使わない＝並び順に依る）。
    /// </para>
    /// <para>
    /// 名前を含めるのは、同じ列集合の一意制約が 2 つある表で、明示的に外した 1 つがもう 1 つまで「外した」ことにして、
    /// 暗黙の外し直し・列の削除に伴う削除・同期後のキーの集合を変えないため（外部キーの <see cref="ForeignKeyKey"/> と対称。
    /// 名前を見ないと、残った 1 つが列の変更を阻んだ＝SQL Server の Msg 5074）。
    /// </para>
    /// </remarks>
    internal static UniqueConstraintId UniqueConstraintKey(
        string table,
        string? name,
        IEnumerable<string> columnNames
    ) => UniqueConstraintId.Of(table, name, columnNames);

    /// <summary>テーブル・列の照合キー（名前は <see cref="SchemaNames.Key"/> で畳む）</summary>
    internal static ColumnId ColumnKey(string table, string column) => ColumnId.Of(table, column);

    /// <summary>
    /// 外部キーの照合キー（子テーブル・親テーブル・構成列ペアの宣言順リスト・実効名・参照アクション。
    /// 名前は <see cref="SchemaNames.Key"/> で畳む）
    /// </summary>
    /// <param name="childTable">子テーブル</param>
    /// <param name="parentTable">親テーブル</param>
    /// <param name="columnPairs">構成列ペア（宣言順）</param>
    /// <param name="relationship">名前と参照アクションを読むリレーション（無ければ既定名・NO ACTION）</param>
    /// <remarks>
    /// 複合外部キーも 1 本のキーへ畳むため、列ペアを宣言順に連結する。
    /// 名前と参照アクションも含める＝同じ列ペアに外部キーが 2 本ある構成で、明示的に外した 1 本がもう 1 本まで
    /// 「外した」ことにして暗黙の外し直しを止めないため（止まると、残った 1 本が主キーの解除・列の変更を阻む）。
    /// 暗黙の外し直しのある実在の方言（逐次 DDL）は外部キーの名前を保存するので、同じ子の表の 2 本は実効名が見分ける。
    /// 参照アクションは、名前を保存しない能力と暗黙の外し直しの組のための成分で、いまの実在の方言の組には当たらない
    /// （名前を保存しない SQLite の再構築の合成は、削除 1 件につき 1 本を外す数え方で 2 本を扱う）。
    /// </remarks>
    internal static ForeignKeyId ForeignKeyKey(
        string childTable,
        string parentTable,
        IEnumerable<ForeignKeyColumnNamePair> columnPairs,
        Relationship? relationship,
        IdentifierLengthLimit limit
    ) =>
        ForeignKeyId.Of(
            childTable,
            parentTable,
            columnPairs,
            ResolveForeignKeyName(relationship, childTable, parentTable, limit),
            relationship?.OnDelete ?? ForeignKeyReferentialAction.NoAction,
            relationship?.OnUpdate ?? ForeignKeyReferentialAction.NoAction
        );

    /// <summary>
    /// 選んだ外部キーの追加・作り直しの項目を警告で名指しする名前（スクリプトが付ける名前＝
    /// <see cref="SyncScriptBuilderHelper.ForeignKeyNameToAdd"/>）
    /// </summary>
    /// <remarks>
    /// 差分は名前の無い外部キーの名前を図全体から求めて項目に載せる（同じ組の 2 本目は子の列名が付く）。リレーションの名前から
    /// 規約名を作り直すと 2 本目は 1 本目と同じ名前になり、DB にある無関係な外部キーを名指しした。
    /// </remarks>
    internal static string SelectedForeignKeyName(
        SchemaDiffItem item,
        IdentifierLengthLimit limit
    ) => SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, limit);

    /// <summary>FK 制約名を解決する（未設定なら <c>FK_{子}_{親}</c> の規約名）</summary>
    /// <param name="relationship">外部キーのリレーション</param>
    /// <param name="childTable">子表</param>
    /// <param name="parentTable">親表</param>
    /// <param name="limit">既定名を縮める同期の方言の上限（<see cref="SyncDialectCapabilities.IdentifierLengthLimit"/>）</param>
    /// <param name="liveSchema">
    /// live の図。live のリレーションなら、図全体から求めた実効名（<see cref="LiveSchema.ForeignKeyNames"/>）を使う
    /// </param>
    internal static string ResolveForeignKeyName(
        Relationship? relationship,
        string childTable,
        string parentTable,
        IdentifierLengthLimit limit,
        LiveSchema? liveSchema = null
    ) =>
        relationship is not null
        && liveSchema is not null
        && liveSchema.ForeignKeyNames(limit).TryGetValue(relationship, out var resolved)
            ? resolved
            : ConstraintNames.EffectiveForeignKey(
                relationship?.ConstraintName,
                childTable,
                parentTable,
                limit
            );

    /// <summary>選択済み ReorderColumns をネイティブ並べ替え計画（<see cref="TableReorderPlan"/>）へ変換する</summary>
    /// <remarks>
    /// live スキーマから移動集合を計算するため live の図（<paramref name="liveSchema"/>）が必須（rebuild 方言と同じく、選択された ReorderColumns が
    /// あるのに live の図が無ければ呼び出し側のバグとして例外にする）。live に無いテーブルの項目は、そのテーブル自体が
    /// 同期対象外とみなして黙って落とす。
    /// </remarks>
    internal static IReadOnlyList<TableReorderPlan> BuildReorderPlans(
        List<SchemaDiffItem> selected,
        LiveSchema? liveSchema
    )
    {
        var reorderItems = selected.Where(i => i.Kind == SchemaDiffKind.ReorderColumns).ToList();

        if (reorderItems.Count == 0)
        {
            return [];
        }

        if (liveSchema is null)
        {
            // ネイティブ並べ替えの移動集合計算には live スキーマが必須（合成定義も live 由来）
            throw new InvalidOperationException(
                "Native column-reorder dialects require a SyncPlanContext (live schema) to compute column moves."
            );
        }

        var liveByName = liveSchema.Tables;

        var plans = new List<TableReorderPlan>();

        foreach (var item in reorderItems)
        {
            var table = SchemaNames.ToStoredForm(item.TableName);

            // live に無いテーブル（そのテーブル自体が同期対象外）・target 不明なら並べ替えられない
            if (!liveByName.TryGet(table, out var live) || item.Entity is null)
            {
                continue;
            }

            var moves = ComputeColumnMoves(live, item.Entity, selected, table);

            // 実質移動が無い（既に目標順）場合は計画に含めない
            if (moves.Count == 0)
            {
                continue;
            }

            plans.Add(
                new TableReorderPlan
                {
                    TableName = live.TableName,
                    Moves = moves,
                    SourceItems = [item],
                }
            );
        }

        return plans;
    }

    /// <summary>1 テーブル分の列移動集合を最小移動（LIS を不動とする）で計算する</summary>
    /// <remarks>
    /// <para>
    /// 実効列順 = live の列順に「選択済み DropColumn を除外・選択済み AddColumn を末尾追加」した並び。
    /// 目標順 = target の列順のうち実効列集合に含まれるもの。目標順にある列（tracked）を実効順で見たとき、
    /// 最長増加部分列（LIS）に入る列は既に相対順が正しいため不動とし、それ以外だけを移動対象にする。
    /// </para>
    /// <para>
    /// 移動は目標順に前から確定し、各移動列の <c>AFTER</c> は目標順で直前の列（先頭なら <c>FIRST</c>）にする。
    /// 直前の列は目標順の左から順に確定済みになっているため、最終的な並びは目標順どおりになる。
    /// 移動列の定義は合成スキーマ由来（選択済み AlterColumn / AddColumn があればその新定義、無ければ live 定義）。
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ColumnMove> ComputeColumnMoves(
        Entity live,
        Entity target,
        List<SchemaDiffItem> selected,
        string table
    )
    {
        // このテーブルの選択済み Add / Drop / Alter を集める
        var tableSelected = selected
            .Where(i => SchemaNames.AreSame(SchemaNames.ToStoredForm(i.TableName), table))
            .ToList();
        var droppedNames = tableSelected
            .Where(i => i.Kind == SchemaDiffKind.DropColumn && i.ColumnName is not null)
            .Select(i => i.ColumnName!)
            .ToHashSet(SchemaNames.Comparer);
        var addedColumns = tableSelected
            .Where(i => i.Kind == SchemaDiffKind.AddColumn && i.Column is not null)
            .Select(i => i.Column!)
            .ToList();

        // 実効列順 = live − 選択 Drop ＋ 選択 Add（末尾）
        var effective = new List<string>();

        foreach (var column in live.Columns)
        {
            if (!droppedNames.Contains(column.Name))
            {
                effective.Add(column.Name);
            }
        }

        effective.AddRange(addedColumns.Select(c => c.Name));

        var effectiveSet = effective.ToHashSet(SchemaNames.Comparer);

        // 目標順 = target の列順のうち実効列集合に含まれるもの（＝並べ替え対象になり得る列）
        var targetOrder = target.Columns.Select(c => c.Name).Where(effectiveSet.Contains).ToList();
        var targetIndex = new Dictionary<string, int>(SchemaNames.Comparer);

        for (var i = 0; i < targetOrder.Count; i++)
        {
            targetIndex.TryAdd(targetOrder[i], i);
        }

        // 実効順に並ぶ tracked 列（目標順にある列）の目標インデックス列を作り、LIS を不動集合にする
        var effectiveTrackedIndices = effective
            .Where(targetIndex.ContainsKey)
            .Select(name => targetIndex[name])
            .ToList();
        var fixedIndices = LongestIncreasingSubsequence(effectiveTrackedIndices);

        var moves = new List<ColumnMove>();

        for (var ti = 0; ti < targetOrder.Count; ti++)
        {
            if (fixedIndices.Contains(ti))
            {
                continue;
            }

            var name = targetOrder[ti];
            var after = ti == 0 ? null : targetOrder[ti - 1];
            moves.Add(
                new ColumnMove(ResolveMovedColumnDefinition(name, tableSelected, live), after)
            );
        }

        return moves;
    }

    /// <summary>移動する列の定義を合成スキーマ規則で解決する（選択 Add → 選択 Alter → live の順）</summary>
    private static Column ResolveMovedColumnDefinition(
        string name,
        IReadOnlyList<SchemaDiffItem> tableSelected,
        Entity live
    )
    {
        // 選択済み AddColumn の新規列（live に無い）はその定義で移動する
        var add = tableSelected.FirstOrDefault(i =>
            i.Kind == SchemaDiffKind.AddColumn
            && i.Column is not null
            && SchemaNames.AreSame(i.Column.Name, name)
        );

        if (add?.Column is not null)
        {
            return add.Column;
        }

        // 選択済み AlterColumn があれば新定義で移動する（未選択の型変更は紛れ込ませないため live 定義）
        var alter = tableSelected.FirstOrDefault(i =>
            i.Kind == SchemaDiffKind.AlterColumn
            && i.Column is not null
            && SchemaNames.AreSame(i.Column.Name, name)
        );

        if (alter?.Column is not null)
        {
            return alter.Column;
        }

        return live.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, name))
            ?? new Column { Name = name };
    }

    /// <summary>
    /// 数列の最長増加部分列（厳密増加）に含まれる値の集合を返す（O(n log n)・パ―シェンスソート＋前駆復元）。
    /// </summary>
    /// <remarks>値は目標インデックス（distinct）。ここに残った値の列は移動不要（不動）とみなす。</remarks>
    private static HashSet<int> LongestIncreasingSubsequence(IReadOnlyList<int> sequence)
    {
        var result = new HashSet<int>();
        var count = sequence.Count;

        if (count == 0)
        {
            return result;
        }

        // tails[k] = 長さ k+1 の増加部分列の末尾になり得る最小値の、sequence 内インデックス
        var tails = new List<int>();
        var predecessor = new int[count];

        for (var i = 0; i < count; i++)
        {
            predecessor[i] = -1;
            // 厳密増加のため sequence[i] 以上になる最初の位置（lower_bound）を二分探索する
            var lo = 0;
            var hi = tails.Count;

            while (lo < hi)
            {
                var mid = (lo + hi) / 2;

                if (sequence[tails[mid]] < sequence[i])
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            if (lo > 0)
            {
                predecessor[i] = tails[lo - 1];
            }

            if (lo == tails.Count)
            {
                tails.Add(i);
            }
            else
            {
                tails[lo] = i;
            }
        }

        // 末尾から前駆をたどって 1 本の LIS を復元する
        for (var k = tails[^1]; k >= 0; k = predecessor[k])
        {
            result.Add(sequence[k]);
        }

        return result;
    }
}
