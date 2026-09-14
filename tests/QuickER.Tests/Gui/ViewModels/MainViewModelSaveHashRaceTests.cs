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
/// 上書き保存とハッシュ採取の競合窓を検証するテストクラス。
/// </summary>
/// <remarks>
/// 保存は「監視の一時停止 → 書き込み → 最終既知ハッシュの記録 → 監視の再開」の順で行う。
/// ハッシュをディスクの読み直しで採ると、書き込み完了から採取までの隙間に外部プロセスが書いた内容の
/// ハッシュを「自分が保存した内容」として記録してしまう。その間の FSW イベントは一時停止で捨てられ
/// （デバウンスはワンショットで再スケジュールされない）、記録済みハッシュも一致するため、以後
/// どの検知経路も差分を見つけられない＝メモリの図とディスクが恒久的に食い違ったままになる。
/// </remarks>
public sealed class MainViewModelSaveHashRaceTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-savehash-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelSaveHashRaceTests() => Directory.CreateDirectory(_folder);

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

    /// <summary>単一テーブルの図をファイルへ書き出す（外部プロセスの書き込みを模す）</summary>
    private static void WriteDiagram(string path, string tableName)
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
    }

    /// <summary>保存先を指定した、ファイル監視なしの VM を作る</summary>
    private MainViewModel CreateViewModel(string savePath, StubDialogService dialogs)
    {
        var vm = new MainViewModel(
            dialogs,
            files: new RecordingFileDialogService { SaveResult = new(savePath, 1) }
        );
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();
        return vm;
    }

    /// <summary>書き出した内容から求めたハッシュが、実ファイルのハッシュと一致することを検証する</summary>
    /// <remarks>
    /// 一致しなければ、保存直後の最終既知ハッシュが常に「外部変更あり」に見えて再読込が暴発する。
    /// 符号化（BOM なし UTF-8）が <see cref="AtomicFile.WriteAllText(string, string)"/> と揃っていることの固定。
    /// </remarks>
    [Fact(DisplayName = "書き出した内容のハッシュはファイル内容のハッシュと一致する")]
    public void ComputeForText_MatchesFileHash()
    {
        var path = Path.Combine(_folder, "Text.json");
        const string contents = "{\n  \"Version\": 1,\n  \"日本語\": \"ü\"\n}";

        AtomicFile.WriteAllText(path, contents);

        DocumentContentHash
            .ComputeForText(contents)
            .Should()
            .Be(DocumentContentHash.TryCompute(path));
    }

    /// <summary>保存の書き込み直後に割り込んだ外部書き込みが、検知されることを検証する</summary>
    [Fact(DisplayName = "保存直後に割り込んだ外部書き込みを検知して再読込する")]
    public void Save_ExternalWriteInRaceWindow_IsDetected()
    {
        var path = Path.Combine(_folder, "Doc.json");
        var vm = CreateViewModel(path, new StubDialogService());
        vm.AddEntityCommand.Execute(null);

        // 書き込み完了とハッシュ記録の隙間（＝監視の一時停止中）で外部プロセスが書き換える
        vm.AfterSaveWriteForTests = () => WriteDiagram(path, "ExternalDuringSave");

        vm.SaveCommand.Execute(null);
        vm.AfterSaveWriteForTests = null;

        vm.Entities.Should()
            .ContainSingle(e => e.TableName == "ExternalDuringSave", "外部内容へ追従する");
        vm.StatusMessage.Should().Be(Strings.Status_ExternalReloaded);
        vm.IsDirty.Should().BeFalse();
    }

    /// <summary>割り込みが無い通常の保存では、余計な再読込・通知が起きないことを検証する</summary>
    [Fact(DisplayName = "割り込みが無ければ保存は通常どおり完了する")]
    public void Save_WithoutExternalWrite_KeepsSavedStatus()
    {
        var path = Path.Combine(_folder, "Plain.json");
        var vm = CreateViewModel(path, new StubDialogService());
        vm.AddEntityCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        vm.StatusMessage.Should().Be(Strings.Status_Saved, "保存通知のまま（再読込へ流れない）");
        vm.IsDirty.Should().BeFalse();
        vm.CurrentFilePath.Should().Be(path);
    }
}
