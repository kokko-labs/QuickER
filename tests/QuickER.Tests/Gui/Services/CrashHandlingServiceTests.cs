using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Services;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// クラッシュ処理（<see cref="CrashHandlingService"/>）の整形・ログ書き出しと、App.xaml.cs の配線を検証するテストクラス。
/// </summary>
/// <remarks>
/// 緊急保存・ログ・後始末・ダイアログの段の順と失敗の隔離・再入・ログの書き出しの行は
/// <c>Gui/Services/Lifecycle/LifecycleRows.Crash.cs</c> へ移した。ここに残すのは整形の純関数・
/// 同じ時刻の並行の書き込み（OS の排他）・アセンブリ属性・App.xaml.cs の配線のソースの検査。
/// 実ファイル書き込みは一時フォルダへ隔離し、ユーザーの <c>%LOCALAPPDATA%\QuickER</c> を汚さない。
/// </remarks>
public class CrashHandlingServiceTests
{
    /// <summary>入れ子の例外（内側に根本原因を持つ）を組み立てる</summary>
    private static Exception CreateNestedException() =>
        new InvalidOperationException("外側の失敗", new FileNotFoundException("内側の根本原因"));

    /// <summary>詳細整形に例外型・メッセージ・内部例外・バージョンが含まれることを検証する</summary>
    [Fact(DisplayName = "FormatDetails: 例外連鎖・バージョンが本文へ含まれる")]
    public void FormatDetails_ContainsExceptionChainAndVersion()
    {
        var details = CrashHandlingService.FormatDetails(CreateNestedException(), "9.9.9+abcdef");

        details.Should().Contain("9.9.9+abcdef");
        details.Should().Contain("System.InvalidOperationException");
        details.Should().Contain("外側の失敗");
        details.Should().Contain("System.IO.FileNotFoundException");
        details.Should().Contain("内側の根本原因");
    }

    /// <summary>スタックトレースを持つ実例外で、トレース本文が詳細へ含まれることを検証する</summary>
    [Fact(DisplayName = "FormatDetails: 実際に投げられた例外のスタックトレースを含む")]
    public void FormatDetails_ContainsStackTrace()
    {
        Exception captured;

        try
        {
            throw new InvalidOperationException("投げられた例外");
        }
        catch (InvalidOperationException ex)
        {
            captured = ex;
        }

        var details = CrashHandlingService.FormatDetails(captured, "0.0.1");

        details.Should().Contain(nameof(FormatDetails_ContainsStackTrace));
    }

    /// <summary>取りこぼした Task の例外（AggregateException）の内側を、2 件目以降も含めて全件出すことを検証する</summary>
    [Fact(DisplayName = "FormatDetails: AggregateException の内側を全件出す")]
    public void FormatDetails_AggregateException_ListsEveryInnerException()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("1 件目の失敗"),
            new AggregateException(
                new TimeoutException("入れ子の 1 件目"),
                new IOException("入れ子の 2 件目", new FileNotFoundException("最内段"))
            )
        );

        var details = CrashHandlingService.FormatDetails(aggregate, "1.2.3");

        details.Should().Contain("1 件目の失敗");
        details.Should().Contain("入れ子の 1 件目");
        details.Should().Contain("入れ子の 2 件目");
        details.Should().Contain("最内段");
        details.Should().Contain("[Inner exception (1) [2/2]]");
        details.Should().Contain("[Inner exception (2) [2/2]]");
    }

    /// <summary>同じ時刻の名前を選ぶ同時の書き手が、互いの記録を上書きしないことを検証する</summary>
    /// <remarks>
    /// 「存在確認 → 上書き書き込み」では、確認と書き込みの間に同じ名前を選んだ別の書き手と後勝ちになる。
    /// 時刻を固定して同じ名前を選ばせ、全員の記録が別々のファイルとして残ることを確かめる。
    /// </remarks>
    [Fact(DisplayName = "WriteCrashLog: 同じ名前を選ぶ同時の書き手でも互いを上書きしない")]
    public void WriteCrashLog_ConcurrentWritersWithSameTimestamp_DoNotOverwrite()
    {
        var folder = CreateTempFolder();
        const int writers = 64;

        try
        {
            using var start = new Barrier(writers);
            var paths = new string?[writers];

            // スレッドプールの増加を待たずに全員を同時に走らせるため、専用スレッドを使う
            var threads = Enumerable
                .Range(0, writers)
                .Select(i => new Thread(() =>
                {
                    start.SignalAndWait();
                    paths[i] = CrashHandlingService.WriteCrashLog(
                        new InvalidOperationException($"writer-{i:D2}"),
                        "1.2.3",
                        folder,
                        CrashHandlingService.CrashLogPrefix,
                        "20261003-120000-000"
                    );
                }))
                .ToList();

            threads.ForEach(thread => thread.Start());
            threads.ForEach(thread => thread.Join());

            paths.Should().OnlyContain(path => path != null);
            paths.Distinct().Should().HaveCount(writers);
            Directory.GetFiles(folder, "crash-*.log").Should().HaveCount(writers);

            for (var i = 0; i < writers; i++)
            {
                File.ReadAllText(paths[i]!).Should().Contain($"writer-{i:D2}");
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>App の UI 例外の受け皿が、再入を判断する HandleUiCrash と後始末の共有インスタンスを使うことをソース上で固定する</summary>
    /// <remarks>
    /// <c>App</c> は WPF の <c>Application</c> 派生でテストから実体化できないため、判断を切り出した
    /// <see cref="CrashHandlingService.HandleUiCrash"/> 側を直接検証する。その形が意味を持つのは <c>App</c> が
    /// 確かにそれを使っているときだけなので、無条件の <c>Environment.Exit</c> へ戻っていないことをここで見張る。
    /// </remarks>
    [Fact(DisplayName = "App の UI 例外の受け皿は HandleUiCrash と後始末の共有インスタンスを使う")]
    public void App_UsesHandleUiCrashWithSharedModuleShutdown()
    {
        var source = File.ReadAllText(
            Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src", "QuickER.Gui", "App.xaml.cs")
        );

        source.Should().Contain("CrashHandlingService.HandleUiCrash(");
        source.Should().Contain("_featureModuleShutdown?.CloseAll()");
        source.Should().NotContain("CrashHandlingService.HandleCrash(");
        Regex
            .Matches(source, @"^\s*Environment\.Exit\(", RegexOptions.Multiline)
            .Should()
            .BeEmpty("終了は HandleUiCrash が処理を担ったときだけ行う");
    }

    /// <summary>バージョン解決が空でない文字列を返すことを検証する（属性欠落時も既定表記へ落ちる）</summary>
    [Fact(DisplayName = "ResolveAppVersion: 空でないバージョン文字列を返す")]
    public void ResolveAppVersion_ReturnsNonEmpty()
    {
        CrashHandlingService.ResolveAppVersion().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>テスト隔離用の一時フォルダを作成する</summary>
    private static string CreateTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"quicker-crash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
