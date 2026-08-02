using System.IO;
using System.Security.Cryptography;
using AwesomeAssertions;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// document-based 化（現在ファイルパス・上書き保存／別名保存・ダーティ追跡・復元メタ）の挙動を検証するテストクラス。
/// </summary>
/// <remarks>
/// ファイル入出力を伴うため実ファイル（一時ディレクトリ）を使い、復元メタの永続化は
/// <see cref="MainViewModel.UsePersistenceForTests"/> で一時フォルダへ隔離して実 AppData を汚さない。
/// </remarks>
public class MainViewModelDocumentTests : IDisposable
{
    /// <summary>テスト専用の一時作業フォルダ（各テストで独立・後始末で削除する）</summary>
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-stageA-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelDocumentTests()
    {
        Directory.CreateDirectory(_folder);
    }

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

    /// <summary>永続化先を一時フォルダへ隔離した VM を生成する</summary>
    private MainViewModel CreateIsolatedViewModel(IFileDialogService files)
    {
        var vm = new MainViewModel(new StubDialogService(), files: files);
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        return vm;
    }

    /// <summary>現在パスがあれば保存ダイアログを出さず、そのパスへ上書き保存することを検証する</summary>
    [Fact(DisplayName = "Save: 現在パスありなら無ダイアログで上書き保存する")]
    public void Save_WithCurrentPath_OverwritesWithoutDialog()
    {
        var path = Path.Combine(_folder, "Doc.json");
        var files = new RecordingFileDialogService
        {
            // 呼ばれたら誤りと分かるよう、別パスをダイアログ結果として仕込む
            SaveResult = new(Path.Combine(_folder, "WRONG.json"), 1),
        };
        var vm = CreateIsolatedViewModel(files);
        vm.CurrentFilePath = path;
        vm.AddEntityCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        files.SaveDialogCallCount.Should().Be(0, "現在パスありの保存はダイアログを開かない");
        File.Exists(path).Should().BeTrue("現在パスへ上書き保存される");
        File.Exists(Path.Combine(_folder, "WRONG.json")).Should().BeFalse();
        vm.CurrentFilePath.Should().Be(path);
    }

    /// <summary>無題（現在パスなし）での保存は従来どおりダイアログを開き、成功時にパスを設定することを検証する</summary>
    [Fact(DisplayName = "Save: 無題ならダイアログを開き、成功でパスを設定する")]
    public void Save_Untitled_FallsBackToDialogAndSetsPath()
    {
        var path = Path.Combine(_folder, "New.json");
        var files = new RecordingFileDialogService { SaveResult = new(path, 1) };
        var vm = CreateIsolatedViewModel(files);
        vm.CurrentFilePath.Should().BeNull();
        vm.AddEntityCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        files.SaveDialogCallCount.Should().Be(1, "無題保存はダイアログを開く");
        vm.CurrentFilePath.Should().Be(path);
        File.Exists(path).Should().BeTrue();
        vm.IsDirty.Should().BeFalse("保存直後はクリーン");
    }

    /// <summary>別名保存は現在パスの有無に依らず常にダイアログを開き、パスを更新することを検証する</summary>
    [Fact(DisplayName = "SaveAs: 現在パスありでも常にダイアログを開きパスを更新する")]
    public void SaveAs_AlwaysOpensDialogAndUpdatesPath()
    {
        var original = Path.Combine(_folder, "Original.json");
        var renamed = Path.Combine(_folder, "Renamed.json");
        var files = new RecordingFileDialogService { SaveResult = new(renamed, 1) };
        var vm = CreateIsolatedViewModel(files);
        vm.CurrentFilePath = original;
        vm.AddEntityCommand.Execute(null);

        vm.SaveAsCommand.Execute(null);

        files.SaveDialogCallCount.Should().Be(1, "別名保存は常にダイアログを開く");
        vm.CurrentFilePath.Should().Be(renamed);
        File.Exists(renamed).Should().BeTrue();
    }

    /// <summary>ファイルを開くと現在パスが設定され、ウィンドウタイトルへ反映されることを検証する</summary>
    [Fact(DisplayName = "Open: 現在パスを設定しクリーン状態にする")]
    public void Open_SetsCurrentPathAndCleanState()
    {
        // 先に保存でファイルを作る
        var path = Path.Combine(_folder, "ToOpen.json");
        var writer = CreateIsolatedViewModel(
            new RecordingFileDialogService { SaveResult = new(path, 1) }
        );
        writer.AddEntityCommand.Execute(null);
        writer.SaveCommand.Execute(null);

        var vm = CreateIsolatedViewModel(
            new RecordingFileDialogService { OpenResult = new(path, 1) }
        );
        vm.OpenCommand.Execute(null);

        vm.CurrentFilePath.Should().Be(path);
        vm.IsDirty.Should().BeFalse();
        vm.WindowTitle.Should().Be("ToOpen - QuickER");
    }

    /// <summary>モジュール置換（DB 取込・AI 生成など）では現在パスを維持し、内容変更でダーティになることを検証する</summary>
    [Fact(DisplayName = "置換: 現在パスを維持しダーティになる")]
    public void ReplaceFromModule_KeepsPathAndBecomesDirty()
    {
        var path = Path.Combine(_folder, "Kept.json");
        var vm = CreateIsolatedViewModel(new RecordingFileDialogService());
        vm.CurrentFilePath = path;
        // 現在パスをクリーン基準にする（Open 相当の状態を再現）
        vm.SaveCommand.Execute(null);
        vm.IsDirty.Should().BeFalse();

        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities = { new Entity { TableName = "T" } },
                TargetDbms = "sqlserver",
            }
        );

        vm.CurrentFilePath.Should().Be(path, "置換ではパスを維持する（無題化しない）");
        vm.IsDirty.Should().BeTrue("置換は内容変更なのでダーティになる");
    }

    /// <summary>Mermaid インポートでも現在パスを維持することを検証する</summary>
    [Fact(DisplayName = "インポート: 現在パスを維持する")]
    public void Import_KeepsCurrentPath()
    {
        var mermaidPath = Path.Combine(_folder, "diagram.mmd");
        File.WriteAllText(
            mermaidPath,
            string.Join(
                Environment.NewLine,
                "erDiagram",
                "    Customer {",
                "        int CustomerId PK",
                "    }"
            )
        );

        var currentPath = Path.Combine(_folder, "Current.json");
        var vm = CreateIsolatedViewModel(
            new RecordingFileDialogService { OpenResult = new(mermaidPath, 1) }
        );
        vm.CurrentFilePath = currentPath;

        vm.ImportDiagramCommand.Execute(null);

        vm.Entities.Should().ContainSingle();
        vm.CurrentFilePath.Should().Be(currentPath, "インポートではパスを維持する");
    }

    /// <summary>読込直後はクリーン→編集でダーティ→上書き保存で再びクリーンになり、タイトルの * が連動することを検証する</summary>
    [Fact(DisplayName = "ダーティ: 保存でクリーン、編集で * 付き、上書きで * 解消")]
    public void DirtyLifecycle_TracksEditsAndSave()
    {
        var path = Path.Combine(_folder, "Life.json");
        var vm = CreateIsolatedViewModel(
            new RecordingFileDialogService { SaveResult = new(path, 1) }
        );
        vm.AddEntityCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        vm.IsDirty.Should().BeFalse();
        vm.WindowTitle.Should().Be("Life - QuickER");

        // 編集するとダーティ・タイトルに * が付く
        vm.AddEntityCommand.Execute(null);
        vm.IsDirty.Should().BeTrue();
        vm.WindowTitle.Should().Be("Life* - QuickER");

        // 上書き保存でクリーンへ戻り * が消える
        vm.SaveCommand.Execute(null);
        vm.IsDirty.Should().BeFalse();
        vm.WindowTitle.Should().Be("Life - QuickER");
    }

    /// <summary>無題文書は編集でダーティでも、保存先が無いためタイトルに * を出さない（QuickER のまま）ことを検証する</summary>
    [Fact(DisplayName = "ダーティ: 無題は編集しても * を出さない")]
    public void Dirty_Untitled_ShowsNoStar()
    {
        var vm = CreateIsolatedViewModel(new RecordingFileDialogService());

        vm.AddEntityCommand.Execute(null);

        vm.IsDirty.Should().BeTrue();
        vm.WindowTitle.Should().Be("QuickER");
    }

    /// <summary>自動保存が現在パス・内容ハッシュ・ダーティを復元メタへ書き出すことを検証する</summary>
    [Fact(DisplayName = "復元メタ: 保存時にパス・ハッシュ・ダーティを永続化する")]
    public void AutoSave_PersistsDocumentMeta()
    {
        var path = Path.Combine(_folder, "Meta.json");
        var store = new GuiAppSettingsStore(_folder);
        var vm = new MainViewModel(
            new StubDialogService(),
            files: new RecordingFileDialogService { SaveResult = new(path, 1) }
        );
        vm.UsePersistenceForTests(store, Path.Combine(_folder, "last_diagram.json"));
        vm.AddEntityCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        vm.AutoSave();

        var meta = store.Load().CurrentDocument;
        meta.FilePath.Should().Be(path);
        meta.IsDirty.Should().BeFalse();

        // ハッシュは保存したファイル内容の SHA-256（16 進）と一致する
        var expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        meta.LastKnownHash.Should().Be(expected);
    }

    /// <summary>次回起動の復元でパス・タイトルが戻り、クリーン状態が引き継がれることを検証する</summary>
    [Fact(DisplayName = "復元: パス・タイトル・クリーン状態を引き継ぐ")]
    public void Restore_RecoversPathTitleAndCleanState()
    {
        var path = Path.Combine(_folder, "Persist.json");
        var autoSave = Path.Combine(_folder, "last_diagram.json");

        var vm1 = new MainViewModel(
            new StubDialogService(),
            files: new RecordingFileDialogService { SaveResult = new(path, 1) }
        );
        vm1.UsePersistenceForTests(new GuiAppSettingsStore(_folder), autoSave);
        vm1.AddEntityCommand.Execute(null);
        vm1.SaveCommand.Execute(null);
        vm1.AutoSave();

        var vm2 = new MainViewModel();
        vm2.UsePersistenceForTests(new GuiAppSettingsStore(_folder), autoSave);
        vm2.Initialize();

        vm2.CurrentFilePath.Should().Be(path);
        vm2.IsDirty.Should().BeFalse();
        vm2.WindowTitle.Should().Be("Persist - QuickER");
    }

    /// <summary>前回終了時に未保存だった場合、復元後もダーティ状態（タイトルの *）が引き継がれることを検証する</summary>
    [Fact(DisplayName = "復元: 前回未保存のダーティ状態を引き継ぐ")]
    public void Restore_CarriesOverDirtyState()
    {
        var path = Path.Combine(_folder, "Dirty.json");
        var autoSave = Path.Combine(_folder, "last_diagram.json");

        var vm1 = new MainViewModel(
            new StubDialogService(),
            files: new RecordingFileDialogService { SaveResult = new(path, 1) }
        );
        vm1.UsePersistenceForTests(new GuiAppSettingsStore(_folder), autoSave);
        vm1.AddEntityCommand.Execute(null);
        vm1.SaveCommand.Execute(null);
        // 保存後に編集して未保存の変更を作る
        vm1.AddEntityCommand.Execute(null);
        vm1.IsDirty.Should().BeTrue();
        vm1.AutoSave();

        var vm2 = new MainViewModel();
        vm2.UsePersistenceForTests(new GuiAppSettingsStore(_folder), autoSave);
        vm2.Initialize();

        vm2.CurrentFilePath.Should().Be(path);
        vm2.IsDirty.Should().BeTrue("前回未保存の状態を引き継ぐ");
        vm2.WindowTitle.Should().Be("Dirty* - QuickER");
    }
}
