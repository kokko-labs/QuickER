using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;

namespace QuickER.Tests.Provider.PostgreSql;

/// <summary><see cref="PostgreSqlSyncScriptBuilder"/> が差分から生成する PostgreSQL DDL の内容と出力順序を検証するテストクラス</summary>
public class PostgreSqlSyncScriptBuilderTests
{
    private static string Build(params SchemaDiffItem[] items) =>
        new PostgreSqlSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(items, new SyncDialectCapabilities())
        );

    /// <summary>AddTable が主キー制約を含む CREATE TABLE 文を生成し、GO を使わないことを検証する</summary>
    [Fact(DisplayName = "AddTable は CREATE TABLE と PK を含み GO を使わない")]
    public void AddTable_GeneratesCreate()
    {
        var e = new Entity { TableName = "customer" };
        e.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "integer",
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

        sql.Should().Contain("CREATE TABLE \"customer\"");
        sql.Should().Contain("\"id\" integer NOT NULL");
        sql.Should().Contain("CONSTRAINT \"PK_customer\" PRIMARY KEY (\"id\")");
        sql.Should().NotContain("GO");
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

        sql.Should()
            .Contain("ALTER TABLE \"customer\" ADD COLUMN \"email\" varchar(200) NOT NULL;");
    }

    /// <summary>AlterColumn が型変更（TYPE）と NULL 制約（SET NOT NULL）を別文で生成することを検証する</summary>
    [Fact(DisplayName = "AlterColumn は TYPE と SET NOT NULL を別文で生成する")]
    public void AlterColumn_GeneratesTypeAndNotNull()
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

        sql.Should().Contain("ALTER TABLE \"customer\" ALTER COLUMN \"name\" TYPE varchar(100);");
        sql.Should().Contain("ALTER TABLE \"customer\" ALTER COLUMN \"name\" SET NOT NULL;");
    }

    /// <summary>NULL 許容へ変更する AlterColumn が DROP NOT NULL を生成することを検証する</summary>
    [Fact(DisplayName = "AlterColumn は NULL 許容化で DROP NOT NULL を生成する")]
    public void AlterColumn_NullableGeneratesDropNotNull()
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

        sql.Should().Contain("ALTER TABLE \"customer\" ALTER COLUMN \"note\" DROP NOT NULL;");
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

        sql.Should().Contain("ALTER TABLE \"customer\" DROP COLUMN \"old\";");
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

        sql.Should().Contain("DROP TABLE \"customer\";");
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
                DataType = "integer",
                IsPrimaryKey = true,
            }
        );
        var order = new Entity { TableName = "order" };
        order.Columns.Add(new Column { Name = "customer_id", DataType = "integer" });
        var rel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(customer.Columns[0].Id, order.Columns[^1].Id)],
            ConstraintName = "FK_order_customer",
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.SetDefault,
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

        sql.Should().Contain("ALTER TABLE \"order\" ADD CONSTRAINT \"FK_order_customer\"");
        sql.Should().Contain("FOREIGN KEY (\"customer_id\") REFERENCES \"customer\" (\"id\")");
        sql.Should().Contain("ON DELETE CASCADE");
        sql.Should().Contain("ON UPDATE SET DEFAULT");
    }

    /// <summary>DropForeignKey が制約名判明時に DROP CONSTRAINT IF EXISTS を生成することを検証する</summary>
    [Fact(DisplayName = "DropForeignKey は制約名があれば DROP CONSTRAINT IF EXISTS を生成する")]
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

        sql.Should()
            .Contain("ALTER TABLE \"order\" DROP CONSTRAINT IF EXISTS \"FK_order_customer\";");
    }

    /// <summary>制約名不明時に DO ブロックでカタログ逆引き削除を生成することを検証する</summary>
    [Fact(DisplayName = "DropForeignKey は制約名不明時に DO ブロックで逆引き削除する")]
    public void DropForeignKey_UsesDoBlockWhenNameUnknown()
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

        sql.Should().Contain("DO $$");
        sql.Should().Contain("pg_constraint");
        sql.Should().Contain("child.relname = 'order'");
        sql.Should().Contain("parent.relname = 'customer'");
    }

    /// <summary>SetTableDescription が COMMENT ON TABLE を生成することを検証する</summary>
    [Fact(DisplayName = "SetTableDescription は COMMENT ON TABLE を生成する")]
    public void SetTableDescription_EmitsCommentOnTable()
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

        sql.Should().Contain("COMMENT ON TABLE \"customer\" IS '顧客マスタ';");
    }

    /// <summary>SetColumnDescription が COMMENT ON COLUMN を生成することを検証する</summary>
    [Fact(DisplayName = "SetColumnDescription は COMMENT ON COLUMN を生成する")]
    public void SetColumnDescription_EmitsCommentOnColumn()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.SetColumnDescription,
                TableName = "customer",
                ColumnName = "name",
                NewDescription = "顧客名",
                IsSelected = true,
            }
        );

        sql.Should().Contain("COMMENT ON COLUMN \"customer\".\"name\" IS '顧客名';");
    }

    /// <summary>新しい説明が空の場合に COMMENT ON ... IS NULL（削除）が生成されることを検証する</summary>
    [Fact(DisplayName = "説明が空なら COMMENT ON ... IS NULL を生成する")]
    public void EmptyDescription_EmitsCommentIsNull()
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

        sql.Should().Contain("COMMENT ON TABLE \"customer\" IS NULL;");
    }

    /// <summary>説明内の単一引用符が二重化エスケープされることを検証する</summary>
    [Fact(DisplayName = "説明内の ' がエスケープされる")]
    public void Description_EscapesSingleQuote()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.SetTableDescription,
                TableName = "customer",
                NewDescription = "O'Brien の顧客",
                IsSelected = true,
            }
        );

        sql.Should().Contain("IS 'O''Brien の顧客';");
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
                DataType = "integer",
                IsPrimaryKey = true,
            }
        );
        var customer = new Entity { TableName = "customer" };
        customer.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "integer",
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
                Column = new Column { Name = "customer_id", DataType = "integer" },
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
        var iAdd = sql.IndexOf("ADD COLUMN \"customer_id\"", StringComparison.Ordinal);
        var iFk = sql.IndexOf("ADD CONSTRAINT", StringComparison.Ordinal);
        iCreate.Should().BeGreaterThan(-1);
        iAdd.Should().BeGreaterThan(iCreate);
        iFk.Should().BeGreaterThan(iAdd);
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
                DataType = "text",
                IsNullable = true,
            }
        );

        foreach (var name in pkColumns)
        {
            e.Columns.Add(
                new Column
                {
                    Name = name,
                    DataType = "integer",
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

    /// <summary>主キー変更が DO ブロックの動的 DROP と新主キーの ADD CONSTRAINT を生成することを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は DO ブロックの DROP と複合 PK の ADD を生成する")]
    public void AlterPrimaryKey_DropsExistingAndAddsComposite()
    {
        var sql = Build(AlterPk("orders", PkTarget("orders", "order_id", "line_no")));

        // 旧主キーの制約名は差分に無いため pg_constraint から逆引きして DO ブロックで外す
        sql.Should().Contain("DO $$");
        sql.Should().Contain("FROM pg_constraint con");
        sql.Should().Contain("WHERE con.contype = 'p' AND tbl.relname = 'orders'");
        sql.Should().Contain("AND tbl.relnamespace = 'public'::regnamespace;");
        sql.Should().Contain("IF pk_name IS NOT NULL THEN");
        sql.Should()
            .Contain("EXECUTE 'ALTER TABLE \"orders\" DROP CONSTRAINT \"' || pk_name || '\"';");
        // 新主キーは列定義順の複合キーとして CREATE TABLE と同じ制約名規則で付与する
        sql.Should()
            .Contain(
                "ALTER TABLE \"orders\" ADD CONSTRAINT \"PK_orders\" PRIMARY KEY (\"order_id\", \"line_no\");"
            );
    }

    /// <summary>主キーが無いテーブルへの主キー付与でも DROP が無害な形（存在時のみ実行）で出ることを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は PK 付与のみでも DROP を no-op 形で出す")]
    public void AlterPrimaryKey_AddOnly_EmitsGuardedDrop()
    {
        var sql = Build(AlterPk("customer", PkTarget("customer", "id")));

        sql.Should().Contain("IF pk_name IS NOT NULL THEN");
        sql.Should()
            .Contain(
                "ALTER TABLE \"customer\" ADD CONSTRAINT \"PK_customer\" PRIMARY KEY (\"id\");"
            );
    }

    /// <summary>主キーの解除のみ（新主キー列ゼロ）では付与文が出ないことを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は PK 解除のみなら ADD を出さない")]
    public void AlterPrimaryKey_DropOnly_OmitsAdd()
    {
        var sql = Build(AlterPk("customer", PkTarget("customer")));

        sql.Should().Contain("IF pk_name IS NOT NULL THEN");
        sql.Should().NotContain("ADD CONSTRAINT");
        sql.Should().NotContain("PRIMARY KEY (");
    }

    /// <summary>
    /// 主キー変更が 2 フェーズへ分かれ、列定義変更を挟んで「PK DROP → ALTER COLUMN → PK ADD」の順に
    /// 出力されることを検証する（PK 制約が残ったままの旧 PK 列の NULL 許容化は失敗する）。
    /// </summary>
    [Fact(DisplayName = "AlterPrimaryKey は AlterColumn を挟んで DROP → ALTER → ADD の順に出る")]
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
                DataType = "integer",
                IsNullable = true,
            },
            IsSelected = true,
        };

        var sql = Build(AlterPk("orders", PkTarget("orders", "order_id")), alterColumn);

        var drop = sql.IndexOf("DO $$", StringComparison.Ordinal);
        var alter = sql.IndexOf("ALTER COLUMN \"old_id\" TYPE integer;", StringComparison.Ordinal);
        var add = sql.IndexOf("ADD CONSTRAINT \"PK_orders\"", StringComparison.Ordinal);

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
                "ALTER TABLE \"customer\" ADD CONSTRAINT \"UQ_customer_code_kind\" UNIQUE (\"code\", \"kind\");"
            );
    }

    /// <summary>DropUniqueConstraint が live 側の実名で DROP CONSTRAINT を生成することを検証する</summary>
    [Fact(DisplayName = "DropUniqueConstraint は実名で DROP CONSTRAINT を生成する")]
    public void DropUniqueConstraint_GeneratesDropConstraint()
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
        sql.Should().Contain("ALTER TABLE \"customer\" DROP CONSTRAINT \"uq_legacy\";");
    }

    // ---------------- 動的 SQL のリテラルエスケープ ----------------

    /// <summary>
    /// 主キー解除の DO ブロックで、クォートしたテーブル名が文字列リテラル用にもエスケープされることを検証する。
    /// </summary>
    /// <remarks>
    /// <c>EXECUTE '…'</c> の中身は文字列リテラルなので、クォートだけではテーブル名の <c>'</c> がリテラルを閉じ、
    /// 以降が SQL のコードとして解釈される。<c>PgIdentifier.QuoteForDynamicSql</c> を通していれば <c>''</c> になる。
    /// </remarks>
    [Fact(DisplayName = "AlterPrimaryKey の DO ブロックはテーブル名の ' を二重化する")]
    public void AlterPrimaryKey_TableNameWithQuote_IsEscapedInDynamicSql()
    {
        var sql = Build(AlterPk("o'rders", PkTarget("o'rders", "order_id")));

        sql.Should()
            .Contain("EXECUTE 'ALTER TABLE \"o''rders\" DROP CONSTRAINT \"' || pk_name || '\"';");
        sql.Should().NotContain("EXECUTE 'ALTER TABLE \"o'rders\"");
    }

    /// <summary>制約名不明の外部キー削除でも、DO ブロックのテーブル名がリテラル用にエスケープされることを検証する</summary>
    [Fact(DisplayName = "DropForeignKey の DO ブロックはテーブル名の ' を二重化する")]
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
            .Contain("EXECUTE 'ALTER TABLE \"o''rders\" DROP CONSTRAINT \"' || fk_name || '\"';");
        sql.Should().NotContain("EXECUTE 'ALTER TABLE \"o'rders\"");
    }

    // ---------------- DO ブロックのドルクォートタグ ----------------

    /// <summary>
    /// 主キー解除の DO ブロックが、テーブル名に <c>$$</c> が含まれていても途中で終端しないことを検証する。
    /// </summary>
    /// <remarks>
    /// ドルクォートは<b>タグ文字列そのもの</b>で終端する。タグを <c>$$</c> 固定にすると、本文へ埋めた名前の
    /// <c>$$</c> がブロックを閉じ、以降が最上位の SQL 文として解釈される（リテラルエスケープでは防げない＝
    /// <c>'</c> の二重化はドルクォートの終端と無関係）。タグは本文と衝突しないものを決定的に選ぶべき。
    /// </remarks>
    [Fact(DisplayName = "AlterPrimaryKey の DO ブロックは $$ 入りテーブル名でも終端されない")]
    public void AlterPrimaryKey_TableNameWithDollarTag_DoesNotTerminateBlock()
    {
        var sql = Build(AlterPk("o$$rders", PkTarget("o$$rders")));

        AssertDollarQuotedBlockIsIntact(sql);
    }

    /// <summary>制約名不明の外部キー削除でも、DO ブロックが <c>$$</c> 入りテーブル名で終端しないことを検証する</summary>
    [Fact(DisplayName = "DropForeignKey の DO ブロックは $$ 入りテーブル名でも終端されない")]
    public void DropForeignKey_TableNameWithDollarTag_DoesNotTerminateBlock()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "o$$rders",
                ParentEntity = new Entity { TableName = "customer" },
                ChildEntity = new Entity { TableName = "o$$rders" },
                IsSelected = true,
            }
        );

        AssertDollarQuotedBlockIsIntact(sql);
    }

    /// <summary>衝突しないタグは既定の <c>$$</c> のままであること（通常の図の出力を変えない）</summary>
    [Fact(DisplayName = "DO ブロックのタグは衝突が無ければ $$ のまま")]
    public void DoBlock_WithoutCollision_KeepsDefaultTag()
    {
        var sql = Build(AlterPk("orders", PkTarget("orders")));

        sql.Should().Contain("DO $$");
        sql.Should().Contain("END $$;");
    }

    /// <summary>
    /// 生成された <c>DO</c> ブロックの本文にドルクォートタグが現れない（＝ブロックが途中で閉じない）ことを検証する。
    /// </summary>
    private static void AssertDollarQuotedBlockIsIntact(string sql)
    {
        var lines = sql.Replace("\r\n", "\n").Split('\n');
        var openIndex = Array.FindIndex(
            lines,
            line => line.StartsWith("DO $", StringComparison.Ordinal)
        );

        openIndex.Should().BeGreaterThanOrEqualTo(0, "DO ブロックが生成されること");

        var tag = lines[openIndex]["DO ".Length..].Trim();
        tag.Should().MatchRegex(@"^\$[A-Za-z0-9_]*\$$", "ドルクォートタグとして妥当な形であること");

        var closeIndex = Array.FindIndex(
            lines,
            openIndex + 1,
            line => line.Trim() == $"END {tag};"
        );

        closeIndex.Should().BeGreaterThan(openIndex, "同じタグで閉じられていること");

        string.Join("\n", lines[(openIndex + 1)..closeIndex])
            .Should()
            .NotContain(
                tag,
                "本文にタグが現れるとブロックがそこで終端し、以降が実行される SQL になる"
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
                DataType = "integer",
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
        sql.Should().Contain("CONSTRAINT \"PK_shops\" PRIMARY KEY (\"id\"),");
        sql.Should().Contain("CONSTRAINT \"UQ_shops_code\" UNIQUE (\"code\")");
        // 最後の制約行に余分なカンマは付かない
        sql.Should().NotContain("UNIQUE (\"code\"),");
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
            .Contain("CONSTRAINT \"UQ_shops_region_code\" UNIQUE (\"region\", \"code\")");
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

        var ddl = new PostgreSqlDdlGenerator().Build(new ErDiagram { Entities = { entity } });
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

    /// <summary>主キーの順序上書きが付与文の列順へ反映されることを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は PrimaryKeyColumnIds の順で複合 PK を付与する")]
    public void AlterPrimaryKey_FollowsPrimaryKeyColumnIds()
    {
        // 列宣言順は (order_id, line_no)。主キーの実効順だけを逆に指定する
        var target = PkTarget("orders", "order_id", "line_no");
        target.PrimaryKeyColumnIds =
        [
            target.Columns.First(c => c.Name == "line_no").Id,
            target.Columns.First(c => c.Name == "order_id").Id,
        ];

        var sql = Build(AlterPk("orders", target));

        sql.Should()
            .Contain(
                "ALTER TABLE \"orders\" ADD CONSTRAINT \"PK_orders\" PRIMARY KEY (\"line_no\", \"order_id\");"
            );
    }
}
