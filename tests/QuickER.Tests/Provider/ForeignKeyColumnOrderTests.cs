using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 複合外部キーの列ペアを、参照先のキーの並びを求める方言（SQL Server・MySQL）の DDL だけが参照先のキーの並びへ
/// 並べ替えて出すことと、並べ替えの規則（主キー優先・一意制約・決まらなければそのまま）を検証する。
/// </summary>
public sealed class ForeignKeyColumnOrderTests
{
    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ];

    /// <summary>DDL は、扱いの表のマスが並べ替えの方言でだけ、列ペアを参照先の主キーの並びへ並べ替える</summary>
    [Theory(
        DisplayName = "DDL: 複合外部キーは SQL Server・MySQL だけが参照先のキーの並びへ並べ替える"
    )]
    [InlineData(DialectNames.SqlServer, true)]
    [InlineData(DialectNames.PostgreSql, false)]
    [InlineData(DialectNames.MySql, true)]
    [InlineData(DialectNames.Oracle, false)]
    [InlineData(DialectNames.Sqlite, false)]
    public void Ddl_CompositeForeignKey_IsReorderedOnlyWhereDeclared(string dialect, bool reordered)
    {
        var (parent, child, relationship) = Scenario();
        var provider = Providers.Single(p => p.Name == dialect);

        var sql = provider.DdlGenerator.Build(
            new ErDiagram { Entities = [parent, child], Relationships = [relationship] }
        );
        var flat = new string(sql.Where(c => char.IsLetter(c) || c == ',' || c == '(').ToArray());

        flat.Should()
            .Contain(
                reordered ? "FOREIGNKEY(x,yREFERENCES" : "FOREIGNKEY(y,xREFERENCES",
                $"{dialect}"
            );
        flat.Should()
            .Contain(reordered ? "REFERENCESparent(a,b" : "REFERENCESparent(b,a", $"{dialect}");
    }

    /// <summary>主キーの並びは実効順（主キーの順序の指定）に従い、主キーと合わなければ一意制約の宣言順に従う</summary>
    [Fact(DisplayName = "並べ替え: 主キーの実効順に従い、主キーと合わなければ一意制約の並びに従う")]
    public void Arrange_FollowsEffectivePrimaryKeyOrderThenUniqueConstraint()
    {
        var (parent, _, _) = Scenario();
        List<ForeignKeyColumnNamePair> pairs = [new("a", "x"), new("b", "y")];

        // 主キーの順序を (b, a) と指定する
        parent.PrimaryKeyColumnIds = [parent.Columns[1].Id, parent.Columns[0].Id];
        ForeignKeyColumnOrder
            .ArrangeToReferencedKey(pairs, parent)
            .Select(p => p.ParentColumn)
            .Should()
            .Equal("b", "a");

        // 主キーが (c) で、一意制約 (b, a) を参照する
        foreach (var column in parent.Columns)
        {
            column.IsPrimaryKey = false;
        }

        parent.Columns.Add(
            new Column
            {
                Name = "c",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        parent.PrimaryKeyColumnIds = [];
        parent.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [parent.Columns[1].Id, parent.Columns[0].Id] }
        );
        ForeignKeyColumnOrder
            .ArrangeToReferencedKey(pairs, parent)
            .Select(p => p.ParentColumn)
            .Should()
            .Equal("b", "a");
    }

    /// <summary>参照先の列の集合が主キーとも一意制約とも一致しなければ、並べ替えない</summary>
    [Fact(DisplayName = "並べ替え: 参照先のキーが決まらなければ宣言の並びのまま")]
    public void Arrange_WithoutMatchingKey_KeepsDeclaredOrder()
    {
        var (parent, _, _) = Scenario();
        parent.Columns.Add(
            new Column
            {
                Name = "c",
                DataType = "int",
                IsNullable = false,
            }
        );
        List<ForeignKeyColumnNamePair> pairs = [new("c", "y"), new("a", "x")];

        ForeignKeyColumnOrder.ArrangeToReferencedKey(pairs, parent).Should().Equal(pairs);
    }

    /// <summary>主キー (a, b) の親と、宣言を (b → y, a → x) にした複合外部キーの子</summary>
    private static (Entity Parent, Entity Child, Relationship Relationship) Scenario()
    {
        var parent = new Entity { TableName = "parent" };
        parent.Columns.Add(
            new Column
            {
                Name = "a",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        parent.Columns.Add(
            new Column
            {
                Name = "b",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        var child = new Entity { TableName = "child" };
        child.Columns.Add(
            new Column
            {
                Name = "x",
                DataType = "int",
                IsNullable = false,
            }
        );
        child.Columns.Add(
            new Column
            {
                Name = "y",
                DataType = "int",
                IsNullable = false,
            }
        );
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            ColumnPairs =
            [
                new(parent.Columns[1].Id, child.Columns[1].Id),
                new(parent.Columns[0].Id, child.Columns[0].Id),
            ],
            ConstraintName = "FK_child_parent",
        };

        return (parent, child, relationship);
    }
}
