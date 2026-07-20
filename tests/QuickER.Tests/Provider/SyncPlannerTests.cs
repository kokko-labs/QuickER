using System.Linq;
using FluentAssertions;
using QuickER.Provider;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary><see cref="SyncPlanner"/> が差分項目から組み立てる実行計画（選択フィルタ・固定順序・グループ化）を検証する</summary>
public class SyncPlannerTests
{
    /// <summary>指定種別・選択状態の差分項目を生成する小さなヘルパー</summary>
    private static SchemaDiffItem Item(
        SchemaDiffKind kind,
        bool selected = true,
        string table = "T"
    ) =>
        new()
        {
            Kind = kind,
            TableName = table,
            IsSelected = selected,
        };

    /// <summary>未選択（IsSelected=false）の項目が計画から除外されることを検証する</summary>
    [Fact(DisplayName = "未選択項目は計画から除外される")]
    public void Unselected_AreExcluded()
    {
        var plan = new SyncPlanner().BuildPlan(
            [
                Item(SchemaDiffKind.AddTable, selected: false),
                Item(SchemaDiffKind.AddColumn, selected: true),
            ],
            new SyncDialectCapabilities()
        );

        plan.Sections.Should().ContainSingle();
        plan.Sections[0].Kind.Should().Be(SchemaDiffKind.AddColumn);
    }

    /// <summary>入力が乱順でもセクションが固定順序（依存関係で失敗しない順）で並ぶことを検証する</summary>
    [Fact(DisplayName = "セクションは固定順序で並ぶ")]
    public void Sections_AreInFixedOrder()
    {
        // わざと逆順寄りに投入する
        var plan = new SyncPlanner().BuildPlan(
            [
                Item(SchemaDiffKind.SetColumnDescription),
                Item(SchemaDiffKind.SetTableDescription),
                Item(SchemaDiffKind.AddForeignKey),
                Item(SchemaDiffKind.DropTable),
                Item(SchemaDiffKind.DropColumn),
                Item(SchemaDiffKind.DropForeignKey),
                Item(SchemaDiffKind.AlterColumn),
                Item(SchemaDiffKind.AddColumn),
                Item(SchemaDiffKind.AddTable),
            ],
            new SyncDialectCapabilities()
        );

        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(
                SchemaDiffKind.AddTable,
                SchemaDiffKind.AddColumn,
                SchemaDiffKind.AlterColumn,
                SchemaDiffKind.DropForeignKey,
                SchemaDiffKind.DropColumn,
                SchemaDiffKind.DropTable,
                SchemaDiffKind.AddForeignKey,
                SchemaDiffKind.SetTableDescription,
                SchemaDiffKind.SetColumnDescription
            );
    }

    /// <summary>同一セクション内は入力の出現順を保持することを検証する</summary>
    [Fact(DisplayName = "セクション内は入力の出現順を保持する")]
    public void WithinSection_PreservesInputOrder()
    {
        var a = Item(SchemaDiffKind.AddColumn, table: "A");
        var b = Item(SchemaDiffKind.AddColumn, table: "B");
        var c = Item(SchemaDiffKind.AddColumn, table: "C");

        var plan = new SyncPlanner().BuildPlan([a, b, c], new SyncDialectCapabilities());

        plan.Sections.Should().ContainSingle();
        plan.Sections[0].Items.Should().Equal(a, b, c);
    }

    /// <summary>該当項目の無い種別はセクションとして現れない（空セクションを含まない）ことを検証する</summary>
    [Fact(DisplayName = "空セクションは含まれない")]
    public void EmptySections_AreOmitted()
    {
        var plan = new SyncPlanner().BuildPlan(
            [Item(SchemaDiffKind.AddTable)],
            new SyncDialectCapabilities()
        );

        plan.Sections.Should().ContainSingle();
        plan.Sections[0].Kind.Should().Be(SchemaDiffKind.AddTable);
    }

    /// <summary>RebuildTable は選択されていても計画から除外される（情報表示専用・SQL 生成対象外）ことを検証する</summary>
    [Fact(DisplayName = "RebuildTable は選択されていても除外される")]
    public void RebuildTable_IsAlwaysExcluded()
    {
        var plan = new SyncPlanner().BuildPlan(
            [
                Item(SchemaDiffKind.RebuildTable, selected: true),
                Item(SchemaDiffKind.AddTable, selected: true),
            ],
            new SyncDialectCapabilities()
        );

        plan.Sections.Should().ContainSingle();
        plan.Sections[0].Kind.Should().Be(SchemaDiffKind.AddTable);
        plan.Sections.Should().NotContain(s => s.Kind == SchemaDiffKind.RebuildTable);
    }

    /// <summary>すべて未選択なら計画が空（IsEmpty）になることを検証する</summary>
    [Fact(DisplayName = "全未選択なら計画は空になる")]
    public void AllUnselected_ResultsInEmptyPlan()
    {
        var plan = new SyncPlanner().BuildPlan(
            [
                Item(SchemaDiffKind.AddTable, selected: false),
                Item(SchemaDiffKind.DropTable, selected: false),
            ],
            new SyncDialectCapabilities()
        );

        plan.IsEmpty.Should().BeTrue();
        plan.Sections.Should().BeEmpty();
    }

    /// <summary>ケーパビリティが異なっても Phase 1 では計画結果に影響しない（スモーク）ことを検証する</summary>
    [Fact(DisplayName = "ケーパビリティは Phase 1 では計画へ影響しない")]
    public void Capabilities_DoNotAffectPlanInPhase1()
    {
        SchemaDiffItem[] items =
        [
            Item(SchemaDiffKind.AddTable),
            Item(SchemaDiffKind.AlterColumn),
            Item(SchemaDiffKind.SetTableDescription),
        ];

        var planner = new SyncPlanner();
        var defaultPlan = planner.BuildPlan(items, new SyncDialectCapabilities());
        var restrictedPlan = planner.BuildPlan(
            items,
            new SyncDialectCapabilities
            {
                SupportsAlterColumn = false,
                SupportsForeignKeyAlter = false,
                SupportsDescriptions = false,
                PersistsForeignKeyConstraintNames = false,
                ColumnReorder = ColumnReorderMode.Rebuild,
            }
        );

        restrictedPlan
            .Sections.Select(s => s.Kind)
            .Should()
            .Equal(defaultPlan.Sections.Select(s => s.Kind));
    }
}
