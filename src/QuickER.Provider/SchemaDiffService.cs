using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.Resources;

namespace QuickER.Provider;

/// <summary>既存 DB スキーマと現在のダイアグラムを比較して <see cref="SchemaDiff"/> を生成するサービス</summary>
/// <remarks>
/// <para>リネームは扱わず「同名 = 同一」を前提とする 名称が変われば削除＋追加として検出する</para>
/// <para>
/// 計算列（<see cref="Column.IsComputed"/>）だけは <b>NULL 許容の差を見ない</b>。実 DB の計算列の NULL 許容は
/// 式から決まる性質で、図がそれを DB へ強制することはできない（意味モデルは式を持たない）。しかも
/// 「NOT NULL の計算列はミラー側の DB へ書けない」問題の逃げ道が<b>図で NULL 許容にする</b>ことなので、
/// 比較すると逃げ道を採った瞬間に取込元の DB へ偽の <c>ALTER COLUMN</c>（NOT NULL → NULL）が出る。
/// 図・DB のどちらか一方だけが計算列の場合も同じ。型の差は検出するが、意味モデルは式を持たないので、式を保ったまま型を
/// 変えられる方言（<see cref="SyncDialectCapabilities.SupportsComputedColumnTypeChange"/>）でだけ選べる項目にし、
/// ほかの方言では選択できない案内項目にする（項目は NULL 許容を運ばない）。
/// </para>
/// <para>
/// 図の主キーと同じ列集合の一意制約の追加は、それを別の制約として持てない方言
/// （<see cref="SyncDialectCapabilities.SupportsUniqueConstraintOnPrimaryKey"/> が <c>false</c>）では選択できない案内項目にする。
/// </para>
/// </remarks>
public class SchemaDiffService
{
    /// <summary>DB 現状とダイアグラムの目標状態を突き合わせて差分項目を計算する</summary>
    /// <param name="liveEntities">DB から取得した現在のエンティティ</param>
    /// <param name="liveRelationships">DB から取得した現在のリレーション</param>
    /// <param name="targetEntities">ダイアグラム上のエンティティ（期待状態）</param>
    /// <param name="targetRelationships">ダイアグラム上のリレーション</param>
    /// <param name="capabilities">
    /// 対象方言の同期ケーパビリティ（省略できない＝方言を決めずに比べる経路を持たない）。
    /// <see cref="SyncDialectCapabilities.SupportsDescriptions"/> が <c>false</c> の方言（SQLite）では
    /// 説明差分を生成しない（コメント機構が無く live 側が常に空＝恒常的な幻の差分になるため）。
    /// <see cref="SyncDialectCapabilities.PersistsForeignKeyConstraintNames"/> が <c>false</c> の方言（SQLite）では
    /// FK シグネチャから制約名を除いて比較する（合成名 live と無名 target で恒常的な Drop+Add 誤検出を避けるため）。
    /// 型の比較は <see cref="SyncDialectCapabilities.AreSameType"/>（両側を方言の取込表記へ寄せてから比べる）、
    /// 外部キーの参照アクションは <see cref="SyncDialectCapabilities.EffectiveOnDelete"/> /
    /// <see cref="SyncDialectCapabilities.EffectiveOnUpdate"/>（方言の実効値）で比べる。
    /// 説明は方言が保存する形（<see cref="SyncDialectCapabilities.IdentifierLengthLimit"/> の
    /// <see cref="QuickER.Dialects.IdentifierLengthLimit.StoredText"/>＝MySQL では BMP の外の文字を <c>?</c> にした形）で比べる。
    /// </param>
    public SchemaDiff Compute(
        IReadOnlyList<Entity> liveEntities,
        IReadOnlyList<Relationship> liveRelationships,
        IReadOnlyList<Entity> targetEntities,
        IReadOnlyList<Relationship> targetRelationships,
        SyncDialectCapabilities capabilities
    )
    {
        var diff = new SchemaDiff();

        // 説明差分を出すか（SQLite などコメント機構が無い方言では出さない）
        var emitDescriptions = capabilities.SupportsDescriptions;

        // 説明は方言が保存する形で比べる（MySQL は BMP の外の文字を ? に置き換えて保存するので、図の絵文字と DB の ? を同じとみなす）
        var descriptionLimit = capabilities.IdentifierLengthLimit;

        bool AreSameStoredDescription(string target, string live) =>
            string.Equals(
                descriptionLimit.StoredText(target),
                descriptionLimit.StoredText(live),
                StringComparison.Ordinal
            );

        // FK 比較で制約名を含めるか（SQLite は合成名で永続化されないため名前を除いて比較する）
        var includeFkConstraintName = capabilities.PersistsForeignKeyConstraintNames;

        // 型表記の正規化と参照アクションの畳み込み
        var dialect = capabilities;

        // 表と列の組を 1 回だけ求める（規則は NameLookup＝同じ名前かは SchemaNames が決め、同じとみなす相手が片側に
        // 複数あるときは綴りまで一致する 1 件を選ぶ。選べなければ組まず、図側は追加・DB 側は削除の候補になる）。
        // 組になる live と大文字小文字だけ違う図側の表・列は、live の綴りへ揃えた複製で差分を組む
        var matching = MatchSchemas(targetEntities, liveEntities);
        targetEntities = matching.Targets;
        var livePartnerOf = matching.LivePartnerOf;
        var matchedLive = livePartnerOf.Values.ToHashSet();

        // ---------- テーブル/カラムの差分 ----------
        foreach (var target in targetEntities)
        {
            var name = NormalizeTable(target);

            if (!livePartnerOf.TryGetValue(target, out var live))
            {
                // DB に存在しないテーブルは新規作成として扱う
                diff.Items.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.AddTable,
                        TableName = name,
                        Entity = target,
                        Description = string.Format(
                            Strings.Diff_AddTable,
                            name,
                            target.Columns.Count
                        ),
                    }
                );

                // 新規テーブル: テーブル説明
                var newTblDesc = DescriptionText.Normalize(target.Description);

                if (emitDescriptions && DescriptionText.IsPresent(newTblDesc))
                {
                    diff.Items.Add(
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.SetTableDescription,
                            TableName = name,
                            Entity = target,
                            NewDescription = newTblDesc,
                            OldDescription = null,
                            Description = string.Format(
                                Strings.Diff_SetTableDescription,
                                name,
                                Truncate(newTblDesc)
                            ),
                        }
                    );
                }

                // 新規テーブル: 各列の説明
                foreach (var c in target.Columns)
                {
                    if (!emitDescriptions || !DescriptionText.IsPresent(c.Description))
                    {
                        continue;
                    }

                    diff.Items.Add(
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.SetColumnDescription,
                            TableName = name,
                            ColumnName = c.Name,
                            Entity = target,
                            Column = c,
                            NewDescription = c.Description,
                            OldDescription = null,
                            Description = string.Format(
                                Strings.Diff_SetColumnDescription,
                                name,
                                c.Name,
                                Truncate(c.Description)
                            ),
                        }
                    );
                }

                continue;
            }

            // 既存テーブル: カラム差分（列の組もテーブルと同じ規則・組は MatchSchemas が求めたもの）
            var liveColumnOf = matching.LiveColumnOf[target];
            var matchedLiveColumns = liveColumnOf.Values.ToHashSet();

            // テーブル説明の差分
            var targetTableDesc = DescriptionText.Normalize(target.Description);
            var liveTableDesc = DescriptionText.Normalize(live.Description);

            if (emitDescriptions && !AreSameStoredDescription(targetTableDesc, liveTableDesc))
            {
                diff.Items.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.SetTableDescription,
                        TableName = name,
                        Entity = target,
                        NewDescription = targetTableDesc,
                        OldDescription = liveTableDesc,
                        Description = !DescriptionText.IsPresent(targetTableDesc)
                            ? string.Format(Strings.Diff_RemoveTableDescription, name)
                            : string.Format(
                                Strings.Diff_UpdateTableDescription,
                                name,
                                Truncate(targetTableDesc)
                            ),
                    }
                );
            }

            foreach (var tcol in target.Columns)
            {
                var cname = tcol.Name;

                if (!liveColumnOf.TryGetValue(tcol, out var lcol))
                {
                    diff.Items.Add(
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddColumn,
                            TableName = name,
                            ColumnName = cname,
                            Entity = target,
                            Column = tcol,
                            Description = string.Format(
                                Strings.Diff_AddColumn,
                                name,
                                cname,
                                tcol.DataType
                            ),
                        }
                    );

                    // 新規列に説明があれば、列追加と一緒に説明を設定する
                    if (emitDescriptions && DescriptionText.IsPresent(tcol.Description))
                    {
                        diff.Items.Add(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.SetColumnDescription,
                                TableName = name,
                                ColumnName = cname,
                                Entity = target,
                                Column = tcol,
                                NewDescription = tcol.Description,
                                OldDescription = null,
                                Description = string.Format(
                                    Strings.Diff_SetColumnDescription,
                                    name,
                                    cname,
                                    Truncate(tcol.Description)
                                ),
                            }
                        );
                    }
                }
                else
                {
                    // 計算列の NULL 許容は図が DB へ強制できる性質ではない（実 DB の NULL 許容は式が決める）。
                    // 比較すると、NOT NULL の計算列を図側で NULL 許容へ逃がした瞬間に取込元の DB へ
                    // 「NOT NULL → NULL」の偽 AlterColumn が出る。DB 側だけが計算列の場合も同じ
                    var computed = tcol.IsComputed || lcol.IsComputed;
                    var compareNullability = !computed;
                    // NULL 許容は実効の値で比べる（主キーの列はモデルの値に依らず NOT NULL）
                    var nullabilityChanged =
                        compareNullability
                        && IsEffectivelyNullable(lcol) != IsEffectivelyNullable(tcol);

                    var typeChanged = !dialect.AreSameType(lcol.DataType, tcol.DataType);

                    if (computed && typeChanged)
                    {
                        diff.Items.Add(
                            BuildComputedColumnTypeChange(name, target, tcol, lcol, dialect)
                        );
                    }
                    else if (typeChanged || nullabilityChanged)
                    {
                        var changeParts = new List<string>();

                        if (typeChanged)
                        {
                            changeParts.Add(
                                string.Format(Strings.Diff_TypeChange, lcol.DataType, tcol.DataType)
                            );
                        }

                        if (nullabilityChanged)
                        {
                            changeParts.Add(
                                string.Format(
                                    Strings.Diff_NullableChange,
                                    NullableLabel(IsEffectivelyNullable(lcol)),
                                    NullableLabel(IsEffectivelyNullable(tcol))
                                )
                            );
                        }

                        diff.Items.Add(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AlterColumn,
                                TableName = name,
                                ColumnName = cname,
                                Entity = target,
                                Column = tcol,
                                OldColumn = lcol,
                                IsSelected = false,
                                Description =
                                    string.Format(Strings.Diff_ColumnChangePrefix, name, cname)
                                    + string.Join(" / ", changeParts),
                            }
                        );
                    }

                    var newColDesc = DescriptionText.Normalize(tcol.Description);
                    var oldColDesc = DescriptionText.Normalize(lcol.Description);

                    if (emitDescriptions && !AreSameStoredDescription(newColDesc, oldColDesc))
                    {
                        diff.Items.Add(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.SetColumnDescription,
                                TableName = name,
                                ColumnName = cname,
                                Entity = target,
                                Column = tcol,
                                NewDescription = newColDesc,
                                OldDescription = oldColDesc,
                                Description = !DescriptionText.IsPresent(newColDesc)
                                    ? string.Format(
                                        Strings.Diff_RemoveColumnDescription,
                                        name,
                                        cname
                                    )
                                    : string.Format(
                                        Strings.Diff_UpdateColumnDescription,
                                        name,
                                        cname,
                                        Truncate(newColDesc)
                                    ),
                            }
                        );
                    }
                }
            }

            // 主キー構成の差分（テーブル単位で 1 項目・既定では未選択）
            var livePk = PrimaryKeyColumnNames(live);
            var targetPk = PrimaryKeyColumnNames(target);

            if (!IsSamePrimaryKey(livePk, targetPk))
            {
                diff.Items.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.AlterPrimaryKey,
                        TableName = name,
                        // 新しい主キー構成の源は target 側エンティティ（列順・PK フラグをそのまま参照する）
                        Entity = target,
                        IsSelected = false,
                        Description = string.Format(
                            Strings.Diff_AlterPrimaryKey,
                            name,
                            FormatPrimaryKey(livePk),
                            FormatPrimaryKey(targetPk)
                        ),
                    }
                );
            }

            // 一意制約の差分（列集合で照合し、制約名の差だけでは差分にしない）。
            // 図側は制約名が未設定（null＝合成名）なことが多く、SQLite に至っては実名を持たないため、
            // 名前を比較に含めると恒常的な Drop＋Add の誤検出になる。
            AppendUniqueConstraintDiffs(diff, name, live, target, dialect);

            foreach (var lcol in live.Columns)
            {
                var cname = lcol.Name;

                if (!matchedLiveColumns.Contains(lcol))
                {
                    diff.Items.Add(
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.DropColumn,
                            TableName = name,
                            ColumnName = cname,
                            Entity = live,
                            Column = lcol,
                            IsSelected = false,
                            Description = string.Format(
                                Strings.Diff_DropColumn,
                                name,
                                cname,
                                lcol.DataType
                            ),
                        }
                    );
                }
            }
        }

        foreach (var live in liveEntities)
        {
            var name = NormalizeTable(live);

            if (!matchedLive.Contains(live))
            {
                diff.Items.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.DropTable,
                        TableName = name,
                        Entity = live,
                        IsSelected = false,
                        Description = string.Format(Strings.Diff_DropTable, name),
                    }
                );
            }
        }

        // ---------- 外部キーの差分 ----------
        // 親・子・列・制約名などのシグネチャでキー化し、同一 FK の有無を集合比較で判定する。
        // 追加候補・削除候補はいったん集め、同じ外部キーの作り直しにあたる組を 1 項目へ畳んでから差分へ並べる
        var fkAdds = new List<SchemaDiffItem>();
        var fkDrops = new List<SchemaDiffItem>();

        // 外部キー名は DDL・同期スクリプトが付けるのと同じ規則で求める（名前の無い同じ組の 2 本目は一意化される。
        // 既定名は同期の方言の上限で縮める）
        var liveFkNames = ConstraintNames.ResolveForeignKeyNames(
            liveEntities,
            liveRelationships,
            dialect.IdentifierLengthLimit
        );
        var targetFkNames = ResolveTargetForeignKeyNames(
            targetEntities,
            targetRelationships,
            liveEntities,
            liveFkNames,
            includeFkConstraintName,
            dialect
        );
        // 外部キーとして扱えるリレーションを両側で 1 回ずつ解決し、シグネチャも 1 回ずつ求める
        var liveFks = ForeignKeyColumnPairResolver
            .ResolveAll(liveRelationships, liveEntities)
            .Select(fk =>
                (
                    Fk: fk,
                    Signature: ForeignKeySignature(
                        fk,
                        liveFkNames,
                        includeFkConstraintName,
                        dialect
                    )
                )
            )
            .ToList();
        var targetFks = ForeignKeyColumnPairResolver
            .ResolveAll(targetRelationships, targetEntities)
            .Select(fk =>
                (
                    Fk: fk,
                    Signature: ForeignKeySignature(
                        fk,
                        targetFkNames,
                        includeFkConstraintName,
                        dialect
                    )
                )
            )
            .ToList();
        var liveFkSignatures = liveFks.Select(fk => fk.Signature).ToHashSet();
        var targetFkSignatures = targetFks.Select(fk => fk.Signature).ToHashSet();

        // 図にあるが DB に無い外部キーは追加候補
        foreach (var (fk, signature) in targetFks)
        {
            if (liveFkSignatures.Contains(signature))
            {
                continue;
            }

            var (rel, parent, child, columnPairs) = fk;
            fkAdds.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddForeignKey,
                    TableName = NormalizeTable(child),
                    // 表示・照合の互換のため先頭ペアの子列名を入れる（構成列の正本は ForeignKeyColumnPairs）
                    ColumnName = columnPairs[0].ChildColumn,
                    Entity = child,
                    ParentEntity = parent,
                    ChildEntity = child,
                    Relationship = rel,
                    // 同期スクリプトは DDL と同じ名前で付ける（名前の無い同じ組の 2 本目は一意化した名前）
                    ForeignKeyName = targetFkNames[rel],
                    ForeignKeyColumnPairs = columnPairs,
                    Description = string.Format(
                        Strings.Diff_AddForeignKey,
                        NormalizeTable(child),
                        FormatColumnList(ForeignKeyColumnPairResolver.ChildColumns(columnPairs)),
                        NormalizeTable(parent),
                        FormatColumnList(ForeignKeyColumnPairResolver.ParentColumns(columnPairs))
                    ),
                }
            );
        }

        // DB にあるが ER 図にない FK は削除候補とする（同じシグネチャの外部キーが図に無いもの。
        // 名前を保存する方言では名前もシグネチャに入るため、名前だけ変えた外部キーも削除＋追加になる）
        foreach (var (fk, signature) in liveFks)
        {
            if (targetFkSignatures.Contains(signature))
            {
                continue;
            }

            var (rel, parent, child, columnPairs) = fk;
            fkDrops.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropForeignKey,
                    TableName = NormalizeTable(child),
                    Entity = child,
                    ParentEntity = parent,
                    ChildEntity = child,
                    Relationship = rel,
                    ForeignKeyName = rel.ConstraintName,
                    ForeignKeyColumnPairs = columnPairs,
                    IsSelected = false,
                    Description = string.Format(
                        Strings.Diff_DropForeignKey,
                        NormalizeTable(child),
                        NormalizeTable(parent)
                    ),
                }
            );
        }

        diff.Items.AddRange(
            PairForeignKeyAlters(fkAdds, fkDrops, includeFkConstraintName, dialect)
        );

        // ---------- 列順の差分（対応方言のみ・既定では非選択） ----------
        // 列順同期は SQLite（テーブル再構築）と MySQL（ネイティブ MODIFY ... AFTER）だけが実現できる。
        // 非対応方言（ColumnReorder=None）では幻の差分を出さない（案内表示は UI 側が担う）。
        if (capabilities.ColumnReorder != ColumnReorderMode.None)
        {
            foreach (var (target, live) in livePartnerOf)
            {
                if (!HasColumnOrderChanged(live, target, matching.LiveColumnOf[target]))
                {
                    continue;
                }

                var tableName = NormalizeTable(target);
                diff.Items.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.ReorderColumns,
                        TableName = tableName,
                        Entity = target,
                        IsSelected = false,
                        Description = string.Format(Strings.Diff_ReorderColumns, tableName),
                    }
                );
            }
        }

        return diff;
    }

    /// <summary>計算列の型の差分の項目を作る（NULL 許容は運ばない・方言が式を保って変えられなければ選択できない案内項目）</summary>
    /// <remarks>
    /// 項目の <see cref="SchemaDiffItem.Column"/> は図の列の複製で、NULL 許容だけを DB の値に戻す＝選んでも計画は
    /// NULL 許容を変えない（以前は図の NULL 許容まで適用し、説明に出ないまま NOT NULL が外れた）。
    /// <see cref="SyncDialectCapabilities.SupportsComputedColumnTypeChange"/> が <c>false</c> の方言では、
    /// 式を書けないので実行すると失敗するか式が消える＝選択できない案内項目にする。
    /// </remarks>
    private static SchemaDiffItem BuildComputedColumnTypeChange(
        string tableName,
        Entity target,
        Column targetColumn,
        Column liveColumn,
        SyncDialectCapabilities dialect
    )
    {
        var column = targetColumn.Clone(preserveId: true);
        column.IsNullable = liveColumn.IsNullable;
        var selectable = dialect.SupportsComputedColumnTypeChange;

        return new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = tableName,
            ColumnName = targetColumn.Name,
            Entity = target,
            Column = column,
            OldColumn = liveColumn,
            IsSelected = false,
            IsSelectable = selectable,
            Description = selectable
                ? string.Format(Strings.Diff_ColumnChangePrefix, tableName, targetColumn.Name)
                    + string.Format(
                        Strings.Diff_TypeChange,
                        liveColumn.DataType,
                        targetColumn.DataType
                    )
                : string.Format(
                    Strings.Diff_ComputedColumnTypeNotSynced,
                    tableName,
                    targetColumn.Name,
                    liveColumn.DataType,
                    targetColumn.DataType
                ),
        };
    }

    /// <summary>1 テーブル分の一意制約差分（追加・削除）を <paramref name="diff"/> へ追加する</summary>
    /// <remarks>
    /// <para>
    /// 照合は <see cref="UniqueConstraintNaming.ColumnSetSignature"/>（大文字小文字・順序を無視した列集合）で行う。
    /// target 側にだけ在る組は <see cref="SchemaDiffKind.AddUniqueConstraint"/>（既定で選択）、live 側にだけ在る組は
    /// <see cref="SchemaDiffKind.DropUniqueConstraint"/>（既定で未選択＝破壊的）になる。
    /// </para>
    /// <para>
    /// 構成列が空・解決不能な制約は差分対象から除外する（DDL 生成が黙って出力しないのと同じ扱い）。
    /// </para>
    /// <para>
    /// 図の主キーと同じ列集合の一意制約は、<see cref="SyncDialectCapabilities.SupportsUniqueConstraintOnPrimaryKey"/> が
    /// <c>false</c> の方言では追加の比較から外す（項目にしない。主キーが一意性を保証するので、図と DB は一意性について
    /// 食い違っていない。SQLite は冗長な UNIQUE の索引を作らず取り込みにも現れない＝項目にすると差分が永久に消えない。
    /// Oracle は ORA-02261 で断る）。図側の署名には残すので、DB に同じ列集合の一意制約があっても削除の項目は出さない。
    /// DDL と同期の <c>CREATE TABLE</c> も同じ方言で省いて注記する（<see cref="UniqueConstraintOnPrimaryKeyNote"/>）。
    /// </para>
    /// <para>
    /// 同じ実効名（追加側は図の名前か合成名、削除側は DB の実名）の追加候補と削除候補は、
    /// <see cref="SchemaDiffKind.AlterUniqueConstraint"/>（既定で未選択）の 1 項目へ畳む。2 項目のままだと、既定どおり
    /// 追加だけを選んで実行したときに同じ名前の制約を足そうとして失敗する。DB 側に名前が無い制約（SQLite の自動名）は
    /// 組まない。名前の照合は前後の空白を除き大文字小文字を区別しない。
    /// </para>
    /// </remarks>
    private static void AppendUniqueConstraintDiffs(
        SchemaDiff diff,
        string tableName,
        Entity live,
        Entity target,
        SyncDialectCapabilities dialect
    )
    {
        var liveConstraints = ResolveUniqueConstraints(live);
        var targetConstraints = ResolveUniqueConstraints(target);

        if (liveConstraints.Count == 0 && targetConstraints.Count == 0)
        {
            return;
        }

        var liveSignatures = liveConstraints
            .Select(c => c.Signature)
            .ToHashSet(StringComparer.Ordinal);
        var targetSignatures = targetConstraints
            .Select(c => c.Signature)
            .ToHashSet(StringComparer.Ordinal);

        // 図の主キーと同じ列集合の一意制約を、別の制約として持てない方言（SQLite・Oracle）がある
        var targetKey = target.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList();
        var targetKeySignature =
            targetKey.Count == 0 ? null : UniqueConstraintNaming.ColumnSetSignature(targetKey);

        var adds = new List<SchemaDiffItem>();
        var drops = new List<SchemaDiffItem>();

        foreach (var constraint in targetConstraints)
        {
            if (liveSignatures.Contains(constraint.Signature))
            {
                continue;
            }

            // 主キーと同じ列集合の一意制約を持てない方言では、主キーが一意性を保証するので比較から外す
            if (
                !dialect.SupportsUniqueConstraintOnPrimaryKey
                && string.Equals(constraint.Signature, targetKeySignature, StringComparison.Ordinal)
            )
            {
                continue;
            }

            var columns = string.Join(", ", constraint.ColumnNames);

            adds.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddUniqueConstraint,
                    TableName = tableName,
                    Entity = target,
                    // 図側の名前は未設定（null）でよい＝レンダラーが UQ_{表}_{列…} を合成する
                    UniqueConstraintName = constraint.Name,
                    UniqueConstraintColumns = constraint.ColumnNames,
                    IsSelected = true,
                    Description = string.Format(
                        Strings.Diff_AddUniqueConstraint,
                        tableName,
                        columns
                    ),
                }
            );
        }

        foreach (var constraint in liveConstraints)
        {
            if (targetSignatures.Contains(constraint.Signature))
            {
                continue;
            }

            drops.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropUniqueConstraint,
                    TableName = tableName,
                    Entity = live,
                    // DROP には DB 側の実名が要る（4 逐次方言は取込済み・SQLite は再構築へ畳まれる）
                    UniqueConstraintName = constraint.Name,
                    UniqueConstraintColumns = constraint.ColumnNames,
                    IsSelected = false,
                    Description = string.Format(
                        Strings.Diff_DropUniqueConstraint,
                        tableName,
                        string.Join(", ", constraint.ColumnNames)
                    ),
                }
            );
        }

        diff.Items.AddRange(
            PairUniqueConstraintAlters(tableName, adds, drops, dialect.IdentifierLengthLimit)
        );
    }

    /// <summary>
    /// 同じ実効名の一意制約の追加候補と削除候補を、1 つの <see cref="SchemaDiffKind.AlterUniqueConstraint"/> へ畳む
    /// （規則は <see cref="AppendUniqueConstraintDiffs"/> の remarks）。
    /// </summary>
    /// <returns>差分へ並べる順の一意制約項目（畳んだ項目は追加候補の位置に入り、残りの削除候補が後に続く）</returns>
    private static List<SchemaDiffItem> PairUniqueConstraintAlters(
        string tableName,
        List<SchemaDiffItem> adds,
        List<SchemaDiffItem> drops,
        IdentifierLengthLimit limit
    )
    {
        var remainingDrops = new List<SchemaDiffItem>(drops);
        var result = new List<SchemaDiffItem>();

        foreach (var add in adds)
        {
            var name = UniqueConstraintNaming.Resolve(
                add.UniqueConstraintName,
                tableName,
                add.UniqueConstraintColumns,
                limit
            );
            var drop = remainingDrops.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.UniqueConstraintName)
                && NameKey(d.UniqueConstraintName) == NameKey(name)
            );

            if (drop is null)
            {
                result.Add(add);
                continue;
            }

            remainingDrops.Remove(drop);
            result.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterUniqueConstraint,
                    TableName = tableName,
                    Entity = add.Entity,
                    UniqueConstraintName = add.UniqueConstraintName,
                    UniqueConstraintColumns = add.UniqueConstraintColumns,
                    ReplacedUniqueConstraint = drop,
                    IsSelected = false,
                    Description = string.Format(
                        Strings.Diff_AlterUniqueConstraint,
                        tableName,
                        name,
                        string.Join(", ", drop.UniqueConstraintColumns),
                        string.Join(", ", add.UniqueConstraintColumns)
                    ),
                }
            );
        }

        result.AddRange(remainingDrops);
        return result;
    }

    /// <summary>エンティティの一意制約を「制約名・構成列名・照合シグネチャ」へ解決する（解決不能な制約は除外）</summary>
    private static List<(
        string? Name,
        List<string> ColumnNames,
        string Signature
    )> ResolveUniqueConstraints(Entity entity)
    {
        var resolved = new List<(string?, List<string>, string)>();

        foreach (var constraint in entity.UniqueConstraints)
        {
            if (!UniqueConstraintNaming.TryResolveColumnNames(entity, constraint, out var columns))
            {
                continue;
            }

            resolved.Add(
                (constraint.Name, columns, UniqueConstraintNaming.ColumnSetSignature(columns))
            );
        }

        return resolved;
    }

    /// <summary>共通列（追加・削除を除いた双方に存在する列）の相対順序が異なるテーブル名の一覧を返す</summary>
    /// <remarks>
    /// 列の追加・削除は無視し、live と target の両方に存在する列だけを取り出してその相対順序を比較する。
    /// これにより「真ん中への列追加」と「並び替え」が同時に起きても、共通列の順序変化として検知できる
    /// （純粋な列追加のみ＝共通列の相対順序が変わらないケースは検知しない）。共通列が 2 列未満のときは検知しない。
    /// </remarks>
    public static IReadOnlyList<string> DetectColumnOrderChanges(
        IReadOnlyList<Entity> liveEntities,
        IReadOnlyList<Entity> targetEntities
    )
    {
        var matching = new SchemaDiffService().MatchSchemas(targetEntities, liveEntities);

        // 表名は、ほかの差分項目と同じく DB の綴りで返す（差分は組になった図の表を DB の綴りへ揃えて組む）
        return matching
            .LivePartnerOf.Where(pair =>
                HasColumnOrderChanged(pair.Value, pair.Key, matching.LiveColumnOf[pair.Key])
            )
            .Select(pair => NormalizeTable(pair.Value))
            .ToList();
    }

    /// <summary>表と列を組んだ結果（<see cref="MatchSchemas"/>）</summary>
    /// <param name="Targets">差分を組む図側の表（組になった相手と綴りが違う表は、DB の綴りへ揃えた複製）</param>
    /// <param name="LivePartnerOf">図側の表（<paramref name="Targets"/> の要素）→ 組になる DB 側の表</param>
    /// <param name="LiveColumnOf">組になった図側の表 → その列 → 組になる DB 側の列</param>
    private sealed record SchemaMatching(
        IReadOnlyList<Entity> Targets,
        Dictionary<Entity, Entity> LivePartnerOf,
        Dictionary<Entity, Dictionary<Column, Column>> LiveColumnOf
    );

    /// <summary>照合の回数（表の組み合わせ・表ごとの列の組み合わせ。テストの観測点）</summary>
    /// <remarks>
    /// <see cref="Compute"/> 1 回につき、表の組み合わせは 1 回、組になった表ごとの列の組み合わせは 1 回だけ行う
    /// （揃えた複製・列の差分・列順の判定が同じ組を使う）。<c>SchemaDiffServiceMatchingCountTests</c> が固定する。
    /// </remarks>
    internal int TableMatchings { get; private set; }

    /// <inheritdoc cref="TableMatchings"/>
    internal int ColumnMatchings { get; private set; }

    /// <summary>
    /// 図側と DB 側の表・列を 1 回だけ組み、組になった DB 側と大文字小文字だけ綴りが違う図側の表・列を、
    /// DB 側の綴りへ揃えた複製に差し替える
    /// </summary>
    /// <remarks>
    /// <para>
    /// 組の規則は <see cref="NameLookup{T}"/>（同じ名前かは <see cref="SchemaNames"/> が決める）。
    /// 差分は表・列を大文字小文字を無視して組むが、スクリプトは図側の綴りを書く。
    /// 引用識別子で綴りを区別する方言では、それでは組んだ相手を指せない（Oracle は引用符なしで作られた表を
    /// 大文字で持つので、図の <c>"Orders"</c> では表 <c>ORDERS</c> に届かず ORA-00942。PostgreSQL は小文字で持つ）。
    /// 組になった表・列だけを DB 側の綴りへ揃えれば、項目・計画・スクリプトのすべてが DB に実在する名前を書く。
    /// </para>
    /// <para>
    /// 揃えるのは組になった表・列だけで、図にしか無い表（AddTable）・列（AddColumn）は図の綴りのまま作る。
    /// 図そのものは書き換えない（Id を保った複製＝リレーション・一意制約・主キーの順序の参照はそのまま通る）。
    /// 揃える組が 1 つも無い表は元のオブジェクトのまま返す（出力はバイト不変）。複製の列の組は、組み直さずに
    /// 元の列と同じ Id の列へ写す。
    /// </para>
    /// <para>
    /// 綴りが違うかの判定は序数で比べる（同一性の判定ではなく、SchemaNames が同じとみなす相手と綴りまで一致するかを
    /// 見る判定。同一性は SchemaNames が決める）。
    /// </para>
    /// </remarks>
    private SchemaMatching MatchSchemas(
        IReadOnlyList<Entity> targetEntities,
        IReadOnlyList<Entity> liveEntities
    )
    {
        TableMatchings++;
        var tablePairs = NameMatching.Match(
            targetEntities,
            liveEntities,
            NormalizeTable,
            NormalizeTable
        );
        var targets = targetEntities.ToList();
        var indexOf = new Dictionary<Entity, int>(ReferenceEqualityComparer.Instance);

        for (var i = 0; i < targets.Count; i++)
        {
            indexOf[targets[i]] = i;
        }

        var livePartnerOf = new Dictionary<Entity, Entity>();
        var liveColumnOf = new Dictionary<Entity, Dictionary<Column, Column>>();

        foreach (var (target, live) in tablePairs)
        {
            ColumnMatchings++;
            var columnPairs = NameMatching
                .Match(target.Columns, live.Columns, column => column.Name, column => column.Name)
                .ToDictionary(pair => pair.Left, pair => pair.Right);
            var renameTable = !string.Equals(
                NormalizeTable(target),
                NormalizeTable(live),
                StringComparison.Ordinal
            );
            var renameColumns = columnPairs.Any(pair =>
                !string.Equals(pair.Key.Name, pair.Value.Name, StringComparison.Ordinal)
            );

            if (!renameTable && !renameColumns)
            {
                livePartnerOf[target] = live;
                liveColumnOf[target] = columnPairs;
                continue;
            }

            var clone = target.Clone(preserveId: true);

            if (renameTable)
            {
                clone.TableName = live.TableName;
            }

            var liveColumnById = columnPairs.ToDictionary(pair => pair.Key.Id, pair => pair.Value);
            var cloneColumnPairs = new Dictionary<Column, Column>();

            foreach (var column in clone.Columns)
            {
                if (liveColumnById.TryGetValue(column.Id, out var liveColumn))
                {
                    column.Name = liveColumn.Name;
                    cloneColumnPairs[column] = liveColumn;
                }
            }

            targets[indexOf[target]] = clone;
            livePartnerOf[clone] = live;
            liveColumnOf[clone] = cloneColumnPairs;
        }

        // 揃える組が無ければ元のリストのまま返す
        IReadOnlyList<Entity> result = targets.SequenceEqual(targetEntities)
            ? targetEntities
            : targets;

        return new SchemaMatching(result, livePartnerOf, liveColumnOf);
    }

    /// <summary>テーブル名を図へ保存する形にする（<see cref="SchemaNames.ToStoredForm"/>・綴りはそのまま）</summary>
    public static string NormalizeTable(Entity e) => SchemaNames.ToStoredForm(e.TableName);

    /// <summary>外部キーの同一性比較に使うシグネチャ（親子・構成列ペア・制約名・参照アクション）を生成する</summary>
    /// <param name="fk">親子・列ペアまで解決済みの外部キー</param>
    /// <param name="foreignKeyNames">同じ側の図の外部キー名（<see cref="ConstraintNames.ResolveForeignKeyNames"/>）</param>
    /// <param name="includeConstraintName">
    /// 制約名を比較キーへ含めるか。<c>false</c>（SQLite）のときは制約名を空にして名前差を無視する。
    /// </param>
    /// <returns>
    /// 複合外部キーの列ペアは対応の集合として並びを見ずに畳む（並びは意味を持たない。参照先のキーの並びを求める
    /// SQL Server・MySQL の DDL は出力の時点で並べ替える＝<see cref="ForeignKeyColumnOrder"/>。Oracle のカタログは
    /// 宣言の並びを残さず参照先のキーの並びで返し、並べ替えて作った外部キーも取り込むとキーの並びで返る）
    /// </returns>
    /// <param name="dialect">
    /// 参照アクションを方言の実効値へ畳むためのケーパビリティ（Oracle は <c>ON UPDATE</c> と
    /// <c>ON DELETE SET DEFAULT</c> を持たないため、図側のそれらを <c>NO ACTION</c> として比べる）
    /// </param>
    /// <remarks>
    /// 列ペアが可変長になったため、値タプルではなく文字列へ畳む（集合比較は <c>HashSet&lt;string&gt;</c>）。
    /// </remarks>
    private static string ForeignKeySignature(
        ResolvedForeignKeyRelationship fk,
        IReadOnlyDictionary<Relationship, string> foreignKeyNames,
        bool includeConstraintName,
        SyncDialectCapabilities dialect
    )
    {
        // 列ペアは対応の集合として、5 方言とも並びを見ずに比べる
        var pairSignature = string.Concat(
            fk.ColumnPairs.Select(p =>
                    SignaturePart(ColumnKey(p.ParentColumn))
                    + SignaturePart(ColumnKey(p.ChildColumn))
                )
                .Order(StringComparer.Ordinal)
        );
        // 名前は DDL・同期スクリプトが付けるのと同じ実効名で比べる（名前の無いリレーションは既定名、
        // 同じ組の 2 本目は一意化した名前）。素の名前で比べると、名前の無い図と、DDL が既定名で作った
        // live の外部キーが別物に見える
        var constraintName = includeConstraintName
            ? NameKey(foreignKeyNames[fk.Relationship])
            : string.Empty;

        // 各部分は長さつきでつなぐ（名前に区切り文字を含む表・列・制約で、別の外部キーと同じ署名にならない）
        return string.Concat(
            SignaturePart(SchemaNames.Key(fk.Parent.TableName)),
            SignaturePart(SchemaNames.Key(fk.Child.TableName)),
            SignaturePart(pairSignature),
            SignaturePart(constraintName),
            SignaturePart(dialect.EffectiveOnDelete(fk.Relationship.OnDelete).ToString()),
            SignaturePart(dialect.EffectiveOnUpdate(fk.Relationship.OnUpdate).ToString())
        );
    }

    /// <summary>署名の 1 部分を長さつきで表す（<see cref="UniqueConstraintNaming.ColumnSetSignature"/> と同じ形）</summary>
    private static string SignaturePart(string value) => $"{value.Length}:{value}";

    /// <summary>列名を照合用のキー（<see cref="SchemaNames.Key"/>）にする（外部キーの署名と作り直しの対応づけが共有する）</summary>
    private static string ColumnKey(string name) => SchemaNames.Key(name);

    /// <summary>列の実効の NULL 許容（主キーの列はモデルの値に依らず NOT NULL）</summary>
    private static bool IsEffectivelyNullable(Column column) =>
        column.IsNullable && !column.IsPrimaryKey;

    /// <summary>制約の名前を照合用のキー（<see cref="SchemaNames.Key"/>）にする（外部キーと一意制約の作り直しの対応づけが使う）</summary>
    private static string NameKey(string name) => SchemaNames.Key(name);

    /// <summary>図の外部キーの名前を求める（旧版の PostgreSQL が切り詰めた既定名を吸収する）</summary>
    /// <remarks>
    /// <para>
    /// 名前は DDL と同じ規則（<see cref="ConstraintNames.ResolveForeignKeyNameDetails"/>・同期の方言の上限で既定名を縮める）。
    /// </para>
    /// <para>
    /// 上限を超える識別子を黙って切り詰める方言（<see cref="SyncDialectCapabilities.TruncatesLongIdentifiers"/>＝PostgreSQL）
    /// では、名前の無いリレーションに限り、同じ子テーブルの live の外部キーに「縮める前の名前を方言の切り方で切った名前」
    /// があれば、その名前を図の外部キーの名前とする（同じ外部キーとみなす）。旧版の QuickER は長い既定名をそのまま
    /// DDL へ書き、PostgreSQL が切り詰めて保存していたため、吸収しないと既定名を縮める版で同期したときに同じ外部キーが
    /// 追加と削除の 2 項目になる。保存された名前（利用者が付けた名前）は対象にしない。
    /// </para>
    /// </remarks>
    private static IReadOnlyDictionary<Relationship, string> ResolveTargetForeignKeyNames(
        IReadOnlyList<Entity> targetEntities,
        IReadOnlyList<Relationship> targetRelationships,
        IReadOnlyList<Entity> liveEntities,
        IReadOnlyDictionary<Relationship, string> liveFkNames,
        bool includeConstraintName,
        SyncDialectCapabilities dialect
    )
    {
        var limit = dialect.IdentifierLengthLimit;
        var details = ConstraintNames.ResolveForeignKeyNameDetails(
            targetEntities,
            targetRelationships,
            limit
        );
        var names = details.ToDictionary(pair => pair.Key, pair => pair.Value.Name);

        if (!dialect.TruncatesLongIdentifiers || !includeConstraintName)
        {
            return names;
        }

        // live の外部キーを「子テーブル＋名前」で引けるようにする
        var liveTables = liveEntities.ToDictionary(entity => entity.Id);
        var liveKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (relationship, name) in liveFkNames)
        {
            if (liveTables.TryGetValue(relationship.TargetEntityId, out var child))
            {
                liveKeys.Add(LegacyForeignKeyKey(NormalizeTable(child), name));
            }
        }

        var targetTables = targetEntities.ToDictionary(entity => entity.Id);

        foreach (var (relationship, resolved) in details)
        {
            if (
                resolved.IsStored
                || !targetTables.TryGetValue(relationship.TargetEntityId, out var child)
            )
            {
                continue;
            }

            // 上限内の名前は縮めていない＝旧版と同じ名前なので吸収の対象外
            if (limit.Fits(resolved.UnshortenedName))
            {
                continue;
            }

            var legacy = limit.Truncate(resolved.UnshortenedName);

            if (liveKeys.Contains(LegacyForeignKeyKey(NormalizeTable(child), legacy)))
            {
                names[relationship] = legacy;
            }
        }

        return names;
    }

    /// <summary>旧版の切り詰めた名前を引くキー（子テーブル＋名前。名前の比べ方はシグネチャと同じ）</summary>
    private static string LegacyForeignKeyKey(string childTable, string name) =>
        NameKey(childTable) + "|" + NameKey(name);

    /// <summary>
    /// 同じ外部キーの作り直しにあたる削除候補と追加候補を、1 つの <see cref="SchemaDiffKind.AlterForeignKey"/> へ畳む。
    /// </summary>
    /// <param name="adds">図にだけ在る外部キー（AddForeignKey 候補・差分へ並べる順）</param>
    /// <param name="drops">DB にだけ在る外部キー（DropForeignKey 候補）</param>
    /// <param name="includeConstraintName">外部キー名を DB に保存する方言か</param>
    /// <param name="dialect">変更内容の文言で参照アクションを方言の実効値へ畳むためのケーパビリティ</param>
    /// <returns>差分へ並べる順の外部キー項目（畳んだ項目は追加候補の位置に入る）</returns>
    /// <remarks>
    /// <para>
    /// 参照アクション・列ペアの順序・参照先だけが変わった外部キーは、シグネチャが違うため削除と追加の 2 項目になる。
    /// 名前を保存する方言では両者が同じ名前を持つため、既定どおり追加だけを選んで実行すると、同じ名前の
    /// 外部キーを足そうとして失敗する。名前を保存しない方言（SQLite）では失敗はしないが、再構築後の表に
    /// 旧と新の外部キーが同じ列へ 2 本並ぶ。どちらも「同じ外部キーを作り直す」1 つの操作なので 1 項目にする。
    /// </para>
    /// <para>
    /// 対応づけは、名前を保存する方言では「子表と実効名」、保存しない方言（SQLite）では「子表と子列の集合」
    /// （同じ列に張る外部キー。参照先だけを変えた外部キーもまとまる）で行う。1 対 1 で、先に見つかったものと組む。
    /// 外部キー名が一意な範囲は方言で違い（PostgreSQL はテーブル単位、SQL Server・MySQL・Oracle はスキーマ単位）、
    /// 「子表と実効名」は同じ子表の中の衝突を拾う。同じ名前のまま別の子表へ移す外部キーは組まない（稀な形。スキーマ単位の
    /// 方言では、既定どおり追加だけを選ぶと名前の衝突で失敗しうる）。
    /// </para>
    /// </remarks>
    private static List<SchemaDiffItem> PairForeignKeyAlters(
        List<SchemaDiffItem> adds,
        List<SchemaDiffItem> drops,
        bool includeConstraintName,
        SyncDialectCapabilities dialect
    )
    {
        // 削除候補をキーごとに並べておき、追加候補ごとに先頭から 1 つずつ取る（総当たりにしない）
        var dropsByKey = new Dictionary<string, Queue<SchemaDiffItem>>(StringComparer.Ordinal);

        foreach (var drop in drops)
        {
            var key = ForeignKeyPairingKey(
                drop,
                includeConstraintName,
                dialect.IdentifierLengthLimit
            );

            if (!dropsByKey.TryGetValue(key, out var queue))
            {
                queue = new Queue<SchemaDiffItem>();
                dropsByKey[key] = queue;
            }

            queue.Enqueue(drop);
        }

        var paired = new HashSet<SchemaDiffItem>(ReferenceEqualityComparer.Instance);
        var result = new List<SchemaDiffItem>();

        foreach (var add in adds)
        {
            if (
                !dropsByKey.TryGetValue(
                    ForeignKeyPairingKey(add, includeConstraintName, dialect.IdentifierLengthLimit),
                    out var candidates
                ) || !candidates.TryDequeue(out var drop)
            )
            {
                result.Add(add);
                continue;
            }

            paired.Add(drop);
            result.Add(BuildForeignKeyAlter(add, drop, dialect));
        }

        result.AddRange(drops.Where(d => !paired.Contains(d)));
        return result;
    }

    /// <summary>削除候補と追加候補を対応づけるキー（<see cref="PairForeignKeyAlters"/> の規則）</summary>
    private static string ForeignKeyPairingKey(
        SchemaDiffItem item,
        bool includeConstraintName,
        IdentifierLengthLimit limit
    )
    {
        var child = SchemaNames.Key(item.ChildEntity!.TableName);

        if (includeConstraintName)
        {
            // 追加候補は差分が求めた名前、削除候補は DB の名前（無ければ既定名）
            var name = ConstraintNames.EffectiveForeignKey(
                item.ForeignKeyName,
                NormalizeTable(item.ChildEntity!),
                NormalizeTable(item.ParentEntity!),
                limit
            );

            return SignaturePart(child) + SignaturePart(NameKey(name));
        }

        var childColumns = string.Concat(
            ForeignKeyColumnPairResolver
                .ChildColumns(item.ForeignKeyColumnPairs)
                .Select(c => SignaturePart(ColumnKey(c)))
                .Order(StringComparer.Ordinal)
        );

        return SignaturePart(child) + childColumns;
    }

    /// <summary>対応づいた追加候補（新しい定義）と削除候補（DB の定義）から外部キー変更の項目を作る</summary>
    private static SchemaDiffItem BuildForeignKeyAlter(
        SchemaDiffItem add,
        SchemaDiffItem drop,
        SyncDialectCapabilities dialect
    )
    {
        var changes = new List<string>();
        var oldParent = NormalizeTable(drop.ParentEntity!);
        var newParent = NormalizeTable(add.ParentEntity!);
        var oldChildColumns = ForeignKeyColumnPairResolver.ChildColumns(drop.ForeignKeyColumnPairs);
        var oldParentColumns = ForeignKeyColumnPairResolver.ParentColumns(
            drop.ForeignKeyColumnPairs
        );
        var newChildColumns = ForeignKeyColumnPairResolver.ChildColumns(add.ForeignKeyColumnPairs);
        var newParentColumns = ForeignKeyColumnPairResolver.ParentColumns(
            add.ForeignKeyColumnPairs
        );

        // 列ペアの並びだけの違いは参照の変更と言わない（差分のシグネチャと同じ規則＝5 方言とも並びを見ない）
        var referenceChanged =
            !SchemaNames.AreSame(oldParent, newParent)
            || !SamePairs(drop.ForeignKeyColumnPairs, add.ForeignKeyColumnPairs);

        if (referenceChanged)
        {
            changes.Add(
                string.Format(
                    Strings.Diff_ForeignKeyChange_Reference,
                    NormalizeTable(drop.ChildEntity!),
                    FormatColumnList(oldChildColumns),
                    oldParent,
                    FormatColumnList(oldParentColumns)
                )
            );
        }

        // 参照アクションは方言の実効値で比べて表示する（DB が表せない ON UPDATE などを「変更」と言わない）
        var oldOnDelete = dialect.EffectiveOnDelete(drop.Relationship!.OnDelete);
        var newOnDelete = dialect.EffectiveOnDelete(add.Relationship!.OnDelete);
        var oldOnUpdate = dialect.EffectiveOnUpdate(drop.Relationship!.OnUpdate);
        var newOnUpdate = dialect.EffectiveOnUpdate(add.Relationship!.OnUpdate);

        if (oldOnDelete != newOnDelete)
        {
            changes.Add(
                string.Format(
                    Strings.Diff_ForeignKeyChange_OnDelete,
                    oldOnDelete.ToSqlText(),
                    newOnDelete.ToSqlText()
                )
            );
        }

        if (oldOnUpdate != newOnUpdate)
        {
            changes.Add(
                string.Format(
                    Strings.Diff_ForeignKeyChange_OnUpdate,
                    oldOnUpdate.ToSqlText(),
                    newOnUpdate.ToSqlText()
                )
            );
        }

        return new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterForeignKey,
            TableName = add.TableName,
            ColumnName = add.ColumnName,
            Entity = add.Entity,
            ParentEntity = add.ParentEntity,
            ChildEntity = add.ChildEntity,
            Relationship = add.Relationship,
            ForeignKeyName = add.ForeignKeyName,
            ForeignKeyColumnPairs = add.ForeignKeyColumnPairs,
            ReplacedForeignKey = drop,
            IsSelected = false,
            Description = string.Format(
                Strings.Diff_AlterForeignKey,
                NormalizeTable(add.ChildEntity!),
                FormatColumnList(newChildColumns),
                newParent,
                FormatColumnList(newParentColumns),
                string.Join(" / ", changes)
            ),
        };
        bool SamePairs(
            IReadOnlyList<ForeignKeyColumnNamePair> left,
            IReadOnlyList<ForeignKeyColumnNamePair> right
        )
        {
            var leftKeys = left.Select(PairKey);
            var rightKeys = right.Select(PairKey);

            return leftKeys
                .Order(StringComparer.Ordinal)
                .SequenceEqual(rightKeys.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        }

        static string PairKey(ForeignKeyColumnNamePair pair) =>
            $"{SchemaNames.Key(pair.ParentColumn)}>{SchemaNames.Key(pair.ChildColumn)}";
    }

    /// <summary>外部キーの構成列を差分表示用の列名リストへ整形する（宣言順・カンマ区切り）</summary>
    private static string FormatColumnList(IEnumerable<string> columnNames) =>
        string.Join(", ", columnNames);

    /// <summary>主キー列の名前を実効順（<see cref="Entity.GetPrimaryKeyColumnsInOrder"/>）で取り出す（順序も比較対象にするため List で返す）</summary>
    private static List<string> PrimaryKeyColumnNames(Entity entity) =>
        entity.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList();

    /// <summary>主キー構成（列の順序付き集合）が同一かを判定する（列名の比較規則は列差分と同じ <see cref="SchemaNames"/>）</summary>
    private static bool IsSamePrimaryKey(IReadOnlyList<string> live, IReadOnlyList<string> target)
    {
        if (live.Count != target.Count)
        {
            return false;
        }

        for (var i = 0; i < live.Count; i++)
        {
            if (!SchemaNames.AreSame(live[i], target[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>主キー列の一覧を表示用の文字列へ整形する（主キーなしは表示言語の「なし」ラベル）</summary>
    private static string FormatPrimaryKey(IReadOnlyList<string> columns) =>
        columns.Count == 0 ? Strings.Diff_PrimaryKey_None : string.Join(", ", columns);

    /// <summary>共通列（双方に存在する列）の相対順序が変更されているかを判定する</summary>
    /// <remarks>共通列は差分と同じ組（<see cref="MatchSchemas"/> が求めた <paramref name="liveColumnOf"/>）</remarks>
    private static bool HasColumnOrderChanged(
        Entity live,
        Entity target,
        Dictionary<Column, Column> liveColumnOf
    )
    {
        // 追加・削除を除いた共通列を、それぞれの出現順で取り出す（組なので同数になる）
        var targetCommon = target.Columns.Where(liveColumnOf.ContainsKey).ToList();
        var matchedLive = liveColumnOf.Values.ToHashSet();
        var liveCommon = live.Columns.Where(matchedLive.Contains).ToList();

        // 共通列が 2 列未満なら相対順序の概念が無いため並び替えとは扱わない
        if (liveCommon.Count < 2)
        {
            return false;
        }

        // 共通列の相対順序が 1 か所でも異なれば列順変更とみなす
        for (var i = 0; i < liveCommon.Count; i++)
        {
            if (!ReferenceEquals(liveColumnOf[targetCommon[i]], liveCommon[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>説明文を指定長で切り詰め、超過時は末尾に省略記号を付ける</summary>
    /// <remarks>切る位置がサロゲートペアの間なら、その手前で切る（片割れの文字を残さない）。</remarks>
    private static string Truncate(string s, int max = 30)
    {
        if (s.Length <= max)
        {
            return s;
        }

        var length = char.IsHighSurrogate(s[max - 1]) ? max - 1 : max;
        return s[..length] + "…";
    }

    /// <summary>NULL 許容の可否を表示言語のラベル（許可 / 禁止）へ変換する</summary>
    private static string NullableLabel(bool isNullable) =>
        isNullable ? Strings.Diff_Nullable_Allow : Strings.Diff_Nullable_Deny;
}
