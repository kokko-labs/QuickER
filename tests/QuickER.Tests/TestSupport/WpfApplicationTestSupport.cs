using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AwesomeAssertions;
using QuickER.Converters;

namespace QuickER.Tests.TestSupport;

/// <summary>WPF 依存テストの共有ヘルパー（Application・App レベルリソースの供給と STA 実行）</summary>
/// <remarks>
/// xunit はテストクラスを並列実行するため、複数の STA テストが同時に
/// 「<see cref="Application.Current"/> が null なら生成」を行うと、二重生成の競合で
/// InvalidOperationException（同一 AppDomain では複数 Application を生成不可）が
/// 散発的に発生する。生成と初期リソース登録をロックで直列化して競合を防ぐ。
/// </remarks>
internal static class WpfApplicationTestSupport
{
    /// <summary>WPF 依存の検証を STA スレッド上で実行し、例外があれば失敗として報告する</summary>
    public static void RunSta(Action action)
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        captured.Should().BeNull(captured?.ToString());
    }

    /// <summary>Application 生成・リソース登録を直列化するためのロック</summary>
    private static readonly object Gate = new();

    /// <summary>XAML（BAML）ロードを直列化するためのロック</summary>
    private static readonly object XamlLoadGate = new();

    /// <summary>
    /// ダイアログ等の XAML（BAML）ロードを伴う生成を直列化して実行する。
    /// <see cref="Application.LoadComponent(object, Uri)"/> が使う System.IO.Packaging は
    /// スレッドセーフでないため、複数の STA テストクラスが同一アセンブリのダイアログを
    /// 並列に生成すると PackagePart の内部リスト操作が競合して散発的に失敗する。
    /// </summary>
    /// <param name="factory">ダイアログ等を生成するファクトリ（InitializeComponent を含む）</param>
    public static T LoadXamlComponent<T>(Func<T> factory)
    {
        lock (XamlLoadGate)
        {
            return factory();
        }
    }

    /// <summary>Application を必要なら生成し、BAML が参照する App レベルリソースを登録する</summary>
    public static void EnsureApplicationResources()
    {
        lock (Gate)
        {
            if (Application.Current is null)
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }

            var resources = Application.Current!.Resources;

            if (!resources.Contains("BoolToVisibilityConverter"))
            {
                resources.Add("BoolToVisibilityConverter", new BooleanToVisibilityConverter());
            }

            if (!resources.Contains("NullToVisibilityConverter"))
            {
                resources.Add("NullToVisibilityConverter", new NullToVisibilityConverter());
            }

            if (!resources.Contains("NullToBooleanConverter"))
            {
                resources.Add("NullToBooleanConverter", new NullToBooleanConverter());
            }

            if (!resources.Contains("CountToVisibilityConverter"))
            {
                resources.Add("CountToVisibilityConverter", new CountToVisibilityConverter());
            }
        }
    }
}
