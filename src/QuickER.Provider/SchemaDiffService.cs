using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Resources;

namespace QuickER.Provider;

/// <summary>既存 DB スキーマと現在のダイアグラムを比較して <see cref="SchemaDiff"/> を生成するサービス</summary>
/// <remarks>リネームは扱わず「同名 = 同一」を前提とする 名称が変われば削除＋追加として検出する</remarks>
public class SchemaDiffService
{
    /// <summary>DB 現状とダイアグラムの目標状態を突き合わせて差分項目を計算する</summary>
    /// <param name="liveEntities">DB から取得した現在のエンティティ</param>
    /// <param name="liveRelationships">DB から取得した現在のリレーション</param>
    /// <param name="targetEntities">ダイアグラム上のエンティティ（期待状態）</param>
    /// <param name="targetRelationships">ダイアグラム上のリレーション</param>
    /// <param name="capabilities">
    /// 対象方言の同期ケーパビリティ。<c>null</c>（既定）なら全差分を生成する。
    /// <see cref="SyncDialectCapabilities.SupportsDescriptions"/> が <c>false</c> の方言（SQLite）では
    /// 説明差分を生成しない（コメント機構が無く live 側が常に空＝恒常的な幻の差分になるため）。
    /// <see cref="SyncDialectCapabilities.PersistsForeignKeyConstraintNames"/> が <c>false</c> の方言（SQLite）では
    /// FK シグネチャから制約名を除いて比較する（合成名 live と無名 target で恒常的な Drop+Add 誤検出を避けるため）。
    /// </param>
    public SchemaDiff Compute(
        IReadOnlyList<Entity> liveEntities,
        IReadOnlyList<Relationship> liveRelationships,
        IReadOnlyList<Entity> targetEntities,
        IReadOnlyList<Relationship> targetRelationships,
        SyncDialectCapabilities? capabilities = null
    )
    {
        var diff = new SchemaDiff();

        // 説明差分を出すか（SQLite などコメント機構が無い方言では出さない）
        var emitDescriptions = capabilities?.SupportsDescriptions ?? true;

        // FK 比較で制約名を含めるか（SQLite は合成名で永続化されないため名前を除いて比較する）
        var includeFkConstraintName = capabilities?.PersistsForeignKeyConstraintNames ?? true;

        var liveByName = liveEntities.ToDictionary(
            NormalizeTable,
            StringComparer.OrdinalIgnoreCase
        );
        var targetByName = targetEntities.ToDictionary(
            NormalizeTable,
            StringComparer.OrdinalIgnoreCase
        );

        // ---------- テーブル/カラムの差分 ----------
        foreach (var (name, target) in targetByName)
        {
            if (!liveByName.TryGetValue(name, out var live))
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
                var newTblDesc = target.Description ?? string.Empty;

                if (emitDescriptions && !string.IsNullOrEmpty(newTblDesc))
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
                    if (!emitDescriptions || string.IsNullOrEmpty(c.Description))
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

            // 既存テーブル: カラム差分
            var liveCols = live.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var targetCols = target.Columns.ToDictionary(
                c => c.Name,
                StringComparer.OrdinalIgnoreCase
            );

            // テーブル説明の差分
            var targetTableDesc = target.Description ?? string.Empty;
            var liveTableDesc = live.Description ?? string.Empty;

            if (
                emitDescriptions
                && !string.Equals(targetTableDesc, liveTableDesc, StringComparison.Ordinal)
            )
            {
                diff.Items.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.SetTableDescription,
                        TableName = name,
                        Entity = target,
                        NewDescription = targetTableDesc,
                        OldDescription = liveTableDesc,
                        Description = string.IsNullOrEmpty(targetTableDesc)
                            ? string.Format(Strings.Diff_RemoveTableDescription, name)
                            : string.Format(
                                Strings.Diff_UpdateTableDescription,
                                name,
                                Truncate(targetTableDesc)
                            ),
                    }
                );
            }

            foreach (var (cname, tcol) in targetCols)
            {
                if (!liveCols.TryGetValue(cname, out var lcol))
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
                    if (emitDescriptions && !string.IsNullOrEmpty(tcol.Description))
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
                    if (
                        !IsSameType(lcol.DataType, tcol.DataType)
                        || lcol.IsNullable != tcol.IsNullable
                    )
                    {
                        var changeParts = new List<string>();

                        if (!IsSameType(lcol.DataType, tcol.DataType))
                        {
                            changeParts.Add(
                                string.Format(Strings.Diff_TypeChange, lcol.DataType, tcol.DataType)
                            );
                        }

                        if (lcol.IsNullable != tcol.IsNullable)
                        {
                            changeParts.Add(
                                string.Format(
                                    Strings.Diff_NullableChange,
                                    NullableLabel(lcol.IsNullable),
                                    NullableLabel(tcol.IsNullable)
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

                    var newColDesc = tcol.Description ?? string.Empty;
                    var oldColDesc = lcol.Description ?? string.Empty;

                    if (
                        emitDescriptions
                        && !string.Equals(newColDesc, oldColDesc, StringComparison.Ordinal)
                    )
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
                                Description = string.IsNullOrEmpty(newColDesc)
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
            AppendUniqueConstraintDiffs(diff, name, live, target);

            foreach (var (cname, lcol) in liveCols)
            {
                if (!targetCols.ContainsKey(cname))
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

        foreach (var (name, live) in liveByName)
        {
            if (!targetByName.ContainsKey(name))
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
        // 親・子・列・制約名などのシグネチャでキー化し、同一 FK の有無を集合比較で判定する
        var liveFkPairs = liveRelationships
            .Where(r => r.Type != RelationshipType.ManyToMany)
            .Select(r => MakeForeignKeySignature(r, liveEntities, includeFkConstraintName))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToHashSet();

        foreach (var rel in targetRelationships)
        {
            if (rel.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            var signature = MakeForeignKeySignature(rel, targetEntities, includeFkConstraintName);

            if (signature is null)
            {
                continue;
            }

            if (liveFkPairs.Contains(signature))
            {
                continue;
            }

            var parent = targetEntities.FirstOrDefault(e => e.Id == rel.SourceEntityId);
            var child = targetEntities.FirstOrDefault(e => e.Id == rel.TargetEntityId);

            if (parent is null || child is null)
            {
                continue;
            }

            // 列ペアが正本（推測フォールバックなし）。解決できない外部キーはシグネチャ生成の時点で落ちている
            var columnPairs = ForeignKeyColumnPairResolver.Resolve(rel, parent, child);

            if (columnPairs is null)
            {
                continue;
            }

            diff.Items.Add(
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

        // DB にあるが ER 図にない FK は削除候補とする
        // 取得側 Relationship には FK 名が無いため、ここでは「親→子ペアの消失」ケースのみ検出する
        var targetFkPairs = targetRelationships
            .Where(r => r.Type != RelationshipType.ManyToMany)
            .Select(r => MakeForeignKeySignature(r, targetEntities, includeFkConstraintName))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToHashSet();

        foreach (var rel in liveRelationships)
        {
            if (rel.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            var signature = MakeForeignKeySignature(rel, liveEntities, includeFkConstraintName);

            if (signature is null)
            {
                continue;
            }

            if (targetFkPairs.Contains(signature))
            {
                continue;
            }

            var parent = liveEntities.FirstOrDefault(e => e.Id == rel.SourceEntityId);
            var child = liveEntities.FirstOrDefault(e => e.Id == rel.TargetEntityId);

            if (parent is null || child is null)
            {
                continue;
            }

            var columnPairs = ForeignKeyColumnPairResolver.Resolve(rel, parent, child);

            if (columnPairs is null)
            {
                continue;
            }

            diff.Items.Add(
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

        // ---------- 列順の差分（対応方言のみ・既定では非選択） ----------
        // 列順同期は SQLite（テーブル再構築）と MySQL（ネイティブ MODIFY ... AFTER）だけが実現できる。
        // 非対応方言（ColumnReorder=None）では幻の差分を出さない（案内表示は UI 側が担う）。
        if (capabilities is not null && capabilities.ColumnReorder != ColumnReorderMode.None)
        {
            foreach (var tableName in DetectColumnOrderChanges(liveEntities, targetEntities))
            {
                if (!targetByName.TryGetValue(tableName, out var target))
                {
                    continue;
                }

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
    /// </remarks>
    private static void AppendUniqueConstraintDiffs(
        SchemaDiff diff,
        string tableName,
        Entity live,
        Entity target
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

        foreach (var constraint in targetConstraints)
        {
            if (liveSignatures.Contains(constraint.Signature))
            {
                continue;
            }

            diff.Items.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddUniqueConstraint,
                    TableName = tableName,
                    Entity = target,
                    // 図側の名前は未設定（null）でよい＝レンダラーが UQ_{表}_{列…} を合成する
                    UniqueConstraintName = constraint.Name,
                    UniqueConstraintColumns = constraint.ColumnNames,
                    Description = string.Format(
                        Strings.Diff_AddUniqueConstraint,
                        tableName,
                        string.Join(", ", constraint.ColumnNames)
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

            diff.Items.Add(
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
        var changed = new List<string>();
        var liveByName = liveEntities.ToDictionary(
            NormalizeTable,
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var target in targetEntities)
        {
            var name = NormalizeTable(target);

            if (!liveByName.TryGetValue(name, out var live))
            {
                continue;
            }

            if (HasColumnOrderChanged(live, target))
            {
                changed.Add(name);
            }
        }

        return changed;
    }

    /// <summary>テーブル名を比較用に正規化する（前後空白を除去する）</summary>
    public static string NormalizeTable(Entity e)
    {
        return e.TableName.Trim();
    }

    /// <summary>外部キーの同一性比較に使うシグネチャ（親子・構成列ペア・制約名・参照アクション）を生成する</summary>
    /// <param name="includeConstraintName">
    /// 制約名を比較キーへ含めるか。<c>false</c>（SQLite）のときは制約名を空にして名前差を無視する。
    /// </param>
    /// <returns>
    /// 親子エンティティまたは構成列を解決できない場合は null。複合外部キーは列ペアを宣言順に並べて畳むため、
    /// 構成列の順序が違う外部キーは別物として扱われる（DDL の意味が異なるため）
    /// </returns>
    /// <remarks>
    /// 列ペアが可変長になったため、値タプルではなく文字列へ畳む（集合比較は <c>HashSet&lt;string&gt;</c>）。
    /// </remarks>
    private static string? MakeForeignKeySignature(
        Relationship rel,
        IReadOnlyList<Entity> entities,
        bool includeConstraintName
    )
    {
        var parent = entities.FirstOrDefault(e => e.Id == rel.SourceEntityId);
        var child = entities.FirstOrDefault(e => e.Id == rel.TargetEntityId);

        if (parent is null || child is null)
        {
            return null;
        }

        var pairs = ForeignKeyColumnPairResolver.Resolve(rel, parent, child);

        if (pairs is null)
        {
            return null;
        }

        var pairSignature = string.Join(
            ",",
            pairs.Select(p =>
                $"{p.ParentColumn.ToLowerInvariant()}>{p.ChildColumn.ToLowerInvariant()}"
            )
        );
        var constraintName = includeConstraintName
            ? rel.ConstraintName?.Trim().ToLowerInvariant() ?? string.Empty
            : string.Empty;

        return string.Join(
            "|",
            NormalizeTable(parent).ToLowerInvariant(),
            NormalizeTable(child).ToLowerInvariant(),
            pairSignature,
            constraintName,
            rel.OnDelete.ToString(),
            rel.OnUpdate.ToString()
        );
    }

    /// <summary>外部キーの構成列を差分表示用の列名リストへ整形する（宣言順・カンマ区切り）</summary>
    private static string FormatColumnList(IEnumerable<string> columnNames) =>
        string.Join(", ", columnNames);

    /// <summary>主キー列の名前を、エンティティの列定義順で取り出す（順序も比較対象にするため List で返す）</summary>
    private static List<string> PrimaryKeyColumnNames(Entity entity) =>
        entity.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

    /// <summary>主キー構成（列の順序付き集合）が同一かを判定する（列名の比較規則は列差分と同じ大文字小文字無視）</summary>
    private static bool IsSamePrimaryKey(IReadOnlyList<string> live, IReadOnlyList<string> target)
    {
        if (live.Count != target.Count)
        {
            return false;
        }

        for (var i = 0; i < live.Count; i++)
        {
            if (!string.Equals(live[i], target[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>主キー列の一覧を表示用の文字列へ整形する（主キーなしは表示言語の「なし」ラベル）</summary>
    private static string FormatPrimaryKey(IReadOnlyList<string> columns) =>
        columns.Count == 0 ? Strings.Diff_PrimaryKey_None : string.Join(", ", columns);

    /// <summary>データ型を大文字小文字・前後空白を無視して同一とみなせるか判定する</summary>
    private static bool IsSameType(string a, string b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>共通列（双方に存在する列）の相対順序が変更されているかを判定する</summary>
    private static bool HasColumnOrderChanged(Entity live, Entity target)
    {
        var liveNames = live.Columns.Select(c => c.Name).ToList();
        var targetNames = target.Columns.Select(c => c.Name).ToList();

        var liveSet = new HashSet<string>(liveNames, StringComparer.OrdinalIgnoreCase);
        var targetSet = new HashSet<string>(targetNames, StringComparer.OrdinalIgnoreCase);

        // 追加・削除を除いた共通列を、それぞれの出現順で取り出す（両者は同一集合＝同数になる）
        var liveCommon = liveNames.Where(targetSet.Contains).ToList();
        var targetCommon = targetNames.Where(liveSet.Contains).ToList();

        // 共通列が 2 列未満なら相対順序の概念が無いため並び替えとは扱わない
        if (liveCommon.Count < 2)
        {
            return false;
        }

        // 共通列の相対順序が 1 か所でも異なれば列順変更とみなす
        for (var i = 0; i < liveCommon.Count; i++)
        {
            if (!string.Equals(liveCommon[i], targetCommon[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>説明文を指定長で切り詰め、超過時は末尾に省略記号を付ける</summary>
    private static string Truncate(string s, int max = 30) =>
        s.Length <= max ? s : s.Substring(0, max) + "…";

    /// <summary>NULL 許容の可否を表示言語のラベル（許可 / 禁止）へ変換する</summary>
    private static string NullableLabel(bool isNullable) =>
        isNullable ? Strings.Diff_Nullable_Allow : Strings.Diff_Nullable_Deny;
}
