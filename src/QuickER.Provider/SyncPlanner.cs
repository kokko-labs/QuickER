using System;
using System.Collections.Generic;
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
    /// rebuild 方言で合成に用いる live スキーマ。逐次 DDL 方言では不要（<c>null</c> 可）。
    /// rebuild 方言で <c>null</c> を渡すと <see cref="InvalidOperationException"/> を投げる（呼び出し側のバグ）。
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

        // 選択済みの項目のみを対象にする（未選択は完全に除外）。まず選んでいない変更（表・列の追加・主キーの変更）を
        // 前提にする項目を外して名指しする。外部キー・一意制約の作り直しはその後で DROP と ADD の 2 項目へ展開し、
        // 以降は通常の差分として扱う（展開の前に判定する＝作り直しを外すときは削除と追加をまとめて外し、
        // 削除だけが走って制約が消えたまま残らない）
        var dependable = ExcludeItemsRequiringUnselected(
            [.. items.Where(i => i.IsSelected)],
            capabilities,
            context,
            warnings,
            excluded
        );

        // ALTER COLUMN も FK の後付けもできない方言はテーブル再構築が必要になる
        var isRebuildDialect =
            !capabilities.SupportsAlterColumn || !capabilities.SupportsForeignKeyAlter;

        // 逐次 DDL の方言では、同期後に両端の型が食い違う外部キーの追加・作り直しを外して名指しする（付け直しが必ず
        // 失敗する）。再構築の方言は外部キーの型を確かめない。続けて、子の列が同期後に NOT NULL になる SET NULL の外部キーの
        // 追加・作り直しを名指しし、作れない方言（SQL Server・MySQL）では外す。どちらも展開の前に判定し、作り直しは
        // 削除と追加をまとめて外す
        var selected = ExpandAlters(
            ExcludeSetNullForeignKeysOnNotNullColumns(
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
            )
        );

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
        WarnUniqueConstraintDropsBreakingForeignKeys(
            selected,
            context,
            warnings,
            capabilities.IdentifierLengthLimit
        );

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
            // 候補キーを証明できない外部キーの追加は外部キーの追加の最後へ、必ず断られる文は計画の先頭へ
            if (!capabilities.TransactionalDdl)
            {
                sections = MoveCertainFailureFirst(
                    ArrangeUnprovenForeignKeyAddsLast(sections, selected, context),
                    sectionItems,
                    capabilities,
                    context
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
            .ToDictionary(g => g.Key, g => g.First().Entity!, SchemaNames.Comparer);
        var postSyncUniques = BuildPostSyncUniqueConstraints(ExpandAlters(selected), context);

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
                column.IsNullable = item.OldColumn.IsNullable;
                kept.Add(
                    new SchemaDiffItem
                    {
                        Kind = item.Kind,
                        TableName = item.TableName,
                        ColumnName = item.ColumnName,
                        Entity = item.Entity,
                        Column = column,
                        OldColumn = item.OldColumn,
                        Description = item.Description,
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

                    return (
                        ConstraintNames.EffectiveForeignKey(
                            item.ForeignKeyName,
                            child,
                            parent,
                            capabilities.IdentifierLengthLimit
                        ),
                        missing
                    );
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

                    return (
                        item.UniqueConstraintName
                            ?? UniqueConstraint.SynthesizeName(
                                table,
                                item.UniqueConstraintColumns,
                                capabilities.IdentifierLengthLimit
                            ),
                        missing
                    );

                case SchemaDiffKind.SetTableDescription:
                    RequireColumns(table, [], missing);
                    return ("COMMENT", missing);

                case SchemaDiffKind.SetColumnDescription when item.ColumnName is not null:
                    RequireColumns(table, [item.ColumnName], missing);
                    return (item.ColumnName, missing);

                case SchemaDiffKind.AlterColumn
                    when item.ColumnName is not null
                        && item.Column is { IsNullable: true }
                        && item.OldColumn is { IsNullable: false }
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

        // 表と列が同期後に在るか（無いものを missing へ「表」「表.列」で足す）
        void RequireColumns(string table, IEnumerable<string> columns, List<string> missing)
        {
            if (addedTables.Contains(table))
            {
                return;
            }

            if (!liveByName.TryGet(table, out var live))
            {
                missing.Add(table);
                return;
            }

            foreach (var column in columns)
            {
                if (
                    !addedColumns.Contains(ColumnKey(table, column))
                    && !live.Columns.Any(c => SchemaNames.AreSame(c.Name, column))
                )
                {
                    missing.Add($"{table}.{column}");
                }
            }
        }

        // 同期後もこの列が主キーに入っているか（主キーの変更を選んでいればその構成、無ければ live の構成）
        bool StaysInPrimaryKey(string table, string column) =>
            PostSyncPrimaryKey(table).Any(c => SchemaNames.AreSame(c, column));

        // 同期後の主キーの列名（主キーの変更を選んでいればその構成、無ければ live の構成）
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

        List<string> PostSyncPrimaryKey(string table)
        {
            var key =
                primaryKeyChanges.TryGetValue(table, out var target)
                    ? target.GetPrimaryKeyColumnsInOrder()
                : liveByName.TryGet(table, out var live) ? live.GetPrimaryKeyColumnsInOrder()
                : [];
            return [.. key.Select(c => c.Name)];
        }
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
                    liveColumn is not { IsNullable: true }
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
                        $"{constraint.Name ?? UniqueConstraint.SynthesizeName(table, columns, limit)} ({string.Join(", ", columns)})"
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
                        $"{constraint.Name ?? UniqueConstraint.SynthesizeName(table, columns, limit)} ({string.Join(", ", columns)})"
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
    /// 外部キーの追加のセクションの中を、参照先が同期後の候補キーだと証明できる追加 → 証明できない追加の順へ安定に並べ替える。
    /// </summary>
    /// <param name="sections">セクション</param>
    /// <param name="selected">選択済みの項目（同期後の主キー・一意制約の合成に使う）</param>
    /// <param name="context">live スキーマ（無ければ並べ替えない）</param>
    /// <remarks>
    /// 証明できない追加（<see cref="SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey"/>・
    /// <see cref="SyncPlanWarningKind.ForeignKeyNotOnCandidateKey"/> と同じ証明＝<see cref="StaysCandidateKey"/>）は、
    /// 取込外の一意索引で通ることがあるので実行は止めないが、DB が断ると計画はそこで止まる。DDL が暗黙コミットされる方言では、
    /// その後ろにある外部キーの付け直し（同じ計画で先に外したもの）が走らず、警告していない外部キーが消えたまま残った。
    /// 外部キーの追加は互いに依存せず、外部キーの追加の後ろに制約の文は無いので、証明できない追加を最後へ回せば、断られても
    /// 消えるのは警告した外部キー自身だけになる。
    /// </remarks>
    private static List<SyncPlanSection> ArrangeUnprovenForeignKeyAddsLast(
        List<SyncPlanSection> sections,
        IReadOnlyList<SchemaDiffItem> selected,
        SyncPlanContext? context
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

        return
        [
            .. sections.Select(section =>
                section.Kind == SchemaDiffKind.AddForeignKey
                    ? new SyncPlanSection
                    {
                        Kind = section.Kind,
                        PrimaryKeyPhase = section.PrimaryKeyPhase,
                        Items = [.. section.Items.OrderBy(item => IsProven(item) ? 0 : 1)],
                    }
                    : section
            ),
        ];

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
    /// 計画が残す外部キーに阻まれて必ず断られる文があれば、計画の順で最初の 1 文を、計画の先頭の独立したセクションへ移す
    /// （何も変えずに失敗させる）。
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
    /// <item>主キーの解除（Drop の段）：旧主キーに支えられた外部キーが残る（SQL Server 3725・PostgreSQL 2BP01・Oracle ORA-02273・
    /// MySQL 1553）。</item>
    /// <item>列の型の変更（<see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/> の方言）：その列を両端のどちらかに
    /// 持つ外部キーが残り、同期後に両端の型が食い違う（MySQL 3780）。</item>
    /// <item>子の列の NOT NULL 化（<see cref="SyncDialectCapabilities.SetNullRequiresNullableChildColumn"/> の方言）：その列を子の列に持つ
    /// SET NULL の外部キーが残る（MySQL 1830）。</item>
    /// </list>
    /// <para>
    /// 主キーの変更を移すときは Drop の段だけを移す（付与は元の位置のまま）。移した文が予言に反して通っても、ほかの文は元の並びで
    /// 続く。
    /// </para>
    /// </remarks>
    private static List<SyncPlanSection> MoveCertainFailureFirst(
        List<SyncPlanSection> sections,
        IReadOnlyList<SchemaDiffItem> sectionItems,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context
    )
    {
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
        var alteredColumnTypes = new Dictionary<string, string>();

        foreach (
            var alter in sectionItems.Where(i =>
                i.Kind == SchemaDiffKind.AlterColumn && i.ColumnName is not null
            )
        )
        {
            alteredColumnTypes[ColumnKey(alter.TableName, alter.ColumnName!)] =
                alter.Column?.DataType ?? string.Empty;
        }

        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            var failing = section.Items.FirstOrDefault(item => IsCertainFailure(section, item));

            if (failing is null)
            {
                continue;
            }

            var rest = section.Items.Where(item => !ReferenceEquals(item, failing)).ToList();
            var moved = new SyncPlanSection
            {
                Kind = section.Kind,
                PrimaryKeyPhase = section.PrimaryKeyPhase,
                Items = [failing],
            };

            return
            [
                moved,
                .. sections
                    .Select(
                        (other, i) =>
                            i == index
                                ? new SyncPlanSection
                                {
                                    Kind = other.Kind,
                                    PrimaryKeyPhase = other.PrimaryKeyPhase,
                                    Items = rest,
                                }
                                : other
                    )
                    .Where(other => other.Items.Count > 0),
            ];
        }

        return sections;

        bool IsCertainFailure(SyncPlanSection section, SchemaDiffItem item)
        {
            var table = SchemaNames.ToStoredForm(item.TableName);

            if (
                section.Kind == SchemaDiffKind.AlterPrimaryKey
                && section.PrimaryKeyPhase == PrimaryKeyPhase.Drop
            )
            {
                return liveByName.TryGet(table, out var live)
                    && live.Columns.Any(c => c.IsPrimaryKey)
                    && retained.Any(fk =>
                        SchemaNames.AreSame(fk.Parent, table)
                        && DependsOnOldPrimaryKey(
                            liveByName,
                            capabilities,
                            fk.Parent,
                            [.. ForeignKeyColumnPairResolver.ParentColumns(fk.Fk.ColumnPairs)]
                        )
                    );
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
                return false;
            }

            var column = ColumnKey(table, item.ColumnName);

            // 残す外部キーの列の型を変え、同期後に両端の型が食い違う
            if (
                capabilities.AlterColumnRequiresForeignKeyRebuild
                && !capabilities.AreSameType(liveColumn.DataType, item.Column.DataType)
                && retained.Any(fk =>
                    fk.Fk.ColumnPairs.Any(p =>
                        ColumnKey(fk.Child, p.ChildColumn) == column
                        || ColumnKey(fk.Parent, p.ParentColumn) == column
                    )
                    && HasPostSyncTypeMismatch(
                        fk.Fk,
                        fk.Child,
                        fk.Parent,
                        alteredColumnTypes,
                        capabilities
                    )
                )
            )
            {
                return true;
            }

            // 残す SET NULL の外部キーの子の列を NOT NULL にする
            return capabilities.SetNullRequiresNullableChildColumn
                && liveColumn.IsNullable
                && !item.Column.IsNullable
                && retained.Any(fk =>
                    UsesSetNull(fk.Fk.Relationship, capabilities)
                    && fk.Fk.ColumnPairs.Any(p => ColumnKey(fk.Child, p.ChildColumn) == column)
                );
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

        // 定義が変わる列 → 新しい型（同期後の両端の型の照合は方言に依らず使う）
        var allAlteredColumnTypes = new Dictionary<string, string>();

        foreach (
            var alter in selected.Where(i =>
                i.Kind == SchemaDiffKind.AlterColumn && i.ColumnName is not null
            )
        )
        {
            allAlteredColumnTypes[ColumnKey(alter.TableName, alter.ColumnName!)] =
                alter.Column?.DataType ?? string.Empty;
        }

        // (b) 外し直しの理由になる列の変更（FK 参加列の型変更が依存エラーになる方言のみ対象）
        var alteredColumnTypes = capabilities.AlterColumnRequiresForeignKeyRebuild
            ? allAlteredColumnTypes
            : new Dictionary<string, string>();
        var liveByName = IndexLiveTables(context);

        // (c) 外部キーを支える索引が消える表 → 消える索引の列（旧主キー・削除する一意制約。MySQL のみ対象）
        var lostIndexes = capabilities.ForeignKeyRequiresLeadingIndex
            ? CollectLostIndexes(selected, pkChangedTables.Keys, context)
            : new Dictionary<string, List<List<string>>>(SchemaNames.Comparer);

        if (pkChangedTables.Count == 0 && alteredColumnTypes.Count == 0 && lostIndexes.Count == 0)
        {
            return selected;
        }

        // 候補キーの証明に使う「同期後に存在する一意制約」（テーブル → 列集合シグネチャの集合）
        var postSyncUniques = BuildPostSyncUniqueConstraints(selected, context);

        // 明示的に DROP が選択されている FK（自動 DROP の重複も再 ADD も行わない）。
        // 端が消える外部キーは InjectVanishingForeignKeyDrops が先に外す項目として足しているのでここに含まれる
        var explicitlyDropped = CollectExplicitlyDroppedForeignKeys(selected, limit);

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
                    alteredColumnTypes,
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

        doomedColumns.IntersectWith(alteredColumnTypes.Keys);

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
                            && IsSameColumnSet(i.UniqueConstraintColumns, referencedColumns)
                        )
                    )
                );
            var columnAltered = fk.ColumnPairs.Any(p =>
                alteredColumnTypes.ContainsKey(ColumnKey(childTable, p.ChildColumn))
                || alteredColumnTypes.ContainsKey(ColumnKey(parentTable, p.ParentColumn))
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
            // どの外部キーも外さずに列の変更の時点で失敗させ、食い違う外部キーを警告で名指しする（同じ列のほかの
            // 外部キー・残した外部キーが参照する親の列の外部キー・無関係な列の外部キーも外さない）
            if (doomedColumns.Count > 0)
            {
                if (
                    columnAltered
                    && HasPostSyncTypeMismatch(
                        fk,
                        childTable,
                        parentTable,
                        alteredColumnTypes,
                        capabilities
                    )
                )
                {
                    AddTypeMismatchWarning(childTable, constraintName);
                }

                continue;
            }

            if (setNullBlocked)
            {
                continue;
            }

            // 主キーの変更での付け直しも、構成列の型を変えて同期後に両端の型が食い違うなら必ず失敗する（列の変更で外し直しを
            // 要しない方言でも）。外さずに主キーの解除の時点で失敗させ、食い違う外部キーを名指しする
            if (
                parentPkChanged
                && fk.ColumnPairs.Any(p =>
                    allAlteredColumnTypes.ContainsKey(ColumnKey(childTable, p.ChildColumn))
                    || allAlteredColumnTypes.ContainsKey(ColumnKey(parentTable, p.ParentColumn))
                )
                && HasPostSyncTypeMismatch(
                    fk,
                    childTable,
                    parentTable,
                    allAlteredColumnTypes,
                    capabilities
                )
            )
            {
                AddTypeMismatchWarning(childTable, constraintName);
                continue;
            }

            var description = string.Format(Strings.Diff_AutoForeignKeyRebuild, constraintName);

            if (
                parentPkChanged
                && !StaysCandidateKey(newPkColumns, postSyncUniques, parentTable, referencedColumns)
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
    /// 参照する列が旧主キーと同じ列集合ならどの方言でも支えられている。外部キーが参照先の索引の先頭の列を要する方言（MySQL）では、
    /// 旧主キーの先頭の列だけを参照する外部キーも支えられうる（8.0 系や restrict_fk_on_non_standard_key=OFF で作れる非標準キーの
    /// 外部キー。ほかに支える索引が無いと主キーの解除がエラー 1553 になる）。一意制約を参照する外部キーは巻き込まれない。
    /// 参照先が live に無ければ支えられているとみなす。
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

        var oldKey = liveParent.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList();

        if (IsSameColumnSet(oldKey, referencedColumns))
        {
            return true;
        }

        return capabilities.ForeignKeyRequiresLeadingIndex
            && oldKey.Count > referencedColumns.Count
            && IsSameColumnSet(oldKey.Take(referencedColumns.Count), referencedColumns);
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

    /// <summary>同期後（選択済みの列定義変更を適用した後）に、外部キーの両端の列の型が食い違うか</summary>
    /// <remarks>
    /// 型の比較は差分の型比較と同じ規則（<see cref="SyncDialectCapabilities.AreSameType"/>＝方言の取込表記へ寄せ、
    /// 前後空白・大文字小文字を無視）。live の表記と図の表記が混ざって比べられるため、正規化を通さないと
    /// 綴りだけ違う同じ型を「食い違う」と判定し、外し直しを止めてしまう。
    /// 変更しない側は live の型で比べる。
    /// </remarks>
    private static bool HasPostSyncTypeMismatch(
        ResolvedForeignKeyRelationship fk,
        string childTable,
        string parentTable,
        Dictionary<string, string> alteredColumnTypes,
        SyncDialectCapabilities capabilities
    )
    {
        foreach (var pair in fk.ColumnPairs)
        {
            var childType = PostSyncType(fk.Child, childTable, pair.ChildColumn);
            var parentType = PostSyncType(fk.Parent, parentTable, pair.ParentColumn);

            if (!capabilities.AreForeignKeyCompatibleTypes(childType, parentType))
            {
                return true;
            }
        }

        return false;

        string PostSyncType(Entity entity, string table, string column) =>
            alteredColumnTypes.TryGetValue(ColumnKey(table, column), out var altered)
                ? altered
                : entity.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, column))?.DataType
                    ?? string.Empty;
    }

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
                    ResolveForeignKeyName(
                        add.Relationship,
                        childTable,
                        parentTable,
                        capabilities.IdentifierLengthLimit
                    )
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
    /// 同期後の型は、列の変更を選んだ列はその新しい型、live に在る列は live の型、live に無い列（追加する表・列）は図の型。
    /// 判定は、構成列のどれかの型がこの同期で決まるときだけ（live の時点で綴りだけ違う既存の外部キーの作り直しは止めない）。
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
        var alteredColumnTypes = new Dictionary<string, string>();

        foreach (
            var alter in selected.Where(i =>
                i.Kind == SchemaDiffKind.AlterColumn && i.ColumnName is not null
            )
        )
        {
            alteredColumnTypes[ColumnKey(alter.TableName, alter.ColumnName!)] =
                alter.Column?.DataType ?? string.Empty;
        }

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
                var decidedBySync = false;
                var mismatch = false;

                foreach (var pair in pairs)
                {
                    var childType = PostSyncType(child, childTable, pair.ChildColumn);
                    var parentType = PostSyncType(parent, parentTable, pair.ParentColumn);

                    if (!capabilities.AreForeignKeyCompatibleTypes(childType, parentType))
                    {
                        mismatch = true;
                    }
                }

                if (decidedBySync && mismatch)
                {
                    ExcludeSelected(
                        item,
                        new SyncPlanWarning(
                            SyncPlanWarningKind.ForeignKeyColumnTypeMismatch,
                            childTable,
                            ResolveForeignKeyName(
                                item.Relationship,
                                childTable,
                                parentTable,
                                capabilities.IdentifierLengthLimit
                            )
                        ),
                        warnings,
                        excluded
                    );
                    continue;
                }

                // 構成列の同期後の型（この同期で決まる型なら decidedBySync を立てる）
                string PostSyncType(Entity diagramEntity, string table, string column)
                {
                    if (alteredColumnTypes.TryGetValue(ColumnKey(table, column), out var altered))
                    {
                        decidedBySync = true;
                        return altered;
                    }

                    if (
                        liveByName.TryGet(table, out var live)
                        && live.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, column))
                            is { } liveColumn
                    )
                    {
                        return liveColumn.DataType;
                    }

                    decidedBySync = true;
                    return diagramEntity
                            .Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, column))
                            ?.DataType
                        ?? string.Empty;
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
                    var constraintName = ResolveForeignKeyName(
                        relationship,
                        childTable,
                        SchemaDiffService.NormalizeTable(parent),
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
                    is { IsNullable: true, IsPrimaryKey: false }
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
                altered[ColumnKey(item.TableName, item.ColumnName)] = item.Column.IsNullable;
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

            return source is null || source.IsNullable && !source.IsPrimaryKey;
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
                        is { IsNullable: false }
                )
                .ToList();

            if (notNull.Count == 0)
            {
                continue;
            }

            var childTable = SchemaDiffService.NormalizeTable(child);
            var name = ResolveForeignKeyName(
                add.Relationship,
                childTable,
                SchemaDiffService.NormalizeTable(add.ParentEntity!),
                capabilities.IdentifierLengthLimit
            );

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
                DbTypeText.AreEquivalent(BaseTypeName(oldType), BaseTypeName(newType))
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

        string BaseTypeName(string type) =>
            capabilities.NormalizeTypeNotation(type ?? string.Empty).Split('(')[0];
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
    /// 列集合シグネチャで重複排除して自動 DROP を注入せず、再 ADD もしない（「消す」意図を尊重する）。
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
    /// 主キーが同じ列を覆っていれば実際には壊れないが、その判定には同期後の主キー構成が要る。誤警告を許容して
    /// 単純な「被参照列と完全一致するか」で判定し、実行はブロックしない（レンダラーは DROP を出す）。
    /// </remarks>
    private static void WarnUniqueConstraintDropsBreakingForeignKeys(
        IReadOnlyList<SchemaDiffItem> selected,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings,
        IdentifierLengthLimit limit
    )
    {
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

            var referencedSignature = UniqueConstraintNaming.ColumnSetSignature(
                ForeignKeyColumnPairResolver.ParentColumns(fk.ColumnPairs)
            );

            foreach (var drop in drops)
            {
                if (!SchemaNames.AreSame(SchemaNames.ToStoredForm(drop.TableName), parentTable))
                {
                    continue;
                }

                if (
                    !string.Equals(
                        UniqueConstraintNaming.ColumnSetSignature(drop.UniqueConstraintColumns),
                        referencedSignature,
                        StringComparison.Ordinal
                    )
                )
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
        $"{SchemaNames.Key(table)}|{(name is null ? string.Empty : SchemaNames.Key(name))}|{UniqueConstraintNaming.ColumnSetSignature(columnNames)}";

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
        $"{SchemaNames.Key(table)}|{SchemaNames.Key(column)}";

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
        $"{SchemaNames.Key(childTable)}|{SchemaNames.Key(parentTable)}|"
        + string.Join(
            ",",
            columnPairs.Select(p =>
                $"{SchemaNames.Key(p.ParentColumn)}>{SchemaNames.Key(p.ChildColumn)}"
            )
        )
        + $"|{SchemaNames.Key(ResolveForeignKeyName(relationship, childTable, parentTable, limit))}"
        + $"|{relationship?.OnDelete ?? ForeignKeyReferentialAction.NoAction}"
        + $"|{relationship?.OnUpdate ?? ForeignKeyReferentialAction.NoAction}";

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
                limit
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
        IdentifierLengthLimit limit
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
                limit
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
                    AuxiliaryObjects = auxiliaryObjects,
                    SourceItems = tableItems,
                }
            );
        }

        return rebuilds;
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
                column.IsNullable = alter.Column.IsNullable;
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
        IdentifierLengthLimit limit
    )
    {
        var foreignKeys = liveForeignKeys.Select(fk => ToRebuildForeignKey(fk, limit)).ToList();

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
    private static TableRebuildForeignKey ToRebuildForeignKey(
        ResolvedForeignKeyRelationship fk,
        IdentifierLengthLimit limit
    )
    {
        var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

        return new TableRebuildForeignKey(
            ResolveForeignKeyName(
                fk.Relationship,
                SchemaDiffService.NormalizeTable(fk.Child),
                parentTable,
                limit
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
