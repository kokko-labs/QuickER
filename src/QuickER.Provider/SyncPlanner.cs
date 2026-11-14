using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.Resources;

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
            LiveViewCheck = live.LiveViewCheck,
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

    /// <summary>DB のビューが同期後の表の形でも読めるかを試す入口（取込の結果＝<see cref="SchemaImportResult.LiveViewCheck"/>）</summary>
    /// <remarks>
    /// 合成には使わない。ビューが読む表の列を消す・足す変更と表の削除のうち、ビューを読めなくするものを外すためだけに参照する
    /// （<see cref="SyncPlanWarningKind.TableChangeBreaksDependentView"/>）。<c>null</c> なら何もしない。
    /// </remarks>
    public ILiveViewCheck? LiveViewCheck { get; init; }

    /// <summary>live の外部キーの実効名（図全体から求めた名前＝DDL が付けるのと同じ規則）</summary>
    /// <remarks>
    /// 名前の無い同じ組の外部キーが 2 本あると、2 本目は子の列名を足した名前になる
    /// （<see cref="ConstraintNames.ResolveForeignKeyNames"/>）。リレーションごとに既定名を求めると 2 本とも同じ名前になり、
    /// 警告・外し直しの名前が DDL と食い違う。上限ごとに 1 回だけ求める。
    /// </remarks>
    /// <param name="limit">既定名を縮める同期の方言の上限（<see cref="SyncDialectCapabilities.IdentifierLengthLimit"/>）</param>
    internal IReadOnlyDictionary<Relationship, string> LiveForeignKeyNames(
        IdentifierLengthLimit limit
    )
    {
        if (_liveForeignKeyNames is null || !Equals(_liveForeignKeyNamesLimit, limit))
        {
            _liveForeignKeyNames = ConstraintNames.ResolveForeignKeyNames(
                LiveEntities,
                LiveRelationships,
                limit
            );
            _liveForeignKeyNamesLimit = limit;
        }

        return _liveForeignKeyNames;
    }

    private IReadOnlyDictionary<Relationship, string>? _liveForeignKeyNames;

    private IdentifierLengthLimit? _liveForeignKeyNamesLimit;
}

/// <summary>
/// 差分項目から方言中立の実行計画（<see cref="SyncPlan"/>）を組み立てるサービス。
/// </summary>
/// <remarks>
/// <para>
/// 選択済みの差分を「依存関係で失敗しない固定順序」でセクション化するのが責務。方言別レンダラー
/// （<see cref="ISyncScriptBuilder"/> の実装）はこの計画を SQL へ変換するだけでよく、選択フィルタ・
/// 出力順序・種別グループ化の知識を各方言に重複させない。
/// </para>
/// <para>
/// 逐次 DDL 方言（SQL Server / PostgreSQL / MySQL / Oracle）では全差分をそのままセクション化する。
/// テーブル再構築方言（SQLite＝<see cref="SyncDialectCapabilities.SupportsAlterColumn"/> または
/// <see cref="SyncDialectCapabilities.SupportsForeignKeyAlter"/> が <c>false</c>）では、逐次 DDL で表現できない
/// 変更（列型変更・列削除・FK 変更・新規テーブルへの後付け FK）をテーブル単位の <see cref="TableRebuildPlan"/> へ
/// 集約する（合成には live スキーマが必須＝<see cref="SyncPlanContext"/> を要求する）。
/// </para>
/// </remarks>
public sealed class SyncPlanner
{
    /// <summary>
    /// セクションの出力順序。依存関係による失敗を避けるため、
    /// FK 解除 → <b>一意制約解除</b> → テーブル削除 → テーブル / 列の追加 → <b>主キー解除</b> → 列定義変更 →
    /// <b>主キー付与</b> → 列削除 → <b>一意制約追加</b> → FK 追加 → 説明設定の順に並べる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// テーブル削除（DropTable）はテーブル追加（AddTable）より前に置く。表の名前を変える同期は古い表の削除と新しい表の
    /// 追加になり、新しい表が古い表の一意制約の名前を引き継ぐと、制約名がスキーマの中で一意な方言（SQL Server・
    /// PostgreSQL・Oracle）では、追加が先だと同じ名前の制約がまだ在って失敗するため。削除する表を参照する外部キーは
    /// その前の FK 解除で外れている（<see cref="InjectVanishingForeignKeyDrops"/>）。DDL が暗黙コミットされる方言
    /// （MySQL・Oracle）では、削除の後の追加が失敗すると古い表は消えたまま残る（同期をやり直すと追加される）。
    /// </para>
    /// <para>
    /// FK 解除（DropForeignKey）を列定義変更（AlterColumn）より前に置くのは、FK が張られた列の型を変更しようとすると
    /// 依存エラーになる方言があるため（SQL Server の Msg 5074）。「FK を外す」と「同じ列の型を変える」を同時に
    /// 選択したケースを通すには、先に FK を外しておく必要がある。
    /// </para>
    /// <para>
    /// 主キー変更（AlterPrimaryKey）は <b>2 フェーズ</b>に分割し、列定義変更を挟む。旧主キー列の NULL 許容化や
    /// 主キー参加列の型変更（AlterColumn）は主キー制約が残ったままでは失敗する（SQL Server の Msg 5074 → 4922・
    /// Oracle の ORA-01451 等）ため、先に旧主キーを外す（Drop フェーズ）。新しい主キー列の NOT NULL 化は
    /// 主キー付与より先に済ませる必要があるため、付与（Add フェーズ）は列定義変更の後に置く。旧主キー列の削除
    /// （DropColumn）は主キーを外した後でなければ失敗するため、両フェーズとも列削除より前に置く。
    /// </para>
    /// <para>
    /// 依存 FK の自動 DROP → 再 ADD（<see cref="InjectImplicitForeignKeyRebuilds"/>）と合わせると、
    /// 「FK 解除 → …（テーブル削除・追加）… → 主キー解除 → 列定義変更 → 主キー付与 → …（列削除）… → FK 追加」となり、
    /// 主キーを参照する FK は主キーが存在しない区間の外側で解除・再作成される。
    /// </para>
    /// <para>
    /// 一意制約は FK の内側に置く。解除（DropUniqueConstraint）は FK 解除の直後＝構成列の定義変更・主キー変更より前
    /// （制約が残ったままでは列を変えられない方言があるため）、追加（AddUniqueConstraint）は FK 追加の直前
    /// （FK が一意制約を候補キーとして参照しうるため、FK より先に張っておく必要がある）。
    /// </para>
    /// </remarks>
    private static readonly (SchemaDiffKind Kind, PrimaryKeyPhase Phase)[] SectionOrder =
    [
        (SchemaDiffKind.DropForeignKey, PrimaryKeyPhase.None),
        (SchemaDiffKind.DropUniqueConstraint, PrimaryKeyPhase.None),
        (SchemaDiffKind.DropTable, PrimaryKeyPhase.None),
        (SchemaDiffKind.AddTable, PrimaryKeyPhase.None),
        (SchemaDiffKind.AddColumn, PrimaryKeyPhase.None),
        (SchemaDiffKind.AlterPrimaryKey, PrimaryKeyPhase.Drop),
        (SchemaDiffKind.AlterColumn, PrimaryKeyPhase.None),
        (SchemaDiffKind.AlterPrimaryKey, PrimaryKeyPhase.Add),
        (SchemaDiffKind.DropColumn, PrimaryKeyPhase.None),
        (SchemaDiffKind.AddUniqueConstraint, PrimaryKeyPhase.None),
        (SchemaDiffKind.AddForeignKey, PrimaryKeyPhase.None),
        (SchemaDiffKind.SetTableDescription, PrimaryKeyPhase.None),
        (SchemaDiffKind.SetColumnDescription, PrimaryKeyPhase.None),
    ];

    /// <summary>選択済みの差分項目から実行計画を組み立てる</summary>
    /// <param name="items">全差分項目（未選択・情報表示専用の項目を含んでよい）</param>
    /// <param name="capabilities">対象方言の同期ケーパビリティ（rebuild への振り分けに用いる）</param>
    /// <param name="context">
    /// live スキーマ。rebuild 方言は合成に、列順を同期する逐次 DDL の方言（<see cref="SyncDialectCapabilities.ColumnReorder"/> が
    /// <see cref="ColumnReorderMode.Native"/>＝MySQL）は列順の項目の移動の組み立てに用いる。ほかの逐次 DDL 方言では省いてよい
    /// （<c>null</c> 可）。rebuild 方言で <c>null</c> を渡すか、Native の方言で
    /// 列順の項目を選んで <c>null</c> を渡すと <see cref="InvalidOperationException"/> を投げる（呼び出し側のバグ）。
    /// </param>
    /// <returns>選択済み項目のみを固定順序でセクション化した計画（空セクションは含まない）</returns>
    public SyncPlan BuildPlan(
        IEnumerable<SchemaDiffItem> items,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context = null
    )
    {
        // 計画の組み立て中に見つかった注意事項（実行は止めず、表示層が実行確認へ織り込む）
        var warnings = new List<SyncPlanWarning>();

        // 利用者が選んだのに計画から外した項目（外す箇所は警告と一緒に ExcludeSelected で積む）
        var excluded = new List<SchemaDiffItem>();

        // 選択済みの項目のみを対象にする（未選択は完全に除外）。列の無い表の追加・名前の重なる表の追加・選んでいない変更を前提にする
        // 項目を外して名指しする（ExcludeUntilStable）。外部キー・一意制約の作り直しはその後で DROP と ADD の 2 項目へ展開し、
        // 以降は通常の差分として扱う（展開の前に判定する＝作り直しを外すときは削除と追加をまとめて外し、削除だけが走って制約が
        // 消えたまま残らない）
        var dependable = ExcludeUntilStable(
            [.. items.Where(i => i.IsSelected)],
            capabilities,
            context,
            warnings,
            excluded
        );

        // 選んだ列の削除で同期後に列が 1 本も残らない表は、その表の列の削除を外して名指しする（方言に依らない。
        // 依存の除外の後の項目で数えるので、外れた項目は列の追加にも削除にも数えない）
        dependable = ExcludeDropsLeavingNoColumns(dependable, context, warnings, excluded);

        // ALTER COLUMN も FK の後付けもできない方言はテーブル再構築が必要になる
        var isRebuildDialect =
            !capabilities.SupportsAlterColumn || !capabilities.SupportsForeignKeyAlter;

        // テーブル再構築の方言では、取込が捨てた表の中の物（図に無い列・外部キー）がある表を作り直す項目を外して名指しする
        // （作り直すと捨てた物がデータごと黙って消える）
        var discardedContent = isRebuildDialect
            ? CollectImportDiscardedContent(context)
            : new Dictionary<string, List<string>>(SchemaNames.Comparer);

        if (discardedContent.Count > 0)
        {
            dependable = ExcludeRebuildsLosingImportDiscardedContent(
                dependable,
                discardedContent,
                context!,
                warnings,
                excluded
            );
        }

        // 逐次 DDL の方言では、同期後に両端の型が食い違う外部キーの追加・作り直しを外して名指しする（付け直しが必ず
        // 失敗する）。再構築の方言は外部キーの型を確かめない。続けて、子の列が同期後に NOT NULL になる SET NULL の外部キーの
        // 追加・作り直しを名指しし、作れない方言（SQL Server・MySQL）では外す。どちらも展開の前に判定し、作り直しは
        // 削除と追加をまとめて外す
        var beforeExpansion = ExcludeSetNullForeignKeysOnNotNullColumns(
            isRebuildDialect
                ? dependable
                : ExcludeForeignKeysWithPostSyncTypeMismatch(
                    dependable,
                    capabilities,
                    context,
                    warnings,
                    excluded
                ),
            capabilities,
            context,
            warnings,
            excluded
        );
        var selected = ExpandAlters(beforeExpansion);

        // 主キーの変更で新しく主キーに加わる NULL 許容の列は、NOT NULL 化を選んでいなくても暗黙に NOT NULL にする
        selected = InjectPrimaryKeyNotNullChanges(selected, context);

        // 選んだ表・列の削除で端が消える live の外部キーは、削除を選んでいなくても暗黙に外す
        // （以降の段はこれらを「明示的に外す外部キー」として扱う）
        selected = InjectVanishingForeignKeyDrops(
            selected,
            context,
            includeDroppedChildTables: !isRebuildDialect,
            capabilities.IdentifierLengthLimit
        );

        // 選んだ列の削除で構成列が消える live の一意制約も、削除を選んでいなくても暗黙に外して名指しする
        selected = InjectVanishingUniqueConstraintDrops(
            selected,
            context,
            warnings,
            capabilities.IdentifierLengthLimit
        );

        // 主キーと同じ列集合の一意制約を持てない逐次 DDL の方言（Oracle）では、選んだ主キーの変更の新しい主キーと同じ列集合の
        // live の一意制約を、削除を選んでいなくても外して名指しする（再構築の方言は合成がその一意制約を省く）
        if (!isRebuildDialect && !capabilities.SupportsUniqueConstraintOnPrimaryKey)
        {
            selected = InjectUniqueConstraintsReplacedByPrimaryKey(
                selected,
                context,
                warnings,
                capabilities.IdentifierLengthLimit
            );
        }

        // 選んだ列の変更（主キーの変更に伴う暗黙の NOT NULL 化を含む）で、DB にある SET NULL の外部キーの子の列が
        // NOT NULL になるときは名指しする（方言に依らない。作れない方言で外し直さないのは暗黙の付け直しの段）
        WarnSetNullOnNotNullLiveForeignKeys(selected, capabilities, context, warnings);

        // 一意制約の削除が既存 FK の被参照列を候補キーでなくしうる場合は警告する（方言に依らず同じ危険）
        WarnUniqueConstraintDropsBreakingForeignKeys(selected, context, warnings, capabilities);

        // 選んだ外部キーの追加・作り直しの参照先が、同期後に候補キーでない場合も警告する（暗黙の付け直しと同じ証明）
        WarnAddedForeignKeysNotOnCandidateKey(selected, capabilities, context, warnings);

        // SET DEFAULT の外部キーの NOT NULL の子の列は、DEFAULT 制約が無いと作れない方言（SQL Server）で名指しする
        WarnSetDefaultOnNotNullColumns(selected, capabilities, warnings);

        // ドットを含む表名を表せない方言（SQLite）では、選んだ項目が触れるその表を名指しする
        WarnSchemaQualifiedTableNames(selected, capabilities, warnings);

        // 既定値のある列の型の種類を変える列の変更を名指しする（SQL Server・PostgreSQL・Oracle）
        WarnDefaultsOnColumnTypeKindChange(selected, capabilities, context, warnings);

        // 説明の BMP の外の文字を保存できない方言（MySQL）では、その文字を書く説明の項目を名指しする
        WarnDescriptionCharactersReplaced(selected, capabilities, warnings);

        if (!isRebuildDialect)
        {
            // 逐次 DDL 方言: 全差分をそのままセクション化する。
            // 列順変更（ReorderColumns）は SectionOrder に無いためセクションからは自然に外れ、
            // Native 方言（MySQL）のときだけネイティブ MODIFY ... AFTER の並べ替え計画へ変換する
            // （None 方言では Compute が ReorderColumns を生成しないため、渡されても計画から消える）
            var reorders =
                capabilities.ColumnReorder == ColumnReorderMode.Native
                    ? BuildReorderPlans(selected, context)
                    : [];

            // 主キー変更・（方言によっては）列定義変更に巻き込まれる live FK を、暗黙の DROP → 再 ADD として注入する
            var sectionItems = InjectImplicitForeignKeyRebuilds(
                selected,
                capabilities,
                context,
                warnings
            );

            // 列定義変更に巻き込まれる live の一意制約も、同じ流儀で暗黙の DROP → 再 ADD として注入する
            sectionItems = InjectImplicitUniqueConstraintRebuilds(
                sectionItems,
                selected,
                capabilities,
                context
            );

            // 列定義変更に巻き込まれる live の主キーは、同じ構成のままの主キー変更として外して付け直す
            sectionItems = InjectImplicitPrimaryKeyRebuilds(
                sectionItems,
                selected,
                capabilities,
                context
            );

            // 参照する列の並びが参照先のキーの並びと一致しないと外部キーを作れない方言（SQL Server・MySQL）では、
            // 外部キーの追加（明示の追加・作り直し・暗黙の付け直し）を参照先の同期後のキーの並びへ並べ替える
            if (capabilities.ReferencedKeyRequiresSameColumnOrder)
            {
                sectionItems = ArrangeForeignKeysToPostSyncKeys(sectionItems, selected, context);
            }

            var sections = BuildSections(sectionItems);

            // DDL が暗黙コミットされる方言（MySQL・Oracle）では、失敗しても消えたまま残る制約を作らないよう失敗点を並べる：
            // 外部キーの追加は断られうるものほど後ろへ（付け直し → 作り直し → 新しい外部キー → 候補キーを証明できない追加）、必ず
            // 断られる文は計画の先頭へ
            if (!capabilities.TransactionalDdl)
            {
                sections = MoveCertainFailureFirst(
                    ArrangeForeignKeyAddsByFailureRisk(
                        sections,
                        selected,
                        context,
                        capabilities.IdentifierLengthLimit
                    ),
                    sectionItems,
                    [.. beforeExpansion.Where(i => i.Kind == SchemaDiffKind.AlterUniqueConstraint)],
                    capabilities,
                    context,
                    warnings,
                    excluded
                );

                // 既存の行で断られうる文（列が空でないと通らない列の変更を含む）の前で外し、その文か後ろで付け直す制約を名指しする
                WarnConstraintsAroundRefusableChanges(
                    sections,
                    beforeExpansion,
                    capabilities,
                    context,
                    warnings
                );
            }

            return new SyncPlan
            {
                Sections = sections,
                Reorders = reorders,
                Warnings = warnings,
                ExcludedItems = excluded,
            };
        }

        if (context is null)
        {
            // rebuild 合成には live スキーマが必須（図の定義を直接使うと未選択の変更が紛れ込むため）
            throw new InvalidOperationException(
                "Rebuild dialects require a SyncPlanContext (live schema) to synthesize table rebuilds."
            );
        }

        return BuildRebuildPlan(
            selected,
            capabilities,
            context,
            warnings,
            excluded,
            capabilities.IdentifierLengthLimit
        );
    }

    /// <summary>
    /// 選択済みの作り直し項目を、外す側と付ける側の 2 項目へ展開する（それ以外の項目は順序を保ってそのまま返す）。
    /// <see cref="SchemaDiffKind.AlterForeignKey"/> は DropForeignKey と AddForeignKey へ、
    /// <see cref="SchemaDiffKind.AlterUniqueConstraint"/> は DropUniqueConstraint と AddUniqueConstraint へ展開する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 計画のセクション（<see cref="SectionOrder"/>）にも方言別レンダラーにも作り直しの種別は無いので、ここで展開しないと
    /// 選んだ作り直しが黙って計画から消える。
    /// </para>
    /// <para>
    /// 展開した外部キーの DROP は「明示的に選んだ削除」として扱われるため、暗黙の外し直し（主キー変更・列定義変更に伴う
    /// 自動 DROP / 再 ADD）とは重複せず、旧い定義を付け直すこともない。展開した一意制約の DROP も、明示的に選んだ削除と
    /// 同じく、外部キーを支える索引の喪失（MySQL）・候補キーの喪失の警告・同期後の一意制約の集合の計算に乗る。
    /// 再構築の方言では同じ表の外部キー・一意制約の集合の合成（live − DROP ＋ ADD）にそのまま乗る。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> ExpandAlters(IEnumerable<SchemaDiffItem> selected)
    {
        var expanded = new List<SchemaDiffItem>();

        foreach (var item in selected)
        {
            if (item.Kind == SchemaDiffKind.AlterUniqueConstraint)
            {
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
                    }
                );
                continue;
            }

            if (item.Kind != SchemaDiffKind.AlterForeignKey)
            {
                expanded.Add(item);
                continue;
            }

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
                }
            );
        }

        return expanded;
    }

    /// <summary>
    /// 選んだ項目のうち、選んでいない変更を前提にするものを外し、
    /// <see cref="SyncPlanWarningKind.SelectedItemRequiresUnselectedItem"/> で名指しする。
    /// </summary>
    /// <param name="selected">選択済みの項目（外部キーの作り直しは展開前）</param>
    /// <param name="capabilities">型の比較に使う方言のケーパビリティ</param>
    /// <param name="context">live スキーマ（無ければ何も外さない）</param>
    /// <param name="warnings">外した項目の警告を積む先</param>
    /// <returns>残す項目（型の変更も含む列の変更で、NULL 許容化だけを外した項目は複製に置き換わる）</returns>
    /// <remarks>
    /// <para>
    /// 前提にする変更は次のとおり。表・列は live に在るか、同じ計画で追加を選んでいれば在るとみなす
    /// （追加を選んだ表の列はすべて在る）。
    /// </para>
    /// <list type="bullet">
    /// <item>外部キーの追加・作り直し: 子表・親表と、両端の構成列。参照先の列が図の主キーとだけ一致し、同期後の主キー
    /// （選んだ主キーの変更か live の主キー）にも一意制約にも一致しないなら、主キーの変更（選んでいない主キーの変更を
    /// 前提にしている。比べるのは列の集合＝並びの違いは同期が参照先のキーの並びへ並べ替える）</item>
    /// <item>主キーの変更: 新しい主キーの列（「新しい列を選ばずに主キー変更だけ選ぶ」と、SQLite の再構築は無い列を
    /// 読み飛ばして部分的な主キーのまま成功していた）</item>
    /// <item>一意制約の追加・作り直し: 構成列（SQLite の再構築は解決できない制約を黙って捨てていた）。主キーと同じ列集合の一意制約を
    /// 持てない方言（<see cref="SyncDialectCapabilities.SupportsUniqueConstraintOnPrimaryKey"/>）では、同期後の主キーと同じ
    /// 列集合なら主キーの変更（差分は図の主キーと同じ列集合を案内項目にするので、ここに来るのは選んでいない主キーの変更で
    /// live の主キーが残る形）</item>
    /// <item>表・列の説明: その表・列</item>
    /// <item>列を NULL 許容にする変更: 同期後もその列が主キーに残るなら、主キーから外す変更（逐次 DDL の方言は
    /// 失敗し、SQLite は黙って NOT NULL のまま）。型の変更も含む項目は、NULL 許容化だけを外した複製で残す</item>
    /// </list>
    /// <para>
    /// 依存先を自動で選ぶことはしない（利用者が選ばなかった変更、特に破壊的な変更を入れることになるため）。
    /// 外した項目はセクションにも再構築にも入らない。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> ExcludeItemsRequiringUnselected(
        List<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        if (context is null)
        {
            return selected;
        }

        var liveByName = IndexLiveTables(context);
        var addedTables = selected
            .Where(i => i.Kind == SchemaDiffKind.AddTable)
            .Select(i => SchemaNames.ToStoredForm(i.TableName))
            .ToHashSet(SchemaNames.Comparer);
        var addedColumns = selected
            .Where(i => i.Kind == SchemaDiffKind.AddColumn && i.ColumnName is not null)
            .Select(i => ColumnKey(i.TableName, i.ColumnName!))
            .ToHashSet();
        // この段で外す主キーの変更（構成列が同期後に無い）は、同期後の主キーに数えない（数えると、その変更の後にだけ
        // 主キーから外れる列の削除を残してしまう）
        var primaryKeyChanges = selected
            .Where(i =>
                i.Kind == SchemaDiffKind.AlterPrimaryKey
                && i.Entity is not null
                && HasAllColumns(
                    SchemaNames.ToStoredForm(i.TableName),
                    i.Entity.GetPrimaryKeyColumnsInOrder().Select(c => c.Name)
                )
            )
            .GroupBy(i => SchemaNames.ToStoredForm(i.TableName), SchemaNames.Comparer)
            .ToDictionary(
                g => g.Key,
                g => g.First().Entity!.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList(),
                SchemaNames.Comparer
            );
        var addedEntities = CollectAddedTables(selected);
        var postSyncUniques = BuildPostSyncUniqueConstraints(ExpandAlters(selected), context);
        // 選んだ削除・作り直しで外す DB の一意制約（照合キー）
        var droppedUniques = ExplicitlyDroppedUniqueConstraints(ExpandAlters(selected));
        // 同期後も残る DB の外部キー・一意制約（選んだ削除・作り直しで外れず、表・構成列も消えないもの）。名前・定義の重なりの判定に使う
        var remaining = RemainingLiveConstraints.From(
            selected,
            droppedUniques,
            capabilities.IdentifierLengthLimit,
            context
        );

        var kept = new List<SchemaDiffItem>(selected.Count);

        foreach (var item in selected)
        {
            var (subject, missing) = FindMissingDependencies(item);

            if (missing.Count == 0)
            {
                kept.Add(item);
                continue;
            }

            ExcludeSelected(
                item,
                new SyncPlanWarning(
                    SyncPlanWarningKind.SelectedItemRequiresUnselectedItem,
                    SchemaNames.ToStoredForm(item.TableName),
                    $"{subject} ({string.Join(", ", missing)})"
                ),
                warnings,
                excluded
            );

            // 型の変更も含む列の変更は、NULL 許容化だけを外して型の変更は残す
            if (
                item.Kind == SchemaDiffKind.AlterColumn
                && item.Column is not null
                && item.OldColumn is not null
                && !capabilities.AreSameType(item.OldColumn.DataType, item.Column.DataType)
            )
            {
                var column = item.Column.Clone(preserveId: true);
                column.IsNullable = ColumnNullability.AllowsNull(item.OldColumn);
                kept.Add(
                    new SchemaDiffItem
                    {
                        Kind = item.Kind,
                        TableName = item.TableName,
                        ColumnName = item.ColumnName,
                        Entity = item.Entity,
                        Column = column,
                        OldColumn = item.OldColumn,
                        // 説明も型の変更だけにする（元の説明は外した NULL 許容化を含む）
                        Description = SchemaDiffService.DescribeColumnTypeChange(
                            item.TableName,
                            item.ColumnName!,
                            item.OldColumn.DataType,
                            column.DataType
                        ),
                    }
                );
            }
        }

        return kept;

        (string Subject, List<string> Missing) FindMissingDependencies(SchemaDiffItem item)
        {
            var table = SchemaNames.ToStoredForm(item.TableName);
            var missing = new List<string>();

            switch (item.Kind)
            {
                case SchemaDiffKind.AddForeignKey
                or SchemaDiffKind.AlterForeignKey
                    when item.ChildEntity is not null && item.ParentEntity is not null:
                {
                    var child = SchemaDiffService.NormalizeTable(item.ChildEntity);
                    var parent = SchemaDiffService.NormalizeTable(item.ParentEntity);
                    var pairs = SyncScriptBuilderHelper.ResolveColumnPairs(item);
                    RequireColumns(
                        child,
                        ForeignKeyColumnPairResolver.ChildColumns(pairs),
                        missing
                    );
                    RequireColumns(
                        parent,
                        ForeignKeyColumnPairResolver.ParentColumns(pairs),
                        missing
                    );

                    // 参照先の列が図の主キーとだけ一致し、同期後の主キーにも一意制約にも一致しないなら、
                    // 選んでいない主キーの変更を前提にしている（比べるのは列の集合）
                    if (
                        missing.Count == 0
                        && !addedTables.Contains(parent)
                        && RequiresPrimaryKeyChange(
                            parent,
                            item.ParentEntity,
                            ForeignKeyColumnPairResolver.ParentColumns(pairs).ToList()
                        )
                    )
                    {
                        missing.Add("PRIMARY KEY");
                    }

                    // 名指しの名前はスクリプトが付ける名前（警告の名前の正本を 1 か所にする）
                    var name = SelectedForeignKeyName(item, capabilities.IdentifierLengthLimit);

                    // 同期後も残る DB の制約と名前が重なる追加・同じ定義の外部キーを 2 本持てない方言での同じ定義の追加は、その制約の
                    // 削除を前提にする（外さずに足すと、名前の重なり・定義の重なりで断られる）
                    if (missing.Count == 0)
                    {
                        missing.AddRange(
                            remaining.NamedLike(name, child, isForeignKey: true, capabilities)
                        );
                        missing.AddRange(
                            remaining.SameDefinitionAs(child, parent, pairs, capabilities)
                        );
                    }

                    return (name, missing);
                }

                case SchemaDiffKind.AlterPrimaryKey when item.Entity is not null:
                    RequireColumns(
                        table,
                        item.Entity.GetPrimaryKeyColumnsInOrder().Select(c => c.Name),
                        missing
                    );
                    return ("PRIMARY KEY", missing);

                case SchemaDiffKind.AddUniqueConstraint
                or SchemaDiffKind.AlterUniqueConstraint:
                    RequireColumns(table, item.UniqueConstraintColumns, missing);

                    // 主キーと同じ列集合の一意制約を持てない方言では、同期後の主キー（選んでいない主キーの変更なら
                    // live の主キー）と同じ列集合の追加は、主キーの変更を前提にする
                    if (
                        missing.Count == 0
                        && !capabilities.SupportsUniqueConstraintOnPrimaryKey
                        && IsSameColumnSet(PostSyncPrimaryKey(table), item.UniqueConstraintColumns)
                    )
                    {
                        missing.Add("PRIMARY KEY");
                    }

                    // 同じ列集合の live の一意制約が同期後も残るなら、その一意制約を外す変更（削除・作り直し）を前提にする
                    // （外さずに足すと、同じ列に一意制約が重なる。Oracle は ORA-02261 で断る）
                    if (missing.Count == 0)
                    {
                        missing.AddRange(
                            RemainingLiveUniquesOn(table, item.UniqueConstraintColumns)
                        );
                    }

                    var uniqueName = UniqueConstraintNaming.Resolve(
                        item.UniqueConstraintName,
                        table,
                        item.UniqueConstraintColumns,
                        capabilities.IdentifierLengthLimit
                    );

                    // 同期後も残る DB の制約と、方言の名前の範囲で名前が重なるなら、その制約の削除を前提にする
                    if (missing.Count == 0)
                    {
                        missing.AddRange(
                            remaining.NamedLike(
                                uniqueName,
                                table,
                                isForeignKey: false,
                                capabilities
                            )
                        );
                    }

                    return (uniqueName, missing);

                case SchemaDiffKind.SetTableDescription:
                    RequireColumns(table, [], missing);
                    return ("COMMENT", missing);

                case SchemaDiffKind.SetColumnDescription when item.ColumnName is not null:
                    RequireColumns(table, [item.ColumnName], missing);
                    return (item.ColumnName, missing);

                case SchemaDiffKind.AlterColumn
                    when item.ColumnName is not null
                        && item.Column is not null
                        && ColumnNullability.AllowsNull(item.Column)
                        && item.OldColumn is not null
                        && !ColumnNullability.AllowsNull(item.OldColumn)
                        && StaysInPrimaryKey(table, item.ColumnName):
                    missing.Add("PRIMARY KEY");
                    return (item.ColumnName, missing);

                // 同期後も主キーに入っている列の削除は、その列を外す主キーの変更を前提にする（選ばずに消すと、
                // 方言によって主キーが黙って消える・縮む・削除が断られる）
                case SchemaDiffKind.DropColumn
                    when item.ColumnName is not null && StaysInPrimaryKey(table, item.ColumnName):
                    missing.Add("PRIMARY KEY");
                    return (item.ColumnName, missing);

                default:
                    return (string.Empty, missing);
            }
        }

        // 表と列が同期後にすべて在るか
        bool HasAllColumns(string table, IEnumerable<string> columns)
        {
            var missing = new List<string>();
            RequireColumns(table, columns, missing);
            return missing.Count == 0;
        }

        // 表と列が同期後に在るか（無いものを missing へ「表」「表.列」で足す。自己参照の外部キーは子と親で同じ表を 2 回
        // 調べるので、すでに足したものは重ねない）
        void RequireColumns(string table, IEnumerable<string> columns, List<string> missing)
        {
            if (addedTables.Contains(table))
            {
                return;
            }

            if (!liveByName.TryGet(table, out var live))
            {
                AddOnce(table);
                return;
            }

            foreach (var column in columns)
            {
                if (
                    !addedColumns.Contains(ColumnKey(table, column))
                    && !live.Columns.Any(c => SchemaNames.AreSame(c.Name, column))
                )
                {
                    AddOnce($"{table}.{column}");
                }
            }

            void AddOnce(string name)
            {
                if (!missing.Contains(name, StringComparer.Ordinal))
                {
                    missing.Add(name);
                }
            }
        }

        // 列集合が同じ live の一意制約のうち、選んだ削除・作り直しで外れないものの名前（照合は DB の一意制約の照合キー）
        IEnumerable<string> RemainingLiveUniquesOn(string table, IReadOnlyList<string> columns)
        {
            if (!liveByName.TryGet(table, out var live) || columns.Count == 0)
            {
                yield break;
            }

            foreach (var constraint in live.UniqueConstraints)
            {
                if (
                    UniqueConstraintNaming.TryResolveColumnNames(
                        live,
                        constraint,
                        out var liveColumns
                    )
                    && IsSameColumnSet(liveColumns, columns)
                    && !droppedUniques.Contains(
                        UniqueConstraintKey(table, constraint.Name, liveColumns)
                    )
                )
                {
                    yield return UniqueConstraintNaming.Resolve(
                        constraint.Name,
                        table,
                        liveColumns,
                        capabilities.IdentifierLengthLimit
                    );
                }
            }
        }

        // 同期後もこの列が主キーに入っているか（主キーの変更を選んでいればその構成、無ければ live の構成）
        bool StaysInPrimaryKey(string table, string column) =>
            PostSyncPrimaryKey(table).Any(c => SchemaNames.AreSame(c, column));

        // 外部キーの参照先の列が図の主キーとだけ一致し、同期後の主キーにも一意制約にも一致しないか（選んでいない主キーの変更を
        // 前提にしているか。比べるのは列の集合）
        bool RequiresPrimaryKeyChange(
            string parentTable,
            Entity targetParent,
            IReadOnlyList<string> referencedColumns
        )
        {
            var targetKey = targetParent.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList();

            return targetKey.Count > 0
                && string.Equals(
                    UniqueConstraintNaming.ColumnSetSignature(targetKey),
                    UniqueConstraintNaming.ColumnSetSignature(referencedColumns),
                    StringComparison.Ordinal
                )
                && !StaysCandidateKey(
                    PostSyncPrimaryKey(parentTable),
                    postSyncUniques,
                    parentTable,
                    referencedColumns
                );
        }

        // 同期後の主キーの列名（この段で外さない主キーの変更を選んでいればその構成、追加する表は図の構成、無ければ live の構成）
        IReadOnlyList<string> PostSyncPrimaryKey(string table) =>
            PostSyncPrimaryKeyOf(table, primaryKeyChanges, addedEntities, liveByName) ?? [];
    }

    /// <summary>
    /// 列の無い表の追加（<see cref="SchemaDiffKind.AddTable"/>）を計画から外し、<see cref="SyncPlanWarningKind.TableWithoutColumns"/>
    /// で名指しする（方言に依らない）。
    /// </summary>
    /// <remarks>
    /// 列の無い表は <c>CREATE TABLE t ( );</c> になり、どの方言でも正しく作れない（判定と理由は <see cref="SqlTableShape"/>）。
    /// 図からの DDL は同じ判定で図全体を止めるが、同期は表の追加 1 つを外せばほかの変更を実行できるので、隣の
    /// <see cref="ExcludeDropsLeavingNoColumns"/> と同じく外して名指しする。依存の除外（<see cref="ExcludeItemsRequiringUnselected"/>）
    /// より前に置き、外した表の追加を前提にする項目はそこで外す。
    /// </remarks>
    private static List<SchemaDiffItem> ExcludeTablesWithoutColumns(
        List<SchemaDiffItem> selected,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        foreach (var add in selected.Where(SqlTableShape.IsAddTableWithoutColumns))
        {
            ExcludeSelected(
                add,
                new SyncPlanWarning(
                    SyncPlanWarningKind.TableWithoutColumns,
                    SchemaNames.ToStoredForm(add.TableName),
                    string.Empty
                ),
                warnings,
                excluded
            );
        }

        return [.. selected.Where(i => !SqlTableShape.IsAddTableWithoutColumns(i))];
    }

    /// <summary>
    /// 選んだ項目のうち、計画に載せられない項目を外して名指しする：列の無い表の追加（<see cref="ExcludeTablesWithoutColumns"/>）・
    /// 名前の重なる表の追加（<see cref="ExcludeTablesWithCollidingConstraintNames"/>）・DB のビューを読めなくする変更
    /// （<see cref="ExcludeChangesBreakingLiveViews"/>）・選んでいない変更を前提にする項目（<see cref="ExcludeItemsRequiringUnselected"/>）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 列の無い表の追加は項目そのものだけで決まるので 1 回だけ外す。外した表の追加は選ばなかった項目と同じに扱い、続く段がそれを
    /// 前提にする項目を外す。
    /// </para>
    /// <para>
    /// 名前の重なる表の追加・ビューを読めなくする変更・依存の除外は、何も外れなくなるまで繰り返す（ビューの判定は外した列の追加を前提にする
    /// 項目を同じ周の依存の除外が外せるよう、その前に置く。依存の除外が列の追加だけを外すと残った列の削除がビューを読めなくしうるので、
    /// 次の周でもう一度試す）。名前の重なりの判定（<see cref="RemainingLiveConstraints"/>）は
    /// 選んだ表・列の削除で消える制約を「残らない」と数えるが、その削除自身が同じ段で外されることがある（主キーの変更を選ばない
    /// 主キーの列の削除）。1 回で止めると、外された削除の表・列の制約が残るのに、その名前と重なる追加が外されずに断られる。
    /// 繰り返しは必ず止まる：周ごとに少なくとも 1 つの選んだ項目を外したときだけ次の周へ進み、外すほど残る制約は増えるだけ
    /// （外した項目が戻ることはない）で、選んだ項目の数は有限。NULL 許容化だけを外して型の変更を残す列の変更は、残した項目が
    /// 次の周で外れない（NULL 許容化を含まない）。周の数が選んだ項目の数を超えたら、止まる理由が崩れたので例外にする（途中の計画を
    /// 返さない）。どの周も、その周で外した項目だけを名指しするので、警告は重ならず、並びは周の順で決まる。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> ExcludeUntilStable(
        List<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        var current = ExcludeTablesWithoutColumns(selected, warnings, excluded);

        for (var round = 0; round <= selected.Count; round++)
        {
            var excludedBefore = excluded.Count;
            current = ExcludeItemsRequiringUnselected(
                ExcludeChangesBreakingLiveViews(
                    ExcludeTablesWithCollidingConstraintNames(
                        current,
                        capabilities,
                        context,
                        warnings,
                        excluded
                    ),
                    context,
                    warnings,
                    excluded
                ),
                capabilities,
                context,
                warnings,
                excluded
            );

            if (excluded.Count == excludedBefore)
            {
                return current;
            }
        }

        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Excluding the selected items did not settle within {selected.Count + 1} rounds."
            )
        );
    }

    /// <summary>
    /// DB のビューが読む表の列を消す・足す変更と表の削除のうち、ビューを読めなくするものを計画から外し、
    /// <see cref="SyncPlanWarningKind.TableChangeBreaksDependentView"/> で表ごとに名指しする（<see cref="SyncPlanContext.LiveViewCheck"/> が
    /// 無ければ何もしない）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ビューは図に無いので、同期はビューを直さない。ビューが読む列・表を消すと（列の並びを書いた <c>SELECT *</c> のビューは列を足しても）
    /// ビューが読めなくなり、実行器が同期の前後の比較で見つけて同期ごと巻き戻す。ビューが読めるかは表の列の名前の集合と表の有無だけで
    /// 決まるので、外すのはその表の列の削除・列の追加・表の削除だけで、型・主キー・制約などの変更は流す。
    /// </para>
    /// <para>
    /// 判定は DB 自身に試させる（<see cref="ILiveViewCheck"/>。ビューの SQL を解析しない）。まず計画全体の同期後の列名で試し、読めなく
    /// なるビューが無ければ何も外さない（<c>SELECT *</c> のビューの列の削除・列の数が変わらない名前の変更などに偽の警告を出さない）。
    /// 読めなくなるときだけ、表ごとに試して原因の表を決め、その表の名前を変える項目を外す。表どうしの組み合わせでだけ読めなくなるときは、
    /// 名前を変える表をすべて原因とする。外した後の計画は <see cref="ExcludeUntilStable"/> の次の周でもう一度試す。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> ExcludeChangesBreakingLiveViews(
        List<SchemaDiffItem> selected,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        if (context?.LiveViewCheck is not { } check)
        {
            return selected;
        }

        var changes = PostSyncColumnNames(selected, IndexLiveTables(context));

        if (changes.Count == 0 || check.ViewsBrokenBy(ToTableChanges(changes)).Count == 0)
        {
            return selected;
        }

        // 表ごとに試して原因の表を決める（表 → 読めなくするビュー）
        var culprits = new Dictionary<string, (string Table, IReadOnlyList<string> Views)>(
            SchemaNames.Comparer
        );

        foreach (var change in changes)
        {
            var views = check.ViewsBrokenBy(ToTableChanges([change]));

            if (views.Count > 0)
            {
                culprits[change.Table] = (change.Table, views);
            }
        }

        // 表どうしの組み合わせでだけ読めなくなる（どの表の変更も単独では読めなくしない）ときは、名前を変える表をすべて原因とする。
        // この枝を通す形は見つかっておらず、行で通していない守りの無い枝（src/QuickER.Provider.Sqlite/CLAUDE.md「計画の段のビューの判定」）。
        // 作れる形が見つかったら、同期計画の期待の行の実 SQLite の段へ行を足す
        if (culprits.Count == 0)
        {
            var views = check.ViewsBrokenBy(ToTableChanges(changes));

            foreach (var change in changes)
            {
                culprits[change.Table] = (change.Table, views);
            }
        }

        var kept = new List<SchemaDiffItem>(selected.Count);

        foreach (var item in selected)
        {
            if (
                IsColumnNameSetChange(item.Kind)
                && culprits.TryGetValue(SchemaNames.ToStoredForm(item.TableName), out var culprit)
            )
            {
                ExcludeSelected(
                    item,
                    new SyncPlanWarning(
                        SyncPlanWarningKind.TableChangeBreaksDependentView,
                        culprit.Table,
                        string.Join(", ", culprit.Views)
                    ),
                    warnings,
                    excluded
                );
                continue;
            }

            kept.Add(item);
        }

        return kept;

        static IReadOnlyDictionary<string, IReadOnlyList<string>?> ToTableChanges(
            IEnumerable<(string Table, IReadOnlyList<string>? Columns)> tables
        ) => tables.ToDictionary(t => t.Table, t => t.Columns, SchemaNames.Comparer);
    }

    /// <summary>表の列の名前の集合か表の有無を変える項目の種別（列の削除・列の追加・表の削除）か</summary>
    private static bool IsColumnNameSetChange(SchemaDiffKind kind) =>
        kind is SchemaDiffKind.DropColumn or SchemaDiffKind.AddColumn or SchemaDiffKind.DropTable;

    /// <summary>
    /// 選んだ項目で列の名前の集合が変わる live の表と、その同期後の列名（表の削除は <c>null</c>）を、項目に最初に現れた順に返す
    /// </summary>
    /// <remarks>
    /// 同期後の列名は「live の列 − 選んだ列の削除 ＋ 選んだ列の追加」（<see cref="ExcludeDropsLeavingNoColumns"/> と同じ数え方）。
    /// 列が 1 本も残らない表は数えない（その削除は <see cref="ExcludeDropsLeavingNoColumns"/> が外す）。live に無い表（追加する表）は
    /// ビューが読んでいないので数えない。表の名前は live の綴りで返す。
    /// </remarks>
    private static List<(string Table, IReadOnlyList<string>? Columns)> PostSyncColumnNames(
        List<SchemaDiffItem> selected,
        NameLookup<Entity> liveByName
    )
    {
        var changes = new List<(string Table, IReadOnlyList<string>? Columns)>();

        foreach (
            var items in selected
                .Where(i => IsColumnNameSetChange(i.Kind))
                .GroupBy(i => SchemaNames.ToStoredForm(i.TableName), SchemaNames.Comparer)
        )
        {
            if (!liveByName.TryGet(items.Key, out var live))
            {
                continue;
            }

            if (items.Any(i => i.Kind == SchemaDiffKind.DropTable))
            {
                changes.Add((live.TableName, null));
                continue;
            }

            var dropped = items
                .Where(i => i.Kind == SchemaDiffKind.DropColumn && i.ColumnName is not null)
                .Select(i => i.ColumnName!)
                .ToList();
            var columns = live
                .Columns.Select(c => c.Name)
                .Where(name => !dropped.Any(d => SchemaNames.AreSame(d, name)))
                .ToList();

            foreach (
                var added in items
                    .Where(i => i.Kind == SchemaDiffKind.AddColumn)
                    .Select(i => i.Column?.Name ?? i.ColumnName)
            )
            {
                if (added is not null && !columns.Any(c => SchemaNames.AreSame(c, added)))
                {
                    columns.Add(added);
                }
            }

            if (columns.Count > 0)
            {
                changes.Add((live.TableName, columns));
            }
        }

        return changes;
    }

    /// <summary>
    /// 一意制約の名前が、同期後も残る DB の制約と方言の名前の範囲で重なる表の追加（<see cref="SchemaDiffKind.AddTable"/>）を、その制約の削除を
    /// 前提にする項目として計画から外し、<see cref="SyncPlanWarningKind.SelectedItemRequiresUnselectedItem"/> で名指しする
    /// </summary>
    /// <remarks>
    /// 判定は外部キー・一意制約の追加と同じ（<see cref="RemainingLiveConstraints"/>・<see cref="SyncDialectCapabilities.ConstraintNamesCollide"/>）。
    /// 表の名前を変えて古い表の一意制約の名前を新しい表が引き継ぐ図を、既定の選び方（表の追加を選び、古い表の削除を選ばない）で流すと、
    /// 新しい表の作成が名前の重なりで断られる。主キーと同じ列の一意制約を持てない方言で、表の作成が省く一意制約は数えない。主キーの名前は
    /// 取り込まないので数えない。依存の除外（<see cref="ExcludeItemsRequiringUnselected"/>）より前に置き、外した表の追加を前提にする項目
    /// （その表の外部キー・説明）はそこで外す（列の無い表の追加の除外と同じ位置）。Detail は「一意制約の名前 (残る制約の表.名前)」。
    /// </remarks>
    private static List<SchemaDiffItem> ExcludeTablesWithCollidingConstraintNames(
        List<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        if (context is null || capabilities.ConstraintNameScope == ConstraintNameScope.None)
        {
            return selected;
        }

        var limit = capabilities.IdentifierLengthLimit;
        var remaining = RemainingLiveConstraints.From(
            selected,
            ExplicitlyDroppedUniqueConstraints(ExpandAlters(selected)),
            limit,
            context
        );
        var dropped = new HashSet<SchemaDiffItem>(ReferenceEqualityComparer.Instance);

        foreach (
            var add in selected.Where(i =>
                i.Kind == SchemaDiffKind.AddTable && i.Entity is not null
            )
        )
        {
            var table = SchemaNames.ToStoredForm(add.TableName);
            var primaryKey = add.Entity!.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList();
            var collisions = UniqueConstraintNaming
                .ResolveAll(add.Entity, add.UniqueConstraintNames, limit)
                .Where(u =>
                    capabilities.SupportsUniqueConstraintOnPrimaryKey
                    || !IsSameColumnSet(primaryKey, u.ColumnNames)
                )
                .Select(u =>
                    (
                        u.Name,
                        Remaining: remaining
                            .NamedLike(u.Name, table, isForeignKey: false, capabilities)
                            .ToList()
                    )
                )
                .Where(c => c.Remaining.Count > 0)
                .ToList();

            if (collisions.Count == 0)
            {
                continue;
            }

            dropped.Add(add);
            ExcludeSelected(
                add,
                new SyncPlanWarning(
                    SyncPlanWarningKind.SelectedItemRequiresUnselectedItem,
                    table,
                    string.Join(
                        ", ",
                        collisions.Select(c => $"{c.Name} ({string.Join(", ", c.Remaining)})")
                    )
                ),
                warnings,
                excluded
            );
        }

        return dropped.Count == 0 ? selected : [.. selected.Where(i => !dropped.Contains(i))];
    }

    /// <summary>
    /// 選んだ列の削除（<see cref="SchemaDiffKind.DropColumn"/>）で、同期後に残る表の列が 1 本も無くなる表について、その表の選んだ列の
    /// 削除をすべて計画から外し、<see cref="SyncPlanWarningKind.TableLeftWithoutColumns"/> で名指しする（方言に依らない）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同期後の列の数は「DB の列 − 選んだ列の削除 ＋ 選んだ列の追加」で数える（削除を選んだ表は数えない＝表ごと消える）。
    /// 表の列をすべて消す変更は、どの方言でも正しく扱えない：SQL Server・MySQL・Oracle は最後の列の削除を断り、DDL が暗黙コミット
    /// される MySQL・Oracle ではそれより前の文（一意制約・主キー・ほかの列の削除）が残る。PostgreSQL は列の無い表を作り、取り込み
    /// 直すと列を読めない表として警告になる。SQLite は列の無い再構築の表を作れない。
    /// </para>
    /// <para>
    /// 外すのは列の削除だけで、同じ表の主キーの解除・一意制約の削除は単独で正しいので残す。依存の除外
    /// （<see cref="ExcludeItemsRequiringUnselected"/>）の後で呼び、列の削除に伴う暗黙の外部キー・一意制約の削除を注入する段より前に置く
    /// （外した列を前提にした暗黙の削除を足さない）。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> ExcludeDropsLeavingNoColumns(
        List<SchemaDiffItem> selected,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        if (context is null)
        {
            return selected;
        }

        var liveByName = IndexLiveTables(context);
        var droppedTables = selected
            .Where(i => i.Kind == SchemaDiffKind.DropTable)
            .Select(i => SchemaNames.ToStoredForm(i.TableName))
            .ToHashSet(SchemaNames.Comparer);
        var tablesWithAddedColumns = selected
            .Where(i => i.Kind == SchemaDiffKind.AddColumn)
            .Select(i => SchemaNames.ToStoredForm(i.TableName))
            .ToHashSet(SchemaNames.Comparer);
        var emptied = new HashSet<string>(SchemaNames.Comparer);

        foreach (
            var drops in selected
                .Where(i => i.Kind == SchemaDiffKind.DropColumn && i.ColumnName is not null)
                .GroupBy(i => SchemaNames.ToStoredForm(i.TableName), SchemaNames.Comparer)
        )
        {
            if (
                droppedTables.Contains(drops.Key)
                || tablesWithAddedColumns.Contains(drops.Key)
                || !liveByName.TryGet(drops.Key, out var live)
            )
            {
                continue;
            }

            var remains = live.Columns.Any(c =>
                !drops.Any(d => SchemaNames.AreSame(d.ColumnName!, c.Name))
            );

            if (!remains)
            {
                emptied.Add(drops.Key);
            }
        }

        if (emptied.Count == 0)
        {
            return selected;
        }

        bool IsExcluded(SchemaDiffItem item) =>
            item.Kind == SchemaDiffKind.DropColumn
            && emptied.Contains(SchemaNames.ToStoredForm(item.TableName));

        foreach (
            var drops in selected
                .Where(IsExcluded)
                .GroupBy(i => SchemaNames.ToStoredForm(i.TableName), SchemaNames.Comparer)
        )
        {
            var warning = new SyncPlanWarning(
                SyncPlanWarningKind.TableLeftWithoutColumns,
                drops.Key,
                string.Join(", ", drops.Select(d => d.ColumnName))
            );

            foreach (var drop in drops)
            {
                ExcludeSelected(drop, warning, warnings, excluded);
            }
        }

        return [.. selected.Where(i => !IsExcluded(i))];
    }

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
        SyncPlanContext? context
    )
    {
        if (context is null)
        {
            return selected;
        }

        var liveByName = IndexLiveTables(context);
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
    /// <param name="context">live スキーマ（無ければ何もしない）</param>
    /// <param name="includeDroppedChildTables">
    /// 子表そのものを消す外部キーも外すか。逐次 DDL の方言では外す（外部キーの削除は表の削除より前のセクションに
    /// 並ぶので、親と子を一緒に消すときに親を先に消して失敗する順序の問題も消える）。再構築の方言（SQLite）では
    /// 外さない（消す表を作り直す意味が無く、表の削除が外部キーごと消す）。
    /// </param>
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
        SyncPlanContext? context,
        bool includeDroppedChildTables,
        IdentifierLengthLimit limit
    )
    {
        if (context is null)
        {
            return selected;
        }

        var dropped = DroppedObjects.From(selected);

        if (dropped.IsEmpty)
        {
            return selected;
        }

        var explicitlyDropped = CollectExplicitlyDroppedForeignKeys(selected, limit);
        var drops = new List<SchemaDiffItem>();

        foreach (var fk in EnumerateLiveForeignKeys(context))
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
                            context
                        )
                    )
                )
            );
        }

        return drops.Count == 0 ? selected : [.. drops, .. selected];
    }

    /// <summary>
    /// 選んだ列の削除で構成列が消える live の一意制約を、暗黙の <see cref="SchemaDiffKind.DropUniqueConstraint"/> として
    /// 先頭へ足し、<see cref="SyncPlanWarningKind.UniqueConstraintDroppedWithColumn"/> で名指しする（付け直さない）。
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
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings,
        IdentifierLengthLimit limit
    )
    {
        if (context is null)
        {
            return selected;
        }

        var dropped = DroppedObjects.From(selected);

        if (dropped.Columns.Count == 0)
        {
            return selected;
        }

        var explicitlyDropped = ExplicitlyDroppedUniqueConstraints(selected);
        var drops = new List<SchemaDiffItem>();

        foreach (var entity in context.LiveEntities)
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
                    }
                );
                warnings.Add(
                    new SyncPlanWarning(
                        SyncPlanWarningKind.UniqueConstraintDroppedWithColumn,
                        table,
                        $"{UniqueConstraintNaming.Resolve(constraint.Name, table, columns, limit)} ({string.Join(", ", columns)})"
                    )
                );
            }
        }

        return drops.Count == 0 ? selected : [.. drops, .. selected];
    }

    /// <summary>
    /// 選んだ主キーの変更の新しい主キーと同じ列集合の live の一意制約を、暗黙の <see cref="SchemaDiffKind.DropUniqueConstraint"/>
    /// として先頭へ足し、<see cref="SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey"/> で名指しする（付け直さない）。
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
    /// その一意制約を参照する外部キーは、主キーの変更の外し直し（<see cref="InjectImplicitForeignKeyRebuilds"/>）が外して、新しい主キー
    /// へ付け直す（外さないと一意制約の削除が断られる）。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> InjectUniqueConstraintsReplacedByPrimaryKey(
        List<SchemaDiffItem> selected,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings,
        IdentifierLengthLimit limit
    )
    {
        if (context is null)
        {
            return selected;
        }

        var pkChangedTables = CollectPrimaryKeyChanges(selected);

        if (pkChangedTables.Count == 0)
        {
            return selected;
        }

        var explicitlyDropped = ExplicitlyDroppedUniqueConstraints(selected);
        var liveByName = IndexLiveTables(context);
        var drops = new List<SchemaDiffItem>();

        foreach (var (table, newKey) in pkChangedTables)
        {
            if (newKey.Count == 0 || !liveByName.TryGet(table, out var live))
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
                    }
                );
                warnings.Add(
                    new SyncPlanWarning(
                        SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey,
                        table,
                        $"{UniqueConstraintNaming.Resolve(constraint.Name, table, columns, limit)} ({string.Join(", ", columns)})"
                    )
                );
            }
        }

        return drops.Count == 0 ? selected : [.. drops, .. selected];
    }

    /// <summary>固定順序のセクション一覧を組み立てる（空セクションは含まない）</summary>
    /// <remarks>
    /// 主キー変更は Drop / Add の 2 フェーズとして 2 回現れる（同じ差分項目群を別位置のセクションで再利用する）。
    /// Add フェーズは「新しい主キー列がある」項目だけに絞るため、主キーの解除のみの変更では出現しない。
    /// </remarks>
    private static List<SyncPlanSection> BuildSections(IReadOnlyList<SchemaDiffItem> selected)
    {
        var sections = new List<SyncPlanSection>();

        foreach (var (kind, phase) in SectionOrder)
        {
            // 種別ごとに抽出する。RebuildTable 等 SectionOrder に無い種別はここで自然に除外される
            var subset = selected.Where(i => i.Kind == kind).ToList();

            // 主キーの解除のみ（新主キー列ゼロ・target 不明）の項目は付与フェーズを持たない
            if (phase == PrimaryKeyPhase.Add)
            {
                subset = subset.Where(HasNewPrimaryKeyColumns).ToList();
            }

            if (subset.Count == 0)
            {
                continue;
            }

            sections.Add(
                new SyncPlanSection
                {
                    Kind = kind,
                    PrimaryKeyPhase = phase,
                    Items = subset,
                }
            );
        }

        return sections;
    }

    // ---------------- 失敗点の並び（DDL が暗黙コミットされる方言） ----------------

    /// <summary>
    /// 外部キーの追加のセクションの中を、断られうる順の少ないものから安定に並べ替える：外した外部キーをそのまま付け直す追加 →
    /// 定義を変えて付け直す追加（選んだ作り直し）→ 新しい外部キーの追加 → 参照先が同期後の候補キーだと証明できない追加。
    /// </summary>
    /// <param name="sections">セクション</param>
    /// <param name="selected">選択済みの項目（同期後の主キー・一意制約の合成に使う）</param>
    /// <param name="context">live スキーマ（無ければ並べ替えない）</param>
    /// <param name="limit">外部キーの実効名を求める識別子の上限</param>
    /// <remarks>
    /// <para>
    /// DDL が暗黙コミットされる方言では、断られた追加の後ろにある外部キーの付け直し（同じ計画で先に外したもの）が走らず、外した
    /// 外部キーが消えたまま残る。外部キーの追加は互いに依存せず、外部キーの追加の後ろに制約の文は無いので、断られうる追加ほど
    /// 後ろへ回せば、断られても消えるのはその追加より後ろの物だけになる。
    /// </para>
    /// <list type="bullet">
    /// <item>そのまま付け直す追加（外したのと同じ子の表・名前・列ペア・参照アクション）：もともと DB にあった制約なので、既存の行で
    /// 断られない。</item>
    /// <item>定義を変えて付け直す追加（同じ子の表・名前で、列ペアか参照アクションが違う）：新しい定義は既存の行で断られうる。断られると
    /// その外部キー自身が外れたまま残る（作り直しの <see cref="SchemaDiffItem.IsDestructive"/>）。</item>
    /// <item>新しい外部キーの追加：計画は列の値を見ないので、親の無い値を持つ行で断られうる（MySQL 1452・Oracle ORA-02298）。前へ置くと、
    /// 後ろの付け直しが走らない。</item>
    /// <item>証明できない追加（<see cref="SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey"/>・
    /// <see cref="SyncPlanWarningKind.ForeignKeyNotOnCandidateKey"/> と同じ証明＝<see cref="StaysCandidateKey"/>）：取込外の一意索引で通る
    /// ことがあるので実行は止めないが、DB が断ると計画はそこで止まる。最後へ回せば、断られても消えるのは警告した外部キー自身だけになる。</item>
    /// </list>
    /// </remarks>
    private static List<SyncPlanSection> ArrangeForeignKeyAddsByFailureRisk(
        List<SyncPlanSection> sections,
        IReadOnlyList<SchemaDiffItem> selected,
        SyncPlanContext? context,
        IdentifierLengthLimit limit
    )
    {
        if (context is null)
        {
            return sections;
        }

        var pkChangedTables = CollectPrimaryKeyChanges(selected);
        var addedTables = CollectAddedTables(selected);
        var liveByName = IndexLiveTables(context);
        var postSyncUniques = BuildPostSyncUniqueConstraints(selected, context);
        var drops = sections
            .Where(s => s.Kind == SchemaDiffKind.DropForeignKey)
            .SelectMany(s => s.Items)
            .Where(i => i.ChildEntity is not null)
            .ToList();

        return
        [
            .. sections.Select(section =>
                section.Kind == SchemaDiffKind.AddForeignKey
                    ? new SyncPlanSection
                    {
                        Kind = section.Kind,
                        PrimaryKeyPhase = section.PrimaryKeyPhase,
                        Items = [.. section.Items.OrderBy(Risk)],
                    }
                    : section
            ),
        ];

        // 断られうる順（0＝そのまま付け直す・1＝定義を変えて付け直す・2＝新しい外部キー・3＝証明できない）
        int Risk(SchemaDiffItem add)
        {
            if (!IsProven(add))
            {
                return 3;
            }

            if (add.ChildEntity is null)
            {
                return 2;
            }

            var child = SchemaDiffService.NormalizeTable(add.ChildEntity);
            var name = SelectedForeignKeyName(add, limit);
            var drop = drops.FirstOrDefault(d =>
                SchemaNames.AreSame(SchemaDiffService.NormalizeTable(d.ChildEntity!), child)
                && SchemaNames.AreSame(DroppedForeignKeyName(d, limit, context), name)
            );

            if (drop is null)
            {
                return 2;
            }

            return
                PairSet(drop) == PairSet(add)
                && (drop.Relationship?.OnDelete ?? ForeignKeyReferentialAction.NoAction)
                    == (add.Relationship?.OnDelete ?? ForeignKeyReferentialAction.NoAction)
                && (drop.Relationship?.OnUpdate ?? ForeignKeyReferentialAction.NoAction)
                    == (add.Relationship?.OnUpdate ?? ForeignKeyReferentialAction.NoAction)
                ? 0
                : 1;
        }

        // 列ペアの対応の集合（並びは参照先のキーの並びへ並べ替えるので比べない）
        static string PairSet(SchemaDiffItem item) =>
            string.Join(
                ",",
                SyncScriptBuilderHelper
                    .ResolveColumnPairs(item)
                    .Select(p =>
                        $"{SchemaNames.Key(p.ParentColumn)}>{SchemaNames.Key(p.ChildColumn)}"
                    )
                    .Order(StringComparer.Ordinal)
            );

        // 参照先が同期後の主キーか一意制約と列の集合で一致するか（参照先の表が分からない・列が無いものは証明済みとして扱う）
        bool IsProven(SchemaDiffItem add)
        {
            if (add.ParentEntity is null)
            {
                return true;
            }

            var parentTable = SchemaDiffService.NormalizeTable(add.ParentEntity);
            var postSyncKey = PostSyncPrimaryKeyOf(
                parentTable,
                pkChangedTables,
                addedTables,
                liveByName
            );
            var referencedColumns = ForeignKeyColumnPairResolver
                .ParentColumns(SyncScriptBuilderHelper.ResolveColumnPairs(add))
                .ToList();

            return postSyncKey is null
                || referencedColumns.Count == 0
                || StaysCandidateKey(postSyncKey, postSyncUniques, parentTable, referencedColumns);
        }
    }

    /// <summary>
    /// 計画が残す外部キーに阻まれて必ず断られる文があれば、計画の順で最初の 1 文（キーの削除は、同じ外部キーを支えるキーの削除の
    /// 組）を、計画の先頭の独立したセクションへ移す（何も変えずに失敗させる）。
    /// </summary>
    /// <param name="sections">セクション</param>
    /// <param name="sectionItems">セクションへ入れた項目（暗黙の外し直しを含む）</param>
    /// <param name="capabilities">方言のケーパビリティ</param>
    /// <param name="context">live スキーマ（無ければ移さない）</param>
    /// <remarks>
    /// <para>
    /// 計画は、両端の型が食い違う外部キー・付け直せない SET NULL の外部キーを外し直さずに残し、その外部キーに阻まれる文で
    /// 失敗させる（<see cref="InjectImplicitForeignKeyRebuilds"/>・警告で名指しする）。DDL が暗黙コミットされる方言では、その文より前に
    /// 外して後ろで付け直す制約（選んだ作り直し・主キーの変更・列の削除に伴う削除・兄弟の外部キーの外し直し）が消えたまま残った。
    /// 必ず断られる文を先頭へ移すと、失敗の時点で何も変わっていない（警告の「DB は変わりません」をそのまま満たす）。
    /// </para>
    /// <para>必ず断られる文（計画のどの文も外さない live の外部キー＝残す外部キーに阻まれるもの）：</para>
    /// <list type="bullet">
    /// <item>キーの削除（主キーの解除の Drop の段・一意制約の削除。主キーが置き換える一意制約の暗黙の削除を含む）：残す外部キーを支えうる
    /// 親のキー（参照する列と同じ列の集合を持つ live の主キー・一意制約。外部キーが参照先の索引の先頭の列を要する方言では、その列を先頭に
    /// 持つキーも）が、この計画ですべて外れる（SQL Server 3725・PostgreSQL 2BP01・Oracle ORA-02273・MySQL 1553）。支えうるキーが
    /// 1 つでも残れば、DB は外部キーをそのキーで支えるので断られると決まらない。</item>
    /// <item>列の型の変更：その列を端に持つ外部キーが残り、同期後に両端の型が食い違う。
    /// <see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/> の方言（MySQL 3780）は外部キーのどの組が食い違っても
    /// 外部キーの列の型の変更を断るので外部キー単位、ほかの方言（Oracle ORA-02267）はその列を含む組が食い違うときだけ（合う組の
    /// 列の変更は通るので、移すと後ろの食い違う列の変更の前に外したものが残る）。</item>
    /// <item>子の列の NOT NULL 化（<see cref="SyncDialectCapabilities.SetNullRequiresNullableChildColumn"/> の方言）：その列を子の列に持つ
    /// SET NULL の外部キーが残る（MySQL 1830）。</item>
    /// </list>
    /// <para>
    /// 支えうるキーを全部外す計画（同じ列の一意制約の双子・主キーと同じ列の一意制約）は、最後のキーの削除だけが断られ、それより前の
    /// 削除は通る。最後の 1 文だけを先頭へ移しても、残りのキーが支えて通ってしまうので、外す文の組を先頭へ移す。組の中は、後ろで
    /// 付け直すキーの削除（一意制約の作り直しを展開した削除・付与の段を持つ主キーの解除）を最後へ回し、前にはただの削除（消えてよい）を
    /// 置く（<c>ReaddedDropsLast</c>）。付け直すキーの削除を前に置くと、それが通った後に組の最後が断られ、付け直しが走らずにそのキーが
    /// 消えたまま残る。組の中に付け直すキーの削除が 2 つ以上あると、どう並べても前の方の付け直すキーが消えるので、一意制約の作り直し
    /// 1 つを計画から外して <see cref="SyncPlanWarningKind.UniqueConstraintRebuildKeptForForeignKey"/> で名指しし（<c>KeepOneReaddedUnique</c>）、
    /// 残りの計画で前寄せをやり直す。外した一意制約が古い形で外部キーを支え続けるので、ほかの削除は通る。
    /// </para>
    /// <para>
    /// 支えうるキーの数え方：双子の片方を「ほかのキー」と数えると前寄せせず、2 つ目の削除が断られたとき先に外した作り直しの外部キーが
    /// 消えたまま残り（MySQL 1553）、主キーと同じ列の一意制約が外部キーを支えるのに主キーの解除を「必ず断られる」と先頭へ移すと、
    /// 続く列の変更の失敗で主キーが消えたまま残る（MySQL 3780）。
    /// </para>
    /// <para>
    /// 主キーの変更を移すときは Drop の段だけを移す（付与は元の位置のまま）。移した文が予言に反して通っても、ほかの文は元の並びで
    /// 続く。
    /// </para>
    /// </remarks>
    private static List<SyncPlanSection> MoveCertainFailureFirst(
        List<SyncPlanSection> sections,
        IReadOnlyList<SchemaDiffItem> sectionItems,
        IReadOnlyList<SchemaDiffItem> uniqueAlters,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        (ResolvedForeignKeyRelationship Fk, string Child, string Parent)? blockingForeignKey = null;

        if (context is null)
        {
            return sections;
        }

        var limit = capabilities.IdentifierLengthLimit;
        var dropped = CollectExplicitlyDroppedForeignKeys(sectionItems, limit);
        var retained = EnumerateLiveForeignKeys(context)
            .Select(fk =>
                (
                    Fk: fk,
                    Child: SchemaDiffService.NormalizeTable(fk.Child),
                    Parent: SchemaDiffService.NormalizeTable(fk.Parent)
                )
            )
            .Where(fk =>
                !dropped.Contains(
                    ForeignKeyKey(fk.Child, fk.Parent, fk.Fk.ColumnPairs, fk.Fk.Relationship, limit)
                )
            )
            .ToList();

        if (retained.Count == 0)
        {
            return sections;
        }

        var liveByName = IndexLiveTables(context);
        var typeDecidingColumns = CollectTypeDecidingColumns(
            sectionItems,
            liveByName,
            capabilities
        );

        // この計画で外すキー（一意制約は照合キー・主キーは表）
        var droppedUniques = sectionItems
            .Where(i => i.Kind == SchemaDiffKind.DropUniqueConstraint)
            .Select(i =>
                UniqueConstraintKey(
                    SchemaNames.ToStoredForm(i.TableName),
                    i.UniqueConstraintName,
                    i.UniqueConstraintColumns
                )
            )
            .ToHashSet();
        var droppedPrimaryKeys = sections
            .Where(s =>
                s.Kind == SchemaDiffKind.AlterPrimaryKey
                && s.PrimaryKeyPhase == PrimaryKeyPhase.Drop
            )
            .SelectMany(s => s.Items)
            .Select(i => SchemaNames.ToStoredForm(i.TableName))
            .ToHashSet(SchemaNames.Comparer);

        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];

            foreach (var item in section.Items)
            {
                var moved = CertainFailure(section, item);

                if (moved is null)
                {
                    continue;
                }

                // 支えうるキーの削除の組に、後ろで付け直すキーが 2 つ以上あると、どう並べても前の方の付け直すキーが消える。一意制約の
                // 作り直しを 1 つ外して古い形のまま残し（外部キーを支え続ける）、残りの計画で前寄せをやり直す
                if (
                    moved.Count(x => IsReadded(x.Section, x.Item)) >= 2
                    && KeepOneReaddedUnique(moved) is { } kept
                )
                {
                    return MoveCertainFailureFirst(
                        kept.Sections,
                        kept.SectionItems,
                        uniqueAlters,
                        capabilities,
                        context,
                        warnings,
                        excluded
                    );
                }

                return MoveFirst(moved);
            }
        }

        // 単独では先頭で断られないが、組の両端を互いに並べられない型へ変える（DB が受け入れている型の違う組の両端を、それぞれ相手の
        // 今の型の仲間へ寄せる）外部キーは、両端の変更を計画の順に移す（先の変更は通り、後の変更が断られる）
        return capabilities.AlterColumnRequiresForeignKeyRebuild ? sections
            : MovePairChangesFirst() is { } pair ? MoveFirst(pair)
            : sections;

        // 外部キーの組の両端を変える列の変更の組（計画の順で先のもの・後のもの）。同期後に両端が並べられないものの最初
        List<(SyncPlanSection Section, SchemaDiffItem Item)>? MovePairChangesFirst()
        {
            var alters = sections
                .Where(s => s.Kind == SchemaDiffKind.AlterColumn)
                .SelectMany(s => s.Items.Select(i => (Section: s, Item: i)))
                .Where(x => x.Item.ColumnName is not null)
                .ToList();

            for (var first = 0; first < alters.Count; first++)
            {
                var firstColumn = ColumnKey(
                    SchemaNames.ToStoredForm(alters[first].Item.TableName),
                    alters[first].Item.ColumnName!
                );

                if (!typeDecidingColumns.TryGetValue(firstColumn, out var firstType))
                {
                    continue;
                }

                for (var second = first + 1; second < alters.Count; second++)
                {
                    var secondColumn = ColumnKey(
                        SchemaNames.ToStoredForm(alters[second].Item.TableName),
                        alters[second].Item.ColumnName!
                    );

                    if (
                        typeDecidingColumns.TryGetValue(secondColumn, out var secondType)
                        && !capabilities.AreForeignKeyCompatibleTypes(firstType, secondType)
                        && retained.Any(fk =>
                            OtherEnds(fk, firstColumn).Any(other => other.Column == secondColumn)
                        )
                    )
                    {
                        return [alters[first], alters[second]];
                    }
                }
            }

            return null;
        }

        // 外部キーがこの列を端に持つか
        bool Touches(
            (ResolvedForeignKeyRelationship Fk, string Child, string Parent) fk,
            string column
        ) =>
            fk.Fk.ColumnPairs.Any(p =>
                ColumnKey(fk.Child, p.ChildColumn) == column
                || ColumnKey(fk.Parent, p.ParentColumn) == column
            );

        // この列を含む組の、相手の端の列と live の型
        IEnumerable<(string Column, string? LiveType)> OtherEnds(
            (ResolvedForeignKeyRelationship Fk, string Child, string Parent) fk,
            string column
        )
        {
            foreach (var pair in fk.Fk.ColumnPairs)
            {
                if (ColumnKey(fk.Child, pair.ChildColumn) == column)
                {
                    yield return (
                        ColumnKey(fk.Parent, pair.ParentColumn),
                        ColumnTypeOf(fk.Fk.Parent, pair.ParentColumn)
                    );
                }

                if (ColumnKey(fk.Parent, pair.ParentColumn) == column)
                {
                    yield return (
                        ColumnKey(fk.Child, pair.ChildColumn),
                        ColumnTypeOf(fk.Fk.Child, pair.ChildColumn)
                    );
                }
            }
        }

        // 必ず断られる文なら、先頭へ移す文の組（キーの削除は同じ外部キーを支えるキーの削除の組・ほかは 1 文）
        List<(SyncPlanSection Section, SchemaDiffItem Item)>? CertainFailure(
            SyncPlanSection section,
            SchemaDiffItem item
        )
        {
            var table = SchemaNames.ToStoredForm(item.TableName);

            if (
                section.Kind
                    is SchemaDiffKind.DropUniqueConstraint
                        or SchemaDiffKind.AlterPrimaryKey
                && (
                    section.Kind == SchemaDiffKind.DropUniqueConstraint
                    || section.PrimaryKeyPhase == PrimaryKeyPhase.Drop
                )
            )
            {
                return
                    liveByName.TryGet(table, out var liveOwner)
                    && BlockedKeyDrops(liveOwner, section.Kind, item) is { } group
                    ? ReaddedDropsLast(group)
                    : null;
            }

            if (
                section.Kind != SchemaDiffKind.AlterColumn
                || item.ColumnName is null
                || item.Column is null
                || !liveByName.TryGet(table, out var liveTable)
                || liveTable.Columns.FirstOrDefault(c =>
                    SchemaNames.AreSame(c.Name, item.ColumnName)
                )
                    is not { } liveColumn
            )
            {
                return null;
            }

            var column = ColumnKey(table, item.ColumnName);

            // 残す外部キーの列の型を変える文。外し直しの方言（MySQL）は外部キーの列の型の変更を外部キーのどの組が同期後に食い違っても
            // 断るので外部キー単位で同期後の両端を見る。ほかの方言（Oracle）は外部キーを張ったまま列を変えるので、先頭へ移した位置
            // （ほかの列はまだ live の型）でその列を含む組が並べられないときだけ断られる。同期後の両端で判定すると、両端を別々の型へ
            // 変える外部キーの、先頭では通る列の変更（子を互換の範囲で広げる）を移し、断られる文（親を互換でない型へ）を元の位置に残す
            if (typeDecidingColumns.TryGetValue(column, out var newType))
            {
                if (
                    capabilities.AlterColumnRequiresForeignKeyRebuild
                        ? retained.Any(fk =>
                            Touches(fk, column)
                            && HasPostSyncTypeMismatch(
                                fk.Fk,
                                fk.Child,
                                fk.Parent,
                                typeDecidingColumns,
                                capabilities
                            )
                        )
                        : retained.Any(fk =>
                            OtherEnds(fk, column)
                                .Any(other =>
                                    !capabilities.AreForeignKeyCompatibleTypes(
                                        newType,
                                        other.LiveType
                                    )
                                )
                        )
                )
                {
                    return [(section, item)];
                }
            }

            // 残す SET NULL の外部キーの子の列を NOT NULL にする
            return
                capabilities.SetNullRequiresNullableChildColumn
                && ColumnNullability.AllowsNull(liveColumn)
                && !ColumnNullability.AllowsNull(item.Column)
                && retained.Any(fk =>
                    UsesSetNull(fk.Fk.Relationship, capabilities)
                    && fk.Fk.ColumnPairs.Any(p => ColumnKey(fk.Child, p.ChildColumn) == column)
                )
                ? [(section, item)]
                : null;
        }

        // このキーの削除が、残す外部キーを支えうる親のキーを最後まで外す組に入るなら、その組（計画の全部のキーの削除のうち、
        // その外部キーを支えうるキーの削除）。支えうるキーが 1 つでも残る外部キーには阻まれない
        List<(SyncPlanSection Section, SchemaDiffItem Item)>? BlockedKeyDrops(
            Entity liveOwner,
            SchemaDiffKind kind,
            SchemaDiffItem drop
        )
        {
            var table = SchemaDiffService.NormalizeTable(liveOwner);
            var keys = SupportingKeyCandidates(liveOwner, table);
            var droppedKey =
                kind == SchemaDiffKind.AlterPrimaryKey
                    ? keys.FirstOrDefault(k => k.Primary)
                    : keys.FirstOrDefault(k =>
                        !k.Primary
                        && k.UniqueKey
                            == UniqueConstraintKey(
                                table,
                                drop.UniqueConstraintName,
                                drop.UniqueConstraintColumns
                            )
                    );

            if (droppedKey.Columns is null)
            {
                return null;
            }

            foreach (var fk in retained.Where(fk => SchemaNames.AreSame(fk.Parent, table)))
            {
                var referenced = ForeignKeyColumnPairResolver
                    .ParentColumns(fk.Fk.ColumnPairs)
                    .ToList();
                var supporters = keys.Where(k =>
                        CanSupportForeignKey(capabilities, k.Columns, referenced)
                    )
                    .ToList();

                if (
                    !supporters.Contains(droppedKey)
                    || supporters.Any(k =>
                        k.Primary
                            ? !droppedPrimaryKeys.Contains(table)
                            : !droppedUniques.Contains(k.UniqueKey!)
                    )
                )
                {
                    continue;
                }

                blockingForeignKey = fk;

                // 組：計画の項目のうち、支えうるキーを外す項目（一意制約の削除と主キーの解除の段）
                return
                [
                    .. sections
                        .Where(s =>
                            s.Kind == SchemaDiffKind.DropUniqueConstraint
                            || s.Kind == SchemaDiffKind.AlterPrimaryKey
                                && s.PrimaryKeyPhase == PrimaryKeyPhase.Drop
                        )
                        .SelectMany(s => s.Items.Select(i => (Section: s, Item: i)))
                        .Where(x =>
                            SchemaNames.AreSame(SchemaNames.ToStoredForm(x.Item.TableName), table)
                            && supporters.Any(k =>
                                k.Primary
                                    ? x.Section.Kind == SchemaDiffKind.AlterPrimaryKey
                                    : x.Section.Kind == SchemaDiffKind.DropUniqueConstraint
                                        && k.UniqueKey
                                            == UniqueConstraintKey(
                                                table,
                                                x.Item.UniqueConstraintName,
                                                x.Item.UniqueConstraintColumns
                                            )
                            )
                        )
                        .Select(x => (x.Section, x.Item)),
                ];
            }

            return null;
        }

        // キーの削除の組を、後ろで付け直すキーの削除が最後に来るように安定に並べ替える。組の最後の削除だけが断られ、前の削除は通る
        // ので、前に置くのはただの削除（消えてよい）にする。付け直すキーの削除を前に置くと、それが通った後に組の最後が断られ、
        // 付け直しが走らずにそのキーが消えたまま残る（一意制約の作り直しを展開した削除・付与の段を持つ主キーの解除）
        List<(SyncPlanSection Section, SchemaDiffItem Item)> ReaddedDropsLast(
            List<(SyncPlanSection Section, SchemaDiffItem Item)> group
        ) => [.. group.OrderBy(x => IsReadded(x.Section, x.Item) ? 1 : 0)];

        // 付け直すキーが 2 つ以上ある組から、一意制約の作り直し 1 つ（組の最初の付け直す一意制約の削除と、その追加）を計画から外す。
        // 外した作り直しは警告とともに外した項目へ積み、残りのセクションと項目を返す（元の作り直しの項目が見つからなければ外さない）
        (List<SyncPlanSection> Sections, List<SchemaDiffItem> SectionItems)? KeepOneReaddedUnique(
            List<(SyncPlanSection Section, SchemaDiffItem Item)> group
        )
        {
            var drop = group
                .Where(x =>
                    x.Section.Kind == SchemaDiffKind.DropUniqueConstraint
                    && IsReadded(x.Section, x.Item)
                )
                .Select(x => x.Item)
                .FirstOrDefault();

            if (drop?.UniqueConstraintName is not { } name || blockingForeignKey is not { } fk)
            {
                return null;
            }

            var table = SchemaNames.ToStoredForm(drop.TableName);
            var alter = uniqueAlters.FirstOrDefault(a =>
                SchemaNames.AreSame(SchemaNames.ToStoredForm(a.TableName), table)
                && a.ReplacedUniqueConstraint?.UniqueConstraintName is { } replaced
                && SchemaNames.AreSame(replaced, name)
            );

            if (alter is null)
            {
                return null;
            }

            var removed = sectionItems
                .Where(i =>
                    ReferenceEquals(i, drop)
                    || i.Kind == SchemaDiffKind.AddUniqueConstraint
                        && SchemaNames.AreSame(SchemaNames.ToStoredForm(i.TableName), table)
                        && i.UniqueConstraintName is { } added
                        && SchemaNames.AreSame(added, name)
                )
                .ToHashSet(ReferenceEqualityComparer.Instance);

            ExcludeSelected(
                alter,
                new SyncPlanWarning(
                    SyncPlanWarningKind.UniqueConstraintRebuildKeptForForeignKey,
                    drop.TableName,
                    $"{name} ({ResolveForeignKeyName(fk.Fk.Relationship, fk.Child, fk.Parent, capabilities.IdentifierLengthLimit, context)})"
                ),
                warnings,
                excluded
            );

            return (
                [
                    .. sections
                        .Select(s => new SyncPlanSection
                        {
                            Kind = s.Kind,
                            PrimaryKeyPhase = s.PrimaryKeyPhase,
                            Items = [.. s.Items.Where(i => !removed.Contains(i))],
                        })
                        .Where(s => s.Items.Count > 0),
                ],
                [.. sectionItems.Where(i => !removed.Contains(i))]
            );
        }

        // このキーの削除の後ろで同じキーを付け直すか（同じ表・同じ実効名の一意制約の追加か、同じ表の主キーの付与の段）
        bool IsReadded(SyncPlanSection section, SchemaDiffItem drop)
        {
            var table = SchemaNames.ToStoredForm(drop.TableName);

            return section.Kind == SchemaDiffKind.AlterPrimaryKey
                ? sections.Any(s =>
                    s.Kind == SchemaDiffKind.AlterPrimaryKey
                    && s.PrimaryKeyPhase == PrimaryKeyPhase.Add
                    && s.Items.Any(i =>
                        SchemaNames.AreSame(SchemaNames.ToStoredForm(i.TableName), table)
                    )
                )
                : drop.UniqueConstraintName is { } name
                    && sections.Any(s =>
                        s.Kind == SchemaDiffKind.AddUniqueConstraint
                        && s.Items.Any(i =>
                            SchemaNames.AreSame(SchemaNames.ToStoredForm(i.TableName), table)
                            && i.UniqueConstraintName is { } added
                            && SchemaNames.AreSame(added, name)
                        )
                    );
        }

        // 外部キーを支えうる親のキーの候補（live の主キーと一意制約）
        List<(
            bool Primary,
            string? UniqueKey,
            IReadOnlyList<string> Columns
        )> SupportingKeyCandidates(Entity liveOwner, string table)
        {
            var keys = new List<(bool Primary, string? UniqueKey, IReadOnlyList<string> Columns)>();
            var primaryKey = liveOwner.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList();

            if (primaryKey.Count > 0)
            {
                keys.Add((true, null, primaryKey));
            }

            foreach (var unique in liveOwner.UniqueConstraints)
            {
                if (
                    UniqueConstraintNaming.TryResolveColumnNames(liveOwner, unique, out var columns)
                )
                {
                    keys.Add((false, UniqueConstraintKey(table, unique.Name, columns), columns));
                }
            }

            return keys;
        }

        // 組を計画の順のまま先頭へ移す（セクションごとに移した項目の独立したセクションを作る）
        List<SyncPlanSection> MoveFirst(
            List<(SyncPlanSection Section, SchemaDiffItem Item)> movedItems
        )
        {
            // 先頭のセクションは移す文の並びのまま作る（同じセクションの文が続く間は 1 つのセクションにまとめる）。移す文の並びは計画の
            // 順か、キーの削除の組なら付け直す削除を最後へ回した順（ReaddedDropsLast）
            var front = new List<SyncPlanSection>();
            var rest = new List<SyncPlanSection>();

            foreach (var run in ConsecutiveBySection(movedItems))
            {
                front.Add(
                    new SyncPlanSection
                    {
                        Kind = run.Section.Kind,
                        PrimaryKeyPhase = run.Section.PrimaryKeyPhase,
                        Items = run.Items,
                        IsMovedFirst = true,
                    }
                );
            }

            foreach (var section in sections)
            {
                var kept = section.Items.Where(i => !IsMoved(section, i)).ToList();

                if (kept.Count > 0)
                {
                    rest.Add(
                        new SyncPlanSection
                        {
                            Kind = section.Kind,
                            PrimaryKeyPhase = section.PrimaryKeyPhase,
                            Items = kept,
                        }
                    );
                }
            }

            return [.. front, .. rest];

            bool IsMoved(SyncPlanSection section, SchemaDiffItem item) =>
                movedItems.Any(m =>
                    ReferenceEquals(m.Section, section) && ReferenceEquals(m.Item, item)
                );

            static List<(SyncPlanSection Section, List<SchemaDiffItem> Items)> ConsecutiveBySection(
                List<(SyncPlanSection Section, SchemaDiffItem Item)> items
            )
            {
                var runs = new List<(SyncPlanSection Section, List<SchemaDiffItem> Items)>();

                foreach (var (section, item) in items)
                {
                    if (runs.Count > 0 && ReferenceEquals(runs[^1].Section, section))
                    {
                        runs[^1].Items.Add(item);
                    }
                    else
                    {
                        runs.Add((section, [item]));
                    }
                }

                return runs;
            }
        }
    }

    /// <summary>
    /// 既存の行で断られうる文より前で外し、その文か後ろで付け直す制約を名指しする（DDL が暗黙コミットされる方言）。列が空でないと通らない
    /// 列の変更（<see cref="SyncDialectCapabilities.RequiresEmptyColumn"/>）は
    /// <see cref="SyncPlanWarningKind.ConstraintRebuildAroundEmptyColumnChange"/>、ほかの断られうる文は
    /// <see cref="SyncPlanWarningKind.ConstraintRebuildAroundRowDependentChange"/> で名指しする。
    /// </summary>
    /// <param name="sections">並べ終えたセクション（前寄せの後）</param>
    /// <param name="capabilities">方言のケーパビリティ</param>
    /// <param name="context">live スキーマ（列の変更の前の型・キー。無ければ名指ししない）</param>
    /// <param name="warnings">警告の積み先</param>
    /// <remarks>
    /// <para>
    /// 計画は列の値を見ないので、値があると DB が文を断ることを止められない。DDL が暗黙コミットされるので、それより前で外した制約は
    /// 付け直されずに残る。外す理由（暗黙に外して付け直す外部キー・一意制約・主キー、主キーの変更の解除、選んだ作り直し）に依らず、
    /// 文の並びで「断られうる文を挟んで外して付け直す」制約を全部名指しする。断られた時点で外れているのは、変更する列に関わる制約に限らない
    /// （主キーの変更の解除は主キー自体を、主キーの変更の外し直しは別の表の外部キーを外す）ので、どの制約がその文を挟むかを文の並びで決める。
    /// 断られうる文そのものが付け直す制約（作り直す一意制約・主キーの変更の主キー）も、その文が断られると外れたまま残るので名指しする
    /// （列が空でないと通らない変更は列の変更なので、付け直しの文にはならない）。
    /// </para>
    /// <para>
    /// 断られうる文（<see cref="RowDependentChanges"/>）は、主キーの付与・一意制約の追加（作り直しの追加を含む）・NULL を許す列の NOT NULL 化
    /// （主キーの変更が足す NOT NULL 化を含む）・型を狭める列の変更（<see cref="NarrowsType"/>）・既定値の無い NOT NULL の列の追加
    /// （<see cref="SyncDialectCapabilities.AddNotNullColumnRequiresEmptyTable"/>）・DB に計算列のある表の普通の列の変更
    /// （<see cref="SyncDialectCapabilities.AlterColumnBlockedByComputedColumn"/>）。同じ文が列が空でないと通らない変更にも当たるときは、
    /// 列が空でないと通らない変更だけに数える。
    /// </para>
    /// <para>
    /// 計画が付け直さずに外す暗黙の削除（端が消える外部キー・構成列が消える一意制約・主キーが置き換える一意制約）も、後ろの断られうる文で
    /// 止まると、消すはずの表・列や置き換える主キーが来ないまま外れて残るので名指しする。利用者が選んだ削除は消えてよいので数えない。
    /// 付け直しの対応は外部キーが子の表と名前、一意制約が表と名前、主キーが表で取る。
    /// Detail は「制約名 (挟む文, …)」（主キーの制約名は <c>PRIMARY KEY</c>）。
    /// </para>
    /// </remarks>
    private static void WarnConstraintsAroundRefusableChanges(
        List<SyncPlanSection> sections,
        IReadOnlyCollection<SchemaDiffItem> userSelected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings
    )
    {
        if (context is null)
        {
            return;
        }

        var limit = capabilities.IdentifierLengthLimit;
        var liveByName = IndexLiveTables(context);
        var steps = sections.SelectMany(s => s.Items.Select(i => (Section: s, Item: i))).ToList();
        var typeDecidingColumns = CollectTypeDecidingColumns(
            [.. steps.Select(s => s.Item)],
            liveByName,
            capabilities
        );

        // 列が空でないと通らない列の変更の位置と、その文面（表.列: 旧 → 新）
        var emptyColumnChanges = new List<(int Index, string Text)>();

        for (var index = 0; index < steps.Count; index++)
        {
            var (section, item) = steps[index];
            var table = SchemaNames.ToStoredForm(item.TableName);

            if (
                section.Kind == SchemaDiffKind.AlterColumn
                && item.ColumnName is not null
                && typeDecidingColumns.TryGetValue(
                    ColumnKey(table, item.ColumnName),
                    out var newType
                )
                && liveByName.TryGet(table, out var liveTable)
                && liveTable.Columns.FirstOrDefault(c =>
                    SchemaNames.AreSame(c.Name, item.ColumnName)
                )
                    is { } liveColumn
                && capabilities.RequiresEmptyColumn(liveColumn.DataType, newType)
            )
            {
                emptyColumnChanges.Add(
                    (index, $"{table}.{item.ColumnName}: {liveColumn.DataType} → {newType}")
                );
            }
        }

        var emptyIndexes = emptyColumnChanges.Select(c => c.Index).ToHashSet();
        var rowDependentChanges = RowDependentChanges(
                steps,
                liveByName,
                typeDecidingColumns,
                capabilities
            )
            .Where(c => !emptyIndexes.Contains(c.Index))
            .ToList();

        if (emptyColumnChanges.Count == 0 && rowDependentChanges.Count == 0)
        {
            return;
        }

        for (var drop = 0; drop < steps.Count; drop++)
        {
            if (Identity(steps[drop], dropping: true) is not { } identity)
            {
                continue;
            }

            var readd = steps.FindIndex(
                drop + 1,
                s => Identity(s, dropping: false) is { } added && added.Key == identity.Key
            );

            if (readd < 0)
            {
                // 付け直さない暗黙の削除（消える端の外部キー・消える構成列の一意制約・主キーが置き換える一意制約）は、後ろの表・列の
                // 削除や主キーの付与が来る前に断られうる文で止まると、外したまま残る。利用者が選んだ削除は消えてよいので数えない
                if (!userSelected.Contains(steps[drop].Item, ReferenceEqualityComparer.Instance))
                {
                    Add(
                        SyncPlanWarningKind.ConstraintRebuildAroundEmptyColumnChange,
                        identity,
                        emptyColumnChanges.Where(c => c.Index > drop)
                    );
                    Add(
                        SyncPlanWarningKind.ConstraintRebuildAroundRowDependentChange,
                        identity,
                        rowDependentChanges.Where(c => c.Index > drop)
                    );
                }

                continue;
            }

            Add(
                SyncPlanWarningKind.ConstraintRebuildAroundEmptyColumnChange,
                identity,
                emptyColumnChanges.Where(c => c.Index > drop && c.Index < readd)
            );
            Add(
                SyncPlanWarningKind.ConstraintRebuildAroundRowDependentChange,
                identity,
                rowDependentChanges.Where(c => c.Index > drop && c.Index <= readd)
            );
        }

        // 挟む文があれば、制約ごとに 1 件の警告を積む
        void Add(
            SyncPlanWarningKind kind,
            (string Key, string Table, string Name) identity,
            IEnumerable<(int Index, string Text)> between
        )
        {
            var texts = between.Select(c => c.Text).Distinct(StringComparer.Ordinal).ToList();

            if (texts.Count == 0)
            {
                return;
            }

            var warning = new SyncPlanWarning(
                kind,
                identity.Table,
                $"{identity.Name} ({string.Join(", ", texts)})"
            );

            if (!warnings.Contains(warning))
            {
                warnings.Add(warning);
            }
        }

        // 外す・付け直す制約の照合キーと、名指しの表・名前（外さない・付け直さない文は null）
        (string Key, string Table, string Name)? Identity(
            (SyncPlanSection Section, SchemaDiffItem Item) step,
            bool dropping
        )
        {
            var (section, item) = step;
            var table = SchemaNames.ToStoredForm(item.TableName);

            switch (section.Kind)
            {
                case SchemaDiffKind.DropForeignKey when dropping:
                case SchemaDiffKind.AddForeignKey when !dropping:
                    if (item.ChildEntity is null)
                    {
                        return null;
                    }

                    var child = SchemaDiffService.NormalizeTable(item.ChildEntity);
                    var name = dropping
                        ? DroppedForeignKeyName(item, limit, context)
                        : SelectedForeignKeyName(item, limit);
                    return (
                        "FK"
                            + SignatureText.Part(SchemaNames.Key(child))
                            + SignatureText.Part(SchemaNames.Key(name)),
                        child,
                        name
                    );
                case SchemaDiffKind.DropUniqueConstraint when dropping:
                case SchemaDiffKind.AddUniqueConstraint when !dropping:
                    var unique = UniqueConstraintNaming.Resolve(
                        item.UniqueConstraintName,
                        item.TableName,
                        item.UniqueConstraintColumns,
                        limit
                    );
                    return (
                        "UQ"
                            + SignatureText.Part(SchemaNames.Key(table))
                            + SignatureText.Part(SchemaNames.Key(unique)),
                        table,
                        unique
                    );
                case SchemaDiffKind.AlterPrimaryKey
                    when section.PrimaryKeyPhase
                        == (dropping ? PrimaryKeyPhase.Drop : PrimaryKeyPhase.Add):
                    return (
                        "PK" + SignatureText.Part(SchemaNames.Key(table)),
                        table,
                        "PRIMARY KEY"
                    );
                default:
                    return null;
            }
        }
    }

    /// <summary>既存の行（とモデルの外の依存）で断られうる文の位置と文面（列が空でないと通らない変更を含めて数える）</summary>
    /// <param name="steps">並べ終えた計画の文</param>
    /// <param name="liveByName">live の表</param>
    /// <param name="typeDecidingColumns">この同期で型が決まる列（<see cref="CollectTypeDecidingColumns"/>）</param>
    /// <param name="capabilities">方言のケーパビリティ</param>
    /// <remarks>
    /// <para>
    /// 主キーの付与と一意制約の追加は、新しい列集合が live の主キーか一意制約の列集合を含み、その列がどれも live にあって型を変えないと
    /// きだけ断られないと証明できる（含む列集合の値の組は、含まれる側が一意なので一意。NULL を含む組は一意制約が通し、主キーは列を NOT NULL に
    /// する文が先に来る＝主キーの変更が新しい主キーの列を NOT NULL にする）。主キーの並べ替えはこの形で名指ししない。
    /// 同じ計画でその live のキーを外していても証明に使う：計画は表の値を変えないので、同期を始めた時点で一意だった値の組は、キーを外した後も
    /// 一意のまま（型を変える列は値が変わりうるので除く）。証明できないものは断られうる文とみなす（保守側）。
    /// </para>
    /// <para>
    /// 列の変更は、NULL を許す列の NOT NULL 化・型を狭める変更（<see cref="SyncDialectCapabilities.NarrowsType"/>）・
    /// <see cref="SyncDialectCapabilities.AlterColumnBlockedByComputedColumn"/> の方言で DB に計算列のある表の普通の列の変更（式がどの列を
    /// 参照するかは図に無いので表の単位で数える）。列の追加は、<see cref="SyncDialectCapabilities.AddNotNullColumnRequiresEmptyTable"/> の方言の
    /// NOT NULL の列（図は既定値を持たない）。
    /// </para>
    /// </remarks>
    private static IEnumerable<(int Index, string Text)> RowDependentChanges(
        List<(SyncPlanSection Section, SchemaDiffItem Item)> steps,
        NameLookup<Entity> liveByName,
        Dictionary<string, string> typeDecidingColumns,
        SyncDialectCapabilities capabilities
    )
    {
        for (var index = 0; index < steps.Count; index++)
        {
            var (section, item) = steps[index];
            var table = SchemaNames.ToStoredForm(item.TableName);
            var liveTable = liveByName.TryGet(table, out var found) ? found : null;

            switch (section.Kind)
            {
                case SchemaDiffKind.AlterPrimaryKey
                    when section.PrimaryKeyPhase == PrimaryKeyPhase.Add:
                {
                    var key =
                        item.Entity?.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList()
                        ?? [];

                    if (
                        key.Count > 0
                        && !HoldsInLiveRows(liveTable, table, key, typeDecidingColumns)
                    )
                    {
                        yield return (index, $"PRIMARY KEY {table} ({string.Join(", ", key)})");
                    }

                    break;
                }
                case SchemaDiffKind.AddUniqueConstraint:
                {
                    var columns = item.UniqueConstraintColumns;

                    if (
                        columns.Count > 0
                        && !HoldsInLiveRows(liveTable, table, columns, typeDecidingColumns)
                    )
                    {
                        yield return (index, $"UNIQUE {table} ({string.Join(", ", columns)})");
                    }

                    break;
                }
                case SchemaDiffKind.AddColumn
                    when capabilities.AddNotNullColumnRequiresEmptyTable
                        && item.Column is { } added
                        && !ColumnNullability.AllowsNull(added):
                    yield return (index, $"{table}.{added.Name}: ADD NOT NULL");
                    break;
                case SchemaDiffKind.AlterColumn
                    when item.Column is { } target
                        && item.ColumnName is { } columnName
                        && liveTable?.Columns.FirstOrDefault(c =>
                            SchemaNames.AreSame(c.Name, columnName)
                        )
                            is { } liveColumn:
                {
                    var typeDecided = typeDecidingColumns.TryGetValue(
                        ColumnKey(table, columnName),
                        out var newType
                    );
                    var typeText = typeDecided
                        ? $"{table}.{columnName}: {liveColumn.DataType} → {newType}"
                        : null;

                    if (
                        ColumnNullability.AllowsNull(liveColumn)
                        && !ColumnNullability.AllowsNull(target)
                    )
                    {
                        yield return (index, $"{table}.{columnName}: NOT NULL");
                    }
                    else if (typeDecided && capabilities.NarrowsType(liveColumn.DataType, newType))
                    {
                        yield return (index, typeText!);
                    }
                    else if (
                        capabilities.AlterColumnBlockedByComputedColumn
                        && !liveColumn.IsComputed
                        && liveTable.Columns.Any(c => c.IsComputed)
                    )
                    {
                        yield return (
                            index,
                            typeText
                                ?? $"{table}.{columnName}: {(ColumnNullability.AllowsNull(target) ? "NULL" : "NOT NULL")}"
                        );
                    }

                    break;
                }
            }
        }

        // 列集合が live のキー（主キー・一意制約）の列集合を含み、その列がどれも live にあって型を変えないか
        static bool HoldsInLiveRows(
            Entity? liveTable,
            string table,
            IReadOnlyList<string> columns,
            Dictionary<string, string> typeDecidingColumns
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
                    .Select(u =>
                        (IReadOnlyList<string>)[.. u.ColumnIds.Select(id => namesById[id])]
                    ),
            ];

            return keys.Any(key => key.Count > 0 && key.All(set.Contains));
        }
    }

    /// <summary>この主キー変更項目が新しい主キー列を持つか（＝付与フェーズを要するか）</summary>
    private static bool HasNewPrimaryKeyColumns(SchemaDiffItem item) =>
        item.Entity?.Columns.Any(c => c.IsPrimaryKey) == true;

    // ---------------- 依存 FK の自動 DROP → 再 ADD（逐次 DDL 方言） ----------------

    /// <summary>
    /// 選択された変更に巻き込まれる live の外部キーを、暗黙の DROP（先頭側）と再 ADD（末尾側）として注入する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 対象は (a) 選択済み <see cref="SchemaDiffKind.AlterPrimaryKey"/> のテーブルを、旧主キーと同じ列集合で<b>参照している</b>
    /// live FK（全方言。<see cref="SyncDialectCapabilities.ForeignKeyRequiresLeadingIndex"/> の方言では旧主キーの先頭の列だけを
    /// 参照する FK も含む。一意制約を参照する FK は主キーの解除に巻き込まれないので外さない）と、
    /// (b) 選択済み <see cref="SchemaDiffKind.AlterColumn"/> の列が子側・親側いずれかとして関与する live FK
    /// （<see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/> が <c>true</c> の方言のみ）と、
    /// (c) 選択済み AlterPrimaryKey のテーブルの旧主キー・削除を選んだ一意制約の索引の先頭に、子列が乗る live FK
    /// （<see cref="SyncDialectCapabilities.ForeignKeyRequiresLeadingIndex"/> が <c>true</c> の方言のみ）。
    /// いずれも「FK が張られたまま」では DDL が依存エラーで失敗するため、計画側で外して戻す。
    /// </para>
    /// <para>
    /// 子表・親表・構成列のどれかが同じ計画で削除される FK は、<see cref="InjectVanishingForeignKeyDrops"/> が
    /// 外すだけの項目として先に足しているので、ここでは明示的に外す FK として扱われ付け直さない。
    /// (b) で同期後に両端の列の型が食い違う FK も注入しない。付け直しが必ず失敗し、DDL が暗黙コミットされる方言では
    /// 外部キーが消えたまま残るため、外さずに列定義変更の時点で失敗させ、
    /// <see cref="SyncPlanWarningKind.ForeignKeyColumnTypeMismatch"/> で名指しする。(a) の付け直しも、構成列の型を変えて
    /// 同期後に両端の型が食い違うなら同じく注入せずに名指しする（列の変更で外し直しを要しない方言でも、付け直しは失敗する）。
    /// そうした外部キーが 1 本でもあれば、列の変更の段で必ず失敗するので、ほかの外部キーも 1 本も外さない
    /// （付け直しは列の変更の後にしか来ない。同じ列に外部キーが 2 本あり 1 本だけ型が食い違うとき、もう 1 本を
    /// 外すと、列の変更が失敗したままその外部キーだけが消えた。残した外部キーが参照する親の列の変更も失敗するので、
    /// 同じ列の外部キーだけを残すのでは足りない）。
    /// </para>
    /// <para>
    /// ユーザーが同じ FK の <see cref="SchemaDiffKind.DropForeignKey"/> を明示選択している場合は、自動 DROP を注入せず
    /// 再 ADD もしない（「消す」という意図を尊重する）。live 情報（<paramref name="context"/>）が無ければ何もしない。
    /// </para>
    /// <para>
    /// 注入項目は <see cref="SchemaDiffService"/> が生成する FK 差分と同じフィールド規則で埋めるため、
    /// 方言別レンダラーは既存の <c>AppendDropForeignKey</c> / <c>AppendAddForeignKey</c> のまま SQL 化できる。
    /// </para>
    /// <para>
    /// 参照アクション <c>SET NULL</c> の外部キーで、子の列が同期後に NOT NULL になるものは、
    /// <see cref="SyncDialectCapabilities.SetNullRequiresNullableChildColumn"/> が <c>true</c> の方言では付け直しが必ず失敗するので
    /// 外し直さずに <see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/> で名指しする。その子の列が変更する列のときは、
    /// 宣言の組み合わせで扱いが分かれる。<c>SetNullRequiresNullableChildColumn</c> が <c>true</c>・
    /// <see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/> が <c>true</c>（列の変更で外部キーを外し直す）・
    /// <see cref="SyncDialectCapabilities.TransactionalDdl"/> が <c>false</c>（MySQL）なら、その列を食い違う外部キーの列と同じく
    /// 失敗が決まった列として扱い、どの外部キーも外し直さない（MySQL は外部キーを張ったまま子の列を NOT NULL にできず
    /// エラー 1830 になるので、外したほかの外部キーが消えたまま残らない）。<c>TransactionalDdl</c> が <c>true</c>（SQL Server）
    /// なら、その外部キーだけを外し直さない（SQL Server は外部キーを張ったまま子の列を NOT NULL にでき、型も変える列の変更は
    /// エラー 5074 で失敗して巻き戻る）。
    /// </para>
    /// <para>
    /// 再 ADD する FK の参照先列が同期後も候補キーであることを証明できない場合は、再作成が実行時に失敗しうるため
    /// <paramref name="warnings"/> へ <see cref="SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey"/> を積む
    /// （証明は「同期後の主キーと完全一致」または「同期後の一意制約のいずれかと完全一致」。証明できなくても
    /// 一意インデックス等で通ることがあり断定はできないため、実行はブロックしない）。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> InjectImplicitForeignKeyRebuilds(
        List<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings
    )
    {
        // 外部キーの既定名は同期の方言の上限で縮める
        var limit = capabilities.IdentifierLengthLimit;

        if (context is null)
        {
            // live 情報が無ければ既存 FK を復元できない（逐次方言では VM が常に渡すため実質防御）
            return selected;
        }

        // (a) 主キー構成が変わるテーブル → 新しい主キー列名の集合
        // （そのテーブルを参照する FK は方言を問わず一旦外す必要がある。列集合は候補キー喪失の判定に使う）
        var pkChangedTables = CollectPrimaryKeyChanges(selected);

        // (b) 外し直しの理由になる、定義が変わる列（FK 参加列の型変更が依存エラーになる方言のみ対象。NULL 許容だけの変更も含む）
        var alteredColumns = new HashSet<string>();

        if (capabilities.AlterColumnRequiresForeignKeyRebuild)
        {
            foreach (
                var alter in selected.Where(i =>
                    i.Kind == SchemaDiffKind.AlterColumn && i.ColumnName is not null
                )
            )
            {
                alteredColumns.Add(ColumnKey(alter.TableName, alter.ColumnName!));
            }
        }

        var liveByName = IndexLiveTables(context);

        // この同期で型が決まる列（同期後の両端の型の照合は方言に依らずこれだけを使う。NULL 許容だけの変更は型を決めない）と、
        // そのうち (b) の外し直しの理由になる列（失敗が決まった列の判定に使う）
        var typeDecidingColumns = CollectTypeDecidingColumns(selected, liveByName, capabilities);
        var rebuildTypeDecidingColumns = capabilities.AlterColumnRequiresForeignKeyRebuild
            ? typeDecidingColumns
            : new Dictionary<string, string>();

        // (c) 外部キーを支える索引が消える表 → 消える索引の列（旧主キー・削除する一意制約。MySQL のみ対象）
        var lostIndexes = capabilities.ForeignKeyRequiresLeadingIndex
            ? CollectLostIndexes(selected, pkChangedTables.Keys, context)
            : new Dictionary<string, List<List<string>>>(SchemaNames.Comparer);

        // 明示的に DROP が選択されている FK（自動 DROP の重複も再 ADD も行わない）。
        // 端が消える外部キーは InjectVanishingForeignKeyDrops が先に外す項目として足しているのでここに含まれる
        var explicitlyDropped = CollectExplicitlyDroppedForeignKeys(selected, limit);

        // 型の食い違いの名指しはここの 1 か所（方言に依らない）：明示に外さない live の外部キーに、端の型がこの同期で決まり
        // 同期後に両端が並べられない組があれば名指しする。その外部キーは外し直さない（下の doomed・主キーの変更の分岐）か、
        // 列の変更で外し直さない方言（PostgreSQL・Oracle）では外部キーを張ったまま列を変えるので、DB が断れば列の変更が失敗する
        foreach (var fk in EnumerateLiveForeignKeys(context))
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                !explicitlyDropped.Contains(
                    ForeignKeyKey(childTable, parentTable, fk.ColumnPairs, fk.Relationship, limit)
                )
                && HasPostSyncTypeMismatch(
                    fk,
                    childTable,
                    parentTable,
                    typeDecidingColumns,
                    capabilities
                )
            )
            {
                AddTypeMismatchWarning(
                    childTable,
                    ResolveForeignKeyName(fk.Relationship, childTable, parentTable, limit, context)
                );
            }
        }

        // (d) 列の変更で外部キーを外し直さない方言で、組の両端の型がこの同期で決まり、片側だけ変えた途中の状態が並べられない
        // 外部キー（外部キーの照合キーの集合）。外部キーを張ったまま両端を 1 列ずつ変えると、同期後の型は合っても最初の列の変更で
        // DB が断る（PostgreSQL 42804・Oracle ORA-02267）ので、外して付け直す
        var intermediateBlocked = capabilities.AlterColumnRequiresForeignKeyRebuild
            ? new HashSet<string>(StringComparer.Ordinal)
            : CollectForeignKeysBlockedInIntermediateState(
                context,
                typeDecidingColumns,
                explicitlyDropped,
                capabilities
            );

        if (
            pkChangedTables.Count == 0
            && alteredColumns.Count == 0
            && lostIndexes.Count == 0
            && intermediateBlocked.Count == 0
        )
        {
            return selected;
        }

        // 候補キーの証明に使う「同期後に存在する一意制約」（テーブル → 列集合シグネチャの集合）
        var postSyncUniques = BuildPostSyncUniqueConstraints(selected, context);

        // 候補キーの証明に使う同期後の主キーの材料（主キーの変更・追加する表・live の表）
        var addedTables = CollectAddedTables(selected);

        // 同期後の列の NULL 許容（SET NULL の外部キーの子の列が NOT NULL になるかの判定に使う）
        var postSyncNullable = PostSyncNullability(selected, context);

        // 同期後に両端の型が食い違う外部キーに関わる、変更する列（その列の変更は、残した外部キーに阻まれて必ず失敗する）
        var doomedColumns = new HashSet<string>();

        foreach (var fk in EnumerateLiveForeignKeys(context))
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            // 明示的に外す外部キーは変更を阻まない
            if (
                explicitlyDropped.Contains(
                    ForeignKeyKey(childTable, parentTable, fk.ColumnPairs, fk.Relationship, limit)
                )
            )
            {
                continue;
            }

            if (
                HasPostSyncTypeMismatch(
                    fk,
                    childTable,
                    parentTable,
                    rebuildTypeDecidingColumns,
                    capabilities
                )
            )
            {
                foreach (var pair in fk.ColumnPairs)
                {
                    doomedColumns.Add(ColumnKey(childTable, pair.ChildColumn));
                    doomedColumns.Add(ColumnKey(parentTable, pair.ParentColumn));
                }
            }

            // 外し直さない SET NULL の外部キー（下の setNullBlocked）の子の列の変更は、DB が断ると失敗する。DDL が暗黙コミット
            // される方言（TransactionalDdl が false）では、そのとき先に外したほかの外部キーが付け直されずに消えたまま残るので、
            // 失敗が決まった列の変更と同じく doomed に入れ、どの外部キーも外し直さない。DDL が巻き戻る方言では、失敗しても
            // 外したものごと戻るので、その外部キーだけを外し直さない（SQL Server は子の列の NOT NULL 化を通す）
            if (
                !capabilities.TransactionalDdl
                && BlocksSetNullRebuild(fk, childTable, capabilities, postSyncNullable)
            )
            {
                foreach (var pair in fk.ColumnPairs)
                {
                    doomedColumns.Add(ColumnKey(childTable, pair.ChildColumn));
                }
            }
        }

        doomedColumns.IntersectWith(alteredColumns);

        var autoDrops = new List<SchemaDiffItem>();
        var autoAdds = new List<SchemaDiffItem>();

        foreach (var fk in EnumerateLiveForeignKeys(context))
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);
            var referencedColumns = ForeignKeyColumnPairResolver
                .ParentColumns(fk.ColumnPairs)
                .ToList();

            // (a) 参照先テーブルの主キーが変わり、その外部キーが旧主キーに支えられているか、新しい主キーと同じ列集合の
            // 一意制約（この計画で外す）を参照している / (b) FK が参加する列の定義が変わる / (c) 子表自身の主キーが変わり、
            // 旧主キーの先頭が FK の子列（MySQL）
            // （複合外部キーは全構成列のどれか 1 つでも定義が変われば作り直しになる）
            var parentPkChanged =
                pkChangedTables.TryGetValue(parentTable, out var newPkColumns)
                && (
                    DependsOnOldPrimaryKey(liveByName, capabilities, parentTable, referencedColumns)
                    || (
                        IsSameColumnSet(newPkColumns, referencedColumns)
                        && selected.Any(i =>
                            i.Kind == SchemaDiffKind.DropUniqueConstraint
                            && SchemaNames.AreSame(
                                SchemaNames.ToStoredForm(i.TableName),
                                parentTable
                            )
                            && CanSupportForeignKey(
                                capabilities,
                                i.UniqueConstraintColumns,
                                referencedColumns
                            )
                        )
                    )
                );
            var columnAltered =
                fk.ColumnPairs.Any(p =>
                    alteredColumns.Contains(ColumnKey(childTable, p.ChildColumn))
                    || alteredColumns.Contains(ColumnKey(parentTable, p.ParentColumn))
                )
                || intermediateBlocked.Contains(
                    ForeignKeyKey(childTable, parentTable, fk.ColumnPairs, fk.Relationship, limit)
                );
            var ownIndexLost =
                lostIndexes.TryGetValue(childTable, out var indexes)
                && indexes.Any(index => IsLeadingPrefix(index, fk.ColumnPairs));

            if (!parentPkChanged && !columnAltered && !ownIndexLost)
            {
                continue;
            }

            if (
                explicitlyDropped.Contains(
                    ForeignKeyKey(childTable, parentTable, fk.ColumnPairs, fk.Relationship, limit)
                )
            )
            {
                continue;
            }

            var constraintName = ResolveForeignKeyName(
                fk.Relationship,
                childTable,
                parentTable,
                limit,
                context
            );

            // SET NULL の外部キーの子の列が同期後に NOT NULL になると、作れない方言（SQL Server・MySQL）では付け直しが必ず
            // 失敗する（1761 / 1830）。外し直さずに名指しする（MySQL は上の doomed で、どの外部キーも外し直さない）
            var setNullBlocked = BlocksSetNullRebuild(
                fk,
                childTable,
                capabilities,
                postSyncNullable
            );

            if (setNullBlocked)
            {
                AddSetNullWarning(
                    warnings,
                    childTable,
                    constraintName,
                    PostSyncNotNullChildColumns(fk, childTable, postSyncNullable)
                );
            }

            // 同期後に両端の列の型が食い違う外部キーがあると、付け直しは必ず失敗し、その外部キーを残せば列の変更が
            // 失敗する。どちらにしても列の変更の段で止まり、外し直しの付け直しはその後にしか来ないので、外すと DDL が
            // 暗黙コミットされる方言（MySQL）では外部キーが消えたまま残る。そこで失敗が決まった列の変更が 1 つでもあれば、
            // どの外部キーも外さずに列の変更の時点で失敗させる（食い違う外部キーは上で名指し済み。同じ列のほかの
            // 外部キー・残した外部キーが参照する親の列の外部キー・無関係な列の外部キーも外さない）
            if (doomedColumns.Count > 0)
            {
                continue;
            }

            if (setNullBlocked)
            {
                continue;
            }

            // 主キーの変更での付け直しも、構成列の型を変えて同期後に両端の型が食い違うなら必ず失敗する（列の変更で外し直しを
            // 要しない方言でも）。外さずに主キーの解除の時点で失敗させる（食い違う外部キーは上で名指し済み。構成列の NULL 許容
            // だけを変える外部キーは型を変えないので、DB が受け入れている型のまま付け直す）
            if (
                parentPkChanged
                && HasPostSyncTypeMismatch(
                    fk,
                    childTable,
                    parentTable,
                    typeDecidingColumns,
                    capabilities
                )
            )
            {
                continue;
            }

            var description = string.Format(Strings.Diff_AutoForeignKeyRebuild, constraintName);

            // 付け直す外部キーの参照先が同期後の候補キー（同期後の主キーか一意制約と列の集合で一致）であることを、外し直しの理由に
            // 依らず証明する（主キーの変更・列の変更・途中の状態・索引の喪失）。証明できなければ名指しし、暗黙コミットの方言では
            // 付け直しを外部キーの追加の最後へ回す（ArrangeForeignKeyAddsByFailureRisk）。主キーの変更の付け直しにだけ掛けると、
            // 参照先の唯一の一意制約を同じ計画で外しながら列の変更で付け直す外部キーが、名指しされずに付け直しで断られ、
            // 暗黙コミットの方言（MySQL・Oracle）で消えたまま残る
            if (
                !StaysCandidateKey(
                    PostSyncPrimaryKeyOf(parentTable, pkChangedTables, addedTables, liveByName),
                    postSyncUniques,
                    parentTable,
                    referencedColumns
                )
            )
            {
                warnings.Add(
                    new SyncPlanWarning(
                        SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey,
                        childTable,
                        constraintName
                    )
                );
            }

            autoDrops.Add(BuildImplicitForeignKeyDrop(fk, childTable, description));

            autoAdds.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddForeignKey,
                    TableName = childTable,
                    ColumnName = fk.ColumnPairs[0].ChildColumn,
                    Entity = fk.Child,
                    ParentEntity = fk.Parent,
                    ChildEntity = fk.Child,
                    Relationship = fk.Relationship,
                    ForeignKeyName = constraintName,
                    ForeignKeyColumnPairs = fk.ColumnPairs,
                    Description = description,
                }
            );
        }

        if (autoDrops.Count == 0)
        {
            return selected;
        }

        // 自動 DROP は DropForeignKey セクションの先頭側・再 ADD は AddForeignKey セクションの末尾側へ置く
        // （BuildSections は種別で抽出しつつ入力順を保持するため、この並びがそのままセクション内の順序になる）
        return [.. autoDrops, .. selected, .. autoAdds];

        // 食い違う外部キーを名指しする。選んだ作り直しを型の食い違いで外した外部キー
        // （ExcludeForeignKeysWithPostSyncTypeMismatch）は既に名指ししているので、同じ警告を重ねない
        void AddTypeMismatchWarning(string childTable, string constraintName)
        {
            var warning = new SyncPlanWarning(
                SyncPlanWarningKind.ForeignKeyColumnTypeMismatch,
                childTable,
                constraintName
            );

            if (!warnings.Contains(warning))
            {
                warnings.Add(warning);
            }
        }
    }

    /// <summary>
    /// 外部キーが参照先の旧主キー（live の主キー）に支えられているか（主キーを外すと巻き込まれるか）。
    /// </summary>
    /// <remarks>
    /// 判定は <see cref="CanSupportForeignKey"/>（同じ列集合か、MySQL では旧主キーの先頭の列だけを参照する非標準キーの外部キーも）。
    /// 一意制約を参照する外部キーは巻き込まれない。参照先が live に無ければ支えられているとみなす。
    /// </remarks>
    private static bool DependsOnOldPrimaryKey(
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
        string description
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
        };

    /// <summary>外部キーの子列が、索引の先頭の列とちょうど同じ集合か</summary>
    /// <remarks>
    /// MySQL は外部キーの子列を先頭に持つ索引を要し、それが消える索引しか無いと索引を外せない（エラー 1553）。
    /// 別の索引が支えている場合も外し直すことになるが、外部キーを外して付け直すだけなので害は無い。
    /// </remarks>
    private static bool IsLeadingPrefix(
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
    /// 同期で外す索引（主キーを変える表の旧主キー・削除を選んだ一意制約）を、表ごとに列の並びで集める。
    /// </summary>
    /// <remarks>
    /// 外部キーを支える唯一の索引を外すと MySQL はエラー 1553 で断るので、これらの索引の先頭に子列が乗る外部キーは
    /// 外してから付け直す（付け直しのとき MySQL が必要な索引を自動で作る）。一意制約の削除は、明示的に選んだものだけを
    /// 見る（MySQL は列の変更で一意制約を外し直さない）。
    /// </remarks>
    private static Dictionary<string, List<List<string>>> CollectLostIndexes(
        IReadOnlyList<SchemaDiffItem> selected,
        IEnumerable<string> primaryKeyChangedTables,
        SyncPlanContext context
    )
    {
        var lost = new Dictionary<string, List<List<string>>>(SchemaNames.Comparer);
        var liveByName = IndexLiveTables(context);

        foreach (var table in primaryKeyChangedTables)
        {
            if (liveByName.TryGet(table, out var live))
            {
                Indexes(table).Add([.. live.GetPrimaryKeyColumnsInOrder().Select(c => c.Name)]);
            }
        }

        foreach (var drop in selected.Where(i => i.Kind == SchemaDiffKind.DropUniqueConstraint))
        {
            Indexes(SchemaNames.ToStoredForm(drop.TableName))
                .Add([.. drop.UniqueConstraintColumns]);
        }

        return lost;

        List<List<string>> Indexes(string table)
        {
            if (!lost.TryGetValue(table, out var indexes))
            {
                indexes = [];
                lost[table] = indexes;
            }

            return indexes;
        }
    }

    /// <summary>
    /// この同期で型が決まる列（列の照合キー → 同期後の型）を集める：列の変更の項目のうち、live に無い列か、live の型と
    /// <see cref="SyncDialectCapabilities.AreSameType"/> で同じでない型へ変える列
    /// </summary>
    /// <param name="items">項目（列の変更以外は読まない）</param>
    /// <param name="liveByName">live の表の索引</param>
    /// <param name="capabilities">型の比較に使う方言のケーパビリティ</param>
    /// <remarks>
    /// 外部キーの両端の型の照合（<see cref="HasPostSyncTypeMismatch"/>・<see cref="ExcludeForeignKeysWithPostSyncTypeMismatch"/>）は、
    /// この関数が返す列だけを「この同期で型が決まる」とみなす。NULL 許容だけを変える列の変更は型を決めない：DB はその外部キーを
    /// 今の両端の型のまま受け入れているので、型を変えずに外して付け直せば同じ組の型で通る。数えると、DB が受け入れている型の違う
    /// 外部キー（PostgreSQL の integer → bigint など）を「付け直せない」と名指しして外し直さず、その外部キーに阻まれる主キーの
    /// 解除が必ず失敗した。
    /// </remarks>
    private static Dictionary<string, string> CollectTypeDecidingColumns(
        IEnumerable<SchemaDiffItem> items,
        NameLookup<Entity> liveByName,
        SyncDialectCapabilities capabilities
    )
    {
        var decided = new Dictionary<string, string>();

        foreach (
            var alter in items.Where(i =>
                i.Kind == SchemaDiffKind.AlterColumn && i.ColumnName is not null
            )
        )
        {
            var newType = alter.Column?.DataType ?? string.Empty;
            var table = SchemaNames.ToStoredForm(alter.TableName);

            if (
                liveByName.TryGet(table, out var live)
                && live.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, alter.ColumnName))
                    is { } liveColumn
                && capabilities.AreSameType(liveColumn.DataType, newType)
            )
            {
                continue;
            }

            decided[ColumnKey(alter.TableName, alter.ColumnName!)] = newType;
        }

        return decided;
    }

    /// <summary>
    /// 端の型がこの同期で決まる組のうち、同期後（その型を当てた後）に両端の列の型が並べられない組がある外部キーか
    /// </summary>
    /// <param name="fk">live の外部キー</param>
    /// <param name="childTable">子表</param>
    /// <param name="parentTable">親表</param>
    /// <param name="typeDecidingColumns">この同期で型が決まる列（<see cref="CollectTypeDecidingColumns"/>）</param>
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
    private static bool HasPostSyncTypeMismatch(
        ResolvedForeignKeyRelationship fk,
        string childTable,
        string parentTable,
        Dictionary<string, string> typeDecidingColumns,
        SyncDialectCapabilities capabilities,
        string? column = null
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
    /// 外部キーを張ったまま両端を 1 列ずつ変えると、片側だけ変えた途中の状態で DB が断る live の外部キー（照合キー
    /// <see cref="ForeignKeyKey"/> の集合）を集める
    /// </summary>
    /// <param name="context">live スキーマ</param>
    /// <param name="typeDecidingColumns">この同期で型が決まる列（<see cref="CollectTypeDecidingColumns"/>）</param>
    /// <param name="explicitlyDropped">明示的に外す外部キー（数えない）</param>
    /// <param name="capabilities">型の比較に使う方言のケーパビリティ</param>
    /// <remarks>
    /// 対象は、同期後の型が並べられる（<see cref="HasPostSyncTypeMismatch"/> が偽＝食い違う外部キーは名指しして外さない）うえで、
    /// ある組の両端とも型がこの同期で決まり、「新しい子 × 古い親」か「古い子 × 新しい親」のどちらかが
    /// <see cref="SyncDialectCapabilities.AreForeignKeyCompatibleTypes"/> で並べられない外部キー。列の変更の順はどちらもありうるので、
    /// どちらかの途中の状態が並べられなければ外す。片側だけ決まる組は途中の状態＝同期後の状態なので、食い違いの名指しが受け持つ。
    /// 列の変更で外部キーを外し直さない方言（PostgreSQL・Oracle）は外部キーを張ったまま列を変えるので、外さないと、外部キーの両端を
    /// 同じ別の型（int → varchar(10)）へ変える同期が、名指しも外し直しもされずに最初の列の変更で断られる（PostgreSQL 42804・
    /// Oracle ORA-02267）。
    /// </remarks>
    private static HashSet<string> CollectForeignKeysBlockedInIntermediateState(
        SyncPlanContext context,
        Dictionary<string, string> typeDecidingColumns,
        HashSet<string> explicitlyDropped,
        SyncDialectCapabilities capabilities
    )
    {
        var limit = capabilities.IdentifierLengthLimit;
        var blocked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fk in EnumerateLiveForeignKeys(context))
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);
            var key = ForeignKeyKey(
                childTable,
                parentTable,
                fk.ColumnPairs,
                fk.Relationship,
                limit
            );

            if (
                explicitlyDropped.Contains(key)
                || HasPostSyncTypeMismatch(
                    fk,
                    childTable,
                    parentTable,
                    typeDecidingColumns,
                    capabilities
                )
            )
            {
                continue;
            }

            foreach (var pair in fk.ColumnPairs)
            {
                if (
                    typeDecidingColumns.TryGetValue(
                        ColumnKey(childTable, pair.ChildColumn),
                        out var newChild
                    )
                    && typeDecidingColumns.TryGetValue(
                        ColumnKey(parentTable, pair.ParentColumn),
                        out var newParent
                    )
                    && (
                        !capabilities.AreForeignKeyCompatibleTypes(
                            newChild,
                            ColumnTypeOf(fk.Parent, pair.ParentColumn)
                        )
                        || !capabilities.AreForeignKeyCompatibleTypes(
                            ColumnTypeOf(fk.Child, pair.ChildColumn),
                            newParent
                        )
                    )
                )
                {
                    blocked.Add(key);
                    break;
                }
            }
        }

        return blocked;
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
    private static bool IsMismatchedUnacceptedPair(
        (string Type, bool Decided) child,
        (string Type, bool Decided) parent,
        bool liveHasPair,
        SyncDialectCapabilities capabilities
    ) =>
        (child.Decided || parent.Decided || !liveHasPair)
        && !capabilities.AreForeignKeyCompatibleTypes(child.Type, parent.Type);

    /// <summary>live の外部キーが持つ組（<see cref="ForeignKeyPairKey"/>）の集合</summary>
    private static HashSet<string> CollectLiveForeignKeyPairs(SyncPlanContext context)
    {
        var pairs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fk in EnumerateLiveForeignKeys(context))
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            foreach (var pair in fk.ColumnPairs)
            {
                pairs.Add(
                    ForeignKeyPairKey(childTable, pair.ChildColumn, parentTable, pair.ParentColumn)
                );
            }
        }

        return pairs;
    }

    /// <summary>外部キーの 1 組の照合キー（子の列と親の列。名前の比べ方は列の照合キーと同じ）</summary>
    private static string ForeignKeyPairKey(
        string childTable,
        string childColumn,
        string parentTable,
        string parentColumn
    ) => ColumnKey(childTable, childColumn) + "\u0002" + ColumnKey(parentTable, parentColumn);

    // ---------------- 主キーの暗黙の外し直し（SQL Server） ----------------

    /// <summary>
    /// 選択された列定義変更が主キーの構成列に及ぶとき、その主キーを同じ構成のまま外して付け直す項目を注入する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 対象は <see cref="SyncDialectCapabilities.AlterColumnRequiresKeyConstraintRebuild"/> が <c>true</c> の方言
    /// （SQL Server）のみ。主キーの構成列は主キー制約を外さずに <c>ALTER COLUMN</c> できない（Msg 5074）ため、
    /// live の主キー構成・順序のままの <see cref="SchemaDiffKind.AlterPrimaryKey"/> を注入し、主キー変更と同じ
    /// 「解除 → 列定義変更 → 付与」の 3 フェーズに乗せる。付け直す主キーの名前は主キー変更と同じ
    /// <c>PK_{テーブル}</c>（旧い名前は取り込んでいないため保てない）。
    /// </para>
    /// <para>
    /// 利用者がそのテーブルの主キー変更を選んでいれば注入しない（その変更が解除と付与を行う）。
    /// 主キーを参照する外部キーは、被参照列が変わる列そのものなので、外部キーの暗黙の外し直しが外して戻す。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> InjectImplicitPrimaryKeyRebuilds(
        List<SchemaDiffItem> sectionItems,
        IReadOnlyList<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context
    )
    {
        if (context is null || !capabilities.AlterColumnRequiresKeyConstraintRebuild)
        {
            return sectionItems;
        }

        var pkChangedTables = CollectPrimaryKeyChanges(selected);
        var injected = new List<SchemaDiffItem>();

        foreach (var live in context.LiveEntities)
        {
            var table = SchemaDiffService.NormalizeTable(live);

            if (pkChangedTables.ContainsKey(table))
            {
                continue;
            }

            var pkColumns = live.GetPrimaryKeyColumnsInOrder();
            var touchesPrimaryKey = selected.Any(i =>
                i.Kind == SchemaDiffKind.AlterColumn
                && i.ColumnName is not null
                && SchemaNames.AreSame(SchemaNames.ToStoredForm(i.TableName), table)
                && pkColumns.Any(c => SchemaNames.AreSame(c.Name, i.ColumnName))
            );

            if (!touchesPrimaryKey)
            {
                continue;
            }

            injected.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterPrimaryKey,
                    TableName = table,
                    // 付与フェーズは Entity の主キー列を実効順で読む＝live のまま付け直す
                    Entity = live,
                    Description = string.Format(
                        Strings.Diff_AutoPrimaryKeyRebuild,
                        string.Join(", ", pkColumns.Select(c => c.Name))
                    ),
                }
            );
        }

        return injected.Count == 0 ? sectionItems : [.. sectionItems, .. injected];
    }

    // ---------------- 候補キー喪失の判定（逐次 DDL 方言・再構築方言で共有） ----------------

    /// <summary>選択済みの主キー変更を「テーブル名 → 新しい主キー列名の集合」へまとめる</summary>
    private static Dictionary<string, List<string>> CollectPrimaryKeyChanges(
        IReadOnlyList<SchemaDiffItem> selected
    )
    {
        var pkChangedTables = new Dictionary<string, List<string>>(SchemaNames.Comparer);

        foreach (var item in selected.Where(i => i.Kind == SchemaDiffKind.AlterPrimaryKey))
        {
            // 新しい主キーの列を実効順で持つ（候補キーの証明は列の集合・外部キーの並べ替えは並びを使う）
            pkChangedTables[SchemaNames.ToStoredForm(item.TableName)] =
            [
                .. item.Entity?.GetPrimaryKeyColumnsInOrder().Select(c => c.Name) ?? [],
            ];
        }

        return pkChangedTables;
    }

    /// <summary>主キーが変わる表を参照する外部キーの被参照列が、同期後も候補キーであることを証明できるか</summary>
    /// <remarks>
    /// 根拠は「同期後の主キーが被参照列集合とちょうど一致」か「同期後に同じ列集合の一意制約が在る」のいずれか。
    /// 被参照列が新主キーに含まれていても他の列と複合になっていれば主キーは根拠にならない
    /// （(id) → (id, code) の拡張は 4 方言中 3 方言で再 ADD が失敗する）。
    /// 一意制約はモデルの正本（Entity.UniqueConstraints）から同期後の集合を厳密に合成して判定するため、
    /// 「自然キー UNIQUE を持つ表の主キー付け替え」は誤警告しない。証明できない構成は
    /// （一意インデックス等で実際には通ることがあっても）警告する＝実行は止めない安全側へ倒す。
    /// 比べるのは列の集合で、どの方言でも並びを問わない（参照する列の並びがキーの並びと一致しないと外部キーを作れない
    /// 方言では、計画が付け直しを参照先の同期後のキーの並びへ並べ替える＝<see cref="ArrangeForeignKeysToPostSyncKeys"/>）。
    /// </remarks>
    private static bool StaysCandidateKey(
        IReadOnlyList<string>? newPkColumns,
        Dictionary<string, HashSet<string>> postSyncUniques,
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
    /// 再構築方言で、主キーを変える表を参照する live 外部キーのうち、被参照列が候補キーでなくなりうるものを警告する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 再構築方言は外部キーを外して付け直すのではなく、子表の外部キー宣言を残したまま親表を作り直す。
    /// 被参照列が候補キーでなくなると、同期後の <c>foreign_key_check</c> が foreign key mismatch で失敗する
    /// （ロールバックされるので安全側だが、逐次 DDL の方言と同じく実行前に知らせる）。判定は
    /// <see cref="StaysCandidateKey"/> を逐次 DDL の方言と共有する。
    /// </para>
    /// <para>
    /// 明示的に削除する外部キー（選択済み DropForeignKey）と、削除する子表（選択済み DropTable）の外部キーは、
    /// 同期後に残らないので対象外にする。
    /// </para>
    /// </remarks>
    private static void WarnRebuildForeignKeysLosingCandidateKey(
        IReadOnlyList<SchemaDiffItem> selected,
        SyncPlanContext context,
        List<SyncPlanWarning> warnings,
        IdentifierLengthLimit limit
    )
    {
        var pkChangedTables = CollectPrimaryKeyChanges(selected);

        if (pkChangedTables.Count == 0)
        {
            return;
        }

        var postSyncUniques = BuildPostSyncUniqueConstraints(selected, context);
        var dropped = DroppedObjects.From(selected);
        var explicitlyDropped = CollectExplicitlyDroppedForeignKeys(selected, limit);

        foreach (var fk in EnumerateLiveForeignKeys(context))
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                !pkChangedTables.TryGetValue(parentTable, out var newPkColumns)
                || dropped.Tables.Contains(childTable)
                || explicitlyDropped.Contains(
                    ForeignKeyKey(childTable, parentTable, fk.ColumnPairs, fk.Relationship, limit)
                )
            )
            {
                continue;
            }

            var referencedColumns = ForeignKeyColumnPairResolver
                .ParentColumns(fk.ColumnPairs)
                .ToList();

            if (StaysCandidateKey(newPkColumns, postSyncUniques, parentTable, referencedColumns))
            {
                continue;
            }

            warnings.Add(
                new SyncPlanWarning(
                    SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey,
                    childTable,
                    ResolveForeignKeyName(fk.Relationship, childTable, parentTable, limit, context)
                )
            );
        }
    }

    /// <summary>
    /// 選んだ外部キーの追加（作り直しは展開後の追加）の参照先の列が、同期後の主キーにも一意制約にもちょうど一致しない
    /// ものを <see cref="SyncPlanWarningKind.ForeignKeyNotOnCandidateKey"/> で名指しする。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同期後の主キーは、主キーの変更を選んだ表はその新しい主キー、追加を選んだ表は図の主キー、それ以外は live の主キー。
    /// 同期後の一意制約は <see cref="BuildPostSyncUniqueConstraints"/>（live − 削除 ＋ 追加、追加を選んだ表の一意制約を含む）。
    /// 比べ方は暗黙の付け直しと同じ <see cref="StaysCandidateKey"/>（列の集合）。
    /// </para>
    /// <para>
    /// 参照先の表が live にも追加にも無い項目は対象外（選んでいない表の追加を前提にする項目は計画の入口で外している）。
    /// </para>
    /// </remarks>
    private static void WarnAddedForeignKeysNotOnCandidateKey(
        IReadOnlyList<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings
    )
    {
        var adds = selected
            .Where(i =>
                i.Kind == SchemaDiffKind.AddForeignKey
                && i.ChildEntity is not null
                && i.ParentEntity is not null
            )
            .ToList();

        if (context is null || adds.Count == 0)
        {
            return;
        }

        var pkChangedTables = CollectPrimaryKeyChanges(selected);
        var addedTables = CollectAddedTables(selected);
        var liveByName = IndexLiveTables(context);
        var postSyncUniques = BuildPostSyncUniqueConstraints(selected, context);

        foreach (var add in adds)
        {
            var childTable = SchemaDiffService.NormalizeTable(add.ChildEntity!);
            var parentTable = SchemaDiffService.NormalizeTable(add.ParentEntity!);
            var postSyncKey = PostSyncPrimaryKeyOf(
                parentTable,
                pkChangedTables,
                addedTables,
                liveByName
            );

            if (postSyncKey is null)
            {
                continue;
            }

            var referencedColumns = ForeignKeyColumnPairResolver
                .ParentColumns(SyncScriptBuilderHelper.ResolveColumnPairs(add))
                .ToList();

            if (
                referencedColumns.Count == 0
                || StaysCandidateKey(postSyncKey, postSyncUniques, parentTable, referencedColumns)
            )
            {
                continue;
            }

            warnings.Add(
                new SyncPlanWarning(
                    SyncPlanWarningKind.ForeignKeyNotOnCandidateKey,
                    childTable,
                    SelectedForeignKeyName(add, capabilities.IdentifierLengthLimit)
                )
            );
        }
    }

    /// <summary>
    /// 選んだ外部キーの追加・作り直しのうち、同期後に両端の列の型が食い違うものを計画から外し、
    /// <see cref="SyncPlanWarningKind.ForeignKeyColumnTypeMismatch"/> で名指しする（逐次 DDL の方言のみ）。
    /// </summary>
    /// <param name="selected">選択済みの項目（外部キーの作り直しは展開前）</param>
    /// <param name="capabilities">型の比較に使う方言のケーパビリティ</param>
    /// <param name="context">live スキーマ（無ければ何も外さない）</param>
    /// <param name="warnings">外した項目の警告を積む先</param>
    /// <returns>残す項目</returns>
    /// <remarks>
    /// <para>
    /// 同期後の型は、型を変える列の変更を選んだ列はその新しい型、live に在る列は live の型、live に無い列（追加する表・列）は図の型。
    /// 判定は、構成列のどれかの型がこの同期で決まるときだけ（<see cref="CollectTypeDecidingColumns"/>＝NULL 許容だけの変更は型を
    /// 決めない。live の時点で綴りだけ違う・DB が受け入れている型の違う既存の外部キーの作り直しは止めない）。
    /// 型の比較は暗黙の付け直しと同じ <see cref="SyncDialectCapabilities.AreForeignKeyCompatibleTypes"/>。
    /// </para>
    /// <para>
    /// 食い違う外部キーの追加は必ず失敗する。作り直しは展開前の 1 項目として外すので、削除だけが走って外部キーが
    /// 消えたまま残ることはない（DDL が暗黙コミットされる方言では、付け直しの失敗で消えたまま残っていた）。
    /// 作り直しを外した外部キーは live に残るので、列の変更で外部キーを外し直す方言では、暗黙の付け直しがこの外部キーを
    /// 食い違う外部キーとして数え、ほかの外部キーも外し直さない（<see cref="InjectImplicitForeignKeyRebuilds"/>）。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> ExcludeForeignKeysWithPostSyncTypeMismatch(
        List<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        if (context is null)
        {
            return selected;
        }

        var liveByName = IndexLiveTables(context);
        var typeDecidingColumns = CollectTypeDecidingColumns(selected, liveByName, capabilities);
        var livePairs = CollectLiveForeignKeyPairs(context);
        var kept = new List<SchemaDiffItem>(selected.Count);

        foreach (var item in selected)
        {
            if (
                item.Kind is SchemaDiffKind.AddForeignKey or SchemaDiffKind.AlterForeignKey
                && item.ChildEntity is { } child
                && item.ParentEntity is { } parent
            )
            {
                var childTable = SchemaDiffService.NormalizeTable(child);
                var parentTable = SchemaDiffService.NormalizeTable(parent);
                var pairs = SyncScriptBuilderHelper.ResolveColumnPairs(item);

                // 組単位で判定する（DB が受け入れた組＝両端とも型が決まらず live に同じ組の外部キーがある組は数えない＝
                // IsMismatchedUnacceptedPair）。DB にまだ無い組は、両端とも型の決まらない live の列でも数える
                if (
                    pairs.Any(pair =>
                        IsMismatchedUnacceptedPair(
                            PostSyncType(child, childTable, pair.ChildColumn),
                            PostSyncType(parent, parentTable, pair.ParentColumn),
                            livePairs.Contains(
                                ForeignKeyPairKey(
                                    childTable,
                                    pair.ChildColumn,
                                    parentTable,
                                    pair.ParentColumn
                                )
                            ),
                            capabilities
                        )
                    )
                )
                {
                    ExcludeSelected(
                        item,
                        new SyncPlanWarning(
                            SyncPlanWarningKind.ForeignKeyColumnTypeMismatch,
                            childTable,
                            SelectedForeignKeyName(item, capabilities.IdentifierLengthLimit)
                        ),
                        warnings,
                        excluded
                    );
                    continue;
                }

                // 構成列の同期後の型と、その型がこの同期で決まるか（列の変更の項目の型・live に無い列は図の型＝決まる。
                // live の列は live の型＝決まらない）
                (string Type, bool Decided) PostSyncType(
                    Entity diagramEntity,
                    string table,
                    string column
                )
                {
                    if (typeDecidingColumns.TryGetValue(ColumnKey(table, column), out var decided))
                    {
                        return (decided, true);
                    }

                    if (
                        liveByName.TryGet(table, out var live)
                        && live.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, column))
                            is { } liveColumn
                    )
                    {
                        return (liveColumn.DataType, false);
                    }

                    return (ColumnTypeOf(diagramEntity, column), true);
                }
            }

            kept.Add(item);
        }

        return kept;
    }

    /// <summary>
    /// 選んだ外部キーの追加・作り直しのうち、参照アクション <c>SET NULL</c> を使い、子の列が同期後に NOT NULL になるものを
    /// <see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/> で名指しする。
    /// <see cref="SyncDialectCapabilities.SetNullRequiresNullableChildColumn"/> が <c>true</c> の方言（SQL Server・MySQL）では
    /// 計画から外す（作り直しは展開前の 1 項目として外すので、削除だけが走って外部キーが消えたまま残らない）。
    /// </summary>
    /// <param name="selected">選択済みの項目（外部キーの作り直しは展開前）</param>
    /// <param name="capabilities">方言のケーパビリティ（方言が書く参照アクションと、作れるかの宣言）</param>
    /// <param name="context">live スキーマ（無ければ図の値で見る）</param>
    /// <param name="warnings">名指しの警告を積む先</param>
    /// <returns>残す項目</returns>
    /// <remarks>
    /// 子の列の同期後の NULL 許容は、選んだ主キーの変更で主キーに入る列は NOT NULL、選んだ列の変更があればその値、
    /// live に在る列は live の値、live に無い列（追加する表・列）は図の値（<see cref="PostSyncNullability"/>）。
    /// 参照アクションは方言が書くもの（<see cref="SyncDialectCapabilities.EffectiveOnDelete"/> /
    /// <see cref="SyncDialectCapabilities.EffectiveOnUpdate"/>）で見る＝ON UPDATE を持たない方言（Oracle）の ON UPDATE は数えない。
    /// </remarks>
    private static List<SchemaDiffItem> ExcludeSetNullForeignKeysOnNotNullColumns(
        List<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        var postSyncNullable = PostSyncNullability(selected, context);
        var kept = new List<SchemaDiffItem>(selected.Count);

        foreach (var item in selected)
        {
            if (
                item.Kind is SchemaDiffKind.AddForeignKey or SchemaDiffKind.AlterForeignKey
                && item.Relationship is { } relationship
                && item.ChildEntity is { } child
                && item.ParentEntity is { } parent
                && UsesSetNull(relationship, capabilities)
            )
            {
                var childTable = SchemaDiffService.NormalizeTable(child);
                var notNull = ForeignKeyColumnPairResolver
                    .ChildColumns(SyncScriptBuilderHelper.ResolveColumnPairs(item))
                    .Where(column => !postSyncNullable(child, childTable, column))
                    .ToList();

                if (notNull.Count > 0)
                {
                    var constraintName = SelectedForeignKeyName(
                        item,
                        capabilities.IdentifierLengthLimit
                    );

                    if (capabilities.SetNullRequiresNullableChildColumn)
                    {
                        ExcludeSelected(
                            item,
                            SetNullWarning(childTable, constraintName, notNull),
                            warnings,
                            excluded
                        );
                        continue;
                    }

                    AddSetNullWarning(warnings, childTable, constraintName, notNull);
                }
            }

            kept.Add(item);
        }

        return kept;
    }

    /// <summary>
    /// 選んだ列の変更（主キーの変更に伴う暗黙の NOT NULL 化を含む）で、DB にある参照アクション <c>SET NULL</c> の外部キーの
    /// 子の列が NOT NULL になるとき、その外部キーを <see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/> で名指しする
    /// （明示的に外す外部キーは除く）。方言に依らない（作れる方言でも、参照される親の行の削除・更新が失敗する）。
    /// </summary>
    /// <remarks>
    /// 名指しするのは、この同期で NOT NULL になる子の列があるときだけ（live の時点で既に NOT NULL の子の列を持つ外部キーは、
    /// 同期が変えるものではない）。列は同期後に NOT NULL の子の列をすべて並べる。作れない方言で外し直さないのは
    /// <see cref="InjectImplicitForeignKeyRebuilds"/>。
    /// </remarks>
    private static void WarnSetNullOnNotNullLiveForeignKeys(
        IReadOnlyList<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings
    )
    {
        if (context is null)
        {
            return;
        }

        var limit = capabilities.IdentifierLengthLimit;
        var explicitlyDropped = CollectExplicitlyDroppedForeignKeys(selected, limit);
        var postSyncNullable = PostSyncNullability(selected, context);

        foreach (var fk in EnumerateLiveForeignKeys(context))
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                !UsesSetNull(fk.Relationship, capabilities)
                || explicitlyDropped.Contains(
                    ForeignKeyKey(childTable, parentTable, fk.ColumnPairs, fk.Relationship, limit)
                )
            )
            {
                continue;
            }

            // この同期で NOT NULL になる子の列（live では NULL を許す列）
            var madeNotNull = fk.ColumnPairs.Any(p =>
                fk.Child.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, p.ChildColumn))
                    is { } childColumn
                && ColumnNullability.AllowsNull(childColumn)
                && !postSyncNullable(fk.Child, childTable, p.ChildColumn)
            );

            if (madeNotNull)
            {
                AddSetNullWarning(
                    warnings,
                    childTable,
                    ResolveForeignKeyName(fk.Relationship, childTable, parentTable, limit, context),
                    PostSyncNotNullChildColumns(fk, childTable, postSyncNullable)
                );
            }
        }
    }

    /// <summary>
    /// 作れない方言で、この外部キーの付け直しが必ず失敗するか（参照アクション <c>SET NULL</c> で、子の列が同期後に NOT NULL）
    /// </summary>
    private static bool BlocksSetNullRebuild(
        ResolvedForeignKeyRelationship fk,
        string childTable,
        SyncDialectCapabilities capabilities,
        Func<Entity, string, string, bool> postSyncNullable
    ) =>
        capabilities.SetNullRequiresNullableChildColumn
        && UsesSetNull(fk.Relationship, capabilities)
        && PostSyncNotNullChildColumns(fk, childTable, postSyncNullable).Count > 0;

    /// <summary>live の外部キーの子の列のうち、同期後に NOT NULL のもの（宣言順）</summary>
    private static List<string> PostSyncNotNullChildColumns(
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
    private static bool UsesSetNull(
        Relationship relationship,
        SyncDialectCapabilities capabilities
    ) =>
        capabilities.EffectiveOnDelete(relationship.OnDelete) == ForeignKeyReferentialAction.SetNull
        || capabilities.EffectiveOnUpdate(relationship.OnUpdate)
            == ForeignKeyReferentialAction.SetNull;

    /// <summary>
    /// 同期後の列の NULL 許容を引く関数を作る（引数は列の表のエンティティ・正規化した表名・列名）。
    /// </summary>
    /// <remarks>
    /// 選んだ主キーの変更で主キーに入る列は NOT NULL、選んだ列の変更があればその値、live に在る列は live の値、
    /// live に無い列は渡したエンティティ（図）の値。主キーの列は NULL を許さない。
    /// </remarks>
    private static Func<Entity, string, string, bool> PostSyncNullability(
        IReadOnlyList<SchemaDiffItem> selected,
        SyncPlanContext? context
    )
    {
        var altered = new Dictionary<string, bool>();
        var newKeyColumns = new HashSet<string>();

        foreach (var item in selected)
        {
            if (
                item.Kind == SchemaDiffKind.AlterColumn
                && item.ColumnName is not null
                && item.Column is not null
            )
            {
                altered[ColumnKey(item.TableName, item.ColumnName)] = ColumnNullability.AllowsNull(
                    item.Column
                );
            }
            else if (item.Kind == SchemaDiffKind.AlterPrimaryKey && item.Entity is not null)
            {
                foreach (var column in item.Entity.GetPrimaryKeyColumnsInOrder())
                {
                    newKeyColumns.Add(ColumnKey(item.TableName, column.Name));
                }
            }
        }

        var liveByName = context is null ? null : IndexLiveTables(context);

        return (entity, table, column) =>
        {
            var key = ColumnKey(table, column);

            if (newKeyColumns.Contains(key))
            {
                return false;
            }

            if (altered.TryGetValue(key, out var nullable))
            {
                return nullable;
            }

            var source =
                liveByName is not null
                && liveByName.TryGet(table, out var live)
                && live.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, column))
                    is { } liveColumn
                    ? liveColumn
                    : entity.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, column));

            return source is null || ColumnNullability.AllowsNull(source);
        };
    }

    /// <summary>
    /// <see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/> を積む（同じ外部キーの同じ警告は重ねない）
    /// </summary>
    private static void AddSetNullWarning(
        List<SyncPlanWarning> warnings,
        string childTable,
        string constraintName,
        IReadOnlyList<string> notNullColumns
    )
    {
        var warning = SetNullWarning(childTable, constraintName, notNullColumns);

        if (!warnings.Contains(warning))
        {
            warnings.Add(warning);
        }
    }

    /// <summary><see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/> の警告（Detail は「制約名 (同期後に NOT NULL の子の列, …)」）</summary>
    private static SyncPlanWarning SetNullWarning(
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
    /// テーブル再構築の方言で、取込が捨てた表の中の物がある表を作り直す選んだ項目を計画から外し、
    /// <see cref="SyncPlanWarningKind.TableRebuildLosesImportDiscardedContent"/> で名指しする。
    /// </summary>
    /// <param name="selected">依存の除外を済ませた選んだ項目</param>
    /// <param name="discarded">表 → 取込が捨てた物（<see cref="CollectImportDiscardedContent"/>）</param>
    /// <param name="context">live スキーマ</param>
    /// <param name="warnings">警告の積み先</param>
    /// <param name="excluded">外した選んだ項目の積み先</param>
    /// <remarks>
    /// <para>
    /// 再構築は live の図から表を作り直すので、取込が捨てた物（大文字小文字・前後の空白だけ違う列名の後着の列・範囲の外や取り込まなかった
    /// 表への外部キーなど）は新しい表に無く、データごと黙って消える。外すのは次の 2 つ（どちらもその表を作り直す）。
    /// </para>
    /// <list type="bullet">
    /// <item>その表の列・主キー・外部キー・一意制約・列順の変更（作り直しを含む）。列の追加だけは作り直さずに足せるので残す。</item>
    /// <item>その表の外部キーの端（親の表・親の列）を消す表・列の削除。消すと、その外部キーを外すためにその表を作り直す。</item>
    /// </list>
    /// <para>
    /// 依存の除外の後・暗黙の削除の注入の前に置く（外した削除を前提に、端の消える外部キーの暗黙の削除を足さない）。その表自身の削除は
    /// 作り直さないので外さない。逐次 DDL の方言は列・制約を ALTER で変えるので、捨てた物は残る（掛けない）。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> ExcludeRebuildsLosingImportDiscardedContent(
        List<SchemaDiffItem> selected,
        Dictionary<string, List<string>> discarded,
        SyncPlanContext context,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        var droppedTables = selected
            .Where(i => i.Kind == SchemaDiffKind.DropTable)
            .Select(i => SchemaNames.ToStoredForm(i.TableName))
            .ToHashSet(SchemaNames.Comparer);

        // 守る表を子に持つ live の外部キーの端（親の表・親の列）
        var protectedEnds = EnumerateLiveForeignKeys(context)
            .Where(fk =>
                discarded.ContainsKey(SchemaDiffService.NormalizeTable(fk.Child))
                && !droppedTables.Contains(SchemaDiffService.NormalizeTable(fk.Child))
            )
            .Select(fk =>
                (
                    Child: SchemaDiffService.NormalizeTable(fk.Child),
                    Parent: SchemaDiffService.NormalizeTable(fk.Parent),
                    ParentColumns: ForeignKeyColumnPairResolver
                        .ParentColumns(fk.ColumnPairs)
                        .ToList()
                )
            )
            .ToList();

        var kept = new List<SchemaDiffItem>(selected.Count);

        foreach (var item in selected)
        {
            var table = SchemaNames.ToStoredForm(item.TableName);
            var rebuilt = ProtectedTableRebuiltBy(item, table);

            if (rebuilt is null)
            {
                kept.Add(item);
                continue;
            }

            ExcludeSelected(
                item,
                new SyncPlanWarning(
                    SyncPlanWarningKind.TableRebuildLosesImportDiscardedContent,
                    rebuilt,
                    string.Join(", ", discarded[rebuilt])
                ),
                warnings,
                excluded
            );
        }

        return kept;

        // この項目が作り直させる、取込が捨てた物のある表（無ければ null）
        string? ProtectedTableRebuiltBy(SchemaDiffItem item, string table)
        {
            if (
                discarded.ContainsKey(table)
                && !droppedTables.Contains(table)
                && (
                    IsRebuildTriggerKind(item.Kind)
                    || item.Kind
                        is SchemaDiffKind.AlterForeignKey
                            or SchemaDiffKind.AlterUniqueConstraint
                )
            )
            {
                return discarded.Keys.First(k => SchemaNames.AreSame(k, table));
            }

            return item.Kind switch
            {
                SchemaDiffKind.DropTable => protectedEnds
                    .FirstOrDefault(end => SchemaNames.AreSame(end.Parent, table))
                    .Child,
                SchemaDiffKind.DropColumn when item.ColumnName is not null => protectedEnds
                    .FirstOrDefault(end =>
                        SchemaNames.AreSame(end.Parent, table)
                        && end.ParentColumns.Any(c => SchemaNames.AreSame(c, item.ColumnName))
                    )
                    .Child,
                _ => null,
            };
        }
    }

    /// <summary>
    /// 選んだ項目を計画から外し、名指しの警告（同じ警告は重ねない）と外した項目の一覧（<see cref="SyncPlan.ExcludedItems"/>）へ
    /// 一緒に積む。選んだ項目を外す箇所はすべてここを通す（警告だけ・一覧だけになると、外した項目が名指しされないか、
    /// 名指しされた項目が記録されない）。
    /// </summary>
    private static void ExcludeSelected(
        SchemaDiffItem item,
        SyncPlanWarning warning,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        if (!warnings.Contains(warning))
        {
            warnings.Add(warning);
        }

        excluded.Add(item);
    }

    /// <summary>
    /// 選んだ外部キーの追加（作り直しは展開後の追加）のうち、参照アクション <c>SET DEFAULT</c> を使い、子の列に NOT NULL の
    /// 列があるものを <see cref="SyncPlanWarningKind.SetDefaultOnNotNullColumn"/> で名指しする
    /// （<see cref="SyncDialectCapabilities.SetDefaultRequiresColumnDefault"/> が <c>true</c> の方言のみ）。
    /// </summary>
    /// <remarks>
    /// 子の列の NULL 許容は図（項目の子の表）の値で見る。主キーの変更などに伴う暗黙の付け直しは対象外
    /// （live に既にある外部キーなので、DB の列は DEFAULT 制約を持っている）。
    /// </remarks>
    private static void WarnSetDefaultOnNotNullColumns(
        IReadOnlyList<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        List<SyncPlanWarning> warnings
    )
    {
        if (!capabilities.SetDefaultRequiresColumnDefault)
        {
            return;
        }

        foreach (
            var add in selected.Where(i =>
                i.Kind == SchemaDiffKind.AddForeignKey
                && i.Relationship is not null
                && i.ChildEntity is not null
                && i.ParentEntity is not null
                && ForeignKeyStatementNotes.UsesSetDefault(i.Relationship)
            )
        )
        {
            var child = add.ChildEntity!;
            var notNull = ForeignKeyColumnPairResolver
                .ChildColumns(SyncScriptBuilderHelper.ResolveColumnPairs(add))
                .Where(name =>
                    child.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, name))
                        is { } column
                    && !ColumnNullability.AllowsNull(column)
                )
                .ToList();

            if (notNull.Count == 0)
            {
                continue;
            }

            var childTable = SchemaDiffService.NormalizeTable(child);
            var name = SelectedForeignKeyName(add, capabilities.IdentifierLengthLimit);

            warnings.Add(
                new SyncPlanWarning(
                    SyncPlanWarningKind.SetDefaultOnNotNullColumn,
                    childTable,
                    $"{name} ({string.Join(", ", notNull)})"
                )
            );
        }
    }

    /// <summary>
    /// 選んだ説明の項目のうち、方言が保存できない文字（BMP の外の文字）を書くものを
    /// <see cref="SyncPlanWarningKind.DescriptionCharactersReplaced"/> で表・列ごとに名指しする
    /// （判定は <see cref="SyncDialectCapabilities.IdentifierLengthLimit"/> の宣言＝MySQL だけ置き換わる）。
    /// </summary>
    private static void WarnDescriptionCharactersReplaced(
        IReadOnlyList<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        List<SyncPlanWarning> warnings
    )
    {
        var limit = capabilities.IdentifierLengthLimit;

        foreach (var item in selected)
        {
            var target = item.Kind switch
            {
                SchemaDiffKind.SetTableDescription => item.TableName,
                SchemaDiffKind.SetColumnDescription when item.ColumnName is not null =>
                    $"{item.TableName}.{item.ColumnName}",
                _ => null,
            };

            if (
                target is null
                || !DescriptionText.IsPresent(item.NewDescription)
                || limit.CanRepresent(item.NewDescription)
            )
            {
                continue;
            }

            warnings.Add(
                new SyncPlanWarning(
                    SyncPlanWarningKind.DescriptionCharactersReplaced,
                    item.TableName,
                    target
                )
            );
        }
    }

    /// <summary>
    /// 選んだ項目が触れる表（項目の表と、外部キーの親子の表）のうち、ドットを含む名前のものを
    /// <see cref="SyncPlanWarningKind.SchemaQualifiedTableName"/> で表ごとに 1 件名指しする
    /// （<see cref="SyncDialectCapabilities.SupportsSchemaQualifiedTableNames"/> が <c>false</c> の方言のみ）。
    /// </summary>
    private static void WarnSchemaQualifiedTableNames(
        IReadOnlyList<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        List<SyncPlanWarning> warnings
    )
    {
        if (capabilities.SupportsSchemaQualifiedTableNames)
        {
            return;
        }

        var seen = new HashSet<string>(SchemaNames.Comparer);

        foreach (var item in selected)
        {
            foreach (
                var name in new[]
                {
                    item.TableName,
                    item.ChildEntity?.TableName,
                    item.ParentEntity?.TableName,
                }
            )
            {
                if (name is null || !SchemaQualifiedName.IsQualified(name))
                {
                    continue;
                }

                var table = SchemaNames.ToStoredForm(name);

                if (seen.Add(table))
                {
                    warnings.Add(
                        new SyncPlanWarning(
                            SyncPlanWarningKind.SchemaQualifiedTableName,
                            table,
                            string.Empty
                        )
                    );
                }
            }
        }
    }

    /// <summary>
    /// 選んだ列の変更のうち、取込で既定値の分かった列の型の種類（方言の正規化を通した、括弧より前の基本の名前）を
    /// 変えるものを <see cref="SyncPlanWarningKind.DefaultOnColumnTypeKindChange"/> で名指しする
    /// （<see cref="SyncDialectCapabilities.ChecksDefaultOnColumnTypeChange"/> が <c>true</c> の方言のみ）。
    /// </summary>
    private static void WarnDefaultsOnColumnTypeKindChange(
        IReadOnlyList<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings
    )
    {
        if (
            !capabilities.ChecksDefaultOnColumnTypeChange
            || context is null
            || context.ColumnDefaults.Count == 0
        )
        {
            return;
        }

        foreach (
            var alter in selected.Where(i =>
                i.Kind == SchemaDiffKind.AlterColumn
                && i.ColumnName is not null
                && i.Column is not null
                && i.OldColumn is not null
            )
        )
        {
            var oldType = alter.OldColumn!.DataType;
            var newType = alter.Column!.DataType;

            if (
                !capabilities.ChangesTypeKind(oldType, newType)
                || !context.ColumnDefaults.Any(d =>
                    SchemaNames.AreSame(d.TableName, alter.TableName)
                    && SchemaNames.AreSame(d.ColumnName, alter.ColumnName!)
                )
            )
            {
                continue;
            }

            warnings.Add(
                new SyncPlanWarning(
                    SyncPlanWarningKind.DefaultOnColumnTypeKindChange,
                    SchemaNames.ToStoredForm(alter.TableName),
                    $"{alter.ColumnName} ({oldType} → {newType})"
                )
            );
        }
    }

    /// <summary>追加を選んだ表（表名 → 図のエンティティ）</summary>
    private static Dictionary<string, Entity> CollectAddedTables(
        IReadOnlyList<SchemaDiffItem> selected
    ) =>
        selected
            .Where(i => i.Kind == SchemaDiffKind.AddTable && i.Entity is not null)
            .GroupBy(i => SchemaNames.ToStoredForm(i.TableName), SchemaNames.Comparer)
            .ToDictionary(g => g.Key, g => g.First().Entity!, SchemaNames.Comparer);

    /// <summary>
    /// 同期後の表の主キー（実効順）。主キーの変更を選んだ表はその新しい主キー、追加を選んだ表は図の主キー、
    /// それ以外は live の主キー。どこにも無い表は <c>null</c>。
    /// </summary>
    private static IReadOnlyList<string>? PostSyncPrimaryKeyOf(
        string table,
        Dictionary<string, List<string>> pkChangedTables,
        Dictionary<string, Entity> addedTables,
        NameLookup<Entity> liveByName
    )
    {
        if (pkChangedTables.TryGetValue(table, out var changedKey))
        {
            return changedKey;
        }

        if (addedTables.TryGetValue(table, out var addedTable))
        {
            return [.. addedTable.GetPrimaryKeyColumnsInOrder().Select(c => c.Name)];
        }

        return liveByName.TryGet(table, out var liveTable)
            ? [.. liveTable.GetPrimaryKeyColumnsInOrder().Select(c => c.Name)]
            : null;
    }

    /// <summary>
    /// 外部キーの追加（明示の追加・作り直しを展開した追加・暗黙の付け直し）の列ペアを、参照先の同期後のキーの並びへ
    /// 並べ替える（参照する列の並びがキーの並びと一致しないと外部キーを作れない方言＝SQL Server・MySQL）。
    /// </summary>
    /// <param name="items">セクションへ入れる項目（暗黙の付け直しを注入した後）</param>
    /// <param name="selected">選択済みの項目（同期後の主キー・一意制約の合成に使う）</param>
    /// <param name="context">live スキーマ（無ければ項目の参照先の表のキーへそろえる）</param>
    /// <returns>外部キーの追加の列ペアを並べ替えた項目（それ以外の項目はそのまま）</returns>
    /// <remarks>
    /// <para>
    /// 差分は列ペアを並びを見ずに比べるので（列ペアは対応の集合）、図の並びが参照先のキーと違っても作り直しにはならず、
    /// 参照される複合主キーの並びだけを入れ替える変更では、旧主キーに支えられた外部キーを外して付け直す。どちらの付け直しも、
    /// 図や live の並びのまま出すとこれらの方言では必ず失敗する（SQL Server の Msg 1776。MySQL は DDL が暗黙コミットされる
    /// ので外部キーが消えたまま残る）。
    /// </para>
    /// <para>
    /// 参照先の同期後のキーは、主キー（主キーの変更を選んだ表はその新しい主キー、追加を選んだ表は図の主キー、それ以外は
    /// live の主キー）、続いて同期後の一意制約（<see cref="BuildPostSyncUniqueKeys"/>）。参照先の列の集合がどのキーとも
    /// ちょうど一致しなければ並べ替えない（キーが決まらない。証明できない付け直しは
    /// <see cref="SyncPlanWarningKind.ForeignKeyNotOnCandidateKey"/> などで別に警告する）。規則の本体は DDL と同じ
    /// <see cref="ForeignKeyColumnOrder.ArrangeToKey"/>。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> ArrangeForeignKeysToPostSyncKeys(
        List<SchemaDiffItem> items,
        IReadOnlyList<SchemaDiffItem> selected,
        SyncPlanContext? context
    )
    {
        var pkChangedTables = CollectPrimaryKeyChanges(selected);
        var addedTables = CollectAddedTables(selected);
        var liveByName = context is null ? null : IndexLiveTables(context);
        var postSyncUniques = BuildPostSyncUniqueKeys(selected, context);

        return [.. items.Select(Arrange)];

        SchemaDiffItem Arrange(SchemaDiffItem item)
        {
            if (
                item.Kind != SchemaDiffKind.AddForeignKey
                || item.ParentEntity is null
                || item.ForeignKeyColumnPairs.Count < 2
            )
            {
                return item;
            }

            var parentTable = SchemaDiffService.NormalizeTable(item.ParentEntity);
            var postSyncKey = liveByName is null
                ? null
                : PostSyncPrimaryKeyOf(parentTable, pkChangedTables, addedTables, liveByName);
            var arranged = postSyncKey is null
                ? ForeignKeyColumnOrder.ArrangeToReferencedKey(
                    item.ForeignKeyColumnPairs,
                    item.ParentEntity
                )
                : ForeignKeyColumnOrder.ArrangeToKey(
                    item.ForeignKeyColumnPairs,
                    PostSyncKeys(parentTable, postSyncKey)
                );

            return arranged.SequenceEqual(item.ForeignKeyColumnPairs)
                ? item
                : item.WithForeignKeyColumnPairs(arranged);
        }

        IEnumerable<IReadOnlyList<string>> PostSyncKeys(
            string parentTable,
            IReadOnlyList<string> postSyncKey
        )
        {
            if (postSyncKey.Count > 0)
            {
                yield return postSyncKey;
            }

            if (postSyncUniques.TryGetValue(parentTable, out var uniques))
            {
                foreach (var unique in uniques)
                {
                    yield return unique;
                }
            }
        }
    }

    /// <summary>
    /// 同期後も残る DB の外部キー・一意制約（選んだ削除・作り直しで外れず、表・構成列も選んだ削除で消えないもの）。足す制約の名前・定義が
    /// 重なるかを、方言の宣言（<see cref="SyncDialectCapabilities.ConstraintNameScope"/>・
    /// <see cref="SyncDialectCapabilities.AllowsDuplicateForeignKeyDefinitions"/>）で判定する
    /// </summary>
    /// <remarks>
    /// 主キーの名前は取り込まない（同期は実行時に逆引きする）ので数えない。端が消える外部キー・構成列が消える一意制約は、同期が追加より前の
    /// 段で暗黙に外すので残る側に数えない（<see cref="InjectVanishingForeignKeyDrops"/>・<see cref="InjectVanishingUniqueConstraintDrops"/>）。
    /// 名前は図全体から求めた実効名で、比べ方は <see cref="SchemaNames"/> の規則。
    /// </remarks>
    private sealed class RemainingLiveConstraints
    {
        private readonly List<(
            string Child,
            string Parent,
            string Name,
            string PairSet
        )> _foreignKeys = [];

        private readonly List<(string Table, string Name)> _uniques = [];

        /// <summary>選んだ項目と DB の図から集める</summary>
        public static RemainingLiveConstraints From(
            IReadOnlyList<SchemaDiffItem> selected,
            HashSet<string> droppedUniques,
            IdentifierLengthLimit limit,
            SyncPlanContext context
        )
        {
            var remaining = new RemainingLiveConstraints();
            var droppedForeignKeys = CollectExplicitlyDroppedForeignKeys(
                ExpandAlters(selected),
                limit
            );
            var droppedObjects = DroppedObjects.From(selected);
            var liveNames = context.LiveForeignKeyNames(limit);

            foreach (
                var fk in ForeignKeyColumnPairResolver.ResolveAll(
                    context.LiveRelationships,
                    context.LiveEntities
                )
            )
            {
                var child = SchemaDiffService.NormalizeTable(fk.Child);
                var parent = SchemaDiffService.NormalizeTable(fk.Parent);

                if (
                    droppedObjects.Touches(fk, child, parent)
                    || droppedForeignKeys.Contains(
                        ForeignKeyKey(child, parent, fk.ColumnPairs, fk.Relationship, limit)
                    )
                    || !liveNames.TryGetValue(fk.Relationship, out var name)
                )
                {
                    continue;
                }

                remaining._foreignKeys.Add((child, parent, name, PairSet(fk.ColumnPairs)));
            }

            foreach (var entity in context.LiveEntities)
            {
                var table = SchemaDiffService.NormalizeTable(entity);

                if (droppedObjects.Tables.Contains(table))
                {
                    continue;
                }

                foreach (var constraint in entity.UniqueConstraints)
                {
                    if (
                        UniqueConstraintNaming.TryResolveColumnNames(
                            entity,
                            constraint,
                            out var columns
                        )
                        && !droppedObjects.TouchesColumns(table, columns)
                        && !droppedUniques.Contains(
                            UniqueConstraintKey(table, constraint.Name, columns)
                        )
                    )
                    {
                        remaining._uniques.Add(
                            (
                                table,
                                UniqueConstraintNaming.Resolve(
                                    constraint.Name,
                                    table,
                                    columns,
                                    limit
                                )
                            )
                        );
                    }
                }
            }

            return remaining;
        }

        /// <summary>足す制約と、方言の名前の範囲で名前が重なる残る制約（「表.名前」）</summary>
        public IEnumerable<string> NamedLike(
            string name,
            string table,
            bool isForeignKey,
            SyncDialectCapabilities capabilities
        ) =>
            _foreignKeys
                .Where(f =>
                    SchemaNames.AreSame(f.Name, name)
                    && capabilities.ConstraintNamesCollide(isForeignKey, table, true, f.Child)
                )
                .Select(f => $"{f.Child}.{f.Name}")
                .Concat(
                    _uniques
                        .Where(u =>
                            SchemaNames.AreSame(u.Name, name)
                            && capabilities.ConstraintNamesCollide(
                                isForeignKey,
                                table,
                                false,
                                u.Table
                            )
                        )
                        .Select(u => $"{u.Table}.{u.Name}")
                );

        /// <summary>
        /// 同じ定義の外部キーを 2 本持てない方言で、足す外部キーと定義（子の表・親の表・列の組の集合）が同じ残る外部キー（「表.名前」）
        /// </summary>
        public IEnumerable<string> SameDefinitionAs(
            string child,
            string parent,
            IReadOnlyList<ForeignKeyColumnNamePair> pairs,
            SyncDialectCapabilities capabilities
        )
        {
            if (capabilities.AllowsDuplicateForeignKeyDefinitions)
            {
                return [];
            }

            var pairSet = PairSet(pairs);
            return _foreignKeys
                .Where(f =>
                    SchemaNames.AreSame(f.Child, child)
                    && SchemaNames.AreSame(f.Parent, parent)
                    && f.PairSet == pairSet
                )
                .Select(f => $"{f.Child}.{f.Name}");
        }

        /// <summary>列の組の集合のキー（並びを見ない。名前は <see cref="SchemaNames.Key"/> で畳む）</summary>
        private static string PairSet(IEnumerable<ForeignKeyColumnNamePair> pairs) =>
            string.Concat(
                pairs
                    .Select(p =>
                        SignatureText.Part(SchemaNames.Key(p.ParentColumn))
                        + SignatureText.Part(SchemaNames.Key(p.ChildColumn))
                    )
                    .Order(StringComparer.Ordinal)
            );
    }

    /// <summary>明示的に削除が選ばれた外部キー（選択済み DropForeignKey）の照合キーを集める</summary>
    private static HashSet<string> CollectExplicitlyDroppedForeignKeys(
        IReadOnlyList<SchemaDiffItem> selected,
        IdentifierLengthLimit limit
    )
    {
        var explicitlyDropped = new HashSet<string>();

        foreach (var dropFk in selected.Where(i => i.Kind == SchemaDiffKind.DropForeignKey))
        {
            var signature = ResolveDroppedForeignKeySignature(dropFk);

            if (signature is null || dropFk.ChildEntity is null)
            {
                continue;
            }

            var (parentTable, columnPairs) = signature.Value;
            explicitlyDropped.Add(
                ForeignKeyKey(
                    SchemaDiffService.NormalizeTable(dropFk.ChildEntity),
                    parentTable,
                    columnPairs,
                    dropFk.Relationship,
                    limit
                )
            );
        }

        return explicitlyDropped;
    }

    // ---------------- 一意制約（UNIQUE）の合成・注入・警告 ----------------

    /// <summary>
    /// 「同期後に存在する一意制約」をテーブルごとに合成する
    /// （live の一意制約 − 選択済み <see cref="SchemaDiffKind.DropUniqueConstraint"/> ＋
    /// 選択済み <see cref="SchemaDiffKind.AddUniqueConstraint"/>）。
    /// </summary>
    /// <returns>テーブル名 → 列集合シグネチャ（<see cref="UniqueConstraintNaming.ColumnSetSignature"/>）の集合</returns>
    /// <remarks>
    /// 図（target）の定義を直接見ないのは、未選択の追加をあてにしてしまうため。合成は選択済み差分だけで行い、
    /// 「実行後に確実に存在する」ものだけを候補キーの証明材料にする。
    /// </remarks>
    private static Dictionary<string, HashSet<string>> BuildPostSyncUniqueConstraints(
        IReadOnlyList<SchemaDiffItem> selected,
        SyncPlanContext? context
    ) =>
        BuildPostSyncUniqueKeys(selected, context)
            .ToDictionary(
                pair => pair.Key,
                pair =>
                    pair.Value.Select(UniqueConstraintNaming.ColumnSetSignature)
                        .ToHashSet(StringComparer.Ordinal),
                SchemaNames.Comparer
            );

    /// <summary>
    /// 「同期後に存在する一意制約」の構成列を、列の並びのままテーブルごとに合成する
    /// （<see cref="BuildPostSyncUniqueConstraints"/> と同じ合成。外部キーの列ペアを一意制約の並びへそろえるのに使う）。
    /// </summary>
    /// <returns>テーブル名 → 一意制約の構成列（live の宣言順、続いて追加を選んだ表の一意制約、選択済みの追加の順）</returns>
    /// <remarks>
    /// 削除は DB の一意制約の照合キー（<see cref="UniqueConstraintKey"/>＝名前と列集合）で照合し、外す制約だけを落とす
    /// （同じ列集合の一意制約が 2 つある表で、1 つを外しても、もう 1 つは同期後のキーとして残る）。
    /// </remarks>
    private static Dictionary<string, List<IReadOnlyList<string>>> BuildPostSyncUniqueKeys(
        IReadOnlyList<SchemaDiffItem> selected,
        SyncPlanContext? context
    )
    {
        // 構成列と、DB の一意制約なら照合キー（外す項目と照合する。図から来た制約は null）
        var byTable = new Dictionary<
            string,
            List<(IReadOnlyList<string> Columns, string? LiveKey)>
        >(SchemaNames.Comparer);

        if (context is null)
        {
            return [];
        }

        // live の一意制約を土台にする
        foreach (var entity in context.LiveEntities)
        {
            var table = SchemaDiffService.NormalizeTable(entity);

            foreach (var constraint in entity.UniqueConstraints)
            {
                if (
                    !UniqueConstraintNaming.TryResolveColumnNames(
                        entity,
                        constraint,
                        out var columns
                    )
                )
                {
                    continue;
                }

                Keys(table).Add((columns, UniqueConstraintKey(table, constraint.Name, columns)));
            }
        }

        // 追加を選んだ表の一意制約を足す（表の作成と一緒に作られる）
        foreach (
            var item in selected.Where(i =>
                i.Kind == SchemaDiffKind.AddTable && i.Entity is not null
            )
        )
        {
            foreach (var constraint in item.Entity!.UniqueConstraints)
            {
                if (
                    UniqueConstraintNaming.TryResolveColumnNames(
                        item.Entity,
                        constraint,
                        out var columns
                    )
                )
                {
                    Keys(SchemaNames.ToStoredForm(item.TableName)).Add((columns, null));
                }
            }
        }

        // 選択済みの削除を落とす（外す DB の一意制約だけ）
        var dropped = ExplicitlyDroppedUniqueConstraints(selected);

        foreach (var keys in byTable.Values)
        {
            keys.RemoveAll(key => key.LiveKey is not null && dropped.Contains(key.LiveKey));
        }

        // 選択済みの追加を足す
        foreach (var item in selected.Where(i => i.Kind == SchemaDiffKind.AddUniqueConstraint))
        {
            if (item.UniqueConstraintColumns.Count == 0)
            {
                continue;
            }

            Keys(SchemaNames.ToStoredForm(item.TableName))
                .Add((item.UniqueConstraintColumns, null));
        }

        return byTable.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(key => key.Columns).ToList(),
            SchemaNames.Comparer
        );

        List<(IReadOnlyList<string> Columns, string? LiveKey)> Keys(string table)
        {
            if (!byTable.TryGetValue(table, out var keys))
            {
                keys = [];
                byTable[table] = keys;
            }

            return keys;
        }
    }

    /// <summary>2 つの列名集合が（順序・大文字小文字を無視して）ちょうど一致するか</summary>
    private static bool IsSameColumnSet(IEnumerable<string> left, IEnumerable<string> right) =>
        string.Equals(
            UniqueConstraintNaming.ColumnSetSignature(left),
            UniqueConstraintNaming.ColumnSetSignature(right),
            StringComparison.Ordinal
        );

    /// <summary>エンティティの列の型（列名は <see cref="SchemaNames"/> の規則で引く。列が無ければ空文字列）</summary>
    /// <remarks>
    /// live の列の型（DB が今受け入れている型）・図の列の型を読む箇所が通す。この同期で型が決まる列の新しい型は
    /// <see cref="CollectTypeDecidingColumns"/> が決め、ここでは読まない。
    /// </remarks>
    private static string ColumnTypeOf(Entity entity, string column) =>
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
    /// 判定はここの 1 か所で、前寄せ（<see cref="MoveCertainFailureFirst"/>）・一意制約の削除の警告
    /// （<see cref="WarnUniqueConstraintDropsBreakingForeignKeys"/>）・主キーの変更の外し直し（<see cref="DependsOnOldPrimaryKey"/> と、
    /// 新しい主キーへ付け直す一意制約の枝）が通す。写しが割れると、前寄せした削除を警告が名指しせず、警告を飛ばした外部キーを
    /// 外し直しも付け直さない。付け直せるか（候補キーの証明＝<see cref="StaysCandidateKey"/>）は
    /// 別の判定で、列の集合の完全一致のまま（非標準キーへの付け直しは証明しない）。
    /// </para>
    /// </remarks>
    private static bool CanSupportForeignKey(
        SyncDialectCapabilities capabilities,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<string> referencedColumns
    ) =>
        IsSameColumnSet(keyColumns, referencedColumns)
        || capabilities.ForeignKeyRequiresLeadingIndex
            && keyColumns.Count > referencedColumns.Count
            && IsSameColumnSet(keyColumns.Take(referencedColumns.Count), referencedColumns);

    /// <summary>
    /// 選択された列定義変更に巻き込まれる live の一意制約を、暗黙の DROP（先頭側）と再 ADD（末尾側）として注入する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 対象は <see cref="SyncDialectCapabilities.AlterColumnRequiresKeyConstraintRebuild"/> が <c>true</c> の方言
    /// （SQL Server）のみ。一意制約が張られた列は制約を外さずに <c>ALTER COLUMN</c> できない（Msg 5074）ため、
    /// FK の暗黙再構築（<see cref="InjectImplicitForeignKeyRebuilds"/>）と同じ形で外して戻す。
    /// </para>
    /// <para>
    /// 利用者が同じ一意制約の <see cref="SchemaDiffKind.DropUniqueConstraint"/> を明示選択している場合は、
    /// DB の一意制約の照合キー（<see cref="UniqueConstraintKey"/>＝表・名前・列集合）で重複排除して自動 DROP を注入せず、再 ADD もしない
    /// （「消す」意図を尊重する。同じ列集合の一意制約がもう 1 つあれば、それは別の制約として外して戻す）。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> InjectImplicitUniqueConstraintRebuilds(
        List<SchemaDiffItem> sectionItems,
        IReadOnlyList<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context
    )
    {
        if (context is null || !capabilities.AlterColumnRequiresKeyConstraintRebuild)
        {
            return sectionItems;
        }

        var alteredColumns = selected
            .Where(i => i.Kind == SchemaDiffKind.AlterColumn && i.ColumnName is not null)
            .Select(i => ColumnKey(i.TableName, i.ColumnName!))
            .ToHashSet();

        if (alteredColumns.Count == 0)
        {
            return sectionItems;
        }

        // 明示的に DROP が選択されている一意制約（自動 DROP の重複も再 ADD も行わない）
        var explicitlyDropped = ExplicitlyDroppedUniqueConstraints(selected);
        var dropped = DroppedObjects.From(selected);

        var autoDrops = new List<SchemaDiffItem>();
        var autoAdds = new List<SchemaDiffItem>();

        foreach (var entity in context.LiveEntities)
        {
            var table = SchemaDiffService.NormalizeTable(entity);

            foreach (var constraint in entity.UniqueConstraints)
            {
                if (
                    !UniqueConstraintNaming.TryResolveColumnNames(
                        entity,
                        constraint,
                        out var columns
                    )
                )
                {
                    continue;
                }

                // 構成列のいずれかが定義変更の対象なら、この制約は外さないと変更できない
                if (!columns.Any(c => alteredColumns.Contains(ColumnKey(table, c))))
                {
                    continue;
                }

                if (
                    explicitlyDropped.Contains(UniqueConstraintKey(table, constraint.Name, columns))
                )
                {
                    continue;
                }

                var description = string.Format(
                    Strings.Diff_AutoUniqueConstraintRebuild,
                    string.Join(", ", columns)
                );

                autoDrops.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.DropUniqueConstraint,
                        TableName = table,
                        Entity = entity,
                        UniqueConstraintName = constraint.Name,
                        UniqueConstraintColumns = columns,
                        Description = description,
                    }
                );

                // 表・構成列が同じ計画で消える一意制約は外すだけで付け直さない（外部キーの外し直しと対称。
                // 付け直すと消えた表・列を参照して失敗する）
                if (dropped.Tables.Contains(table) || dropped.TouchesColumns(table, columns))
                {
                    continue;
                }

                autoAdds.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.AddUniqueConstraint,
                        TableName = table,
                        Entity = entity,
                        UniqueConstraintName = constraint.Name,
                        UniqueConstraintColumns = columns,
                        Description = description,
                    }
                );
            }
        }

        if (autoDrops.Count == 0)
        {
            return sectionItems;
        }

        // 自動 DROP は DropUniqueConstraint セクションの先頭側・再 ADD は AddUniqueConstraint セクションの末尾側へ置く
        return [.. autoDrops, .. sectionItems, .. autoAdds];
    }

    /// <summary>
    /// 選択された一意制約の削除が既存 FK の被参照列を候補キーでなくしうる場合に警告を積む。
    /// </summary>
    /// <remarks>
    /// 削除する一意制約がその外部キーを支えうるか（<see cref="CanSupportForeignKey"/>＝前寄せと同じ判定。MySQL は参照する列を先頭に
    /// 並べる一意制約も）で判定する。ほかのキーが同じ列を覆っていれば実際には壊れないが、誤警告を許容してほかのキーは数えず、
    /// 実行はブロックしない（レンダラーは DROP を出す）。
    /// </remarks>
    private static void WarnUniqueConstraintDropsBreakingForeignKeys(
        IReadOnlyList<SchemaDiffItem> selected,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings,
        SyncDialectCapabilities capabilities
    )
    {
        var limit = capabilities.IdentifierLengthLimit;

        if (context is null)
        {
            return;
        }

        var drops = selected.Where(i => i.Kind == SchemaDiffKind.DropUniqueConstraint).ToList();

        if (drops.Count == 0)
        {
            return;
        }

        // 同期で外す外部キー（明示的に外す・端が消える＝計画の入口で外す項目を足している）は壊れようがないので警告しない
        var explicitlyDropped = CollectExplicitlyDroppedForeignKeys(selected, limit);
        var dropped = DroppedObjects.From(selected);
        var pkChangedTables = CollectPrimaryKeyChanges(selected);

        foreach (var fk in EnumerateLiveForeignKeys(context))
        {
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);

            // 新しい主キーが参照する列とちょうど同じなら、候補キーは主キーが保つ（主キーの変更の外し直しが新しい主キーへ付け直す）
            if (
                dropped.Touches(fk, childTable, parentTable)
                || explicitlyDropped.Contains(
                    ForeignKeyKey(childTable, parentTable, fk.ColumnPairs, fk.Relationship, limit)
                )
                || (
                    pkChangedTables.TryGetValue(parentTable, out var newKey)
                    && IsSameColumnSet(
                        newKey,
                        ForeignKeyColumnPairResolver.ParentColumns(fk.ColumnPairs)
                    )
                )
            )
            {
                continue;
            }

            var referenced = ForeignKeyColumnPairResolver.ParentColumns(fk.ColumnPairs).ToList();

            foreach (var drop in drops)
            {
                if (!SchemaNames.AreSame(SchemaNames.ToStoredForm(drop.TableName), parentTable))
                {
                    continue;
                }

                if (!CanSupportForeignKey(capabilities, drop.UniqueConstraintColumns, referenced))
                {
                    continue;
                }

                warnings.Add(
                    new SyncPlanWarning(
                        SyncPlanWarningKind.UniqueConstraintDropMayBreakForeignKey,
                        parentTable,
                        ResolveForeignKeyName(
                            fk.Relationship,
                            SchemaDiffService.NormalizeTable(fk.Child),
                            parentTable,
                            limit,
                            context
                        )
                    )
                );
            }
        }
    }

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
    private static string UniqueConstraintKey(
        string table,
        string? name,
        IEnumerable<string> columnNames
    ) =>
        SignatureText.Part(SchemaNames.Key(table))
        + SignatureText.Part(name is null ? string.Empty : SchemaNames.Key(name))
        + UniqueConstraintNaming.ColumnSetSignature(columnNames);

    /// <summary>外す項目（DropUniqueConstraint）が外す DB の一意制約の照合キー（<see cref="UniqueConstraintKey"/>）</summary>
    private static HashSet<string> ExplicitlyDroppedUniqueConstraints(
        IEnumerable<SchemaDiffItem> selected
    ) =>
        selected
            .Where(i => i.Kind == SchemaDiffKind.DropUniqueConstraint)
            .Select(i =>
                UniqueConstraintKey(i.TableName, i.UniqueConstraintName, i.UniqueConstraintColumns)
            )
            .ToHashSet();

    /// <summary>選んだ表・列の削除（制約の端が同じ計画で消えるかの判定に使う）</summary>
    private sealed class DroppedObjects
    {
        /// <summary>削除する表</summary>
        public HashSet<string> Tables { get; } = new(SchemaNames.Comparer);

        /// <summary>削除する列（<see cref="ColumnKey"/> の照合キー）</summary>
        public HashSet<string> Columns { get; } = [];

        /// <summary>表・列の削除が 1 つも無いか</summary>
        public bool IsEmpty => Tables.Count == 0 && Columns.Count == 0;

        /// <summary>選んだ項目から削除する表・列を集める</summary>
        public static DroppedObjects From(IEnumerable<SchemaDiffItem> selected)
        {
            var dropped = new DroppedObjects();

            foreach (var item in selected)
            {
                if (item.Kind == SchemaDiffKind.DropTable)
                {
                    dropped.Tables.Add(SchemaNames.ToStoredForm(item.TableName));
                }
                else if (item.Kind == SchemaDiffKind.DropColumn && item.ColumnName is not null)
                {
                    dropped.Columns.Add(ColumnKey(item.TableName, item.ColumnName));
                }
            }

            return dropped;
        }

        /// <summary>外部キーの子表・親表・構成列のどれかが消えるか</summary>
        public bool Touches(
            ResolvedForeignKeyRelationship fk,
            string childTable,
            string parentTable
        ) =>
            Tables.Contains(childTable)
            || Tables.Contains(parentTable)
            || fk.ColumnPairs.Any(p =>
                Columns.Contains(ColumnKey(childTable, p.ChildColumn))
                || Columns.Contains(ColumnKey(parentTable, p.ParentColumn))
            );

        /// <summary>表の列のどれかが消えるか</summary>
        public bool TouchesColumns(string table, IEnumerable<string> columns) =>
            columns.Any(c => Columns.Contains(ColumnKey(table, c)));
    }

    /// <summary>テーブル・列の照合キー（名前は <see cref="SchemaNames.Key"/> で畳む）</summary>
    private static string ColumnKey(string table, string column) =>
        SignatureText.Part(SchemaNames.Key(table)) + SignatureText.Part(SchemaNames.Key(column));

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
    /// 「外した」ことにして暗黙の外し直しを止めないため（止まると、残った 1 本が主キーの解除・列の変更を阻んで失敗した）。
    /// 名前を保存しない SQLite でも参照アクションが違えば区別できる（名前も参照アクションも同じ 2 本は区別できないが、
    /// その 2 本は DB の上で同じ外部キーなので扱いが変わらない）。
    /// </remarks>
    private static string ForeignKeyKey(
        string childTable,
        string parentTable,
        IEnumerable<ForeignKeyColumnNamePair> columnPairs,
        Relationship? relationship,
        IdentifierLengthLimit limit
    ) =>
        SignatureText.Part(SchemaNames.Key(childTable))
        + SignatureText.Part(SchemaNames.Key(parentTable))
        + SignatureText.Part(
            string.Concat(
                columnPairs.Select(p =>
                    SignatureText.Part(SchemaNames.Key(p.ParentColumn))
                    + SignatureText.Part(SchemaNames.Key(p.ChildColumn))
                )
            )
        )
        + SignatureText.Part(
            SchemaNames.Key(ResolveForeignKeyName(relationship, childTable, parentTable, limit))
        )
        + $"{relationship?.OnDelete ?? ForeignKeyReferentialAction.NoAction}"
        + $"|{relationship?.OnUpdate ?? ForeignKeyReferentialAction.NoAction}";

    /// <summary>
    /// 選んだ外部キーの追加・作り直しの項目を警告で名指しする名前（スクリプトが付ける名前＝
    /// <see cref="SyncScriptBuilderHelper.ForeignKeyNameToAdd"/>）
    /// </summary>
    /// <remarks>
    /// 差分は名前の無い外部キーの名前を図全体から求めて項目に載せる（同じ組の 2 本目は子の列名が付く）。リレーションの名前から
    /// 規約名を作り直すと 2 本目は 1 本目と同じ名前になり、DB にある無関係な外部キーを名指しした。
    /// </remarks>
    private static string SelectedForeignKeyName(
        SchemaDiffItem item,
        IdentifierLengthLimit limit
    ) => SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, limit);

    /// <summary>
    /// 外す外部キーの項目を、付け直す項目と組むときの名前（DB の外部キーの実効名＝図全体から求めた
    /// <see cref="SyncPlanContext.LiveForeignKeyNames"/>。名前のある DB の外部キーは項目が運ぶ DB の名前）
    /// </summary>
    /// <remarks>
    /// 外す項目は DB の名前を運び、名前の無い DB の外部キーでは空になる。足す項目の名前の正本（<see cref="SelectedForeignKeyName"/>）で求めると、
    /// 関係だけから規約名を作り直すので、名前の無い同じ組の 2 本目が 1 本目の名前になり、付け直す項目（2 本目の実効名）と組めない。
    /// </remarks>
    private static string DroppedForeignKeyName(
        SchemaDiffItem item,
        IdentifierLengthLimit limit,
        SyncPlanContext? context
    ) =>
        item.ForeignKeyName is null
        && item.Relationship is not null
        && context is not null
        && context.LiveForeignKeyNames(limit).TryGetValue(item.Relationship, out var resolved)
            ? resolved
            : SelectedForeignKeyName(item, limit);

    /// <summary>FK 制約名を解決する（未設定なら <c>FK_{子}_{親}</c> の規約名）</summary>
    /// <param name="relationship">外部キーのリレーション</param>
    /// <param name="childTable">子表</param>
    /// <param name="parentTable">親表</param>
    /// <param name="limit">既定名を縮める同期の方言の上限（<see cref="SyncDialectCapabilities.IdentifierLengthLimit"/>）</param>
    /// <param name="context">
    /// live の文脈。live のリレーションなら、図全体から求めた実効名（<see cref="SyncPlanContext.LiveForeignKeyNames"/>）を使う
    /// </param>
    private static string ResolveForeignKeyName(
        Relationship? relationship,
        string childTable,
        string parentTable,
        IdentifierLengthLimit limit,
        SyncPlanContext? context = null
    ) =>
        relationship is not null
        && context is not null
        && context.LiveForeignKeyNames(limit).TryGetValue(relationship, out var resolved)
            ? resolved
            : ConstraintNames.EffectiveForeignKey(
                relationship?.ConstraintName,
                childTable,
                parentTable,
                limit
            );

    /// <summary>live のテーブルを名前で引く索引（規則は差分と同じ <see cref="NameLookup{T}"/>）</summary>
    /// <remarks>
    /// 序数一致の 1 件を優先し、無ければ大文字小文字を無視した一致が 1 件のときだけ引ける。
    /// 2 件以上なら引けない＝並び順で片方を選ばない。引けないテーブルの項目は、再構築・列順変更の対象にせず
    /// 既存の「live に無いテーブル」と同じ扱い（再構築はセクションへ残してレンダラーがスキップを明示・
    /// 列順変更は計画に含めない）になる。
    /// </remarks>
    private static NameLookup<Entity> IndexLiveTables(SyncPlanContext context) =>
        new(context.LiveEntities, SchemaDiffService.NormalizeTable);

    /// <summary>live のリレーションから、親子エンティティ・構成列ペアまで解決できた FK を列挙する</summary>
    /// <remarks>
    /// 多対多や、親子・構成列が解決できないリレーションは FK として扱えないため除外する
    /// （規則は差分計算と共有の <see cref="ForeignKeyColumnPairResolver.ResolveAll"/>）。
    /// 複合外部キーは列ペアを宣言順に保持したまま列挙する。
    /// </remarks>
    private static List<ResolvedForeignKeyRelationship> EnumerateLiveForeignKeys(
        SyncPlanContext context
    ) => ForeignKeyColumnPairResolver.ResolveAll(context.LiveRelationships, context.LiveEntities);

    /// <summary>
    /// rebuild 方言の実行計画を組み立てる。逐次 DDL で表現できない変更をテーブル単位の再構築へ集約し、
    /// 残り（新規テーブル対象でない列追加・テーブル削除）はセクションへ残す。
    /// </summary>
    private static SyncPlan BuildRebuildPlan(
        List<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext context,
        List<SyncPlanWarning> warnings,
        IReadOnlyList<SchemaDiffItem> excluded,
        IdentifierLengthLimit limit
    )
    {
        // 選択済み AddTable のテーブル名（新規テーブル＝CreateOnly の対象）
        var addedTables = selected
            .Where(i => i.Kind == SchemaDiffKind.AddTable)
            .Select(i => SchemaNames.ToStoredForm(i.TableName))
            .ToHashSet(SchemaNames.Comparer);

        // live のテーブル索引（合成の土台）
        var liveByName = IndexLiveTables(context);

        // 既存テーブルの再構築対象（新規テーブルを子とする AddForeignKey は CreateOnly へ畳むため除外）。
        // live に実在するテーブルに限る——「新規テーブルの AddTable は未選択のまま、そのテーブルへの
        // AddForeignKey だけ選択」のような組み合わせでは合成の土台が無く、該当項目はセクションへ残して
        // レンダラーのスキップコメントに委ねる（例外にすると UI のチェック操作でクラッシュするため）
        // 削除を選んだ表は作り直さない（作り直してから消すのは無駄で、意味の無い喪失警告も出る）。
        // その表への作り直しの項目は、表ごと消えるので処理済みとして捨てる
        var droppedTables = DroppedObjects.From(selected).Tables;
        var existingRebuildTables = selected
            .Where(i =>
                IsRebuildTriggerKind(i.Kind)
                && !addedTables.Contains(SchemaNames.ToStoredForm(i.TableName))
                && !droppedTables.Contains(SchemaNames.ToStoredForm(i.TableName))
            )
            .Select(i => SchemaNames.ToStoredForm(i.TableName))
            .Where(liveByName.Contains)
            .ToHashSet(SchemaNames.Comparer);

        // rebuild へ転用（畳み込み）が確定した項目の集合（SchemaDiffItem は参照同一性で比較する）
        var diverted = new HashSet<SchemaDiffItem>();

        foreach (
            var item in selected.Where(i =>
                droppedTables.Contains(SchemaNames.ToStoredForm(i.TableName))
                && (IsRebuildTriggerKind(i.Kind) || i.Kind == SchemaDiffKind.AddColumn)
            )
        )
        {
            diverted.Add(item);
        }

        // live の外部キーは子表ごとに 1 回だけまとめる（作り直す表ごとに全外部キーを解決し直さない）
        var liveForeignKeysByChild = EnumerateLiveForeignKeys(context)
            .ToLookup(fk => SchemaDiffService.NormalizeTable(fk.Child), SchemaNames.Comparer);

        var rebuilds = new List<TableRebuildPlan>();
        rebuilds.AddRange(BuildCreateOnlyRebuilds(selected, addedTables, diverted, limit));
        rebuilds.AddRange(
            BuildExistingTableRebuilds(
                selected,
                existingRebuildTables,
                liveByName,
                diverted,
                context,
                liveForeignKeysByChild,
                limit,
                capabilities
            )
        );

        // 既存テーブルの作り直し（CreateOnly=false）だけが列レベル属性を落とす。
        // 新規テーブル作成（CreateOnly=true）は落とす元の定義が無いので対象外
        WarnRebuildDropsColumnAttributes(rebuilds, context, warnings);

        // 主キーを付け替える親表を参照する外部キーの、候補キー喪失（逐次 DDL の方言と同じ判定）
        WarnRebuildForeignKeysLosingCandidateKey(selected, context, warnings, limit);

        // 転用済みの項目を除いた残りをセクション化する（転用できなかった項目はレンダラーがスキップを明示する）
        var sectionItems = selected.Where(i => !diverted.Contains(i)).ToList();

        return new SyncPlan
        {
            Sections = BuildSections(sectionItems),
            Rebuilds = rebuilds,
            Warnings = warnings,
            ExcludedItems = excluded,
        };
    }

    /// <summary>
    /// 再構築で失われる属性を live の <c>CREATE TABLE</c> 文から検出し、テーブル単位の警告を積む。
    /// </summary>
    /// <remarks>
    /// 再構築の <c>CREATE TABLE</c> は意味モデルから組み立て直すため、モデルが持たない属性
    /// （列の属性・制約の句・テーブルオプション。一覧は <see cref="TableRebuildAttributeDetector"/>）は再現されない。
    /// 検出材料（<see cref="SyncPlanContext.TableCreateSql"/>）を持たない方言では 1 件も積まれない
    /// ＝逐次 DDL 方言の計画にこの警告は出ない。
    /// </remarks>
    private static void WarnRebuildDropsColumnAttributes(
        List<TableRebuildPlan> rebuilds,
        SyncPlanContext context,
        List<SyncPlanWarning> warnings
    )
    {
        if (context.TableCreateSql.Count == 0)
        {
            return;
        }

        foreach (var rebuild in rebuilds)
        {
            if (rebuild.CreateOnly)
            {
                continue;
            }

            if (
                !context.TableCreateSql.TryGetValue(
                    SchemaNames.ToStoredForm(rebuild.TableName),
                    out var createSql
                )
            )
            {
                continue;
            }

            var lost = TableRebuildAttributeDetector.Detect(createSql);

            if (lost.Count == 0)
            {
                continue;
            }

            warnings.Add(
                new SyncPlanWarning(
                    SyncPlanWarningKind.TableRebuildDropsColumnAttribute,
                    rebuild.TableName,
                    string.Join(", ", lost)
                )
            );
        }
    }

    /// <summary>選択済み AddTable を CreateOnly の再構築計画へ変換する（新規テーブルへの FK はインラインへ畳む）</summary>
    /// <remarks>
    /// 畳み込んだ項目を <paramref name="diverted"/> へ足す。遅延評価のイテレータにすると、呼び出し側が列挙するまで
    /// <paramref name="diverted"/> が埋まらず、列挙の前にセクションを組むと転用済みの項目が二重に残るため、リストで返す。
    /// </remarks>
    private static List<TableRebuildPlan> BuildCreateOnlyRebuilds(
        List<SchemaDiffItem> selected,
        HashSet<string> addedTables,
        HashSet<SchemaDiffItem> diverted,
        IdentifierLengthLimit limit
    )
    {
        var rebuilds = new List<TableRebuildPlan>();

        foreach (var addTable in selected.Where(i => i.Kind == SchemaDiffKind.AddTable))
        {
            var table = SchemaNames.ToStoredForm(addTable.TableName);
            diverted.Add(addTable);

            // その新テーブルを子とする選択済み AddForeignKey を FK 仕様として畳み込む。
            // 解決できない FK は畳まずセクションへ残し、レンダラーのスキップコメントで明示する
            var fkItems = selected
                .Where(i =>
                    i.Kind == SchemaDiffKind.AddForeignKey
                    && SchemaNames.AreSame(SchemaNames.ToStoredForm(i.TableName), table)
                )
                .ToList();
            var fks = new List<TableRebuildForeignKey>();
            var source = new List<SchemaDiffItem> { addTable };

            foreach (var fkItem in fkItems)
            {
                var resolved = ResolveAddedForeignKey(fkItem, limit);

                if (resolved is null)
                {
                    continue;
                }

                fks.Add(resolved);
                source.Add(fkItem);
                diverted.Add(fkItem);
            }

            rebuilds.Add(
                new TableRebuildPlan
                {
                    TableName = addTable.TableName,
                    NewDefinition =
                        addTable.Entity?.Clone(preserveId: true)
                        ?? new Entity { TableName = addTable.TableName },
                    ForeignKeys = fks,
                    CreateOnly = true,
                    CopyColumns = [],
                    AuxiliaryObjects = [],
                    SourceItems = source,
                }
            );
        }

        return rebuilds;
    }

    /// <summary>既存テーブルの再構築計画を組み立てる（live 定義に選択差分のみを適用して合成する）</summary>
    /// <remarks>
    /// 合成で解決できなかった項目（無い列の定義変更・構成列の無い一意制約・解決できない外部キー）は処理済みにせず、
    /// セクションへ残す（レンダラーがスキップのコメントを出す）。処理済みにすると、何も変えずに成功扱いになり、次の差分で
    /// 同じ項目がまた出る。作り直す理由になる項目が 1 つも解決できなければ、その表は作り直さない。
    /// 畳み込んだ項目は <paramref name="diverted"/> へ足すので、<see cref="BuildCreateOnlyRebuilds"/> と同じくリストで返す。
    /// </remarks>
    private static List<TableRebuildPlan> BuildExistingTableRebuilds(
        List<SchemaDiffItem> selected,
        HashSet<string> existingRebuildTables,
        NameLookup<Entity> liveByName,
        HashSet<SchemaDiffItem> diverted,
        SyncPlanContext context,
        ILookup<string, ResolvedForeignKeyRelationship> liveForeignKeysByChild,
        IdentifierLengthLimit limit,
        SyncDialectCapabilities capabilities
    )
    {
        var rebuilds = new List<TableRebuildPlan>();

        foreach (var table in existingRebuildTables)
        {
            // existingRebuildTables は引ける名前だけに絞ってある
            if (!liveByName.TryGet(table, out var live))
            {
                continue;
            }

            // このテーブルに畳み込む項目（再構築トリガー種別＋列追加）を確定する
            var candidates = selected
                .Where(i =>
                    SchemaNames.AreSame(SchemaNames.ToStoredForm(i.TableName), table)
                    && (IsRebuildTriggerKind(i.Kind) || i.Kind == SchemaDiffKind.AddColumn)
                )
                .ToList();

            // 列の追加・削除・変更を先に当てた定義で、残りの項目が解決できるかを見る
            var columnsOnly = live.Clone(preserveId: true);
            ApplySelectedColumnChanges(columnsOnly, candidates);
            var tableItems = candidates
                .Where(i => IsResolvableInRebuild(i, live, columnsOnly, limit))
                .ToList();

            if (!tableItems.Any(i => IsRebuildTriggerKind(i.Kind)))
            {
                continue;
            }

            foreach (var item in tableItems)
            {
                diverted.Add(item);
            }

            // live 定義の深いコピーに、選択された差分のみを適用して合成する
            var newDef = live.Clone(preserveId: true);

            // 一意制約の削除は、列を消す前の live の列で照合する（列を先に消すと構成列を引き当てられず、
            // 消える列を含む一意制約が合成後の定義に残る）
            ApplySelectedUniqueConstraintDrops(newDef, tableItems);
            ApplySelectedColumnChanges(newDef, tableItems);

            // 主キー変更（AlterPrimaryKey）が選択されていれば、合成後の列へ target の主キー構成を反映する
            ApplySelectedPrimaryKeyChange(newDef, tableItems);

            // 列順変更（ReorderColumns）が選択されていれば、合成後の列を target の列名順へ並べ替える
            ApplySelectedReorder(newDef, tableItems);

            // 一意制約の追加が選択されていれば、合成後の一意制約集合へ反映する
            ApplySelectedUniqueConstraintAdds(newDef, tableItems);

            // FK 集合 = live の子側 FK − 選択済み DropForeignKey ＋ 選択済み AddForeignKey
            var foreignKeys = SynthesizeForeignKeys(
                tableItems,
                liveForeignKeysByChild[table],
                limit,
                context
            );

            // データ移送対象 = live と合成後の両方に存在する列（合成後の列順で並べる）
            var liveColumnNames = live.Columns.Select(c => c.Name).ToHashSet(SchemaNames.Comparer);
            var copyColumns = newDef
                .Columns.Select(c => c.Name)
                .Where(liveColumnNames.Contains)
                .ToList();

            var auxiliaryObjects = context
                .AuxiliaryObjects.Where(a =>
                    SchemaNames.AreSame(SchemaNames.ToStoredForm(a.TableName), table)
                )
                .ToList();

            rebuilds.Add(
                new TableRebuildPlan
                {
                    TableName = live.TableName,
                    NewDefinition = newDef,
                    ForeignKeys = foreignKeys,
                    CreateOnly = false,
                    CopyColumns = copyColumns,
                    RowidAliasNullGuardColumn = RowidAliasNullGuardColumn(
                        live,
                        newDef,
                        copyColumns,
                        capabilities
                    ),
                    AuxiliaryObjects = auxiliaryObjects,
                    SourceItems = tableItems,
                }
            );
        }

        return rebuilds;
    }

    /// <summary>
    /// 移送の前に NULL を断る写す列（<see cref="TableRebuildPlan.RowidAliasNullGuardColumn"/>）。合成後の主キーが単一列で、その宣言の型で
    /// DB が NULL を採番の値へ置き換え、live ではその列が同じ置き換えをする主キーでなかった写す列。無ければ null
    /// </summary>
    /// <remarks>
    /// live でも置き換える主キー（SQLite の rowid の別名）だった列は NULL を持てないので印さない。型の変更・主キーの移動・複合の主キーの
    /// 縮小・主キーの付与のどれで別名になっても同じ判定になる。
    /// </remarks>
    private static string? RowidAliasNullGuardColumn(
        Entity live,
        Entity newDefinition,
        IReadOnlyList<string> copyColumns,
        SyncDialectCapabilities capabilities
    )
    {
        var newKey = newDefinition.GetPrimaryKeyColumnsInOrder();

        if (
            newKey.Count != 1
            || !capabilities.SingleColumnPrimaryKeyReplacesNull(newKey[0].DataType)
            || !copyColumns.Any(c => SchemaNames.AreSame(c, newKey[0].Name))
        )
        {
            return null;
        }

        var liveKey = live.GetPrimaryKeyColumnsInOrder();
        var aliasInLive =
            liveKey.Count == 1
            && SchemaNames.AreSame(liveKey[0].Name, newKey[0].Name)
            && capabilities.SingleColumnPrimaryKeyReplacesNull(liveKey[0].DataType);

        return aliasInLive ? null : newKey[0].Name;
    }

    /// <summary>再構築の合成で項目を解決できるか（解決できない項目は再構築へ畳まずセクションへ残す）</summary>
    /// <param name="item">再構築へ畳む候補の項目</param>
    /// <param name="live">live の表</param>
    /// <param name="columnsApplied">live に選んだ列の追加・削除・変更を当てた定義</param>
    private static bool IsResolvableInRebuild(
        SchemaDiffItem item,
        Entity live,
        Entity columnsApplied,
        IdentifierLengthLimit limit
    )
    {
        bool HasColumn(Entity entity, string? name) =>
            name is not null && entity.Columns.Any(c => SchemaNames.AreSame(c.Name, name));

        return item.Kind switch
        {
            SchemaDiffKind.AlterColumn => HasColumn(live, item.ColumnName),
            SchemaDiffKind.AddUniqueConstraint => item.UniqueConstraintColumns.Count > 0
                && item.UniqueConstraintColumns.All(c => HasColumn(columnsApplied, c)),
            SchemaDiffKind.AddForeignKey => ResolveAddedForeignKey(item, limit) is { } fk
                && fk.ChildColumns.All(c => HasColumn(columnsApplied, c)),
            _ => true,
        };
    }

    /// <summary>合成後の定義へ、選択された列変更（AlterColumn の写し込み / DropColumn 除去 / AddColumn 追加）を適用する</summary>
    /// <remarks>
    /// <para>
    /// AlterColumn は live の列オブジェクトを残したまま、差分が語る性質（型・NULL 許容・計算列）だけを写す。
    /// 図側の列へ差し替えてはいけない。図は live と別の Guid 空間にあるため、差し替えると live の
    /// <see cref="Entity.UniqueConstraints"/> と <see cref="Entity.PrimaryKeyColumnIds"/> が指す列 ID が
    /// 解決できなくなり、一意制約が再構築後の表から黙って消え、複合主キーの順序も変わる。
    /// 図側の主キー指定も持ち込まれ、選んでいない主キー変更が紛れ込む。
    /// </para>
    /// <para>
    /// AddColumn の列は主キーにしない。主キーの膜と順序を変えるのは選択済みの AlterPrimaryKey だけ
    /// （<see cref="ApplySelectedPrimaryKeyChange"/>）で、それが無いのに図側の主キー指定を持ち込むと、
    /// 選んでいない主キー変更が適用される。
    /// </para>
    /// </remarks>
    private static void ApplySelectedColumnChanges(
        Entity newDef,
        IReadOnlyList<SchemaDiffItem> tableItems
    )
    {
        // AlterColumn: 同名の live 列へ、型・NULL 許容・計算列だけを写す（Id・名前・主キー指定は live のまま）
        foreach (var alter in tableItems.Where(i => i.Kind == SchemaDiffKind.AlterColumn))
        {
            var column = newDef.Columns.Find(c => SchemaNames.AreSame(c.Name, alter.ColumnName));

            if (column is not null && alter.Column is not null)
            {
                column.DataType = alter.Column.DataType;
                column.IsNullable = ColumnNullability.AllowsNull(alter.Column);
                column.IsComputed = alter.Column.IsComputed;
            }
        }

        // DropColumn: 同名の列を除去する
        foreach (var drop in tableItems.Where(i => i.Kind == SchemaDiffKind.DropColumn))
        {
            newDef.Columns.RemoveAll(c => SchemaNames.AreSame(c.Name, drop.ColumnName));
        }

        // AddColumn: 末尾へ畳み込む（このテーブルが他理由で再構築される場合のみここへ来る）。
        // 主キーにするかは AlterPrimaryKey の選択だけが決める
        foreach (var add in tableItems.Where(i => i.Kind == SchemaDiffKind.AddColumn))
        {
            if (add.Column is not null)
            {
                var added = add.Column.Clone(preserveId: true);

                // 主キーを外す前に実効の NULL 許容を写す（図の主キーの列は印に依らず NOT NULL で、逐次 DDL の方言の
                // 列の追加も NOT NULL で作る。印のまま主キーを外すと、再構築の表だけが NULL 許容の列になる）
                added.IsNullable = ColumnNullability.AllowsNull(add.Column);
                added.IsPrimaryKey = false;
                newDef.Columns.Add(added);
            }
        }
    }

    /// <summary>
    /// 選択された AlterPrimaryKey があれば、合成後の各列の主キー指定と主キーの順序を target の構成で上書きする。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 上書きするのは <see cref="Column.IsPrimaryKey"/> と <see cref="Entity.PrimaryKeyColumnIds"/> のみで、
    /// 列の型・NULL 許容などは他差分の選択状況に従う
    /// （未選択の AlterColumn を主キー変更のついでに適用してしまわないため）。AlterPrimaryKey が未選択なら何もしない。
    /// </para>
    /// <para>
    /// target は live とは別の図＝列 Guid の空間が違うため、順序は target の実効順を列名で
    /// 合成後定義の列へ写像して作り直す（合成後定義に無い列名は読み飛ばす）。
    /// live 由来の順序リストをそのまま残すと、上書きした主キーの膜と順序が食い違う。
    /// </para>
    /// </remarks>
    private static void ApplySelectedPrimaryKeyChange(
        Entity newDef,
        IReadOnlyList<SchemaDiffItem> tableItems
    )
    {
        var alterPk = tableItems.FirstOrDefault(i => i.Kind == SchemaDiffKind.AlterPrimaryKey);

        if (alterPk?.Entity is null)
        {
            return;
        }

        var targetPkColumns = alterPk.Entity.GetPrimaryKeyColumnsInOrder();
        var pkNames = targetPkColumns.Select(c => c.Name).ToHashSet(SchemaNames.Comparer);

        foreach (var column in newDef.Columns)
        {
            column.IsPrimaryKey = pkNames.Contains(column.Name);
        }

        // target の実効順を列名で合成後定義の列 Guid へ写像し直す
        newDef.PrimaryKeyColumnIds = targetPkColumns
            .Select(pk => newDef.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, pk.Name)))
            .Where(c => c is not null)
            .Select(c => c!.Id)
            .ToList();
    }

    /// <summary>
    /// 選択された ReorderColumns があれば、合成後の列を target（差分項目の Entity）の列名順へ並べ替える。
    /// </summary>
    /// <remarks>
    /// target に存在する列を target の順序で先頭側へ並べ、target に無い列（未選択の削除で残った列）は
    /// 元の相対順序を保ったまま末尾へ回す（安定ソート）。ReorderColumns が未選択なら何もしない。
    /// </remarks>
    private static void ApplySelectedReorder(
        Entity newDef,
        IReadOnlyList<SchemaDiffItem> tableItems
    )
    {
        var reorder = tableItems.FirstOrDefault(i => i.Kind == SchemaDiffKind.ReorderColumns);

        if (reorder?.Entity is null)
        {
            return;
        }

        // target 列名 → 目標順インデックス
        var targetOrder = new Dictionary<string, int>(SchemaNames.Comparer);

        for (var i = 0; i < reorder.Entity.Columns.Count; i++)
        {
            targetOrder.TryAdd(reorder.Entity.Columns[i].Name, i);
        }

        // target にある列は目標順、無い列は元の相対順を保って末尾へ（OrderBy は安定ソート）
        var reordered = newDef
            .Columns.Select((column, index) => (column, index))
            .OrderBy(x => targetOrder.TryGetValue(x.column.Name, out var ti) ? ti : int.MaxValue)
            .ThenBy(x => x.index)
            .Select(x => x.column)
            .ToList();

        newDef.Columns.Clear();
        newDef.Columns.AddRange(reordered);
    }

    /// <summary>
    /// 選択された一意制約の削除を、合成後の定義（<paramref name="newDef"/>）の <see cref="Entity.UniqueConstraints"/>
    /// へ反映する。列の変更を当てる前に呼ぶ（構成列を live の列で引き当てるため）。
    /// </summary>
    /// <remarks>
    /// 合成後の定義は live の写しで、削除の項目も live の制約の名前を運ぶので、DB の一意制約の照合キー
    /// （<see cref="UniqueConstraintKey"/>＝名前と列集合）で除去する（同じ列集合のほかの一意制約は残す）。
    /// </remarks>
    private static void ApplySelectedUniqueConstraintDrops(
        Entity newDef,
        IReadOnlyList<SchemaDiffItem> tableItems
    )
    {
        foreach (var drop in tableItems.Where(i => i.Kind == SchemaDiffKind.DropUniqueConstraint))
        {
            var dropped = ExplicitlyDroppedUniqueConstraints([drop]);
            newDef.UniqueConstraints.RemoveAll(constraint =>
                UniqueConstraintNaming.TryResolveColumnNames(newDef, constraint, out var columns)
                && dropped.Contains(UniqueConstraintKey(drop.TableName, constraint.Name, columns))
            );
        }
    }

    /// <summary>
    /// 選択された一意制約の追加を、合成後の定義（<paramref name="newDef"/>）の <see cref="Entity.UniqueConstraints"/>
    /// へ反映する。
    /// </summary>
    /// <remarks>
    /// 差分項目が運ぶ<b>列名</b>を合成後の列へ引き当てて新しい構成列 ID を作る
    /// （差分項目の Entity は図側＝列 ID が live のクローンと一致しないため、ID をそのまま持ち込めない）。
    /// 引き当てられない列を含む追加は黙って捨てる（DDL 生成が解決不能な制約を出力しないのと同じ流儀）。
    /// </remarks>
    private static void ApplySelectedUniqueConstraintAdds(
        Entity newDef,
        IReadOnlyList<SchemaDiffItem> tableItems
    )
    {
        foreach (var add in tableItems.Where(i => i.Kind == SchemaDiffKind.AddUniqueConstraint))
        {
            if (add.UniqueConstraintColumns.Count == 0)
            {
                continue;
            }

            var columnIds = new List<Guid>(add.UniqueConstraintColumns.Count);
            var allResolved = true;

            foreach (var columnName in add.UniqueConstraintColumns)
            {
                var column = newDef.Columns.FirstOrDefault(c =>
                    SchemaNames.AreSame(c.Name, columnName)
                );

                if (column is null)
                {
                    allResolved = false;
                    break;
                }

                columnIds.Add(column.Id);
            }

            if (!allResolved)
            {
                continue;
            }

            newDef.UniqueConstraints.Add(
                new UniqueConstraint { Name = add.UniqueConstraintName, ColumnIds = columnIds }
            );
        }
    }

    // ---------------- ネイティブ列順変更（MySQL）の集約 ----------------

    /// <summary>選択済み ReorderColumns をネイティブ並べ替え計画（<see cref="TableReorderPlan"/>）へ変換する</summary>
    /// <remarks>
    /// live スキーマから移動集合を計算するため context 必須（rebuild 方言と同じく、選択された ReorderColumns が
    /// あるのに context が無ければ呼び出し側のバグとして例外にする）。live に無いテーブルの項目は、そのテーブル自体が
    /// 同期対象外とみなして黙って落とす。
    /// </remarks>
    private static IReadOnlyList<TableReorderPlan> BuildReorderPlans(
        List<SchemaDiffItem> selected,
        SyncPlanContext? context
    )
    {
        var reorderItems = selected.Where(i => i.Kind == SchemaDiffKind.ReorderColumns).ToList();

        if (reorderItems.Count == 0)
        {
            return [];
        }

        if (context is null)
        {
            // ネイティブ並べ替えの移動集合計算には live スキーマが必須（合成定義も live 由来）
            throw new InvalidOperationException(
                "Native column-reorder dialects require a SyncPlanContext (live schema) to compute column moves."
            );
        }

        var liveByName = IndexLiveTables(context);

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

    /// <summary>再構築後に張る FK 集合を合成する（live 集合から Drop を除き Add を足す）</summary>
    /// <param name="tableItems">この表へ畳み込む選択済みの項目</param>
    /// <param name="liveForeignKeys">この表を子に持つ live の外部キー</param>
    private static List<TableRebuildForeignKey> SynthesizeForeignKeys(
        IReadOnlyList<SchemaDiffItem> tableItems,
        IEnumerable<ResolvedForeignKeyRelationship> liveForeignKeys,
        IdentifierLengthLimit limit,
        SyncPlanContext context
    )
    {
        var foreignKeys = liveForeignKeys
            .Select(fk => ToRebuildForeignKey(fk, limit, context))
            .ToList();

        // 選択済み DropForeignKey に一致する live FK を 1 本だけ除去する（親テーブル・構成列ペアで候補を絞り、
        // 名前・参照アクションまで合うものを優先する）。同じ列ペアに外部キーが 2 本ある構成で、1 本の削除・作り直しが
        // もう 1 本まで消さないため（以前は構成列だけで照合して 2 本とも外し、変えていない外部キーが黙って消えた）
        foreach (var dropFk in tableItems.Where(i => i.Kind == SchemaDiffKind.DropForeignKey))
        {
            var signature = ResolveDroppedForeignKeySignature(dropFk);

            if (signature is null)
            {
                continue;
            }

            var (parentTable, columnPairs) = signature.Value;
            var candidates = foreignKeys
                .Where(fk =>
                    SchemaNames.AreSame(fk.ParentTable, parentTable)
                    && fk.ChildColumns.SequenceEqual(
                        ForeignKeyColumnPairResolver.ChildColumns(columnPairs),
                        SchemaNames.Comparer
                    )
                    && fk.ParentColumns.SequenceEqual(
                        ForeignKeyColumnPairResolver.ParentColumns(columnPairs),
                        SchemaNames.Comparer
                    )
                )
                .ToList();
            var dropName = dropFk.ChildEntity is null
                ? null
                : ResolveForeignKeyName(
                    dropFk.Relationship,
                    SchemaDiffService.NormalizeTable(dropFk.ChildEntity),
                    parentTable,
                    limit
                );
            var onDelete = dropFk.Relationship?.OnDelete ?? ForeignKeyReferentialAction.NoAction;
            var onUpdate = dropFk.Relationship?.OnUpdate ?? ForeignKeyReferentialAction.NoAction;
            var removed =
                candidates.FirstOrDefault(fk =>
                    SchemaNames.AreSame(fk.ConstraintName, dropName)
                    && fk.OnDelete == onDelete
                    && fk.OnUpdate == onUpdate
                )
                ?? candidates.FirstOrDefault(fk =>
                    fk.OnDelete == onDelete && fk.OnUpdate == onUpdate
                )
                ?? candidates.FirstOrDefault();

            if (removed is not null)
            {
                foreignKeys.Remove(removed);
            }
        }

        // 選択済み AddForeignKey を追加する
        foreach (var addFk in tableItems.Where(i => i.Kind == SchemaDiffKind.AddForeignKey))
        {
            var resolved = ResolveAddedForeignKey(addFk, limit);

            if (resolved is not null)
            {
                foreignKeys.Add(resolved);
            }
        }

        return foreignKeys;
    }

    /// <summary>live の外部キーを、再構築の CREATE TABLE へ載せる FK 仕様へ変換する</summary>
    /// <remarks>名前は警告・外し直しと同じく図全体から求めた live の実効名（<see cref="SyncPlanContext.LiveForeignKeyNames"/>）。</remarks>
    private static TableRebuildForeignKey ToRebuildForeignKey(
        ResolvedForeignKeyRelationship fk,
        IdentifierLengthLimit limit,
        SyncPlanContext context
    )
    {
        var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

        return new TableRebuildForeignKey(
            ResolveForeignKeyName(
                fk.Relationship,
                SchemaDiffService.NormalizeTable(fk.Child),
                parentTable,
                limit,
                context
            ),
            [.. ForeignKeyColumnPairResolver.ChildColumns(fk.ColumnPairs)],
            parentTable,
            [.. ForeignKeyColumnPairResolver.ParentColumns(fk.ColumnPairs)],
            fk.Relationship.OnDelete,
            fk.Relationship.OnUpdate
        );
    }

    /// <summary>選択済み AddForeignKey 差分項目を解決済みの FK 仕様へ変換する（解決不能なら null）</summary>
    private static TableRebuildForeignKey? ResolveAddedForeignKey(
        SchemaDiffItem item,
        IdentifierLengthLimit limit
    )
    {
        if (item.ChildEntity is null || item.ParentEntity is null)
        {
            return null;
        }

        var columnPairs = SyncScriptBuilderHelper.ResolveColumnPairs(item);

        if (columnPairs.Count == 0)
        {
            return null;
        }

        var childTable = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTable = SchemaDiffService.NormalizeTable(item.ParentEntity);

        return new TableRebuildForeignKey(
            SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, limit),
            [.. ForeignKeyColumnPairResolver.ChildColumns(columnPairs)],
            parentTable,
            [.. ForeignKeyColumnPairResolver.ParentColumns(columnPairs)],
            item.Relationship?.OnDelete ?? ForeignKeyReferentialAction.NoAction,
            item.Relationship?.OnUpdate ?? ForeignKeyReferentialAction.NoAction
        );
    }

    /// <summary>
    /// DropForeignKey 差分項目を、live FK 照合用のシグネチャ（親テーブル・構成列ペア）へ解決する
    /// </summary>
    private static (
        string ParentTable,
        IReadOnlyList<ForeignKeyColumnNamePair> ColumnPairs
    )? ResolveDroppedForeignKeySignature(SchemaDiffItem item)
    {
        if (item.Relationship is null || item.ParentEntity is null || item.ChildEntity is null)
        {
            return null;
        }

        // 差分項目に載った列ペアを優先し、無ければリレーションから解決し直す
        // （差分計算が作った項目には必ず載るが、外部から組み立てた項目でもモデルから復元できるようにする）
        IReadOnlyList<ForeignKeyColumnNamePair>? columnPairs =
            SyncScriptBuilderHelper.ResolveColumnPairs(item);

        if (columnPairs.Count == 0)
        {
            columnPairs = ForeignKeyColumnPairResolver.Resolve(
                item.Relationship,
                item.ParentEntity,
                item.ChildEntity
            );
        }

        if (columnPairs is null || columnPairs.Count == 0)
        {
            return null;
        }

        return (SchemaDiffService.NormalizeTable(item.ParentEntity), columnPairs);
    }

    /// <summary>
    /// この種別が既存テーブルの再構築を要求するか
    /// （列型変更・主キー変更・列削除・FK 変更・列順変更・一意制約の変更）
    /// </summary>
    /// <remarks>
    /// rebuild 方言（SQLite）では列順変更・主キー変更・一意制約の追加削除もテーブル再構築で実現するため、
    /// ReorderColumns / AlterPrimaryKey / Add・DropUniqueConstraint も再構築トリガーに含める
    /// （それだけが選択された場合も CreateOnly=false の再構築になる）。
    /// </remarks>
    private static bool IsRebuildTriggerKind(SchemaDiffKind kind) =>
        kind
            is SchemaDiffKind.AlterColumn
                or SchemaDiffKind.AlterPrimaryKey
                or SchemaDiffKind.DropColumn
                or SchemaDiffKind.DropForeignKey
                or SchemaDiffKind.AddForeignKey
                or SchemaDiffKind.ReorderColumns
                or SchemaDiffKind.AddUniqueConstraint
                or SchemaDiffKind.DropUniqueConstraint;
}
