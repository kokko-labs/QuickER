using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;
using FluentAssertions;

namespace QuickER.Tests.ViewModels;

/// <summary><see cref="SchemaSyncDialogViewModel"/> の差分選択・プレビュー生成・実行確認を検証するテストクラス</summary>
public class SchemaSyncDialogViewModelTests
{
    /// <summary>全選択が選択可能な差分のみを対象とし、案内項目を選択しないことを検証する</summary>
    [Fact(DisplayName = "全選択は選択可能な差分のみを対象にする")]
    public void SelectAll_SelectsOnlySelectableItems()
    {
        var vm = new SchemaSyncDialogViewModel(new SqlConnectionSettings(), [], []);
        vm.DiffItems.Add(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "Customer",
                Entity = new QuickER.Model.Entity
                {
                    TableName = "Customer",
                    Columns =
                    {
                        new QuickER.Model.Column
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                        },
                    },
                },

                IsSelected = false,
                IsSelectable = true,
            }
        );
        vm.DiffItems.Add(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.RebuildTable,
                TableName = "Order",
                Description = "列順変更は DB 同期しません: [Order]",
                IsSelected = false,
                IsSelectable = false,
            }
        );

        vm.SelectAllCommand.Execute(null);

        vm.DiffItems[0].IsSelected.Should().BeTrue();
        vm.DiffItems[1].IsSelected.Should().BeFalse();
    }

    /// <summary>全解除が選択不可の案内項目の状態を変更しないことを検証する</summary>
    [Fact(DisplayName = "全解除は選択不可の案内項目の状態を変更しない")]
    public void DeselectAll_DoesNotChangeNonSelectableItems()
    {
        var vm = new SchemaSyncDialogViewModel(new SqlConnectionSettings(), [], []);
        vm.DiffItems.Add(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "Customer",
                ColumnName = "Name",
                Column = new QuickER.Model.Column { Name = "Name", DataType = "nvarchar(50)" },
                IsSelected = true,
                IsSelectable = true,
            }
        );
        vm.DiffItems.Add(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.RebuildTable,
                TableName = "Order",
                Description = "列順変更は DB 同期しません: [Order]",
                IsSelected = false,
                IsSelectable = false,
            }
        );

        vm.DeselectAllCommand.Execute(null);

        vm.DiffItems[0].IsSelected.Should().BeFalse();
        vm.DiffItems[1].IsSelected.Should().BeFalse();
    }

    /// <summary>プレビュー生成が選択済みの通常差分のみを対象とし、案内項目を出力しないことを検証する</summary>
    [Fact(DisplayName = "スクリプト生成時は案内項目を選択していなくても通常差分のみが対象になる")]
    public void UpdatePreview_IgnoresNonSelectedInformationalItems()
    {
        var vm = new SchemaSyncDialogViewModel(new SqlConnectionSettings(), [], []);
        vm.DiffItems.Add(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "Customer",
                ColumnName = "Name",
                Column = new QuickER.Model.Column { Name = "Name", DataType = "nvarchar(50)" },
                IsSelected = true,
                IsSelectable = true,
            }
        );
        vm.DiffItems.Add(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.RebuildTable,
                TableName = "Order",
                Description = "列順変更は DB 同期しません: [Order]",
                IsSelected = false,
                IsSelectable = false,
            }
        );

        vm.UpdatePreview();

        vm.ScriptPreview.Should().Contain("ALTER TABLE [Customer] ADD [Name] nvarchar(50) NULL;");
        vm.ScriptPreview.Should().NotContain("RebuildTable");
        vm.ScriptPreview.Should().NotContain("列順変更は DB 同期しません");
    }

    /// <summary>実行時の警告確認でキャンセルすると、スクリプトを実行しないことを検証する</summary>
    [Fact(DisplayName = "Execute: 警告確認でキャンセルするとスクリプトは実行されない")]
    public async Task Execute_ConfirmDeclined_DoesNotRunScript()
    {
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new SchemaSyncDialogViewModel(new SqlConnectionSettings(), [], [], dialogs)
        {
            ScriptPreview = "DROP TABLE [X];",
        };

        await vm.ExecuteCommand.ExecuteAsync(null);

        dialogs.WarningConfirmMessages.Should().ContainSingle();
        vm.StatusMessage.Should().BeEmpty();
        vm.IsBusy.Should().BeFalse();
    }
}
