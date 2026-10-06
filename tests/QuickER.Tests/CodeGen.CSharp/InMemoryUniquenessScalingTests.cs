using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Runtime;
using QuickER.Runtime.InMemory;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// インメモリのストアが、多くの行をまとめて書く経路（一括挿入・グラフ保存の公開）で、一意制約の検査を
/// 行数に比例する手間で行うことを検証するテストクラス。
/// </summary>
/// <remarks>
/// 時間は環境で揺れるため測らず、一意制約の列の getter が読まれた回数を数える。書く行ごとに既存の全行と
/// 比べる形では回数が行数の 2 乗で増え、索引を引く形では行数の定数倍に収まる。
/// ランタイムパッケージ <c>QuickER.Runtime.InMemory</c> を手書きのエンティティで直接使う。
/// </remarks>
public class InMemoryUniquenessScalingTests
{
    private const int Rows = 500;

    /// <summary>一意制約の列を 1 本持ち、その列が読まれた回数を数えるエンティティ</summary>
    [Table("tags")]
    [UniqueConstraint(nameof(Code), Name = "UQ_tags_code")]
    private sealed class TagEntity : EntityBaseCore
    {
        /// <summary>Code の getter が呼ばれた回数（このクラスのテストは順に走るので共有してよい）</summary>
        public static int CodeReads;

        private string _code = string.Empty;

        [Key]
        [Column("tag_id")]
        public int TagId { get; set; }

        [Column("code")]
        public string Code
        {
            get
            {
                Interlocked.Increment(ref CodeReads);
                return _code;
            }
            set => _code = value;
        }
    }

    private sealed class TagRepository(InMemoryDataStore store)
        : InMemoryRepositoryCore<TagEntity, int>(store);

    private static TagEntity[] NewTags(int start, int count) =>
        Enumerable
            .Range(start, count)
            .Select(id => new TagEntity
            {
                TagId = id,
                Code = $"code-{id}",
                RowState = RowState.Added,
            })
            .ToArray();

    [Fact(
        DisplayName = "一括挿入の一意制約の検査は、ストアと同じバッチの行を行数に比例する手間で調べる"
    )]
    public async Task BulkInsert_ChecksUniquenessInLinearWork()
    {
        var repository = new TagRepository(new InMemoryDataStore());
        await repository.BulkInsertAsync(NewTags(0, Rows), TestContext.Current.CancellationToken);

        Volatile.Write(ref TagEntity.CodeReads, 0);
        var inserted = await repository.BulkInsertAsync(
            NewTags(Rows, Rows),
            TestContext.Current.CancellationToken
        );

        inserted.Should().Be(Rows);
        Volatile
            .Read(ref TagEntity.CodeReads)
            .Should()
            .BeLessThan(10 * Rows, "書く行ごとに全行と比べると、読み出しは行数の 2 乗で増える");
    }

    [Fact(DisplayName = "グラフ保存の一意制約の検査は、公開する行を行数に比例する手間で調べる")]
    public async Task GraphSave_ChecksUniquenessInLinearWork()
    {
        var repository = new TagRepository(new InMemoryDataStore());
        await repository.BulkInsertAsync(NewTags(0, Rows), TestContext.Current.CancellationToken);

        Volatile.Write(ref TagEntity.CodeReads, 0);
        var saved = await repository.SaveAsync(
            NewTags(Rows, Rows),
            cancellationToken: TestContext.Current.CancellationToken
        );

        saved.Should().Be(Rows);
        Volatile
            .Read(ref TagEntity.CodeReads)
            .Should()
            .BeLessThan(10 * Rows, "公開する行ごとに全行と比べると、読み出しは行数の 2 乗で増える");
    }

    [Fact(DisplayName = "索引を引く検査でも、一括挿入の同じ値は拒否される")]
    public async Task BulkInsert_DuplicateValueInBatch_IsRejected()
    {
        var repository = new TagRepository(new InMemoryDataStore());
        var tags = NewTags(0, 3);
        tags[2].Code = tags[0].Code;

        var act = () => repository.BulkInsertAsync(tags, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<System.InvalidOperationException>())
            .Which.Message.Should()
            .Contain("UQ_tags_code");
        (await repository.GetAllAsync(TestContext.Current.CancellationToken))
            .Should()
            .BeEmpty("拒否したバッチは 1 行も残さない");
    }
}
