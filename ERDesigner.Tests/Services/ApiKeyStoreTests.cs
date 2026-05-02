using System;
using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="ApiKeyStore"/> の保存/復元動作を検証します。
/// </summary>
public class ApiKeyStoreTests
{
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
