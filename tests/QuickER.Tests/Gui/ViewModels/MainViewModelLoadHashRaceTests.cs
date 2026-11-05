using System.Collections.Concurrent;
using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 再読込の読み取り直後に割り込んだ外部書き込みへ、実際の FileSystemWatcher を経て追従することを検証するテストクラス。
/// </summary>
/// <remarks>
/// 開く・保存・再読込の競合窓の分岐そのもの（最終既知ハッシュは読み書きした内容から採る）は、文書の状態機械のエンジン
/// （<see cref="DocumentFileTests"/> のシナリオ race-* と乱数の手の列）が監視イベントを注入して決定的に確かめる。
/// ここは監視器の通知が実際に届き、次の再読込へ進むこと（OS のタイミング）だけを残す。
/// </remarks>
public sealed class MainViewModelLoadHashRaceTests : IDisposable
{
    private readonly TestTempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>単一テーブルの図をファイルへ書き出す（外部プロセスの書き込みを模す）</summary>
    private static void WriteDiagram(string path, string tableName) =>
        JsonStorageService.SaveAtomic(
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

    /// <summary>
    /// 再読込の読み取り直後に割り込んだ書き込みの監視イベントが、正しい最終既知ハッシュのもとで捨てられずに届き、
    /// 次の再読込で追従することを、実際の監視器を経て検証する
    /// </summary>
    [Fact(DisplayName = "再読込: 読み取り直後の外部書き込みへ実際の監視を経て追従する")]
    public void Reload_ExternalWriteInRaceWindow_IsFollowedThroughWatcher()
    {
        var path = Path.Combine(_temp.Path, "Watched.json");
        WriteDiagram(path, "Original");
        var vm = new MainViewModel(
            new StubDialogService(),
            files: new RecordingFileDialogService { OpenResult = new(path, 1) }
        );
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_temp.Path),
            Path.Combine(_temp.Path, "last_diagram.json")
        );
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

            // 監視の通知はスレッドプールで届くため、並列実行の負荷で数秒遅れ得る（届けばすぐ抜ける）
            var deadline = DateTime.UtcNow.AddSeconds(30);

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
