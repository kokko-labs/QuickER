using FluentAssertions;
using QuickER.Model;
using QuickER.Sqlite;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="SqliteDdlGenerator"/> の DDL 生成（インライン PK / FK / UNIQUE・コメント・識別子クォート）を検証するテストクラス
/// </summary>
public class SqliteDdlGeneratorTests
{
    /// <summary>ヘッダコメントが他方言と同一文言で出力されることを検証する</summary>
    [Fact(DisplayName = "Build: ヘッダコメントが共通文言で出力される")]
    public void Build_EmitsCommonHeader()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "t",
                    Columns =
                    {
                        new Column { Name = "id", DataType = "INT" },
                    },
                },
            ],
        };

        var sql = new SqliteDdlGenerator().Build(diagram);

        sql.Should().StartWith("-- QuickER によって自動生成された DDL");
    }

    /// <summary>
    /// 単一整数 PK でも宣言型を verbatim に維持し、AUTOINCREMENT を出力しないことを検証する
    /// （モデルに自動採番の概念がなく他方言も IDENTITY 等を出力しない。アプリ側採番前提）
    /// </summary>
    [Fact(DisplayName = "Build: 単一整数 PK は宣言型を維持し AUTOINCREMENT を出力しない")]
    public void Build_SingleIntegerPk_KeepsDeclaredTypeWithoutAutoIncrement()
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
                            DataType = "INT",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "name",
                            DataType = "NVARCHAR(50)",
                            IsNullable = true,
                        },
                    },
                },
            ],
        };

        var sql = new SqliteDdlGenerator().Build(diagram);

        sql.Should().Contain("CREATE TABLE \"users\"");
        // 宣言型 INT を INTEGER 等へ書き換えない（往復無損失の要）
        sql.Should().Contain("\"id\" INT NOT NULL");
        sql.Should().Contain("\"name\" NVARCHAR(50) NULL");
        sql.Should().NotContain("AUTOINCREMENT");
        // PK は末尾のインライン PRIMARY KEY 制約として出力する
        sql.Should().Contain("CONSTRAINT \"PK_users\" PRIMARY KEY (\"id\")");
    }

    /// <summary>複合主キーは末尾のインライン PRIMARY KEY 制約へまとめて出力されることを検証する</summary>
    [Fact(DisplayName = "Build: 複合 PK は末尾のインライン PRIMARY KEY を生成する")]
    public void Build_CompositePrimaryKey_EmitsInlineConstraint()
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
                            DataType = "INT",
                            IsPrimaryKey = true,
                        },
                        new Column
                        {
                            Name = "line_no",
                            DataType = "INT",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new SqliteDdlGenerator().Build(diagram);

        sql.Should().NotContain("AUTOINCREMENT");
        sql.Should()
            .Contain("CONSTRAINT \"PK_order_items\" PRIMARY KEY (\"order_id\", \"line_no\")");
    }

    /// <summary>非整数の単一 PK もインライン PRIMARY KEY 制約になることを検証する</summary>
    [Fact(DisplayName = "Build: 非整数の単一 PK はインライン PRIMARY KEY 制約になる")]
    public void Build_NonIntegerSinglePk_UsesInlineConstraint()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "docs",
                    Columns =
                    {
                        new Column
                        {
                            Name = "code",
                            DataType = "NVARCHAR(20)",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new SqliteDdlGenerator().Build(diagram);

        sql.Should().NotContain("AUTOINCREMENT");
        sql.Should().Contain("CONSTRAINT \"PK_docs\" PRIMARY KEY (\"code\")");
    }

    /// <summary>1 対多リレーションからインライン FK 制約と参照アクションが生成されることを検証する</summary>
    [Fact(
        DisplayName = "Build: 1対多リレーションがインライン FOREIGN KEY と参照アクションを生成する"
    )]
    public void Build_OneToMany_EmitsInlineForeignKeyWithActions()
    {
        var parent = new Entity
        {
            TableName = "parent",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "INT",
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
                    DataType = "INT",
                    IsPrimaryKey = true,
                },
                new Column { Name = "parent_id", DataType = "INT" },
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

        var sql = new SqliteDdlGenerator().Build(diagram);

        // FK は子テーブルの CREATE TABLE 内にインラインで出る（ALTER TABLE は使わない）
        sql.Should().NotContain("ALTER TABLE");
        sql.Should().Contain("CONSTRAINT \"FK_child_parent\"");
        sql.Should().Contain("FOREIGN KEY (\"parent_id\") REFERENCES \"parent\" (\"id\")");
        sql.Should().Contain("ON DELETE CASCADE");
        sql.Should().Contain("ON UPDATE SET NULL");
    }

    /// <summary>テーブル・カラムの説明が SQL コメント（--）として出力されることを検証する</summary>
    [Fact(DisplayName = "Build: テーブル・カラムの説明が -- コメントで出力される")]
    public void Build_Descriptions_EmittedAsSqlComments()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "products",
                    Description = "商品マスタ",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "INT",
                            IsPrimaryKey = true,
                            Description = "商品ID",
                        },
                    },
                },
            ],
        };

        var sql = new SqliteDdlGenerator().Build(diagram);

        sql.Should().Contain("-- products: 商品マスタ");
        sql.Should().Contain("-- id: 商品ID");
    }

    /// <summary>多対多リレーションはコメント行のみ出力されることを検証する</summary>
    [Fact(DisplayName = "Build: 多対多はジャンクションテーブルのコメントを出力する")]
    public void Build_ManyToMany_EmitsComment()
    {
        var a = new Entity
        {
            TableName = "a",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "INT",
                    IsPrimaryKey = true,
                },
            },
        };
        var b = new Entity
        {
            TableName = "b",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "INT",
                    IsPrimaryKey = true,
                },
            },
        };
        var diagram = new ErDiagram
        {
            Entities = [a, b],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = a.Id,
                    TargetEntityId = b.Id,
                    Type = RelationshipType.ManyToMany,
                },
            ],
        };

        var sql = new SqliteDdlGenerator().Build(diagram);

        sql.Should().Contain("-- 多対多 (a ⇄ b): ジャンクションテーブルを別途定義してください。");
    }

    /// <summary>日本語・二重引用符を含む識別子がクォート・エスケープされることを検証する</summary>
    [Fact(DisplayName = "Build: 識別子が二重引用符でクォート・エスケープされる")]
    public void Build_Identifiers_AreQuotedAndEscaped()
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
                            Name = "co\"l",
                            DataType = "NVARCHAR(10)",
                            IsPrimaryKey = true,
                        },
                    },
                },
            ],
        };

        var sql = new SqliteDdlGenerator().Build(diagram);

        sql.Should().Contain("CREATE TABLE \"顧客\"");
        sql.Should().Contain("\"co\"\"l\"");
    }
}
