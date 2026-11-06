using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockFolderStore.Manifest"/> の読み取り用の複製の API 契約（参照の同一性・書き換えへの耐性）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 保存単位の振る舞い（往復・画面の upsert・削除・書き込みの失敗・読み込みの検証）は、操作の列の表
/// （<c>Folder/MockFolderTests</c>）と出力の表（<c>Outputs/MockOutputsTests</c>）が持つ。
/// ここに残すのは、状態の観測では取れない参照の同一性と、受け取った複製を書き換えたときの耐性だけ。
/// </remarks>
public class MockFolderStoreTests : IDisposable
{
    private readonly TestTempFolder _temp = new();

    /// <summary>固定時刻（改訂履歴のタイムスタンプを決定的にする）</summary>
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

    private string Folder => Path.Combine(_temp.Path, "mock");

    public void Dispose() => _temp.Dispose();

    private MockFolderStore CreateNew(string title = "受注管理", string schema = "# schema") =>
        MockFolderStore.CreateNew(Folder, title, schema, () => FixedNow);

    [Fact(
        DisplayName = "Manifest は変更までは同じインスタンスを返し、保存後は新しいインスタンスを返す"
    )]
    public void Manifest_IsSharedUntilNextChange()
    {
        var store = CreateNew();

        var first = store.Manifest;
        store.Manifest.Should().BeSameAs(first);

        store.SaveStylesheet("body{}", "css");

        store.Manifest.Should().NotBeSameAs(first);
        store.Manifest.Revisions.Should().ContainSingle();
    }

    [Fact(
        DisplayName = "Manifest のスナップショットを書き換えても、内部状態と次の保存には影響しない"
    )]
    public void Manifest_MutatingSnapshot_DoesNotAffectStore()
    {
        var store = CreateNew(title: "元の表題");

        var snapshot = store.Manifest;
        snapshot.Title = "書き換え";
        snapshot.Screens.Add(new MockScreen { File = "Ghost.html" });

        store.SaveStylesheet("body{}", "css");

        store.Manifest.Title.Should().Be("元の表題");
        store.Manifest.Screens.Should().BeEmpty();
        var reopened = MockFolderStore.Open(Folder);
        reopened.Manifest.Title.Should().Be("元の表題");
        reopened.Manifest.Screens.Should().BeEmpty();
    }
}
