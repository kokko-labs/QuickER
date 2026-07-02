using FluentAssertions;
using QuickER.Model;
using QuickER.PostgreSql;

namespace QuickER.Tests.Provider;

/// <summary><see cref="PostgreSqlDdlGenerator"/> の DDL 生成（CREATE TABLE・複合 PK・FK・識別子クォート）を検証するテストクラス</summary>
public class PostgreSqlDdlGeneratorTests
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
                            DataType = "integer",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "name",
                            DataType = "varchar(50)",
                            IsNullable = true,
                        },
                    },
                },
            ],
        };

        var sql = new PostgreSqlDdlGenerator().Build(diagram);

        sql.Should().Contain("CREATE TABLE \"users\"");
        sql.Should().Contain("\"id\" integer NOT NULL");
        sql.Should().Contain("CONSTRAINT \"PK_users\" PRIMARY KEY (\"id\")");
        sql.Should().Contain("\"name\" varchar(50) NULL");
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
                            DataType = "integer",
                            IsPrimaryKey = true,
                        },
                        new Column
                        {
                            Name = "line_no",
                            DataType = "integer",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new PostgreSqlDdlGenerator().Build(diagram);

        sql.Should()
            .Contain("CONSTRAINT \"PK_order_items\" PRIMARY KEY (\"order_id\", \"line_no\")");
    }

    /// <summary>1 対多リレーションから FK 制約と参照アクションが生成されることを検証する</summary>
    [Fact(DisplayName = "Build: 1対多リレーションが FOREIGN KEY と参照アクションを生成する")]
    public void Build_OneToMany_EmitsForeignKeyWithActions()
    {
        var parent = new Entity
        {
            TableName = "parent",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "integer",
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
                    DataType = "integer",
                    IsPrimaryKey = true,
                },
                new Column { Name = "parent_id", DataType = "integer" },
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
                    OnUpdate = ForeignKeyReferentialAction.SetNull,
                },
            ],
        };

        var sql = new PostgreSqlDdlGenerator().Build(diagram);

        sql.Should().Contain("ALTER TABLE \"child\" ADD CONSTRAINT \"FK_child_parent\"");
        sql.Should().Contain("FOREIGN KEY (\"parent_id\") REFERENCES \"parent\" (\"id\")");
        sql.Should().Contain("ON DELETE CASCADE");
        sql.Should().Contain("ON UPDATE SET NULL");
    }

    /// <summary>schema.table 名が "schema"."table" へ分割クォートされ、PK 制約名が安全化されることを検証する</summary>
    [Fact(DisplayName = "Build: schema.table 形式は \"schema\".\"table\" へ分割される")]
    public void Build_SchemaQualifiedTableName_SplitsQuotes()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "public.users",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "integer",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new PostgreSqlDdlGenerator().Build(diagram);

        sql.Should().Contain("CREATE TABLE \"public\".\"users\"");
        sql.Should().Contain("CONSTRAINT \"PK_public_users\" PRIMARY KEY (\"id\")");
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
                            DataType = "integer",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new PostgreSqlDdlGenerator().Build(diagram);

        sql.Should().Contain("CREATE TABLE \"顧客\"");
        sql.Should().Contain("\"顧客ID\" integer NOT NULL");
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
                            DataType = "integer",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new PostgreSqlDdlGenerator().Build(diagram);

        sql.Should().Contain("CREATE TABLE \"wei\"\"rd\"");
        sql.Should().Contain("\"co\"\"l\" integer NOT NULL");
    }
}
