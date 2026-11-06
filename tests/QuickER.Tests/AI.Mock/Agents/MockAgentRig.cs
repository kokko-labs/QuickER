using System.IO;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock.Agents;

/// <summary>組の前提（ターゲット・UI のファイルの有無・追加の指示）</summary>
/// <param name="Target">生成のターゲット</param>
/// <param name="UiFilePresent">プロジェクトフォルダに UI のファイルを置いておくか（続行の促しの判定に効く）</param>
/// <param name="AdditionalInstructions">要求に添える追加の指示</param>
internal sealed record MockAgentRigOptions(
    MockAgentTarget Target,
    bool UiFilePresent = true,
    string? AdditionalInstructions = null
);

/// <summary>実の実行器＋テストダブルの組（行ごとに作り、作業フォルダごと捨てる）</summary>
/// <remarks>
/// 作業フォルダは <see cref="TestTempFolder"/> に行ごとに作る。要求はどの実行器にも同じもの
/// （プロジェクト名 <see cref="ProjectName"/>・モデル <see cref="Model"/>・プロバイダー <c>openai</c>）を渡す。
/// </remarks>
internal abstract class MockAgentRig : IAsyncDisposable
{
    /// <summary>プロジェクト名</summary>
    public const string ProjectName = "AcmeMock";

    /// <summary>要求のモデル</summary>
    public const string Model = "test-model";

    /// <summary>要求のモデルプロバイダー（Codex だけが読む）</summary>
    public const string ModelProvider = "openai";

    /// <summary>実行の完了を待つ上限（取り消しが届かない変異でテストがハングせず失敗する。全件の実行の負荷でも揺れないよう、壊れたときだけ待つ長さにする）</summary>
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(60);

    private readonly TestTempFolder _folder = new();
    private readonly List<string> _progress = [];
    private readonly object _gate = new();
    private readonly TaskCompletionSource _turnOpened = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    /// <summary>組の前提を受け取り、作業フォルダと要求を用意する</summary>
    /// <param name="options">組の前提</param>
    protected MockAgentRig(MockAgentRigOptions options)
    {
        Options = options;

        if (options.UiFilePresent)
        {
            // 続行の促しの判定はプロジェクトフォルダ配下だけを見るので、実際のレイアウトに合わせて置く
            var project = Path.Combine(_folder.Path, ProjectName);
            Directory.CreateDirectory(project);
            File.WriteAllText(
                Path.Combine(project, options.Target.UiFile),
                options.Target.UiContent
            );
        }

        Request = new MockProjectAgentRequest(
            WorkingDirectory: _folder.Path,
            ProjectName: ProjectName,
            AdditionalInstructions: options.AdditionalInstructions,
            Model: Model,
            Profile: options.Target.Profile,
            ModelProvider: ModelProvider
        );
    }

    /// <summary>組の前提</summary>
    public MockAgentRigOptions Options { get; }

    /// <summary>作業フォルダ（出力フォルダ）</summary>
    public string WorkingDirectory => _folder.Path;

    /// <summary>実行器へ渡す要求</summary>
    public MockProjectAgentRequest Request { get; }

    /// <summary>実の実行器</summary>
    public abstract IMockProjectAgent Agent { get; }

    /// <summary>進捗へ流れた文章の連結</summary>
    public string ProgressText
    {
        get
        {
            lock (_gate)
            {
                return string.Concat(_progress);
            }
        }
    }

    /// <summary>バックエンドへ送られたターンのプロンプト（送った順）</summary>
    public abstract IReadOnlyList<string> Prompts { get; }

    /// <summary>バックエンドへ渡したシステムプロンプト（相当のもの。渡す前なら null）</summary>
    public abstract string? SystemPrompt { get; }

    /// <summary>起動の設定（起動の前なら null）</summary>
    public abstract MockAgentLaunch? Launch { get; }

    /// <summary>セッション（スレッド）を張ったか</summary>
    public abstract bool SessionStarted { get; }

    /// <summary>バックエンドが受け取った中断の数</summary>
    public abstract int InterruptsReceived { get; }

    /// <summary>最後の中断の宛先（宛先を運ばない・中断が無ければ null）</summary>
    public abstract string? LastInterruptTarget { get; }

    /// <summary>バックエンドが破棄されたか</summary>
    public abstract bool BackendDisposed { get; }

    /// <summary>バックエンドの可用性を変える</summary>
    /// <param name="available">可用か</param>
    public abstract void SetBackendAvailable(bool available);

    /// <summary>実行中のまま残したターンを、バックエンドの中断の完了として終える</summary>
    /// <remarks><see cref="MockAgentCapability.InterruptCompletesViaEvent"/> を持つアダプタの組だけが呼ばれる</remarks>
    public virtual void EndOpenTurnAsInterrupted() =>
        throw new NotSupportedException("この実行器は中断の完了の通知を持たない");

    /// <summary>台本で実行を始める（終わり方が <see cref="MockAgentTurnEnding.Open"/> なら、ターンを開いたまま戻らない）</summary>
    /// <param name="script">台本</param>
    /// <param name="runToken">実行器へ渡すトークン</param>
    public Task<MockProjectAgentOutcome> Start(
        MockAgentScript script,
        CancellationToken runToken
    ) => PlayAsync(script, runToken);

    /// <summary>台本で実行し、完了を上限つきで待つ</summary>
    /// <param name="script">台本</param>
    /// <param name="runToken">実行器へ渡すトークン</param>
    public Task<MockProjectAgentOutcome> RunAsync(
        MockAgentScript script,
        CancellationToken runToken
    ) => Bounded(Start(script, runToken));

    /// <summary>実行の完了を上限つきで待つ</summary>
    /// <param name="run">実行</param>
    public static Task<MockProjectAgentOutcome> Bounded(Task<MockProjectAgentOutcome> run) =>
        run.WaitAsync(RunTimeout);

    /// <summary>台本の <see cref="MockAgentTurnEnding.Open"/> のターンが開くまで待つ</summary>
    public Task WhenTurnOpenAsync() => _turnOpened.Task.WaitAsync(RunTimeout);

    /// <summary>台本を実行器のバックエンドへ訳して流す</summary>
    /// <param name="script">台本</param>
    /// <param name="runToken">実行器へ渡すトークン</param>
    protected abstract Task<MockProjectAgentOutcome> PlayAsync(
        MockAgentScript script,
        CancellationToken runToken
    );

    /// <summary>実行器の進捗の受け口</summary>
    /// <param name="text">進捗の文章</param>
    protected void OnProgress(string text)
    {
        lock (_gate)
        {
            _progress.Add(text);
        }
    }

    /// <summary>台本の <see cref="MockAgentTurnEnding.Open"/> のターンが開いたことを知らせる</summary>
    protected void SignalTurnOpen() => _turnOpened.TrySetResult();

    /// <summary>条件が満たされるまで上限つきで待つ（実行器が別のスレッドで次のターンを送るのを待つ）</summary>
    /// <param name="condition">条件</param>
    protected static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + RunTimeout;

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("実行器が台本の次のターンへ進まない");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>作業フォルダを中身ごと消す</summary>
    public virtual ValueTask DisposeAsync()
    {
        _folder.Dispose();
        return ValueTask.CompletedTask;
    }
}
