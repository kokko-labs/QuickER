using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.MySql;
using QuickER.Provider;

namespace QuickER.Tests.MySql;

/// <summary><see cref="MySqlSyncScriptBuilder"/> が差分から生成する MySQL DDL の内容と出力順序を検証するテストクラス</summary>
public class MySqlSyncScriptBuilderTests
{
    private static string Build(params SchemaDiffItem[] items) =>
        new MySqlSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(items, new SyncDialectCapabilities())
        );

    /// <summary>AddTable が主キー制約を含む CREATE TABLE 文を生成することを検証する</summary>
    [Fact(DisplayName = "AddTable は CREATE TABLE と PK を含む")]
    public void AddTable_GeneratesCreate()
    {
        var e = new Entity { TableName = "customer" };
        e.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        e.Columns.Add(
            new Column
            {
                Name = "name",
                DataType = "varchar(50)",
                IsNullable = true,
            }
        );

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "customer",
                Entity = e,
                IsSelected = true,
            }
        );

        sql.Should().Contain("CREATE TABLE `customer`");
        sql.Should().Contain("`id` int NOT NULL");
        sql.Should().Contain("CONSTRAINT `PK_customer` PRIMARY KEY (`id`)");
    }

    /// <summary>AddColumn が ALTER TABLE ... ADD COLUMN 文を生成することを検証する</summary>
    [Fact(DisplayName = "AddColumn は ALTER TABLE ADD COLUMN を生成する")]
    public void AddColumn_GeneratesAlterAddColumn()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "customer",
                ColumnName = "email",
                Column = new Column
                {
                    Name = "email",
                    DataType = "varchar(200)",
                    IsNullable = false,
                },
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `customer` ADD COLUMN `email` varchar(200) NOT NULL;");
    }

    /// <summary>AlterColumn が MODIFY COLUMN で型と NULL 制約を再指定することを検証する</summary>
    [Fact(DisplayName = "AlterColumn は MODIFY COLUMN で型と NOT NULL を再指定する")]
    public void AlterColumn_GeneratesModifyColumn()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = "customer",
                ColumnName = "name",
                Column = new Column
                {
                    Name = "name",
                    DataType = "varchar(100)",
                    IsNullable = false,
                },
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `customer` MODIFY COLUMN `name` varchar(100) NOT NULL;");
    }

    /// <summary>AlterColumn は説明がある場合 COMMENT を含めて既存コメントの消失を防ぐことを検証する</summary>
    [Fact(DisplayName = "AlterColumn は説明があれば COMMENT を含める")]
    public void AlterColumn_WithDescription_IncludesComment()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = "customer",
                ColumnName = "name",
                Column = new Column
                {
                    Name = "name",
                    DataType = "varchar(100)",
                    IsNullable = false,
                    Description = "顧客名",
                },
                IsSelected = true,
            }
        );

        sql.Should().Contain("MODIFY COLUMN `name` varchar(100) NOT NULL COMMENT '顧客名';");
    }

    /// <summary>NULL 許容へ変更する AlterColumn が NULL を再指定することを検証する</summary>
    [Fact(DisplayName = "AlterColumn は NULL 許容化で NULL を再指定する")]
    public void AlterColumn_NullableGeneratesNull()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = "customer",
                ColumnName = "note",
                Column = new Column
                {
                    Name = "note",
                    DataType = "text",
                    IsNullable = true,
                },
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `customer` MODIFY COLUMN `note` text NULL;");
    }

    /// <summary>DropColumn が ALTER TABLE ... DROP COLUMN 文を生成することを検証する</summary>
    [Fact(DisplayName = "DropColumn は DROP COLUMN を生成する")]
    public void DropColumn_GeneratesDropColumn()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropColumn,
                TableName = "customer",
                ColumnName = "old",
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `customer` DROP COLUMN `old`;");
    }

    /// <summary>DropTable が DROP TABLE 文を生成することを検証する</summary>
    [Fact(DisplayName = "DropTable は DROP TABLE を生成する")]
    public void DropTable_GeneratesDropTable()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropTable,
                TableName = "customer",
                IsSelected = true,
            }
        );

        sql.Should().Contain("DROP TABLE `customer`;");
    }

    /// <summary>AddForeignKey が FK 制約追加文と参照アクションを生成することを検証する</summary>
    [Fact(DisplayName = "AddForeignKey は ADD CONSTRAINT FOREIGN KEY と参照アクションを生成する")]
    public void AddForeignKey_GeneratesConstraint()
    {
        var customer = new Entity { TableName = "customer" };
        customer.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        var order = new Entity { TableName = "order" };
        order.Columns.Add(new Column { Name = "customer_id", DataType = "int" });
        var rel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(customer.Columns[0].Id, order.Columns[^1].Id)],
            ConstraintName = "FK_order_customer",
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.SetNull,
        };

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddForeignKey,
                TableName = "order",
                ColumnName = "customer_id",
                ForeignKeyColumnPairs = [new("id", "customer_id")],
                ParentEntity = customer,
                ChildEntity = order,
                Relationship = rel,
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `order` ADD CONSTRAINT `FK_order_customer`");
        sql.Should().Contain("FOREIGN KEY (`customer_id`) REFERENCES `customer` (`id`)");
        sql.Should().Contain("ON DELETE CASCADE");
        sql.Should().Contain("ON UPDATE SET NULL");
    }

    /// <summary>DropForeignKey が制約名判明時に DROP FOREIGN KEY を生成することを検証する</summary>
    [Fact(DisplayName = "DropForeignKey は制約名があれば DROP FOREIGN KEY を生成する")]
    public void DropForeignKey_UsesConstraintNameWhenAvailable()
    {
        var customer = new Entity { TableName = "customer" };
        var order = new Entity { TableName = "order" };

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "order",
                ParentEntity = customer,
                ChildEntity = order,
                ForeignKeyName = "FK_order_customer",
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `order` DROP FOREIGN KEY `FK_order_customer`;");
    }

    /// <summary>制約名不明時にプリペアド動的 SQL でカタログ逆引き削除を生成することを検証する</summary>
    [Fact(DisplayName = "DropForeignKey は制約名不明時にプリペアド動的 SQL で逆引き削除する")]
    public void DropForeignKey_UsesPreparedStatementWhenNameUnknown()
    {
        var customer = new Entity { TableName = "customer" };
        var order = new Entity { TableName = "order" };

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "order",
                ParentEntity = customer,
                ChildEntity = order,
                IsSelected = true,
            }
        );

        sql.Should().Contain("information_schema.REFERENTIAL_CONSTRAINTS");
        sql.Should().Contain("INTO @fk");
        sql.Should().Contain("rc.CONSTRAINT_SCHEMA = DATABASE()");
        sql.Should().Contain("rc.TABLE_NAME = 'order'");
        sql.Should().Contain("rc.REFERENCED_TABLE_NAME = 'customer'");
        // 同じ親子ペアに FK が 2 本ある構成でも取り出しが揺れないよう名前順で決定化する
        sql.Should().Contain("ORDER BY rc.CONSTRAINT_NAME LIMIT 1;");
        // 制約名は実行時の値なので、クォートの終端文字（`）を二重化してから埋める
        sql.Should()
            .Contain(
                "SET @sql = IF(@fk IS NULL, 'DO 0', CONCAT('ALTER TABLE `order` "
                    + "DROP FOREIGN KEY `', REPLACE(@fk, '`', '``'), '`'));"
            );
        sql.Should().Contain("PREPARE stmt FROM @sql;");
        sql.Should().Contain("EXECUTE stmt;");
        sql.Should().Contain("DEALLOCATE PREPARE stmt;");
    }

    /// <summary>SetTableDescription が ALTER TABLE ... COMMENT を生成することを検証する</summary>
    [Fact(DisplayName = "SetTableDescription は ALTER TABLE COMMENT を生成する")]
    public void SetTableDescription_EmitsAlterTableComment()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.SetTableDescription,
                TableName = "customer",
                NewDescription = "顧客マスタ",
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `customer` COMMENT = '顧客マスタ';");
    }

    /// <summary>列説明の差分項目を組み立てる（Entity は列定義の復元には使われない＝ライブ再構成のため）</summary>
    private static SchemaDiffItem ColumnDesc(
        string table,
        string column,
        string newDescription,
        Entity? entity = null
    ) =>
        new()
        {
            Kind = SchemaDiffKind.SetColumnDescription,
            TableName = table,
            ColumnName = column,
            Entity = entity,
            NewDescription = newDescription,
            IsSelected = true,
        };

    /// <summary>
    /// SetColumnDescription が information_schema からのライブ再構成＋プリペアド動的 SQL で
    /// COMMENT を設定することを検証する。
    /// </summary>
    /// <remarks>
    /// MySQL の MODIFY は列定義の完全再指定なので、モデルの型・NULL 制約だけから復元すると
    /// DEFAULT / AUTO_INCREMENT / 照合順序 / ON UPDATE / INVISIBLE / SRID / 生成列が消える。
    /// 実行時点の実定義を information_schema から組み立て直し、COMMENT だけを差し替える。
    /// </remarks>
    [Fact(
        DisplayName = "SetColumnDescription はライブ再構成した MODIFY COLUMN を動的 SQL で実行する"
    )]
    public void SetColumnDescription_RebuildsDefinitionFromLiveCatalog()
    {
        var sql = Build(ColumnDesc("customer", "name", "顧客名"));

        // SELECT ... INTO は該当行が無いとユーザー変数を書き換えないため、事前に初期化する
        sql.Should().Contain("SET @gen = '';");
        sql.Should().Contain("SET @dfx = '';");
        sql.Should().Contain("SET @cmt = '';");
        sql.Should().Contain("SET @exists = 0;");

        // 実定義の取得元と対象列の特定
        sql.Should().Contain("FROM information_schema.COLUMNS c");
        sql.Should()
            .Contain(
                "WHERE c.TABLE_SCHEMA = DATABASE() AND c.TABLE_NAME = 'customer' "
                    + "AND c.COLUMN_NAME = 'name' LIMIT 1;"
            );
        sql.Should().Contain("INTO @def");

        // 新しい説明は生成時に確定し、リテラル化は実行時の QUOTE() が担う。
        // ライブ検索は @cmt へ既存コメントを読み込むので、新値の代入はその後でなければ上書きされて消える
        sql.Should().Contain("SET @cmt = '顧客名';");
        sql.IndexOf("SET @cmt = '顧客名';", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(sql.IndexOf("INTO @exists, @gen, @dfx, @cmt", StringComparison.Ordinal));
        sql.Should()
            .Contain(
                "SET @sql = IF(@def IS NOT NULL, CONCAT('ALTER TABLE `customer` "
                    + "MODIFY COLUMN `name` ', @def, ' COMMENT ', QUOTE(@cmt)), "
                    + "IF(@exists = 0, 'DO 0', "
                    + "'QuickER sync: customer.name definition could not be rebuilt'));"
            );
        sql.Should().Contain("PREPARE stmt FROM @sql;");
        sql.Should().Contain("EXECUTE stmt;");
        sql.Should().Contain("DEALLOCATE PREPARE stmt;");
    }

    /// <summary>
    /// 再構成の CONCAT が、列に付き得る属性（照合順序・生成列・NULL 制約・SRID・DEFAULT・
    /// INVISIBLE・AUTO_INCREMENT・ON UPDATE）をすべて組み込むことを検証する。
    /// </summary>
    /// <remarks>いずれか 1 つが欠けると、その属性を持つ実 DB の列が同期のたびに静かに失われる。</remarks>
    [Fact(DisplayName = "ライブ再構成は列属性の全句を組み立てる")]
    public void LiveRebuild_IncludesEveryColumnAttributeClause()
    {
        var sql = Build(ColumnDesc("customer", "name", "顧客名"));

        // 型・照合順序
        sql.Should().Contain("c.COLUMN_TYPE,");
        sql.Should()
            .Contain("IF(c.COLLATION_NAME IS NULL, '', CONCAT(' COLLATE ', c.COLLATION_NAME)),");
        // 生成列（式が在るときだけ・VIRTUAL / STORED の別は EXTRA から）
        sql.Should()
            .Contain(
                "IF(@gen = '', '', CONCAT(' GENERATED ALWAYS AS (', @gen, ') ', "
                    + "IF(INSTR(c.EXTRA, 'STORED GENERATED') > 0, 'STORED', 'VIRTUAL'))),"
            );
        // NULL 制約
        sql.Should().Contain("IF(c.IS_NULLABLE = 'YES', ' NULL', ' NOT NULL'),");
        // 空間参照系
        sql.Should().Contain("IF(c.SRS_ID IS NULL, '', CONCAT(' SRID ', c.SRS_ID)),");
        // EXTRA 由来の属性
        sql.Should().Contain("IF(INSTR(c.EXTRA, 'INVISIBLE') > 0, ' INVISIBLE', ''),");
        sql.Should().Contain("IF(INSTR(c.EXTRA, 'auto_increment') > 0, ' AUTO_INCREMENT', ''),");
        // ON UPDATE は精度込みで EXTRA から取り、続く属性（INVISIBLE）を巻き込まない
        sql.Should()
            .Contain(
                "IF(INSTR(c.EXTRA, 'on update ') > 0, CONCAT(' ON UPDATE ', "
                    + "SUBSTRING_INDEX(SUBSTRING(c.EXTRA, INSTR(c.EXTRA, 'on update ') + 10), ' ', 1)), '')"
            );
    }

    /// <summary>DEFAULT 句の 4 分岐（なし / CURRENT_TIMESTAMP / 式既定 / リテラル）が出ることを検証する</summary>
    /// <remarks>
    /// 分岐順は「NULL → CURRENT_TIMESTAMP → DEFAULT_GENERATED → リテラル」。CURRENT_TIMESTAMP の判定を
    /// DEFAULT_GENERATED より先に置くのは、8.0.13 未満では EXTRA に DEFAULT_GENERATED が付かないため。
    /// BIT のリテラル既定（<c>b'1'</c>）と BINARY / VARBINARY のリテラル既定（16 進テキスト
    /// <c>0x6162</c>）は QUOTE すると文字列リテラルに化けるので生出力する。
    /// </remarks>
    [Fact(
        DisplayName = "ライブ再構成の DEFAULT は 4 分岐（なし / CURRENT_TIMESTAMP / 式 / リテラル）"
    )]
    public void LiveRebuild_DefaultClauseHasFourBranches()
    {
        var sql = Build(ColumnDesc("customer", "name", "顧客名"));

        // (a) 既定なしは句を出さない（NOT NULL 列へ DEFAULT NULL を出すと構文エラーになる）
        sql.Should().Contain("IF(c.COLUMN_DEFAULT IS NULL, '',");
        // (b) CURRENT_TIMESTAMP / CURRENT_TIMESTAMP(n) は括弧で包まず生出力する
        sql.Should()
            .Contain(
                "IF(c.DATA_TYPE IN ('timestamp', 'datetime') "
                    + "AND (UPPER(c.COLUMN_DEFAULT) = 'CURRENT_TIMESTAMP' "
                    + @"OR UPPER(c.COLUMN_DEFAULT) LIKE 'CURRENT\_TIMESTAMP(%)'), "
                    + "CONCAT(' DEFAULT ', c.COLUMN_DEFAULT),"
            );
        // (c) 式既定は括弧付きで、リテラル用にエスケープされた値をパーサで復元した @dfx を使う
        sql.Should()
            .Contain(
                "IF(INSTR(c.EXTRA, 'DEFAULT_GENERATED') > 0, CONCAT(' DEFAULT (', @dfx, ')'),"
            );
        // (d) BIT / BINARY / VARBINARY は生出力・それ以外は QUOTE で安全に再引用する
        sql.Should()
            .Contain(
                "IF(c.DATA_TYPE IN ('bit', 'binary', 'varbinary'), "
                    + "CONCAT(' DEFAULT ', c.COLUMN_DEFAULT),"
            );
        sql.Should().Contain("CONCAT(' DEFAULT ', QUOTE(c.COLUMN_DEFAULT))");
    }

    /// <summary>
    /// 生成列の式と式既定を、サーバー自身のパーサへ通して復元する 1 段目の動的 SQL が出ることを検証する。
    /// </summary>
    /// <remarks>
    /// <c>information_schema</c> の <c>GENERATION_EXPRESSION</c> / 式既定の <c>COLUMN_DEFAULT</c> は
    /// 「文字列リテラル用にエスケープした形」で格納されており（<c>'</c> → <c>\'</c>・<c>\</c> → <c>\\</c>）、
    /// そのまま SQL へ埋めると構文エラーになる。REPLACE の逐次適用では復元できないため、
    /// リテラルとして 1 度パースさせて元へ戻す。
    /// </remarks>
    [Fact(DisplayName = "ライブ再構成は生成列の式と式既定をパーサ経由で復元する")]
    public void LiveRebuild_DecodesEscapedExpressionsThroughParser()
    {
        var sql = Build(ColumnDesc("customer", "name", "顧客名"));

        sql.Should()
            .Contain(
                "SELECT 1, c.GENERATION_EXPRESSION, "
                    + "IF(INSTR(c.EXTRA, 'DEFAULT_GENERATED') > 0, IFNULL(c.COLUMN_DEFAULT, ''), ''), "
                    + "c.COLUMN_COMMENT"
            );
        sql.Should().Contain("INTO @exists, @gen, @dfx, @cmt");
        sql.Should()
            .Contain(
                "SET @sql = CONCAT('SELECT ''', IFNULL(@gen, ''), ''', ''', "
                    + "IFNULL(@dfx, ''), ''' INTO @gen, @dfx');"
            );
    }

    /// <summary>説明同期がモデルの型・NULL 制約を一切 SQL へ出さないことを検証する</summary>
    /// <remarks>
    /// 出していると、未選択の AlterColumn（モデル側の型変更）が説明同期に相乗りして黙って適用される。
    /// </remarks>
    [Fact(DisplayName = "SetColumnDescription はモデルの型・NULL 制約を出力しない")]
    public void SetColumnDescription_DoesNotEmitModelTypeOrNullability()
    {
        var e = new Entity { TableName = "customer" };
        e.Columns.Add(
            new Column
            {
                Name = "name",
                DataType = "varchar(50)",
                IsNullable = false,
            }
        );

        var sql = Build(ColumnDesc("customer", "name", "顧客名", e));

        sql.Should().NotContain("varchar(50)");
        sql.Should().NotContain("MODIFY COLUMN `name` varchar");
    }

    /// <summary>列が実 DB に無い場合へ備え、無害な <c>DO 0</c> へ分岐することを検証する</summary>
    /// <remarks>生成時の静的なスキップコメントでは「実行時点で列が在るか」を語れないため実行時判定にする。</remarks>
    [Fact(DisplayName = "SetColumnDescription は列不在時に DO 0 へ分岐する")]
    public void SetColumnDescription_FallsBackToNoOpWhenColumnMissing()
    {
        var sql = Build(ColumnDesc("customer", "gone", "説明"));

        sql.Should().Contain("IF(@exists = 0, 'DO 0'");
        sql.Should().NotContain("-- Skipped");
    }

    /// <summary>
    /// 「列が無い（no-op）」と「列は在るのに定義を再構成できなかった（失敗）」が別の分岐へ落ちることを
    /// 検証する。
    /// </summary>
    /// <remarks>
    /// <c>@def</c> の <c>NULL</c> だけで判断すると、列順同期で移動列の 1 本が畳まれても
    /// 中途半端な列順のまま「成功」になる。存在は <c>@exists</c> が別に持ち、在るのに再構成できない
    /// ときは実行できない文を組み立てて必ず失敗させる（<c>SIGNAL</c> はプリペアド文で使えない）。
    /// </remarks>
    [Theory(DisplayName = "ライブ再構成の失敗は no-op と区別して必ず失敗する文へ倒す")]
    [InlineData(SchemaDiffKind.SetColumnDescription)]
    [InlineData(SchemaDiffKind.ReorderColumns)]
    public void LiveRebuild_DistinguishesMissingColumnFromRebuildFailure(SchemaDiffKind kind)
    {
        var sql =
            kind == SchemaDiffKind.SetColumnDescription
                ? Build(ColumnDesc("t", "c", "説明"))
                : BuildReorder(
                    (ReorderTable("t", "id", "a", "b", "c"), ReorderTable("t", "id", "c", "a", "b"))
                );

        // 存在フラグは検索のたびに初期化され、行が引けたときだけ 1 が入る
        sql.Should().Contain("SET @exists = 0;");
        sql.Should().Contain("INTO @exists, @gen, @dfx, @cmt");

        // 分岐の形: 定義が組めた → ALTER ／ 列が無い → DO 0 ／ 在るのに組めない → 失敗する文
        sql.Should().Contain("SET @sql = IF(@def IS NOT NULL, CONCAT('ALTER TABLE `t` ");
        sql.Should()
            .Contain(
                "IF(@exists = 0, 'DO 0', 'QuickER sync: t.c definition could not be rebuilt'));"
            );
        // 旧実装（両者を DO 0 に畳む形）へ戻っていないこと
        sql.Should().NotContain("IF(@def IS NULL, 'DO 0'");
    }

    /// <summary>説明の <c>'</c> / <c>\</c> / 改行が SET 文のリテラルとしてエスケープされることを検証する</summary>
    /// <remarks>
    /// 実行時の動的 SQL へは <c>QUOTE(@cmt)</c> が安全に再引用するため、生成時のエスケープは
    /// <c>SET @cmt = '…'</c> の 1 段だけでよい。
    /// </remarks>
    [Fact(DisplayName = "SetColumnDescription は説明の ' と \\ を 1 段エスケープする")]
    public void SetColumnDescription_EscapesQuoteAndBackslashOnce()
    {
        var sql = Build(ColumnDesc("customer", "name", "O'Brien\\path\nsecond"));

        sql.Should().Contain("SET @cmt = 'O''Brien\\\\path\nsecond';");
    }

    /// <summary>テーブル名・列名の <c>'</c> が動的 SQL のリテラルとしてもエスケープされることを検証する</summary>
    [Fact(DisplayName = "SetColumnDescription の動的 SQL は名前の ' を二重化する")]
    public void SetColumnDescription_NameWithQuote_IsEscapedInDynamicSql()
    {
        var sql = Build(ColumnDesc("o'rders", "na'me", "説明"));

        // カタログ検索の WHERE 句（素の名前をリテラルとして渡す）
        sql.Should().Contain("c.TABLE_NAME = 'o''rders' AND c.COLUMN_NAME = 'na''me' LIMIT 1;");
        // 組み立てる ALTER 文（クォート済みの名前をさらにリテラルとしてエスケープする）
        sql.Should().Contain("CONCAT('ALTER TABLE `o''rders` MODIFY COLUMN `na''me` ', @def, ");
    }

    /// <summary>テーブル説明が空の場合に空文字 COMMENT（削除）が生成されることを検証する</summary>
    [Fact(DisplayName = "テーブル説明が空なら空文字 COMMENT を生成する")]
    public void EmptyTableDescription_EmitsEmptyComment()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.SetTableDescription,
                TableName = "customer",
                NewDescription = "",
                OldDescription = "古い",
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `customer` COMMENT = '';");
    }

    /// <summary>説明内の単一引用符とバックスラッシュがエスケープされることを検証する</summary>
    [Fact(DisplayName = "説明内の ' と \\ がエスケープされる")]
    public void Description_EscapesSingleQuoteAndBackslash()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.SetTableDescription,
                TableName = "customer",
                NewDescription = @"O'Brien\path",
                IsSelected = true,
            }
        );

        sql.Should().Contain(@"COMMENT = 'O''Brien\\path';");
    }

    /// <summary>RebuildTable は情報表示専用で SQL を生成しないことを検証する</summary>
    [Fact(DisplayName = "RebuildTable は SQL を生成しない")]
    public void RebuildTable_GeneratesNothing()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.RebuildTable,
                TableName = "customer",
                IsSelected = true,
            }
        );

        sql.Should().NotContain("customer");
    }

    /// <summary>未選択の差分項目がスクリプトへ出力されないことを検証する</summary>
    [Fact(DisplayName = "選択されていない項目はスクリプトに含まれない")]
    public void Unselected_Excluded()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "customer",
                ColumnName = "email",
                Column = new Column { Name = "email", DataType = "varchar(200)" },
                IsSelected = false,
            }
        );

        sql.Should().NotContain("email");
    }

    /// <summary>依存関係を満たすよう CREATE → ADD COLUMN → ADD CONSTRAINT の順で出力されることを検証する</summary>
    [Fact(DisplayName = "実行順序: AddTable → AddColumn → AddForeignKey")]
    public void Order_AddTable_Then_AddColumn_Then_Fk()
    {
        var e = new Entity { TableName = "t" };
        e.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        var customer = new Entity { TableName = "customer" };
        customer.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddForeignKey,
                TableName = "t",
                ColumnName = "customer_id",
                ForeignKeyColumnPairs = [new("id", "customer_id")],
                ParentEntity = customer,
                ChildEntity = e,
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "t",
                ColumnName = "customer_id",
                Column = new Column { Name = "customer_id", DataType = "int" },
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "t",
                Entity = e,
                IsSelected = true,
            }
        );

        var iCreate = sql.IndexOf("CREATE TABLE", StringComparison.Ordinal);
        var iAdd = sql.IndexOf("ADD COLUMN `customer_id`", StringComparison.Ordinal);
        var iFk = sql.IndexOf("ADD CONSTRAINT", StringComparison.Ordinal);
        iCreate.Should().BeGreaterThan(-1);
        iAdd.Should().BeGreaterThan(iCreate);
        iFk.Should().BeGreaterThan(iAdd);
    }

    // ---------------- 列順変更 (MODIFY ... AFTER) ----------------

    /// <summary>指定名・順の列（int NULL）を持つエンティティを組み立てる</summary>
    private static Entity ReorderTable(string name, params string[] cols)
    {
        var e = new Entity { TableName = name };

        foreach (var c in cols)
        {
            e.Columns.Add(
                new Column
                {
                    Name = c,
                    DataType = "int",
                    IsNullable = true,
                }
            );
        }

        return e;
    }

    /// <summary>Native ケーパビリティ＋live 土台で ReorderColumns から MySQL スクリプトを生成する</summary>
    private static string BuildReorder(params (Entity Live, Entity Target)[] tables)
    {
        var items = tables
            .Select(t => new SchemaDiffItem
            {
                Kind = SchemaDiffKind.ReorderColumns,
                TableName = t.Live.TableName,
                Entity = t.Target,
                IsSelected = true,
            })
            .ToArray();
        var context = new SyncPlanContext { LiveEntities = tables.Select(t => t.Live).ToArray() };
        var plan = new SyncPlanner().BuildPlan(
            items,
            new SyncDialectCapabilities { ColumnReorder = ColumnReorderMode.Native },
            context
        );
        return new MySqlSyncScriptBuilder().Build(plan);
    }

    /// <summary>列順変更が見出しとライブ再構成の MODIFY COLUMN ... AFTER を生成することを検証する</summary>
    /// <remarks>位置しか変えない操作なので、列定義は実 DB から再構成し COMMENT もライブの値を温存する。</remarks>
    [Fact(DisplayName = "ReorderColumns はライブ再構成した MODIFY COLUMN ... AFTER を生成する")]
    public void Reorder_GeneratesModifyAfterWithHeading()
    {
        // live: id,a,b,c → target: id,c,a,b（c を id の直後へ）
        var sql = BuildReorder(
            (ReorderTable("t", "id", "a", "b", "c"), ReorderTable("t", "id", "c", "a", "b"))
        );

        sql.Should().Contain("-- ===== ReorderColumns: t =====");
        sql.Should()
            .Contain(
                "WHERE c.TABLE_SCHEMA = DATABASE() AND c.TABLE_NAME = 't' "
                    + "AND c.COLUMN_NAME = 'c' LIMIT 1;"
            );
        sql.Should()
            .Contain(
                "SET @sql = IF(@def IS NOT NULL, CONCAT('ALTER TABLE `t` "
                    + "MODIFY COLUMN `c` ', @def, ' COMMENT ', QUOTE(@cmt), ' AFTER `id`'), "
                    + "IF(@exists = 0, 'DO 0', "
                    + "'QuickER sync: t.c definition could not be rebuilt'));"
            );
        sql.Should().Contain("DEALLOCATE PREPARE stmt;");
    }

    /// <summary>列順変更がモデルの型・NULL 制約・説明を出力しないことを検証する</summary>
    /// <remarks>
    /// 位置変更でモデル再指定すると、実 DB の DEFAULT / AUTO_INCREMENT / 照合順序などを落とす。
    /// COMMENT もライブの値（<c>@cmt</c>）を温存し、生成時に値を焼き込まない。
    /// </remarks>
    [Fact(DisplayName = "ReorderColumns はモデルの列定義・説明を出力しない")]
    public void Reorder_DoesNotEmitModelDefinition()
    {
        var sql = BuildReorder(
            (ReorderTable("t", "id", "a", "b", "c"), ReorderTable("t", "id", "c", "a", "b"))
        );

        sql.Should().NotContain("MODIFY COLUMN `c` int");
        sql.Should().Contain("QUOTE(@cmt)");

        // 説明はライブの COLUMN_COMMENT を QUOTE して使う＝生成時に値を焼き込まない
        // （@cmt へ現れる代入は、カタログ検索前の空初期化だけであること）
        sql.Replace("\r\n", "\n")
            .Split('\n')
            .Where(line => line.StartsWith("SET @cmt", StringComparison.Ordinal))
            .Should()
            .AllBe("SET @cmt = '';");
    }

    /// <summary>先頭へ動かす列が FIRST を生成することを検証する</summary>
    [Fact(DisplayName = "ReorderColumns は先頭移動で FIRST を生成する")]
    public void Reorder_MoveToFront_GeneratesFirst()
    {
        // live: a,b,c → target: c,a,b（c を先頭へ）
        var sql = BuildReorder(
            (ReorderTable("t", "a", "b", "c"), ReorderTable("t", "c", "a", "b"))
        );

        sql.Should()
            .Contain(
                "SET @sql = IF(@def IS NOT NULL, CONCAT('ALTER TABLE `t` "
                    + "MODIFY COLUMN `c` ', @def, ' COMMENT ', QUOTE(@cmt), ' FIRST'), "
                    + "IF(@exists = 0, 'DO 0', "
                    + "'QuickER sync: t.c definition could not be rebuilt'));"
            );
        sql.Should().NotContain("AFTER");
    }

    /// <summary>複数テーブルの列順変更がテーブルごとの見出しで出力されることを検証する</summary>
    [Fact(DisplayName = "ReorderColumns は複数テーブルをテーブルごとに出力する")]
    public void Reorder_MultipleTables_EmitsPerTableHeadings()
    {
        var sql = BuildReorder(
            (ReorderTable("t1", "id", "a", "b", "c"), ReorderTable("t1", "id", "c", "a", "b")),
            (ReorderTable("t2", "x", "y", "z"), ReorderTable("t2", "z", "x", "y"))
        );

        sql.Should().Contain("-- ===== ReorderColumns: t1 =====");
        sql.Should().Contain("-- ===== ReorderColumns: t2 =====");
        sql.Should()
            .Contain(
                "CONCAT('ALTER TABLE `t1` MODIFY COLUMN `c` ', @def, "
                    + "' COMMENT ', QUOTE(@cmt), ' AFTER `id`')"
            );
        sql.Should()
            .Contain(
                "CONCAT('ALTER TABLE `t2` MODIFY COLUMN `z` ', @def, "
                    + "' COMMENT ', QUOTE(@cmt), ' FIRST')"
            );
    }

    // ---------------- 主キー変更（AlterPrimaryKey） ----------------

    /// <summary>指定の主キー列を持つ target エンティティを組み立てる</summary>
    private static Entity PkTarget(string table, params string[] pkColumns)
    {
        var e = new Entity { TableName = table };
        e.Columns.Add(
            new Column
            {
                Name = "memo",
                DataType = "varchar(50)",
                IsNullable = true,
            }
        );

        foreach (var name in pkColumns)
        {
            e.Columns.Add(
                new Column
                {
                    Name = name,
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                }
            );
        }

        return e;
    }

    /// <summary>主キー変更の差分項目を生成する（Entity＝新しい主キー構成の源）</summary>
    private static SchemaDiffItem AlterPk(string table, Entity target) =>
        new()
        {
            Kind = SchemaDiffKind.AlterPrimaryKey,
            TableName = table,
            Entity = target,
            IsSelected = true,
        };

    /// <summary>主キー変更が存在確認付きの動的 DROP と ADD PRIMARY KEY を生成することを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は存在確認付き DROP と複合 PK の ADD を生成する")]
    public void AlterPrimaryKey_DropsExistingAndAddsComposite()
    {
        var sql = Build(AlterPk("orders", PkTarget("orders", "order_id", "line_no")));

        // 主キーが無いテーブルでは DROP PRIMARY KEY がエラーになるため存在確認してから動的 SQL で外す
        sql.Should().Contain("SET @pk = NULL;");
        sql.Should().Contain("FROM information_schema.TABLE_CONSTRAINTS tc");
        sql.Should()
            .Contain(
                "WHERE tc.CONSTRAINT_SCHEMA = DATABASE() AND tc.TABLE_NAME = 'orders' "
                    + "AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY' LIMIT 1;"
            );
        sql.Should()
            .Contain(
                "SET @sql = IF(@pk IS NULL, 'DO 0', 'ALTER TABLE `orders` DROP PRIMARY KEY');"
            );
        sql.Should().Contain("PREPARE stmt FROM @sql;");
        // MySQL の主キー名は PRIMARY 固定のため CONSTRAINT 名は指定しない
        sql.Should().Contain("ALTER TABLE `orders` ADD PRIMARY KEY (`order_id`, `line_no`);");
        sql.Should().NotContain("ADD CONSTRAINT");
    }

    /// <summary>主キーが無いテーブルへの主キー付与でも DROP が無害な形（DO 0 へ分岐）で出ることを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は PK 付与のみでも DROP を no-op 形で出す")]
    public void AlterPrimaryKey_AddOnly_EmitsGuardedDrop()
    {
        var sql = Build(AlterPk("customer", PkTarget("customer", "id")));

        sql.Should().Contain("IF(@pk IS NULL, 'DO 0'");
        sql.Should().Contain("ALTER TABLE `customer` ADD PRIMARY KEY (`id`);");
    }

    /// <summary>主キーの解除のみ（新主キー列ゼロ）では付与文が出ないことを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は PK 解除のみなら ADD を出さない")]
    public void AlterPrimaryKey_DropOnly_OmitsAdd()
    {
        var sql = Build(AlterPk("customer", PkTarget("customer")));

        sql.Should().Contain("IF(@pk IS NULL, 'DO 0'");
        sql.Should().NotContain("ADD PRIMARY KEY");
    }

    /// <summary>
    /// 主キー変更が 2 フェーズへ分かれ、列定義変更を挟んで「PK DROP → MODIFY COLUMN → PK ADD」の順に
    /// 出力されることを検証する（PK 制約が残ったままの旧 PK 列の NULL 許容化は失敗し、MySQL は部分適用で止まる）。
    /// </summary>
    [Fact(DisplayName = "AlterPrimaryKey は AlterColumn を挟んで DROP → MODIFY → ADD の順に出る")]
    public void AlterPrimaryKey_SplitsAroundAlterColumn()
    {
        var alterColumn = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "orders",
            ColumnName = "old_id",
            Column = new Column
            {
                Name = "old_id",
                DataType = "int",
                IsNullable = true,
            },
            IsSelected = true,
        };

        var sql = Build(AlterPk("orders", PkTarget("orders", "order_id")), alterColumn);

        var drop = sql.IndexOf("SET @pk = NULL;", StringComparison.Ordinal);
        var alter = sql.IndexOf("MODIFY COLUMN `old_id`", StringComparison.Ordinal);
        var add = sql.IndexOf("ADD PRIMARY KEY (`order_id`);", StringComparison.Ordinal);

        drop.Should().BeGreaterThan(-1);
        alter.Should().BeGreaterThan(drop);
        add.Should().BeGreaterThan(alter);
    }

    /// <summary>AddUniqueConstraint が ALTER TABLE ... ADD CONSTRAINT ... UNIQUE を生成することを検証する</summary>
    [Fact(DisplayName = "AddUniqueConstraint は ADD CONSTRAINT UNIQUE を生成する")]
    public void AddUniqueConstraint_GeneratesAddConstraint()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddUniqueConstraint,
            TableName = "customer",
            UniqueConstraintColumns = ["code", "kind"],
            IsSelected = true,
        };

        var sql = Build(item);
        // 制約名は未設定のため UQ_{テーブル}_{列…} が合成される
        sql.Should()
            .Contain(
                "ALTER TABLE `customer` ADD CONSTRAINT `UQ_customer_code_kind` UNIQUE (`code`, `kind`);"
            );
    }

    /// <summary>DropUniqueConstraint が DROP CONSTRAINT ではなく DROP INDEX を生成することを検証する</summary>
    /// <remarks>MySQL の一意制約は実体が一意インデックスで、DROP CONSTRAINT は 8.0.19 未満で通らない。</remarks>
    [Fact(DisplayName = "DropUniqueConstraint は DROP INDEX を生成する")]
    public void DropUniqueConstraint_GeneratesDropIndex()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropUniqueConstraint,
            TableName = "customer",
            UniqueConstraintName = "uq_legacy",
            UniqueConstraintColumns = ["code"],
            IsSelected = true,
        };

        var sql = Build(item);
        sql.Should().Contain("ALTER TABLE `customer` DROP INDEX `uq_legacy`;");
        sql.Should().NotContain("DROP CONSTRAINT");
    }

    // ---------------- 動的 SQL のリテラルエスケープ ----------------

    /// <summary>
    /// 主キー解除のプリペアド動的 SQL で、クォートしたテーブル名が文字列リテラル用にもエスケープされることを検証する。
    /// </summary>
    /// <remarks>
    /// <c>SET @sql = IF(…, '…')</c> の中身は文字列リテラルなので、クォートだけではテーブル名の <c>'</c> が
    /// リテラルを閉じ、以降が SQL のコードとして解釈される。
    /// <c>MySqlIdentifier.QuoteForDynamicSql</c> を通していれば <c>''</c> になる。
    /// </remarks>
    [Fact(DisplayName = "AlterPrimaryKey の動的 SQL はテーブル名の ' を二重化する")]
    public void AlterPrimaryKey_TableNameWithQuote_IsEscapedInDynamicSql()
    {
        var sql = Build(AlterPk("o'rders", PkTarget("o'rders", "order_id")));

        sql.Should()
            .Contain(
                "SET @sql = IF(@pk IS NULL, 'DO 0', 'ALTER TABLE `o''rders` DROP PRIMARY KEY');"
            );
        sql.Should().NotContain("'ALTER TABLE `o'rders`");
    }

    /// <summary>制約名不明の外部キー削除でも、CONCAT のテーブル名がリテラル用にエスケープされることを検証する</summary>
    [Fact(DisplayName = "DropForeignKey の動的 SQL はテーブル名の ' を二重化する")]
    public void DropForeignKey_TableNameWithQuote_IsEscapedInDynamicSql()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = "o'rders",
            ParentEntity = new Entity { TableName = "customer" },
            ChildEntity = new Entity { TableName = "o'rders" },
            IsSelected = true,
        };

        var sql = Build(item);

        sql.Should()
            .Contain(
                "CONCAT('ALTER TABLE `o''rders` DROP FOREIGN KEY `', REPLACE(@fk, '`', '``'), '`')"
            );
        sql.Should().NotContain("CONCAT('ALTER TABLE `o'rders`");
    }

    /// <summary>制約名不明の外部キー削除が、逆引きの前に @fk を NULL 初期化することを検証する</summary>
    [Fact(DisplayName = "DropForeignKey は逆引きの前に @fk を NULL 初期化する")]
    public void DropForeignKey_InitializesVariableBeforeLookup()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "order",
                ParentEntity = new Entity { TableName = "customer" },
                ChildEntity = new Entity { TableName = "order" },
                IsSelected = true,
            }
        );

        // SELECT ... INTO は該当行が無いとユーザー変数を書き換えないため、直前に NULL で初期化する
        // （初期化しないと 2 件目以降が直前の FK 名で誤って DROP する）
        sql.Should().Contain("SET @fk = NULL;");
        sql.IndexOf("SET @fk = NULL;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(
                sql.IndexOf("SELECT rc.CONSTRAINT_NAME INTO @fk", StringComparison.Ordinal)
            );
    }

    // ---------------- AddTable の UNIQUE 制約（DDL 生成との同形性） ----------------

    /// <summary>一意制約の検証用エンティティ（id / code / region の 3 列・id が主キー）を作る</summary>
    private static Entity BuildUniqueEntity()
    {
        var entity = new Entity { TableName = "shops" };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "varchar(20)",
                IsNullable = false,
            }
        );
        entity.Columns.Add(
            new Column
            {
                Name = "region",
                DataType = "varchar(10)",
                IsNullable = false,
            }
        );
        return entity;
    }

    /// <summary>エンティティ 1 件の AddTable 差分から同期スクリプトを生成する</summary>
    private static string BuildAddTable(Entity entity) =>
        Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = entity.TableName,
                Entity = entity,
                IsSelected = true,
            }
        );

    /// <summary>SQL から UNIQUE 制約行（前後空白・末尾の区切りカンマを除く）を抜き出す</summary>
    private static List<string> UniqueConstraintLines(string sql) =>
        sql.Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Trim().TrimEnd(','))
            .Where(line => line.Contains("UNIQUE (", StringComparison.Ordinal))
            .ToList();

    /// <summary>名前付き単一列の一意制約が CREATE TABLE へインライン出力されることを検証する</summary>
    [Fact(DisplayName = "AddTable は名前付き単一列 UNIQUE を CREATE TABLE に含む")]
    public void AddTable_NamedSingleColumnUnique_EmitsConstraint()
    {
        var entity = BuildUniqueEntity();
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_shops_code", ColumnIds = [entity.Columns[1].Id] }
        );

        var sql = BuildAddTable(entity);

        // PK 行には後続の UNIQUE 行が続くため区切りカンマが付く
        sql.Should().Contain("CONSTRAINT `PK_shops` PRIMARY KEY (`id`),");
        sql.Should().Contain("CONSTRAINT `UQ_shops_code` UNIQUE (`code`)");
        // 最後の制約行に余分なカンマは付かない
        sql.Should().NotContain("UNIQUE (`code`),");
    }

    /// <summary>制約名なしの複合一意制約が合成名・宣言順で出力されることを検証する</summary>
    [Fact(DisplayName = "AddTable は名前なし複合 UNIQUE を合成名で出力する")]
    public void AddTable_UnnamedCompositeUnique_SynthesizesName()
    {
        var entity = BuildUniqueEntity();
        // 宣言順は region → code（列定義順とは逆）
        entity.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [entity.Columns[2].Id, entity.Columns[1].Id] }
        );

        BuildAddTable(entity)
            .Should()
            .Contain("CONSTRAINT `UQ_shops_region_code` UNIQUE (`region`, `code`)");
    }

    /// <summary>同期の CREATE TABLE と DDL 生成の UNIQUE 句が同形（名前・列並び・位置）であることを検証する</summary>
    [Fact(DisplayName = "AddTable の UNIQUE 句は DDL 生成と同形")]
    public void AddTable_UniqueConstraints_MatchDdlGenerator()
    {
        var entity = BuildUniqueEntity();
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_shops_code", ColumnIds = [entity.Columns[1].Id] }
        );
        entity.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [entity.Columns[2].Id, entity.Columns[1].Id] }
        );

        var ddl = new MySqlDdlGenerator().Build(new ErDiagram { Entities = { entity } });
        var sync = BuildAddTable(entity);

        UniqueConstraintLines(sync).Should().HaveCount(2);
        UniqueConstraintLines(sync).Should().Equal(UniqueConstraintLines(ddl));

        // 位置も同形＝PK 制約行より後、CREATE TABLE の閉じ括弧より前
        var pk = sync.IndexOf("PRIMARY KEY (", StringComparison.Ordinal);
        var unique = sync.IndexOf("UNIQUE (", StringComparison.Ordinal);
        var close = sync.IndexOf(");", StringComparison.Ordinal);
        pk.Should().BeLessThan(unique);
        unique.Should().BeLessThan(close);
    }

    /// <summary>一意制約を持たないテーブルでは UNIQUE 行を 1 行も出力しないことを検証する</summary>
    [Fact(DisplayName = "AddTable は一意制約が無ければ UNIQUE を出力しない")]
    public void AddTable_WithoutUniqueConstraints_EmitsNoUnique()
    {
        BuildAddTable(BuildUniqueEntity()).Should().NotContain("UNIQUE");
    }

    // ---------------- カタログ検索のスキーマスコープ ----------------

    /// <summary>
    /// スキーマ修飾名を操作する文が、カタログ検索も同じスキーマへ絞ることを検証する。
    /// </summary>
    /// <remarks>
    /// <c>DATABASE()</c> 固定だと、<c>other.t</c> を操作する文がカレント DB の同名テーブルの
    /// 定義を引き当て、それを <c>other.t</c> へ焼き付ける（同名が無ければ引き当て失敗＝黙って成功報告）。
    /// </remarks>
    [Fact(DisplayName = "修飾名のライブ再構成はカタログ検索もそのスキーマへ絞る")]
    public void LiveRebuild_QualifiedTable_ScopesCatalogLookupToThatSchema()
    {
        var sql = Build(ColumnDesc("other.t", "memo", "説明"));

        sql.Should()
            .Contain(
                "WHERE c.TABLE_SCHEMA = 'other' AND c.TABLE_NAME = 't' "
                    + "AND c.COLUMN_NAME = 'memo' LIMIT 1;"
            );
        sql.Should().NotContain("c.TABLE_SCHEMA = DATABASE()");
        // 操作対象は従来どおり分割クォートした修飾名
        sql.Should().Contain("CONCAT('ALTER TABLE `other`.`t` MODIFY COLUMN `memo` ', @def, ");
    }

    /// <summary>無修飾名のカタログ検索は従来どおりカレント DB を指すことを検証する</summary>
    [Fact(DisplayName = "無修飾名のライブ再構成は DATABASE() を指す")]
    public void LiveRebuild_UnqualifiedTable_ScopesCatalogLookupToCurrentDatabase()
    {
        Build(ColumnDesc("t", "memo", "説明"))
            .Should()
            .Contain("WHERE c.TABLE_SCHEMA = DATABASE() AND c.TABLE_NAME = 't' ");
    }

    /// <summary>スキーマ名の <c>'</c> がリテラルとしてエスケープされることを検証する</summary>
    [Fact(DisplayName = "スキーマ名の ' はカタログ検索のリテラルとして二重化される")]
    public void LiveRebuild_SchemaNameWithQuote_IsEscaped()
    {
        Build(ColumnDesc("o'ther.t", "memo", "説明"))
            .Should()
            .Contain("WHERE c.TABLE_SCHEMA = 'o''ther' AND c.TABLE_NAME = 't' ");
    }

    /// <summary>主キー逆引きも修飾名のスキーマへ絞ることを検証する</summary>
    [Fact(DisplayName = "修飾名の主キー逆引きはそのスキーマへ絞る")]
    public void DropPrimaryKey_QualifiedTable_ScopesLookupToThatSchema()
    {
        var sql = Build(AlterPk("other.orders", PkTarget("other.orders", "id")));

        sql.Should()
            .Contain(
                "WHERE tc.CONSTRAINT_SCHEMA = 'other' AND tc.TABLE_NAME = 'orders' "
                    + "AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY' LIMIT 1;"
            );
        sql.Should().NotContain("tc.CONSTRAINT_SCHEMA = DATABASE()");
    }

    /// <summary>外部キー逆引きが子・親それぞれのスキーマへ絞ることを検証する</summary>
    [Fact(DisplayName = "修飾名の外部キー逆引きは子・親のスキーマへ絞る")]
    public void DropForeignKey_QualifiedTables_ScopeLookupToTheirSchemas()
    {
        var parent = new Entity { TableName = "sales.customer" };
        var child = new Entity { TableName = "other.order" };

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "other.order",
                ParentEntity = parent,
                ChildEntity = child,
                IsSelected = true,
            }
        );

        sql.Should()
            .Contain(
                "WHERE rc.CONSTRAINT_SCHEMA = 'other' AND rc.TABLE_NAME = 'order' "
                    + "AND rc.REFERENCED_TABLE_NAME = 'customer' "
                    + "AND rc.UNIQUE_CONSTRAINT_SCHEMA = 'sales' "
                    + "ORDER BY rc.CONSTRAINT_NAME LIMIT 1;"
            );
    }

    /// <summary>親が無修飾なら参照先スキーマの絞り込みを足さない（従来出力のまま）ことを検証する</summary>
    [Fact(DisplayName = "親が無修飾なら外部キー逆引きに参照先スキーマ条件を足さない")]
    public void DropForeignKey_UnqualifiedParent_OmitsReferencedSchemaPredicate()
    {
        var parent = new Entity { TableName = "customer" };
        var child = new Entity { TableName = "order" };

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "order",
                ParentEntity = parent,
                ChildEntity = child,
                IsSelected = true,
            }
        );

        sql.Should().NotContain("rc.UNIQUE_CONSTRAINT_SCHEMA");
    }
}
