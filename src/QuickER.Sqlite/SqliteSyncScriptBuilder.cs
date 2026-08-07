using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Sqlite;

/// <summary>
/// 実行計画（<see cref="SyncPlan"/>）から SQLite 用の同期スクリプトを生成する。
/// </summary>
/// <remarks>
/// <para>
/// SQLite は <c>ALTER COLUMN</c> / <c>ADD・DROP CONSTRAINT</c> を持たないため、列型変更・列削除・FK 変更は
/// <see cref="SyncPlanner"/> が組み立てた <see cref="SyncPlan.Rebuilds"/>（テーブル再構築計画）を
/// 「新テーブル作成 → データ移送 → 旧 DROP → RENAME → 補助オブジェクト再作成」の手順で反映する
/// （sqlite.org の ALTER TABLE ドキュメントの推奨手順）。列追加・テーブル削除は逐次 DDL で直接出す。
/// </para>
/// <para>
/// 出力は <c>PRAGMA foreign_keys=OFF;</c> で始まり <c>PRAGMA foreign_key_check;</c> →
/// <c>PRAGMA foreign_keys=ON;</c> で終わる。実 FK 検査は実行側（<see cref="SqliteSchemaSyncExecutor"/>）が
/// <c>foreign_key_check</c> を <c>ExecuteReader</c> で行う対規約とし、スクリプト中の同 PRAGMA は
/// スタンドアロン実行時のための備え（トランザクション内では無害）。
/// </para>
/// <para>
/// <b>既知の制限</b>: NOT NULL の列追加（AddColumn）は SQLite が DEFAULT 無しの <c>ADD COLUMN ... NOT NULL</c> を
/// 受け付けないため実行時に失敗する（トランザクションはロールバックされ、データは無変更で安全）。
/// </para>
/// </remarks>
public sealed class SqliteSyncScriptBuilder : SyncScriptBuilderBase
{
    /// <summary>再構築で作る一時テーブル名の接尾辞（衝突は考慮しない前提＝スキーマ内で一意と仮定）</summary>
    private const string RebuildSuffix = "_quicker_rebuild";

    /// <inheritdoc />
    public override string Build(SyncPlan plan)
    {
        // 空の計画は空文字列（実行側は空スクリプトを no-op として COMMIT する）
        if (plan.Sections.Count == 0 && plan.Rebuilds.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        // 再構築中は FK 強制を切る（実行側がトランザクション外で実際に OFF にする。ここは対規約の明示）
        sb.AppendLine("PRAGMA foreign_keys=OFF;");
        sb.AppendLine();

        // 1. 新規テーブル（CreateOnly）＝FK 句インラインの CREATE TABLE
        foreach (var rebuild in plan.Rebuilds.Where(r => r.CreateOnly))
        {
            AppendCreateOnlyTable(sb, rebuild);
            sb.AppendLine();
        }

        // 2. 列追加セクション（SQLite は ADD COLUMN を逐次 DDL で直接扱える）
        foreach (var section in plan.Sections.Where(s => s.Kind == SchemaDiffKind.AddColumn))
        {
            AppendSection(sb, section);
        }

        // 3. 既存テーブルの再構築ブロック
        foreach (var rebuild in plan.Rebuilds.Where(r => !r.CreateOnly))
        {
            AppendRebuildBlock(sb, rebuild);
        }

        // 4. テーブル削除セクション
        foreach (var section in plan.Sections.Where(s => s.Kind == SchemaDiffKind.DropTable))
        {
            AppendSection(sb, section);
        }

        // 5. 想定外セクション（説明設定など・通常は capabilities で抑止済み）を防御的に描画する
        foreach (
            var section in plan.Sections.Where(s =>
                s.Kind != SchemaDiffKind.AddColumn && s.Kind != SchemaDiffKind.DropTable
            )
        )
        {
            AppendSection(sb, section);
        }

        // FK 整合性を最終確認する対規約（実行側が ExecuteReader で違反を検出しロールバックする）
        sb.AppendLine("PRAGMA foreign_key_check;");
        sb.AppendLine("PRAGMA foreign_keys=ON;");

        return sb.ToString();
    }

    /// <summary>CreateOnly（新規テーブル）の <c>CREATE TABLE</c> を出力する（FK 句インライン）</summary>
    private static void AppendCreateOnlyTable(StringBuilder sb, TableRebuildPlan rebuild)
    {
        AppendCreateTableBody(
            sb,
            SqliteIdentifier.Quote(rebuild.TableName),
            rebuild.TableName,
            rebuild.NewDefinition,
            rebuild.ForeignKeys
        );
    }

    /// <summary>1 テーブル分の再構築ブロック（新テーブル作成 → 移送 → DROP → RENAME → 補助再作成）を出力する</summary>
    private static void AppendRebuildBlock(StringBuilder sb, TableRebuildPlan rebuild)
    {
        var table = rebuild.TableName;
        var tempName = table + RebuildSuffix;
        var quotedTemp = SqliteIdentifier.QuoteSimple(tempName);

        // 見出し（固定文は英語が正本）
        sb.AppendLine($"-- ===== RebuildTable: {table} =====");

        // 合成後の定義で一時テーブルを作る（制約名は元テーブル名基準＝リネーム後に自然な名前になる）
        AppendCreateTableBody(sb, quotedTemp, table, rebuild.NewDefinition, rebuild.ForeignKeys);

        // データ移送（live と合成後の両方に存在する列のみ・型変更は SQLite の型親和性で変換される）
        if (rebuild.CopyColumns.Count > 0)
        {
            var cols = string.Join(", ", rebuild.CopyColumns.Select(SqliteIdentifier.QuoteSimple));
            sb.AppendLine(
                $"INSERT INTO {quotedTemp} ({cols}) SELECT {cols} FROM {SqliteIdentifier.Quote(table)};"
            );
        }

        sb.AppendLine($"DROP TABLE {SqliteIdentifier.Quote(table)};");
        sb.AppendLine($"ALTER TABLE {quotedTemp} RENAME TO {SqliteIdentifier.QuoteSimple(table)};");

        // 補助オブジェクト（インデックス・トリガー）を元の CREATE SQL 全文で再作成する。
        // 削除された列を参照する索引の再作成は実行時に失敗する（＝トランザクションがロールバックされ安全）。
        foreach (var aux in rebuild.AuxiliaryObjects)
        {
            var createSql = aux.CreateSql.Trim();

            if (createSql.Length == 0)
            {
                continue;
            }

            // 文末のセミコロンを保証する（sqlite_master の sql はセミコロンを含まないため）
            sb.AppendLine(createSql.EndsWith(';') ? createSql : createSql + ";");
        }

        sb.AppendLine();
    }

    /// <summary>
    /// <c>CREATE TABLE</c> の本体（列定義 → PK → UNIQUE → FK のインライン）を書き出す。
    /// 列定義・PK 行・UNIQUE 行・FK 行は <see cref="SqliteDdlGenerator"/> と共有し、DDL 生成との整合を保つ。
    /// </summary>
    /// <param name="quotedTableName">出力するテーブル名（クォート済み。再構築では一時テーブル名）</param>
    /// <param name="constraintTableName">
    /// 制約名の基にするテーブル名（再構築ではリネーム後の実テーブル名）。
    /// 一意制約の合成名（<c>UQ_{表}_{列…}</c>）にも使うため、一時テーブル名は渡さない。
    /// </param>
    /// <param name="definition">合成後のテーブル定義（一意制約もここが正本）</param>
    /// <param name="foreignKeys">インライン出力する FK 仕様</param>
    private static void AppendCreateTableBody(
        StringBuilder sb,
        string quotedTableName,
        string constraintTableName,
        Entity definition,
        IReadOnlyList<TableRebuildForeignKey> foreignKeys
    )
    {
        sb.AppendLine($"CREATE TABLE {quotedTableName} (");

        // 末尾へまとめて出す制約行（PK → UNIQUE → FK）を収集する
        var trailingConstraints = new List<string>();

        var pks = definition.Columns.Where(c => c.IsPrimaryKey).ToList();

        if (pks.Count > 0)
        {
            trailingConstraints.Add(
                SqliteDdlGenerator.BuildPrimaryKeyConstraintLine(constraintTableName, pks)
            );
        }

        // 一意制約は合成後の定義（Entity.UniqueConstraints）から出す。構成列が解決できない制約
        // （選択した列削除で構成列が消えた等）は ResolveAll が黙って除外する。
        // 合成名 UQ_{表}_{列…} の基になるテーブル名は definition.TableName＝実テーブル名
        // （再構築の一時テーブル名ではない）ため、リネーム後に自然な制約名になる
        foreach (
            var unique in UniqueConstraintNaming.ResolveAll(definition, SqliteIdentifier.SafeName)
        )
        {
            trailingConstraints.Add(
                SqliteDdlGenerator.BuildUniqueConstraintLine(unique.Name, unique.ColumnNames)
            );
        }

        foreach (var fk in foreignKeys)
        {
            trailingConstraints.Add(
                SqliteDdlGenerator.BuildForeignKeyConstraintLine(
                    fk.ConstraintName,
                    fk.ChildColumns,
                    fk.ParentTable,
                    fk.ParentColumns,
                    fk.OnDelete,
                    fk.OnUpdate
                )
            );
        }

        // 列定義。後続の列行または末尾制約行が続く場合はカンマで区切る
        for (var i = 0; i < definition.Columns.Count; i++)
        {
            var line = SqliteDdlGenerator.BuildColumnDefinition(definition.Columns[i]);
            var hasMoreColumns = i < definition.Columns.Count - 1;

            if (hasMoreColumns || trailingConstraints.Count > 0)
            {
                line += ",";
            }

            sb.AppendLine(line);
        }

        for (var i = 0; i < trailingConstraints.Count; i++)
        {
            var isLast = i == trailingConstraints.Count - 1;
            sb.AppendLine(trailingConstraints[i] + (isLast ? string.Empty : ","));
        }

        sb.AppendLine(");");
    }

    // ---------------- セクション項目のディスパッチ実装 ----------------

    /// <summary>列追加（<c>ALTER TABLE ... ADD COLUMN</c>）を出力する</summary>
    protected override void AppendAddColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        // 列定義の組み立ては DDL 生成と共有する（先頭インデントのみ除去してインライン化）
        var definition = SqliteDdlGenerator.BuildColumnDefinition(col).TrimStart();
        sb.AppendLine(
            $"ALTER TABLE {SqliteIdentifier.Quote(item.TableName)} ADD COLUMN {definition};"
        );
    }

    /// <summary>テーブル削除（<c>DROP TABLE</c>）を出力する</summary>
    protected override void AppendDropTable(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine($"DROP TABLE {SqliteIdentifier.Quote(item.TableName)};");
    }

    // ---------------- 以下はプランナーが再構築へ畳めた項目では呼ばれない ----------------
    // 畳み込めなかった項目（例: 新規テーブルの AddTable が未選択のまま、そのテーブルへの AddForeignKey
    // だけが選択された場合）がセクション経由で渡されたとき、英語のスキップコメントで明示する

    /// <inheritdoc />
    protected override void AppendCreateTable(StringBuilder sb, SchemaDiffItem item) =>
        AppendNotApplicable(sb, SchemaDiffKind.AddTable, item.TableName);

    /// <inheritdoc />
    protected override void AppendAlterColumn(StringBuilder sb, SchemaDiffItem item) =>
        AppendNotApplicable(sb, SchemaDiffKind.AlterColumn, item.TableName);

    /// <inheritdoc />
    protected override void AppendDropColumn(StringBuilder sb, SchemaDiffItem item) =>
        AppendNotApplicable(sb, SchemaDiffKind.DropColumn, item.TableName);

    /// <inheritdoc />
    protected override void AppendAddForeignKey(StringBuilder sb, SchemaDiffItem item) =>
        AppendNotApplicable(sb, SchemaDiffKind.AddForeignKey, item.TableName);

    /// <inheritdoc />
    protected override void AppendDropForeignKey(StringBuilder sb, SchemaDiffItem item) =>
        AppendNotApplicable(sb, SchemaDiffKind.DropForeignKey, item.TableName);

    /// <inheritdoc />
    protected override void AppendAddUniqueConstraint(StringBuilder sb, SchemaDiffItem item) =>
        AppendNotApplicable(sb, SchemaDiffKind.AddUniqueConstraint, item.TableName);

    /// <inheritdoc />
    protected override void AppendDropUniqueConstraint(StringBuilder sb, SchemaDiffItem item) =>
        AppendNotApplicable(sb, SchemaDiffKind.DropUniqueConstraint, item.TableName);

    /// <inheritdoc />
    protected override void AppendSetTableDescription(StringBuilder sb, SchemaDiffItem item) =>
        AppendDescriptionUnsupported(sb, item.TableName);

    /// <inheritdoc />
    protected override void AppendSetColumnDescription(StringBuilder sb, SchemaDiffItem item) =>
        AppendDescriptionUnsupported(sb, item.TableName);

    /// <summary>再構築へ畳み込めなかった項目のスキップコメント（英語が正本）</summary>
    private static void AppendNotApplicable(
        StringBuilder sb,
        SchemaDiffKind kind,
        string tableName
    ) =>
        sb.AppendLine(
            $"-- Skipped '{kind}' on {tableName}: the target table is not part of this synchronization "
                + "(e.g. its creation is unselected) or the reference could not be resolved."
        );

    /// <summary>説明設定は SQLite にスキーマレベルの機構が無いため出力しない（防御コメント・英語が正本）</summary>
    private static void AppendDescriptionUnsupported(StringBuilder sb, string tableName) =>
        sb.AppendLine(
            $"-- Skipped: SQLite has no schema-level description mechanism ({tableName})."
        );
}
