using System;
using System.Collections.Generic;
using AwesomeAssertions;
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
/// 主キー・一意制約の構成列の削除（<see cref="SchemaDiffKind.DropColumn"/>）を、それを前提にする変更を選ばずに選んだときの
/// 同期スクリプトを検証する。
/// </summary>
/// <remarks>
/// 一意制約の構成列の削除は、その一意制約を暗黙に外して名指しする。同期スクリプトでは、その一意制約の削除が列の削除より前に出る
/// （外さないと、方言によって一意制約が黙って消える・縮む・削除が断られる）。計画の中身（主キーの列の削除を外す・一意制約を暗黙に
/// 外す・警告）は、同期計画の期待の行 <c>plan-drop-member-column-*</c>＝SyncPlanExpectationRows.PlanDropMemberColumn が見る。
/// </remarks>
public class SyncPlannerDropMemberColumnTests
{
    /// <summary>逐次 DDL の 4 方言（SQLite は再構築へ畳むので、一意制約の削除の文と列の削除の文を持たない）</summary>
    public static TheoryData<string> Dialects() =>
        new() { "sqlserver", "postgresql", "mysql", "oracle" };

    /// <summary>
    /// 主キーの列 b と一意制約 (c, d) の構成列 d の削除だけを選ぶと、同期スクリプトで暗黙に外す一意制約の削除が列の削除より前に
    /// 出ることを検証する。
    /// </summary>
    [Theory(
        DisplayName = "列の削除: 一意制約の構成列の削除では、暗黙に外す一意制約の削除が同期スクリプトで列の削除より前に出る"
    )]
    [MemberData(nameof(Dialects))]
    public void DropColumnsOnly_DropsUniqueConstraintBeforeColumnInScript(string dialect)
    {
        var provider = ProviderFor(dialect);
        var (live, target) = Tables();
        var diff = new SchemaDiffService().Compute(
            [live],
            [],
            [target],
            [],
            provider.SyncCapabilities
        );

        foreach (var item in diff.Items)
        {
            item.IsSelected = item.Kind == SchemaDiffKind.DropColumn;
        }

        var plan = new SyncPlanner().BuildPlan(
            diff.Items,
            provider.SyncCapabilities,
            Context(live)
        );

        var script = provider.SyncScriptBuilder.Build(plan);
        script
            .IndexOf("UQ_t_cd", StringComparison.Ordinal)
            .Should()
            .BePositive()
            .And.BeLessThan(LastIndexOfDropColumn(script));
    }

    /// <summary>live: t(a PK, b PK, c, d, UNIQUE UQ_t_cd (c, d))・目標: t(a PK, c)</summary>
    private static (Entity Live, Entity Target) Tables()
    {
        var live = new Entity
        {
            TableName = "t",
            Columns = { Key("a"), Key("b"), Plain("c"), Plain("d") },
        };
        live.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_t_cd",
                ColumnIds = [live.Columns[2].Id, live.Columns[3].Id],
            }
        );
        var target = new Entity { TableName = "t", Columns = { Key("a"), Plain("c") } };
        return (live, target);
    }

    private static Column Key(string name) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };

    private static Column Plain(string name) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsNullable = true,
        };

    private static SyncPlanContext Context(Entity live) =>
        new()
        {
            LiveEntities = [live],
            LiveRelationships = [],
            AuxiliaryObjects = [],
            TableCreateSql = new Dictionary<string, string>(),
            ColumnDefaults = [],
        };

    private static IDatabaseProvider ProviderFor(string dialect) =>
        dialect switch
        {
            "postgresql" => new PostgreSqlProvider(),
            "mysql" => new MySqlProvider(),
            "oracle" => new OracleProvider(),
            "sqlite" => new SqliteProvider(),
            _ => new SqlServerProvider(),
        };

    /// <summary>列 d を消す文の位置（方言で引用符が違うので DROP COLUMN の最後の出現で見る）</summary>
    private static int LastIndexOfDropColumn(string script) =>
        script.LastIndexOf("DROP COLUMN", StringComparison.Ordinal);
}
