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
/// 既定値のある列の型の種類を変える列の変更を、計画が事前に警告すること（J2）を、5 方言の実際の宣言で検証する。
/// </summary>
public sealed class DefaultOnColumnTypeKindChangeTests
{
    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ];

    /// <summary>既定値のある列の型の種類を変えると、宣言した方言（SQLite 以外の 4 方言）だけが警告する</summary>
    [Theory(
        DisplayName = "既定値のある列の型の種類の変更は、SQLite 以外の 4 方言の計画だけが警告する"
    )]
    [InlineData(DialectNames.SqlServer, true)]
    [InlineData(DialectNames.PostgreSql, true)]
    [InlineData(DialectNames.MySql, true)]
    [InlineData(DialectNames.Oracle, true)]
    [InlineData(DialectNames.Sqlite, false)]
    public void TypeKindChangeWithDefault_WarnsOnlyWhereDeclared(string dialect, bool warns)
    {
        var warnings = Plan(dialect, "varchar(20)", "int", hasDefault: true);

        if (warns)
        {
            warnings
                .Should()
                .Equal(
                    new SyncPlanWarning(
                        SyncPlanWarningKind.DefaultOnColumnTypeKindChange,
                        "item",
                        "v (varchar(20) → int)"
                    )
                );
        }
        else
        {
            warnings.Should().BeEmpty();
        }
    }

    /// <summary>型の種類が変わらない（長さだけ）変更と、既定値の無い列の変更は警告しない</summary>
    [Theory(DisplayName = "型の種類が変わらない変更と既定値の無い列の変更は警告しない")]
    [InlineData("varchar(20)", "varchar(40)", true)]
    [InlineData("varchar(20)", "int", false)]
    public void SameKindOrNoDefault_DoesNotWarn(string oldType, string newType, bool hasDefault)
    {
        Plan(DialectNames.SqlServer, oldType, newType, hasDefault).Should().BeEmpty();
    }

    private static List<SyncPlanWarning> Plan(
        string dialect,
        string oldType,
        string newType,
        bool hasDefault
    )
    {
        var live = new Entity { TableName = "item" };
        live.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        live.Columns.Add(
            new Column
            {
                Name = "v",
                DataType = oldType,
                IsNullable = false,
            }
        );
        var provider = Providers.Single(p => p.Name == dialect);

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "item",
                    ColumnName = "v",
                    Column = new Column
                    {
                        Name = "v",
                        DataType = newType,
                        IsNullable = false,
                    },
                    OldColumn = live.Columns[1],
                    IsSelected = true,
                },
            ],
            provider.SyncCapabilities,
            new SyncPlanContext
            {
                LiveEntities = [live],
                // 既定値の無い場合も、別の列（id）の既定値は持たせる＝列ごとの照合を通す
                ColumnDefaults = hasDefault
                    ? [new SchemaColumnDefault("item", "v", "'abc'")]
                    : [new SchemaColumnDefault("item", "id", "0")],
            }
        );

        return
        [
            .. plan.Warnings.Where(w =>
                w.Kind == SyncPlanWarningKind.DefaultOnColumnTypeKindChange
            ),
        ];
    }
}
