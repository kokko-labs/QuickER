using System.IO;
using AwesomeAssertions;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 自動保存の図（last_diagram.json）と文書メタ（gui-settings.json）が食い違ったまま復元されないことを固定する
/// </summary>
/// <remarks>
/// 自動保存は 2 つのファイルへ別々に書くので、メタだけが書けない・2 つの書き込みの間で落ちると、別の図の内容に
/// 前のファイルのパスが付く。そのまま復元すると、上書き保存が前のファイルを確認なしに別の図で上書きする。
/// メタは一緒に書いた図の内容のハッシュを持ち、起動時に照らし合わせる（<see cref="CurrentDocumentSettings.AutoSaveHash"/>）。
/// </remarks>
public sealed class MainViewModelAutoSaveMetaTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "QuickERTests",
        "autosave-meta-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelAutoSaveMetaTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // 後始末の失敗は無視する
        }
    }

    private string AutoSavePath => Path.Combine(_folder, "last_diagram.json");

    private string PathA => Path.Combine(_folder, "A.json");

    private string PathB => Path.Combine(_folder, "B.json");

    /// <summary>テーブル 1 つの図を保存した 2 つ目のファイル（B）を作る</summary>
    private void CreateFileB()
    {
        var writer = new MainViewModel(
            new StubDialogService(),
            files: new RecordingFileDialogService { SaveResult = new(PathB, 1) }
        );
        writer.UsePersistenceForTests(
            new GuiAppSettingsStore(Path.Combine(_folder, "other")),
            Path.Combine(_folder, "other.json")
        );
        writer.DisableFileWatchingForTests();
        writer.AddEntityCommand.Execute(null);
        writer.Entities[0].TableName = "OnlyInB";
        writer.SaveCommand.Execute(null);
    }

    /// <summary>A を保存して自動保存し（メタ＝A）、続けて B を開いた状態の VM を返す</summary>
    private MainViewModel SaveAThenOpenB()
    {
        CreateFileB();
        var vm = new MainViewModel(
            new StubDialogService(),
            files: new RecordingFileDialogService
            {
                SaveResult = new(PathA, 1),
                OpenResult = new(PathB, 1),
            }
        );
        vm.UsePersistenceForTests(new GuiAppSettingsStore(_folder), AutoSavePath);
        vm.DisableFileWatchingForTests();
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0].TableName = "OnlyInA";
        vm.SaveCommand.Execute(null);
        vm.AutoSave();
        vm.OpenCommand.Execute(null);
        vm.CurrentFilePath.Should().Be(PathB);
        return vm;
    }

    private MainViewModel Restart(StubDialogService dialogs, RecordingFileDialogService files)
    {
        var vm = new MainViewModel(dialogs, files: files);
        vm.UsePersistenceForTests(new GuiAppSettingsStore(_folder), AutoSavePath);
        vm.DisableFileWatchingForTests();
        vm.Initialize();
        return vm;
    }

    [Fact(
        DisplayName = "メタだけ書けなかった自動保存は、無題・未保存として復元し、上書き保存で前のファイルを上書きしない"
    )]
    public void SettingsWriteFails_RestoresUntitled_AndDoesNotOverwritePreviousFile()
    {
        var vm = SaveAThenOpenB();
        var originalA = File.ReadAllText(PathA);

        // 終了時の自動保存で gui-settings.json だけが書けない（図は B の内容・メタは A のまま）
        using (
            new FileStream(
                Path.Combine(_folder, GuiAppSettingsStore.DefaultFileName),
                FileMode.Open,
                FileAccess.Read,
                FileShare.None
            )
        )
        {
            vm.AutoSave();
        }

        var dialogs = new StubDialogService();
        var files = new RecordingFileDialogService();
        var restored = Restart(dialogs, files);

        restored.Entities.Select(e => e.TableName).Should().Equal("OnlyInB");
        restored.CurrentFilePath.Should().BeNull("メタのパスは別の図のものなので信じない");
        restored.IsDirty.Should().BeTrue("ファイルへ保存されていない内容として開く");
        restored.StatusMessage.Should().Be(Strings.Status_AutoSaveRestoredUntitled);

        // 上書き保存は名前を付けて保存になる（キャンセル）＝A は書き換わらない
        restored.SaveCommand.Execute(null);

        files.SaveDialogCallCount.Should().Be(1);
        File.ReadAllText(PathA).Should().Be(originalA);
    }

    [Fact(DisplayName = "図とメタの両方を書けた自動保存は、ファイルのパスを付けて復元する")]
    public void BothWritten_RestoresFilePath()
    {
        var vm = SaveAThenOpenB();
        vm.AutoSave();

        var restored = Restart(new StubDialogService(), new RecordingFileDialogService());

        restored.CurrentFilePath.Should().Be(PathB);
        restored.IsDirty.Should().BeFalse();
        restored.Entities.Select(e => e.TableName).Should().Equal("OnlyInB");
    }

    [Fact(DisplayName = "図のハッシュを持たない旧版のメタは、照合せずに信じる")]
    public void LegacyMetaWithoutHash_IsTrusted()
    {
        var vm = SaveAThenOpenB();
        vm.AutoSave();
        var store = new GuiAppSettingsStore(_folder);
        var settings = store.Load();
        settings.CurrentDocument.AutoSaveHash = null;
        store.Save(settings).Should().BeTrue();

        var restored = Restart(new StubDialogService(), new RecordingFileDialogService());

        restored.CurrentFilePath.Should().Be(PathB);
        restored.IsDirty.Should().BeFalse();
    }
}
