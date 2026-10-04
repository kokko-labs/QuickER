using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedTranslatorParityFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成ランタイムの式の翻訳を、3 つの実装先（SQL Server の ADO・SQLite の ADO・インメモリ）で
/// C# の意味論（同じシードへ LINQ to Objects を当てた結果）と突き合わせる共通基底。
/// </summary>
/// <remarks>
/// <para>
/// 入力は <see cref="TranslatorParityFixtureDefinition"/> の図をマルチターゲット＋インメモリで生成したフィクスチャで、
/// 3 つの実装先が同じエンティティ型を共有する。派生はスキーマの用意と DI の登録だけを差し込む。
/// </para>
/// <para>
/// 期待値は「同じ実装先から全行を読み戻し、コンパイルしたラムダを当てた結果」。実 DB が値を変えて格納する列
/// （SQL Server の <c>nchar(10)</c> は空白で詰めて返す）でも、翻訳した条件と C# の条件が同じ格納値を見て比べられる。
/// 詰めた値は C# の結論も変える（<c>"abc       ".EndsWith("c")</c> は偽）ので、シードの値で比べると格納の差を翻訳の差と取り違える。
/// EF Core はマルチターゲットと生成時エラーで排他になるので、この基底には入らない（翻訳器を通らない実装でもある）。
/// </para>
/// </remarks>
public abstract class TranslatorParityRuntimeTestsBase
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private ServiceProvider? _provider;

    /// <summary>スキーマを作り直し（DB を持つ実装先だけ）、リポジトリを登録した DI コンテナを返す</summary>
    protected abstract Task<ServiceProvider> CreateProviderAsync();

    /// <summary>実装先を初期化し、probes のシードを投入する。各テストの先頭で呼ぶ</summary>
    protected async Task SetUpAsync()
    {
        _provider = await CreateProviderAsync();

        var probes = Repository<IProbeRepository>();
        foreach (var probe in SeedProbes())
        {
            await probes.InsertAsync(probe, Ct);
        }
    }

    /// <summary>登録済みのリポジトリを解決する</summary>
    protected T Repository<T>()
        where T : notnull =>
        (
            _provider ?? throw new InvalidOperationException("SetUpAsync を先に呼ぶこと")
        ).GetRequiredService<T>();

    /// <summary>
    /// probes のシード。呼ぶたびに新しいインスタンスを返す（挿入がインスタンスの状態を変えるため使い回さない）。
    /// </summary>
    /// <remarks>
    /// オフセットつき日時は UTC へ換算すると年・日・時が変わる値を混ぜる（id 1・2・5）。名前は空白だけ・タブ・
    /// 全角空白・空文字・改行を含む値を持つ（IsNullOrEmpty / IsNullOrWhiteSpace の境界）。固定長の code は空白だけ（id 5）と空文字（id 9）を持つ。
    /// </remarks>
    protected static IReadOnlyList<ProbeEntity> SeedProbes() =>
        [
            Probe(
                1,
                "Alice",
                "abc",
                new DateTime(2024, 1, 15, 10, 30, 0),
                new DateTimeOffset(2024, 1, 1, 5, 0, 0, TimeSpan.FromHours(9)),
                true,
                group: "g1",
                xy: "a]b"
            ),
            Probe(
                2,
                null,
                "abcd",
                new DateTime(2024, 1, 16, 0, 0, 0),
                new DateTimeOffset(2023, 12, 31, 22, 0, 0, TimeSpan.FromHours(-3)),
                false
            ),
            Probe(
                3,
                "  ",
                "xabx",
                new DateTime(2024, 1, 14, 23, 59, 59),
                new DateTimeOffset(2024, 6, 30, 23, 30, 0, TimeSpan.Zero),
                true
            ),
            Probe(4, "\t", null, null, null, false),
            Probe(
                5,
                "　",
                "  ",
                new DateTime(2023, 5, 1, 0, 0, 0),
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.FromHours(9)),
                true
            ),
            Probe(6, "", "Zc", new DateTime(2024, 1, 15, 0, 0, 0), null, false),
            Probe(7, "Bob\n", null, new DateTime(2025, 1, 15, 8, 0, 0), null, true),
            Probe(8, "label", "ab", null, null, false),
            Probe(9, "x", "", null, null, false),
        ];

    private static ProbeEntity Probe(
        int id,
        string? name,
        string? code,
        DateTime? at,
        DateTimeOffset? atOffset,
        bool isActive,
        string? group = null,
        string? xy = null
    ) =>
        new()
        {
            ProbeId = id,
            Name = name,
            Code = code,
            At = at,
            AtOffset = atOffset,
            IsActive = isActive,
            Group = group,
            XY = xy,
        };

    /// <summary>
    /// 実装先の Query() が返す行の集合が、同じ実装先から読み戻した全行へ C# のラムダを当てた結果と一致することを表明する。
    /// </summary>
    protected async Task ShouldMatchCSharpAsync(Expression<Func<ProbeEntity, bool>> predicate)
    {
        var stored = await Repository<IProbeRepository>().Query().ToListAsync(Ct);
        stored.Should().HaveCount(SeedProbes().Count);

        var expected = stored.Where(predicate.Compile()).Select(p => p.ProbeId).ToList();

        var actual = await Repository<IProbeRepository>().Query().Where(predicate).ToListAsync(Ct);

        actual
            .Select(p => p.ProbeId)
            .Should()
            .BeEquivalentTo(expected, "条件 {0} は C# の意味論と同じ行を返すべき", predicate);
    }

    // ===== 列を含まない部分式（クロージャの値・定数）は先に評価する =====

    /// <summary>
    /// 「null なら絞らない」書き方が、絞らない側・絞る側の両方で C# と同じ行を返すことを検証する。
    /// </summary>
    /// <remarks>
    /// 列を含まない比較（<c>filter == null</c>）を値どうしのパラメータ比較にすると、NULL を束縛した <c>@p0 = @p1</c> が
    /// UNKNOWN になり、絞らないはずの条件が NULL の行だけを返す。
    /// </remarks>
    [Fact(DisplayName = "[翻訳パリティ] null なら絞らない書き方が C# と同じ行を返す")]
    public async Task NullFilterIdiom_MatchesCSharp()
    {
        await SetUpAsync();

        string? none = null;
        string? alice = "Alice";

        await ShouldMatchCSharpAsync(x => none == null || x.Name == none);
        await ShouldMatchCSharpAsync(x => alice == null || x.Name == alice);
        await ShouldMatchCSharpAsync(x => none != null && x.Name == none);
        await ShouldMatchCSharpAsync(x => alice != null && x.Name == alice);
    }

    /// <summary>列を含まない bool の部分式が、論理演算のどちら側にあっても C# と同じ行を返すことを検証する。</summary>
    [Fact(DisplayName = "[翻訳パリティ] 列を含まない bool の部分式を先に評価する")]
    public async Task ColumnFreeBoolean_IsEvaluatedFirst()
    {
        await SetUpAsync();

        var on = true;
        var off = false;

        await ShouldMatchCSharpAsync(x => on || x.IsActive);
        await ShouldMatchCSharpAsync(x => off || x.IsActive);
        await ShouldMatchCSharpAsync(x => on && x.IsActive);
        await ShouldMatchCSharpAsync(x => off && x.IsActive);
        await ShouldMatchCSharpAsync(x => x.IsActive && !on);
        await ShouldMatchCSharpAsync(x => x.IsActive || !off);
        await ShouldMatchCSharpAsync(x => on);
        await ShouldMatchCSharpAsync(x => !on);
    }

    /// <summary>
    /// 左辺で結論が決まるとき、右辺の列を含まない部分を評価しないことを検証する（C# の短絡評価）。
    /// </summary>
    /// <remarks><c>text</c> は null なので、右辺の <c>text.Length</c> を評価すると NullReferenceException になる。</remarks>
    [Fact(DisplayName = "[翻訳パリティ] 論理演算は C# と同じく左から短絡する")]
    public async Task LogicalOperators_ShortCircuitLikeCSharp()
    {
        await SetUpAsync();

        string? text = null;

        await ShouldMatchCSharpAsync(x => text == null || (text.Length > 3 && x.IsActive));
        await ShouldMatchCSharpAsync(x => text != null && (text.Length > 3 || x.IsActive));
    }

    // ===== 固定長の列の LIKE =====

    /// <summary>
    /// 固定長の列（<c>nchar(10)</c>）への StartsWith / EndsWith / Contains が C# と同じ行を返すことを検証する。
    /// </summary>
    /// <remarks>
    /// パターンを列の固定長の型で束縛すると、パターン自体が空白で詰められ（<c>'ab%       '</c>）一致しなくなる。
    /// </remarks>
    [Fact(DisplayName = "[翻訳パリティ] 固定長の列の文字列一致が C# と同じ行を返す")]
    public async Task StringMatchOnFixedLengthColumn_MatchesCSharp()
    {
        await SetUpAsync();

        await ShouldMatchCSharpAsync(x => x.Code != null && x.Code.StartsWith("ab"));
        await ShouldMatchCSharpAsync(x => x.Code != null && x.Code.EndsWith("c"));
        await ShouldMatchCSharpAsync(x => x.Code != null && x.Code.Contains("ab"));
    }

    /// <summary>char を受ける多重定義の文字列一致が、C# と同じ行を返すことを検証する。</summary>
    [Fact(DisplayName = "[翻訳パリティ] char を受ける文字列一致が C# と同じ行を返す")]
    public async Task StringMatchWithCharArgument_MatchesCSharp()
    {
        await SetUpAsync();

        await ShouldMatchCSharpAsync(x => x.Name != null && x.Name.Contains('l'));
        await ShouldMatchCSharpAsync(x => x.Name != null && x.Name.StartsWith('A'));
        await ShouldMatchCSharpAsync(x => x.Name != null && x.Name.EndsWith('e'));
    }

    // ===== IsNullOrEmpty / IsNullOrWhiteSpace =====

    /// <summary>
    /// <c>string.IsNullOrEmpty</c> / <c>IsNullOrWhiteSpace</c> が、空白だけ・タブ・全角空白・改行を含む値で
    /// C# と同じ行を返すことを検証する（可変長の name 列と固定長の code 列の両方）。
    /// </summary>
    /// <remarks>
    /// SQL Server の <c>col = ''</c> は末尾の空白を無視して比べるため、空白だけの値も空とみなす。
    /// code 列（<c>nchar(10)</c>）は SQL Server では詰めて格納・返却されるので、C# の結論も詰めた値に対して出す（基底の期待値の取り方）。
    /// </remarks>
    [Fact(DisplayName = "[翻訳パリティ] IsNullOrEmpty / IsNullOrWhiteSpace が C# と同じ行を返す")]
    public async Task NullOrEmptyChecks_MatchCSharp()
    {
        await SetUpAsync();

        await ShouldMatchCSharpAsync(x => string.IsNullOrEmpty(x.Name));
        await ShouldMatchCSharpAsync(x => !string.IsNullOrEmpty(x.Name));
        await ShouldMatchCSharpAsync(x => string.IsNullOrWhiteSpace(x.Name));
        await ShouldMatchCSharpAsync(x => !string.IsNullOrWhiteSpace(x.Name));
        await ShouldMatchCSharpAsync(x => string.IsNullOrEmpty(x.Code));
        await ShouldMatchCSharpAsync(x => string.IsNullOrWhiteSpace(x.Code));
    }

    // ===== 識別子の引用 =====

    /// <summary>
    /// 予約語の列名（<c>group</c>）・閉じ角括弧を含む列名（<c>x]y</c>）の値を Query() で読み戻せることを検証する。
    /// </summary>
    /// <remarks>
    /// SQL Server の取得は FOR JSON で列ごとに別名を付けるため、別名・列名を角括弧で囲まないと
    /// 予約語で構文エラーになり、<c>]</c> を二重にしないと文が壊れる。
    /// </remarks>
    [Fact(DisplayName = "[翻訳パリティ] 予約語・閉じ角括弧の列名の値を Query で読み戻せる")]
    public async Task ReservedAndBracketColumnNames_RoundTripThroughQuery()
    {
        await SetUpAsync();

        var probe = await Repository<IProbeRepository>()
            .Query()
            .Where(x => x.ProbeId == 1)
            .FirstOrDefaultAsync(Ct);

        probe.Should().NotBeNull();
        probe!.Group.Should().Be("g1");
        probe.XY.Should().Be("a]b");

        await ShouldMatchCSharpAsync(x => x.Group == "g1");
        await ShouldMatchCSharpAsync(x => x.XY == "a]b");
    }

    /// <summary>
    /// 予約語の表名（<c>order</c>）を親に持つ Include が、子方向・親参照の両方で関連を組み立てることを検証する。
    /// </summary>
    /// <remarks>
    /// 親参照のナビゲーション名 <c>Order</c> は SQL Server の FOR JSON で別名になる。角括弧で囲まないと構文エラーになる。
    /// </remarks>
    [Fact(
        DisplayName = "[翻訳パリティ] 予約語の表名・ナビゲーション名を経由する Include が関連を組み立てる"
    )]
    public async Task IncludeThroughReservedWordNames_AssemblesRelations()
    {
        await SetUpAsync();

        await Repository<IOrderRepository>()
            .InsertAsync(new OrderEntity { OrderId = 1, Group = "first" }, Ct);
        await Repository<IOrderRepository>()
            .InsertAsync(new OrderEntity { OrderId = 2, Group = "second" }, Ct);
        await Repository<IOrderLineRepository>()
            .InsertAsync(new OrderLineEntity { LineId = 10, OrderId = 1 }, Ct);
        await Repository<IOrderLineRepository>()
            .InsertAsync(new OrderLineEntity { LineId = 11, OrderId = 1 }, Ct);
        await Repository<IOrderLineRepository>()
            .InsertAsync(new OrderLineEntity { LineId = 20, OrderId = 2 }, Ct);

        var lines = await Repository<IOrderLineRepository>()
            .Query()
            .Include(x => x.Order)
            .ToListAsync(Ct);

        lines
            .Select(l => (l.LineId, l.Order.Group))
            .Should()
            .BeEquivalentTo([(10, "first"), (11, "first"), (20, "second")]);

        var orders = await Repository<IOrderRepository>()
            .Query()
            .Include(x => x.OrderLines)
            .ToListAsync(Ct);

        orders
            .Select(o => (o.OrderId, Lines: o.OrderLines.Select(l => l.LineId).Order().ToList()))
            .Should()
            .BeEquivalentTo([(1, new List<int> { 10, 11 }), (2, new List<int> { 20 })]);
    }

    // ===== CLR の型が違うキーの Include =====

    /// <summary>
    /// 主キーが long（bigint）・外部キーが int の親子を投入する（SQL Server はこの外部キー制約を作らないが、値の組は同じ）。
    /// </summary>
    private async Task SeedCrossTypeFamilyAsync()
    {
        var parents = Repository<IBigParentRepository>();
        await parents.InsertAsync(new BigParentEntity { Id = 1, Label = "a" }, Ct);
        await parents.InsertAsync(new BigParentEntity { Id = 2, Label = "b" }, Ct);
        await parents.InsertAsync(new BigParentEntity { Id = 3, Label = "c" }, Ct);

        var children = Repository<ISmallChildRepository>();
        await children.InsertAsync(new SmallChildEntity { Id = 10, BigParentId = 1 }, Ct);
        await children.InsertAsync(new SmallChildEntity { Id = 11, BigParentId = 1 }, Ct);
        await children.InsertAsync(new SmallChildEntity { Id = 20, BigParentId = 2 }, Ct);
        await children.InsertAsync(new SmallChildEntity { Id = 30, BigParentId = null }, Ct);
    }

    /// <summary>
    /// 主キーと外部キーの CLR の型が違う（long と int）親子でも、Include が子方向・親参照の両方で関連を組み立てることを検証する。
    /// </summary>
    /// <remarks>
    /// 箱詰めした値どうしの比較では <c>(object)1L</c> と <c>(object)1</c> が等しくならないため、
    /// 突き合わせを CLR の値で行う実装先（SQLite・インメモリ）ではナビゲーションが黙って空になっていた。
    /// </remarks>
    [Fact(DisplayName = "[翻訳パリティ] 主キーと外部キーの型が違っても Include が関連を組み立てる")]
    public async Task IncludeAcrossKeyTypes_AssemblesRelations()
    {
        await SetUpAsync();
        await SeedCrossTypeFamilyAsync();

        var parents = await Repository<IBigParentRepository>()
            .Query()
            .Include(x => x.SmallChilds)
            .ToListAsync(Ct);

        parents
            .Select(p => (p.Id, Children: p.SmallChilds.Select(c => c.Id).Order().ToList()))
            .Should()
            .BeEquivalentTo([
                (1L, new List<int> { 10, 11 }),
                (2L, new List<int> { 20 }),
                (3L, new List<int>()),
            ]);

        var children = await Repository<ISmallChildRepository>()
            .Query()
            .Include(x => x.BigParent)
            .ToListAsync(Ct);

        children
            .Select(c => (c.Id, c.BigParent?.Label))
            .Should()
            .BeEquivalentTo([(10, "a"), (11, "a"), (20, "b"), (30, (string?)null)]);
    }

    /// <summary>
    /// 主キーと外部キーの型が違う親子で、カスケードつきの一括削除が子まで消すことを検証する。
    /// </summary>
    /// <remarks>SQL の実装先は SQL の副問い合わせで子を消す。インメモリは CLR の値で子を探すため、型が違うと子が残っていた。</remarks>
    [Fact(
        DisplayName = "[翻訳パリティ] 主キーと外部キーの型が違ってもカスケードの一括削除が子を消す"
    )]
    public async Task CascadeDeleteAcrossKeyTypes_RemovesChildren()
    {
        await SetUpAsync();
        await SeedCrossTypeFamilyAsync();

        var deleted = await Repository<IBigParentRepository>()
            .Query()
            .Where(x => x.Id == 1)
            .ExecuteDeleteAsync(cascadeDelete: true, Ct);

        deleted.Should().Be(3);

        var remaining = await Repository<ISmallChildRepository>().Query().ToListAsync(Ct);
        remaining.Select(c => c.Id).Should().BeEquivalentTo([20, 30]);
    }

    // ===== 更新できる列が無い表の UPDATE =====

    /// <summary>
    /// キー列だけの表で、直接の UpdateAsync とグラフ保存の更新が構文エラーにならず、行の有無を正しく返すことを検証する。
    /// </summary>
    /// <remarks>
    /// 代入する列が 1 本も無いと UPDATE の SET が空になり、どちらの方言でも構文エラーになっていた。
    /// </remarks>
    [Fact(DisplayName = "[翻訳パリティ] キー列だけの表の更新が行の有無を返す")]
    public async Task UpdateOfKeyOnlyTable_ReportsWhetherTheRowExists()
    {
        await SetUpAsync();

        var tags = Repository<ITagRepository>();
        await tags.InsertAsync(new TagEntity { Name = "a" }, Ct);

        (await tags.UpdateAsync(new TagEntity { Name = "a" }, cancellationToken: Ct))
            .Should()
            .BeTrue();
        (await tags.UpdateAsync(new TagEntity { Name = "missing" }, cancellationToken: Ct))
            .Should()
            .BeFalse();

        var updated = new TagEntity { Name = "a" };
        updated.MarkUpdated();
        (await tags.SaveAsync(updated, cancellationToken: Ct)).Should().Be(1);

        var gone = new TagEntity { Name = "missing" };
        gone.MarkUpdated();
        (await tags.SaveAsync(gone, insertWhenUpdateMissing: true, cancellationToken: Ct))
            .Should()
            .Be(1);

        (await tags.Query().ToListAsync(Ct))
            .Select(t => t.Name)
            .Should()
            .BeEquivalentTo(["a", "missing"]);
    }

    /// <summary>
    /// キー列と rowversion だけの表（SQL Server では更新できる列が無い）で、読み直した実体の更新が通ることを検証する。
    /// </summary>
    [Fact(DisplayName = "[翻訳パリティ] キー列と rowversion だけの表の更新が通る")]
    public async Task UpdateOfVersionOnlyTable_Succeeds()
    {
        await SetUpAsync();

        var tags = Repository<IVersionedTagRepository>();
        await tags.InsertAsync(new VersionedTagEntity { Name = "v" }, Ct);

        var current = await tags.GetByIdAsync("v", Ct);
        current.Should().NotBeNull();

        (await tags.UpdateAsync(current!, cancellationToken: Ct)).Should().BeTrue();
        (await tags.UpdateAsync(new VersionedTagEntity { Name = "missing" }, cancellationToken: Ct))
            .Should()
            .BeFalse();

        var reread = await tags.GetByIdAsync("v", Ct);
        reread!.MarkUpdated();
        (await tags.SaveAsync(reread, cancellationToken: Ct)).Should().Be(1);
    }

    // ===== 日時の部品 =====

    /// <summary>
    /// <c>Nullable&lt;T&gt;.Value</c> を経由した列の参照（日付の部品・比較）が翻訳され、C# と同じ行を返すことを検証する。
    /// </summary>
    [Fact(DisplayName = "[翻訳パリティ] Nullable の Value を経由した列の参照が C# と同じ行を返す")]
    public async Task NullableValueAccess_MatchesCSharp()
    {
        await SetUpAsync();

        var at = new DateTime(2024, 1, 16, 0, 0, 0);

        await ShouldMatchCSharpAsync(x => x.At != null && x.At.Value.Year == 2024);
        await ShouldMatchCSharpAsync(x => x.At != null && x.At.Value.Day == 15);
        await ShouldMatchCSharpAsync(x => x.At != null && x.At.Value == at);
        await ShouldMatchCSharpAsync(x => x.At != null && x.At.Value > at);
    }

    /// <summary>
    /// 日時の列の <c>.Date</c> が、等号・範囲の比較で C# と同じ行を返すことを検証する。
    /// </summary>
    /// <remarks>
    /// SQLite の <c>date(col)</c> は 'YYYY-MM-DD' を返すが、DateTime のパラメータは 'YYYY-MM-DD HH:MM:SS' で束縛されるため、
    /// 文字列としての比較が食い違う（等号は一致せず、<c>&gt;=</c> は境界の日を落とす）。
    /// </remarks>
    [Fact(DisplayName = "[翻訳パリティ] 日時の列の Date が C# と同じ行を返す")]
    public async Task DatePartDate_MatchesCSharp()
    {
        await SetUpAsync();

        var day = new DateTime(2024, 1, 15);

        await ShouldMatchCSharpAsync(x => x.At != null && x.At.Value.Date == day);
        await ShouldMatchCSharpAsync(x => x.At != null && x.At.Value.Date >= day);
        await ShouldMatchCSharpAsync(x => x.At != null && x.At.Value.Date < day);
    }

    /// <summary>
    /// オフセットつき日時の列の日付部品が、UTC へ換算されずに格納した時差のまま評価されることを検証する（C# と同じ）。
    /// </summary>
    [Fact(DisplayName = "[翻訳パリティ] オフセットつき日時の日付部品が C# と同じ行を返す")]
    public async Task DateTimeOffsetParts_MatchCSharp()
    {
        await SetUpAsync();

        var newYear = new DateTime(2024, 1, 1);

        await ShouldMatchCSharpAsync(x => x.AtOffset != null && x.AtOffset.Value.Year == 2024);
        await ShouldMatchCSharpAsync(x => x.AtOffset != null && x.AtOffset.Value.Month == 12);
        await ShouldMatchCSharpAsync(x => x.AtOffset != null && x.AtOffset.Value.Day == 1);
        await ShouldMatchCSharpAsync(x => x.AtOffset != null && x.AtOffset.Value.Hour == 5);
        await ShouldMatchCSharpAsync(x => x.AtOffset != null && x.AtOffset.Value.Minute == 30);
        await ShouldMatchCSharpAsync(x => x.AtOffset != null && x.AtOffset.Value.DayOfYear == 1);
        await ShouldMatchCSharpAsync(x => x.AtOffset != null && x.AtOffset.Value.Date == newYear);
    }
}
