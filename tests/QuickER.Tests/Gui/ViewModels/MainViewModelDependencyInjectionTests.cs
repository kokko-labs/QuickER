using System.IO;
using AwesomeAssertions;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Services;
using QuickER.SqlServer;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// DI 注入された各ダイアログサービスをスタブへ差し替え、<see cref="MainViewModel"/> が
/// ウィンドウを一切表示せずにコマンドを実行できる（＝ View 層から分離されている）ことを検証する
/// </summary>
public class MainViewModelDependencyInjectionTests
{
    /// <summary>保存コマンドが、ファイル選択スタブの返すパスへ実際にドキュメントを書き出すことを検証する</summary>
    [Fact(DisplayName = "SaveCommand はファイル選択結果のパスへ保存する")]
    public void SaveCommand_WritesDocumentToPickedPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-di-{Guid.NewGuid()}.json");
        var files = new StubFileDialogService { SaveResult = new FileDialogResult(path, 1) };
        var vm = new MainViewModel(new StubDialogService(), new StubAppDialogService(), files);
        vm.AddEntityCommand.Execute(null);

        try
        {
            vm.SaveCommand.Execute(null);

            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>ファイル選択がキャンセル（null）された場合は何も書き出さないことを検証する</summary>
    [Fact(DisplayName = "SaveCommand はキャンセル時に保存しない")]
    public void SaveCommand_DoesNothing_WhenCancelled()
    {
        var files = new StubFileDialogService { SaveResult = null };
        var vm = new MainViewModel(new StubDialogService(), new StubAppDialogService(), files);
        vm.AddEntityCommand.Execute(null);

        var act = () => vm.SaveCommand.Execute(null);

        act.Should().NotThrow();
    }

    // ---------------- スタブ実装 ----------------
    // StubDialogService / StubFileDialogService は共有版（QuickER.Tests.TestDoubles）を使用する

    /// <summary>アプリ固有ダイアログを表示せず常にキャンセル相当を返すスタブ</summary>
    private sealed class StubAppDialogService : IAppDialogService
    {
        public PrintOptions? ShowPrintOptionsDialog(string? defaultTitle) => null;
    }
}
