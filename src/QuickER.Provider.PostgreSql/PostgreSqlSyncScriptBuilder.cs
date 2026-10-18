using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Provider.PostgreSql;

/// <summary>選択済みの <see cref="SchemaDiffItem"/> から PostgreSQL 用の DDL バッチを生成する</summary>
/// <remarks>
/// 依存関係による失敗を避けるため、以下の順序で出力する。文はすべて <c>;</c> で終端し、<c>GO</c> は使わない。
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
///   <item>SetTableDescription / SetColumnDescription（COMMENT ON）</item>
/// </list>
/// </remarks>
public sealed class PostgreSqlSyncScriptBuilder : SyncScriptBuilderBase
{
    /// <inheritdoc />
    protected override IdentifierLengthLimit IdentifierLimit => PgIdentifier.Limit;

    /// <inheritdoc/>
    protected override string DialectName => PostgreSqlProvider.ProviderName;

    // ---------------- 各種 DDL ----------------

    /// <summary>CREATE TABLE 文（主キー制約・一意制約を含む）を生成する</summary>
    /// <remarks>
    /// 制約行は DDL 生成と同じヘルパーで組み立てる（同期で作ったテーブルにだけ UNIQUE が無い、という
    /// 食い違いを構造的に防ぐ）。
    /// </remarks>
    protected override void AppendCreateTable(StringBuilder sb, SchemaDiffItem item)
    {
        var e = item.Entity!;
        sb.AppendLine($"CREATE TABLE {PgIdentifier.Quote(item.TableName)} (");

        // 列定義の末尾カンマ判定に「後続制約行の有無」が必要なため、制約行を先に組み立てる
        var constraintLines = TableConstraintLineBuilder.Build(
            e,
            item.TableName,
            PgIdentifier.QuoteSimple,
            name => $"\"{PgIdentifier.Escape(name)}\"",
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
                $"    {PgIdentifier.QuoteSimple(col.Name)} {col.DataType} {SyncScriptBuilderHelper.GetNullabilityClause(col)}";

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

        // 計算列は式が図に無い＝ここで足すのは普通の列。手で式を足す必要があることを添える
        ComputedColumnComment.Append(sb, col, string.Empty);

        // 採番の定義は図に無い＝ここで足すのは普通の列。扱いの表が注記とする方言だけ書き添える
        IdentityColumnComment.Append(sb, col, string.Empty, DialectName, UnsupportedFeature.Sync);
        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(item.TableName)} "
                + $"ADD COLUMN {PgIdentifier.QuoteSimple(col.Name)} {col.DataType} {SyncScriptBuilderHelper.GetNullabilityClause(col)};"
        );
    }

    /// <summary>ALTER TABLE ... ALTER COLUMN（列定義変更）文を生成する</summary>
    /// <remarks>
    /// PostgreSQL は型変更と NULL 制約変更を別の文で表現する。
    /// 型は <c>ALTER COLUMN ... TYPE 新型</c>、NULL 制約は <c>SET NOT NULL</c> / <c>DROP NOT NULL</c> を用いる。
    /// 型の系統が変わる変更（<c>varchar</c> → <c>integer</c> 等）には <c>USING 列::新型</c> を付ける（<see cref="NeedsUsingCast"/>）。
    /// 新しい型が文字列型なら、実行時に列の照合順序（<c>pg_attribute.attcollation</c>）を読み、型の既定と違うときだけ
    /// <c>COLLATE</c> を書き添える（書かなければ新しい型の既定へ戻る）。表は <c>to_regclass</c> で <c>ALTER TABLE</c> と同じ
    /// 1 つの表に絞り、照合順序の名前は <c>quote_ident</c> で組む。
    /// </remarks>
    protected override void AppendAlterColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        var table = PgIdentifier.Quote(item.TableName);
        var column = PgIdentifier.QuoteSimple(col.Name);
        var usingClause = NeedsUsingCast(item.OldColumn?.DataType, col.DataType)
            ? $" USING {column}::{col.DataType}"
            : string.Empty;

        if (IsCollatableType(col.DataType))
        {
            // ALTER COLUMN … TYPE は COLLATE を書かなければ照合順序を新しい型の既定へ戻す。実行時に列の照合順序を読み、
            // 型の既定と違うときだけ書き添える（表は ALTER TABLE と同じ 1 つの表に to_regclass で絞る）。
            // COLLATE は USING より前に書く構文
            var dynamicTable = PgIdentifier.QuoteForDynamicSql(item.TableName);
            var body = new StringBuilder();
            body.AppendLine("DECLARE coll text;");
            body.AppendLine("BEGIN");
            body.AppendLine(
                "    SELECT CASE WHEN a.attcollation = 0 OR a.attcollation = t.typcollation THEN NULL"
            );
            body.AppendLine(
                "        ELSE quote_ident(n.nspname) || '.' || quote_ident(co.collname) END INTO coll"
            );
            body.AppendLine("    FROM pg_attribute a");
            body.AppendLine("    JOIN pg_type t ON t.oid = a.atttypid");
            body.AppendLine("    LEFT JOIN pg_collation co ON co.oid = a.attcollation");
            body.AppendLine("    LEFT JOIN pg_namespace n ON n.oid = co.collnamespace");
            body.AppendLine(
                $"    WHERE a.attrelid = to_regclass('{dynamicTable}') "
                    + $"AND a.attname = '{PgIdentifier.EscapeStringLiteral(col.Name)}' AND NOT a.attisdropped;"
            );
            body.AppendLine(
                $"    EXECUTE '{PgIdentifier.EscapeStringLiteral($"ALTER TABLE {table} ALTER COLUMN {column} TYPE {col.DataType}")}'"
                    + $" || COALESCE(' COLLATE ' || coll, '') || '{PgIdentifier.EscapeStringLiteral(usingClause)}';"
            );
            AppendDoBlock(sb, body.ToString());
        }
        else
        {
            sb.AppendLine(
                $"ALTER TABLE {table} ALTER COLUMN {column} TYPE {col.DataType}{usingClause};"
            );
        }

        // 主キー列または NULL 非許容なら NOT NULL を設定し、それ以外は NOT NULL を外す
        var nullClause = col.IsPrimaryKey || !col.IsNullable ? "SET NOT NULL" : "DROP NOT NULL";
        sb.AppendLine($"ALTER TABLE {table} ALTER COLUMN {column} {nullClause};");
    }

    /// <summary>型の表記が照合順序を持つ文字列型（配列を含む）か</summary>
    private static bool IsCollatableType(string type) =>
        type.Split('(')[0].Replace("[]", string.Empty).Trim().ToLowerInvariant()
            is "text"
                or "varchar"
                or "character varying"
                or "char"
                or "character"
                or "bpchar"
                or "citext"
                or "name";

    /// <summary>主キー変更の解除フェーズ（旧主キー制約の DROP）文を生成する</summary>
    /// <remarks>
    /// 旧主キーの制約名は差分項目に含まれないため、テーブル名から <c>pg_constraint</c> を逆引きし、
    /// DO ブロックで動的に DROP する（主キーが無いテーブルなら何も実行しない）。
    /// 実行時に読んだ制約名は図の外から来る値なので、動的 SQL へは必ず <c>quote_ident</c> を通して埋める
    /// （<c>"</c> を含む名前で構文が崩れないように。外部キー側の逆引きも同じ）。
    /// 表は <c>to_regclass('クォートした名前')</c> で、<c>ALTER TABLE</c> と同じ 1 つの表に絞る（ドット付きの名前は
    /// そのスキーマの表、修飾の無い名前は検索パスの表）。表が無ければ NULL になり、何もしない（外部キー側も同じ）。
    /// </remarks>
    protected override void AppendDropPrimaryKey(StringBuilder sb, SchemaDiffItem item)
    {
        // クォートした名前を EXECUTE の文字列リテラルへ埋めるため、リテラルエスケープ込みのヘルパーを通す
        var table = PgIdentifier.QuoteForDynamicSql(item.TableName);

        // 旧主キーの制約名はシステムカタログを逆引きして特定し、見つかったときだけ DROP する
        var body = new StringBuilder();
        body.AppendLine("DECLARE pk_name text;");
        body.AppendLine("BEGIN");
        body.AppendLine("    SELECT con.conname INTO pk_name");
        body.AppendLine("    FROM pg_constraint con");
        // ALTER TABLE と同じ 1 つの表に絞る（別スキーマの同名の表を取り違えない・表が無ければ NULL で何もしない）
        body.AppendLine($"    WHERE con.contype = 'p' AND con.conrelid = to_regclass('{table}');");
        body.AppendLine("    IF pk_name IS NOT NULL THEN");
        body.AppendLine(
            $"        EXECUTE 'ALTER TABLE {table} DROP CONSTRAINT ' || quote_ident(pk_name);"
        );
        body.AppendLine("    END IF;");

        AppendDoBlock(sb, body.ToString());
    }

    /// <summary>組み立て済みの本文を <c>DO … END …;</c> のドルクォートブロックとして書き出す</summary>
    /// <remarks>
    /// タグは本文と衝突しないものを <see cref="PgIdentifier.DollarQuoteTag"/> が決定的に選ぶ。
    /// 本文へ埋めた名前に <c>$$</c> が含まれるとブロックがそこで終端し、以降が最上位の SQL 文として
    /// 解釈されるため、タグの選択は本文が完成してからでなければならない。
    /// </remarks>
    private static void AppendDoBlock(StringBuilder sb, string body)
    {
        var tag = PgIdentifier.DollarQuoteTag(body);

        sb.AppendLine($"DO {tag}");
        sb.Append(body);
        sb.AppendLine($"END {tag};");
    }

    /// <summary>主キー変更の付与フェーズ（新主キー制約の ADD）文を生成する</summary>
    /// <remarks>
    /// 新しい主キー構成は <see cref="SchemaDiffItem.Entity"/>（target 側エンティティ）の主キー列を実効順
    /// （<see cref="Entity.GetPrimaryKeyColumnsInOrder"/>）で読み、
    /// 制約名は CREATE TABLE と同じ <c>PK_{テーブル名}</c> 規則で組み立てる。
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

        var pkCols = string.Join(", ", pks.Select(p => PgIdentifier.QuoteSimple(p.Name)));
        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(item.TableName)} ADD CONSTRAINT \"{PgIdentifier.Escape(ConstraintNames.PrimaryKey(item.TableName, IdentifierLimit))}\" "
                + $"PRIMARY KEY ({pkCols});"
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
            IdentifierLimit
        );
        var cols = string.Join(", ", item.UniqueConstraintColumns.Select(PgIdentifier.QuoteSimple));
        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(item.TableName)} ADD CONSTRAINT \"{PgIdentifier.Escape(name)}\" "
                + $"UNIQUE ({cols});"
        );
    }

    /// <summary>一意制約を削除する ALTER TABLE ... DROP CONSTRAINT 文を生成する</summary>
    protected override void AppendDropUniqueConstraint(StringBuilder sb, SchemaDiffItem item)
    {
        var name = UniqueConstraintNaming.Resolve(
            item.UniqueConstraintName,
            item.TableName,
            item.UniqueConstraintColumns,
            IdentifierLimit
        );
        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(item.TableName)} "
                + $"DROP CONSTRAINT \"{PgIdentifier.Escape(name)}\";"
        );
    }

    /// <summary>ALTER TABLE ... DROP COLUMN（列削除）文を生成する</summary>
    protected override void AppendDropColumn(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(item.TableName)} "
                + $"DROP COLUMN {PgIdentifier.QuoteSimple(item.ColumnName!)};"
        );
    }

    /// <summary>DROP TABLE（テーブル削除）文を生成する</summary>
    protected override void AppendDropTable(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine($"DROP TABLE {PgIdentifier.Quote(item.TableName)};");
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
        var fkName = SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, IdentifierLimit);
        var referentialActions = SyncScriptBuilderHelper.BuildReferentialActionClause(
            item.Relationship
        );
        // 複合外部キーは構成列を宣言順にカンマ区切りで並べる（単列なら従来と同一の出力）
        var childColumnList = string.Join(
            ", ",
            ForeignKeyColumnPairResolver.ChildColumns(columnPairs).Select(PgIdentifier.QuoteSimple)
        );
        var parentColumnList = string.Join(
            ", ",
            ForeignKeyColumnPairResolver.ParentColumns(columnPairs).Select(PgIdentifier.QuoteSimple)
        );

        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(childTbl)} ADD CONSTRAINT \"{PgIdentifier.Escape(fkName)}\" "
                + $"FOREIGN KEY ({childColumnList}) "
                + $"REFERENCES {PgIdentifier.Quote(parentTbl)} ({parentColumnList}){referentialActions};"
        );
    }

    /// <summary>外部キー制約を削除する ALTER TABLE ... DROP CONSTRAINT 文を生成する</summary>
    /// <remarks>
    /// 制約名が判明していれば <c>IF EXISTS</c> 付きで直接 DROP する 不明な場合は親子テーブル名から
    /// システムカタログを逆引きし、DO ブロックで動的に削除する
    /// </remarks>
    protected override void AppendDropForeignKey(StringBuilder sb, SchemaDiffItem item)
    {
        if (item.ChildEntity is null || item.ParentEntity is null)
        {
            return;
        }

        var childTbl = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTbl = SchemaDiffService.NormalizeTable(item.ParentEntity);

        // 制約名が判明している場合は IF EXISTS のうえ直接 DROP する
        if (!string.IsNullOrWhiteSpace(item.ForeignKeyName))
        {
            sb.AppendLine(
                $"ALTER TABLE {PgIdentifier.Quote(childTbl)} "
                    + $"DROP CONSTRAINT IF EXISTS \"{PgIdentifier.Escape(item.ForeignKeyName)}\";"
            );
            return;
        }

        // 制約名不明時は親子テーブル名からシステムカタログを逆引きして DO ブロックで削除する
        var body = new StringBuilder();
        body.AppendLine("DECLARE fk_name text;");
        body.AppendLine("BEGIN");
        body.AppendLine("    SELECT con.conname INTO fk_name");
        body.AppendLine("    FROM pg_constraint con");
        // 親子とも ALTER TABLE と同じ 1 つの表に絞る（別スキーマの同名の表を取り違えない・表が無ければ NULL で何もしない）
        body.AppendLine(
            $"    WHERE con.contype = 'f' AND con.conrelid = to_regclass('{PgIdentifier.QuoteForDynamicSql(childTbl)}')"
        );
        body.AppendLine(
            $"        AND con.confrelid = to_regclass('{PgIdentifier.QuoteForDynamicSql(parentTbl)}');"
        );
        body.AppendLine("    IF fk_name IS NOT NULL THEN");
        body.AppendLine(
            // クォートした名前を EXECUTE の文字列リテラルへ埋めるため、リテラルエスケープ込みのヘルパーを通す
            $"        EXECUTE 'ALTER TABLE {PgIdentifier.QuoteForDynamicSql(childTbl)} DROP CONSTRAINT ' || quote_ident(fk_name);"
        );
        body.AppendLine("    END IF;");

        AppendDoBlock(sb, body.ToString());
    }

    // ---------------- COMMENT ON (説明) ----------------

    /// <summary>テーブルの説明（COMMENT ON TABLE）設定文を生成する</summary>
    protected override void AppendSetTableDescription(StringBuilder sb, SchemaDiffItem item)
    {
        var newVal = DescriptionText.Normalize(item.NewDescription);
        var target = PgIdentifier.Quote(item.TableName);
        // 新値が空なら IS NULL で説明を削除、それ以外は文字列リテラルで設定する
        var isClause = !DescriptionText.IsPresent(newVal)
            ? "NULL"
            : $"'{PgIdentifier.EscapeStringLiteral(newVal)}'";
        sb.AppendLine($"COMMENT ON TABLE {target} IS {isClause};");
    }

    /// <summary>カラムの説明（COMMENT ON COLUMN）設定文を生成する</summary>
    protected override void AppendSetColumnDescription(StringBuilder sb, SchemaDiffItem item)
    {
        var newVal = DescriptionText.Normalize(item.NewDescription);
        var target =
            $"{PgIdentifier.Quote(item.TableName)}.{PgIdentifier.QuoteSimple(item.ColumnName!)}";
        var isClause = !DescriptionText.IsPresent(newVal)
            ? "NULL"
            : $"'{PgIdentifier.EscapeStringLiteral(newVal)}'";
        sb.AppendLine($"COMMENT ON COLUMN {target} IS {isClause};");
    }

    /// <summary>型の系統を判定するための型カタログ（解析だけに使う）</summary>
    private static readonly PostgreSqlTypeCatalog TypeCatalog = new();

    /// <summary>列の型の変更に <c>USING 列::新型</c> を付けるべきか</summary>
    /// <remarks>
    /// <para>
    /// <c>ALTER COLUMN ... TYPE</c> は <c>USING</c> が無いと代入キャストで値を変換するので、代入キャストの無い
    /// 系統をまたぐ変更（<c>varchar</c> → <c>integer</c>・<c>text</c> → <c>date</c>・<c>integer</c> → <c>boolean</c> 等）は
    /// 42804 で失敗する。明示キャストを付けると、変換できる値の列ならそのまま通る。
    /// </para>
    /// <para>
    /// 付けるのは「両方の型をカタログで解析でき、系統が違い、新しい型が文字列系でない」ときだけ。
    /// 常に付けてはいけない＝明示キャストは <c>varchar(n)</c> への変換で長すぎる値を<b>黙って切り詰める</b>
    /// （代入キャストはエラーにする）。文字列系への変更はどの型からも代入キャストで通るので付けない。
    /// 解析できない型（PostGIS の型・ユーザー定義型等）は系統が分からないので付けない（従来どおり）。
    /// </para>
    /// </remarks>
    internal static bool NeedsUsingCast(string? oldType, string newType)
    {
        if (
            string.IsNullOrWhiteSpace(oldType)
            || !TypeCatalog.TryParse(oldType, out var oldCanonical)
            || !TypeCatalog.TryParse(newType, out var newCanonical)
        )
        {
            return false;
        }

        var oldFamily = FamilyOf(oldCanonical.Kind);
        var newFamily = FamilyOf(newCanonical.Kind);

        return oldFamily is not null
            && newFamily is not null
            && oldFamily != newFamily
            && newFamily != TypeFamily.Text;
    }

    /// <summary>正規型の種別を型の系統へ分類する（分類しない種別は null）</summary>
    private static TypeFamily? FamilyOf(CanonicalTypeKind kind) =>
        kind switch
        {
            CanonicalTypeKind.String
            or CanonicalTypeKind.AnsiString
            or CanonicalTypeKind.FixedString
            or CanonicalTypeKind.AnsiFixedString => TypeFamily.Text,
            CanonicalTypeKind.TinyInt
            or CanonicalTypeKind.SmallInt
            or CanonicalTypeKind.Int32
            or CanonicalTypeKind.Int64
            or CanonicalTypeKind.Decimal
            or CanonicalTypeKind.Float32
            or CanonicalTypeKind.Float64
            or CanonicalTypeKind.Money => TypeFamily.Number,
            CanonicalTypeKind.Boolean => TypeFamily.Boolean,
            CanonicalTypeKind.Date
            or CanonicalTypeKind.Time
            or CanonicalTypeKind.DateTime
            or CanonicalTypeKind.DateTimeOffset => TypeFamily.Temporal,
            CanonicalTypeKind.Binary or CanonicalTypeKind.FixedBinary => TypeFamily.Binary,
            CanonicalTypeKind.Guid => TypeFamily.Guid,
            CanonicalTypeKind.Json => TypeFamily.Json,
            CanonicalTypeKind.Xml => TypeFamily.Xml,
            _ => null,
        };

    /// <summary><see cref="NeedsUsingCast"/> が比べる型の系統</summary>
    private enum TypeFamily
    {
        Text,
        Number,
        Boolean,
        Temporal,
        Binary,
        Guid,
        Json,
        Xml,
    }
}
