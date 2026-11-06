using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockFolderDesignSession"/> の配線（渡したストアの公開）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 会話の開始・ツールの実行・通知・初回ターンの振る舞いは、操作の列の表（<c>Folder/MockFolderTests</c>）が持つ。
/// </remarks>
public class MockFolderDesignSessionTests : IDisposable
{
    private readonly TestTempFolder _temp = new();

    private static readonly DateTimeOffset FixedNow = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

    public void Dispose() => _temp.Dispose();

    /// <summary>Store プロパティが渡したストアを公開することを検証する</summary>
    [Fact(DisplayName = "Store プロパティが渡したストアを公開する")]
    public void Store_ExposesInjectedStore()
    {
        var store = MockFolderStore.CreateNew(
            Path.Combine(_temp.Path, "mock"),
            "受注管理",
            string.Empty,
            () => FixedNow
        );
        var session = new MockFolderDesignSession(toolHost => new FakeChatEngine(toolHost), store);

        session.Store.Should().BeSameAs(store);
    }
}
