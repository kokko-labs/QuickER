using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock.Runner;

/// <summary>スクリプト化した挙動を返すフェイクのモックプロジェクトエージェント</summary>
internal sealed class FakeMockProjectAgent : IMockProjectAgent
{
    /// <summary>RunAsync 実行時に走らせる副作用（成果物生成のシミュレーション等）</summary>
    public Action<string>? OnRun { get; set; }

    /// <summary>返すエージェント結果</summary>
    public MockProjectAgentOutcome Outcome { get; set; } = new(true, null, false);

    /// <summary>RunAsync でスローする例外（タイムアウト・中断のシミュレーション）</summary>
    public Exception? ThrowOnRun { get; set; }

    /// <summary>
    /// RunAsync の中で、トークンが取り消されるまで待つか（中断のシミュレーション。60 秒で諦める＝
    /// 中断が届かない変異でもテストがハングせず失敗する。短いと全件の実行の負荷で、取り消しより先に待ちが終わって揺れる）。
    /// </summary>
    public bool WaitForCancellation { get; set; }

    /// <summary>RunAsync の始めに流す進捗の文章</summary>
    public string ProgressText { get; set; } = "作業を開始します。\n";

    /// <summary>RunAsync が呼ばれたスレッド</summary>
    public int? RunThreadId { get; private set; }

    public MockProjectAgentRequest? CapturedRequest { get; private set; }
    public bool Interrupted { get; private set; }
    public bool Available { get; set; } = true;

    /// <summary>
    /// 最終ビルドが境界越えになると宣言するか（既定 false＝Claude Code / API キー相当）。
    /// </summary>
    public bool EscapesSandbox { get; set; }

    public bool IsAvailable() => Available;

    /// <summary>実行ログに出すバックエンド名</summary>
    public string BackendName { get; set; } = "FakeBackend";

    public bool FinalBuildEscapesSandbox => EscapesSandbox;

    public async Task<MockProjectAgentOutcome> RunAsync(
        MockProjectAgentRequest request,
        Action<string> onProgress,
        CancellationToken cancellationToken
    )
    {
        RunThreadId = Environment.CurrentManagedThreadId;
        CapturedRequest = request;
        onProgress(ProgressText);
        OnRun?.Invoke(request.WorkingDirectory);

        if (ThrowOnRun is not null)
        {
            throw ThrowOnRun;
        }

        if (WaitForCancellation)
        {
            await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
        }

        return Outcome;
    }

    public Task InterruptAsync()
    {
        Interrupted = true;
        return Task.CompletedTask;
    }
}

/// <summary>スクリプト化したビルド結果を返すフェイクビルド検証器</summary>
internal sealed class FakeBuildRunner : IBuildRunner
{
    public bool BuildSuccess { get; set; } = true;
    public bool DotnetAvailable { get; set; } = true;
    public int BuildCallCount { get; private set; }

    /// <summary>最後に渡されたビルド対象（ソリューションのフルパス）</summary>
    public string? CapturedSolutionPath { get; private set; }

    /// <summary>ビルド開始時点の状態を観測するフック（obj/bin の有無など）</summary>
    public Action? OnBuild { get; set; }

    /// <summary>
    /// ビルドが「終わらない」状況の再現（true で待たされる）。
    /// </summary>
    /// <remarks>
    /// 無限待ちにすると、専用タイムアウトを外す変異でテストが失敗でなくハングする。
    /// 一方で短すぎると、全件の実行の負荷でタイマーの通知が遅れたとき、打ち切りより先に待ちが終わって
    /// 正しい実装でも「打ち切られなかった」と判定される（5 秒では実際に揺れた）。
    /// 打ち切りで終わる行は待ちの長さに依らず速いので、壊れたときだけ待つ長さとして 60 秒にする。
    /// </remarks>
    public bool SlowBuild { get; set; }

    /// <summary>BuildAsync で投げる例外（dotnet を起動できない等のシミュレーション）</summary>
    public Exception? ThrowOnBuild { get; set; }

    /// <summary>ビルドが始まったことの合図（中断をビルド中に届けるテスト用）</summary>
    public TaskCompletionSource BuildStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<BuildRunResult> BuildAsync(
        string solutionFilePath,
        CancellationToken cancellationToken = default
    )
    {
        BuildCallCount++;
        CapturedSolutionPath = solutionFilePath;
        OnBuild?.Invoke();
        BuildStarted.TrySetResult();

        if (ThrowOnBuild is not null)
        {
            throw ThrowOnBuild;
        }

        if (SlowBuild)
        {
            await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
        }

        return new BuildRunResult(BuildSuccess, "build output");
    }

    public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DotnetAvailable);
}
