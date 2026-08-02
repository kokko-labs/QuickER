using System.IO;
using AwesomeAssertions;
using QuickER.AI.UI;

namespace QuickER.Tests.AI.UI;

/// <summary><see cref="ApiKeyStore"/> の保存・復元動作を検証するテストクラス</summary>
/// <remarks>
/// 実 %APPDATA% を汚さず（並列テストの IO 競合も避けて）検証するため、
/// 保存先フォルダ指定オーバーロードでテストごとの一時フォルダへ隔離する。
/// </remarks>
public class ApiKeyStoreTests
{
    /// <summary>テストごとに独立した一時フォルダのパスを組み立てる（作成は Save 側が行う）</summary>
    private static string NewTempFolder() =>
        Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

    /// <summary>一時フォルダを丸ごと後始末する</summary>
    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Save した API キーを Load で同値復元できることを検証する</summary>
    [Fact(DisplayName = "Save した API キーを Load で復元できる")]
    public void SaveThenLoad_RoundTrip()
    {
        var folder = NewTempFolder();
        const string keyName = "ApiKeyStoreTests";
        const string expected = "sk-test-abc123!@#";

        try
        {
            ApiKeyStore.Save(keyName, expected, folder);
            var actual = ApiKeyStore.Load(keyName, folder);
            actual.Should().Be(expected);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>空文字を保存すると鍵が削除され、Load が空文字を返すことを検証する</summary>
    [Fact(DisplayName = "空文字を Save すると削除され、Load は空文字を返す")]
    public void SaveEmpty_Deletes()
    {
        var folder = NewTempFolder();
        const string keyName = "ApiKeyStoreTests";

        try
        {
            ApiKeyStore.Save(keyName, "temp-key", folder);
            ApiKeyStore.Save(keyName, string.Empty, folder);
            ApiKeyStore.Load(keyName, folder).Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }
}
