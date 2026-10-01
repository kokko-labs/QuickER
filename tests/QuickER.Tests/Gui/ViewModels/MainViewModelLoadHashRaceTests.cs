using System.Collections.Concurrent;
using System.IO;
using System.Text;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 開く・外部変更の再読込と、最終既知ハッシュの採取との競合窓を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 読込は「ファイルの読み取り → 図へ反映 → 最終既知ハッシュの記録」の順で行う。ハッシュをディスクの
/// 読み直しで採ると、読み取りから採取までの隙間に外部プロセスが書いた内容のハッシュを「読み込んだ内容」
/// として記録してしまう。その書き込みの監視イベントは期待ハッシュと一致して捨てられ、GUI は古い内容を
/// クリーンとして表示し続け、次の上書き保存は照合を素通りして外部の変更を黙って消す。
/// </para>
/// <para>
/// 隙間は <see cref="MainViewModel.AfterLoadReadForTests"/> で決定的に開ける（保存側の
/// <see cref="MainViewModelSaveHashRaceTests"/> と同じ流儀）。
/// </para>
/// </remarks>
public sealed class MainViewModelLoadHashRaceTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-loadhash-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelLoadHashRaceTests() => Directory.CreateDirectory(_folder);

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

    /// <summary>単一テーブルの図をファイルへ書き出し、その内容ハッシュを返す（外部プロセスの書き込みを模す）</summary>
    private static string WriteDiagram(string path, string tableName)
    {
        JsonStorageService.Save(path, CreateDocument(tableName));
        return DocumentContentHash.TryCompute(path)!;
    }

    /// <summary>単一テーブルの図の保存文書を作る</summary>
    private static DiagramDocument CreateDocument(string tableName) =>
        new()
        {
            Schema = new ErDiagram
            {
                Entities = { new Entity { TableName = tableName } },
                TargetDbms = "sqlserver",
            },
            Layout = null,
        };

    /// <summary>開くファイルを指定した VM を作る（既定はファイル監視なし）</summary>
    private MainViewModel CreateViewModel(
        string openPath,
        StubDialogService dialogs,
        bool watchFiles = false
    )
    {
        var vm = new MainViewModel(
            dialogs,
            files: new RecordingFileDialogService { OpenResult = new(openPath, 1) }
        );
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );

        if (!watchFiles)
        {
            vm.DisableFileWatchingForTests();
        }

        return vm;
    }

    /// <summary>読み込んだバイト列から求めたハッシュが、実ファイルのハッシュと一致することを検証する</summary>
    /// <remarks>BOM 付きのファイルでも一致する（復号済みの文字列から採ると一致しない）ことまで固定する</remarks>
    [Fact(DisplayName = "読み込んだバイト列のハッシュはファイル内容のハッシュと一致する")]
    public void ComputeForBytes_MatchesFileHash()
    {
        var path = Path.Combine(_folder, "Bytes.json");
        File.WriteAllText(path, "{ \"日本語\": \"ü\" }", new UTF8Encoding(true));

        JsonStorageService.TryLoad(path, out _, out _, out _, out var rawContents);

        rawContents
            .Should()
            .Equal(File.ReadAllBytes(path), "読み取り失敗以外は検証の成否に依らず返す");
        DocumentContentHash
            .ComputeForBytes(rawContents)
            .Should()
            .Be(DocumentContentHash.TryCompute(path));
    }

    /// <summary>開く操作の読み取り直後に割り込んだ外部書き込みが、検知されて再読込されることを検証する</summary>
    /// <remarks>
    /// 開く経路は監視の開始が読み取りより後（現在パスが決まった時点）なので、隙間の書き込みは監視イベント
    /// すら起きない。記録直後のディスクとの再照合だけがこれを拾う。
    /// </remarks>
    [Fact(DisplayName = "開く: 読み取り直後に割り込んだ外部書き込みを検知して再読込する")]
    public void Open_ExternalWriteInRaceWindow_IsDetected()
    {
        var path = Path.Combine(_folder, "Open.json");
        WriteDiagram(path, "Original");
        var vm = CreateViewModel(path, new StubDialogService());

        // 読み取りとハッシュ記録の隙間で外部プロセスが書き換える（1 回だけ）
        vm.AfterLoadReadForTests = () =>
        {
            vm.AfterLoadReadForTests = null;
            WriteDiagram(path, "ExternalDuringOpen");
        };

        vm.OpenCommand.Execute(null);

        vm.Entities.Should()
            .ContainSingle(e => e.TableName == "ExternalDuringOpen", "外部内容へ追従する");
        vm.StatusMessage.Should().Be(Strings.Status_ExternalReloaded);
        vm.IsDirty.Should().BeFalse();
    }

    /// <summary>割り込みが無い通常の開く操作では、余計な再読込・通知が起きないことを検証する</summary>
    [Fact(DisplayName = "開く: 割り込みが無ければ通常どおり開いて終わる")]
    public void Open_WithoutExternalWrite_KeepsOpenedStatus()
    {
        var path = Path.Combine(_folder, "Plain.json");
        WriteDiagram(path, "Original");
        var vm = CreateViewModel(path, new StubDialogService());

        vm.OpenCommand.Execute(null);

        vm.Entities.Should().ContainSingle(e => e.TableName == "Original");
        vm.StatusMessage.Should().Be(Strings.Status_Opened, "開いた通知のまま（再読込へ流れない）");
        vm.IsDirty.Should().BeFalse();
        vm.CurrentFilePath.Should().Be(path);
    }

    /// <summary>BOM 付き UTF-8 の図ファイルを開いても、外部変更と誤判定しないことを検証する</summary>
    /// <remarks>
    /// 最終既知ハッシュを復号済みの文字列から採ると、復号が BOM を剥がすためディスクのハッシュと一致せず、
    /// 開いた直後の再照合が毎回「外部変更あり」と判定して再読込が暴発する。読み込んだバイト列から採る
    /// ことの固定。
    /// </remarks>
    [Fact(DisplayName = "開く: BOM 付きのファイルでも外部変更と誤判定しない")]
    public void Open_FileWithByteOrderMark_IsNotTreatedAsExternalChange()
    {
        var path = Path.Combine(_folder, "Bom.json");
        var json = JsonStorageService.SaveAtomic(
            Path.Combine(_folder, "Seed.json"),
            CreateDocument("WithBom")
        );
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var vm = CreateViewModel(path, new StubDialogService());

        vm.OpenCommand.Execute(null);

        vm.Entities.Should().ContainSingle(e => e.TableName == "WithBom");
        vm.StatusMessage.Should().Be(Strings.Status_Opened, "BOM の有無で再読込へ流れない");
        vm.IsDirty.Should().BeFalse();
    }

    /// <summary>
    /// 外部変更の再読込中に割り込んだ外部書き込みが、次の上書き保存で確認として現れることを検証する
    /// </summary>
    /// <remarks>
    /// 再読込の経路は記録直後に再照合しない（監視が拾う。下の実監視のテスト）。ここでは監視を止めた状態で、
    /// 記録した最終既知ハッシュが「読み込んだ内容」であること＝隙間の書き込みをディスクとの食い違いとして
    /// 保っていることを、利用者に見える結果（上書き前の確認）で確かめる。読み直し方式では食い違いが消え、
    /// 確認なしに外部の変更を上書きする。
    /// </remarks>
    [Fact(DisplayName = "再読込: 読み取り直後の外部書き込みを、次の上書き保存で黙って消さない")]
    public void Reload_ExternalWriteInRaceWindow_IsNotSilentlyOverwritten()
    {
        var path = Path.Combine(_folder, "Reload.json");
        WriteDiagram(path, "Original");
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateViewModel(path, dialogs);
        vm.OpenCommand.Execute(null);

        vm.AfterLoadReadForTests = () =>
        {
            vm.AfterLoadReadForTests = null;
            WriteDiagram(path, "ExternalDuringReload");
        };

        var firstHash = WriteDiagram(path, "External");
        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Modified, firstHash);
        vm.Entities.Should().ContainSingle(e => e.TableName == "External");

        vm.SaveCommand.Execute(null);

        dialogs.WarningConfirmMessages.Should().Contain(Strings.Confirm_OverwriteExternalChange);
        JsonStorageService
            .Load(path)
            .Schema.Entities.Should()
            .ContainSingle(
                e => e.TableName == "ExternalDuringReload",
                "確認で取り消したので外部の内容が残る"
            );
    }

    /// <summary>
    /// 外部変更の再読込中に割り込んだ外部書き込みへ、実際のファイル監視を経て追従することを検証する
    /// </summary>
    /// <remarks>
    /// 再読込の経路では監視が最初から動いている。記録した最終既知ハッシュが読み込んだ内容のものなら、
    /// 隙間の書き込みの監視イベントは期待ハッシュと食い違って捨てられずに届き、UI のキューを経て次の
    /// 再読込へ進む。UI スレッドの代わりにテストのスレッドがキューを汲む（VM は 1 つのスレッドだけが触る）。
    /// </remarks>
    [Fact(DisplayName = "再読込: 読み取り直後の外部書き込みへ実際の監視を経て追従する")]
    public void Reload_ExternalWriteInRaceWindow_IsFollowedThroughWatcher()
    {
        var path = Path.Combine(_folder, "Watched.json");
        WriteDiagram(path, "Original");
        var vm = CreateViewModel(path, new StubDialogService(), watchFiles: true);
        var uiQueue = new ConcurrentQueue<Action>();
        vm.SetUiPost(uiQueue.Enqueue);

        try
        {
            vm.OpenCommand.Execute(null);
            vm.Entities.Should().ContainSingle(e => e.TableName == "Original");

            vm.AfterLoadReadForTests = () =>
            {
                vm.AfterLoadReadForTests = null;
                WriteDiagram(path, "ExternalDuringReload");
            };

            // 1 回目の外部書き込み → 監視 → 再読込（その読み取り直後に 2 回目が割り込む）
            WriteDiagram(path, "External");

            var deadline = DateTime.UtcNow.AddSeconds(15);

            while (
                DateTime.UtcNow < deadline
                && !vm.Entities.Any(e => e.TableName == "ExternalDuringReload")
            )
            {
                while (uiQueue.TryDequeue(out var action))
                {
                    action();
                }

                Thread.Sleep(20);
            }

            vm.Entities.Should()
                .ContainSingle(
                    e => e.TableName == "ExternalDuringReload",
                    "隙間の書き込みの監視イベントが届き、次の再読込で追従する"
                );
            vm.IsDirty.Should().BeFalse();
        }
        finally
        {
            vm.DisableFileWatchingForTests();
        }
    }
}
