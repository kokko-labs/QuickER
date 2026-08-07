using AwesomeAssertions;
using QuickER.Model;
using QuickER.SqlServer;
using QuickER.ViewModels;

namespace QuickER.Tests.SqlServer;

/// <summary><see cref="SqlServerDdlGenerator"/> の DDL 生成（CREATE TABLE・FK・識別子エスケープ）を検証するテストクラス</summary>
public class SqlServerDdlGeneratorTests
{
    /// <summary>CREATE TABLE と PRIMARY KEY 制約が出力されることを検証する</summary>
    [Fact(DisplayName = "Build: CREATE TABLE と PRIMARY KEY が出力される")]
    public void Build_EmitsCreateTableAndPk()
    {
        var vm = new MainViewModel();
        var e = new EntityViewModel(
            new Entity
            {
                TableName = "User",
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
                        DataType = "nvarchar(50)",
                        IsNullable = true,
                    },
                },
            }
        );
        vm.Entities.Add(e);

        var sql = new SqlServerDdlGenerator().Build(vm.ToDiagramModel());

        sql.Should().Contain("CREATE TABLE [User]");
        sql.Should().Contain("[Id] int NOT NULL");
        sql.Should().Contain("PRIMARY KEY ([Id])");
        sql.Should().Contain("[Name] nvarchar(50) NULL");
    }

    /// <summary>NULL 許容しない列に NOT NULL が出力されることを検証する</summary>
    [Fact(DisplayName = "Build: NULL 許容 OFF の列は NOT NULL が出力される")]
    public void Build_NotNullableColumn_EmitsNotNull()
    {
        var vm = new MainViewModel();
        var e = new EntityViewModel(
            new Entity
            {
                TableName = "User",
                Columns =
                {
                    new Column
                    {
                        Name = "Code",
                        DataType = "nvarchar(20)",
                        IsNullable = false,
                    },
                },
            }
        );
        vm.Entities.Add(e);

        var sql = new SqlServerDdlGenerator().Build(vm.ToDiagramModel());

        sql.Should().Contain("[Code] nvarchar(20) NOT NULL");
    }

    /// <summary>1 対多リレーションから FOREIGN KEY 制約が生成されることを検証する</summary>
    [Fact(DisplayName = "Build: 1対多リレーションが FOREIGN KEY を生成する")]
    public void Build_OneToMany_EmitsForeignKey()
    {
        var vm = new MainViewModel();
        var parent = new EntityViewModel(
            new Entity
            {
                TableName = "P",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                },
            }
        );
        var child = new EntityViewModel(
            new Entity
            {
                TableName = "C",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column { Name = "ParentId", DataType = "int" },
                },
            }
        );
        vm.Entities.Add(parent);
        vm.Entities.Add(child);
        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                },
                parent,
                child
            )
        );

        var sql = new SqlServerDdlGenerator().Build(vm.ToDiagramModel());

        sql.Should().Contain("ALTER TABLE [C]");
        sql.Should().Contain("FOREIGN KEY ([ParentId])");
        sql.Should().Contain("REFERENCES [P] ([Id])");
    }

    /// <summary>指定の制約名と ON DELETE/UPDATE 参照アクションが FOREIGN KEY へ出力されることを検証する</summary>
    [Fact(DisplayName = "Build: 制約名と参照アクションが FOREIGN KEY に出力される")]
    public void Build_EmitsConstraintNameAndReferentialActions()
    {
        var vm = new MainViewModel();
        var parent = new EntityViewModel(
            new Entity
            {
                TableName = "Parent",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                },
            }
        );
        var child = new EntityViewModel(
            new Entity
            {
                TableName = "Child",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column { Name = "ParentId", DataType = "int" },
                },
            }
        );
        vm.Entities.Add(parent);
        vm.Entities.Add(child);
        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                    ConstraintName = "FK_Child_Parent_Custom",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.SetNull,
                },
                parent,
                child
            )
        );

        var sql = new SqlServerDdlGenerator().Build(vm.ToDiagramModel());

        sql.Should().Contain("CONSTRAINT [FK_Child_Parent_Custom]");
        sql.Should().Contain("ON DELETE CASCADE");
        sql.Should().Contain("ON UPDATE SET NULL");
    }

    /// <summary>schema.table 名が [schema].[table] へ括弧分割され、PK 制約名が安全化されることを検証する</summary>
    [Fact(
        DisplayName = "Build: schema.table 形式は [schema].[table] に分割され、PK 制約名は安全な名前になる"
    )]
    public void Build_SchemaQualifiedTableName_SplitsBracketsAndUsesSafeConstraintName()
    {
        var vm = new MainViewModel();
        var e = new EntityViewModel(
            new Entity
            {
                TableName = "dbo.User",
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
            }
        );
        vm.Entities.Add(e);

        var sql = new SqlServerDdlGenerator().Build(vm.ToDiagramModel());

        sql.Should().Contain("CREATE TABLE [dbo].[User]");
        sql.Should().Contain("CONSTRAINT [PK_dbo_User] PRIMARY KEY ([Id])");
    }

    /// <summary>識別子に含まれる ] が二重化エスケープされることを検証する</summary>
    [Fact(DisplayName = "Build: 識別子に含まれる ] がエスケープされる")]
    public void Build_IdentifierContainingClosingBracket_IsEscaped()
    {
        var vm = new MainViewModel();
        var e = new EntityViewModel(
            new Entity
            {
                TableName = "Weird]Name",
                Columns =
                {
                    new Column
                    {
                        Name = "Col]umn",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                },
            }
        );
        vm.Entities.Add(e);

        var sql = new SqlServerDdlGenerator().Build(vm.ToDiagramModel());

        sql.Should().Contain("CREATE TABLE [Weird]]Name]");
        sql.Should().Contain("[Col]]umn] int NOT NULL");
        sql.Should().Contain("PRIMARY KEY ([Col]]umn])");
    }

    /// <summary>schema 修飾された親子テーブルの FK が括弧分割と安全な既定制約名で出力されることを検証する</summary>
    [Fact(
        DisplayName = "Build: schema 修飾された親子の FOREIGN KEY は分割括弧付けと安全な既定制約名で出力される"
    )]
    public void Build_SchemaQualifiedForeignKey_UsesBracketsAndSafeDefaultConstraintName()
    {
        var vm = new MainViewModel();
        var parent = new EntityViewModel(
            new Entity
            {
                TableName = "dbo.P",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                },
            }
        );
        var child = new EntityViewModel(
            new Entity
            {
                TableName = "dbo.C",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column { Name = "ParentId", DataType = "int" },
                },
            }
        );
        vm.Entities.Add(parent);
        vm.Entities.Add(child);
        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                },
                parent,
                child
            )
        );

        var sql = new SqlServerDdlGenerator().Build(vm.ToDiagramModel());

        sql.Should().Contain("ALTER TABLE [dbo].[C] ADD CONSTRAINT [FK_dbo_C_dbo_P]");
        sql.Should().Contain("FOREIGN KEY ([ParentId]) REFERENCES [dbo].[P] ([Id])");
    }

    /// <summary>テーブル・列の説明が拡張プロパティ MS_Description の追加文（GO なし・スキーマ分解）で出力されることを検証する</summary>
    [Fact(
        DisplayName = "Build: テーブル・列の説明が sp_addextendedproperty で出力される（GO なし）"
    )]
    public void Build_EmitsExtendedPropertyDescriptions()
    {
        var diagram = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "dbo.User",
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
                            DataType = "nvarchar(50)",
                            Description = "氏名",
                        },
                    },
                },
            },
        };

        var sql = new SqlServerDdlGenerator().Build(diagram);

        // テーブルレベル（@level2 を含まない）
        sql.Should()
            .Contain(
                "EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'利用者マスタ', "
                    + "@level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',  @level1name=N'User';"
            );
        // カラムレベル（@level2 を含む）
        sql.Should()
            .Contain(
                "EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'氏名', "
                    + "@level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',  @level1name=N'User', "
                    + "@level2type=N'COLUMN', @level2name=N'Name';"
            );
        // DDL の他文（CREATE / ALTER）に合わせ GO は出力しない
        sql.Should().NotContain("GO");
    }

    /// <summary>説明に含まれるシングルクォートが N リテラルの規則で二重化エスケープされることを検証する</summary>
    [Fact(DisplayName = "Build: 説明のシングルクォートがエスケープされる")]
    public void Build_EscapesQuotesInDescriptions()
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
                        new Column { Name = "C", DataType = "int" },
                    },
                },
            },
        };

        var sql = new SqlServerDdlGenerator().Build(diagram);

        sql.Should().Contain("@value=N'It''s a table'");
    }

    /// <summary>説明が無い図では拡張プロパティが一切出力されない（従来出力と不変）ことを検証する</summary>
    [Fact(DisplayName = "Build: 説明なしの図では拡張プロパティを出力しない")]
    public void Build_NoDescription_EmitsNoExtendedProperty()
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

        var sql = new SqlServerDdlGenerator().Build(diagram);

        sql.Should().NotContain("sp_addextendedproperty");
    }

    /// <summary>一意制約を持つエンティティの図を組み立てる</summary>
    /// <param name="withPrimaryKey">主キー列を含めるかどうか（PK 行との区切りカンマ検証用）</param>
    private static (ErDiagram Diagram, Entity Entity) BuildUniqueDiagram(bool withPrimaryKey = true)
    {
        var entity = new Entity { TableName = "shops" };

        if (withPrimaryKey)
        {
            entity.Columns.Add(
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                }
            );
        }

        entity.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "nvarchar(20)",
                IsNullable = false,
            }
        );
        entity.Columns.Add(
            new Column
            {
                Name = "region",
                DataType = "nvarchar(10)",
                IsNullable = false,
            }
        );

        return (new ErDiagram { Entities = { entity } }, entity);
    }

    /// <summary>名前付き単一列の一意制約が PK 制約行の直後へ出力されることを検証する</summary>
    [Fact(DisplayName = "Build: 名前付き単一列 UNIQUE が PK の直後に出力される")]
    public void Build_NamedSingleColumnUnique_EmitsConstraint()
    {
        var (diagram, entity) = BuildUniqueDiagram();
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_shops_code", ColumnIds = [entity.Columns[1].Id] }
        );

        var sql = new SqlServerDdlGenerator().Build(diagram);

        // PK 行には後続制約があるため区切りカンマが付く
        sql.Should().Contain("CONSTRAINT [PK_shops] PRIMARY KEY ([id]),");
        sql.Should().Contain("CONSTRAINT [UQ_shops_code] UNIQUE ([code])");
        // 最後の制約行に余分なカンマは付かない
        sql.Should().NotContain("UNIQUE ([code]),");
    }

    /// <summary>制約名なしの複合一意制約が合成名・宣言順で出力されることを検証する</summary>
    [Fact(DisplayName = "Build: 名前なし複合 UNIQUE は UQ_テーブル_列… の合成名になる")]
    public void Build_UnnamedCompositeUnique_SynthesizesName()
    {
        var (diagram, entity) = BuildUniqueDiagram();
        // 宣言順は region → code（列定義順とは逆）
        entity.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [entity.Columns[2].Id, entity.Columns[1].Id] }
        );

        var sql = new SqlServerDdlGenerator().Build(diagram);

        sql.Should().Contain("CONSTRAINT [UQ_shops_region_code] UNIQUE ([region], [code])");
    }

    /// <summary>PK が無くても列定義の末尾カンマが一意制約行の有無で正しく付くことを検証する</summary>
    [Fact(DisplayName = "Build: PK なしでも UNIQUE 行の前の列にカンマが付く")]
    public void Build_WithoutPrimaryKey_KeepsCommaBeforeUnique()
    {
        var (diagram, entity) = BuildUniqueDiagram(withPrimaryKey: false);
        entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [entity.Columns[0].Id] });

        var sql = new SqlServerDdlGenerator().Build(diagram);

        sql.Should().NotContain("PRIMARY KEY");
        sql.Should().Contain("[region] nvarchar(10) NOT NULL,");
        sql.Should().Contain("CONSTRAINT [UQ_shops_code] UNIQUE ([code])");
    }

    /// <summary>一意制約を持たない図では UNIQUE 行を 1 行も出力しないことを検証する（既存出力のバイト不変）</summary>
    [Fact(DisplayName = "Build: 一意制約が無ければ UNIQUE を出力しない")]
    public void Build_WithoutUniqueConstraints_EmitsNoUnique()
    {
        var (diagram, _) = BuildUniqueDiagram();

        new SqlServerDdlGenerator().Build(diagram).Should().NotContain("UNIQUE");
    }
}
