using FluentAssertions;
using QuickER.Services;

namespace QuickER.Tests.Services;

/// <summary><see cref="ApiKeyStore"/> の保存・復元動作を検証するテストクラス</summary>
public class ApiKeyStoreTests
{
    /// <summary>Save した API キーを Load で同値復元できることを検証する</summary>
    [Fact(DisplayName = "Save した API キーを Load で復元できる")]
    public void SaveThenLoad_RoundTrip()
    {
        var keyName = "ApiKeyStoreTests_" + Guid.NewGuid().ToString("N");
        const string expected = "sk-test-abc123!@#";

        try
        {
            ApiKeyStore.Save(keyName, expected);
            var actual = ApiKeyStore.Load(keyName);
            actual.Should().Be(expected);
        }
        finally
        {
            ApiKeyStore.Save(keyName, string.Empty);
        }
    }

    /// <summary>空文字を保存すると鍵が削除され、Load が空文字を返すことを検証する</summary>
    [Fact(DisplayName = "空文字を Save すると削除され、Load は空文字を返す")]
    public void SaveEmpty_Deletes()
    {
        var keyName = "ApiKeyStoreTests_" + Guid.NewGuid().ToString("N");

        try
        {
            ApiKeyStore.Save(keyName, "temp-key");
            ApiKeyStore.Save(keyName, string.Empty);
            ApiKeyStore.Load(keyName).Should().BeEmpty();
        }
        finally
        {
            ApiKeyStore.Save(keyName, string.Empty);
        }
    }
}
