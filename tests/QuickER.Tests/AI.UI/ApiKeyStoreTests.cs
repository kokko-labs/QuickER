using System.IO;
using AwesomeAssertions;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.UI;

/// <summary><see cref="ApiKeyStore"/> の保存・復元動作を検証するテストクラス</summary>
/// <remarks>
/// 実 %LOCALAPPDATA% を汚さず（並列テストの IO 競合も避けて）検証するため、
/// 保存先フォルダ指定オーバーロードでテストごとの一時フォルダへ隔離する。
/// </remarks>
public class ApiKeyStoreTests : IDisposable
{
    /// <summary>テストごとの一時フォルダ（破棄で中身ごと消す）</summary>
    private readonly TestTempFolder _temp = new();

    /// <summary>テストごとに独立した一時フォルダのパスを組み立てる（作成は Save 側が行う）</summary>
    private string NewTempFolder() => Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose() => _temp.Dispose();

    /// <summary>Save した API キーを Load で同値復元できることを検証する</summary>
    [Fact(DisplayName = "Save した API キーを Load で復元できる")]
    public void SaveThenLoad_RoundTrip()
    {
        var folder = NewTempFolder();
        const string keyName = "ApiKeyStoreTests";
        const string expected = "sk-test-abc123!@#";

        ApiKeyStore.Save(keyName, expected, folder);
        var actual = ApiKeyStore.Load(keyName, folder);
        actual.Should().Be(new SavedApiKey(SavedApiKeyStatus.Loaded, expected));
    }

    /// <summary>空文字を保存すると鍵が削除され、Load が「保存されていない」を返すことを検証する</summary>
    [Fact(DisplayName = "空文字を Save すると削除され、Load は保存されていないを返す")]
    public void SaveEmpty_Deletes()
    {
        var folder = NewTempFolder();
        const string keyName = "ApiKeyStoreTests";

        ApiKeyStore.Save(keyName, "temp-key", folder);
        ApiKeyStore.Save(keyName, string.Empty, folder);
        ApiKeyStore.Load(keyName, folder).Should().Be(SavedApiKey.Missing);
    }

    /// <remarks>
    /// 別のユーザー・別の PC で暗号化したファイルや壊れたファイルは復号できない。空のキーとして返すと、
    /// 呼び出し側が空の値を保存したとき（保存をやめたとき）に暗号ファイルを消してしまう。
    /// </remarks>
    [Fact(
        DisplayName = "復号できない暗号ファイルは、保存されていないではなく読めないとして返し、ファイルは残す"
    )]
    public void Load_UndecryptableFile_IsUnreadable()
    {
        var folder = NewTempFolder();
        const string keyName = "ApiKeyStoreTests";

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, keyName + ".dat");
        File.WriteAllBytes(path, [1, 2, 3, 4]);

        ApiKeyStore.Load(keyName, folder).Should().Be(SavedApiKey.Unreadable);
        File.Exists(path).Should().BeTrue();
    }
}
