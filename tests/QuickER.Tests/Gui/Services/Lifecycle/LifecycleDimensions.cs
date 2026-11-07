using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>アプリの一生の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 入口 × 段の失敗・前提の形を数える。その入口に無い段の失敗（<c>ShutdownSteps.Run</c> に「ダウンロードの失敗」など）は
/// 原理的に起こり得ないので <c>isValid</c> で外す（<see cref="FaultsOf"/>）。
/// </remarks>
internal static class LifecycleDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "os-boundary-lifecycle";

    /// <summary>入口</summary>
    public const string Entrance = "Entrance";

    /// <summary>段の失敗・前提の形</summary>
    public const string Fault = "Fault";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.OsBoundary,
            [
                new Factor(
                    Entrance,
                    Enum.GetValues<LifecycleEntrance>()
                        .Select(e => new Level(e.ToString()))
                        .ToList(),
                    "アプリの一生の入口"
                ),
                new Factor(
                    Fault,
                    Enum.GetValues<LifecycleFault>().Select(f => new Level(f.ToString())).ToList(),
                    "段の失敗・利用者の応答・前提の形"
                ),
            ],
            isValid: IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    /// <remarks>
    /// 領域の中の網羅すべき組は行ですべて流すので、残るのは配布ビルドで埋められない受け入れの 1 組だけ（<c>#if !DEBUG</c>）。
    /// </remarks>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.OsBoundary,
            [
#if !DEBUG
                // 配布ビルドでは環境変数による差し替えの分岐ごとコンパイルされない（src/QuickER.Gui/Services/UpdateFeed.cs の #if DEBUG）。
                // 空白の環境変数を定数のフィードへ戻す扱いは Debug ビルドにしか存在しないので、Release では埋められない
                CoverageGap.AcceptedTuple(
                    Scope,
                    "P3-10",
                    "環境変数による差し替えは #if DEBUG 限定（src/QuickER.Gui/Services/UpdateFeed.cs）で、配布ビルドには空白の環境変数を扱う分岐が無い",
                    (Entrance, nameof(LifecycleEntrance.UpdateFeedResolve)),
                    (Fault, nameof(LifecycleFault.FeedEnvironmentBlank))
                ),
#endif
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.OsBoundary,
            "Lifecycle",
            "アプリの一生の表（入口 × 段の失敗・前提の形。終了の連鎖・クラッシュ・更新・正常終了の最後の処理・言語の切替）",
            Table,
            LifecycleRows.Table.Count,
            [
                "tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleDimensions.cs",
                "tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.cs",
                "tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.Shutdown.cs",
                "tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.Faults.cs",
                "tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.Crash.cs",
                "tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.Update.cs",
                "tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.PostExit.cs",
                "tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.Language.cs",
                "tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleTests.cs",
            ],
            [
                "src/QuickER.Gui.Abstractions/ShutdownSteps.cs",
                "src/QuickER.Gui/Services/FeatureModuleShutdown.cs",
                "src/QuickER.Gui/Services/PostExitActions.cs",
                "src/QuickER.Gui/Services/CrashHandlingService.cs",
                "src/QuickER.Gui/Services/CrashLogShutdownFailureReporter.cs",
                "src/QuickER.Gui/Services/UpdateFeed.cs",
                "src/QuickER.Gui/Services/UpdateService.cs",
                "src/QuickER.Gui/ViewModels/LanguageSwitchViewModel.cs",
            ]
        );

    /// <summary>1 行を流した後、入口と失敗の形を数える</summary>
    public static void Observe(CoverageCounter counter, LifecycleCase row) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Entrance] = row.Entrance.ToString(),
                [Fault] = row.Fault.ToString(),
            }
        );

    /// <summary>入口が持つ段の失敗・前提の形（それ以外はその入口に無い段の失敗）</summary>
    public static IReadOnlyCollection<LifecycleFault> FaultsOf(LifecycleEntrance entrance) =>
        entrance switch
        {
            LifecycleEntrance.ShutdownStepsRun =>
            [
                LifecycleFault.None,
                LifecycleFault.StepThrows,
                LifecycleFault.MultipleStepsThrow,
                LifecycleFault.ReporterThrows,
            ],
            LifecycleEntrance.ModuleCloseAll =>
            [
                LifecycleFault.None,
                LifecycleFault.StepThrows,
                LifecycleFault.MultipleStepsThrow,
                LifecycleFault.ReporterThrows,
            ],
            LifecycleEntrance.ModuleShutdownOnce =>
            [
                LifecycleFault.None,
                LifecycleFault.StepThrows,
                LifecycleFault.ReporterThrows,
                LifecycleFault.Reentry,
            ],
            LifecycleEntrance.CompleteExit =>
            [
                LifecycleFault.None,
                LifecycleFault.StepThrows,
                LifecycleFault.ReporterThrows,
                LifecycleFault.ApplyFails,
            ],
            LifecycleEntrance.HandleCrash =>
            [
                LifecycleFault.None,
                LifecycleFault.StepThrows,
                LifecycleFault.EmergencySaveThrows,
                LifecycleFault.DialogThrows,
                LifecycleFault.HangingCleanup,
                LifecycleFault.Reentry,
                LifecycleFault.UnwritablePath,
            ],
            LifecycleEntrance.HandleUiCrash =>
            [
                LifecycleFault.None,
                LifecycleFault.StepThrows,
                LifecycleFault.EmergencySaveThrows,
                LifecycleFault.DialogThrows,
                LifecycleFault.HangingCleanup,
                LifecycleFault.Reentry,
                LifecycleFault.UnwritablePath,
            ],
            LifecycleEntrance.WriteCrashLog =>
            [
                LifecycleFault.None,
                LifecycleFault.UnwritablePath,
                LifecycleFault.SameSecond,
            ],
            LifecycleEntrance.ShutdownFailureReport =>
            [
                LifecycleFault.None,
                LifecycleFault.UnwritablePath,
                LifecycleFault.SameSecond,
            ],
            LifecycleEntrance.UpdateFeedResolve =>
            [
                LifecycleFault.None,
                LifecycleFault.FeedEnvironmentSet,
                LifecycleFault.FeedEnvironmentBlank,
            ],
            LifecycleEntrance.CheckOnStartup =>
            [
                LifecycleFault.None,
                LifecycleFault.SettingOff,
                LifecycleFault.NoFeed,
                LifecycleFault.NotInstalled,
                LifecycleFault.NoUpdate,
                LifecycleFault.Declined,
                LifecycleFault.RestartNow,
                LifecycleFault.RestartLater,
                LifecycleFault.ShutdownCancelled,
                LifecycleFault.DownloadFails,
                LifecycleFault.CheckFails,
            ],
            LifecycleEntrance.PostExitRun =>
            [
                LifecycleFault.None,
                LifecycleFault.ReporterThrows,
                LifecycleFault.Reentry,
                LifecycleFault.ApplyFails,
                LifecycleFault.ReleaseFails,
                LifecycleFault.NoStarter,
                LifecycleFault.UpdateAndRelaunch,
            ],
            LifecycleEntrance.LanguageSelect =>
            [
                LifecycleFault.None,
                LifecycleFault.SameLanguage,
                LifecycleFault.SaveFails,
                LifecycleFault.ConfirmDeclined,
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(entrance), entrance, null),
        };

    /// <summary>原理的に起こり得ない組：その入口に無い段の失敗</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        if (
            !assignment.TryGetValue(Entrance, out var entrance)
            || !assignment.TryGetValue(Fault, out var fault)
        )
        {
            return true;
        }

        return FaultsOf(Enum.Parse<LifecycleEntrance>(entrance))
            .Contains(Enum.Parse<LifecycleFault>(fault));
    }
}
