using FluentAssertions;
using QuickER.Model;
using QuickER.MySql;

namespace QuickER.Tests.MySql;

/// <summary><see cref="MySqlDdlGenerator"/> の DDL 生成（CREATE TABLE・複合 PK・FK・識別子クォート）を検証するテストクラス</summary>
public class MySqlDdlGeneratorTests
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
                            DataType = "int",
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

        var sql = new MySqlDdlGenerator().Build(diagram);

        sql.Should().Contain("CREATE TABLE `users`");
        sql.Should().Contain("`id` int NOT NULL");
        sql.Should().Contain("CONSTRAINT `PK_users` PRIMARY KEY (`id`)");
        sql.Should().Contain("`name` varchar(50) NULL");
    }

    /// <summary>ENGINE 句を出力しないことを検証する（8.0 既定 InnoDB）</summary>
    [Fact(DisplayName = "Build: ENGINE 句を出力しない")]
    public void Build_DoesNotEmitEngineClause()
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
                            DataType = "int",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new MySqlDdlGenerator().Build(diagram);

        sql.Should().NotContain("ENGINE");
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
                            DataType = "int",
                            IsPrimaryKey = true,
                        },
                        new Column
                        {
                            Name = "line_no",
                            DataType = "int",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new MySqlDdlGenerator().Build(diagram);

        sql.Should().Contain("CONSTRAINT `PK_order_items` PRIMARY KEY (`order_id`, `line_no`)");
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
                    DataType = "int",
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
                    DataType = "int",
                    IsPrimaryKey = true,
                },
                new Column { Name = "parent_id", DataType = "int" },
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

        var sql = new MySqlDdlGenerator().Build(diagram);

        sql.Should().Contain("ALTER TABLE `child` ADD CONSTRAINT `FK_child_parent`");
        sql.Should().Contain("FOREIGN KEY (`parent_id`) REFERENCES `parent` (`id`)");
        sql.Should().Contain("ON DELETE CASCADE");
        sql.Should().Contain("ON UPDATE SET NULL");
    }

    /// <summary>schema.table 名が `schema`.`table` へ分割クォートされ、PK 制約名が安全化されることを検証する</summary>
    [Fact(DisplayName = "Build: schema.table 形式は `schema`.`table` へ分割される")]
    public void Build_SchemaQualifiedTableName_SplitsQuotes()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "shop.users",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new MySqlDdlGenerator().Build(diagram);

        sql.Should().Contain("CREATE TABLE `shop`.`users`");
        sql.Should().Contain("CONSTRAINT `PK_shop_users` PRIMARY KEY (`id`)");
    }

    /// <summary>日本語テーブル名・列名がバッククォートでクォートされることを検証する</summary>
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
                            DataType = "int",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new MySqlDdlGenerator().Build(diagram);

        sql.Should().Contain("CREATE TABLE `顧客`");
        sql.Should().Contain("`顧客ID` int NOT NULL");
        sql.Should().Contain("PRIMARY KEY (`顧客ID`)");
    }

    /// <summary>識別子に含まれるバッククォートが二重化エスケープされることを検証する</summary>
    [Fact(DisplayName = "Build: 識別子に含まれる ` がエスケープされる")]
    public void Build_IdentifierContainingBacktick_IsEscaped()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "wei`rd",
                    Columns =
                    {
                        new Column
                        {
                            Name = "co`l",
                            DataType = "int",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new MySqlDdlGenerator().Build(diagram);

        sql.Should().Contain("CREATE TABLE `wei``rd`");
        sql.Should().Contain("`co``l` int NOT NULL");
    }

    /// <summary>テーブル説明が閉じ括弧後の COMMENT= 句、列説明が列定義インライン COMMENT で出力されることを検証する</summary>
    [Fact(
        DisplayName = "Build: テーブル説明は COMMENT= 句、列説明はインライン COMMENT で出力される"
    )]
    public void Build_EmitsTableAndColumnComments()
    {
        var diagram = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "User",
                    Description = "利用者マスタ",
                    Columns =
                    {
                        new Column
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "Name",
                            DataType = "varchar(50)",
                            IsNullable = true,
                            Description = "氏名",
                        },
                    },
                },
            },
        };

        var sql = new MySqlDdlGenerator().Build(diagram);

        // 列説明は列定義インラインの COMMENT（区切りカンマの前）
        sql.Should().Contain("`Name` varchar(50) NULL COMMENT '氏名',");
        // テーブル説明は閉じ括弧後の COMMENT= 句
        sql.Should().Contain(") COMMENT='利用者マスタ';");
    }

    /// <summary>説明に含まれるシングルクォートが二重化エスケープされることを検証する</summary>
    [Fact(DisplayName = "Build: 説明のシングルクォートがエスケープされる")]
    public void Build_EscapesQuotesInComments()
    {
        var diagram = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "T",
                    Description = "It's a table",
                    Columns =
                    {
                        new Column
                        {
                            Name = "C",
                            DataType = "int",
                            Description = "it's a column",
                        },
                    },
                },
            },
        };

        var sql = new MySqlDdlGenerator().Build(diagram);

        sql.Should().Contain("COMMENT 'it''s a column'");
        sql.Should().Contain(") COMMENT='It''s a table';");
    }

    /// <summary>説明が無い図では COMMENT が一切出力されない（従来出力と不変）ことを検証する</summary>
    [Fact(DisplayName = "Build: 説明なしの図では COMMENT を出力しない")]
    public void Build_NoDescription_EmitsNoComment()
    {
        var diagram = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "T",
                    Columns =
                    {
                        new Column
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    },
                },
            },
        };

        var sql = new MySqlDdlGenerator().Build(diagram);

        sql.Should().NotContain("COMMENT");
    }
}
