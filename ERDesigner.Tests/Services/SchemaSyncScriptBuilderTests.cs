using ERDesigner.Models;
using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="SchemaSyncScriptBuilder"/> の生成 SQL のテスト。
/// </summary>
public class SchemaSyncScriptBuilderTests
{
    [Fact(DisplayName = "AddTable は CREATE TABLE と PK を含む")]
    public void AddTable_GeneratesCreate()
    {
        var e = new Entity { TableName = "Customer" };
        e.Columns.Add(new Column { Name = "Id", DataType = "int", IsPrimaryKey = true });
        e.Columns.Add(new Column { Name = "Name", DataType = "nvarchar(50)" });
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddTable,
            TableName = "Customer",
            Entity = e,
            IsSelected = true
        };
        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
        sql.Should().Contain("CREATE TABLE [Customer]");
        sql.Should().Contain("[Id] int NOT NULL");
        sql.Should().Contain("PRIMARY KEY ([Id])");
        sql.Should().Contain("GO");
    }

    [Fact(DisplayName = "AddColumn は ALTER TABLE ADD を生成する")]
    public void AddColumn_GeneratesAlterAdd()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddColumn,
            TableName = "Customer",
            ColumnName = "Email",
            Column = new Column { Name = "Email", DataType = "nvarchar(200)" },
            IsSelected = true
        };
        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
        sql.Should().Contain("ALTER TABLE [Customer] ADD [Email] nvarchar(200) NULL;");
    }

    [Fact(DisplayName = "AlterColumn は ALTER COLUMN を生成する")]
    public void AlterColumn_GeneratesAlterColumn()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "Customer",
            ColumnName = "Name",
            Column = new Column { Name = "Name", DataType = "nvarchar(100)" },
            IsSelected = true
        };
        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
        sql.Should().Contain("ALTER TABLE [Customer] ALTER COLUMN [Name] nvarchar(100) NULL;");
    }

    [Fact(DisplayName = "DropColumn は DROP COLUMN を生成する")]
    public void DropColumn_GeneratesDropColumn()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropColumn,
            TableName = "Customer",
            ColumnName = "Old",
            IsSelected = true
        };
        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
        sql.Should().Contain("ALTER TABLE [Customer] DROP COLUMN [Old];");
    }

    [Fact(DisplayName = "選択されていない項目はスクリプトに含まれない")]
    public void Unselected_Excluded()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddColumn,
            TableName = "Customer",
            ColumnName = "Email",
            Column = new Column { Name = "Email", DataType = "nvarchar(200)" },
            IsSelected = false
        };
        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
        sql.Should().NotContain("Email");
    }

    [Fact(DisplayName = "AddForeignKey は ALTER ADD CONSTRAINT FOREIGN KEY を生成する")]
    public void AddFk_GeneratesConstraint()
    {
        var customer = new Entity { TableName = "Customer" };
        customer.Columns.Add(new Column { Name = "Id", DataType = "int", IsPrimaryKey = true });
        var order = new Entity { TableName = "Order" };
        order.Columns.Add(new Column { Name = "Id", DataType = "int", IsPrimaryKey = true });
        order.Columns.Add(new Column { Name = "Customer_Id", DataType = "int" });

        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "Order",
            ColumnName = "Customer_Id",
            ParentEntity = customer,
            ChildEntity = order,
            IsSelected = true
        };
        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
        sql.Should().Contain("ALTER TABLE [Order] ADD CONSTRAINT [FK_Order_Customer]");
        sql.Should().Contain("FOREIGN KEY ([Customer_Id]) REFERENCES [Customer] ([Id])");
    }

    [Fact(DisplayName = "実行順序: AddTable → AddColumn → AddForeignKey")]
    public void Order_AddTable_Then_AddColumn_Then_Fk()
    {
        var e = new Entity { TableName = "T" };
        e.Columns.Add(new Column { Name = "Id", DataType = "int", IsPrimaryKey = true });
        var customer = new Entity { TableName = "Customer" };
        customer.Columns.Add(new Column { Name = "Id", DataType = "int", IsPrimaryKey = true });

        var items = new[]
        {
            new SchemaDiffItem { Kind = SchemaDiffKind.AddForeignKey, TableName = "T", ColumnName = "Customer_Id",
                ParentEntity = customer, ChildEntity = e, IsSelected = true },
            new SchemaDiffItem { Kind = SchemaDiffKind.AddColumn, TableName = "T", ColumnName = "Customer_Id",
                Column = new Column { Name = "Customer_Id", DataType = "int" }, IsSelected = true },
            new SchemaDiffItem { Kind = SchemaDiffKind.AddTable, TableName = "T", Entity = e, IsSelected = true },
        };
        var sql = SchemaSyncScriptBuilder.Build(items);
        var iCreate = sql.IndexOf("CREATE TABLE");
        var iAdd = sql.IndexOf("ADD [Customer_Id]");
        var iFk = sql.IndexOf("ADD CONSTRAINT");
        iCreate.Should().BeGreaterThan(-1);
        iAdd.Should().BeGreaterThan(iCreate);
        iFk.Should().BeGreaterThan(iAdd);
    }

    [Fact(DisplayName = "SetTableDescription は sp_addextendedproperty / sp_updateextendedproperty を出力する")]
    public void SetTableDescription_EmitsExtendedProperty()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.SetTableDescription,
            TableName = "Customer",
            NewDescription = "顧客マスタ",
            OldDescription = null,
            IsSelected = true
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
            IsSelected = true
        };
        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
        sql.Should().Contain("@level2type=N'COLUMN'");
        sql.Should().Contain("@level2name=N'Name'");
        sql.Should().Contain("N'顧客名'");
    }

    [Fact(DisplayName = "新値が空ならば sp_dropextendedproperty が出力される")]
    public void EmptyNewDescription_EmitsDrop()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.SetTableDescription,
            TableName = "Customer",
            NewDescription = "",
            OldDescription = "古い",
            IsSelected = true
        };
        var sql = SchemaSyncScriptBuilder.Build(new[] { item });
        sql.Should().Contain("sp_dropextendedproperty");
        sql.Should().NotContain("sp_addextendedproperty");
    }
}
