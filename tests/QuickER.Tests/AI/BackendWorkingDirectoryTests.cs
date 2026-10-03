using System.IO;
using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// CLI バックエンドの一時作業フォルダ（<see cref="BackendWorkingDirectory"/>）の置き場所と後始末を検証するテストクラス。
/// </summary>
public class BackendWorkingDirectoryTests : IDisposable
{
    /// <summary>テスト用のバックエンド名（実在のバックエンドのフォルダと混ざらないようにする）</summary>
    private const string Backend = "quicker-tests-backend";

    /// <summary>テストが作ったバックエンド名のフォルダ（空なら後片づけで消す）</summary>
    private static string BackendRoot => Path.Combine(Path.GetTempPath(), "QuickER", Backend);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (
                Directory.Exists(BackendRoot)
                && !Directory.EnumerateFileSystemEntries(BackendRoot).Any()
            )
            {
                Directory.Delete(BackendRoot);
            }
        }
        catch (IOException)
        {
            // 並行して走る同じクラスのテストが使っていれば残す
        }
    }

    /// <summary>一時作業フォルダは %TEMP%\QuickER\{バックエンド名}\{GUID} に空で作られることを検証する</summary>
    [Fact(DisplayName = "一時作業フォルダは TEMP の QuickER 配下にバックエンドごと空で作る")]
    public void Create_MakesEmptyFolderUnderTempPerBackend()
    {
        var path = BackendWorkingDirectory.Create(Backend);

        try
        {
            Path.GetDirectoryName(path).Should().Be(BackendRoot);
            Path.GetFileName(path).Should().MatchRegex("^[0-9a-f]{32}$");
            Directory.EnumerateFileSystemEntries(path).Should().BeEmpty();
        }
        finally
        {
            BackendWorkingDirectory.TryDelete(path);
        }

        Directory.Exists(path).Should().BeFalse();
    }

    /// <summary>空のパス（作っていない）と、中身のあるフォルダの削除を検証する</summary>
    [Fact(DisplayName = "削除は空のパスでは何もせず中身ごと消す")]
    public void TryDelete_IgnoresEmptyPathAndDeletesContents()
    {
        BackendWorkingDirectory.TryDelete(string.Empty);

        var path = BackendWorkingDirectory.Create(Backend);
        File.WriteAllText(Path.Combine(path, "mcp-config.json"), "{}");

        BackendWorkingDirectory.TryDelete(path);

        Directory.Exists(path).Should().BeFalse();
    }
}
