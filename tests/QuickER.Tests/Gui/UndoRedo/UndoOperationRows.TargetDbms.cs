using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestDoubles;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class UndoOperationRows
{
    /// <summary>
    /// 対象 DBMS の切り替え（画面）：型の変換と方言の切り替えが 1 つの履歴になり、Undo / Redo で方言と列の型が戻る
    /// （<c>MainViewModelTargetDbmsTests</c> の実プロバイダ 2 つの構成）
    /// </summary>
    private static IEnumerable<UndoRow> TargetDbmsRows()
    {
        yield return Row(
                "target-dbms-change-pushes-one-command-and-round-trips-types",
                "対象 DBMS の切り替えは ChangeTargetDbmsCommand を 1 件積み、確認で続行すると、Undo / Redo で方言と列の型が戻る"
            )
            .Diagram(TargetDbmsDiagram)
            .Operate(
                UndoEntry.Screen,
                "SelectedProvider へ SQLite を選ぶ（NOT NULL 解除の確認には「はい」）",
                vm => vm.SelectedProvider = Recall<IDatabaseProvider>(vm, "sqlite")
            )
            .Commands(typeof(ChangeTargetDbmsCommand))
            .Check(
                UndoStage.Operated,
                "方言が SQLite になり、行バージョン列の型が変わり、確認は 1 回だけ出る",
                vm =>
                {
                    vm.CurrentProvider.Name.Should().Be(DialectNames.Sqlite);
                    ColumnNamed(vm, "T", "RowVer").DataType.Should().NotBe("rowversion");
                    Recall<StubDialogService>(vm, "dialogs")
                        .WarningConfirmDetailsMessages.Should()
                        .ContainSingle();
                }
            )
            .Check(
                UndoStage.Undone,
                "方言と行バージョン列の型が戻る",
                vm =>
                {
                    vm.CurrentProvider.Name.Should().Be(DialectNames.SqlServer);
                    ColumnNamed(vm, "T", "RowVer").DataType.Should().Be("rowversion");
                }
            )
            .Check(
                UndoStage.Redone,
                "方言と型が切り替え後へ戻る",
                vm =>
                {
                    vm.CurrentProvider.Name.Should().Be(DialectNames.Sqlite);
                    ColumnNamed(vm, "T", "RowVer").DataType.Should().NotBe("rowversion");
                }
            );
    }

    /// <summary>
    /// SQL Server と SQLite の実プロバイダを登録した、表 T（Id＝主キー・主キーでない行バージョン列 RowVer）の図
    /// （確認は <see cref="StubDialogService"/> が「はい」を返す。SQLite への切り替えは行バージョン列の NOT NULL 解除の確認を伴う）
    /// </summary>
    private static MainViewModel TargetDbmsDiagram()
    {
        var sqlite = new SqliteProvider();
        var dialogs = new StubDialogService();
        var vm = new MainViewModel(
            dialogs,
            providers: new DatabaseProviderRegistry([new SqlServerProvider(), sqlite])
        );
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                TargetDbms = DialectNames.SqlServer,
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
                            new Column
                            {
                                Name = "RowVer",
                                DataType = "rowversion",
                                IsNullable = false,
                            },
                        },
                    },
                },
            }
        );
        Remember(vm, "sqlite", sqlite);
        Remember(vm, "dialogs", dialogs);

        return vm;
    }
}
