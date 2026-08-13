using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedBinaryFixture;

/// <summary>
/// 無制限バイナリ除外フィクスチャ（<c>BinaryFixture.g.cs</c>）のインメモリ Repository が、実 DB と同じ除外の意味論
/// （読み取り複製の除外列 strip・UPDATE ガード・射影は完全クローンから）を満たすことを検証する。
/// </summary>
/// <remarks>
/// インメモリは DB を持たないため、実 DB の「SELECT が除外列を返さない」を「読み取り複製で除外列を未取得状態へ
/// strip する」ことで再現する。UPDATE ガードと、除外列を参照する射影の取得可否も実 DB と揃える。
/// </remarks>
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

    /// <summary>1. 読み取り複製は除外列を未取得状態へ strip する（除外対象外は保持）</summary>
    [Fact(
        DisplayName = "[Binary/InMemory] 1: 読み取り複製は除外列を strip する（有界バイナリは保持）"
    )]
    public async Task GetById_StripsExcludedColumns()
    {
        var (_, documents) = await SeededAsync();

        var doc = await documents.GetByIdAsync(1, Ct);
        doc.Should().NotBeNull();
        doc!.Payload.Should().BeNull("除外列 payload（nullable）は未取得状態＝null");
        doc.Thumb.Should().BeEmpty("除外列 thumb（非 nullable）は未取得状態＝空配列");
        doc.Checksum.Should().Equal(Doc1Checksum, "有界バイナリ checksum は除外対象外＝保持される");
    }

    /// <summary>3. 除外列へ非空値を代入した UpdateAsync は例外になる（実 DB と同じガード）</summary>
    [Fact(DisplayName = "[Binary/InMemory] 3: 除外列へ非空値代入後の UpdateAsync が例外になる")]
    public async Task Update_WithAssignedExcludedColumn_Throws()
    {
        var (_, documents) = await SeededAsync();

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Payload = [5, 5];

        var act = async () => await documents.UpdateAsync(doc, cancellationToken: Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>5. 除外列が未取得状態のままなら UpdateAsync は成功する（非除外列の変更が反映される）</summary>
    [Fact(
        DisplayName = "[Binary/InMemory] 5: 除外列が未取得状態なら UpdateAsync 成功（変更が反映）"
    )]
    public async Task Update_WithUnsetExcludedColumn_Succeeds()
    {
        var (_, documents) = await SeededAsync();

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Title = "alpha2";

        (await documents.UpdateAsync(doc, cancellationToken: Ct)).Should().BeTrue();
        (await documents.GetByIdAsync(1, Ct))!.Title.Should().Be("alpha2");

        // 実 DB の UPDATE は除外列を SET に含めない＝blob は温存される。行ごと差し替えるインメモリも同じ結果になること
        var stored = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);
        stored!
            .Payload.Should()
            .Equal(Doc1Payload, "未取得状態のまま更新しても除外列 payload は消えない");
        stored.Thumb.Should().Equal(Doc1Thumb, "非 nullable の除外列 thumb も消えない");
    }

    /// <summary>グラフ保存（SaveAsync）経由の更新でも除外列の blob は温存される</summary>
    [Fact(DisplayName = "[Binary/InMemory] 5b: グラフ保存の更新でも除外列 blob は温存される")]
    public async Task Save_WithUnsetExcludedColumn_PreservesBlob()
    {
        var (_, documents) = await SeededAsync();

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Title = "alpha3";
        doc.RowState = RowState.Updated;

        await documents.SaveAsync(doc, cancellationToken: Ct);

        var stored = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);
        stored!.Title.Should().Be("alpha3");
        stored.Payload.Should().Equal(Doc1Payload, "グラフ保存でも除外列 payload は消えない");
        stored.Thumb.Should().Equal(Doc1Thumb);
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

    /// <summary>WithUnboundedBinary は除外列を strip しない完全クローンを返す（blob が取れる・返却は複製）</summary>
    [Fact(
        DisplayName = "[Binary/InMemory] WithUnboundedBinary は除外列 strip なしの複製を返す（blob が取れる）"
    )]
    public async Task WithUnboundedBinary_ReturnsUnstrippedClone()
    {
        var (store, documents) = await SeededAsync();

        var doc = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);

        doc.Should().NotBeNull();
        doc!.Payload.Should().Equal(Doc1Payload, "strip されず除外列 payload が取れる");
        doc.Thumb.Should().Equal(Doc1Thumb, "strip されず除外列 thumb が取れる");

        // 返却は複製＝取得エンティティを書き換えてもストアの実体（次回取得）へは波及しない
        doc.Payload![0] = 0xFF;
        var reread = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);
        reread!
            .Payload.Should()
            .Equal(Doc1Payload, "返却は完全クローンのためストアは書き換わらない");
    }

    /// <summary>
    /// 射影のフォールバック経路（列抽出不能なセレクタ）でも <c>WithUnboundedBinary</c> が効く（方言側とパリティ）。
    /// </summary>
    /// <remarks>
    /// 方言側のフォールバックは <c>ToListAsync</c> 委譲なので自然にフラグを拾う。インメモリだけが常に strip して
    /// いたため、同じ呼び出しが実 DB では blob を返しインメモリでは null という乖離があった。
    /// 対照は <c>BinaryColumnAdoRuntimeTests</c> の 8b。
    /// </remarks>
    [Fact(
        DisplayName = "[Binary/InMemory] 射影のフォールバックでも WithUnboundedBinary で除外列が取れる"
    )]
    public async Task Projection_Fallback_HonorsWithUnboundedBinary()
    {
        var (_, documents) = await SeededAsync();

        // エンティティを丸ごと引数に取るためセレクタから列を抽出できない＝完全クローンからのフォールバック射影になる
        var withFlag = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .ToProjectionListAsync(d => PayloadOf(d), Ct);
        withFlag.Should().ContainSingle().Which.Should().Equal(Doc1Payload);

        // 指定しなければ従来どおり除外列は未取得状態のまま射影される
        var withoutFlag = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .ToProjectionListAsync(d => PayloadOf(d), Ct);
        withoutFlag.Should().ContainSingle().Which.Should().BeNull();
    }

    /// <summary>射影セレクタからの列抽出を成立させないためのヘルパー（エンティティ全体を引数に取る）</summary>
    private static byte[]? PayloadOf(DocumentEntity document) => document.Payload;

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

    /// <summary>WithUnboundedBinary と Include の併用はインメモリでも例外になる（実 DB とパリティ）</summary>
    [Fact(DisplayName = "[Binary/InMemory] WithUnboundedBinary と Include の併用は例外になる")]
    public async Task WithUnboundedBinary_WithInclude_Throws()
    {
        var (_, documents) = await SeededAsync();

        var act = async () =>
            await documents
                .Query()
                .Where(d => d.DocumentId == 1)
                .Include(d => d.DocumentNotes)
                .WithUnboundedBinary()
                .FirstOrDefaultAsync(Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Stream アクセサ（Read/Write{Column}Async）のインメモリ・パリティ ──

    /// <summary>Write→Read の往復でデータが一致する（MemoryStream パリティ）</summary>
    [Fact(DisplayName = "[Binary/InMemory] Stream: Write→Read の往復が一致する")]
    public async Task Stream_WriteThenRead_RoundTrips()
    {
        var (_, documents) = await SeededAsync();

        var payload = new byte[64 * 1024];
        new Random(11).NextBytes(payload);
        (await documents.WritePayloadAsync(1, new MemoryStream(payload), cancellationToken: Ct))
            .Should()
            .BeTrue();

        using var destination = new MemoryStream();
        (await documents.ReadPayloadAsync(1, destination, Ct)).Should().BeTrue();
        destination.ToArray().Should().Equal(payload);
    }

    /// <summary>Read は行なし・列 NULL で false（宛先へ何も書かない）</summary>
    [Fact(DisplayName = "[Binary/InMemory] Stream: Read は行なし・NULL で false")]
    public async Task Stream_Read_ReturnsFalse_ForMissingRowOrNull()
    {
        var (_, documents) = await SeededAsync();

        using var noRow = new MemoryStream();
        (await documents.ReadPayloadAsync(999, noRow, Ct)).Should().BeFalse();
        noRow.Length.Should().Be(0);

        // payload を NULL に設定してから読むと false
        (await documents.WritePayloadAsync(1, null, cancellationToken: Ct))
            .Should()
            .BeTrue();
        using var nullColumn = new MemoryStream();
        (await documents.ReadPayloadAsync(1, nullColumn, Ct)).Should().BeFalse();
        nullColumn.Length.Should().Be(0);
    }

    /// <summary>Write はストア実体を直接書き換えず複製を差し戻す（既に取得済みのエンティティに影響しない）</summary>
    [Fact(DisplayName = "[Binary/InMemory] Stream: Write はストア実体を直接書き換えない")]
    public async Task Stream_Write_DoesNotMutateStoreInPlace()
    {
        var (_, documents) = await SeededAsync();

        // WithUnboundedBinary で取得した複製（元 payload を保持）
        var before = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);

        var replacement = new byte[256];
        new Random(22).NextBytes(replacement);
        (await documents.WritePayloadAsync(1, new MemoryStream(replacement), cancellationToken: Ct))
            .Should()
            .BeTrue();

        // 先に取得していた複製は書き換わらない（返却複製の独立性）
        before!.Payload.Should().Equal(Doc1Payload, "取得済み複製は Write の影響を受けない");

        // ストアには新しい blob が反映されている
        var after = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);
        after!.Payload.Should().Equal(replacement);
    }
}
