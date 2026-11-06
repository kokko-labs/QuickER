using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Extensibility;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>アプリの一生の入口（アプリの一生のエンジンの Lifecycle の部分）</summary>
/// <remarks>
/// SUT は複数のプロジェクトにまたがる：
/// QuickER.Gui.Abstractions（<c>ShutdownSteps</c>）、
/// QuickER.Gui（<c>FeatureModuleShutdown</c>・<c>FeatureModuleShutdownOnce</c>・<c>PostExitActions</c>・
/// <c>CrashHandlingService</c>・<c>CrashLogShutdownFailureReporter</c>・<c>UpdateFeed</c>・<c>UpdateService</c>・
/// <c>LanguageSwitchViewModel</c>）。
/// </remarks>
internal enum LifecycleEntrance
{
    /// <summary><c>ShutdownSteps.Run</c>（終了の連鎖の 1 段を受け止める）</summary>
    ShutdownStepsRun,

    /// <summary><c>FeatureModuleShutdown.CloseAll</c>（モジュールごとに受け止める）</summary>
    ModuleCloseAll,

    /// <summary><c>FeatureModuleShutdownOnce.CloseAll</c>（プロセスで 1 回だけ）</summary>
    ModuleShutdownOnce,

    /// <summary><c>PostExitActions.CompleteExit</c>（DI の破棄から登録された処理まで）</summary>
    CompleteExit,

    /// <summary><c>CrashHandlingService.HandleCrash</c></summary>
    HandleCrash,

    /// <summary><c>CrashHandlingService.HandleUiCrash</c></summary>
    HandleUiCrash,

    /// <summary><c>CrashHandlingService.WriteCrashLog</c></summary>
    WriteCrashLog,

    /// <summary><c>CrashLogShutdownFailureReporter.Report</c></summary>
    ShutdownFailureReport,

    /// <summary><c>UpdateFeed.Resolve</c></summary>
    UpdateFeedResolve,

    /// <summary><c>UpdateService.CheckOnStartupAsync</c></summary>
    CheckOnStartup,

    /// <summary><c>PostExitActions.Run</c></summary>
    PostExitRun,

    /// <summary><c>LanguageSwitchViewModel.SelectLanguageCommand</c></summary>
    LanguageSelect,
}

/// <summary>入口の段の失敗・利用者の応答・前提の形（次元表の因子 Fault の水準）</summary>
internal enum LifecycleFault
{
    /// <summary>失敗も特別な前提もない</summary>
    None,

    /// <summary>段（処理）が投げる</summary>
    StepThrows,

    /// <summary>複数の段が投げる</summary>
    MultipleStepsThrow,

    /// <summary>記録先が投げる</summary>
    ReporterThrows,

    /// <summary>ダイアログが投げる</summary>
    DialogThrows,

    /// <summary>緊急保存が投げる</summary>
    EmergencySaveThrows,

    /// <summary>後始末がハングする</summary>
    HangingCleanup,

    /// <summary>再入・2 回目の呼び出し</summary>
    Reentry,

    /// <summary>書けない保存先</summary>
    UnwritablePath,

    /// <summary>同じ秒の連続</summary>
    SameSecond,

    /// <summary>設定がオフ</summary>
    SettingOff,

    /// <summary>更新のフィードが無い</summary>
    NoFeed,

    /// <summary>インストールされていない実行</summary>
    NotInstalled,

    /// <summary>更新が無い</summary>
    NoUpdate,

    /// <summary>利用者が更新を辞退する</summary>
    Declined,

    /// <summary>利用者が「今すぐ再起動」を選ぶ</summary>
    RestartNow,

    /// <summary>利用者が「後で」を選ぶ</summary>
    RestartLater,

    /// <summary>終了が取り消される（<c>PostExitActions.Run</c> が呼ばれない）</summary>
    ShutdownCancelled,

    /// <summary>ダウンロードが失敗する</summary>
    DownloadFails,

    /// <summary>更新の確認が失敗する</summary>
    CheckFails,

    /// <summary>更新の適用が失敗する</summary>
    ApplyFails,

    /// <summary>単一インスタンスの座の手放しが失敗する</summary>
    ReleaseFails,

    /// <summary>起動処理が登録されていない</summary>
    NoStarter,

    /// <summary>更新の適用と再起動の要求が両方ある</summary>
    UpdateAndRelaunch,

    /// <summary>フィードの環境変数が非空</summary>
    FeedEnvironmentSet,

    /// <summary>フィードの環境変数が空白だけ</summary>
    FeedEnvironmentBlank,

    /// <summary>今と同じ言語を選ぶ</summary>
    SameLanguage,

    /// <summary>設定を保存できない</summary>
    SaveFails,

    /// <summary>再起動の確認を取り消す</summary>
    ConfirmDeclined,
}

/// <summary>1 行を流した観測</summary>
internal sealed class LifecycleObservation : IDisposable
{
    private readonly List<Action> _disposers = [];
    private TestTempFolder? _temp;

    /// <summary>入口が外へ出した例外</summary>
    public Exception? Thrown { get; set; }

    /// <summary>呼び出しの記録</summary>
    public List<string> Calls { get; } = [];

    /// <summary>終了の連鎖で受け止めた失敗の記録先</summary>
    public RecordingShutdownFailureReporter Reporter { get; } = new();

    /// <summary>ダイアログの記録</summary>
    public StubDialogService Dialogs { get; } = new();

    /// <summary>行の中で作ったものの受け渡し（Act から Expect へ）</summary>
    public Dictionary<string, object?> Bag { get; } = new(StringComparer.Ordinal);

    /// <summary>一時フォルダ（初めて読んだときに作り、行の終わりに消す）</summary>
    public string Folder => (_temp ??= new TestTempFolder()).Path;

    /// <summary>行の終わりに実行する後始末を登録する（静的な状態の復元・解放など）</summary>
    public void OnDispose(Action action) => _disposers.Add(action);

    /// <summary>受け渡しの値を型つきで取り出す</summary>
    public T Get<T>(string key) => (T)Bag[key]!;

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var action in Enumerable.Reverse(_disposers))
        {
            action();
        }

        _temp?.Dispose();
    }
}

/// <summary>アプリの一生の表の 1 行の入力</summary>
/// <param name="Entrance">入口</param>
/// <param name="Fault">段の失敗・前提の形</param>
/// <param name="Act">入口を呼ぶ（例外は流す側が受け止めて観測へ入れる）</param>
/// <param name="Expect">元のテストの表明</param>
internal sealed record LifecycleCase(
    LifecycleEntrance Entrance,
    LifecycleFault Fault,
    Func<LifecycleObservation, Task> Act,
    Action<LifecycleObservation> Expect
);

/// <summary>アプリの一生の表（入口 × 段の失敗・前提の形 → 振る舞い）</summary>
/// <remarks>
/// 行は元の単発のテストと同じ入力・同じ表明を持つ（主題ごとのファイル：<c>LifecycleRows.Shutdown.cs</c>・
/// <c>LifecycleRows.Crash.cs</c>・<c>LifecycleRows.Update.cs</c>・<c>LifecycleRows.PostExit.cs</c>・
/// <c>LifecycleRows.Language.cs</c>）。流す側（<see cref="LifecycleRunner"/>）は全行に「入口が例外を外へ出さない」を掛ける。
/// </remarks>
internal static partial class LifecycleRows
{
    /// <summary>表</summary>
    public static ScenarioTable<LifecycleCase> Table =>
        new(
            EngineIds.OsBoundary,
            [
                .. ShutdownRows(),
                .. CrashRows(),
                .. UpdateRows(),
                .. PostExitRows(),
                .. LanguageRows(),
            ]
        );

    /// <summary>行を作る（同期の入口）</summary>
    private static ScenarioRow<LifecycleCase> Row(
        string id,
        string description,
        LifecycleEntrance entrance,
        LifecycleFault fault,
        Action<LifecycleObservation> act,
        Action<LifecycleObservation> expect
    ) =>
        AsyncRow(
            id,
            description,
            entrance,
            fault,
            o =>
            {
                act(o);
                return Task.CompletedTask;
            },
            expect
        );

    /// <summary>行を作る（非同期の入口）</summary>
    private static ScenarioRow<LifecycleCase> AsyncRow(
        string id,
        string description,
        LifecycleEntrance entrance,
        LifecycleFault fault,
        Func<LifecycleObservation, Task> act,
        Action<LifecycleObservation> expect
    ) => new(id, null, description, new LifecycleCase(entrance, fault, act, expect));

    /// <summary>後始末の処理を差し込める検証用モジュール（呼ばれた順に Id を記録する）</summary>
    private sealed class ClosingModule(string id, List<string> log, Action? onClosing = null)
        : IFeatureModule
    {
        public string Id { get; } = id;

        public void ConfigureServices(IServiceCollection services) { }

        public IReadOnlyList<FeatureToolbarItem> CreateToolbarItems(IServiceProvider services) =>
            [];

        public void OnMainWindowClosing(IServiceProvider services)
        {
            log.Add(Id);
            onClosing?.Invoke();
        }
    }
}

/// <summary>アプリの一生の表の行を流す</summary>
internal static class LifecycleRunner
{
    /// <summary>1 行を流す（入口が例外を外へ出さないことの照合 → 行の表明）。常に true</summary>
    public static async Task<bool> RunAsync(LifecycleCase row)
    {
        using var observation = new LifecycleObservation();

        try
        {
            await row.Act(observation);
        }
        catch (Exception ex)
        {
            observation.Thrown = ex;
        }

        observation.Thrown.Should().BeNull("アプリの一生の入口は例外を外へ出さない");
        row.Expect(observation);
        return true;
    }
}
