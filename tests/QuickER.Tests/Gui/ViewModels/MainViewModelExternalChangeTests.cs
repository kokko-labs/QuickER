using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 外部変更検知（ステージ B）の ViewModel 統合挙動を検証するテストクラス。
/// </summary>
/// <remarks>
/// 監視サービスの発火は <see cref="MainViewModel.RaiseExternalChangeForTests"/> で同期注入し、
/// FS タイミングに依存させず分岐を決定的に検証する（既定の _uiPost は同期実行）。
/// 実ファイル入出力は一時フォルダへ隔離する。
/// </remarks>
public sealed class MainViewModelExternalChangeTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-stageB-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelExternalChangeTests() => Directory.CreateDirectory(_folder);

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
            // 後始末失敗はテスト結果に影響させない
        }
    }

    private MainViewModel CreateIsolatedViewModel(
        StubDialogService dialogs,
        RecordingFileDialogService? files = null
    )
    {
        var vm = new MainViewModel(dialogs, files: files ?? new RecordingFileDialogService());
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        // 実 FileSystemWatcher を起動せず、外部変更は注入のみで決定的に検証する
        vm.DisableFileWatchingForTests();
        return vm;
    }

    /// <summary>指定内容の図を書き出してから、その図を開いた（現在パス紐付き・クリーン）VM を返す</summary>
    private MainViewModel OpenClean(string path, string tableName, StubDialogService dialogs)
    {
        WriteDiagram(path, tableName);
        var vm = CreateIsolatedViewModel(
            dialogs,
            new RecordingFileDialogService { OpenResult = new(path, 1) }
        );
        vm.OpenCommand.Execute(null);
        vm.IsDirty.Should().BeFalse();
        return vm;
    }

    /// <summary>単一テーブルの図をファイルへ書き出し、その内容ハッシュを返す（外部書き込みの模擬）</summary>
    private static string WriteDiagram(string path, string tableName)
    {
        var document = new DiagramDocument
        {
            Schema = new ErDiagram
            {
                Entities = { new Entity { TableName = tableName } },
                TargetDbms = "sqlserver",
            },
            Layout = null,
        };
        JsonStorageService.Save(path, document);
        return DocumentContentHash.TryCompute(path)!;
    }

    /// <summary>現在パスへ紐付けたクリーンな VM を作る（保存済み・ダーティなし）</summary>
    private MainViewModel CreateCleanBoundViewModel(string path, string tableName) =>
        OpenClean(path, tableName, new StubDialogService());

    /// <summary>クリーン時の外部変更は無確認で再読込し、ビューポート維持（fit 非発火）＋控えめ通知することを検証する</summary>
    [Fact(DisplayName = "クリーン: 外部変更を無確認で自動再読込する")]
    public void Clean_AutoReloadsWithoutConfirmation()
    {
        var path = Path.Combine(_folder, "Doc.json");
        var vm = CreateCleanBoundViewModel(path, "Original");
        vm.Entities.Should().ContainSingle(e => e.TableName == "Original");

        // fit-to-window が再読込経路で発火しない（ビューポート維持）ことを見張る
        var fitRequested = false;
        vm.FitToWindowRequested += (_, _) => fitRequested = true;

        var newHash = WriteDiagram(path, "External");
        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Modified, newHash);

        vm.Entities.Should().ContainSingle(e => e.TableName == "External", "外部内容を反映する");
        vm.IsDirty.Should().BeFalse("再読込後はクリーン");
        vm.UndoRedo.CanUndo.Should().BeFalse("再読込は履歴をクリアする");
        vm.StatusMessage.Should().Be(Strings.Status_ExternalReloaded);
        fitRequested.Should().BeFalse("再読込ではビューポートを維持し fit を発火しない");
    }

    /// <summary>ダーティ時の外部変更で「再読込」を選ぶと、未保存変更を破棄して外部内容を反映することを検証する</summary>
    [Fact(DisplayName = "ダーティ: 確認で再読込を選ぶと外部内容を反映する")]
    public void Dirty_ConfirmReload_AppliesExternalContent()
    {
        var path = Path.Combine(_folder, "Doc.json");
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = OpenClean(path, "Original", dialogs);

        // 未保存編集でダーティにする
        vm.AddEntityCommand.Execute(null);
        vm.IsDirty.Should().BeTrue();

        var newHash = WriteDiagram(path, "External");
        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Modified, newHash);

        dialogs.WarningConfirmMessages.Should().ContainSingle();
        vm.Entities.Should().ContainSingle(e => e.TableName == "External");
        vm.IsDirty.Should().BeFalse("再読込後はクリーン");
    }

    /// <summary>クエリ差し替え（Undo 非対象の変更）だけでもダーティ扱いになり、確認を経ることを検証する</summary>
    /// <remarks>
    /// クエリは Undo 履歴に積まれないが保存文書の一部なので、無確認の自動再読込に流れると
    /// 定義が無警告で失われる。ここではその防止（確認ダイアログ経由になること）を見張る。
    /// </remarks>
    [Fact(DisplayName = "ダーティ: クエリ差し替えだけでも外部変更を確認する")]
    public void QueryReplacement_MakesExternalChangeConfirmed()
    {
        var path = Path.Combine(_folder, "Doc.json");
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = OpenClean(path, "Original", dialogs);

        // エンティティ編集は行わず、名前付きクエリの差し替えだけでダーティにする
        vm.ReplaceQueries([new QueryDefinition { EntityId = vm.Entities[0].Id, Name = "GetAll" }]);
        vm.IsDirty.Should().BeTrue();

        var externalHash = WriteDiagram(path, "External");
        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Modified, externalHash);

        dialogs.WarningConfirmMessages.Should().ContainSingle("無確認の自動再読込へ流さない");
        vm.Queries.Should().ContainSingle("続行を選んだのでクエリは保持される");
    }

    /// <summary>ダーティ時に「続行」を選ぶと未保存変更を保持し、同一内容では再確認しないことを検証する</summary>
    [Fact(DisplayName = "ダーティ: 続行を選ぶと変更を保持し同一内容は再確認しない")]
    public void Dirty_Continue_KeepsChangesAndSuppressesReconfirm()
    {
        var path = Path.Combine(_folder, "Doc.json");
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = OpenClean(path, "Original", dialogs);
        vm.AddEntityCommand.Execute(null);

        var externalHash = WriteDiagram(path, "External");

        // 1 回目: 続行を選ぶ → 変更を保持し再読込しない
        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Modified, externalHash);
        dialogs.WarningConfirmMessages.Should().ContainSingle();
        vm.Entities.Should().Contain(e => e.TableName == "Original");
        vm.IsDirty.Should().BeTrue();

        // 2 回目: 同一内容ハッシュ → 再確認しない（抑止）
        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Modified, externalHash);
        dialogs.WarningConfirmMessages.Should().ContainSingle("同一内容では再確認しない");

        // 3 回目: 別内容ハッシュ → 再び確認する
        var otherHash = WriteDiagram(path, "External2");
        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Modified, otherHash);
        dialogs.WarningConfirmMessages.Should().HaveCount(2, "別内容では再確認する");
    }

    /// <summary>破損／非 DiagramDocument への外部変更は再読込せず現状維持し、失敗を通知することを検証する</summary>
    [Fact(DisplayName = "破損: 非文書への外部変更は現状維持する")]
    public void Corrupt_KeepsCurrentDiagram()
    {
        var path = Path.Combine(_folder, "Doc.json");
        var vm = CreateCleanBoundViewModel(path, "Keep");

        // 図として妥当でない JSON（Version/Schema を欠く）で上書きする
        File.WriteAllText(path, "{\"unrelated\":true}");
        var garbageHash = DocumentContentHash.TryCompute(path)!;

        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Modified, garbageHash);

        vm.Entities.Should().ContainSingle(e => e.TableName == "Keep", "破損時は現状維持");
        vm.StatusMessage.Should().Be(Strings.Status_ExternalReloadFailed);
    }

    /// <summary>削除検知はステータス通知のみで、現在パス・図を維持することを検証する</summary>
    [Fact(DisplayName = "削除: 通知のみで現状維持する")]
    public void Deleted_NotifiesOnlyAndKeepsState()
    {
        var path = Path.Combine(_folder, "Doc.json");
        var vm = CreateCleanBoundViewModel(path, "Alive");

        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Deleted, null);

        vm.CurrentFilePath.Should().Be(path, "削除でもパスは維持する");
        vm.Entities.Should().ContainSingle(e => e.TableName == "Alive");
        vm.StatusMessage.Should().Be(Strings.Status_ExternalFileDeleted);
    }

    /// <summary>無題（現在パスなし）では外部変更イベントを無視することを検証する（監視しない契約の観測面）</summary>
    [Fact(DisplayName = "無題: パス不一致の外部変更は無視する")]
    public void Untitled_IgnoresExternalEvents()
    {
        var vm = CreateIsolatedViewModel(new StubDialogService());
        vm.CurrentFilePath.Should().BeNull();
        vm.AddEntityCommand.Execute(null);

        vm.RaiseExternalChangeForTests(
            DocumentFileChangeKind.Modified,
            "deadbeef",
            path: Path.Combine(_folder, "Other.json")
        );

        vm.StatusMessage.Should().Be(Strings.Status_Ready, "無題・パス不一致では何もしない");
    }

    /// <summary>起動時チェック: 復元がクリーンで現ファイルが変わっていれば自動再読込することを検証する</summary>
    [Fact(DisplayName = "起動時: クリーン復元で外部変更があれば自動再読込する")]
    public void Startup_Clean_AutoReloads()
    {
        var path = Path.Combine(_folder, "Startup.json");
        var autoSave = Path.Combine(_folder, "last_diagram.json");

        // vm1: 保存してクリーン→自動保存メタ（パス・ハッシュ）を永続化
        var vm1 = new MainViewModel(
            new StubDialogService(),
            files: new RecordingFileDialogService { SaveResult = new(path, 1) }
        );
        vm1.UsePersistenceForTests(new GuiAppSettingsStore(_folder), autoSave);
        vm1.DisableFileWatchingForTests();
        vm1.AddEntityCommand.Execute(null);
        vm1.SaveCommand.Execute(null);
        vm1.AutoSave();

        // 外部でファイルを差し替える（vm1 保存後）
        WriteDiagram(path, "ExternalAtStartup");

        // vm2: 復元→起動時チェックで外部変更を検知し自動再読込
        var vm2 = CreateIsolatedViewModel(new StubDialogService());
        vm2.Initialize();

        vm2.CurrentFilePath.Should().Be(path);
        vm2.Entities.Should().ContainSingle(e => e.TableName == "ExternalAtStartup");
        vm2.IsDirty.Should().BeFalse();
    }

    /// <summary>起動時チェック: 復元がダーティなら確認ダイアログを出すことを検証する</summary>
    [Fact(DisplayName = "起動時: ダーティ復元で外部変更があれば確認する")]
    public void Startup_Dirty_Confirms()
    {
        var path = Path.Combine(_folder, "StartupDirty.json");
        var autoSave = Path.Combine(_folder, "last_diagram.json");

        var vm1 = new MainViewModel(
            new StubDialogService(),
            files: new RecordingFileDialogService { SaveResult = new(path, 1) }
        );
        vm1.UsePersistenceForTests(new GuiAppSettingsStore(_folder), autoSave);
        vm1.DisableFileWatchingForTests();
        vm1.AddEntityCommand.Execute(null);
        vm1.SaveCommand.Execute(null);
        // 保存後に編集して未保存の変更を作る（復元でダーティ引継ぎ）
        vm1.AddEntityCommand.Execute(null);
        vm1.AutoSave();

        WriteDiagram(path, "ExternalAtStartup");

        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm2 = CreateIsolatedViewModel(dialogs);
        vm2.Initialize();

        vm2.IsDirty.Should().BeTrue("復元ダーティを引き継ぐ");
        dialogs.WarningConfirmMessages.Should().ContainSingle("起動時のダーティ外部変更は確認する");
    }
}
