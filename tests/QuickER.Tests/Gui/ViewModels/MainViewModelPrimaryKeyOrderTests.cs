using System.IO;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 主キーの編集（構成列の置き換え・実効順の並び替え）と、その Undo / Redo を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 永続化先は一時フォルダへ隔離し（<c>UsePersistenceForTests</c>）、実 %LOCALAPPDATA% へは触れない。
/// </para>
/// <para>
/// 主キーの編集は「膜（<see cref="ColumnViewModel.IsPrimaryKey"/>）＋順序
/// （<see cref="EntityViewModel.PrimaryKeyColumnIds"/>）＋NULL 許容の連動」の 3 つが同時に動くため、
/// <c>Execute → Undo → Redo</c> の 3 点で状態を確かめる（膜が N 列動いても履歴は 1 件であること込み）。
/// </para>
/// </remarks>
public class MainViewModelPrimaryKeyOrderTests : IDisposable
{
    /// <summary>テスト専用の一時作業フォルダ（各テストで独立・後始末で削除する）</summary>
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-pkorder-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelPrimaryKeyOrderTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch
        {
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    /// <summary>TenantId / RegionCode の複合主キーと NULL 許容の Code 列を持つ VM を返す</summary>
    /// <param name="pinPrimaryKeyOrder">true なら宣言順どおりの主キー順を明示的に保存する</param>
    private MainViewModel CreateViewModel(bool pinPrimaryKeyOrder = true)
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();

        var tenantId = new Column
        {
            Name = "TenantId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var regionCode = new Column
        {
            Name = "RegionCode",
            DataType = "nvarchar(10)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var code = new Column
        {
            Name = "Code",
            DataType = "nvarchar(20)",
            IsNullable = true,
        };

        var model = new Entity
        {
            TableName = "TenantRegion",
            Columns = { tenantId, regionCode, code },
        };

        if (pinPrimaryKeyOrder)
        {
            model.PrimaryKeyColumnIds = [tenantId.Id, regionCode.Id];
        }

        var entity = new EntityViewModel(model);
        vm.Entities.Add(entity);
        vm.SelectedEntity = entity;
        vm.UndoRedo.Clear();

        return vm;
    }

    /// <summary>実効順の主キー列名を取り出す</summary>
    private static List<string> Order(EntityViewModel entity) =>
        entity.GetPrimaryKeyColumnsInOrder().Select(column => column.Name).ToList();

    [Fact(DisplayName = "主キー列の上移動は実効順を変え、1 回の Undo で戻る")]
    public void MovePrimaryKeyColumnUp_ReordersAndUndoesInOneStep()
    {
        var vm = CreateViewModel();
        var entity = vm.SelectedEntity!;

        vm.MovePrimaryKeyColumnUpCommand.Execute(entity.PrimaryKeyMembers[1]);

        Order(entity).Should().Equal("RegionCode", "TenantId");
        entity
            .PrimaryKeyMembers.Select(m => m.Column.Name)
            .Should()
            .Equal("RegionCode", "TenantId");

        // 並べ替えは膜を動かさないため、履歴は 1 件で足りる（列数分に分裂しないこと）
        vm.UndoRedo.Undo();
        Order(entity).Should().Equal("TenantId", "RegionCode");
        vm.UndoRedo.CanUndo.Should().BeFalse();

        vm.UndoRedo.Redo();
        Order(entity).Should().Equal("RegionCode", "TenantId");
    }

    [Fact(DisplayName = "主キー列の下移動は実効順を変え、1 回の Undo で戻る")]
    public void MovePrimaryKeyColumnDown_ReordersAndUndoesInOneStep()
    {
        var vm = CreateViewModel();
        var entity = vm.SelectedEntity!;

        vm.MovePrimaryKeyColumnDownCommand.Execute(entity.PrimaryKeyMembers[0]);

        Order(entity).Should().Equal("RegionCode", "TenantId");

        vm.UndoRedo.Undo();
        Order(entity).Should().Equal("TenantId", "RegionCode");
        vm.UndoRedo.CanUndo.Should().BeFalse();
    }

    [Fact(DisplayName = "端の行の移動は何も起こさない（履歴も積まない）")]
    public void MovePrimaryKeyColumn_AtBoundary_DoesNothing()
    {
        var vm = CreateViewModel();
        var entity = vm.SelectedEntity!;

        vm.MovePrimaryKeyColumnUpCommand.Execute(entity.PrimaryKeyMembers[0]);
        vm.MovePrimaryKeyColumnDownCommand.Execute(entity.PrimaryKeyMembers[1]);

        Order(entity).Should().Equal("TenantId", "RegionCode");
        vm.UndoRedo.CanUndo.Should().BeFalse();
    }

    [Fact(DisplayName = "NULL 許容列を主キーへ含めると NOT NULL になり、Undo で NULL 許容が戻る")]
    public void ApplyPrimaryKey_NullableColumn_BecomesNotNull_AndUndoRestoresNullability()
    {
        var vm = CreateViewModel();
        var entity = vm.SelectedEntity!;
        var code = entity.Columns.Single(column => column.Name == "Code");

        vm.ApplyPrimaryKey(entity, [entity.Columns[0], code]);

        code.IsPrimaryKey.Should().BeTrue();
        code.IsNullable.Should().BeFalse();
        Order(entity).Should().Equal("TenantId", "Code");

        // 膜が 2 列動いても履歴は 1 件（RunWithoutTracking で分裂させない）
        vm.UndoRedo.Undo();
        code.IsPrimaryKey.Should().BeFalse();
        code.IsNullable.Should().BeTrue("主キーを外したあとの NULL 許容は変更前の値へ戻るべき");
        Order(entity).Should().Equal("TenantId", "RegionCode");
        vm.UndoRedo.CanUndo.Should().BeFalse();

        vm.UndoRedo.Redo();
        code.IsPrimaryKey.Should().BeTrue();
        code.IsNullable.Should().BeFalse();
        Order(entity).Should().Equal("TenantId", "Code");
    }

    [Fact(DisplayName = "主キーから外した列の NULL 許容は Undo / Redo をまたいで据え置かれる")]
    public void ApplyPrimaryKey_DroppedColumn_KeepsNullability()
    {
        var vm = CreateViewModel();
        var entity = vm.SelectedEntity!;
        var regionCode = entity.Columns.Single(column => column.Name == "RegionCode");

        vm.ApplyPrimaryKey(entity, [entity.Columns[0]]);

        regionCode.IsPrimaryKey.Should().BeFalse();
        regionCode
            .IsNullable.Should()
            .BeFalse("主キーから外しただけで NULL 許容を変えてはならない");

        vm.UndoRedo.Undo();
        regionCode.IsPrimaryKey.Should().BeTrue();
        regionCode.IsNullable.Should().BeFalse();

        vm.UndoRedo.Redo();
        regionCode.IsPrimaryKey.Should().BeFalse();
        regionCode.IsNullable.Should().BeFalse();
    }

    [Fact(DisplayName = "同じ主キー構成・同じ順序の再適用では履歴を積まない")]
    public void ApplyPrimaryKey_NoChange_DoesNotPushHistory()
    {
        var vm = CreateViewModel();
        var entity = vm.SelectedEntity!;

        vm.ApplyPrimaryKey(entity, [entity.Columns[0], entity.Columns[1]]);

        vm.UndoRedo.CanUndo.Should().BeFalse();
    }

    /// <summary>
    /// 順序リストが空（＝列宣言順にまかせている）図へ、同じ並びを明示した場合は履歴を積むことを検証する。
    /// </summary>
    /// <remarks>
    /// 「実効順が同じかどうか」で早期 return すると、この<b>ピン留め</b>（以後の列並び替えで主キー順が
    /// ずれなくなる実変更）を取りこぼす。判定が保存リストと膜で行われていることを固定する。
    /// </remarks>
    [Fact(DisplayName = "順序未設定の図へ同じ並びを明示するとピン留めとして履歴を積む")]
    public void ApplyPrimaryKey_PinningSameEffectiveOrder_PushesHistory()
    {
        var vm = CreateViewModel(pinPrimaryKeyOrder: false);
        var entity = vm.SelectedEntity!;

        entity.PrimaryKeyColumnIds.Should().BeEmpty();
        Order(entity).Should().Equal("TenantId", "RegionCode");

        vm.ApplyPrimaryKey(entity, [entity.Columns[0], entity.Columns[1]]);

        vm.UndoRedo.CanUndo.Should().BeTrue();
        entity.PrimaryKeyColumnIds.Should().Equal(entity.Columns[0].Id, entity.Columns[1].Id);

        vm.UndoRedo.Undo();
        entity.PrimaryKeyColumnIds.Should().BeEmpty();
    }
}
