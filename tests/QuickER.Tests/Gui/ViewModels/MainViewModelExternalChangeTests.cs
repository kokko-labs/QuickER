using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 外部変更の通知の一時表示（ステータスバーの戻し）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 外部変更の分岐そのもの（クリーンは自動再読込・ダーティは確認・再入・起動時・監視の通知）は、文書の状態機械のエンジン
/// （<see cref="DocumentFileTests"/> のシナリオ ext-* と乱数の手の列）が確かめる。ここは通知のタイマーの世代の単体だけを残す。
/// </remarks>
public sealed class MainViewModelExternalChangeTests : IDisposable
{
    private readonly TestTempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>
    /// 古い一時通知の戻し処理（タイマー）が遅れて走っても、その後に出た通知を「準備完了」で消さないことを検証する
    /// </summary>
    /// <remarks>
    /// タイマーを破棄しても、既に動き出した・UI のキューへ載った戻し処理は止まらない。実タイマー（5 秒）は待たず、
    /// 戻し処理を世代つきで直接呼んで判定を確かめる
    /// </remarks>
    [Fact(DisplayName = "一時通知: 古い通知の戻し処理は、後から出た通知を消さない")]
    public void StatusRevert_FromOlderNotification_DoesNotClearNewerOne()
    {
        var path = Path.Combine(_temp.Path, "Doc.json");
        JsonStorageService.SaveAtomic(
            path,
            new DiagramDocument
            {
                Schema = new ErDiagram
                {
                    Entities = { new Entity { TableName = "Alive" } },
                    TargetDbms = "sqlserver",
                },
                Layout = null,
            }
        );
        var vm = new MainViewModel(
            new StubDialogService(),
            files: new RecordingFileDialogService { OpenResult = new(path, 1) }
        );
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_temp.Path),
            Path.Combine(_temp.Path, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();
        vm.OpenCommand.Execute(null);

        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Deleted, null);
        var older = vm.StatusGenerationForTests;
        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Renamed, null);
        var newer = vm.StatusGenerationForTests;

        vm.RevertStatusIfCurrent(older);
        vm.StatusMessage.Should()
            .Be(Strings.Status_ExternalFileRenamed, "古い通知の戻し処理は無視する");

        vm.RevertStatusIfCurrent(newer);
        vm.StatusMessage.Should().Be(Strings.Status_Ready);
    }
}
