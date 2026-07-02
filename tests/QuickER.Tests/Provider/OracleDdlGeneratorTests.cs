using FluentAssertions;
using QuickER.Model;
using QuickER.Oracle;

namespace QuickER.Tests.Provider;

/// <summary><see cref="OracleDdlGenerator"/> の DDL 生成（CREATE TABLE・複合 PK・FK・ON UPDATE 非出力・識別子クォート）を検証するテストクラス</summary>
public class OracleDdlGeneratorTests
{
    /// <summary>CREATE TABLE と PRIMARY KEY 制約が出力されることを検証する</summary>
    [Fact(DisplayName = "Build: CREATE TABLE と PRIMARY KEY が出力される")]
    public void Build_EmitsCreateTableAndPk()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "users",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "NUMBER(10)",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "name",
                            DataType = "VARCHAR2(50)",
                            IsNullable = true,
                        },
                    },
                },
            ],
        };

        var sql = new OracleDdlGenerator().Build(diagram);

        sql.Should().Contain("CREATE TABLE \"users\"");
        sql.Should().Contain("\"id\" NUMBER(10) NOT NULL");
        sql.Should().Contain("CONSTRAINT \"PK_users\" PRIMARY KEY (\"id\")");
        sql.Should().Contain("\"name\" VARCHAR2(50) NULL");
    }

    /// <summary>複合主キーが 1 つの PRIMARY KEY 制約へまとめて出力されることを検証する</summary>
    [Fact(DisplayName = "Build: 複合 PK は列を並べた PRIMARY KEY を生成する")]
    public void Build_CompositePrimaryKey_EmitsCombinedConstraint()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "order_items",
                    Columns =
                    {
                        new Column
                        {
                            Name = "order_id",
                            DataType = "NUMBER(10)",
                            IsPrimaryKey = true,
                        },
                        new Column
                        {
                            Name = "line_no",
                            DataType = "NUMBER(10)",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new OracleDdlGenerator().Build(diagram);

        sql.Should()
            .Contain("CONSTRAINT \"PK_order_items\" PRIMARY KEY (\"order_id\", \"line_no\")");
    }

    /// <summary>1 対多リレーションから FK 制約と ON DELETE 句が生成され、ON UPDATE が出力されないことを検証する</summary>
    [Fact(DisplayName = "Build: 1対多は FOREIGN KEY と ON DELETE を生成し ON UPDATE は出さない")]
    public void Build_OneToMany_EmitsForeignKey_NoOnUpdate()
    {
        var parent = new Entity
        {
            TableName = "parent",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "NUMBER(10)",
                    IsPrimaryKey = true,
                },
            },
        };
        var child = new Entity
        {
            TableName = "child",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "NUMBER(10)",
                    IsPrimaryKey = true,
                },
                new Column { Name = "parent_id", DataType = "NUMBER(10)" },
            },
        };
        var diagram = new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    SourceColumnId = parent.Columns[0].Id,
                    TargetColumnId = child.Columns[1].Id,
                    ConstraintName = "FK_child_parent",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.SetNull, // Oracle では無視される
                },
            ],
        };

        var sql = new OracleDdlGenerator().Build(diagram);

        sql.Should().Contain("ALTER TABLE \"child\" ADD CONSTRAINT \"FK_child_parent\"");
        sql.Should().Contain("FOREIGN KEY (\"parent_id\") REFERENCES \"parent\" (\"id\")");
        sql.Should().Contain("ON DELETE CASCADE");
        // 注意コメントには "ON UPDATE" が含まれるが、SQL の句としては出力しない
        sql.Should().NotContain("ON DELETE CASCADE ON UPDATE");
        sql.Should().Contain("-- 注: Oracle は ON UPDATE をサポートしないため無視");
    }

    /// <summary>ON DELETE SET NULL が句として出力されることを検証する</summary>
    [Fact(DisplayName = "Build: ON DELETE SET NULL が出力される")]
    public void Build_OnDeleteSetNull_EmitsClause()
    {
        var parent = new Entity
        {
            TableName = "parent",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "NUMBER(10)",
                    IsPrimaryKey = true,
                },
            },
        };
        var child = new Entity
        {
            TableName = "child",
            Columns = { new Column { Name = "parent_id", DataType = "NUMBER(10)" } },
        };
        var diagram = new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    SourceColumnId = parent.Columns[0].Id,
                    TargetColumnId = child.Columns[0].Id,
                    OnDelete = ForeignKeyReferentialAction.SetNull,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
            ],
        };

        var sql = new OracleDdlGenerator().Build(diagram);

        sql.Should().Contain("ON DELETE SET NULL");
        sql.Should().NotContain("Oracle は ON UPDATE をサポートしない");
    }

    /// <summary>ON DELETE NO ACTION は句を省略することを検証する（既定）</summary>
    [Fact(DisplayName = "Build: ON DELETE NO ACTION は句を省略する")]
    public void Build_OnDeleteNoAction_OmitsClause()
    {
        var parent = new Entity
        {
            TableName = "parent",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "NUMBER(10)",
                    IsPrimaryKey = true,
                },
            },
        };
        var child = new Entity
        {
            TableName = "child",
            Columns = { new Column { Name = "parent_id", DataType = "NUMBER(10)" } },
        };
        var diagram = new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    SourceColumnId = parent.Columns[0].Id,
                    TargetColumnId = child.Columns[0].Id,
                    OnDelete = ForeignKeyReferentialAction.NoAction,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
            ],
        };

        var sql = new OracleDdlGenerator().Build(diagram);

        sql.Should().Contain("ADD CONSTRAINT");
        sql.Should().NotContain("ON DELETE");
    }

    /// <summary>日本語テーブル名・列名が二重引用符でクォートされることを検証する</summary>
    [Fact(DisplayName = "Build: 日本語テーブル名・列名がクォートされる")]
    public void Build_JapaneseIdentifiers_AreQuoted()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "顧客",
                    Columns =
                    {
                        new Column
                        {
                            Name = "顧客ID",
                            DataType = "NUMBER(10)",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new OracleDdlGenerator().Build(diagram);

        sql.Should().Contain("CREATE TABLE \"顧客\"");
        sql.Should().Contain("\"顧客ID\" NUMBER(10) NOT NULL");
        sql.Should().Contain("PRIMARY KEY (\"顧客ID\")");
    }

    /// <summary>識別子に含まれる二重引用符が二重化エスケープされることを検証する</summary>
    [Fact(DisplayName = "Build: 識別子に含まれる \" がエスケープされる")]
    public void Build_IdentifierContainingQuote_IsEscaped()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "wei\"rd",
                    Columns =
                    {
                        new Column
                        {
                            Name = "co\"l",
                            DataType = "NUMBER(10)",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new OracleDdlGenerator().Build(diagram);

        sql.Should().Contain("CREATE TABLE \"wei\"\"rd\"");
        sql.Should().Contain("\"co\"\"l\" NUMBER(10) NOT NULL");
    }
}
