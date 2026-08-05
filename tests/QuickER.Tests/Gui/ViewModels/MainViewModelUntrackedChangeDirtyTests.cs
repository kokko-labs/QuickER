using System.IO;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// Undo 履歴へ積まない変更（名前付きクエリの差し替え・エンティティ表示幅）が
/// ダーティ判定（<see cref="MainViewModel.IsDirty"/>）へ正しく乗ることを検証するテストクラス。
/// </summary>
/// <remarks>
/// どちらも保存文書の一部（クエリはスキーマ、幅はレイアウト）なので、ダーティにならないと
/// 外部変更の無確認自動再読込・新規作成・Open で無警告に失われる。
/// 永続化先は一時フォルダへ隔離し、実 FileSystemWatcher は起動しない。
/// </remarks>
public class MainViewModelUntrackedChangeDirtyTests : IDisposable
{
    /// <summary>テスト専用の一時作業フォルダ（各テストで独立・後始末で削除する）</summary>
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-untracked-dirty-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelUntrackedChangeDirtyTests() => Directory.CreateDirectory(_folder);

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

    /// <summary>エンティティ 1 個を保存済み（現在パス紐付き・クリーン）にした VM を返す</summary>
    private MainViewModel CreateCleanSavedViewModel()
    {
        var path = Path.Combine(_folder, "Doc.json");
        var vm = new MainViewModel(
            new StubDialogService(),
            files: new RecordingFileDialogService { SaveResult = new(path, 1) }
        );
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();

        vm.AddEntityCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        vm.IsDirty.Should().BeFalse("保存直後はクリーン");

        return vm;
    }

    /// <summary>クエリ差し替えがダーティ扱いになり、タイトルへ * が付くことを検証する</summary>
    [Fact(DisplayName = "ReplaceQueries: クエリ差し替えでダーティになる")]
    public void ReplaceQueries_MarksDirty()
    {
        var vm = CreateCleanSavedViewModel();

        vm.ReplaceQueries([new QueryDefinition { EntityId = vm.Entities[0].Id, Name = "GetAll" }]);

        vm.IsDirty.Should().BeTrue("クエリは保存文書の一部なので未保存変更になる");
        vm.WindowTitle.Should().Contain("*");
    }

    /// <summary>クエリ差し替え後に保存するとクリーンへ戻ることを検証する</summary>
    [Fact(DisplayName = "ReplaceQueries: 保存すると再びクリーンになる")]
    public void ReplaceQueries_ThenSave_BecomesClean()
    {
        var vm = CreateCleanSavedViewModel();
        vm.ReplaceQueries([new QueryDefinition { EntityId = vm.Entities[0].Id, Name = "GetAll" }]);

        vm.SaveCommand.Execute(null);

        vm.IsDirty.Should().BeFalse();
        vm.WindowTitle.Should().NotContain("*");
    }

    /// <summary>幅自動調整で幅が実際に変わったときはダーティ扱いになることを検証する</summary>
    [Fact(DisplayName = "幅自動調整: 幅が変わるとダーティになる")]
    public void AutoFitEntityWidths_WhenWidthChanges_MarksDirty()
    {
        var vm = CreateCleanSavedViewModel();

        // 幅は Undo 非対象なので、直接書き換えただけではダーティにならない（前提の確認）
        vm.Entities[0].Width = 999;
        vm.IsDirty.Should().BeFalse();

        vm.AutoFitEntityWidthsCommand.Execute(null);

        vm.Entities[0].Width.Should().NotBe(999, "内容に合う幅へ調整される");
        vm.IsDirty.Should().BeTrue("幅は保存対象（EntityLayout.Width）なので未保存変更になる");
    }

    /// <summary>幅自動調整で幅が 1 つも変わらなければクリーンのままであることを検証する</summary>
    [Fact(DisplayName = "幅自動調整: 幅が変わらなければクリーンのまま")]
    public void AutoFitEntityWidths_WhenNothingChanges_StaysClean()
    {
        var vm = CreateCleanSavedViewModel();

        // 1 回目で自動幅へ揃えたうえで保存し、クリーンな状態を作る
        vm.AutoFitEntityWidthsCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        vm.IsDirty.Should().BeFalse();

        // 2 回目は幅が動かない（自動幅は内容だけで決まる冪等な計算）
        vm.AutoFitEntityWidthsCommand.Execute(null);

        vm.IsDirty.Should().BeFalse("無変更の自動調整でダーティにはしない");
    }
}
