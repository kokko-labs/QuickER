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
    /// どれも元の単発のテストが持たず、P3-10 では新しい期待を作って行を足していない組。足すなら表の行で足し、ここから消す。
    /// </remarks>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.OsBoundary,
            [
                Group(
                    "終了の連鎖の器（ShutdownSteps・FeatureModuleShutdown）の記録先が投げる形は、元のテストが器の入口でだけ持つ。"
                        + "モジュールの入口・1 回だけの入口へ記録先の失敗を重ねた期待は元に無い",
                    (LifecycleEntrance.ModuleCloseAll, [LifecycleFault.ReporterThrows]),
                    (LifecycleEntrance.ModuleShutdownOnce, [LifecycleFault.ReporterThrows])
                ),
                Group(
                    "モジュールの後始末は、元のテストが失敗なしと 2 つが失敗する形だけを持つ（1 つだけが失敗する形の期待は元に無い）。"
                        + "1 回だけの入口も、元のテストは 2 回呼ぶ形（Reentry）でだけ見ている",
                    (LifecycleEntrance.ModuleCloseAll, [LifecycleFault.StepThrows]),
                    (
                        LifecycleEntrance.ModuleShutdownOnce,
                        [LifecycleFault.StepThrows, LifecycleFault.None]
                    )
                ),
                Group(
                    "DI の破棄から登録された処理までの連鎖は、元のテストが破棄の失敗の 1 形だけを持つ。"
                        + "記録先が投げる・適用が失敗する形の期待は元に無い",
                    (
                        LifecycleEntrance.CompleteExit,
                        [
                            LifecycleFault.None,
                            LifecycleFault.ReporterThrows,
                            LifecycleFault.ApplyFails,
                        ]
                    )
                ),
                Group(
                    "クラッシュ処理の入口は、元のテストが持つ失敗の形だけを行にしている。"
                        + "ログを書けない保存先での緊急保存・ダイアログの受け渡しや、UI の入口への失敗の重ね合わせの期待は元に無い",
                    (LifecycleEntrance.HandleCrash, [LifecycleFault.UnwritablePath]),
                    (
                        LifecycleEntrance.HandleUiCrash,
                        [
                            LifecycleFault.None,
                            LifecycleFault.StepThrows,
                            LifecycleFault.EmergencySaveThrows,
                            LifecycleFault.DialogThrows,
                            LifecycleFault.HangingCleanup,
                            LifecycleFault.UnwritablePath,
                        ]
                    )
                ),
                Group(
                    "クラッシュログの書き出しの同じ時刻を選ぶ同時の書き手は、元のクラスに別の Fact として残る（このフェーズの外）。"
                        + "記録先の入口の同じ秒の連続の期待は元に無い",
                    (LifecycleEntrance.ShutdownFailureReport, [LifecycleFault.SameSecond])
                ),
                Group(
                    "終了の最後の処理の入口は、元のテストが持つ失敗の形だけを行にしている。記録先が投げる形の期待は元に無い",
                    (LifecycleEntrance.PostExitRun, [LifecycleFault.ReporterThrows])
                ),
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

    /// <summary>入口ごとに形を名指しした組をまとめて 1 つの項目にする（理由が同じ組）</summary>
    private static CoverageGapEntry Group(
        string reason,
        params (LifecycleEntrance Entrance, LifecycleFault[] Faults)[] pairs
    )
    {
        var set = pairs
            .SelectMany(p => p.Faults.Select(f => (p.Entrance.ToString(), f.ToString())))
            .ToHashSet();
        return CoverageGap.Matching(
            Scope,
            "P3-10",
            "P4",
            reason,
            Entrance,
            Fault,
            (entrance, fault) => set.Contains((entrance, fault))
        );
    }

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
