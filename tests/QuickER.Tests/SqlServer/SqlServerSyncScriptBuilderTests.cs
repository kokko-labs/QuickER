using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Services;
using QuickER.SqlServer;

namespace QuickER.Tests.SqlServer;

/// <summary><see cref="SqlServerSyncScriptBuilder"/> が差分から生成する T-SQL の内容と出力順序を検証するテストクラス</summary>
public class SqlServerSyncScriptBuilderTests
{
    /// <summary>差分項目からプランナー経由で同期スクリプトを生成する（新 API へのアダプタ）</summary>
    private static string BuildScript(ISyncScriptBuilder builder, params SchemaDiffItem[] items) =>
        builder.Build(new SyncPlanner().BuildPlan(items, new SyncDialectCapabilities()));

    /// <summary>AddTable が主キー制約を含む CREATE TABLE 文を生成することを検証する</summary>
    [Fact(DisplayName = "AddTable は CREATE TABLE と PK を含む")]
    public void AddTable_GeneratesCreate()
    {
        var e = new Entity { TableName = "Customer" };
        e.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        e.Columns.Add(
            new Column
            {
                Name = "Name",
                DataType = "nvarchar(50)",
                IsNullable = true,
            }
        );
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddTable,
            TableName = "Customer",
            Entity = e,
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        sql.Should().Contain("CREATE TABLE [Customer]");
        sql.Should().Contain("[Id] int NOT NULL");
        sql.Should().Contain("PRIMARY KEY ([Id])");
        sql.Should().Contain("GO");
    }

    /// <summary>AddColumn が ALTER TABLE ... ADD 文を生成することを検証する</summary>
    [Fact(DisplayName = "AddColumn は ALTER TABLE ADD を生成する")]
    public void AddColumn_GeneratesAlterAdd()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddColumn,
            TableName = "Customer",
            ColumnName = "Email",
            Column = new Column
            {
                Name = "Email",
                DataType = "nvarchar(200)",
                IsNullable = false,
            },
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        sql.Should().Contain("ALTER TABLE [Customer] ADD [Email] nvarchar(200) NOT NULL;");
    }

    /// <summary>AlterColumn が ALTER TABLE ... ALTER COLUMN 文を生成することを検証する</summary>
    [Fact(DisplayName = "AlterColumn は ALTER COLUMN を生成する")]
    public void AlterColumn_GeneratesAlterColumn()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "Customer",
            ColumnName = "Name",
            Column = new Column
            {
                Name = "Name",
                DataType = "nvarchar(100)",
                IsNullable = false,
            },
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        sql.Should().Contain("ALTER TABLE [Customer] ALTER COLUMN [Name] nvarchar(100) NOT NULL;");
    }

    /// <summary>DropColumn が ALTER TABLE ... DROP COLUMN 文を生成することを検証する</summary>
    [Fact(DisplayName = "DropColumn は DROP COLUMN を生成する")]
    public void DropColumn_GeneratesDropColumn()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropColumn,
            TableName = "Customer",
            ColumnName = "Old",
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        sql.Should().Contain("ALTER TABLE [Customer] DROP COLUMN [Old];");
    }

    /// <summary>未選択の差分項目がスクリプトへ出力されないことを検証する</summary>
    [Fact(DisplayName = "選択されていない項目はスクリプトに含まれない")]
    public void Unselected_Excluded()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddColumn,
            TableName = "Customer",
            ColumnName = "Email",
            Column = new Column { Name = "Email", DataType = "nvarchar(200)" },
            IsSelected = false,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        sql.Should().NotContain("Email");
    }

    /// <summary>AddForeignKey が FK 制約追加文を生成し、参照列・参照先を解決することを検証する</summary>
    [Fact(DisplayName = "AddForeignKey は ALTER ADD CONSTRAINT FOREIGN KEY を生成する")]
    public void AddFk_GeneratesConstraint()
    {
        var customer = new Entity { TableName = "Customer" };
        customer.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        var order = new Entity { TableName = "Order" };
        order.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        order.Columns.Add(new Column { Name = "Customer_Id", DataType = "int" });
        order.Columns.Add(new Column { Name = "CustomerRef", DataType = "int" });
        var rel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(customer.Columns[0].Id, order.Columns[2].Id)],
        };

        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "Order",
            ColumnName = "CustomerRef",
            ForeignKeyColumnPairs = [new("Id", "CustomerRef")],
            ParentEntity = customer,
            ChildEntity = order,
            Relationship = rel,
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        sql.Should().Contain("ALTER TABLE [Order] ADD CONSTRAINT [FK_Order_Customer]");
        sql.Should().Contain("FOREIGN KEY ([CustomerRef]) REFERENCES [Customer] ([Id])");
    }

    /// <summary>AddForeignKey が指定の制約名と ON DELETE/UPDATE 参照アクションを反映することを検証する</summary>
    [Fact(DisplayName = "AddForeignKey は制約名と参照アクションを生成する")]
    public void AddFk_GeneratesConstraintNameAndReferentialActions()
    {
        var customer = new Entity { TableName = "Customer" };
        customer.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        var order = new Entity { TableName = "Order" };
        order.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        order.Columns.Add(new Column { Name = "CustomerId", DataType = "int" });
        var rel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(customer.Columns[0].Id, order.Columns[1].Id)],
            ConstraintName = "FK_Order_Customer_Custom",
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.SetDefault,
        };

        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "Order",
            ColumnName = "CustomerId",
            ForeignKeyColumnPairs = [new("Id", "CustomerId")],
            ParentEntity = customer,
            ChildEntity = order,
            Relationship = rel,
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        sql.Should().Contain("CONSTRAINT [FK_Order_Customer_Custom]");
        sql.Should().Contain("ON DELETE CASCADE");
        sql.Should().Contain("ON UPDATE SET DEFAULT");
    }

    /// <summary>DropForeignKey が制約名判明時に存在チェック付きで直接 DROP することを検証する</summary>
    [Fact(DisplayName = "DropForeignKey は制約名があればその名前で削除する")]
    public void DropFk_UsesConstraintNameWhenAvailable()
    {
        var customer = new Entity { TableName = "Customer" };
        var order = new Entity { TableName = "Order" };
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = "Order",
            ParentEntity = customer,
            ChildEntity = order,
            ForeignKeyName = "FK_Order_CustomerRef",
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        sql.Should().Contain("FK_Order_CustomerRef");
        sql.Should().Contain("DROP CONSTRAINT [FK_Order_CustomerRef]");
    }

    /// <summary>
    /// 同一列の DropForeignKey と AlterColumn を両方選択したとき、DROP CONSTRAINT が ALTER COLUMN より
    /// 先に出力されることを検証する（FK が張られたままの列は型変更できない＝SQL Server の Msg 5074）
    /// </summary>
    [Fact(DisplayName = "実行順序: 同一列の DropForeignKey → AlterColumn")]
    public void Order_DropFk_Then_AlterColumn_OnSameColumn()
    {
        var customer = new Entity { TableName = "Customer" };
        var order = new Entity { TableName = "Order" };

        var items = new[]
        {
            // 入力は「型変更が先」の並びにして、プランナーが順序を入れ替えることを確かめる
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = "Order",
                ColumnName = "CustomerId",
                Column = new Column
                {
                    Name = "CustomerId",
                    DataType = "bigint",
                    IsNullable = false,
                },
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "Order",
                ColumnName = "CustomerId",
                ParentEntity = customer,
                ChildEntity = order,
                ForeignKeyName = "FK_Order_CustomerRef",
                IsSelected = true,
            },
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), items);
        var iDropFk = sql.IndexOf("DROP CONSTRAINT [FK_Order_CustomerRef]");
        var iAlter = sql.IndexOf("ALTER COLUMN [CustomerId]");
        iDropFk.Should().BeGreaterThan(-1);
        iAlter.Should().BeGreaterThan(iDropFk);
    }

    /// <summary>依存関係を満たすよう CREATE → ADD COLUMN → ADD CONSTRAINT の順で出力されることを検証する</summary>
    [Fact(DisplayName = "実行順序: AddTable → AddColumn → AddForeignKey")]
    public void Order_AddTable_Then_AddColumn_Then_Fk()
    {
        var e = new Entity { TableName = "T" };
        e.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        var customer = new Entity { TableName = "Customer" };
        customer.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );

        var items = new[]
        {
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddForeignKey,
                TableName = "T",
                ColumnName = "Customer_Id",
                ForeignKeyColumnPairs = [new("Id", "Customer_Id")],
                ParentEntity = customer,
                ChildEntity = e,
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "T",
                ColumnName = "Customer_Id",
                Column = new Column { Name = "Customer_Id", DataType = "int" },
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "T",
                Entity = e,
                IsSelected = true,
            },
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), items);
        var iCreate = sql.IndexOf("CREATE TABLE");
        var iAdd = sql.IndexOf("ADD [Customer_Id]");
        var iFk = sql.IndexOf("ADD CONSTRAINT");
        iCreate.Should().BeGreaterThan(-1);
        iAdd.Should().BeGreaterThan(iCreate);
        iFk.Should().BeGreaterThan(iAdd);
    }

    /// <summary>SetTableDescription がテーブルレベルの MS_Description 設定文（add/update 切替）を出力することを検証する</summary>
    [Fact(
        DisplayName = "SetTableDescription は sp_addextendedproperty / sp_updateextendedproperty を出力する"
    )]
    public void SetTableDescription_EmitsExtendedProperty()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.SetTableDescription,
            TableName = "Customer",
            NewDescription = "顧客マスタ",
            OldDescription = null,
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        sql.Should().Contain("sp_addextendedproperty");
        sql.Should().Contain("sp_updateextendedproperty");
        sql.Should().Contain("MS_Description");
        sql.Should().Contain("@level1type=N'TABLE'");
        sql.Should().Contain("@level1name=N'Customer'");
        sql.Should().Contain("N'顧客マスタ'");
        sql.Should().NotContain("@level2type");
    }

    /// <summary>SetColumnDescription が列レベル（@level2type=COLUMN）の説明設定文を出力することを検証する</summary>
    [Fact(DisplayName = "SetColumnDescription は @level2type=COLUMN を含む")]
    public void SetColumnDescription_EmitsColumnLevel()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.SetColumnDescription,
            TableName = "Customer",
            ColumnName = "Name",
            NewDescription = "顧客名",
            OldDescription = "旧",
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        sql.Should().Contain("@level2type=N'COLUMN'");
        sql.Should().Contain("@level2name=N'Name'");
        sql.Should().Contain("N'顧客名'");
    }

    /// <summary>新しい説明が空の場合に説明削除（sp_dropextendedproperty）が出力されることを検証する</summary>
    [Fact(DisplayName = "新値が空ならば sp_dropextendedproperty が出力される")]
    public void EmptyNewDescription_EmitsDrop()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.SetTableDescription,
            TableName = "Customer",
            NewDescription = "",
            OldDescription = "古い",
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        sql.Should().Contain("sp_dropextendedproperty");
        sql.Should().NotContain("sp_addextendedproperty");
    }

    // ---------------- 主キー変更（AlterPrimaryKey） ----------------

    /// <summary>指定の主キー列を持つ target エンティティを組み立てる</summary>
    private static Entity PkTarget(string table, params string[] pkColumns)
    {
        var e = new Entity { TableName = table };
        e.Columns.Add(
            new Column
            {
                Name = "Memo",
                DataType = "nvarchar(50)",
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

    /// <summary>主キー変更が旧主キーの動的 DROP と新主キーの ADD CONSTRAINT を生成することを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は旧 PK の動的 DROP と複合 PK の ADD を生成する")]
    public void AlterPrimaryKey_DropsExistingAndAddsComposite()
    {
        var sql = BuildScript(
            new SqlServerSyncScriptBuilder(),
            new[] { AlterPk("Order", PkTarget("Order", "OrderId", "LineNo")) }
        );

        // 旧主キーの制約名は差分に無いため sys.key_constraints から逆引きして動的 SQL で外す
        sql.Should().Contain("DECLARE @pk sysname;");
        sql.Should().Contain("FROM sys.key_constraints kc");
        sql.Should().Contain("WHERE kc.type = 'PK' AND t.name = N'Order';");
        sql.Should()
            .Contain(
                "IF @pk IS NOT NULL EXEC('ALTER TABLE [Order] DROP CONSTRAINT [' + @pk + ']');"
            );
        // 新主キーは列定義順の複合キーとして CREATE TABLE と同じ制約名規則で付与する
        sql.Should()
            .Contain(
                "ALTER TABLE [Order] ADD CONSTRAINT [PK_Order] PRIMARY KEY ([OrderId], [LineNo]);"
            );
    }

    /// <summary>主キーが無いテーブルへの主キー付与でも DROP が無害な形（存在時のみ実行）で出ることを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は PK 付与のみでも DROP を no-op 形で出す")]
    public void AlterPrimaryKey_AddOnly_EmitsGuardedDrop()
    {
        var sql = BuildScript(
            new SqlServerSyncScriptBuilder(),
            new[] { AlterPk("Customer", PkTarget("Customer", "Id")) }
        );

        sql.Should().Contain("IF @pk IS NOT NULL EXEC(");
        sql.Should()
            .Contain("ALTER TABLE [Customer] ADD CONSTRAINT [PK_Customer] PRIMARY KEY ([Id]);");
    }

    /// <summary>主キーの解除のみ（新主キー列ゼロ）では付与文が出ないことを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は PK 解除のみなら ADD を出さない")]
    public void AlterPrimaryKey_DropOnly_OmitsAdd()
    {
        var sql = BuildScript(
            new SqlServerSyncScriptBuilder(),
            new[] { AlterPk("Customer", PkTarget("Customer")) }
        );

        sql.Should().Contain("IF @pk IS NOT NULL EXEC(");
        sql.Should().NotContain("ADD CONSTRAINT");
        sql.Should().NotContain("PRIMARY KEY (");
    }

    /// <summary>
    /// 主キー変更が 2 フェーズへ分かれ、列定義変更を挟んで「PK DROP → ALTER COLUMN → PK ADD」の順に
    /// 出力されることを検証する（PK 制約が残ったままの ALTER COLUMN は Msg 5074 → 4922 で失敗する）。
    /// </summary>
    [Fact(DisplayName = "AlterPrimaryKey は AlterColumn を挟んで DROP → ALTER → ADD の順に出る")]
    public void AlterPrimaryKey_SplitsAroundAlterColumn()
    {
        var alterColumn = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "Order",
            ColumnName = "OldId",
            Column = new Column
            {
                Name = "OldId",
                DataType = "int",
                IsNullable = true,
            },
            IsSelected = true,
        };

        var sql = BuildScript(
            new SqlServerSyncScriptBuilder(),
            AlterPk("Order", PkTarget("Order", "OrderId")),
            alterColumn
        );

        var drop = sql.IndexOf("DECLARE @pk sysname;", StringComparison.Ordinal);
        var alter = sql.IndexOf("ALTER COLUMN [OldId]", StringComparison.Ordinal);
        var add = sql.IndexOf("ADD CONSTRAINT [PK_Order]", StringComparison.Ordinal);

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
            TableName = "Customer",
            UniqueConstraintColumns = ["Code", "Kind"],
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        // 制約名は未設定のため UQ_{テーブル}_{列…} が合成される
        sql.Should()
            .Contain(
                "ALTER TABLE [Customer] ADD CONSTRAINT [UQ_Customer_Code_Kind] UNIQUE ([Code], [Kind]);"
            );
    }

    /// <summary>DropUniqueConstraint が live 側の実名で DROP CONSTRAINT を生成することを検証する</summary>
    [Fact(DisplayName = "DropUniqueConstraint は実名で DROP CONSTRAINT を生成する")]
    public void DropUniqueConstraint_GeneratesDropConstraint()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropUniqueConstraint,
            TableName = "Customer",
            UniqueConstraintName = "UQ_Legacy",
            UniqueConstraintColumns = ["Code"],
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        sql.Should().Contain("ALTER TABLE [Customer] DROP CONSTRAINT [UQ_Legacy];");
    }
}
