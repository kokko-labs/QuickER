using QuickER.Model;
using QuickER.Services;
using FluentAssertions;

using QuickER.SqlServer;

namespace QuickER.Tests.Services;

/// <summary><see cref="SchemaSyncScriptBuilder"/> が差分から生成する T-SQL の内容と出力順序を検証するテストクラス</summary>
public class SchemaSyncScriptBuilderTests
{
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

        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
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

        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
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

        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
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

        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
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

        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
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
            SourceColumnId = customer.Columns[0].Id,
            TargetColumnId = order.Columns[2].Id,
        };

        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "Order",
            ColumnName = "CustomerRef",
            ParentEntity = customer,
            ChildEntity = order,
            Relationship = rel,
            IsSelected = true,
        };

        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
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
            SourceColumnId = customer.Columns[0].Id,
            TargetColumnId = order.Columns[1].Id,
            ConstraintName = "FK_Order_Customer_Custom",
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.SetDefault,
        };

        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "Order",
            ColumnName = "CustomerId",
            ParentEntity = customer,
            ChildEntity = order,
            Relationship = rel,
            IsSelected = true,
        };

        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
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

        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
        sql.Should().Contain("FK_Order_CustomerRef");
        sql.Should().Contain("DROP CONSTRAINT [FK_Order_CustomerRef]");
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

        var sql = SchemaSyncScriptBuilder.Build(items);
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

        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
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

        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
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

        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
        sql.Should().Contain("sp_dropextendedproperty");
        sql.Should().NotContain("sp_addextendedproperty");
    }
}
