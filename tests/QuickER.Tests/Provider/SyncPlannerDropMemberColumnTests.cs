using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;
using Xunit;
using ProviderStrings = QuickER.Provider.Resources.Strings;

namespace QuickER.Tests.Provider;

/// <summary>
/// 主キー・一意制約の構成列の削除（<see cref="SchemaDiffKind.DropColumn"/>）を、それを前提にする変更を選ばずに選んだときの
/// 計画を検証する。
/// </summary>
/// <remarks>
/// 主キーの列の削除は、その列を外す主キーの変更を選んだときだけ行い、選ばなければ外して名指しする。一意制約の構成列の
/// 削除は、その一意制約を暗黙に外して名指しする。どちらも方言に依らず同じ（外さないと、方言によって主キー・一意制約が
/// 黙って消える・縮む・削除が断られる）。
/// </remarks>
public class SyncPlannerDropMemberColumnTests
{
    /// <summary>5 方言のプロバイダ</summary>
    public static TheoryData<string> Dialects() =>
        new() { "sqlserver", "postgresql", "mysql", "oracle", "sqlite" };

    /// <summary>
    /// 主キーの列 b の削除だけを選ぶと外して名指しし、一意制約 (c, d) の構成列 d の削除はその一意制約を暗黙に外して
    /// 名指しすることを検証する。逐次 DDL の方言では一意制約の削除が列の削除より前に出て、SQLite の再構築は主キーを保ち
    /// 一意制約を持たない。
    /// </summary>
    [Theory(
        DisplayName = "列の削除: 主キーの列は主キーの変更が無ければ外し、一意制約の構成列は一意制約ごと外して警告する"
    )]
    [MemberData(nameof(Dialects))]
    public void DropColumnsOnly_ExcludePrimaryKeyColumnAndDropUniqueConstraint(string dialect)
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

        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.SelectedItemRequiresUnselectedItem)
            .Which.Should()
            .Be(
                new SyncPlanWarning(
                    SyncPlanWarningKind.SelectedItemRequiresUnselectedItem,
                    "t",
                    "b (PRIMARY KEY)"
                )
            );
        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.UniqueConstraintDroppedWithColumn)
            .Which.Should()
            .Be(
                new SyncPlanWarning(
                    SyncPlanWarningKind.UniqueConstraintDroppedWithColumn,
                    "t",
                    "UQ_t_cd (c, d)"
                )
            );

        if (plan.Rebuilds.Count > 0)
        {
            var definition = plan.Rebuilds.Should().ContainSingle().Which.NewDefinition;
            definition.Columns.Select(c => c.Name).Should().Equal("a", "b", "c");
            definition.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("a", "b");
            definition.UniqueConstraints.Should().BeEmpty();
            return;
        }

        plan.Sections.Should().NotContain(s => s.Kind == SchemaDiffKind.AlterPrimaryKey);
        plan.Sections.Single(s => s.Kind == SchemaDiffKind.DropColumn)
            .Items.Select(i => i.ColumnName)
            .Should()
            .Equal("d");
        var drop = plan
            .Sections.Single(s => s.Kind == SchemaDiffKind.DropUniqueConstraint)
            .Items.Should()
            .ContainSingle()
            .Which;
        drop.UniqueConstraintName.Should().Be("UQ_t_cd");
        drop.Description.Should()
            .Be(string.Format(ProviderStrings.Diff_AutoUniqueConstraintDrop, "c, d"));

        var script = provider.SyncScriptBuilder.Build(plan);
        script
            .IndexOf("UQ_t_cd", StringComparison.Ordinal)
            .Should()
            .BePositive()
            .And.BeLessThan(LastIndexOfDropColumn(script));
    }

    /// <summary>
    /// 主キーの変更（b を外す）も選べば、主キーの列 b の削除は計画に残り、警告を出さないことを検証する。
    /// </summary>
    [Theory(DisplayName = "列の削除: その列を外す主キーの変更も選べば、主キーの列を削除する")]
    [MemberData(nameof(Dialects))]
    public void DropPrimaryKeyColumn_WithPrimaryKeyChange_IsKept(string dialect)
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
            item.IsSelected =
                item.Kind == SchemaDiffKind.AlterPrimaryKey
                || item is { Kind: SchemaDiffKind.DropColumn, ColumnName: "b" };
        }

        var plan = new SyncPlanner().BuildPlan(
            diff.Items,
            provider.SyncCapabilities,
            Context(live)
        );

        plan.Warnings.Should()
            .NotContain(w => w.Kind == SyncPlanWarningKind.SelectedItemRequiresUnselectedItem);

        if (plan.Rebuilds.Count > 0)
        {
            var definition = plan.Rebuilds.Should().ContainSingle().Which.NewDefinition;
            definition.Columns.Select(c => c.Name).Should().Equal("a", "c", "d");
            definition.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("a");
            return;
        }

        plan.Sections.Single(s => s.Kind == SchemaDiffKind.DropColumn)
            .Items.Select(i => i.ColumnName)
            .Should()
            .Equal("b");
    }

    /// <summary>
    /// 利用者が同じ一意制約の削除を選んでいれば、暗黙の削除を足さず警告も出さないことを検証する。
    /// </summary>
    [Fact(DisplayName = "列の削除: 一意制約の削除を選んでいれば、暗黙に足さず警告もしない")]
    public void DropUniqueMemberColumn_WithExplicitDrop_AddsNothing()
    {
        var (live, target) = Tables();
        var capabilities = TestDialectCapabilities.Neutral;
        var diff = new SchemaDiffService().Compute([live], [], [target], [], capabilities);

        foreach (var item in diff.Items)
        {
            item.IsSelected =
                item.Kind == SchemaDiffKind.DropUniqueConstraint
                || item is { Kind: SchemaDiffKind.DropColumn, ColumnName: "d" };
        }

        var plan = new SyncPlanner().BuildPlan(diff.Items, capabilities, Context(live));

        plan.Warnings.Should()
            .NotContain(w => w.Kind == SyncPlanWarningKind.UniqueConstraintDroppedWithColumn);
        plan.Sections.Single(s => s.Kind == SchemaDiffKind.DropUniqueConstraint)
            .Items.Should()
            .ContainSingle()
            .Which.Description.Should()
            .NotBe(string.Format(ProviderStrings.Diff_AutoUniqueConstraintDrop, "c, d"));
    }

    /// <summary>
    /// 選んだ主キーの変更が、選んでいない列の追加を前提にするため外れるとき、その変更でだけ主キーから外れる列の削除も
    /// 外すことを検証する（外れた主キーの変更を同期後の主キーに数えると、列の削除だけが残って主キーが消える）。
    /// </summary>
    [Fact(DisplayName = "列の削除: 外れた主キーの変更を前提にする主キーの列の削除も外す")]
    public void DropPrimaryKeyColumn_WhenPrimaryKeyChangeIsExcluded_IsExcluded()
    {
        var live = new Entity { TableName = "t", Columns = { Key("a"), Plain("x") } };
        var target = new Entity { TableName = "t", Columns = { Key("n"), Plain("x") } };
        var capabilities = TestDialectCapabilities.Neutral;
        var diff = new SchemaDiffService().Compute([live], [], [target], [], capabilities);

        foreach (var item in diff.Items)
        {
            item.IsSelected =
                item.Kind is SchemaDiffKind.AlterPrimaryKey or SchemaDiffKind.DropColumn;
        }

        var plan = new SyncPlanner().BuildPlan(diff.Items, capabilities, Context(live));

        plan.Warnings.Where(w => w.Kind == SyncPlanWarningKind.SelectedItemRequiresUnselectedItem)
            .Select(w => w.Detail)
            .Should()
            .BeEquivalentTo(["PRIMARY KEY (t.n)", "a (PRIMARY KEY)"]);
        plan.Sections.Should()
            .NotContain(s =>
                s.Kind == SchemaDiffKind.DropColumn || s.Kind == SchemaDiffKind.AlterPrimaryKey
            );
    }

    /// <summary>5 方言 × 新しい列を足すか</summary>
    public static TheoryData<string, bool> DialectsWithAddition()
    {
        var data = new TheoryData<string, bool>();

        foreach (var dialect in new[] { "sqlserver", "postgresql", "mysql", "oracle", "sqlite" })
        {
            data.Add(dialect, false);
            data.Add(dialect, true);
        }

        return data;
    }

    /// <summary>
    /// 表の列をすべて消す変更（主キーの解除と列の削除）は、列の削除をすべて計画から外して
    /// <see cref="SyncPlanWarningKind.TableLeftWithoutColumns"/> で名指しし、外した列を前提にした暗黙の一意制約・外部キーの削除を
    /// 足さないことを検証する。新しい列も足す変更（元の列を全部消して置き換える）は外さない（同期後に列が残る）。
    /// </summary>
    [Theory(
        DisplayName = "列の削除: 表の列が 1 本も残らないなら列の削除を外して警告し、暗黙の削除も足さない（列を足すなら外さない）"
    )]
    [MemberData(nameof(DialectsWithAddition))]
    public void DropAllColumns_ExcludesDropsUnlessColumnIsAdded(string dialect, bool addColumn)
    {
        var provider = ProviderFor(dialect);
        var (live, _) = Tables();
        var parent = new Entity { TableName = "p", Columns = { Key("id") } };
        var fk = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = live.Id,
            Type = RelationshipType.OneToMany,
            ConstraintName = "FK_t_p",
            ColumnPairs = [new RelationshipColumnPair(parent.Columns[0].Id, live.Columns[2].Id)],
        };
        var target = new Entity { TableName = "t" };

        if (addColumn)
        {
            target.Columns.Add(Plain("n"));
        }

        var targetParent = new Entity { TableName = "p", Columns = { Key("id") } };
        var diff = new SchemaDiffService().Compute(
            [live, parent],
            [fk],
            [target, targetParent],
            [],
            provider.SyncCapabilities
        );

        foreach (var item in diff.Items)
        {
            item.IsSelected =
                item.Kind
                    is SchemaDiffKind.AlterPrimaryKey
                        or SchemaDiffKind.DropColumn
                        or SchemaDiffKind.AddColumn;
        }

        var plan = new SyncPlanner().BuildPlan(
            diff.Items,
            provider.SyncCapabilities,
            new SyncPlanContext
            {
                LiveEntities = [live, parent],
                LiveRelationships = [fk],
                AuxiliaryObjects = [],
                TableCreateSql = new Dictionary<string, string>(),
                ColumnDefaults = [],
            }
        );

        if (addColumn)
        {
            plan.Warnings.Should()
                .NotContain(w => w.Kind == SyncPlanWarningKind.TableLeftWithoutColumns);
            plan.ExcludedItems.Should().BeEmpty();
            return;
        }

        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.TableLeftWithoutColumns)
            .Which.Should()
            .Be(
                new SyncPlanWarning(SyncPlanWarningKind.TableLeftWithoutColumns, "t", "a, b, c, d")
            );
        plan.ExcludedItems.Select(i => (i.Kind, i.ColumnName))
            .Should()
            .BeEquivalentTo(
                new[] { "a", "b", "c", "d" }.Select(c => (SchemaDiffKind.DropColumn, (string?)c))
            );
        plan.Warnings.Should()
            .NotContain(w => w.Kind == SyncPlanWarningKind.UniqueConstraintDroppedWithColumn);

        if (plan.Rebuilds.Count > 0)
        {
            var definition = plan.Rebuilds.Should().ContainSingle().Which;
            definition.NewDefinition.Columns.Select(c => c.Name).Should().Equal("a", "b", "c", "d");
            definition.NewDefinition.GetPrimaryKeyColumnsInOrder().Should().BeEmpty();
            definition.NewDefinition.UniqueConstraints.Should().ContainSingle();
            definition.ForeignKeys.Should().ContainSingle();
            definition.CopyColumns.Should().Equal("a", "b", "c", "d");
            return;
        }

        plan.Sections.Should()
            .NotContain(s =>
                s.Kind == SchemaDiffKind.DropColumn
                || s.Kind == SchemaDiffKind.DropUniqueConstraint
                || s.Kind == SchemaDiffKind.DropForeignKey
            );
        plan.Sections.Should()
            .ContainSingle(s =>
                s.Kind == SchemaDiffKind.AlterPrimaryKey
                && s.PrimaryKeyPhase == PrimaryKeyPhase.Drop
            );
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
