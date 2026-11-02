using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider.MySql;

/// <summary>
/// <see cref="MySqlSyncScriptBuilder"/> が差分から生成する MySQL DDL のうち、実 DB で流しても観測できない文字列の形を検証するテストクラス
/// </summary>
/// <remarks>
/// 実 DB で流して結果を観測できる文の形は、同期計画の期待の行（<c>SyncPlanExpectationRows.RealMySql.cs</c> の
/// <c>realdb-mysql-script-*</c> など）が実 MySQL で流して見る。ここに残すのは、実方言の差分からは届かない分岐
/// （実行時に列が無い・名前の無い外部キーの削除・スキーマ修飾の表）と、結果が同じになる出力の形（見出し・注意コメントの位置など）。
/// </remarks>
public class MySqlSyncScriptBuilderTests
{
    private static string Build(params SchemaDiffItem[] items) =>
        new MySqlSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(items, TestDialectCapabilities.Neutral)
        );

    /// <summary>
    /// AlterColumn は、実行時に列が無ければ失敗させる（説明・列順の同期と違い DO 0 にしない）ことを検証する
    /// （差分の項目は DB にある列から作るので、列が無い分岐は実 DB の行では通らない）。
    /// </summary>
    [Theory(DisplayName = "AlterColumn は実行時の定義から組み、型と NULL 許容だけを差し替える")]
    [InlineData(false)]
    [InlineData(true)]
    public void AlterColumn_RebuildsLiveDefinitionWithTypeAndNullability(bool nullable)
    {
        var sql = Build(AlterColumn("name", "varchar(100)", nullable));

        // 実行時に列が無ければ失敗させる（説明・列順の同期は DO 0）
        sql.Should().Contain("IF(@exists = 0, 'QuickER sync: customer.name does not exist'");
        sql.Should().NotContain("'DO 0'");
    }

    /// <summary>
    /// 新しい型が数値型のときは照合順序を書き添えないことを検証する
    /// （MySQL は数値型の <c>COLLATE</c> を黙って無視するので、書いても書かなくても実 DB の状態は同じ）。
    /// </summary>
    [Theory(DisplayName = "AlterColumn は新しい型が文字列型のときだけ照合順序を書き添える")]
    [InlineData("int")]
    [InlineData("decimal(5,2)")]
    public void AlterColumn_KeepsCollationOnlyForStringTypes(string type)
    {
        Build(AlterColumn("name", type, nullable: true))
            .Should()
            .NotContain("CONCAT(' COLLATE ', c.COLLATION_NAME)");
    }

    /// <summary>説明の同期は、実行時に列が無ければ何もしない（列の変更とは分かれる）ことを検証する</summary>
    [Fact(DisplayName = "説明の同期は、実行時に列が無ければ DO 0 で何もしない")]
    public void SetColumnDescription_MissingColumn_IsNoOp()
    {
        Build(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.SetColumnDescription,
                    TableName = "customer",
                    ColumnName = "name",
                    NewDescription = "x",
                    IsSelected = true,
                }
            )
            .Should()
            .Contain("IF(@exists = 0, 'DO 0'");
    }

    private static SchemaDiffItem AlterColumn(string column, string type, bool nullable) =>
        new()
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "customer",
            ColumnName = column,
            Column = new Column
            {
                Name = column,
                DataType = type,
                IsNullable = nullable,
            },
            IsSelected = true,
        };

    /// <summary>InnoDB で効かない SET DEFAULT の注意コメントを、外部キーを足す文の直前に置くことを検証する</summary>
    [Theory(DisplayName = "AddForeignKey は SET DEFAULT の句を出さず注意コメントを添える")]
    [InlineData(ForeignKeyReferentialAction.SetDefault, ForeignKeyReferentialAction.Cascade)]
    [InlineData(ForeignKeyReferentialAction.Cascade, ForeignKeyReferentialAction.SetDefault)]
    public void AddForeignKey_SetDefault_IsOmittedWithNote(
        ForeignKeyReferentialAction onDelete,
        ForeignKeyReferentialAction onUpdate
    )
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
            OnDelete = onDelete,
            OnUpdate = onUpdate,
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

        sql.Should()
            .Contain(
                MySqlReferentialAction.SetDefaultNote
                    + Environment.NewLine
                    + "ALTER TABLE `order` ADD CONSTRAINT `FK_order_customer`"
            );
    }

    /// <summary>
    /// DropForeignKey が、外部キーを外した後に MySQL が残した同じ名前の索引を外すか付け替える文を続けることを検証する
    /// （残すと、同じ名前の外部キーを別の列へ付け直すときエラー 1061 になる）。ここに残すのは、名前を逆引きする場合と、
    /// スキーマ修飾の子表の場合（どちらも実方言の差分からは届かない）。
    /// </summary>
    [Theory(
        DisplayName = "DropForeignKey は外部キーを外した後に残る同じ名前の索引を外すか付け替える"
    )]
    [InlineData("order", null, "s.INDEX_NAME = @fk", "DATABASE()")]
    [InlineData(
        "other.order",
        "FK_order_customer",
        "s.INDEX_NAME = 'FK_order_customer'",
        "'other'"
    )]
    public void DropForeignKey_DropsOrRenamesLeftoverIndexOfSameName(
        string childTable,
        string? constraintName,
        string expectedNameMatch,
        string expectedSchema
    )
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = childTable,
                ParentEntity = new Entity { TableName = "customer" },
                ChildEntity = new Entity { TableName = childTable },
                ForeignKeyName = constraintName,
                IsSelected = true,
            }
        );
        var quotedChild = childTable == "order" ? "`order`" : "`other`.`order`";

        var dropForeignKey = sql.IndexOf("DROP FOREIGN KEY", System.StringComparison.Ordinal);
        var lookup = sql.IndexOf("SELECT s.INDEX_NAME,", System.StringComparison.Ordinal);
        dropForeignKey.Should().BeGreaterThanOrEqualTo(0);
        lookup.Should().BeGreaterThan(dropForeignKey);
        sql.Should().Contain("SET @ix = NULL;");
        sql.Should().Contain("SET @to = NULL;");
        sql.Should().Contain(expectedNameMatch);
        sql.Should().Contain($"s.TABLE_SCHEMA = {expectedSchema}");
        sql.Should().Contain("s.NON_UNIQUE = 1");
        // 付け替える先は、索引の先頭の列を先頭に使うほかの外部キーの名前（名前順で最初）
        sql.Should()
            .Contain(
                "k.REFERENCED_TABLE_NAME IS NOT NULL AND k.ORDINAL_POSITION = 1 "
                    + "AND k.COLUMN_NAME = s.COLUMN_NAME "
                    + "ORDER BY k.CONSTRAINT_NAME LIMIT 1) INTO @ix, @to"
            );
        sql.Should()
            .Contain(
                "SET @sql = CASE WHEN @ix IS NULL THEN 'DO 0' "
                    + $"WHEN @to IS NULL THEN CONCAT('ALTER TABLE {quotedChild} "
                    + "DROP INDEX `', REPLACE(@ix, '`', '``'), '`') "
                    + "WHEN EXISTS (SELECT 1 FROM information_schema.STATISTICS x "
                    + $"WHERE x.TABLE_SCHEMA = {expectedSchema} "
                    + "AND x.TABLE_NAME = 'order' AND x.INDEX_NAME = @to) THEN 'DO 0' "
                    + $"ELSE CONCAT('ALTER TABLE {quotedChild} RENAME INDEX `', "
                    + "REPLACE(@ix, '`', '``'), '` TO `', REPLACE(@to, '`', '``'), '`') END;"
            );
    }

    /// <summary>
    /// 同じ子表の外部キーを続けて外すとき、子表に 1 本だけの外部キーには索引の確かめ直しを足さないことを検証する
    /// （余分な確かめ直しは結果を変えない）。
    /// </summary>
    [Fact(
        DisplayName = "DropForeignKey は同じ子表の後の外部キーを外した後に、先に外した外部キーの名前の索引を確かめ直す"
    )]
    public void DropForeignKey_RechecksLeftoverIndexAfterLaterDropOnSameTable()
    {
        static SchemaDiffItem Drop(string child, string name) =>
            new()
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = child,
                ParentEntity = new Entity { TableName = "customer" },
                ChildEntity = new Entity { TableName = child },
                ForeignKeyName = name,
                IsSelected = true,
            };

        var sql = Build(Drop("order", "fk_a"), Drop("invoice", "fk_c"), Drop("order", "fk_b"));

        static int Count(string text, string value)
        {
            var count = 0;

            for (
                var index = text.IndexOf(value, System.StringComparison.Ordinal);
                index >= 0;
                index = text.IndexOf(value, index + 1, System.StringComparison.Ordinal)
            )
            {
                count++;
            }

            return count;
        }

        Count(sql, "s.INDEX_NAME = 'fk_c'").Should().Be(1);
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
            TestDialectCapabilities.Neutral with
            {
                ColumnReorder = ColumnReorderMode.Native,
            },
            context
        );
        return new MySqlSyncScriptBuilder().Build(plan);
    }

    /// <summary>列順変更が表の見出しのコメントを出すことを検証する（見出しは実行の結果に出ない）</summary>
    [Fact(DisplayName = "ReorderColumns はライブ再構成した MODIFY COLUMN ... AFTER を生成する")]
    public void Reorder_GeneratesModifyAfterWithHeading()
    {
        // live: id,a,b,c → target: id,c,a,b（c を id の直後へ）
        var sql = BuildReorder(
            (ReorderTable("t", "id", "a", "b", "c"), ReorderTable("t", "id", "c", "a", "b"))
        );

        sql.Should().Contain("-- ===== ReorderColumns: t =====");
    }

    /// <summary>複数テーブルの列順変更がテーブルごとの見出しで出力されることを検証する（見出しは実行の結果に出ない）</summary>
    [Fact(DisplayName = "ReorderColumns は複数テーブルをテーブルごとに出力する")]
    public void Reorder_MultipleTables_EmitsPerTableHeadings()
    {
        var sql = BuildReorder(
            (ReorderTable("t1", "id", "a", "b", "c"), ReorderTable("t1", "id", "c", "a", "b")),
            (ReorderTable("t2", "x", "y", "z"), ReorderTable("t2", "z", "x", "y"))
        );

        sql.Should().Contain("-- ===== ReorderColumns: t1 =====");
        sql.Should().Contain("-- ===== ReorderColumns: t2 =====");
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

    /// <summary>DropUniqueConstraint が DROP CONSTRAINT を使わないことを検証する</summary>
    /// <remarks>
    /// MySQL の一意制約は実体が一意インデックスで、DROP CONSTRAINT は 8.0.19 未満で通らない
    /// （検証用の 8.4 では両方通るので、実 DB の行では違いが出ない）。
    /// </remarks>
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

        Build(item).Should().NotContain("DROP CONSTRAINT");
    }

    // ---------------- 動的 SQL のリテラルエスケープ ----------------

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
