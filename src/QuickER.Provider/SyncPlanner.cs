using System;
using System.Collections.Generic;
using System.Linq;
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
    /// 合成には使わない（合成の正本はあくまで <see cref="LiveEntities"/>）。再構築で失われる列レベル属性
    /// （<c>AUTOINCREMENT</c> / <c>DEFAULT</c> / <c>CHECK</c> / <c>COLLATE</c> / 生成列）を検出して
    /// 警告するためだけに参照する。取得できない方言では空のまま＝警告も出ない。
    /// </remarks>
    public IReadOnlyDictionary<string, string> TableCreateSql { get; init; } =
        new Dictionary<string, string>();
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
    /// テーブル / 列の追加 → FK 解除 → <b>一意制約解除</b> → <b>主キー解除</b> → 列定義変更 → <b>主キー付与</b> →
    /// 列 / テーブル削除 → <b>一意制約追加</b> → FK 追加 → 説明設定の順に並べる。
    /// </summary>
    /// <remarks>
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
    /// 「FK 解除 → 主キー解除 → 列定義変更 → 主キー付与 → …（列 / テーブル削除）… → FK 追加」となり、
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
        (SchemaDiffKind.AddTable, PrimaryKeyPhase.None),
        (SchemaDiffKind.AddColumn, PrimaryKeyPhase.None),
        (SchemaDiffKind.DropForeignKey, PrimaryKeyPhase.None),
        (SchemaDiffKind.DropUniqueConstraint, PrimaryKeyPhase.None),
        (SchemaDiffKind.AlterPrimaryKey, PrimaryKeyPhase.Drop),
        (SchemaDiffKind.AlterColumn, PrimaryKeyPhase.None),
        (SchemaDiffKind.AlterPrimaryKey, PrimaryKeyPhase.Add),
        (SchemaDiffKind.DropColumn, PrimaryKeyPhase.None),
        (SchemaDiffKind.DropTable, PrimaryKeyPhase.None),
        (SchemaDiffKind.AddUniqueConstraint, PrimaryKeyPhase.None),
        (SchemaDiffKind.AddForeignKey, PrimaryKeyPhase.None),
        (SchemaDiffKind.SetTableDescription, PrimaryKeyPhase.None),
        (SchemaDiffKind.SetColumnDescription, PrimaryKeyPhase.None),
    ];

    /// <summary>テーブル名の比較は方言横断で大文字小文字を無視する</summary>
    private static readonly StringComparer TableComparer = StringComparer.OrdinalIgnoreCase;

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
        // 選択済みの項目のみを対象にする（未選択は完全に除外）
        var selected = items.Where(i => i.IsSelected).ToList();

        // 計画の組み立て中に見つかった注意事項（実行は止めず、表示層が実行確認へ織り込む）
        var warnings = new List<SyncPlanWarning>();

        // 一意制約の削除が既存 FK の被参照列を候補キーでなくしうる場合は警告する（方言に依らず同じ危険）
        WarnUniqueConstraintDropsBreakingForeignKeys(selected, context, warnings);

        // ALTER COLUMN も FK の後付けもできない方言はテーブル再構築が必要になる
        var isRebuildDialect =
            !capabilities.SupportsAlterColumn || !capabilities.SupportsForeignKeyAlter;

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

            return new SyncPlan
            {
                Sections = BuildSections(sectionItems),
                Reorders = reorders,
                Warnings = warnings,
            };
        }

        if (context is null)
        {
            // rebuild 合成には live スキーマが必須（図の定義を直接使うと未選択の変更が紛れ込むため）
            throw new InvalidOperationException(
                "Rebuild dialects require a SyncPlanContext (live schema) to synthesize table rebuilds."
            );
        }

        return BuildRebuildPlan(selected, context, warnings);
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

    /// <summary>この主キー変更項目が新しい主キー列を持つか（＝付与フェーズを要するか）</summary>
    private static bool HasNewPrimaryKeyColumns(SchemaDiffItem item) =>
        item.Entity?.Columns.Any(c => c.IsPrimaryKey) == true;

    // ---------------- 依存 FK の自動 DROP → 再 ADD（逐次 DDL 方言） ----------------

    /// <summary>
    /// 選択された変更に巻き込まれる live の外部キーを、暗黙の DROP（先頭側）と再 ADD（末尾側）として注入する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 対象は (a) 選択済み <see cref="SchemaDiffKind.AlterPrimaryKey"/> のテーブルを<b>参照している</b> live FK（全方言）と、
    /// (b) 選択済み <see cref="SchemaDiffKind.AlterColumn"/> の列が子側・親側いずれかとして関与する live FK
    /// （<see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/> が <c>true</c> の方言のみ）。
    /// いずれも「FK が張られたまま」では DDL が依存エラーで失敗するため、計画側で外して戻す。
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
        if (context is null)
        {
            // live 情報が無ければ既存 FK を復元できない（逐次方言では VM が常に渡すため実質防御）
            return selected;
        }

        // (a) 主キー構成が変わるテーブル → 新しい主キー列名の集合
        // （そのテーブルを参照する FK は方言を問わず一旦外す必要がある。列集合は候補キー喪失の判定に使う）
        var pkChangedTables = new Dictionary<string, HashSet<string>>(TableComparer);

        foreach (var item in selected.Where(i => i.Kind == SchemaDiffKind.AlterPrimaryKey))
        {
            var table = item.TableName.Trim();

            if (!pkChangedTables.TryGetValue(table, out var newPkColumns))
            {
                newPkColumns = new HashSet<string>(TableComparer);
                pkChangedTables[table] = newPkColumns;
            }

            foreach (var pk in item.Entity?.Columns.Where(c => c.IsPrimaryKey).ToList() ?? [])
            {
                newPkColumns.Add(pk.Name);
            }
        }

        // (b) 定義が変わる列（FK 参加列の型変更が依存エラーになる方言のみ対象）
        HashSet<string> alteredColumns = capabilities.AlterColumnRequiresForeignKeyRebuild
            ? selected
                .Where(i => i.Kind == SchemaDiffKind.AlterColumn && i.ColumnName is not null)
                .Select(i => ColumnKey(i.TableName, i.ColumnName!))
                .ToHashSet()
            : [];

        if (pkChangedTables.Count == 0 && alteredColumns.Count == 0)
        {
            return selected;
        }

        // 候補キーの証明に使う「同期後に存在する一意制約」（テーブル → 列集合シグネチャの集合）
        var postSyncUniques = BuildPostSyncUniqueConstraints(selected, context);

        // 明示的に DROP が選択されている FK（自動 DROP の重複も再 ADD も行わない）
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
                    columnPairs
                )
            );
        }

        var autoDrops = new List<SchemaDiffItem>();
        var autoAdds = new List<SchemaDiffItem>();

        foreach (var fk in EnumerateLiveForeignKeys(context))
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);
            var referencedColumns = ForeignKeyColumnPairResolver
                .ParentColumns(fk.ColumnPairs)
                .ToList();

            // (a) 参照先テーブルの主キーが変わる / (b) FK が参加する列の定義が変わる
            // （複合外部キーは全構成列のどれか 1 つでも定義が変われば作り直しになる）
            var parentPkChanged = pkChangedTables.TryGetValue(parentTable, out var newPkColumns);
            var affected =
                parentPkChanged
                || fk.ColumnPairs.Any(p =>
                    alteredColumns.Contains(ColumnKey(childTable, p.ChildColumn))
                    || alteredColumns.Contains(ColumnKey(parentTable, p.ParentColumn))
                );

            if (!affected)
            {
                continue;
            }

            if (explicitlyDropped.Contains(ForeignKeyKey(childTable, parentTable, fk.ColumnPairs)))
            {
                continue;
            }

            var constraintName = ResolveForeignKeyName(fk.Relationship, childTable, parentTable);
            var description = string.Format(Strings.Diff_AutoForeignKeyRebuild, constraintName);

            // 被参照列（複合外部キーなら全構成列）が候補キーであり続ける根拠は
            // 「同期後の主キーが被参照列集合とちょうど一致」か「同期後に同じ列集合の一意制約が在る」のいずれか。
            // 被参照列が新主キーに含まれていても他の列と複合になっていれば主キーは根拠にならない
            // （(id) → (id, code) の拡張は 4 方言中 3 方言で再 ADD が失敗する）。
            // 一意制約はモデルの正本（Entity.UniqueConstraints）から同期後の集合を厳密に合成して判定するため、
            // 「自然キー UNIQUE を持つ表の主キー付け替え」は誤警告しない。証明できない構成は
            // （一意インデックス等で実際には通ることがあっても）警告する＝実行は止めない安全側へ倒す
            var staysCandidateKey =
                (newPkColumns is not null && IsSameColumnSet(newPkColumns, referencedColumns))
                || IsCoveredByUniqueConstraint(postSyncUniques, parentTable, referencedColumns);

            if (parentPkChanged && !staysCandidateKey)
            {
                warnings.Add(
                    new SyncPlanWarning(
                        SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey,
                        childTable,
                        constraintName
                    )
                );
            }

            autoDrops.Add(
                new SchemaDiffItem
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
                }
            );

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
    )
    {
        var byTable = new Dictionary<string, HashSet<string>>(TableComparer);

        if (context is null)
        {
            return byTable;
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

                Signatures(byTable, table).Add(UniqueConstraintNaming.ColumnSetSignature(columns));
            }
        }

        // 選択済みの削除を落とす
        foreach (var item in selected.Where(i => i.Kind == SchemaDiffKind.DropUniqueConstraint))
        {
            if (byTable.TryGetValue(item.TableName.Trim(), out var signatures))
            {
                signatures.Remove(
                    UniqueConstraintNaming.ColumnSetSignature(item.UniqueConstraintColumns)
                );
            }
        }

        // 選択済みの追加を足す
        foreach (var item in selected.Where(i => i.Kind == SchemaDiffKind.AddUniqueConstraint))
        {
            if (item.UniqueConstraintColumns.Count == 0)
            {
                continue;
            }

            Signatures(byTable, item.TableName.Trim())
                .Add(UniqueConstraintNaming.ColumnSetSignature(item.UniqueConstraintColumns));
        }

        return byTable;

        static HashSet<string> Signatures(Dictionary<string, HashSet<string>> byTable, string table)
        {
            if (!byTable.TryGetValue(table, out var signatures))
            {
                signatures = new HashSet<string>(StringComparer.Ordinal);
                byTable[table] = signatures;
            }

            return signatures;
        }
    }

    /// <summary>
    /// この列集合とちょうど一致する一意制約が同期後に存在するか（＝被参照列が候補キーであり続ける証明）。
    /// </summary>
    /// <remarks>
    /// 複合外部キーでは被参照列が複数になるため、列集合シグネチャ（順序・大文字小文字を無視）で照合する。
    /// 「一意制約の列集合が被参照列を包含する」では不十分（<c>(id)</c> → <c>(id, code)</c> の拡張は
    /// 4 方言中 3 方言で再 ADD が失敗する）ため、完全一致のみを証明とみなす。
    /// </remarks>
    private static bool IsCoveredByUniqueConstraint(
        Dictionary<string, HashSet<string>> postSyncUniques,
        string table,
        IEnumerable<string> columnNames
    ) =>
        postSyncUniques.TryGetValue(table, out var signatures)
        && signatures.Contains(UniqueConstraintNaming.ColumnSetSignature(columnNames));

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
    /// 対象は <see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/> が <c>true</c> の方言
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
        if (context is null || !capabilities.AlterColumnRequiresForeignKeyRebuild)
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
        var explicitlyDropped = selected
            .Where(i => i.Kind == SchemaDiffKind.DropUniqueConstraint)
            .Select(i => UniqueConstraintKey(i.TableName, i.UniqueConstraintColumns))
            .ToHashSet(StringComparer.Ordinal);

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

                if (explicitlyDropped.Contains(UniqueConstraintKey(table, columns)))
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
    /// 単純な「被参照列と完全一致するか」で判定し、実行はブロックしない（レンダラーは従来どおり DROP を出す）。
    /// </remarks>
    private static void WarnUniqueConstraintDropsBreakingForeignKeys(
        IReadOnlyList<SchemaDiffItem> selected,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings
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

        foreach (var fk in EnumerateLiveForeignKeys(context))
        {
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);
            var referencedSignature = UniqueConstraintNaming.ColumnSetSignature(
                ForeignKeyColumnPairResolver.ParentColumns(fk.ColumnPairs)
            );

            foreach (var drop in drops)
            {
                if (!TableComparer.Equals(drop.TableName.Trim(), parentTable))
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
                            parentTable
                        )
                    )
                );
            }
        }
    }

    /// <summary>一意制約の照合キー（テーブル＋列集合シグネチャ。大文字小文字・列順を無視する）</summary>
    private static string UniqueConstraintKey(string table, IEnumerable<string> columnNames) =>
        $"{table.Trim().ToLowerInvariant()}|{UniqueConstraintNaming.ColumnSetSignature(columnNames)}";

    /// <summary>テーブル・列の照合キー（大文字小文字・前後空白を無視する）</summary>
    private static string ColumnKey(string table, string column) =>
        $"{table.Trim().ToLowerInvariant()}|{column.Trim().ToLowerInvariant()}";

    /// <summary>
    /// 外部キーの照合キー（子テーブル・親テーブル・構成列ペアの宣言順リスト。
    /// 大文字小文字・前後空白を無視する）
    /// </summary>
    /// <remarks>複合外部キーも 1 本のキーへ畳むため、列ペアを宣言順に連結する</remarks>
    private static string ForeignKeyKey(
        string childTable,
        string parentTable,
        IEnumerable<ForeignKeyColumnNamePair> columnPairs
    ) =>
        $"{childTable.Trim().ToLowerInvariant()}|{parentTable.Trim().ToLowerInvariant()}|"
        + string.Join(
            ",",
            columnPairs.Select(p =>
                $"{p.ParentColumn.Trim().ToLowerInvariant()}>{p.ChildColumn.Trim().ToLowerInvariant()}"
            )
        );

    /// <summary>FK 制約名を解決する（未設定なら <c>FK_{子}_{親}</c> の規約名）</summary>
    private static string ResolveForeignKeyName(
        Relationship? relationship,
        string childTable,
        string parentTable
    ) =>
        string.IsNullOrWhiteSpace(relationship?.ConstraintName)
            ? $"FK_{SafeName(childTable)}_{SafeName(parentTable)}"
            : relationship!.ConstraintName!;

    /// <summary>live のリレーションから、親子エンティティ・構成列ペアまで解決できた FK を列挙する</summary>
    /// <remarks>
    /// 多対多や、親子・構成列が解決できないリレーションは FK として扱えないため除外する。
    /// 複合外部キーは列ペアを宣言順に保持したまま列挙する。
    /// </remarks>
    private static IEnumerable<(
        Relationship Relationship,
        Entity Parent,
        Entity Child,
        IReadOnlyList<ForeignKeyColumnNamePair> ColumnPairs
    )> EnumerateLiveForeignKeys(SyncPlanContext context)
    {
        foreach (var rel in context.LiveRelationships)
        {
            if (rel.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            var parent = context.LiveEntities.FirstOrDefault(e => e.Id == rel.SourceEntityId);
            var child = context.LiveEntities.FirstOrDefault(e => e.Id == rel.TargetEntityId);

            if (parent is null || child is null)
            {
                continue;
            }

            var columnPairs = ForeignKeyColumnPairResolver.Resolve(rel, parent, child);

            if (columnPairs is null)
            {
                continue;
            }

            yield return (rel, parent, child, columnPairs);
        }
    }

    /// <summary>
    /// rebuild 方言の実行計画を組み立てる。逐次 DDL で表現できない変更をテーブル単位の再構築へ集約し、
    /// 残り（新規テーブル対象でない列追加・テーブル削除）は従来どおりセクションへ残す。
    /// </summary>
    private static SyncPlan BuildRebuildPlan(
        List<SchemaDiffItem> selected,
        SyncPlanContext context,
        List<SyncPlanWarning> warnings
    )
    {
        // 選択済み AddTable のテーブル名（新規テーブル＝CreateOnly の対象）
        var addedTables = selected
            .Where(i => i.Kind == SchemaDiffKind.AddTable)
            .Select(i => i.TableName.Trim())
            .ToHashSet(TableComparer);

        // live のテーブル索引（合成の土台）
        var liveByName = new Dictionary<string, Entity>(TableComparer);

        foreach (var live in context.LiveEntities)
        {
            liveByName.TryAdd(SchemaDiffService.NormalizeTable(live), live);
        }

        // 既存テーブルの再構築対象（新規テーブルを子とする AddForeignKey は CreateOnly へ畳むため除外）。
        // live に実在するテーブルに限る——「新規テーブルの AddTable は未選択のまま、そのテーブルへの
        // AddForeignKey だけ選択」のような組み合わせでは合成の土台が無く、該当項目はセクションへ残して
        // レンダラーのスキップコメントに委ねる（例外にすると UI のチェック操作でクラッシュするため）
        var existingRebuildTables = selected
            .Where(i => IsRebuildTriggerKind(i.Kind) && !addedTables.Contains(i.TableName.Trim()))
            .Select(i => i.TableName.Trim())
            .Where(liveByName.ContainsKey)
            .ToHashSet(TableComparer);

        // rebuild へ転用（畳み込み）が確定した項目の集合（SchemaDiffItem は参照同一性で比較する）
        var diverted = new HashSet<SchemaDiffItem>();

        var rebuilds = new List<TableRebuildPlan>();
        rebuilds.AddRange(BuildCreateOnlyRebuilds(selected, addedTables, diverted));
        rebuilds.AddRange(
            BuildExistingTableRebuilds(
                selected,
                existingRebuildTables,
                liveByName,
                diverted,
                context
            )
        );

        // 既存テーブルの作り直し（CreateOnly=false）だけが列レベル属性を落とす。
        // 新規テーブル作成（CreateOnly=true）は落とす元の定義が無いので対象外
        WarnRebuildDropsColumnAttributes(rebuilds, context, warnings);

        // 転用済みの項目を除いた残りをセクション化する（転用できなかった項目はレンダラーがスキップを明示する）
        var sectionItems = selected.Where(i => !diverted.Contains(i)).ToList();

        return new SyncPlan
        {
            Sections = BuildSections(sectionItems),
            Rebuilds = rebuilds,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// 再構築で失われる列レベル属性を live の <c>CREATE TABLE</c> 文から検出し、テーブル単位の警告を積む。
    /// </summary>
    /// <remarks>
    /// 再構築の <c>CREATE TABLE</c> は意味モデルから組み立て直すため、モデルが持たない
    /// <c>AUTOINCREMENT</c> / <c>DEFAULT</c> / <c>CHECK</c> / <c>COLLATE</c> / 生成列は再現されない。
    /// 検出材料（<see cref="SyncPlanContext.TableCreateSql"/>）を持たない方言では 1 件も積まれない
    /// ＝逐次 DDL 方言の計画はバイト不変。
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

            if (!context.TableCreateSql.TryGetValue(rebuild.TableName.Trim(), out var createSql))
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
    private static IEnumerable<TableRebuildPlan> BuildCreateOnlyRebuilds(
        List<SchemaDiffItem> selected,
        HashSet<string> addedTables,
        HashSet<SchemaDiffItem> diverted
    )
    {
        foreach (var addTable in selected.Where(i => i.Kind == SchemaDiffKind.AddTable))
        {
            var table = addTable.TableName.Trim();
            diverted.Add(addTable);

            // その新テーブルを子とする選択済み AddForeignKey を FK 仕様として畳み込む。
            // 解決できない FK は畳まずセクションへ残し、レンダラーのスキップコメントで明示する
            var fkItems = selected
                .Where(i =>
                    i.Kind == SchemaDiffKind.AddForeignKey
                    && TableComparer.Equals(i.TableName.Trim(), table)
                )
                .ToList();
            var fks = new List<TableRebuildForeignKey>();
            var source = new List<SchemaDiffItem> { addTable };

            foreach (var fkItem in fkItems)
            {
                var resolved = ResolveAddedForeignKey(fkItem);

                if (resolved is null)
                {
                    continue;
                }

                fks.Add(resolved);
                source.Add(fkItem);
                diverted.Add(fkItem);
            }

            yield return new TableRebuildPlan
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
            };
        }
    }

    /// <summary>既存テーブルの再構築計画を組み立てる（live 定義に選択差分のみを適用して合成する）</summary>
    private static IEnumerable<TableRebuildPlan> BuildExistingTableRebuilds(
        List<SchemaDiffItem> selected,
        HashSet<string> existingRebuildTables,
        Dictionary<string, Entity> liveByName,
        HashSet<SchemaDiffItem> diverted,
        SyncPlanContext context
    )
    {
        foreach (var table in existingRebuildTables)
        {
            var live = liveByName[table];

            // このテーブルに畳み込む項目（再構築トリガー種別＋列追加）を確定する
            var tableItems = selected
                .Where(i =>
                    TableComparer.Equals(i.TableName.Trim(), table)
                    && (IsRebuildTriggerKind(i.Kind) || i.Kind == SchemaDiffKind.AddColumn)
                )
                .ToList();

            foreach (var item in tableItems)
            {
                diverted.Add(item);
            }

            // live 定義の深いコピーに、選択された差分のみを適用して合成する
            var newDef = live.Clone(preserveId: true);
            ApplySelectedColumnChanges(newDef, tableItems);

            // 主キー変更（AlterPrimaryKey）が選択されていれば、合成後の列へ target の主キー構成を反映する
            ApplySelectedPrimaryKeyChange(newDef, tableItems);

            // 列順変更（ReorderColumns）が選択されていれば、合成後の列を target の列名順へ並べ替える
            ApplySelectedReorder(newDef, tableItems);

            // 一意制約の追加・削除が選択されていれば、合成後の一意制約集合へ反映する
            ApplySelectedUniqueConstraintChanges(newDef, tableItems);

            // FK 集合 = live の子側 FK − 選択済み DropForeignKey ＋ 選択済み AddForeignKey
            var foreignKeys = SynthesizeForeignKeys(table, tableItems, context);

            // データ移送対象 = live と合成後の両方に存在する列（合成後の列順で並べる）
            var liveColumnNames = live.Columns.Select(c => c.Name).ToHashSet(TableComparer);
            var copyColumns = newDef
                .Columns.Select(c => c.Name)
                .Where(liveColumnNames.Contains)
                .ToList();

            var auxiliaryObjects = context
                .AuxiliaryObjects.Where(a => TableComparer.Equals(a.TableName.Trim(), table))
                .ToList();

            yield return new TableRebuildPlan
            {
                TableName = live.TableName,
                NewDefinition = newDef,
                ForeignKeys = foreignKeys,
                CreateOnly = false,
                CopyColumns = copyColumns,
                AuxiliaryObjects = auxiliaryObjects,
                SourceItems = tableItems,
            };
        }
    }

    /// <summary>合成後の定義へ、選択された列変更（AlterColumn 置換 / DropColumn 除去 / AddColumn 追加）を適用する</summary>
    private static void ApplySelectedColumnChanges(
        Entity newDef,
        IReadOnlyList<SchemaDiffItem> tableItems
    )
    {
        // AlterColumn: 同名の列定義を置換する
        foreach (var alter in tableItems.Where(i => i.Kind == SchemaDiffKind.AlterColumn))
        {
            var index = newDef.Columns.FindIndex(c =>
                TableComparer.Equals(c.Name, alter.ColumnName)
            );

            if (index >= 0 && alter.Column is not null)
            {
                newDef.Columns[index] = alter.Column.Clone(preserveId: true);
            }
        }

        // DropColumn: 同名の列を除去する
        foreach (var drop in tableItems.Where(i => i.Kind == SchemaDiffKind.DropColumn))
        {
            newDef.Columns.RemoveAll(c => TableComparer.Equals(c.Name, drop.ColumnName));
        }

        // AddColumn: 末尾へ畳み込む（このテーブルが他理由で再構築される場合のみここへ来る）
        foreach (var add in tableItems.Where(i => i.Kind == SchemaDiffKind.AddColumn))
        {
            if (add.Column is not null)
            {
                newDef.Columns.Add(add.Column.Clone(preserveId: true));
            }
        }
    }

    /// <summary>
    /// 選択された AlterPrimaryKey があれば、合成後の各列の主キー指定を target の構成で上書きする。
    /// </summary>
    /// <remarks>
    /// 上書きするのは <see cref="Column.IsPrimaryKey"/> のみで、列の型・NULL 許容などは他差分の選択状況に従う
    /// （未選択の AlterColumn を主キー変更のついでに適用してしまわないため）。AlterPrimaryKey が未選択なら何もしない。
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

        var pkNames = alterPk
            .Entity.Columns.Where(c => c.IsPrimaryKey)
            .Select(c => c.Name)
            .ToHashSet(TableComparer);

        foreach (var column in newDef.Columns)
        {
            column.IsPrimaryKey = pkNames.Contains(column.Name);
        }
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
        var targetOrder = new Dictionary<string, int>(TableComparer);

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
    /// 選択された一意制約の追加・削除を、合成後の定義（<paramref name="newDef"/>）の
    /// <see cref="Entity.UniqueConstraints"/> へ反映する。
    /// </summary>
    /// <remarks>
    /// 削除は列集合シグネチャの一致で除去する（制約名は live と図で食い違うため照合に使えない）。
    /// 追加は差分項目が運ぶ<b>列名</b>を合成後の列へ引き当てて新しい構成列 ID を作る
    /// （差分項目の Entity は図側＝列 ID が live のクローンと一致しないため、ID をそのまま持ち込めない）。
    /// 引き当てられない列を含む追加は黙って捨てる（DDL 生成が解決不能な制約を出力しないのと同じ流儀）。
    /// </remarks>
    private static void ApplySelectedUniqueConstraintChanges(
        Entity newDef,
        IReadOnlyList<SchemaDiffItem> tableItems
    )
    {
        foreach (var drop in tableItems.Where(i => i.Kind == SchemaDiffKind.DropUniqueConstraint))
        {
            var signature = UniqueConstraintNaming.ColumnSetSignature(drop.UniqueConstraintColumns);
            newDef.UniqueConstraints.RemoveAll(constraint =>
                UniqueConstraintNaming.TryResolveColumnNames(newDef, constraint, out var columns)
                && string.Equals(
                    UniqueConstraintNaming.ColumnSetSignature(columns),
                    signature,
                    StringComparison.Ordinal
                )
            );
        }

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
                    TableComparer.Equals(c.Name, columnName)
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

        var liveByName = new Dictionary<string, Entity>(TableComparer);

        foreach (var live in context.LiveEntities)
        {
            liveByName.TryAdd(SchemaDiffService.NormalizeTable(live), live);
        }

        var plans = new List<TableReorderPlan>();

        foreach (var item in reorderItems)
        {
            var table = item.TableName.Trim();

            // live に無いテーブル（そのテーブル自体が同期対象外）・target 不明なら並べ替えられない
            if (!liveByName.TryGetValue(table, out var live) || item.Entity is null)
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
            .Where(i => TableComparer.Equals(i.TableName.Trim(), table))
            .ToList();
        var droppedNames = tableSelected
            .Where(i => i.Kind == SchemaDiffKind.DropColumn && i.ColumnName is not null)
            .Select(i => i.ColumnName!)
            .ToHashSet(TableComparer);
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

        var effectiveSet = effective.ToHashSet(TableComparer);

        // 目標順 = target の列順のうち実効列集合に含まれるもの（＝並べ替え対象になり得る列）
        var targetOrder = target.Columns.Select(c => c.Name).Where(effectiveSet.Contains).ToList();
        var targetIndex = new Dictionary<string, int>(TableComparer);

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
            && TableComparer.Equals(i.Column.Name, name)
        );

        if (add?.Column is not null)
        {
            return add.Column;
        }

        // 選択済み AlterColumn があれば新定義で移動する（未選択の型変更は紛れ込ませないため live 定義）
        var alter = tableSelected.FirstOrDefault(i =>
            i.Kind == SchemaDiffKind.AlterColumn
            && i.Column is not null
            && TableComparer.Equals(i.Column.Name, name)
        );

        if (alter?.Column is not null)
        {
            return alter.Column;
        }

        return live.Columns.FirstOrDefault(c => TableComparer.Equals(c.Name, name))
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
    private static List<TableRebuildForeignKey> SynthesizeForeignKeys(
        string childTable,
        IReadOnlyList<SchemaDiffItem> tableItems,
        SyncPlanContext context
    )
    {
        var foreignKeys = ResolveLiveForeignKeys(childTable, context).ToList();

        // 選択済み DropForeignKey に一致する live FK を除去する（親テーブル・構成列ペアのシグネチャで照合）
        foreach (var dropFk in tableItems.Where(i => i.Kind == SchemaDiffKind.DropForeignKey))
        {
            var signature = ResolveDroppedForeignKeySignature(dropFk);

            if (signature is null)
            {
                continue;
            }

            var (parentTable, columnPairs) = signature.Value;
            foreignKeys.RemoveAll(fk =>
                TableComparer.Equals(fk.ParentTable, parentTable)
                && fk.ChildColumns.SequenceEqual(
                    ForeignKeyColumnPairResolver.ChildColumns(columnPairs),
                    TableComparer
                )
                && fk.ParentColumns.SequenceEqual(
                    ForeignKeyColumnPairResolver.ParentColumns(columnPairs),
                    TableComparer
                )
            );
        }

        // 選択済み AddForeignKey を追加する
        foreach (var addFk in tableItems.Where(i => i.Kind == SchemaDiffKind.AddForeignKey))
        {
            var resolved = ResolveAddedForeignKey(addFk);

            if (resolved is not null)
            {
                foreignKeys.Add(resolved);
            }
        }

        return foreignKeys;
    }

    /// <summary>live のリレーションから、指定した子テーブルの FK 仕様を解決する</summary>
    private static IEnumerable<TableRebuildForeignKey> ResolveLiveForeignKeys(
        string childTable,
        SyncPlanContext context
    )
    {
        foreach (var fk in EnumerateLiveForeignKeys(context))
        {
            if (!TableComparer.Equals(SchemaDiffService.NormalizeTable(fk.Child), childTable))
            {
                continue;
            }

            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            yield return new TableRebuildForeignKey(
                ResolveForeignKeyName(
                    fk.Relationship,
                    SchemaDiffService.NormalizeTable(fk.Child),
                    parentTable
                ),
                [.. ForeignKeyColumnPairResolver.ChildColumns(fk.ColumnPairs)],
                parentTable,
                [.. ForeignKeyColumnPairResolver.ParentColumns(fk.ColumnPairs)],
                fk.Relationship.OnDelete,
                fk.Relationship.OnUpdate
            );
        }
    }

    /// <summary>選択済み AddForeignKey 差分項目を解決済みの FK 仕様へ変換する（解決不能なら null）</summary>
    private static TableRebuildForeignKey? ResolveAddedForeignKey(SchemaDiffItem item)
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
            ResolveForeignKeyName(item.Relationship, childTable, parentTable),
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

    /// <summary>制約名の安全化（"." と空白を "_" へ置換。<c>SqliteIdentifier.SafeName</c> と同一規則）</summary>
    private static string SafeName(string name) =>
        (name ?? string.Empty).Replace(".", "_").Replace(" ", "_");
}
