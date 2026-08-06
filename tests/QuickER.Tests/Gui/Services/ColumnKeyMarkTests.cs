using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// ER 図カードのキー標識（PK / FK / UQ）の判定と表示を 1 本化した
/// <see cref="ColumnKeyMarkPalette"/> と <see cref="ColumnViewModel"/> の派生プロパティを検証するテストクラス。
/// </summary>
/// <remarks>
/// キャンバス XAML・ベクタ印刷・SVG 出力・寸法計測の 4 経路が同じ判定を共有するため、
/// 判定規則（PK &gt; FK &gt; UQ の優先度・簡易表示での可視性）をここで固定する。
/// </remarks>
public class ColumnKeyMarkTests
{
    [Theory(DisplayName = "キー標識は PK > FK > UQ の優先度で 1 つに畳まれる")]
    [InlineData(false, false, false, ColumnKeyMark.None)]
    [InlineData(true, false, false, ColumnKeyMark.PrimaryKey)]
    [InlineData(false, true, false, ColumnKeyMark.ForeignKey)]
    [InlineData(false, false, true, ColumnKeyMark.Unique)]
    [InlineData(true, true, true, ColumnKeyMark.PrimaryKey)]
    [InlineData(false, true, true, ColumnKeyMark.ForeignKey)]
    [InlineData(true, false, true, ColumnKeyMark.PrimaryKey)]
    public void Resolve_AppliesPriority(
        bool isPrimaryKey,
        bool isForeignKey,
        bool isUniqueMember,
        ColumnKeyMark expected
    )
    {
        ColumnKeyMarkPalette
            .Resolve(isPrimaryKey, isForeignKey, isUniqueMember)
            .Should()
            .Be(expected);
    }

    [Theory(DisplayName = "キー標識の表示文字はいずれも 2 文字（欄幅を変えない）")]
    [InlineData(ColumnKeyMark.None, "")]
    [InlineData(ColumnKeyMark.PrimaryKey, "PK")]
    [InlineData(ColumnKeyMark.ForeignKey, "FK")]
    [InlineData(ColumnKeyMark.Unique, "UQ")]
    public void GetText_ReturnsExpectedLabel(ColumnKeyMark mark, string expected)
    {
        ColumnKeyMarkPalette.GetText(mark).Should().Be(expected);
    }

    [Fact(DisplayName = "キー標識の配色は PK 赤・FK 青・UQ 緑")]
    public void GetColor_UsesDesignatedColors()
    {
        ColumnKeyMarkPalette.GetColor(ColumnKeyMark.PrimaryKey).Should().Be("#D93025");
        ColumnKeyMarkPalette.GetColor(ColumnKeyMark.ForeignKey).Should().Be("#1A73E8");
        ColumnKeyMarkPalette.GetColor(ColumnKeyMark.Unique).Should().Be("#188038");
    }

    [Fact(DisplayName = "簡易表示では UQ 行は畳まれる（PK/FK のみ表示）")]
    public void IsVisibleInCompactView_HidesUniqueOnlyRows()
    {
        ColumnKeyMarkPalette.IsVisibleInCompactView(ColumnKeyMark.PrimaryKey).Should().BeTrue();
        ColumnKeyMarkPalette.IsVisibleInCompactView(ColumnKeyMark.ForeignKey).Should().BeTrue();
        ColumnKeyMarkPalette.IsVisibleInCompactView(ColumnKeyMark.Unique).Should().BeFalse();
        ColumnKeyMarkPalette.IsVisibleInCompactView(ColumnKeyMark.None).Should().BeFalse();
    }

    [Fact(DisplayName = "一意制約の構成列になると ColumnViewModel の標識が UQ へ変わり通知される")]
    public void ColumnViewModel_KeyMark_FollowsUniqueMembership()
    {
        var code = new Column { Name = "Code", DataType = "nvarchar(20)" };
        var entity = new EntityViewModel(new Entity { TableName = "Customer", Columns = { code } });
        var column = entity.Columns[0];

        column.KeyMark.Should().Be(ColumnKeyMark.None);
        column.KeyMarkText.Should().BeEmpty();

        var notified = new List<string?>();
        column.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        entity.UniqueConstraints.Add(
            new UniqueConstraintViewModel(entity, new UniqueConstraint { ColumnIds = { code.Id } })
        );

        column.KeyMark.Should().Be(ColumnKeyMark.Unique);
        column.KeyMarkText.Should().Be("UQ");
        column.KeyMarkColor.Should().Be("#188038");
        notified
            .Should()
            .Contain(nameof(ColumnViewModel.KeyMarkText), "図の標識が即時更新されるための通知");
    }

    [Fact(
        DisplayName = "簡易表示の表示高さは UQ 構成列を含めても変わらない（カード高さの意味論を維持）"
    )]
    public void CompactViewHeight_IgnoresUniqueMembers()
    {
        var code = new Column { Name = "Code", DataType = "nvarchar(20)" };
        var entity = new EntityViewModel(
            new Entity
            {
                TableName = "Customer",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    code,
                },
            },
            new EntityLayout { Width = 220 }
        );

        // キャッシュを介さず毎回計測し直す経路で比較する（DisplayHeight はキャッシュのため差が出ない）
        var before = DiagramMetricsService.EstimateEntityHeight(
            entity,
            showDescriptions: false,
            isCompactView: true
        );

        entity.UniqueConstraints.Add(
            new UniqueConstraintViewModel(entity, new UniqueConstraint { ColumnIds = { code.Id } })
        );

        DiagramMetricsService
            .EstimateEntityHeight(entity, showDescriptions: false, isCompactView: true)
            .Should()
            .Be(before);
    }
}
