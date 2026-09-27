using System.IO;
using AwesomeAssertions;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 終了の連鎖で受け止めた失敗の記録先（<see cref="CrashLogShutdownFailureReporter"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 実ファイル書き込みは一時フォルダへ隔離し（コンストラクタの <c>logBaseDirOverride</c>）、
/// ユーザーの <c>%LOCALAPPDATA%\QuickER</c> を汚さない。
/// </remarks>
public class CrashLogShutdownFailureReporterTests
{
    /// <summary>検証用の一時フォルダを作る</summary>
    private static string CreateTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        return folder;
    }

    /// <summary>
    /// クラッシュではないことが分かるよう <c>shutdown-</c> 接頭辞で書き出され、
    /// 段の名前と元の例外が本文に載ることを検証する。
    /// </summary>
    /// <remarks>
    /// アプリは落ちていないのに <c>crash-*.log</c> が増えると「落ちた」と読み違えるため、
    /// ファイル名の接頭辞を分けること自体が仕様。
    /// </remarks>
    [Fact(DisplayName = "Report: shutdown- 接頭辞のログへ段の名前と例外を書き出す")]
    public void Report_WritesShutdownPrefixedLogWithStepName()
    {
        var folder = CreateTempFolder();

        try
        {
            var reporter = new CrashLogShutdownFailureReporter(folder);

            reporter.Report(
                "AiChat.SaveSettings",
                new InvalidOperationException("設定の保存に失敗", new IOException("ディスクフル"))
            );

            Directory.GetFiles(folder, "crash-*.log").Should().BeEmpty();
            var logs = Directory.GetFiles(folder, "shutdown-*.log");
            logs.Should().ContainSingle();

            var content = File.ReadAllText(logs[0]);
            content.Should().Contain("AiChat.SaveSettings");
            content.Should().Contain("設定の保存に失敗");
            content.Should().Contain("ディスクフル");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>書き込み不能な保存先でも例外を漏らさないことを検証する</summary>
    /// <remarks>
    /// ここで投げると「1 つの失敗が他を止めない」という受け止めの目的がそのまま破れる。
    /// </remarks>
    [Fact(DisplayName = "Report: 書き込み不能な保存先でも例外を投げない")]
    public void Report_UnwritablePath_DoesNotThrow()
    {
        // 既存ファイルを「フォルダ」として指定する＝Directory.CreateDirectory が必ず失敗する状況
        var file = Path.Combine(Path.GetTempPath(), $"shutdown-block-{Guid.NewGuid()}.tmp");
        File.WriteAllText(file, "block");

        try
        {
            var reporter = new CrashLogShutdownFailureReporter(file);

            var act = () => reporter.Report("AiChat.Close", new InvalidOperationException("失敗"));

            act.Should().NotThrow();
        }
        finally
        {
            File.Delete(file);
        }
    }
}
