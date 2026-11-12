using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.Integration.GeneratedRuntime;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.GeneratedBinaryFixture;

/// <summary>
/// 無制限バイナリ除外フィクスチャ（<c>BinaryFixture.g.cs</c>）のインメモリ Repository が、実 DB と同じ除外の意味論
/// （読み取り複製の除外列 strip・UPDATE ガード・射影は完全クローンから）を満たすことを検証する。
/// 除外の読み書き・<c>WithUnboundedBinary()</c>・射影のフォールバック・Stream アクセサの契約は、実装先どうしの一致のシナリオ
/// （<c>blob-*</c>）が QuickER 版と並べて見る。ここに残すのは、その図に無いもの（有界バイナリ・素の <c>byte[]</c>・名前付きクエリ）と
/// 割り当て量。
/// </summary>
/// <remarks>
/// インメモリは DB を持たないため、実 DB の「SELECT が除外列を返さない」を「読み取り複製で除外列を未取得状態へ
/// strip する」ことで再現する。UPDATE ガードと、除外列を参照する射影の取得可否も実 DB と揃える。
/// </remarks>
[RuntimeCell(RuntimeFeatures.BinaryColumn, RuntimeTarget.InMemory)]
public sealed class BinaryInMemoryFixtureRuntimeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly byte[] Doc1Payload = [1, 2, 3, 4];
    private static readonly byte[] Doc1Thumb = [9, 9];
    private static readonly byte[] Doc1Checksum = [10, 11, 12];

    /// <summary>データストアと文書リポジトリを生成し、文書 1 件（payload/thumb/checksum あり）を投入する</summary>
    private static async Task<(
        InMemoryDataStore Store,
        IDocumentRepository Documents
    )> SeededAsync()
    {
        var store = new InMemoryDataStore();
        var documents = new InMemoryDocumentRepository(store);

        await documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Payload = Doc1Payload,
                Thumb = Doc1Thumb,
                Checksum = Doc1Checksum,
                RowState = RowState.Added,
            },
            Ct
        );

        return (store, documents);
    }

    /// <summary>
    /// 1. 読み取り複製は有界バイナリ（除外対象外）を strip しない（除外列を strip することは実装先どうしの一致の
    /// <c>blob-excluded-columns-read-and-write-guard</c> が見る。その図は有界バイナリを持たない）
    /// </summary>
    [Fact(DisplayName = "[Binary/InMemory] 1: 読み取り複製は有界バイナリを strip しない")]
    public async Task GetById_KeepsBoundedBinary()
    {
        var (_, documents) = await SeededAsync();

        var doc = await documents.GetByIdAsync(1, Ct);
        doc.Should().NotBeNull();
        doc!.Payload.Should().BeNull("除外列 payload（nullable）は未取得状態＝null");
        doc.Checksum.Should().Equal(Doc1Checksum, "有界バイナリ checksum は除外対象外＝保持される");
    }

    /// <summary>
    /// 読み取りは除外列の blob を写さない。返す行は除外列を未取得状態のまま作るので、ストアの行が大きな blob を
    /// 持っていても、読み取りの割り当て量は blob の大きさに比例しない。
    /// </summary>
    /// <remarks>
    /// 行を JSON で丸ごと複製してから除外列を捨てる形では、捨てる blob も Base64 で往復して blob の数倍を割り当てる。
    /// 読み取りは同期で完了するため、現在のスレッドの割り当て量で測れる。
    /// </remarks>
    [Fact(
        DisplayName = "[Binary/InMemory] 1b: 読み取りは除外列の blob を写さない（割り当てが blob の大きさに比例しない）"
    )]
    public async Task Read_DoesNotCopyExcludedBlob()
    {
        var store = new InMemoryDataStore();
        var documents = new InMemoryDocumentRepository(store);
        var blob = new byte[4 * 1024 * 1024];

        await documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "big",
                Payload = blob,
                Thumb = [],
                RowState = RowState.Added,
            },
            Ct
        );

        // 1 回目は JIT やキャッシュの割り当てを含むので、測る前に済ませる
        await documents.GetByIdAsync(1, Ct);
        await documents.Query().ToListAsync(Ct);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var byId = await documents.GetByIdAsync(1, Ct);
        var listed = await documents.Query().ToListAsync(Ct);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        byId!.Payload.Should().BeNull("除外列は未取得状態で返る");
        listed.Should().ContainSingle().Which.Payload.Should().BeNull();
        allocated.Should().BeLessThan(blob.Length / 4, "読み取りは捨てる blob を写さない");
    }

    /// <summary>バイナリ列の OrderBy は例外にならず辞書式バイト順に並ぶ（実 DB の varbinary 順序と同じ）</summary>
    [Fact(DisplayName = "[Binary/InMemory] OrderBy: byte[] 列が辞書式バイト順で並ぶ")]
    public async Task OrderBy_BinaryColumn_SortsLexicographically()
    {
        var (_, documents) = await SeededAsync();

        // checksum は有界バイナリ（除外対象外）＝読み取り複製にも載る。byte[] は IComparable を実装しないため
        // 既定の比較子では ArgumentException になっていた
        await documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 2,
                Title = "beta",
                Checksum = [10, 11],
                RowState = RowState.Added,
            },
            Ct
        );
        await documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 3,
                Title = "gamma",
                Checksum = [2, 250],
                RowState = RowState.Added,
            },
            Ct
        );

        // 先頭バイト順＝[2,250] < [10,11] < [10,11,12]（前方一致なら短い方が先）
        var ascending = await documents.Query().OrderBy(d => d.Checksum).ToListAsync(Ct);
        ascending.Select(d => d.DocumentId).Should().Equal(3, 2, 1);

        var descending = await documents.Query().OrderByDescending(d => d.Checksum).ToListAsync(Ct);
        descending.Select(d => d.DocumentId).Should().Equal(1, 2, 3);
    }

    /// <summary>射影（GetPayloads）は除外列 payload の値を取得できる（ストアの完全クローンから射影する）</summary>
    [Fact(DisplayName = "[Binary/InMemory] 射影は除外列 payload の値を取得できる")]
    public async Task Projection_ReturnsExcludedColumnValue()
    {
        var (_, documents) = await SeededAsync();

        var rows = await documents.GetPayloadsAsync(Ct);
        rows.Should().ContainSingle();
        rows[0].DocumentId.Should().Be(1);
        rows[0].Payload.Should().Equal(Doc1Payload, "射影は完全クローンから作るため除外列も取れる");
    }

    /// <summary>
    /// 保存後に呼び出し側が byte[] 列を破壊的に書き換えても、ストアの行には波及しない（列コピーの独立性）。
    /// </summary>
    [Fact(DisplayName = "[Binary/InMemory] 保存後に byte[] を書き換えてもストアの行は変わらない")]
    public async Task Insert_CopiesBinaryColumns_NotShared()
    {
        var store = new InMemoryDataStore();
        var documents = new InMemoryDocumentRepository(store);

        var checksum = new byte[] { 1, 2, 3 };
        var payload = new byte[] { 7, 7, 7 };
        await documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Payload = payload,
                Checksum = checksum,
                RowState = RowState.Added,
            },
            Ct
        );

        // 呼び出し側の配列をその場で書き換える（値オブジェクトではない素の byte[]）
        checksum[0] = 0xFF;
        payload[0] = 0xFF;

        var stored = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);
        stored!.Checksum.Should().Equal([1, 2, 3], "ストアは自分の複製を持つ");
        stored.Payload.Should().Equal([7, 7, 7]);
    }
}
