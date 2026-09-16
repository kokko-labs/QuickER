using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.MySql;

/// <summary>選択済みの <see cref="SchemaDiffItem"/> から MySQL 用の DDL バッチを生成する</summary>
/// <remarks>
/// <para>
/// 依存関係による失敗を避けるため、以下の順序で出力する。文はすべて <c>;</c> で終端する。
/// <list type="number">
///   <item>AddTable</item>
///   <item>AddColumn</item>
///   <item>DropForeignKey（FK 依存列の型変更・列/テーブル削除より前に外す）</item>
///   <item>DropUniqueConstraint（構成列の定義変更・主キー変更より前に外す）</item>
///   <item>AlterPrimaryKey / Drop フェーズ（旧主キー制約の解除。旧主キー列の NULL 許容化を通すため列定義変更より前）</item>
///   <item>AlterColumn</item>
///   <item>AlterPrimaryKey / Add フェーズ（新主キー制約の付与。新主キー列の NOT NULL 化を済ませた後に行う）</item>
///   <item>DropColumn</item>
///   <item>DropTable</item>
///   <item>AddUniqueConstraint（FK が候補キーとして参照しうるため FK 追加より前に張る）</item>
///   <item>AddForeignKey</item>
///   <item>SetTableDescription / SetColumnDescription</item>
/// </list>
/// </para>
/// <para>
/// MySQL 固有の重要事項:
/// <list type="bullet">
///   <item>
///   列変更はすべて <c>MODIFY COLUMN</c> による列定義の完全再指定で行うが、定義の出どころが 2 系統ある。
///   <b>AlterColumn</b>（型・NULL 制約を変える操作）はモデル（<see cref="SchemaDiffItem.Column"/>）を正とした再指定で、
///   図に無い列属性（DEFAULT / AUTO_INCREMENT / 照合順序 / ON UPDATE / INVISIBLE / SRID / 生成列）は落ちる。
///   <b>SetColumnDescription / ReorderColumns</b>（末尾句だけを変える操作）はモデルを使わず、
///   <c>information_schema</c> から実行時点の実定義を再構成して COMMENT・位置だけを差し替える
///   （<c>AppendLiveColumnDefinitionLookup</c>）。
///   </item>
///   <item>テーブル説明は <c>ALTER TABLE ... COMMENT = '…'</c>（削除は空文字）で設定する。</item>
///   <item>
///   DropForeignKey は制約名既知なら <c>DROP FOREIGN KEY 名前</c> で直接外す。制約名不明時は MySQL に
///   DO ブロックが無いため、<c>information_schema</c> から制約名を引いてプリペアド動的 SQL で削除する。
///   </item>
///   <item>
///   AlterPrimaryKey の <c>DROP PRIMARY KEY</c> は主キーが無いテーブルではエラーになるため、
///   <c>information_schema</c> を逆引きして主キーが在るときだけプリペアド動的 SQL で実行する。
///   付与側は MySQL の主キー名が <c>PRIMARY</c> 固定のため <c>CONSTRAINT</c> 名を指定しない。
///   </item>
/// </list>
/// </para>
/// </remarks>
public sealed class MySqlSyncScriptBuilder : SyncScriptBuilderBase
{
    /// <summary>
    /// <c>information_schema</c> 検索のスキーマ絞り込み述語を組み立てる。
    /// </summary>
    /// <param name="schemaColumn">絞り込む列（<c>c.TABLE_SCHEMA</c> / <c>tc.CONSTRAINT_SCHEMA</c> 等）</param>
    /// <param name="tableName">操作対象のテーブル名（スキーマ修飾され得る）</param>
    /// <remarks>
    /// カタログ検索のスコープは、実際に <c>ALTER</c> する対象と同じスキーマでなければならない。
    /// 無条件に <c>DATABASE()</c> で絞ると、<c>other.t</c> を操作する文がカレント DB の同名テーブル
    /// <c>t</c> の定義（型・NULL 許容・DEFAULT）を引き当て、それを <c>other.t</c> へ焼き付けてしまう
    /// （同名が無ければ引き当て失敗＝何もせず成功報告）。
    /// </remarks>
    private static string SchemaPredicate(string schemaColumn, string tableName)
    {
        var schema = MySqlIdentifier.SchemaNameOnly(tableName);

        return schema is null
            ? $"{schemaColumn} = DATABASE()"
            : $"{schemaColumn} = '{MySqlIdentifier.EscapeStringLiteral(schema)}'";
    }

    // ---------------- 各種 DDL ----------------

    /// <summary>CREATE TABLE 文（主キー制約・一意制約を含む）を生成する</summary>
    /// <remarks>
    /// 制約行は DDL 生成と同じヘルパーで組み立てる（同期で作ったテーブルにだけ UNIQUE が無い、という
    /// 食い違いを構造的に防ぐ）。
    /// </remarks>
    protected override void AppendCreateTable(StringBuilder sb, SchemaDiffItem item)
    {
        var e = item.Entity!;
        sb.AppendLine($"CREATE TABLE {MySqlIdentifier.Quote(item.TableName)} (");

        // 列定義の末尾カンマ判定に「後続制約行の有無」が必要なため、制約行を先に組み立てる
        var constraintLines = TableConstraintLineBuilder.Build(
            e,
            item.TableName,
            MySqlIdentifier.QuoteSimple,
            name => $"`{MySqlIdentifier.Escape(name)}`",
            MySqlIdentifier.SafeName
        );

        for (var i = 0; i < e.Columns.Count; i++)
        {
            var col = e.Columns[i];
            var line =
                $"    {MySqlIdentifier.QuoteSimple(col.Name)} {col.DataType} {SyncScriptBuilderHelper.GetNullabilityClause(col)}";

            // 後続のカラム行、または制約行（PRIMARY KEY / UNIQUE）が続く場合は区切りのカンマを付ける
            if (i < e.Columns.Count - 1 || constraintLines.Count > 0)
            {
                line += ",";
            }

            sb.AppendLine(line);
        }

        TableConstraintLineBuilder.Append(sb, constraintLines);

        sb.AppendLine(");");
    }

    /// <summary>ALTER TABLE ... ADD COLUMN（列追加）文を生成する</summary>
    protected override void AppendAddColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"ADD COLUMN {MySqlIdentifier.QuoteSimple(col.Name)} {BuildColumnDefinition(col)};"
        );
    }

    /// <summary>ALTER TABLE ... MODIFY COLUMN（列定義変更）文を生成する</summary>
    /// <remarks>
    /// <para>
    /// MySQL の MODIFY は列定義を完全に再指定するため、型・NULL 制約に加えて
    /// 対象列に説明があれば COMMENT も含める（含めないと既存コメントが消える）。
    /// </para>
    /// <para>
    /// <b>MODIFY は図に無い列属性を落とす</b>: 意味モデルが持たない DEFAULT / AUTO_INCREMENT /
    /// 照合順序 / ON UPDATE / INVISIBLE / SRID / 生成列は、この再指定で実 DB から消える。
    /// AlterColumn は型そのものを変える操作＝モデルが正なので、これは意図した挙動（既定未選択の差分項目）。
    /// 末尾句だけを変える SetColumnDescription / ReorderColumns は逆にライブ再構成を使い、
    /// 列属性を温存する（<c>AppendLiveColumnDefinitionLookup</c>）。
    /// </para>
    /// </remarks>
    protected override void AppendAlterColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"MODIFY COLUMN {MySqlIdentifier.QuoteSimple(col.Name)} {BuildColumnDefinition(col)};"
        );
    }

    /// <summary>主キー変更の解除フェーズ（旧主キー制約の DROP）文を生成する</summary>
    /// <remarks>
    /// <c>ALTER TABLE ... DROP PRIMARY KEY</c> は主キーが無いテーブルに対してエラーになるため、
    /// <c>information_schema.TABLE_CONSTRAINTS</c> を逆引きし、主キーが在るときだけプリペアド動的 SQL で
    /// 実行する（無ければ無害な <c>DO 0</c>）。
    /// </remarks>
    protected override void AppendDropPrimaryKey(StringBuilder sb, SchemaDiffItem item)
    {
        // クォートした名前をプリペアド動的 SQL の文字列リテラルへ埋めるため、リテラルエスケープ込みのヘルパーを通す
        var table = MySqlIdentifier.QuoteForDynamicSql(item.TableName);
        var tableName = MySqlIdentifier.EscapeStringLiteral(
            MySqlIdentifier.TableNameOnly(item.TableName)
        );

        // 主キーの有無を information_schema で確かめてからプリペアド動的 SQL で外す。
        // 接続文字列に AllowUserVariables=true が付与されている前提（Executor 側で付与）。
        // SELECT ... INTO は該当行が無いとユーザー変数を書き換えないため、事前に NULL で初期化する。
        sb.AppendLine("SET @pk = NULL;");
        sb.AppendLine("SELECT tc.CONSTRAINT_NAME INTO @pk");
        sb.AppendLine("FROM information_schema.TABLE_CONSTRAINTS tc");
        sb.AppendLine(
            $"WHERE {SchemaPredicate("tc.CONSTRAINT_SCHEMA", item.TableName)} "
                + $"AND tc.TABLE_NAME = '{tableName}' "
                + "AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY' LIMIT 1;"
        );
        // 主キーが無ければ無害な DO 0 を実行する
        sb.AppendLine(
            $"SET @sql = IF(@pk IS NULL, 'DO 0', 'ALTER TABLE {table} DROP PRIMARY KEY');"
        );
        sb.AppendLine("PREPARE stmt FROM @sql;");
        sb.AppendLine("EXECUTE stmt;");
        sb.AppendLine("DEALLOCATE PREPARE stmt;");
    }

    /// <summary>主キー変更の付与フェーズ（新主キー制約の ADD）文を生成する</summary>
    /// <remarks>
    /// 新しい主キー構成は <see cref="SchemaDiffItem.Entity"/>（target 側エンティティ）の主キー列を実効順
    /// （<see cref="Entity.GetPrimaryKeyColumnsInOrder"/>）で読む。
    /// MySQL の主キー名は <c>PRIMARY</c> 固定のため <c>CONSTRAINT</c> 名は指定しない。
    /// 主キー列が 1 つも無い場合（主キーの解除のみ）は付与文を出さない。
    /// </remarks>
    protected override void AppendAddPrimaryKey(StringBuilder sb, SchemaDiffItem item)
    {
        var pks = item.Entity?.GetPrimaryKeyColumnsInOrder() ?? [];

        // 新しい主キー列が無い（＝主キーの解除のみ）場合は付与文を出さない
        if (pks.Count == 0)
        {
            return;
        }

        var pkCols = string.Join(", ", pks.Select(p => MySqlIdentifier.QuoteSimple(p.Name)));
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} ADD PRIMARY KEY ({pkCols});"
        );
    }

    /// <summary>一意制約を追加する ALTER TABLE ... ADD CONSTRAINT ... UNIQUE 文を生成する</summary>
    protected override void AppendAddUniqueConstraint(StringBuilder sb, SchemaDiffItem item)
    {
        if (item.UniqueConstraintColumns.Count == 0)
        {
            sb.AppendLine(SyncScriptBuilderHelper.BuildUniqueConstraintSkipComment(item));
            return;
        }

        var name = UniqueConstraintNaming.Resolve(
            item.UniqueConstraintName,
            item.TableName,
            item.UniqueConstraintColumns,
            MySqlIdentifier.SafeName
        );
        var cols = string.Join(
            ", ",
            item.UniqueConstraintColumns.Select(MySqlIdentifier.QuoteSimple)
        );
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} ADD CONSTRAINT `{MySqlIdentifier.Escape(name)}` "
                + $"UNIQUE ({cols});"
        );
    }

    /// <summary>一意制約を削除する ALTER TABLE ... DROP INDEX 文を生成する</summary>
    /// <remarks>
    /// MySQL の一意制約は実体が一意インデックスで、<c>DROP CONSTRAINT</c> は 8.0.19 未満で受け付けられない。
    /// どの版でも通る <c>DROP INDEX</c> 構文を使う（制約名＝インデックス名）。
    /// </remarks>
    protected override void AppendDropUniqueConstraint(StringBuilder sb, SchemaDiffItem item)
    {
        var name = UniqueConstraintNaming.Resolve(
            item.UniqueConstraintName,
            item.TableName,
            item.UniqueConstraintColumns,
            MySqlIdentifier.SafeName
        );
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"DROP INDEX {MySqlIdentifier.QuoteSimple(name)};"
        );
    }

    /// <summary>ALTER TABLE ... DROP COLUMN（列削除）文を生成する</summary>
    protected override void AppendDropColumn(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"DROP COLUMN {MySqlIdentifier.QuoteSimple(item.ColumnName!)};"
        );
    }

    /// <summary>DROP TABLE（テーブル削除）文を生成する</summary>
    protected override void AppendDropTable(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine($"DROP TABLE {MySqlIdentifier.Quote(item.TableName)};");
    }

    /// <summary>外部キー制約を追加する ALTER TABLE 文を生成する</summary>
    protected override void AppendAddForeignKey(StringBuilder sb, SchemaDiffItem item)
    {
        if (item.ChildEntity is null || item.ParentEntity is null)
        {
            return;
        }

        var columnPairs = SyncScriptBuilderHelper.ResolveColumnPairs(item);

        // 構成列が特定できない場合は不正な DDL を出さず、コメントでスキップを明示する
        if (columnPairs.Count == 0)
        {
            sb.AppendLine(SyncScriptBuilderHelper.BuildForeignKeySkipComment(item));
            return;
        }

        var childTbl = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTbl = SchemaDiffService.NormalizeTable(item.ParentEntity);
        var fkName = string.IsNullOrWhiteSpace(item.Relationship?.ConstraintName)
            ? $"FK_{MySqlIdentifier.SafeName(childTbl)}_{MySqlIdentifier.SafeName(parentTbl)}"
            : item.Relationship.ConstraintName!;
        var referentialActions = SyncScriptBuilderHelper.BuildReferentialActionClause(
            item.Relationship
        );
        // 複合外部キーは構成列を宣言順にカンマ区切りで並べる（単列なら従来と同一の出力）
        var childColumnList = string.Join(
            ", ",
            ForeignKeyColumnPairResolver
                .ChildColumns(columnPairs)
                .Select(MySqlIdentifier.QuoteSimple)
        );
        var parentColumnList = string.Join(
            ", ",
            ForeignKeyColumnPairResolver
                .ParentColumns(columnPairs)
                .Select(MySqlIdentifier.QuoteSimple)
        );

        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(childTbl)} ADD CONSTRAINT `{MySqlIdentifier.Escape(fkName)}` "
                + $"FOREIGN KEY ({childColumnList}) "
                + $"REFERENCES {MySqlIdentifier.Quote(parentTbl)} ({parentColumnList}){referentialActions};"
        );
    }

    /// <summary>外部キー制約を削除する ALTER TABLE ... DROP FOREIGN KEY 文を生成する</summary>
    /// <remarks>
    /// 制約名が判明していれば直接 <c>DROP FOREIGN KEY</c> する。不明な場合は MySQL に DO ブロックが無いため、
    /// <c>information_schema.REFERENTIAL_CONSTRAINTS</c> から制約名を引いてプリペアド動的 SQL で削除する。
    /// FK が見つからない場合は <c>DO 0</c> を実行して無害に済ませる。
    /// </remarks>
    protected override void AppendDropForeignKey(StringBuilder sb, SchemaDiffItem item)
    {
        if (item.ChildEntity is null || item.ParentEntity is null)
        {
            return;
        }

        var childTbl = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTbl = SchemaDiffService.NormalizeTable(item.ParentEntity);

        // 制約名が判明している場合は直接 DROP FOREIGN KEY する
        if (!string.IsNullOrWhiteSpace(item.ForeignKeyName))
        {
            sb.AppendLine(
                $"ALTER TABLE {MySqlIdentifier.Quote(childTbl)} "
                    + $"DROP FOREIGN KEY {MySqlIdentifier.QuoteSimple(item.ForeignKeyName)};"
            );
            return;
        }

        // 制約名不明時は information_schema を逆引きしてプリペアド動的 SQL で削除する。
        // 接続文字列に AllowUserVariables=true が付与されている前提（Executor 側で付与）。
        var childName = MySqlIdentifier.EscapeStringLiteral(
            MySqlIdentifier.TableNameOnly(childTbl)
        );
        var parentName = MySqlIdentifier.EscapeStringLiteral(
            MySqlIdentifier.TableNameOnly(parentTbl)
        );
        // クォートした名前を CONCAT の文字列リテラルへ埋めるため、リテラルエスケープ込みのヘルパーを通す
        var childQuoted = MySqlIdentifier.QuoteForDynamicSql(childTbl);
        // 親がスキーマ修飾されているときだけ、参照先スキーマでも絞る（無修飾時の出力は従来どおり）
        var parentSchema = MySqlIdentifier.SchemaNameOnly(parentTbl) is null
            ? string.Empty
            : $" AND {SchemaPredicate("rc.UNIQUE_CONSTRAINT_SCHEMA", parentTbl)}";

        // SELECT ... INTO は該当行が無いとユーザー変数を書き換えないため、事前に NULL で初期化する
        // （初期化しないと 2 件目以降の削除が直前の FK 名を読んで別の制約を落とす）
        sb.AppendLine("SET @fk = NULL;");
        sb.AppendLine("SELECT rc.CONSTRAINT_NAME INTO @fk");
        sb.AppendLine("FROM information_schema.REFERENTIAL_CONSTRAINTS rc");
        // 同じ親子ペアに FK が 2 本ある構成では LIMIT 1 の取り出しが不定になるため、名前順で決定化する
        sb.AppendLine(
            $"WHERE {SchemaPredicate("rc.CONSTRAINT_SCHEMA", childTbl)} "
                + $"AND rc.TABLE_NAME = '{childName}' "
                + $"AND rc.REFERENCED_TABLE_NAME = '{parentName}'{parentSchema} "
                + "ORDER BY rc.CONSTRAINT_NAME LIMIT 1;"
        );
        // FK が見つからなければ無害な DO 0 を実行する。
        // 制約名は実行時の値なので、クォートの終端文字（`）は REPLACE で二重化してから埋める
        // （`a`b` 形の名前がバッククォートを閉じて構文エラーになるのを防ぐ）
        sb.AppendLine(
            $"SET @sql = IF(@fk IS NULL, 'DO 0', CONCAT('ALTER TABLE {childQuoted} "
                + "DROP FOREIGN KEY `', REPLACE(@fk, '`', '``'), '`'));"
        );
        sb.AppendLine("PREPARE stmt FROM @sql;");
        sb.AppendLine("EXECUTE stmt;");
        sb.AppendLine("DEALLOCATE PREPARE stmt;");
    }

    // ---------------- 説明 (COMMENT) ----------------

    /// <summary>テーブルの説明（ALTER TABLE ... COMMENT）設定文を生成する</summary>
    /// <remarks>新値が空なら空文字コメントを設定して説明を削除する</remarks>
    protected override void AppendSetTableDescription(StringBuilder sb, SchemaDiffItem item)
    {
        var newVal = item.NewDescription ?? string.Empty;
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"COMMENT = '{MySqlIdentifier.EscapeStringLiteral(newVal)}';"
        );
    }

    /// <summary>カラムの説明を、実行時に再構成した列定義の MODIFY COLUMN で設定する文を生成する</summary>
    /// <remarks>
    /// 列定義はモデルからではなく <c>information_schema</c> から実行時点の実定義として再構成し
    /// （<see cref="AppendLiveColumnDefinitionLookup"/>）、末尾の COMMENT だけを新しい値へ差し替える。
    /// 新値は生成時に確定するため <c>@cmt</c> へ代入し、リテラル化は実行時の <c>QUOTE()</c> が担う。
    /// </remarks>
    protected override void AppendSetColumnDescription(StringBuilder sb, SchemaDiffItem item)
    {
        var columnName = item.ColumnName!;
        AppendLiveColumnDefinitionLookup(sb, item.TableName, columnName);

        // ライブ再構成が拾った既存コメントを、同期する新しい説明で上書きする（順序が逆だと新値が消える）。
        // QUOTE() が実行時に安全な文字列リテラルへ再引用するため、ここでのエスケープは SET 文の 1 段だけ
        var newVal = MySqlIdentifier.EscapeStringLiteral(item.NewDescription ?? string.Empty);
        sb.AppendLine($"SET @cmt = '{newVal}';");
        AppendModifyColumnStatement(sb, item.TableName, columnName, position: null);
    }

    // ---------------- 実行時の列定義再構成（ライブ再構成） ----------------

    /// <summary>
    /// <c>information_schema.COLUMNS</c> から実行時点の列定義を再構成し、ユーザー変数
    /// <c>@def</c>（COMMENT を除く列定義）と <c>@cmt</c>（現在のコメント）へ入れる文を書き出す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// MySQL の <c>MODIFY COLUMN</c> は列定義の完全再指定なので、モデルが持つ型・NULL 制約だけから
    /// 復元すると実 DB にある DEFAULT / AUTO_INCREMENT / 照合順序 / ON UPDATE / INVISIBLE / SRID /
    /// 生成列が消える。MySQL の DDL は暗黙コミットで取り消せないため、実行時点のカタログから
    /// 定義を組み立て直し、変えたい末尾句（COMMENT・位置）だけを差し替える。
    /// </para>
    /// <para>
    /// <b>2 段構えの理由</b>: <c>GENERATION_EXPRESSION</c> と式既定の <c>COLUMN_DEFAULT</c> は
    /// 「文字列リテラル用にエスケープした形」（<c>'</c> → <c>\'</c>・<c>\</c> → <c>\\</c>）で格納されており、
    /// そのまま SQL へ埋めると構文エラーになる。<c>REPLACE</c> の逐次適用では
    /// <c>\\'</c> の並びを正しく解けないため、リテラルとして 1 度パースさせて復元する
    /// （サーバー自身のパーサを使うので規則が二重定義にならない）。
    /// </para>
    /// <para>
    /// 列が実行時に存在するかは <c>@exists</c> が持つ（行が引けたときだけ 1 が入る）。存在しなければ
    /// 呼び出し側が無害な <c>DO 0</c> へ分岐する（生成時の静的なスキップコメントでは実行時の存否を
    /// 語れない）。<c>@exists</c> と <c>@def</c> を分けるのは、<c>@def</c> の <c>NULL</c> だけでは
    /// 「列が無い」と「列は在るのに再構成できなかった」を区別できず、後者を無言で成功にしてしまうため。
    /// </para>
    /// </remarks>
    private static void AppendLiveColumnDefinitionLookup(
        StringBuilder sb,
        string tableName,
        string columnName
    )
    {
        // カタログ検索は素の名前を文字列リテラルとして渡す（クォートではなくリテラルエスケープ）
        var table = MySqlIdentifier.EscapeStringLiteral(MySqlIdentifier.TableNameOnly(tableName));
        var column = MySqlIdentifier.EscapeStringLiteral(columnName);
        var where =
            $"WHERE {SchemaPredicate("c.TABLE_SCHEMA", tableName)} AND c.TABLE_NAME = '{table}' "
            + $"AND c.COLUMN_NAME = '{column}' LIMIT 1;";

        // SELECT ... INTO は該当行が無いとユーザー変数を書き換えないため、事前に初期化する
        // （初期化しないと 2 列目以降が直前の列の定義を読んで別の列の属性を焼き付ける）。
        // @exists はその性質をそのまま使った存在フラグ＝行が引けたときだけ 1 が書き込まれる
        sb.AppendLine("SET @gen = '';");
        sb.AppendLine("SET @dfx = '';");
        sb.AppendLine("SET @cmt = '';");
        sb.AppendLine("SET @exists = 0;");

        // 1 段目: エスケープ済みで格納されている生成列の式・式既定と、現在のコメントを取り出す
        sb.AppendLine(
            "SELECT 1, c.GENERATION_EXPRESSION, "
                + "IF(INSTR(c.EXTRA, 'DEFAULT_GENERATED') > 0, IFNULL(c.COLUMN_DEFAULT, ''), ''), "
                + "c.COLUMN_COMMENT"
        );
        sb.AppendLine("INTO @exists, @gen, @dfx, @cmt");
        sb.AppendLine("FROM information_schema.COLUMNS c");
        sb.AppendLine(where);

        // エスケープされた 2 つの式を、文字列リテラルとして 1 度パースさせて元の SQL 片へ戻す
        sb.AppendLine(
            "SET @sql = CONCAT('SELECT ''', IFNULL(@gen, ''), ''', ''', "
                + "IFNULL(@dfx, ''), ''' INTO @gen, @dfx');"
        );
        AppendExecutePrepared(sb);

        // 2 段目: 復元した式を織り込みつつ列定義（COMMENT を除く）を組み立てる
        sb.AppendLine("SET @def = NULL;");
        sb.AppendLine("SELECT CONCAT(");
        sb.AppendLine("c.COLUMN_TYPE,");
        // 照合順序は文字列型にだけ付く（非文字列型は COLLATION_NAME が NULL）
        sb.AppendLine("IF(c.COLLATION_NAME IS NULL, '', CONCAT(' COLLATE ', c.COLLATION_NAME)),");
        // 生成列は式と VIRTUAL / STORED の別を復元する（DEFAULT_GENERATED とは別物なので文字列判定を分ける）
        sb.AppendLine(
            "IF(@gen = '', '', CONCAT(' GENERATED ALWAYS AS (', @gen, ') ', "
                + "IF(INSTR(c.EXTRA, 'STORED GENERATED') > 0, 'STORED', 'VIRTUAL'))),"
        );
        sb.AppendLine("IF(c.IS_NULLABLE = 'YES', ' NULL', ' NOT NULL'),");
        // 空間列は空間参照系を保つ（SRS_ID が非 NULL のときだけ SRID を付ける）
        sb.AppendLine("IF(c.SRS_ID IS NULL, '', CONCAT(' SRID ', c.SRS_ID)),");
        // DEFAULT は 4 分岐。CURRENT_TIMESTAMP の判定を DEFAULT_GENERATED より先に置くのは、
        // DEFAULT CURRENT_TIMESTAMP にも EXTRA へ DEFAULT_GENERATED が付くため（実測: 8.0.46 / 8.4）。
        // 先に判定しないと式既定として ' DEFAULT (CURRENT_TIMESTAMP(6))' の括弧付きで出力され、
        // MySQL がこれを now(6) へ正規化してカタログ上の既定表記が黙って変わる
        sb.AppendLine("IF(c.COLUMN_DEFAULT IS NULL, '',");
        sb.AppendLine(
            "IF(c.DATA_TYPE IN ('timestamp', 'datetime') "
                + "AND (UPPER(c.COLUMN_DEFAULT) = 'CURRENT_TIMESTAMP' "
                + "OR UPPER(c.COLUMN_DEFAULT) LIKE 'CURRENT\\_TIMESTAMP(%)'), "
                + "CONCAT(' DEFAULT ', c.COLUMN_DEFAULT),"
        );
        sb.AppendLine(
            "IF(INSTR(c.EXTRA, 'DEFAULT_GENERATED') > 0, CONCAT(' DEFAULT (', @dfx, ')'),"
        );
        // リテラル既定がそのままでは文字列リテラルにならない型は生出力する。
        // BIT は b'1' 形、BINARY / VARBINARY は 16 進テキスト 0x6162 形で格納されており、
        // QUOTE すると前者は文字列 "b'1'"、後者は文字列 "0x6162" に化ける
        // （varbinary は黙って既定値が変わり、binary(n) は ERROR 1067 でスクリプトが中断する）。
        // BLOB / TEXT / GEOMETRY / JSON はリテラル既定を持てず（ERROR 1101）式既定のみなので、
        // DEFAULT_GENERATED の分岐で足りる＝この一覧に含めない
        sb.AppendLine(
            "IF(c.DATA_TYPE IN ('bit', 'binary', 'varbinary'), CONCAT(' DEFAULT ', c.COLUMN_DEFAULT),"
        );
        sb.AppendLine("CONCAT(' DEFAULT ', QUOTE(c.COLUMN_DEFAULT)))))),");
        sb.AppendLine("IF(INSTR(c.EXTRA, 'INVISIBLE') > 0, ' INVISIBLE', ''),");
        sb.AppendLine("IF(INSTR(c.EXTRA, 'auto_increment') > 0, ' AUTO_INCREMENT', ''),");
        // ON UPDATE は精度込みで EXTRA から取る。EXTRA には後続の属性（INVISIBLE）が並び得るため
        // 最初の空白までで切る（ON UPDATE の値そのものは空白を含まない）
        sb.AppendLine(
            "IF(INSTR(c.EXTRA, 'on update ') > 0, CONCAT(' ON UPDATE ', "
                + "SUBSTRING_INDEX(SUBSTRING(c.EXTRA, INSTR(c.EXTRA, 'on update ') + 10), ' ', 1)), '')"
        );
        sb.AppendLine(") INTO @def");
        sb.AppendLine("FROM information_schema.COLUMNS c");
        sb.AppendLine(where);
    }

    /// <summary>
    /// 再構成済みの <c>@def</c> / <c>@cmt</c> から <c>MODIFY COLUMN</c> をプリペアド動的 SQL で実行する文を書き出す。
    /// </summary>
    /// <param name="position">
    /// 位置指定句（<c>" AFTER `x`"</c> / <c>" FIRST"</c>。動的 SQL のリテラル内へ入るため
    /// 識別子はリテラルエスケープ済みであること）。位置を変えないときは <c>null</c>。
    /// </param>
    private static void AppendModifyColumnStatement(
        StringBuilder sb,
        string tableName,
        string columnName,
        string? position
    )
    {
        // クォートした名前を CONCAT の文字列リテラルへ埋めるため、リテラルエスケープ込みのヘルパーを通す
        var table = MySqlIdentifier.QuoteForDynamicSql(tableName);
        var column = MySqlIdentifier.QuoteSimpleForDynamicSql(columnName);
        var positionArg = position is null ? string.Empty : $", '{position}'";
        // 失敗文の本文（固定文は英語が正本）。PREPARE が構文エラーで落ちることで確実に中断させる
        var failure = MySqlIdentifier.EscapeStringLiteral(
            $"QuickER sync: {tableName}.{columnName} definition could not be rebuilt"
        );

        // 3 分岐にするのは、@def が NULL になる理由が 2 通りあり意味が正反対だから。
        //   列が実行時に無い（@exists = 0）  → 何もしないのが正しい＝無害な DO 0
        //   列は在るのに再構成できた定義が NULL（@exists = 1）→ 列順同期なら移動列の 1 本だけが
        //     畳まれて中途半端な列順のまま「成功」になる。実行できない文を組み立てて必ず失敗させる
        //     （SIGNAL はプリペアド文では使えない＝ERROR 1295。エラー文へ本文がそのまま載る形を採る）
        sb.AppendLine(
            $"SET @sql = IF(@def IS NOT NULL, CONCAT('ALTER TABLE {table} "
                + $"MODIFY COLUMN {column} ', @def, ' COMMENT ', QUOTE(@cmt){positionArg}), "
                + $"IF(@exists = 0, 'DO 0', '{failure}'));"
        );
        AppendExecutePrepared(sb);
    }

    /// <summary><c>@sql</c> に組み立てた文をプリペアド実行して解放する 3 文を書き出す</summary>
    private static void AppendExecutePrepared(StringBuilder sb)
    {
        sb.AppendLine("PREPARE stmt FROM @sql;");
        sb.AppendLine("EXECUTE stmt;");
        sb.AppendLine("DEALLOCATE PREPARE stmt;");
    }

    // ---------------- 列順変更 (MODIFY ... AFTER) ----------------

    /// <summary>ネイティブ列順変更（<c>ALTER TABLE ... MODIFY COLUMN ... AFTER</c>）を生成する</summary>
    /// <remarks>
    /// <para>
    /// テーブルごとに見出しコメントを付け、各列を「直前に置く列の直後（先頭なら <c>FIRST</c>）」へ移す
    /// <c>MODIFY COLUMN</c> を出力する。位置しか変えない操作なので、列定義は
    /// <see cref="AppendLiveColumnDefinitionLookup"/> で実 DB から再構成し、COMMENT もライブの値
    /// （<c>@cmt</c>）を温存する（モデル再指定では図に無い列属性を落とす）。
    /// </para>
    /// <para>
    /// MySQL の <c>MODIFY</c> による位置変更は内部的にテーブルコピー（メタデータのみの高速 DDL にはならない）
    /// になり得る点に注意する。移動列数はプランナーが最長増加部分列で最小化している。
    /// </para>
    /// </remarks>
    protected override void AppendReorders(StringBuilder sb, SyncPlan plan)
    {
        foreach (var reorder in plan.Reorders)
        {
            // 見出し（固定文は英語が正本）。テーブル名は自由入力なので改行・制御文字を畳む
            sb.AppendLine(
                $"-- ===== ReorderColumns: {SqlComment.Sanitize(reorder.TableName)} ====="
            );

            foreach (var move in reorder.Moves)
            {
                // AfterColumn が null なら先頭（FIRST）、それ以外は指定列の直後（AFTER 列名）。
                // 位置句は動的 SQL のリテラル内へ入るため識別子はリテラルエスケープ込みでクォートする
                var position = move.AfterColumn is null
                    ? " FIRST"
                    : $" AFTER {MySqlIdentifier.QuoteSimpleForDynamicSql(move.AfterColumn)}";

                AppendLiveColumnDefinitionLookup(sb, reorder.TableName, move.Column.Name);
                AppendModifyColumnStatement(sb, reorder.TableName, move.Column.Name, position);
            }

            sb.AppendLine();
        }
    }

    /// <summary>モデルを正とした列定義（型・NULL 制約・COMMENT）を組み立てる</summary>
    /// <remarks>
    /// AddColumn（新しい列＝ライブに定義が無い）と AlterColumn（型を変える＝モデルが正）で用いる。
    /// 説明が設定されていれば COMMENT を付与し、既存コメントの消失を防ぐ。
    /// 末尾句だけを変える経路（SetColumnDescription / ReorderColumns）では使わない
    /// ——モデルに無い列属性を落とすため、そちらはライブ再構成を通す。
    /// </remarks>
    private static string BuildColumnDefinition(Column column)
    {
        var sb = new StringBuilder();
        sb.Append(column.DataType);
        sb.Append(' ');
        sb.Append(SyncScriptBuilderHelper.GetNullabilityClause(column));
        // インライン COMMENT 句は DDL 生成と同じ表記を共有する（説明が空なら付かない）
        sb.Append(MySqlIdentifier.ColumnCommentClause(column.Description));
        return sb.ToString();
    }
}
