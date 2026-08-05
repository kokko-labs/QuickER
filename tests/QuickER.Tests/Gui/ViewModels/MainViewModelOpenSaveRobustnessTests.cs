using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 図の読込・保存の堅牢化を検証するテストクラス。
/// </summary>
/// <remarks>
/// 検証する不変条件は 3 つ。
/// <list type="bullet">
/// <item>Open は破損 JSON・無関係な JSON を拒否し、現在の図と現在パスを一切変えない（無言の全消し・誤紐付けの防止）</item>
/// <item>Open は図を失う他の経路と同じく未保存確認（ConfirmDiscard）を通す（空でクリーンなときだけ無確認）</item>
/// <item>保存に失敗したらエラー通知のうえダーティのまま保持する（保存できていないのにクリーン扱いにしない）</item>
/// </list>
/// 実ファイル入出力は一時フォルダへ隔離し、復元メタの永続化先も
/// <see cref="MainViewModel.UsePersistenceForTests"/> で差し替えて実 AppData を汚さない。
/// </remarks>
public sealed class MainViewModelOpenSaveRobustnessTests : IDisposable
{
    /// <summary>テスト専用の一時作業フォルダ（各テストで独立・後始末で削除する）</summary>
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-openrobust-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelOpenSaveRobustnessTests() => Directory.CreateDirectory(_folder);

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

    /// <summary>永続化先を一時フォルダへ隔離し、実 FileSystemWatcher を起動しない VM を生成する</summary>
    private MainViewModel CreateIsolatedViewModel(
        StubDialogService dialogs,
        IFileDialogService files
    )
    {
        var vm = new MainViewModel(dialogs, files: files);
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        // 外部変更検知はこのテストの関心外。実監視を止めて FS タイミングに依存させない
        vm.DisableFileWatchingForTests();
        return vm;
    }

    /// <summary>単一テーブルの正当な図ファイルを書き出す</summary>
    private string WriteValidDiagram(string fileName, string tableName)
    {
        var path = Path.Combine(_folder, fileName);
        JsonStorageService.Save(
            path,
            new DiagramDocument
            {
                Schema = new ErDiagram
                {
                    Entities = { new Entity { TableName = tableName } },
                    TargetDbms = "sqlserver",
                },
                Layout = null,
            }
        );
        return path;
    }

    /// <summary>指定内容のテキストファイルを書き出す（不正な図ファイルの模擬）</summary>
    private string WriteText(string fileName, string content)
    {
        var path = Path.Combine(_folder, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    // ---------------- Open: 不正なファイルの拒否 ----------------

    /// <summary>壊れた JSON を開いても例外で落ちず、エラー通知のみで現在の図・現在パスが維持されることを検証する</summary>
    [Fact(DisplayName = "Open: 壊れた JSON は落ちずにエラー通知し現状維持する")]
    public void Open_CorruptedJson_ShowsErrorAndKeepsCurrentState()
    {
        var corrupted = WriteText("Broken.json", "{ \"Version\": 1, \"Schema\": {");
        var keptPath = Path.Combine(_folder, "Kept.json");
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = CreateIsolatedViewModel(
            dialogs,
            new RecordingFileDialogService { OpenResult = new(corrupted, 1) }
        );
        vm.AddEntityCommand.Execute(null);
        vm.CurrentFilePath = keptPath;

        vm.OpenCommand.Execute(null);

        dialogs.ErrorMessages.Should().ContainSingle().Which.Should().Contain(corrupted);
        vm.Entities.Should().ContainSingle("読込に失敗したら現在の図を変えない");
        vm.CurrentFilePath.Should().Be(keptPath, "失敗したファイルへ紐付け直さない");
        vm.IsDirty.Should().BeTrue("未保存の変更はそのまま残る");
    }

    /// <summary>図ではない無関係な JSON は「空の図」として取り込まず、現状維持で拒否されることを検証する</summary>
    [Fact(DisplayName = "Open: 無関係な JSON は空の図として取り込まず拒否する")]
    public void Open_UnrelatedJson_IsRejectedAndKeepsCurrentState()
    {
        // package.json のような無関係な JSON。既定値で吸収すると図が無言で全消えする
        var unrelated = WriteText("package.json", "{\"name\":\"x\",\"version\":\"1.0.0\"}");
        var keptPath = Path.Combine(_folder, "Kept.json");
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = CreateIsolatedViewModel(
            dialogs,
            new RecordingFileDialogService { OpenResult = new(unrelated, 1) }
        );
        vm.AddEntityCommand.Execute(null);
        vm.CurrentFilePath = keptPath;

        vm.OpenCommand.Execute(null);

        dialogs.ErrorMessages.Should().ContainSingle().Which.Should().Contain(unrelated);
        vm.Entities.Should().ContainSingle("無関係な JSON で図を空にしない");
        vm.CurrentFilePath.Should().Be(keptPath, "無関係なファイルを次の上書き保存先にしない");
        File.ReadAllText(unrelated)
            .Should()
            .Be("{\"name\":\"x\",\"version\":\"1.0.0\"}", "他人のファイルは書き換えない");
    }

    // ---------------- Open: 未保存確認 ----------------

    /// <summary>未保存変更があるときは警告水準で確認し、キャンセルで現在の図が維持されることを検証する</summary>
    [Fact(DisplayName = "Open: ダーティなら警告確認し、キャンセルで現状維持する")]
    public void Open_Dirty_UsesWarningConfirmAndCancelKeepsCurrentDiagram()
    {
        var path = WriteValidDiagram("FromFile.json", "FromFile");
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateIsolatedViewModel(
            dialogs,
            new RecordingFileDialogService { OpenResult = new(path, 1) }
        );
        vm.AddEntityCommand.Execute(null);
        vm.IsDirty.Should().BeTrue();

        vm.OpenCommand.Execute(null);

        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Confirm_OpenDiagram);
        vm.Entities.Should().ContainSingle(entity => entity.TableName == "NewTable");
        vm.CurrentFilePath.Should().BeNull("キャンセルではファイルへ紐付かない");
    }

    /// <summary>保存済みクリーンな図の上書き読込は、警告でなく通常確認（Question）で確認することを検証する</summary>
    [Fact(DisplayName = "Open: クリーンで図があるときは通常確認（Question）")]
    public void Open_CleanWithEntities_UsesPlainConfirm()
    {
        var path = WriteValidDiagram("Clean.json", "FromFile");
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = CreateIsolatedViewModel(
            dialogs,
            new RecordingFileDialogService { OpenResult = new(path, 1) }
        );

        // 1 回目は空でクリーンなので無確認。2 回目は図があるクリーン状態からの読込になる
        vm.OpenCommand.Execute(null);
        vm.IsDirty.Should().BeFalse();
        vm.OpenCommand.Execute(null);

        dialogs
            .ConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Confirm_OpenDiagram);
        dialogs.WarningConfirmMessages.Should().BeEmpty("クリーンなら警告水準へ引き上げない");
        vm.Entities.Should().ContainSingle(entity => entity.TableName == "FromFile");
    }

    /// <summary>空でクリーンな新規状態（失うものがない）では確認を出さずに開くことを検証する</summary>
    [Fact(DisplayName = "Open: 空でクリーンなら確認を出さない")]
    public void Open_CleanAndEmpty_DoesNotConfirm()
    {
        var path = WriteValidDiagram("Empty.json", "FromFile");
        // 確認が出れば拒否されて読み込まれない設定にし、確認の有無を結果でも二重に確かめる
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateIsolatedViewModel(
            dialogs,
            new RecordingFileDialogService { OpenResult = new(path, 1) }
        );

        vm.OpenCommand.Execute(null);

        dialogs.ConfirmMessages.Should().BeEmpty();
        dialogs.WarningConfirmMessages.Should().BeEmpty();
        vm.Entities.Should().ContainSingle(entity => entity.TableName == "FromFile");
        vm.CurrentFilePath.Should().Be(path);
    }

    // ---------------- Save: 失敗時の保護 ----------------

    /// <summary>保存に失敗したらエラー通知し、クリーン化・パス紐付けをせずダーティのまま保持することを検証する</summary>
    [Fact(DisplayName = "Save: 書き込み失敗はエラー通知しダーティを維持する")]
    public void Save_WriteFailure_ShowsErrorAndKeepsDirty()
    {
        // 親ディレクトリが存在しない保存先＝一時ファイルの書き込み段階で確実に失敗する
        var unwritable = Path.Combine(_folder, "missing-directory", "Doc.json");
        var dialogs = new StubDialogService();
        var vm = CreateIsolatedViewModel(
            dialogs,
            new RecordingFileDialogService { SaveResult = new(unwritable, 1) }
        );
        vm.AddEntityCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        dialogs
            .ErrorMessages.Should()
            .ContainSingle()
            .Which.Should()
            .StartWith(Strings.Save_Failed);
        vm.IsDirty.Should().BeTrue("保存できていない変更をクリーン扱いにしない");
        vm.CurrentFilePath.Should().BeNull("保存に失敗したファイルへ紐付けない");
        File.Exists(unwritable).Should().BeFalse();
        File.Exists(unwritable + ".tmp").Should().BeFalse("一時ファイルを残さない");
    }

    /// <summary>保存に成功した後の失敗では、現在パスとダーティ状態が保存前の状態のまま保たれることを検証する</summary>
    [Fact(DisplayName = "Save: 失敗しても直前の現在パスを書き換えない")]
    public void SaveAs_WriteFailure_KeepsPreviousCurrentPath()
    {
        var existing = Path.Combine(_folder, "Existing.json");
        var unwritable = Path.Combine(_folder, "missing-directory", "Renamed.json");
        var dialogs = new StubDialogService();
        var vm = CreateIsolatedViewModel(
            dialogs,
            new RecordingFileDialogService { SaveResult = new(unwritable, 1) }
        );
        vm.CurrentFilePath = existing;
        vm.AddEntityCommand.Execute(null);

        vm.SaveAsCommand.Execute(null);

        dialogs.ErrorMessages.Should().ContainSingle();
        vm.CurrentFilePath.Should().Be(existing, "別名保存の失敗で現在パスを移さない");
        vm.IsDirty.Should().BeTrue();
    }
}
