namespace QuickER.Tests.TestSupport.Guards;

/// <summary>規則の自己検査の表：アプリ（App・Program の配線・ダイアログ・XAML）</summary>
internal static partial class SourceRuleProbes
{
    private static IEnumerable<SourceRuleProbe> Gui() =>
        [
            // 名前空間の接頭辞を問わず DataGrid の要素を拾い、2 属性のどちらかが "False" の明示でないもの（属性なし・True・
            // 小文字の false）を拾う。2 属性とも "False" のものと、名前が DataGrid で始まる別の要素は拾わない
            new SourceRuleProbe(
                "datagrid-row-edits-forms",
                "datagrid-row-edits",
                [
                    new ProbeFile(
                        "Probe.xaml",
                        """
                        <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                     xmlns:c="clr-namespace:Controls">
                          <StackPanel>
                            <DataGrid x:Name="Ok" CanUserAddRows="False" CanUserDeleteRows="False" />
                            <DataGrid x:Name="NoDelete" CanUserAddRows="False" />
                            <DataGrid x:Name="AddTrue" CanUserAddRows="True" CanUserDeleteRows="False" />
                            <c:DataGrid CanUserAddRows="false" CanUserDeleteRows="False" />
                            <DataGridTextColumn Header="x" />
                          </StackPanel>
                        </UserControl>
                        """,
                        "行の追加・削除を切っていない DataGrid（x:Name=NoDelete）",
                        "行の追加・削除を切っていない DataGrid（x:Name=AddTrue）",
                        "行の追加・削除を切っていない DataGrid（x:Name=）"
                    ),
                ]
            ),
            // 呼び出しの形（括弧つき）を 1 出現 1 件拾い、cref の名前（括弧なし）は拾わない
            new SourceRuleProbe(
                "dialog-owner-resolve-uses-forms",
                "dialog-owner-resolve-uses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        /// <see cref="DialogOwner.Resolve"/>
                        internal static class Probe
                        {
                            public static object? A() => DialogOwner.Resolve();
                            public static object? B() => DialogOwner.Resolve() ?? DialogOwner.Resolve();
                        }
                        """,
                        "DialogOwner.Resolve()",
                        "DialogOwner.Resolve()",
                        "DialogOwner.Resolve()"
                    ),
                ]
            ),
            // オーナーなしの表示（== の前の空白・改行を問わない）を拾い、オーナーを渡す表示と条件演算子の分岐は拾わない
            new SourceRuleProbe(
                "file-dialog-ownerless-shows-forms",
                "file-dialog-ownerless-shows",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static bool A(Dialog dialog) => dialog.ShowDialog() == true;
                            public static bool B(Dialog dialog) => dialog.ShowDialog()
                                == true;
                            public static bool C(Dialog dialog, object owner) => dialog.ShowDialog(owner) == true;
                            public static bool D(Dialog dialog, object? owner) =>
                                (owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true;
                        }
                        """,
                        "dialog.ShowDialog() ==",
                        "dialog.ShowDialog() =="
                    ),
                ]
            ),
            // 自前の解決の宣言を拾い、共有の解決の呼び出しは拾わない
            new SourceRuleProbe(
                "message-box-owner-resolver-copies-forms",
                "message-box-owner-resolver-copies",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            private static Window? ResolveOwner() => null;
                            private static Window? Owner() => DialogOwner.Resolve();
                        }
                        """,
                        "private static Window? ResolveOwner"
                    ),
                ]
            ),
            // XAML を文字列で読み、宣言の表記を拾う。別の値・空白の入った表記は拾わない
            new SourceRuleProbe(
                "app-shutdown-mode-declaration-forms",
                "app-shutdown-mode-declaration",
                [
                    new ProbeFile(
                        "App.xaml",
                        """
                        <Application x:Class="QuickER.App" ShutdownMode="OnMainWindowClose">
                          <!-- ShutdownMode="OnLastWindowClose" / ShutdownMode = "OnMainWindowClose" -->
                        </Application>
                        """,
                        "ShutdownMode=\"OnMainWindowClose\""
                    ),
                ]
            ),
            // コードでの代入（空白の有無を問わない）を拾い、= の続かない参照は拾わない。比較（==）も拾う
            // （元の正規表現のまま。取りこぼさない側）
            new SourceRuleProbe(
                "app-shutdown-mode-overrides-forms",
                "app-shutdown-mode-overrides",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal sealed class Probe : Application
                        {
                            public void A() => ShutdownMode = ShutdownMode.OnLastWindowClose;
                            public void B() => this.ShutdownMode=ShutdownMode.OnExplicitShutdown;
                            public bool C() => ShutdownMode == ShutdownMode.OnMainWindowClose;
                            public object D() => DefaultShutdownMode;
                        }
                        """,
                        "ShutdownMode =",
                        "ShutdownMode =",
                        "ShutdownMode ="
                    ),
                ]
            ),
            // MainWindow への window の代入（空白を問わない・this. つきも）を拾い、別の変数・別の名前の代入は拾わない
            new SourceRuleProbe(
                "app-main-window-assignment-forms",
                "app-main-window-assignment",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal sealed class Probe : Application
                        {
                            public void A(Window window) => MainWindow = window;
                            public void B(Window window) => this.MainWindow  =  window ;
                            public void C(Window other) => MainWindow = other;
                            public void D(Window window) => OldMainWindow = window;
                        }
                        """,
                        "MainWindow = window;",
                        "MainWindow = window;"
                    ),
                ]
            ),
            // 2 つの表記を本文の順に拾い、整形の違う表記は拾わない（文字列で一字一句を見る）
            new SourceRuleProbe(
                "app-feature-module-shutdown-uses-forms",
                "app-feature-module-shutdown-uses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        var featureModuleShutdown = new FeatureModuleShutdownOnce(modules, reporter);
                        window.Closing += (_, _) => featureModuleShutdown.CloseAll();
                        window.Closing += (_, _) =>featureModuleShutdown.CloseAll();
                        """,
                        "new FeatureModuleShutdownOnce(",
                        "window.Closing += (_, _) => featureModuleShutdown.CloseAll();"
                    ),
                ]
            ),
            // 直接の呼び出し（括弧の前の空白を問わない）を拾い、共有の部品の呼び出しは拾わない
            new SourceRuleProbe(
                "app-feature-module-shutdown-bypasses-forms",
                "app-feature-module-shutdown-bypasses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        module.OnMainWindowClosing();
                        module.OnMainWindowClosing (window);
                        featureModuleShutdown.CloseAll();
                        """,
                        ".OnMainWindowClosing(",
                        ".OnMainWindowClosing("
                    ),
                ]
            ),
            // 2 つの表記を本文の順に拾う
            new SourceRuleProbe(
                "app-ui-crash-handler-uses-forms",
                "app-ui-crash-handler-uses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        CrashHandlingService.HandleUiCrash(e.Exception, () => _featureModuleShutdown?.CloseAll());
                        _featureModuleShutdown.CloseAll();
                        """,
                        "CrashHandlingService.HandleUiCrash(",
                        "_featureModuleShutdown?.CloseAll()"
                    ),
                ]
            ),
            // 再入を判断しない HandleCrash と行頭の Environment.Exit を拾い、HandleUiCrash と行の途中の Exit は拾わない
            new SourceRuleProbe(
                "app-ui-crash-handler-bypasses-forms",
                "app-ui-crash-handler-bypasses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        CrashHandlingService.HandleCrash(e.Exception);
                            Environment.Exit(1);
                        CrashHandlingService.HandleUiCrash(e.Exception, cleanup);
                        if (handled) Environment.Exit(1);
                        """,
                        "CrashHandlingService.HandleCrash(",
                        "行頭の Environment.Exit("
                    ),
                ]
            ),
            // 文字列で読む規則は、条件コンパイルの外れる分岐の中の表記も拾う
            new SourceRuleProbe(
                "app-ui-crash-handler-bypasses-inactive-branch",
                "app-ui-crash-handler-bypasses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static void OnCrash()
                            {
                        #if QUICKER_PROBE_BRANCH
                                Environment.Exit(1);
                        #endif
                            }
                        }
                        """,
                        "行頭の Environment.Exit("
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 3 つの表記を本文の順に拾う
            new SourceRuleProbe(
                "program-single-instance-uses-forms",
                "program-single-instance-uses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        if (outcome == SingleInstanceOutcome.Inaccessible) { Report(); }
                        actions.ReleaseSingleInstanceBeforeRelaunch(singleInstance.Dispose);
                        actions.RelaunchWith(WpfApplicationRestartService.StartNewInstance);
                        actions.RelaunchWith(Restart);
                        """,
                        "outcome == SingleInstanceOutcome.Inaccessible",
                        "ReleaseSingleInstanceBeforeRelaunch(singleInstance.Dispose)",
                        "RelaunchWith(WpfApplicationRestartService.StartNewInstance)"
                    ),
                ]
            ),
            // 文字列で読む必須の規則は、条件コンパイルの外れる分岐の中の表記でも満たされる（偽の緑になりうる側。読み先はいま
            // #if を持たない）
            new SourceRuleProbe(
                "program-single-instance-uses-inactive-branch",
                "program-single-instance-uses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static void Main()
                            {
                        #if QUICKER_PROBE_BRANCH
                                actions.ReleaseSingleInstanceBeforeRelaunch(singleInstance.Dispose);
                        #endif
                            }
                        }
                        """,
                        "ReleaseSingleInstanceBeforeRelaunch(singleInstance.Dispose)"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 結果を受け取らない形を拾い、引数を渡す形は拾わない
            new SourceRuleProbe(
                "program-single-instance-bypasses-forms",
                "program-single-instance-bypasses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using var guard = SingleInstanceGuard.TryAcquire();
                        using var other = SingleInstanceGuard.TryAcquire(Activate, out var outcome);
                        """,
                        "SingleInstanceGuard.TryAcquire()"
                    ),
                ]
            ),
        ];
}
