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

    /// <summary>DB から取得した補助オブジェクト（再構築で温存するインデックス・トリガー・一意制約）。</summary>
    public IReadOnlyList<SchemaAuxiliaryObject> AuxiliaryObjects { get; init; } = [];

    /// <summary>
    /// 取込で検出した複合外部キーの警告（テーブル再構築のブロック判定に用いる）。
    /// </summary>
    /// <remarks>
    /// 複合外部キーの子テーブルを再構築すると、列対応を失った外部キーが単列外部キーとして作り直される
    /// （成功して静かに壊れる）。<see cref="SyncPlanner"/> はここに挙がったテーブルの再構築を計画から除外する。
    /// </remarks>
    public IReadOnlyList<CompositeForeignKeyImportWarning> CompositeForeignKeyWarnings { get; init; } =
    [];
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
    /// テーブル / 列の追加 → FK 解除 → <b>主キー解除</b> → 列定義変更 → <b>主キー付与</b> →
    /// 列 / テーブル削除 → FK 追加 → 説明設定の順に並べる。
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
    /// </remarks>
    private static readonly (SchemaDiffKind Kind, PrimaryKeyPhase Phase)[] SectionOrder =
    [
        (SchemaDiffKind.AddTable, PrimaryKeyPhase.None),
        (SchemaDiffKind.AddColumn, PrimaryKeyPhase.None),
        (SchemaDiffKind.DropForeignKey, PrimaryKeyPhase.None),
        (SchemaDiffKind.AlterPrimaryKey, PrimaryKeyPhase.Drop),
        (SchemaDiffKind.AlterColumn, PrimaryKeyPhase.None),
        (SchemaDiffKind.AlterPrimaryKey, PrimaryKeyPhase.Add),
        (SchemaDiffKind.DropColumn, PrimaryKeyPhase.None),
        (SchemaDiffKind.DropTable, PrimaryKeyPhase.None),
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

        // ALTER COLUMN も FK の後付けもできない方言はテーブル再構築が必要になる
        var isRebuildDialect =
            !capabilities.SupportsAlterColumn || !capabilities.SupportsForeignKeyAlter;

        if (!isRebuildDialect)
        {
            // 逐次 DDL 方言: 全差分をそのままセクション化する。
            // まず、複合外部キーの作り直しを招く変更（主キー変更・FK 関与列の定義変更）を計画から落とす。
            // 同期ダイアログでも選択不可へ格下げしているが、直接 API を使う経路・格下げ漏れに備えた最終防御
            var planned = BlockCompositeForeignKeyChanges(
                selected,
                capabilities,
                context,
                warnings
            );

            // 列順変更（ReorderColumns）は SectionOrder に無いためセクションからは自然に外れ、
            // Native 方言（MySQL）のときだけネイティブ MODIFY ... AFTER の並べ替え計画へ変換する
            // （None 方言では Compute が ReorderColumns を生成しないため、渡されても計画から消える）
            var reorders =
                capabilities.ColumnReorder == ColumnReorderMode.Native
                    ? BuildReorderPlans(planned, context)
                    : [];

            // 主キー変更・（方言によっては）列定義変更に巻き込まれる live FK を、暗黙の DROP → 再 ADD として注入する
            var sectionItems = InjectImplicitForeignKeyRebuilds(
                planned,
                capabilities,
                context,
                warnings
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

    // ---------------- 複合外部キーの作り直しを招く変更の除外（逐次 DDL 方言） ----------------

    /// <summary>
    /// 複合外部キーの自動 DROP → 再 ADD を招く変更を計画から取り除き、除外した項目ごとに警告を積む。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="InjectImplicitForeignKeyRebuilds"/> は live のリレーションを直接列挙するため、UI で外部キー差分を
    /// 選択不可へ格下げしても素通りする。その対象が複合外部キー（取込で列対応を失った外部キー）だと、
    /// 単列の定義で作り直されて<b>成功したまま制約だけが静かに壊れる</b>（MySQL）か、部分適用で外部キーが消える
    /// （Oracle）。テーブル再構築のブロック（<see cref="BlockCompositeForeignKeyRebuilds"/>）と同じ理由のため、
    /// 原因となる変更そのものを計画から落とす。
    /// </para>
    /// <para>
    /// 落とすのは該当する項目だけで、他の変更は従来どおり同期する。除外した項目は計画に入らない＝プレビューにも
    /// 現れないため、レンダラー側のスキップコメントは出さず、実行確認の警告（<paramref name="warnings"/>）で伝える。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> BlockCompositeForeignKeyChanges(
        List<SchemaDiffItem> selected,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        List<SyncPlanWarning> warnings
    )
    {
        if (context is null || context.CompositeForeignKeyWarnings.Count == 0)
        {
            return selected;
        }

        var scope = CompositeForeignKeyGuard.BuildSyncScope(context);

        if (scope.IsEmpty)
        {
            return selected;
        }

        var kept = new List<SchemaDiffItem>(selected.Count);

        foreach (var item in selected)
        {
            if (!CompositeForeignKeyGuard.IsBlockedChange(item, capabilities, scope))
            {
                kept.Add(item);
                continue;
            }

            warnings.Add(
                new SyncPlanWarning(
                    SyncPlanWarningKind.CompositeForeignKeyBlocksChange,
                    item.TableName.Trim(),
                    item.ColumnName?.Trim() ?? string.Empty
                )
            );
        }

        return kept;
    }

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
    /// 再 ADD する FK の参照先列が新しい主キー列に含まれない場合は、再作成が実行時に失敗しうるため
    /// <paramref name="warnings"/> へ <see cref="SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey"/>
    /// を積む（一意制約は取り込んでいない＝候補キーの喪失を断定できないため、実行はブロックしない）。
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

        // 明示的に DROP が選択されている FK（自動 DROP の重複も再 ADD も行わない）
        var explicitlyDropped = new HashSet<string>();

        foreach (var dropFk in selected.Where(i => i.Kind == SchemaDiffKind.DropForeignKey))
        {
            var signature = ResolveDroppedForeignKeySignature(dropFk);

            if (signature is null || dropFk.ChildEntity is null)
            {
                continue;
            }

            var (childCol, parentTable, parentCol) = signature.Value;
            explicitlyDropped.Add(
                ForeignKeyKey(
                    SchemaDiffService.NormalizeTable(dropFk.ChildEntity),
                    childCol,
                    parentTable,
                    parentCol
                )
            );
        }

        var autoDrops = new List<SchemaDiffItem>();
        var autoAdds = new List<SchemaDiffItem>();

        foreach (var fk in EnumerateLiveForeignKeys(context))
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            // (a) 参照先テーブルの主キーが変わる / (b) FK が参加する列の定義が変わる
            var parentPkChanged = pkChangedTables.TryGetValue(parentTable, out var newPkColumns);
            var affected =
                parentPkChanged
                || alteredColumns.Contains(ColumnKey(childTable, fk.ChildColumn))
                || alteredColumns.Contains(ColumnKey(parentTable, fk.ParentColumn.Name));

            if (!affected)
            {
                continue;
            }

            if (
                explicitlyDropped.Contains(
                    ForeignKeyKey(childTable, fk.ChildColumn, parentTable, fk.ParentColumn.Name)
                )
            )
            {
                continue;
            }

            var constraintName = ResolveForeignKeyName(fk.Relationship, childTable, parentTable);
            var description = string.Format(Strings.Diff_AutoForeignKeyRebuild, constraintName);

            // 参照先列が候補キーであり続けるのは「新しい主キーが被参照列 1 列ちょうど」のときだけ。
            // 注入する FK は常に単列参照のため、被参照列が新主キーに含まれていても他の列と複合になっていれば
            // 主キーは候補キーの根拠にならない（(id) → (id, code) の拡張は 4 方言中 3 方言で再 ADD が失敗する）。
            // MySQL のプレフィックス成立や UNIQUE 制約で実際には通る構成もあるが、一意制約を取り込んでいない以上
            // 断定できず、警告は実行を止めない＝安全側（誤警告を許容する側）へ倒す
            var staysCandidateKey =
                newPkColumns is { Count: 1 } && newPkColumns.Contains(fk.ParentColumn.Name);

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
                    Description = description,
                }
            );

            autoAdds.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddForeignKey,
                    TableName = childTable,
                    ColumnName = fk.ChildColumn,
                    Entity = fk.Child,
                    ParentEntity = fk.Parent,
                    ChildEntity = fk.Child,
                    Relationship = fk.Relationship,
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

    /// <summary>テーブル・列の照合キー（大文字小文字・前後空白を無視する）</summary>
    private static string ColumnKey(string table, string column) =>
        $"{table.Trim().ToLowerInvariant()}|{column.Trim().ToLowerInvariant()}";

    /// <summary>外部キーの照合キー（子テーブル・子列・親テーブル・親列。大文字小文字・前後空白を無視する）</summary>
    private static string ForeignKeyKey(
        string childTable,
        string childColumn,
        string parentTable,
        string parentColumn
    ) => $"{ColumnKey(childTable, childColumn)}|{ColumnKey(parentTable, parentColumn)}";

    /// <summary>FK 制約名を解決する（未設定なら <c>FK_{子}_{親}</c> の規約名）</summary>
    private static string ResolveForeignKeyName(
        Relationship? relationship,
        string childTable,
        string parentTable
    ) =>
        string.IsNullOrWhiteSpace(relationship?.ConstraintName)
            ? $"FK_{SafeName(childTable)}_{SafeName(parentTable)}"
            : relationship!.ConstraintName!;

    /// <summary>live のリレーションから、親子エンティティ・親列・子列名まで解決できた FK を列挙する</summary>
    /// <remarks>多対多や、親子・参照列が解決できないリレーションは FK として扱えないため除外する。</remarks>
    private static IEnumerable<(
        Relationship Relationship,
        Entity Parent,
        Entity Child,
        Column ParentColumn,
        string ChildColumn
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

            var parentCol = SchemaDiffService.ResolveReferencedColumn(rel, parent);

            if (parentCol is null)
            {
                continue;
            }

            var childCol = SchemaDiffService.ResolveFkColumnName(rel, child, parent, parentCol);

            if (childCol is null)
            {
                continue;
            }

            yield return (rel, parent, child, parentCol, childCol);
        }
    }

    /// <summary>
    /// rebuild 方言の実行計画を組み立てる。逐次 DDL で表現できない変更をテーブル単位の再構築へ集約し、
    /// 残り（新規テーブル対象でない列追加・テーブル削除）は従来どおりセクションへ残す。
    /// </summary>
    /// <remarks>
    /// 複合外部キーの子テーブルは再構築対象から除外し、警告を積む（<see cref="BlockCompositeForeignKeyRebuilds"/>）。
    /// 除外されたテーブルの差分項目は畳み込まれずセクションへ残るため、レンダラーのスキップコメントで
    /// 「同期していない」ことがプレビューにも現れる。
    /// </remarks>
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

        // 複合外部キーの子テーブルは再構築すると外部キーが単列へ作り替えられるため、そのテーブルだけ止める
        BlockCompositeForeignKeyRebuilds(existingRebuildTables, context, warnings);

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
    /// 複合外部キーの子テーブルを再構築対象から取り除き、除外したテーブルごとに警告を積む。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 意味モデルは複合外部キーの列対応を保持できないため、その子テーブルを再構築すると外部キーが
    /// 単列外部キーとして作り直される（実行は成功し、制約だけが静かに壊れる）。実行時に失敗して気づける
    /// 他の限界と違い自動検出できないため、該当テーブルの再構築だけを計画から落とす。
    /// </para>
    /// <para>
    /// ブロックの粒度は「該当テーブルのみ」で、他テーブルの同期は従来どおり続行する。除外したテーブルの
    /// 差分項目は畳み込まれずセクションへ残り、レンダラーがスキップコメントを出す。
    /// </para>
    /// </remarks>
    private static void BlockCompositeForeignKeyRebuilds(
        HashSet<string> existingRebuildTables,
        SyncPlanContext context,
        List<SyncPlanWarning> warnings
    )
    {
        if (context.CompositeForeignKeyWarnings.Count == 0)
        {
            return;
        }

        // 出力順を安定させるためテーブル名順に処理する（HashSet の列挙順に依存しない）
        var blocked = existingRebuildTables
            .Where(t =>
                CompositeForeignKeyGuard.IsCompositeChildTable(
                    t,
                    context.CompositeForeignKeyWarnings
                )
            )
            .OrderBy(t => t, TableComparer)
            .ToList();

        foreach (var table in blocked)
        {
            existingRebuildTables.Remove(table);
            warnings.Add(
                new SyncPlanWarning(SyncPlanWarningKind.RebuildBlockedByCompositeForeignKey, table)
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

        // 選択済み DropForeignKey に一致する live FK を除去する（列・親テーブル・親列のシグネチャで照合）
        foreach (var dropFk in tableItems.Where(i => i.Kind == SchemaDiffKind.DropForeignKey))
        {
            var signature = ResolveDroppedForeignKeySignature(dropFk);

            if (signature is null)
            {
                continue;
            }

            var (childCol, parentTable, parentCol) = signature.Value;
            foreignKeys.RemoveAll(fk =>
                TableComparer.Equals(fk.ChildColumn, childCol)
                && TableComparer.Equals(fk.ParentTable, parentTable)
                && TableComparer.Equals(fk.ParentColumn, parentCol)
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
                fk.ChildColumn,
                parentTable,
                fk.ParentColumn.Name,
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

        var parentCol = SyncScriptBuilderHelper.ResolveReferencedColumn(item);

        if (parentCol is null || string.IsNullOrEmpty(item.ColumnName))
        {
            return null;
        }

        var childTable = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTable = SchemaDiffService.NormalizeTable(item.ParentEntity);

        return new TableRebuildForeignKey(
            ResolveForeignKeyName(item.Relationship, childTable, parentTable),
            item.ColumnName!,
            parentTable,
            parentCol.Name,
            item.Relationship?.OnDelete ?? ForeignKeyReferentialAction.NoAction,
            item.Relationship?.OnUpdate ?? ForeignKeyReferentialAction.NoAction
        );
    }

    /// <summary>DropForeignKey 差分項目を、live FK 照合用のシグネチャ（子列・親テーブル・親列）へ解決する</summary>
    private static (
        string ChildColumn,
        string ParentTable,
        string ParentColumn
    )? ResolveDroppedForeignKeySignature(SchemaDiffItem item)
    {
        if (item.Relationship is null || item.ParentEntity is null || item.ChildEntity is null)
        {
            return null;
        }

        var parentCol = SchemaDiffService.ResolveReferencedColumn(
            item.Relationship,
            item.ParentEntity
        );

        if (parentCol is null)
        {
            return null;
        }

        var childCol = SchemaDiffService.ResolveFkColumnName(
            item.Relationship,
            item.ChildEntity,
            item.ParentEntity,
            parentCol
        );

        if (childCol is null)
        {
            return null;
        }

        return (childCol, SchemaDiffService.NormalizeTable(item.ParentEntity), parentCol.Name);
    }

    /// <summary>この種別が既存テーブルの再構築を要求するか（列型変更・主キー変更・列削除・FK 変更・列順変更）</summary>
    /// <remarks>
    /// rebuild 方言（SQLite）では列順変更・主キー変更もテーブル再構築で実現するため、ReorderColumns /
    /// AlterPrimaryKey も再構築トリガーに含める（それだけが選択された場合も CreateOnly=false の再構築になる）。
    /// </remarks>
    private static bool IsRebuildTriggerKind(SchemaDiffKind kind) =>
        kind
            is SchemaDiffKind.AlterColumn
                or SchemaDiffKind.AlterPrimaryKey
                or SchemaDiffKind.DropColumn
                or SchemaDiffKind.DropForeignKey
                or SchemaDiffKind.AddForeignKey
                or SchemaDiffKind.ReorderColumns;

    /// <summary>制約名の安全化（"." と空白を "_" へ置換。<c>SqliteIdentifier.SafeName</c> と同一規則）</summary>
    private static string SafeName(string name) =>
        (name ?? string.Empty).Replace(".", "_").Replace(" ", "_");
}
