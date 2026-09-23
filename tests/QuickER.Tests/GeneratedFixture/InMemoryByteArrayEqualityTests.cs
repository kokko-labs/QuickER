using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

// 生成フィクスチャ自身の namespace に置く＝他フィクスチャの同名型と曖昧にならないようにする
namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// インメモリ実行器の <c>byte[]</c> 等値が参照比較でなく値比較（バイト列比較）であることを、コミット済み
/// フィクスチャ（<c>InMemoryFixture.g.cs</c> の <c>blank_probes</c>）の実型で検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// SQL 実装は列の実バイトを比較するのに対し、C# の <c>==</c> は配列を参照比較する。インメモリストアは
/// 取得のたびに複製した配列を返す設計なので、書き換え（<c>ByteArrayEqualityRewriter</c>）が無いと
/// 「同じ内容の blob を持つ行」への等値述語がインメモリだけ 1 件も一致しない
/// （名前付きクエリの DSL 等値・一意性チェックの byte[] 構成列も同じ経路で壊れていた）。
/// null の扱いも SQL の観測結果へ揃える＝null 値との等値は NULL 行だけに一致し、非 null 値は NULL 行に一致しない。
/// </remarks>
public sealed class InMemoryByteArrayEqualityTests
{
    /// <summary>blob 内容の異なる 3 行（うち 1 行は NULL）をシードしたリポジトリを作る</summary>
    private static InMemoryBlankProbeRepository CreateSeededRepository()
    {
        var repository = new InMemoryBlankProbeRepository(new InMemoryDataStore());

        InsertProbe(repository, 1, [0x01, 0x02]);
        InsertProbe(repository, 2, [0x0A, 0x0B]);
        InsertProbe(repository, 3, null);

        return repository;
    }

    private static void InsertProbe(
        InMemoryBlankProbeRepository repository,
        int id,
        byte[]? chunk
    ) =>
        repository
            .InsertAsync(
                new BlankProbeEntity
                {
                    ProbeId = id,
                    Flag = true,
                    SeenAt = new DateTime(2026, 1, 2),
                    Chunk = chunk,
                    Rate = 1m,
                    LoggedAt = new DateTime(2026, 2, 3),
                    Stamp = [0xFF],
                }
            )
            .GetAwaiter()
            .GetResult();

    [Fact]
    public async Task 同じ内容の別インスタンスと等値比較すると一致する()
    {
        var repository = CreateSeededRepository();
        var probe = new byte[] { 0x01, 0x02 }; // シードとは別インスタンス

        var matched = await repository
            .Query()
            .Where(x => x.Chunk == probe)
            .ToListAsync(TestContext.Current.CancellationToken);

        matched
            .Select(x => x.ProbeId)
            .Should()
            .Equal([1], "参照でなくバイト列の内容で一致するべき（SQL 実装と同じ観測結果）");
    }

    [Fact]
    public async Task 不等値は内容の違う行とNULL行に一致する()
    {
        var repository = CreateSeededRepository();
        var probe = new byte[] { 0x01, 0x02 };

        var matched = await repository
            .Query()
            .Where(x => x.Chunk != probe)
            .ToListAsync(TestContext.Current.CancellationToken);

        // C# の値意味論（null != 値 は true）＝列側 IS NULL 補償済みの SQL と同じ観測結果
        matched.Select(x => x.ProbeId).Should().BeEquivalentTo([2, 3]);
    }

    [Fact]
    public async Task Null値との等値はNULL行だけに一致する()
    {
        var repository = CreateSeededRepository();
        byte[]? probe = null;

        var matched = await repository
            .Query()
            .Where(x => x.Chunk == probe)
            .ToListAsync(TestContext.Current.CancellationToken);

        matched.Select(x => x.ProbeId).Should().Equal([3]);
    }
}
