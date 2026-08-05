using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// クラッシュ処理（<see cref="CrashHandlingService"/>）の整形・ログ書き出し・実行順序を検証するテストクラス。
/// </summary>
/// <remarks>
/// 実ファイル書き込みは一時フォルダへ隔離し（<c>baseDirOverride</c>）、ユーザーの
/// <c>%APPDATA%\QuickER</c> を汚さない。再入ガードは静的状態のため各ケース冒頭で初期化する。
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

    /// <summary>指定フォルダへログが書き出され、パスと内容が返ることを検証する</summary>
    [Fact(DisplayName = "WriteCrashLog: 指定フォルダへ書き出しフルパスを返す")]
    public void WriteCrashLog_WritesFileAndReturnsPath()
    {
        var folder = CreateTempFolder();

        try
        {
            var path = CrashHandlingService.WriteCrashLog(CreateNestedException(), "1.2.3", folder);

            path.Should().NotBeNull();
            Path.GetFileName(path!).Should().StartWith("crash-").And.EndWith(".log");
            Path.GetDirectoryName(path!).Should().Be(folder);

            var content = File.ReadAllText(path!);
            content.Should().Contain("1.2.3");
            content.Should().Contain("外側の失敗");
            content.Should().Contain("内側の根本原因");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>同一秒内に連続して書き出しても互いを上書きせず別ファイルになることを検証する</summary>
    /// <remarks>未観測タスク例外は 1 回の GC から連続発火しうるため、秒精度の名前では証跡が失われる</remarks>
    [Fact(DisplayName = "WriteCrashLog: 同一秒内の連続書き出しでも上書きしない")]
    public void WriteCrashLog_ConsecutiveWritesWithinSameSecond_DoNotOverwrite()
    {
        var folder = CreateTempFolder();

        try
        {
            var first = CrashHandlingService.WriteCrashLog(
                new InvalidOperationException("1 件目"),
                "1.2.3",
                folder
            );
            var second = CrashHandlingService.WriteCrashLog(
                new InvalidOperationException("2 件目"),
                "1.2.3",
                folder
            );

            first.Should().NotBeNull();
            second.Should().NotBeNull();
            second.Should().NotBe(first);

            Directory.GetFiles(folder, "crash-*.log").Should().HaveCount(2);
            File.ReadAllText(first!).Should().Contain("1 件目");
            File.ReadAllText(second!).Should().Contain("2 件目");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>保存先フォルダを作れない場合に例外を漏らさず null を返すことを検証する</summary>
    [Fact(DisplayName = "WriteCrashLog: 書き込み不能なパスでは null を返す")]
    public void WriteCrashLog_UnwritablePath_ReturnsNull()
    {
        // 既存ファイルを「フォルダ」として指定する＝Directory.CreateDirectory が必ず失敗する状況
        var file = Path.Combine(Path.GetTempPath(), $"crash-block-{Guid.NewGuid()}.tmp");
        File.WriteAllText(file, "block");

        try
        {
            CrashHandlingService
                .WriteCrashLog(CreateNestedException(), "1.2.3", file)
                .Should()
                .BeNull();
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>緊急保存 → ログ → ダイアログの順に実行され、ダイアログへログパスが渡ることを検証する</summary>
    [Fact(DisplayName = "HandleCrash: 緊急保存 → ログ → ダイアログの順に実行する")]
    public void HandleCrash_RunsStepsInOrder()
    {
        CrashHandlingService.ResetForTests();

        var folder = CreateTempFolder();
        var steps = new List<string>();
        string? receivedLogPath = null;

        try
        {
            CrashHandlingService.HandleCrash(
                CreateNestedException(),
                "1.2.3",
                () => steps.Add("save"),
                logPath =>
                {
                    steps.Add("dialog");
                    receivedLogPath = logPath;
                },
                folder
            );

            steps.Should().Equal("save", "dialog");
            receivedLogPath.Should().NotBeNull();
            File.Exists(receivedLogPath!).Should().BeTrue();
        }
        finally
        {
            CrashHandlingService.ResetForTests();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>緊急保存が例外を投げても、ログ書き出しとダイアログ表示が続行されることを検証する</summary>
    [Fact(DisplayName = "HandleCrash: 緊急保存が失敗してもログとダイアログは実行される")]
    public void HandleCrash_EmergencySaveThrows_ContinuesToLogAndDialog()
    {
        CrashHandlingService.ResetForTests();

        var folder = CreateTempFolder();
        string? receivedLogPath = null;
        var dialogShown = false;

        try
        {
            var act = () =>
                CrashHandlingService.HandleCrash(
                    CreateNestedException(),
                    "1.2.3",
                    () => throw new InvalidOperationException("緊急保存の二次例外"),
                    logPath =>
                    {
                        dialogShown = true;
                        receivedLogPath = logPath;
                    },
                    folder
                );

            act.Should().NotThrow();
            dialogShown.Should().BeTrue();
            receivedLogPath.Should().NotBeNull();
        }
        finally
        {
            CrashHandlingService.ResetForTests();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>ダイアログ表示が例外を投げても呼び出し元へ伝播しないことを検証する（終了処理を止めない）</summary>
    [Fact(DisplayName = "HandleCrash: ダイアログ表示の失敗は呼び出し元へ伝播しない")]
    public void HandleCrash_ShowDialogThrows_DoesNotPropagate()
    {
        CrashHandlingService.ResetForTests();

        var folder = CreateTempFolder();

        try
        {
            var act = () =>
                CrashHandlingService.HandleCrash(
                    CreateNestedException(),
                    "1.2.3",
                    () => { },
                    _ => throw new InvalidOperationException("ダイアログの二次例外"),
                    folder
                );

            act.Should().NotThrow();
        }
        finally
        {
            CrashHandlingService.ResetForTests();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>2 回目以降の呼び出しが再入ガードで何もしないことを検証する</summary>
    [Fact(DisplayName = "HandleCrash: 2 回目の呼び出しは再入ガードで何もしない")]
    public void HandleCrash_SecondCall_IsIgnored()
    {
        CrashHandlingService.ResetForTests();

        var folder = CreateTempFolder();
        var saveCount = 0;
        var dialogCount = 0;

        try
        {
            for (var i = 0; i < 2; i++)
            {
                CrashHandlingService.HandleCrash(
                    CreateNestedException(),
                    "1.2.3",
                    () => saveCount++,
                    _ => dialogCount++,
                    folder
                );
            }

            saveCount.Should().Be(1);
            dialogCount.Should().Be(1);
            Directory.GetFiles(folder, "crash-*.log").Should().ContainSingle();
        }
        finally
        {
            CrashHandlingService.ResetForTests();
            Directory.Delete(folder, recursive: true);
        }
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
