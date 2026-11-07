using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.AI.Mock.Runner;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockProjectAgentRunner"/> の、流れの表（<c>MockRunnerTests</c>）の行にしないものを検証するテストクラス
/// （変更検知のスナップショットを走らせるスレッド）。
/// </summary>
/// <remarks>
/// ランナーの流れ（成果物・画面の数・最終ビルド・タイムアウト・中断・委譲・実行ログ）は <c>MockRunnerTests</c> の行、
/// 実行器ごとの契約は <c>MockAgentContractTests</c> の行、最終ビルドの直前のビルド出力の削除と変更検知は <c>MockBuildBoundaryTests</c> の行が見る。
/// </remarks>
public class MockProjectAgentRunnerTests : IDisposable
{
    /// <summary>テストで使うプロジェクト名（スキャフォールドのレイアウト規則に合わせてフォルダを作る）</summary>
    private const string ProjectName = "AcmeMock";

    /// <summary>テストごとの一時フォルダ（破棄で中身ごと消す）</summary>
    private readonly TestTempFolder _temp = new();

    /// <summary>テストごとの作業フォルダを作る</summary>
    private string NewTempFolder()
    {
        var folder = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <inheritdoc />
    public void Dispose() => _temp.Dispose();

    /// <summary>プロジェクトフォルダ（<c>{出力フォルダ}/{プロジェクト名}/</c>）のパス</summary>
    private static string ProjectDirectory(string outputDirectory) =>
        Path.Combine(outputDirectory, ProjectName);

    /// <summary>成果物を書き出す副作用（プロジェクトフォルダ配下の csproj + xaml）を返す</summary>
    private static Action<string> WriteArtifacts() =>
        dir =>
        {
            var project = ProjectDirectory(dir);
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, $"{ProjectName}.csproj"), "<Project/>");
            File.WriteAllText(Path.Combine(project, "MainWindow.xaml"), "<Window/>");
        };

    /// <summary>
    /// 変更検知のスナップショット（全ファイルのハッシュ）が呼び出し元のスレッド（UI スレッド）で走らないことを
    /// 検証する。スナップショットが読むファイルへ oplock を掛けて、スナップショットをそのファイルを開く途中で止め、
    /// その間に単一スレッドの同期コンテキスト（呼び出し元の代役）へ戻りが返り、そのスレッドが別の処理を受け付けることを見る
    /// （スナップショットが呼び出し元で同期に走れば、止まっている間はランが戻らず、そのスレッドも空かない）。
    /// </summary>
    [Fact(DisplayName = "スナップショットは呼び出し元のスレッドで走らない")]
    public async Task RunAsync_CapturesSnapshotOffCallerThread()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent { EscapesSandbox = true, OnRun = WriteArtifacts() };
        var runner = new MockProjectAgentRunner(
            agent,
            new FakeBuildRunner(),
            confirmBuild: _ => true
        );

        // 拡張子が比較の対象（UI 層のソース・静的資産でない）なので、スナップショットはこのファイルを開いてハッシュする
        var held = Path.Combine(folder, "held.json");
        File.WriteAllText(held, "{}");
        var oplock = FileOplock.Acquire(held);
        var callerFree = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Task? run = null;

        try
        {
            var running = SingleThreadContext.RunAsync(() =>
            {
                run = runner.RunAsync(
                    folder,
                    ProjectName,
                    additionalInstructions: null,
                    string.Empty,
                    _ => { },
                    TestContext.Current.CancellationToken
                );

                // ランが戻ったあと、呼び出し元のスレッドが次の処理を受け付けるか
                SynchronizationContext.Current!.Post(
                    _ => callerFree.TrySetResult(Environment.CurrentManagedThreadId),
                    null
                );

                return run;
            });

            oplock
                .WaitForBreak(TimeSpan.FromSeconds(30))
                .Should()
                .BeTrue("スナップショットは held.json を開こうとして止まる");
            var freeThreadId = await callerFree
                .Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)
                .ContinueWith(
                    task => task.IsCompletedSuccessfully ? task.Result : (int?)null,
                    TaskScheduler.Default
                );

            freeThreadId
                .Should()
                .NotBeNull("スナップショットが止まっている間に、呼び出し元のスレッドは空いている");
            run!.IsCompleted.Should().BeFalse("スナップショットは止まったまま");

            oplock.Dispose();
            var callerThreadId = await running;

            freeThreadId.Should().Be(callerThreadId);
            agent.RunThreadId.Should().NotBeNull();
            agent.RunThreadId.Should().NotBe(callerThreadId);
        }
        finally
        {
            oplock.Dispose();
        }
    }

    /// <summary>
    /// 専用スレッド 1 本で継続を回す同期コンテキスト（UI スレッドの代役）。渡した処理をそのスレッドで始め、
    /// 完了まで継続を同じスレッドへ戻す。戻り値はそのスレッドの ID。
    /// </summary>
    private sealed class SingleThreadContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(
            SendOrPostCallback Callback,
            object? State
        )> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public static Task<int> RunAsync(Func<Task> start)
        {
            var completion = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            var thread = new Thread(() =>
            {
                var context = new SingleThreadContext();
                SetSynchronizationContext(context);

                try
                {
                    var task = start();
                    task.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);

                    foreach (var (callback, state) in context._queue.GetConsumingEnumerable())
                    {
                        callback(state);
                    }

                    task.GetAwaiter().GetResult();
                    completion.SetResult(Environment.CurrentManagedThreadId);
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            })
            {
                IsBackground = true,
            };
            thread.Start();
            return completion.Task;
        }
    }
}
