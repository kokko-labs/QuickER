using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Settings;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 上書き保存前のディスク照合（黙った上書きの防止）を検証するテストクラス。
/// </summary>
/// <remarks>
/// クリーン時の自動再読込は、破損・将来版・型不一致で失敗すると 5 秒の一時通知だけを残し、
/// 文書はクリーン・最終既知ハッシュは旧値のままになる。保存前に照合しないと、その次の Ctrl+S が
/// 外部の新しい内容を黙って上書きする（保存後の再照合は自分が書いた内容と一致するため検知できない）。
/// </remarks>
public sealed class MainViewModelSaveOverwriteGuardTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-overwrite-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelSaveOverwriteGuardTests() => Directory.CreateDirectory(_folder);

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

    /// <summary>単一テーブルの図をファイルへ書き出し、その内容ハッシュを返す（外部書き込みの模擬）</summary>
    private static string WriteDiagram(string path, string tableName)
    {
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
        return DocumentContentHash.TryCompute(path)!;
    }

    /// <summary>ファイル監視を止めた、永続化先を一時フォルダへ隔離した VM を作る</summary>
    private MainViewModel CreateViewModel(
        StubDialogService dialogs,
        RecordingFileDialogService files
    )
    {
        var vm = new MainViewModel(dialogs, files: files);
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();
        return vm;
    }

    /// <summary>指定パスの図を開いた（現在パス紐付き・クリーン）VM を返す</summary>
    private MainViewModel OpenDiagram(string path, StubDialogService dialogs, string? saveTo = null)
    {
        var vm = CreateViewModel(
            dialogs,
            new RecordingFileDialogService
            {
                OpenResult = new(path, 1),
                SaveResult = saveTo is null ? null : new(saveTo, 1),
            }
        );
        vm.OpenCommand.Execute(null);
        vm.IsDirty.Should().BeFalse();
        return vm;
    }

    /// <summary>外部で変更されたファイルへの上書き保存は、確認をキャンセルすると書き込まないことを検証する</summary>
    [Fact(DisplayName = "上書き保存: 外部変更を確認し、キャンセルすると書き込まない")]
    public void Save_ExternallyChangedFile_CancelKeepsDiskAndDirty()
    {
        var path = Path.Combine(_folder, "Doc.json");
        WriteDiagram(path, "Original");

        var dialogs = new StubDialogService();
        var vm = OpenDiagram(path, dialogs);
        vm.AddEntityCommand.Execute(null);

        // 自動再読込が失敗した（＝追従できなかった）状況を模し、外部が書き換えたまま保存へ進む
        WriteDiagram(path, "External");
        dialogs.ConfirmResult = false;

        vm.SaveCommand.Execute(null);

        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Confirm_OverwriteExternalChange);
        JsonStorageService
            .Load(path)
            .Schema.Entities.Should()
            .ContainSingle(e => e.TableName == "External", "キャンセルでは書き込まない");
        vm.IsDirty.Should().BeTrue("保存していないためダーティのまま");
        vm.StatusMessage.Should().NotBe(Strings.Status_Saved);
    }

    /// <summary>外部変更の確認に続行すると、現在の内容で上書きすることを検証する</summary>
    [Fact(DisplayName = "上書き保存: 外部変更の確認に続行すると上書きする")]
    public void Save_ExternallyChangedFile_ConfirmOverwrites()
    {
        var path = Path.Combine(_folder, "Doc.json");
        WriteDiagram(path, "Original");

        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = OpenDiagram(path, dialogs);
        vm.AddEntityCommand.Execute(null);
        WriteDiagram(path, "External");

        vm.SaveCommand.Execute(null);

        dialogs.WarningConfirmMessages.Should().Contain(Strings.Confirm_OverwriteExternalChange);
        JsonStorageService.Load(path).Schema.Entities.Should().HaveCount(2, "現在の図で上書きする");
        vm.IsDirty.Should().BeFalse();
    }

    /// <summary>外部変更が無ければ、上書き保存で確認を出さないことを検証する</summary>
    [Fact(DisplayName = "上書き保存: 外部変更が無ければ確認しない")]
    public void Save_UnchangedFile_DoesNotConfirm()
    {
        var path = Path.Combine(_folder, "Doc.json");
        WriteDiagram(path, "Original");

        var dialogs = new StubDialogService();
        var vm = OpenDiagram(path, dialogs);
        vm.AddEntityCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        dialogs.WarningConfirmMessages.Should().BeEmpty();
        vm.StatusMessage.Should().Be(Strings.Status_Saved);
        vm.IsDirty.Should().BeFalse();
    }

    /// <summary>現在の文書と別のファイルへの保存では照合しないことを検証する（新規保存と同じ扱い）</summary>
    [Fact(DisplayName = "上書き保存: 現在の文書でないファイルへの保存は照合しない")]
    public void Save_OtherFile_DoesNotConfirm()
    {
        var other = Path.Combine(_folder, "Other.json");
        WriteDiagram(other, "Unrelated");

        var dialogs = new StubDialogService();
        var vm = CreateViewModel(
            dialogs,
            new RecordingFileDialogService { SaveResult = new(other, 1) }
        );
        vm.AddEntityCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        dialogs.WarningConfirmMessages.Should().BeEmpty("現在の文書を上書きするわけではない");
        vm.CurrentFilePath.Should().Be(other);
        vm.IsDirty.Should().BeFalse();
    }

    /// <summary>「名前を付けて保存」で大文字小文字だけ違う同一パスを選んだ場合も照合されることを検証する</summary>
    [Fact(DisplayName = "名前を付けて保存: 大小違いの同一パスでも外部変更を照合する")]
    public void SaveAs_SamePathDifferentCase_IsCompared()
    {
        var path = Path.Combine(_folder, "Doc.json");
        var sameFileUpperCase = Path.Combine(_folder, "DOC.JSON");
        WriteDiagram(path, "Original");

        var dialogs = new StubDialogService();
        var vm = OpenDiagram(path, dialogs, saveTo: sameFileUpperCase);
        vm.AddEntityCommand.Execute(null);

        WriteDiagram(path, "External");
        dialogs.ConfirmResult = false;

        vm.SaveAsCommand.Execute(null);

        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Confirm_OverwriteExternalChange);
        JsonStorageService
            .Load(path)
            .Schema.Entities.Should()
            .ContainSingle(e => e.TableName == "External", "同じファイルなので書き込まない");
    }

    /// <summary>「このまま続行」で無視した外部バージョンと同じ内容なら、保存時に再確認しないことを検証する</summary>
    [Fact(DisplayName = "上書き保存: 続行を選んだ外部バージョンでは再確認しない")]
    public void Save_IgnoredExternalVersion_DoesNotConfirmAgain()
    {
        var path = Path.Combine(_folder, "Doc.json");
        WriteDiagram(path, "Original");

        var dialogs = new StubDialogService();
        var vm = OpenDiagram(path, dialogs);
        vm.AddEntityCommand.Execute(null);

        // ダーティ中の外部変更で「続行」（＝再読込しない）を選ぶ
        var externalHash = WriteDiagram(path, "External");
        dialogs.ConfirmResult = false;
        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Modified, externalHash);
        dialogs.WarningConfirmMessages.Should().ContainSingle();

        vm.SaveCommand.Execute(null);

        dialogs
            .WarningConfirmMessages.Should()
            .NotContain(
                Strings.Confirm_OverwriteExternalChange,
                "同じ内容について続行を選んだ後は再確認しない"
            );
        vm.StatusMessage.Should().Be(Strings.Status_Saved);
        JsonStorageService.Load(path).Schema.Entities.Should().HaveCount(2);
    }
}
