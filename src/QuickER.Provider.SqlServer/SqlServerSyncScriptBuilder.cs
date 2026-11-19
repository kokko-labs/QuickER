using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.SqlServer;

/// <summary>実行計画（<see cref="SyncPlan"/>）の文から SQL Server 用の T-SQL バッチを生成する</summary>
/// <remarks>
/// 依存関係による失敗を避けるため、以下の順序で出力する
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
///   <item>SetTableDescription / SetColumnDescription（拡張プロパティ MS_Description）</item>
/// </list>
/// </remarks>
public sealed class SqlServerSyncScriptBuilder : SyncScriptBuilderBase
{
    /// <inheritdoc />
    protected override IdentifierLengthLimit IdentifierLimit => SqlIdentifier.Limit;

    /// <inheritdoc/>
    protected override string DialectName => SqlServerProvider.ProviderName;

    // ---------------- 各種 DDL ----------------

    /// <summary>CREATE TABLE 文（主キー制約・一意制約を含む）を生成する</summary>
    /// <remarks>
    /// 制約行は DDL 生成と同じヘルパーで組み立てる（同期で作ったテーブルにだけ UNIQUE が無い、という
    /// 食い違いを構造的に防ぐ）。
    /// </remarks>
    protected override void AppendCreateTable(StringBuilder sb, CreateTableStep step)
    {
        var e = step.Definition;
        sb.AppendLine($"CREATE TABLE {SqlIdentifier.Bracket(step.TableName)} (");

        // 列定義の末尾カンマ判定に「後続制約行の有無」が必要なため、制約行を先に組み立てる
        var constraintLines = TableConstraintLineBuilder.Build(
            e,
            step.TableName,
            SqlIdentifier.Bracket(step.TableName),
            SqlIdentifier.BracketSimple,
            name => $"[{SqlIdentifier.Escape(name)}]",
            step.UniqueConstraintNames,
            IdentifierLimit,
            DialectName,
            UnsupportedFeature.Sync
        );

        for (var i = 0; i < e.Columns.Count; i++)
        {
            var col = e.Columns[i];

            // 計算列は式が図に無い＝ここで出すのは普通の列。手で式を足す必要があることを添える
            ComputedColumnComment.Append(sb, col, "    ");

            // 採番の定義は図に無い＝ここで出すのは普通の列。扱いの表が注記とする方言だけ書き添える
            IdentityColumnComment.Append(sb, col, "    ", DialectName, UnsupportedFeature.Sync);

            var line =
                $"    {SqlIdentifier.BracketSimple(col.Name)} {col.DataType} {SyncScriptBuilderHelper.GetNullabilityClause(col)}";

            // 後続のカラム行、または制約行（PRIMARY KEY / UNIQUE）が続く場合は区切りのカンマを付ける
            if (i < e.Columns.Count - 1 || constraintLines.Count > 0)
            {
                line += ",";
            }

            sb.AppendLine(line);
        }

        TableConstraintLineBuilder.Append(sb, constraintLines);

        sb.AppendLine(");");
        sb.AppendLine("GO");
    }

    /// <summary>ALTER TABLE ... ADD（列追加）文を生成する</summary>
    protected override void AppendAddColumn(StringBuilder sb, AddColumnStep step)
    {
        var col = step.Column;

        // 計算列は式が図に無い＝ここで足すのは普通の列。手で式を足す必要があることを添える
        ComputedColumnComment.Append(sb, col, string.Empty);

        // 採番の定義は図に無い＝ここで足すのは普通の列。扱いの表が注記とする方言だけ書き添える
        IdentityColumnComment.Append(sb, col, string.Empty, DialectName, UnsupportedFeature.Sync);

        sb.AppendLine(
            $"ALTER TABLE {SqlIdentifier.Bracket(step.TableName)} "
                + $"ADD {SqlIdentifier.BracketSimple(col.Name)} {col.DataType} {SyncScriptBuilderHelper.GetNullabilityClause(col)};"
        );
        sb.AppendLine("GO");
    }

    /// <summary>ALTER TABLE ... ALTER COLUMN（列定義変更）文を生成する</summary>
    /// <remarks>
    /// <para>
    /// 図に無い列属性を実行時に読み直して保つ。<c>ALTER COLUMN</c> は照合順序を書かなければ DB の既定へ戻すので、
    /// 新しい型が文字列型なら <c>sys.columns.collation_name</c> を読んで書き添える。型が変わるときは、DEFAULT 制約
    /// （<c>sys.default_constraints</c>）を読み、外して型を変えてから同じ名前・同じ定義で付け直す（残したままだと
    /// エラー 5074 で断られる）。付け直しでは既定値が新しい型に合うかを確かめないので、合わなくても同期は通り、
    /// 既定値を使う行の追加で失敗する（計画が事前に警告する）。NULL 許容だけの変更では DEFAULT 制約に触れない。
    /// </para>
    /// <para>
    /// 実行時に読んだ名前は <c>QUOTENAME</c> を通し、文は <c>nvarchar(max)</c> の変数に組み立てて <c>sp_executesql</c> で
    /// 実行する。表は <c>OBJECT_ID</c> で <c>ALTER TABLE</c> と同じ 1 つの表に絞る。
    /// </para>
    /// </remarks>
    protected override void AppendAlterColumn(StringBuilder sb, AlterColumnStep step)
    {
        var col = step.Column;
        var nullability = SyncScriptBuilderHelper.GetNullabilityClause(col);
        var keepsCollation = IsCollatableType(col.DataType);
        var typeChanges =
            step.OldColumn is null
            || !DbTypeText.AreEquivalent(step.OldColumn.DataType, col.DataType);

        // 照合順序も DEFAULT 制約も関わらない変更（文字列型でない列の NULL 許容だけの変更）は、素の ALTER COLUMN で足りる
        if (!keepsCollation && !typeChanges)
        {
            sb.AppendLine(
                $"ALTER TABLE {SqlIdentifier.Bracket(step.TableName)} "
                    + $"ALTER COLUMN {SqlIdentifier.BracketSimple(col.Name)} {col.DataType} {nullability};"
            );
            sb.AppendLine("GO");
            return;
        }

        // 動的 SQL の文字列リテラルへ埋める名前・型は、リテラルエスケープ込みで組む
        var table = SqlIdentifier.QuoteForDynamicSql(step.TableName);
        var column = SqlIdentifier.EscapeStringLiteral(SqlIdentifier.BracketSimple(col.Name));
        var columnName = SqlIdentifier.EscapeStringLiteral(col.Name);
        var type = SqlIdentifier.EscapeStringLiteral(col.DataType);

        // 照合順序と DEFAULT 制約は実行時にカタログから読む（ALTER TABLE と同じ 1 つの表に OBJECT_ID で絞る）
        sb.AppendLine(
            "DECLARE @df sysname, @dfdef nvarchar(max), @coll sysname, @sql nvarchar(max);"
        );

        if (typeChanges)
        {
            // 型を変える前に DEFAULT 制約を外す（残すとエラー 5074 で ALTER COLUMN が断られる）
            AppendDropDefaultConstraint(sb, table, columnName);
        }

        if (keepsCollation)
        {
            sb.AppendLine("SELECT @coll = c.collation_name FROM sys.columns c");
            sb.AppendLine(
                $"WHERE c.object_id = OBJECT_ID(N'{table}') AND c.name = N'{columnName}';"
            );
        }

        // 照合順序を書かない ALTER COLUMN は DB の既定へ戻すので、読んだ照合順序を書き添える（名前はカタログの値＝識別子）
        sb.AppendLine(
            $"SET @sql = N'ALTER TABLE {table} ALTER COLUMN {column} {type}'"
                + " + ISNULL(N' COLLATE ' + @coll, N'')"
                + $" + N' {nullability}';"
        );
        sb.AppendLine("EXEC sys.sp_executesql @sql;");

        if (typeChanges)
        {
            // 同じ名前・同じ定義で付け直す。定義はカタログが SQL の式の形で持つ（('it''s') など）ので、文字列リテラルへ
            // 埋めるのでなく変数のまま連結する（二重化すると式が変わる）
            sb.AppendLine(
                $"SET @sql = N'ALTER TABLE {table} ADD CONSTRAINT ' + QUOTENAME(@df) + N' DEFAULT ' + @dfdef + N' FOR {column}';"
            );
            sb.AppendLine("IF @df IS NOT NULL EXEC sys.sp_executesql @sql;");
        }

        sb.AppendLine("GO");
    }

    /// <summary>型の表記が照合順序を持つ文字列型（<c>char</c> / <c>varchar</c> / <c>nchar</c> / <c>nvarchar</c> / <c>text</c> / <c>ntext</c>）か</summary>
    private static bool IsCollatableType(string type) =>
        type.Split('(')[0].Trim().ToLowerInvariant()
            is "char"
                or "varchar"
                or "nchar"
                or "nvarchar"
                or "text"
                or "ntext";

    /// <summary>主キー変更の解除フェーズ（旧主キー制約の DROP）文を生成する</summary>
    /// <remarks>
    /// <para>
    /// 旧主キーの制約名は差分項目に含まれないため、sys.key_constraints を逆引きし、
    /// 動的 SQL で DROP する（主キーが無いテーブルなら何も実行しない）。
    /// </para>
    /// <para>
    /// 逆引きは <c>OBJECT_ID</c> で ALTER TABLE と同じ 1 つのテーブルに絞る（テーブル名だけで引くと、
    /// 別スキーマの同名テーブルの主キー名を拾う）。実行時に読んだ制約名は図の外から来る値なので、
    /// 動的 SQL へは必ず <c>QUOTENAME</c> を通して埋める（<c>]</c> を含む名前で構文が崩れないように）。
    /// </para>
    /// </remarks>
    protected override void AppendDropPrimaryKey(StringBuilder sb, DropPrimaryKeyStep step)
    {
        // 括弧付けした名前を文字列リテラルへ埋めるため、リテラルエスケープ込みのヘルパーを通す
        var table = SqlIdentifier.QuoteForDynamicSql(step.TableName);

        // 旧主キーの制約名はカタログビューを逆引きして特定する（主キーが無ければ @pk は NULL のまま）
        sb.AppendLine("DECLARE @pk sysname;");
        sb.AppendLine("SELECT @pk = kc.name FROM sys.key_constraints kc");
        sb.AppendLine($"WHERE kc.type = 'PK' AND kc.parent_object_id = OBJECT_ID(N'{table}');");
        AppendDropConstraintByVariable(sb, table, "@pk");
        sb.AppendLine("GO");
    }

    /// <summary>実行時に変数へ読んだ制約名で、テーブルの制約を動的 SQL で DROP する文を書き出す</summary>
    /// <remarks>
    /// <c>EXEC( … )</c> の中には関数呼び出しを書けないため、<c>QUOTENAME</c> を通した文を
    /// <c>nvarchar(max)</c> の変数に組み立ててから <c>sp_executesql</c> で実行する
    /// （<c>QUOTENAME</c> の結果は <c>]</c> の二重化で最大 258 文字になり、<c>sysname</c> へ戻すと切り詰められる）。
    /// 変数が NULL（＝制約が見つからない）なら何も実行しない。
    /// </remarks>
    /// <param name="table">文字列リテラル用にエスケープ済みの括弧付けテーブル名</param>
    /// <param name="nameVariable">制約名を保持する変数名（<c>@pk</c> / <c>@fk</c>）</param>
    private static void AppendDropConstraintByVariable(
        StringBuilder sb,
        string table,
        string nameVariable
    )
    {
        sb.AppendLine(
            $"DECLARE @sql nvarchar(max) = N'ALTER TABLE {table} DROP CONSTRAINT ' + QUOTENAME({nameVariable});"
        );
        sb.AppendLine($"IF {nameVariable} IS NOT NULL EXEC sys.sp_executesql @sql;");
    }

    /// <summary>主キー変更の付与フェーズ（新主キー制約の ADD）文を生成する</summary>
    /// <remarks>
    /// 新しい主キー構成は文の主キーの列名（<see cref="AddPrimaryKeyStep.Columns"/>＝主キーの中の並びの順）で読み、
    /// 制約名は CREATE TABLE と同じ <c>PK_{テーブル名}</c> 規則で組み立てる。
    /// 主キー列が 1 つも無い場合（主キーの解除のみ）は付与文を出さない。
    /// </remarks>
    protected override void AppendAddPrimaryKey(StringBuilder sb, AddPrimaryKeyStep step)
    {
        var pks = step.Columns;

        // 新しい主キー列が無い（＝主キーの解除のみ）場合は付与文を出さない
        if (pks.Count == 0)
        {
            return;
        }

        var pkCols = string.Join(", ", pks.Select(SqlIdentifier.BracketSimple));
        sb.AppendLine(
            $"ALTER TABLE {SqlIdentifier.Bracket(step.TableName)} ADD CONSTRAINT [{SqlIdentifier.Escape(ConstraintNames.PrimaryKey(step.TableName, IdentifierLimit))}] "
                + $"PRIMARY KEY ({pkCols});"
        );
        sb.AppendLine("GO");
    }

    /// <summary>一意制約を追加する ALTER TABLE ... ADD CONSTRAINT ... UNIQUE 文を生成する</summary>
    protected override void AppendAddUniqueConstraint(
        StringBuilder sb,
        AddUniqueConstraintStep step
    )
    {
        if (step.Columns.Count == 0)
        {
            sb.AppendLine(SyncScriptBuilderHelper.BuildUniqueConstraintSkipComment(step));
            return;
        }

        var name = UniqueConstraintNaming.Resolve(
            step.Name,
            step.TableName,
            step.Columns,
            IdentifierLimit
        );
        var cols = string.Join(", ", step.Columns.Select(SqlIdentifier.BracketSimple));
        sb.AppendLine(
            $"ALTER TABLE {SqlIdentifier.Bracket(step.TableName)} ADD CONSTRAINT [{SqlIdentifier.Escape(name)}] "
                + $"UNIQUE ({cols});"
        );
        sb.AppendLine("GO");
    }

    /// <summary>一意制約を削除する ALTER TABLE ... DROP CONSTRAINT 文を生成する</summary>
    protected override void AppendDropUniqueConstraint(
        StringBuilder sb,
        DropUniqueConstraintStep step
    )
    {
        var name = UniqueConstraintNaming.Resolve(
            step.Name,
            step.TableName,
            step.Columns,
            IdentifierLimit
        );
        sb.AppendLine(
            $"ALTER TABLE {SqlIdentifier.Bracket(step.TableName)} "
                + $"DROP CONSTRAINT [{SqlIdentifier.Escape(name)}];"
        );
        sb.AppendLine("GO");
    }

    /// <summary>ALTER TABLE ... DROP COLUMN（列削除）文を生成する</summary>
    /// <remarks>
    /// 列の DEFAULT 制約を先に外す（残したままだとエラー 5074 で断られる）。列ごと消えるので付け直さない。名前は実行時に
    /// カタログから読み、列の変更と同じく <c>QUOTENAME</c> と <c>OBJECT_ID</c> で扱う。同じ列を参照する CHECK 制約や索引は
    /// 外さない（図が持たないもので、残っていれば列の削除は断られ、同期はトランザクションごと戻る）。
    /// </remarks>
    protected override void AppendDropColumn(StringBuilder sb, DropColumnStep step)
    {
        sb.AppendLine("DECLARE @df sysname, @dfdef nvarchar(max), @sql nvarchar(max);");
        AppendDropDefaultConstraint(
            sb,
            SqlIdentifier.QuoteForDynamicSql(step.TableName),
            SqlIdentifier.EscapeStringLiteral(step.ColumnName)
        );
        sb.AppendLine(
            $"ALTER TABLE {SqlIdentifier.Bracket(step.TableName)} "
                + $"DROP COLUMN {SqlIdentifier.BracketSimple(step.ColumnName)};"
        );
        sb.AppendLine("GO");
    }

    /// <summary>
    /// 列の DEFAULT 制約の名前と定義を <c>@df</c> / <c>@dfdef</c> へ読み、あれば外す文を出す（変数は呼び出し側が宣言する）。
    /// </summary>
    /// <remarks>
    /// 表は <c>OBJECT_ID</c> で <c>ALTER TABLE</c> と同じ 1 つの表に絞り、読んだ名前は <c>QUOTENAME</c> を通して
    /// <c>nvarchar(max)</c> の変数に組んだ文を <c>sp_executesql</c> で実行する。
    /// </remarks>
    /// <param name="sb">出力先</param>
    /// <param name="table">動的 SQL の文字列リテラルへ埋める表の名前（<see cref="SqlIdentifier.QuoteForDynamicSql"/> 済み）</param>
    /// <param name="columnName">文字列リテラルへ埋める列名（<see cref="SqlIdentifier.EscapeStringLiteral"/> 済み）</param>
    private static void AppendDropDefaultConstraint(
        StringBuilder sb,
        string table,
        string columnName
    )
    {
        sb.AppendLine("SELECT @df = dc.name, @dfdef = dc.definition");
        sb.AppendLine("FROM sys.default_constraints dc");
        sb.AppendLine(
            "JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id"
        );
        sb.AppendLine(
            $"WHERE dc.parent_object_id = OBJECT_ID(N'{table}') AND c.name = N'{columnName}';"
        );
        sb.AppendLine($"SET @sql = N'ALTER TABLE {table} DROP CONSTRAINT ' + QUOTENAME(@df);");
        sb.AppendLine("IF @df IS NOT NULL EXEC sys.sp_executesql @sql;");
    }

    /// <summary>DROP TABLE（テーブル削除）文を生成する</summary>
    protected override void AppendDropTable(StringBuilder sb, DropTableStep step)
    {
        sb.AppendLine($"DROP TABLE {SqlIdentifier.Bracket(step.TableName)};");
        sb.AppendLine("GO");
    }

    /// <summary>外部キー制約を追加する ALTER TABLE 文を生成する</summary>
    protected override void AppendAddForeignKey(StringBuilder sb, AddForeignKeyStep step)
    {
        var columnPairs = step.ColumnPairs;

        // 構成列が特定できない場合は不正な DDL を出さず、コメントでスキップを明示する
        if (columnPairs.Count == 0)
        {
            sb.AppendLine(SyncScriptBuilderHelper.BuildForeignKeySkipComment(step));
            return;
        }

        var childTbl = SchemaDiffService.NormalizeTable(step.ChildTable);
        var parentTbl = SchemaDiffService.NormalizeTable(step.ParentTable);
        var fkName = SyncScriptBuilderHelper.ForeignKeyNameToAdd(step, IdentifierLimit);
        var referentialActions = SyncScriptBuilderHelper.BuildReferentialActionClause(step);
        // 複合外部キーは構成列を宣言順にカンマ区切りで並べる（単列なら従来と同一の出力）
        var childColumnList = string.Join(
            ", ",
            ForeignKeyColumnPairResolver
                .ChildColumns(columnPairs)
                .Select(SqlIdentifier.BracketSimple)
        );
        var parentColumnList = string.Join(
            ", ",
            ForeignKeyColumnPairResolver
                .ParentColumns(columnPairs)
                .Select(SqlIdentifier.BracketSimple)
        );

        sb.AppendLine(
            $"ALTER TABLE {SqlIdentifier.Bracket(childTbl)} ADD CONSTRAINT [{SqlIdentifier.Escape(fkName)}] "
                + $"FOREIGN KEY ({childColumnList}) "
                + $"REFERENCES {SqlIdentifier.Bracket(parentTbl)} ({parentColumnList}){referentialActions};"
        );
        sb.AppendLine("GO");
    }

    /// <summary>外部キー制約を削除する文を生成する</summary>
    /// <remarks>
    /// 制約名が判明していれば存在チェック付きで直接 DROP する 不明な場合は親子テーブル名から
    /// sys.foreign_keys を逆引きし、動的 SQL で削除する
    /// </remarks>
    protected override void AppendDropForeignKey(StringBuilder sb, DropForeignKeyStep step)
    {
        var childTbl = SchemaDiffService.NormalizeTable(step.ChildTable);
        var parentTbl = SchemaDiffService.NormalizeTable(step.ParentTable);

        // 制約名が判明している場合は存在チェックのうえ直接 DROP する
        if (!string.IsNullOrWhiteSpace(step.Name))
        {
            sb.AppendLine(
                $"IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'{SqlIdentifier.EscapeStringLiteral(step.Name)}')"
            );
            sb.AppendLine(
                $"    ALTER TABLE {SqlIdentifier.Bracket(childTbl)} DROP CONSTRAINT [{SqlIdentifier.Escape(step.Name)}];"
            );
            sb.AppendLine("GO");
            return;
        }

        // 制約名不明時は親子テーブルからカタログビューを逆引きして特定する。
        // 主キーの解除と同じく OBJECT_ID でスキーマまで絞り、読んだ名前は QUOTENAME を通して埋める
        // 括弧付けした名前を文字列リテラルへ埋めるため、リテラルエスケープ込みのヘルパーを通す
        var child = SqlIdentifier.QuoteForDynamicSql(childTbl);
        var parent = SqlIdentifier.QuoteForDynamicSql(parentTbl);
        sb.AppendLine("DECLARE @fk sysname;");
        sb.AppendLine("SELECT @fk = fk.name FROM sys.foreign_keys fk");
        sb.AppendLine($"WHERE fk.parent_object_id = OBJECT_ID(N'{child}')");
        sb.AppendLine($"  AND fk.referenced_object_id = OBJECT_ID(N'{parent}');");
        AppendDropConstraintByVariable(sb, child, "@fk");
        sb.AppendLine("GO");
    }

    // ---------------- MS_Description (拡張プロパティ) ----------------

    /// <summary>テーブルの説明（MS_Description）設定文を生成する</summary>
    protected override void AppendSetTableDescription(
        StringBuilder sb,
        SetTableDescriptionStep step
    ) => AppendDescriptionStatement(sb, step.TableName, null, step.NewDescription);

    /// <summary>カラムの説明（MS_Description）設定文を生成する</summary>
    protected override void AppendSetColumnDescription(
        StringBuilder sb,
        SetColumnDescriptionStep step
    ) => AppendDescriptionStatement(sb, step.TableName, step.ColumnName, step.NewDescription);

    /// <summary>拡張プロパティ MS_Description の設定・更新・削除文を生成する</summary>
    /// <remarks>
    /// 実行時点の存在状態を判定し、add / update / drop を切り替える 新値が空なら削除する
    /// </remarks>
    /// <param name="tableName">表名</param>
    /// <param name="columnName">列名（カラムレベルの拡張プロパティのとき。テーブルレベルは null）</param>
    /// <param name="newDescription">新しい説明（空なら削除）</param>
    private static void AppendDescriptionStatement(
        StringBuilder sb,
        string tableName,
        string? columnName,
        string? newDescription
    )
    {
        var newVal = DescriptionText.Normalize(newDescription);
        var levelArgs = SqlServerExtendedProperty.LevelArguments(tableName, columnName);
        var objectIdLiteral = SqlServerExtendedProperty.ObjectIdLiteral(tableName);

        // 修飾の無い表は既定スキーマを変数で渡す。文ごとに GO で区切るので、文ごとに宣言する
        if (SqlServerExtendedProperty.UsesDefaultSchema(tableName))
        {
            sb.AppendLine(SqlServerExtendedProperty.DeclareDefaultSchema);
        }
        var minorIdCondition = columnName is not null
            ? $"      AND ep.minor_id = COLUMNPROPERTY({objectIdLiteral}, N'{SqlIdentifier.EscapeStringLiteral(columnName)}', 'ColumnId')"
            : $"      AND ep.minor_id = 0";

        if (!DescriptionText.IsPresent(newVal))
        {
            // 新値が空の場合は既存の拡張プロパティを存在チェックのうえ削除する
            sb.AppendLine($"IF EXISTS (");
            sb.AppendLine($"    SELECT 1 FROM sys.extended_properties ep");
            sb.AppendLine($"    WHERE ep.name = N'MS_Description' AND ep.class = 1");
            sb.AppendLine($"      AND ep.major_id = {objectIdLiteral}");
            sb.AppendLine($"{minorIdCondition})");
            sb.AppendLine(
                $"    EXEC sys.sp_dropextendedproperty @name=N'MS_Description', {levelArgs};"
            );
        }
        else
        {
            // 既存があれば update、無ければ add で冪等に説明を設定する
            var escaped = SqlIdentifier.EscapeStringLiteral(newVal);
            sb.AppendLine($"IF EXISTS (");
            sb.AppendLine($"    SELECT 1 FROM sys.extended_properties ep");
            sb.AppendLine($"    WHERE ep.name = N'MS_Description' AND ep.class = 1");
            sb.AppendLine($"      AND ep.major_id = {objectIdLiteral}");
            sb.AppendLine($"{minorIdCondition})");
            sb.AppendLine(
                $"    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'{escaped}', {levelArgs};"
            );
            sb.AppendLine($"ELSE");
            sb.AppendLine(
                $"    EXEC sys.sp_addextendedproperty    @name=N'MS_Description', @value=N'{escaped}', {levelArgs};"
            );
        }

        sb.AppendLine("GO");
    }
}
