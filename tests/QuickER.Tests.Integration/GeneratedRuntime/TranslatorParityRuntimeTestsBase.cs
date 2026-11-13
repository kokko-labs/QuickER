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

    /// <summary>
    /// 固定長のバイナリの列（プロパティ名 → 長さ）。期待を作るとき、これらの列と比べる値をその列へ保存したときの値へ直す
    /// （<see cref="ComparingBytes"/>。SQL Server の派生だけが持つ）
    /// </summary>
    protected virtual IReadOnlyDictionary<string, int>? FixedBinaryLengths => null;

    /// <summary>
    /// SQL Server の図の固定長のバイナリの列（<c>bin_fixed</c> は <c>binary(4)</c>＝<see cref="TranslatorParityFixtureDefinition"/>）
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, int> SqlServerFixedBinaryLengths =
        new Dictionary<string, int>(StringComparer.Ordinal) { ["BinFixed"] = 4 };

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
    /// name と group は NULL の 4 つの組を持つ（id 1＝両方非 NULL の異値・id 10＝name だけ NULL・id 11＝両方非 NULL の同値・
    /// ほか＝group が NULL）。NULL 許容の decimal の amount は 100・200・300 と、境界の 250・重複の 100・NULL を散らす。
    /// 列の型より細かい値と比べる列は id 1・2 が値を持つ（day は日付だけ・at2 と clock は 1/100 秒まで）。Unicode でない ansi は、
    /// 列が表せない文字が化けた形の '??'（id 2）と '?'（id 4）を持つ。
    /// </remarks>
    internal static IReadOnlyList<ProbeEntity> SeedProbes() =>
        [
            Probe(
                1,
                "Alice",
                "abc",
                new DateTime(2024, 1, 15, 10, 30, 0),
                new DateTimeOffset(2024, 1, 1, 5, 0, 0, TimeSpan.FromHours(9)),
                true,
                group: "g1",
                xy: "a]b",
                amount: 100m,
                day: new DateTime(2024, 1, 1),
                at2: new DateTime(2024, 1, 1, 10, 0, 0).AddMilliseconds(120),
                clock: new TimeSpan(0, 10, 0, 0, 120),
                ansi: "abc",
                bin: [0x01],
                binFixed: [0x01]
            ),
            Probe(
                2,
                null,
                "abcd",
                new DateTime(2024, 1, 16, 0, 0, 0),
                new DateTimeOffset(2023, 12, 31, 22, 0, 0, TimeSpan.FromHours(-3)),
                false,
                amount: 200m,
                day: new DateTime(2024, 1, 2),
                at2: new DateTime(2024, 1, 1, 10, 0, 0).AddMilliseconds(130),
                clock: new TimeSpan(0, 10, 0, 0, 130),
                ansi: "??",
                bin: [0x01, 0x00],
                binFixed: [0x01, 0x00]
            ),
            Probe(
                3,
                "  ",
                "xabx",
                new DateTime(2024, 1, 14, 23, 59, 59),
                new DateTimeOffset(2024, 6, 30, 23, 30, 0, TimeSpan.Zero),
                true,
                amount: 300m,
                bin: [0x01, 0x00, 0x00, 0x00],
                binFixed: [0x01]
            ),
            Probe(4, "\t", null, null, null, false, ansi: "?", bin: [0xFF]),
            Probe(
                5,
                "　",
                "  ",
                new DateTime(2023, 5, 1, 0, 0, 0),
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.FromHours(9)),
                true,
                amount: 100m,
                binFixed: [0x01, 0x00, 0x00, 0x00]
            ),
            Probe(6, "", "Zc", new DateTime(2024, 1, 15, 0, 0, 0), null, false),
            Probe(7, "Bob\n", null, new DateTime(2025, 1, 15, 8, 0, 0), null, true, amount: 250m),
            Probe(8, "label", "ab", null, null, false),
            Probe(9, "x", "", null, null, false),
            Probe(10, null, null, null, null, true, group: "g10"),
            Probe(11, "Alicia", null, null, null, false, group: "Alicia"),
        ];

    private static ProbeEntity Probe(
        int id,
        string? name,
        string? code,
        DateTime? at,
        DateTimeOffset? atOffset,
        bool isActive,
        string? group = null,
        string? xy = null,
        decimal? amount = null,
        DateTime? day = null,
        DateTime? at2 = null,
        TimeSpan? clock = null,
        string? ansi = null,
        byte[]? bin = null,
        byte[]? binFixed = null
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
            Amount = amount,
            Day = day,
            At2 = at2,
            Clock = clock,
            Ansi = ansi,
            Bin = bin,
            BinFixed = binFixed,
        };

    /// <summary>
    /// 実装先の Query() が返す行の集合が、同じ実装先から読み戻した全行へ C# のラムダを当てた結果と一致することを表明する。
    /// </summary>
    protected Task ShouldMatchCSharpAsync(Expression<Func<ProbeEntity, bool>> predicate) =>
        ShouldMatchCSharpAsync(
            Repository<IProbeRepository>(),
            predicate,
            p => p.ProbeId,
            SeedProbes().Count,
            FixedBinaryLengths
        );

    /// <summary>
    /// <paramref name="repository"/> の Query() が返す行の集合が、同じ実装先から読み戻した全行へ C# のラムダを当てた結果と
    /// 一致することを表明する（表を問わない形。<paramref name="seeded"/> は読み戻す行の数）。
    /// </summary>
    protected static async Task ShouldMatchCSharpAsync<TEntity, TKey>(
        IRepository<TEntity, TKey> repository,
        Expression<Func<TEntity, bool>> predicate,
        Func<TEntity, long> key,
        int seeded,
        IReadOnlyDictionary<string, int>? fixedBinaryLengths = null
    )
        where TEntity : EntityBaseCore, new()
    {
        var stored = await repository.Query().ToListAsync(Ct);
        stored.Should().HaveCount(seeded);

        var expected = stored
            .Where(
                (
                    (Expression<Func<TEntity, bool>>)ComparingBytes(predicate, fixedBinaryLengths)
                ).Compile()
            )
            .Select(key)
            .ToList();

        var actual = await repository.Query().Where(predicate).ToListAsync(Ct);

        actual
            .Select(key)
            .Should()
            .BeEquivalentTo(expected, "条件 {0} は C# の意味論と同じ行を返すべき", predicate);
    }

    /// <summary>
    /// 期待を作る述語の、バイナリ（<c>byte[]</c>）の <c>==</c>・<c>!=</c> と、<c>byte[]</c> の並びの <c>Contains</c> を、バイトの列の比較へ
    /// 書き換える（ほかの部分はそのまま）
    /// </summary>
    /// <remarks>
    /// コンパイルした C# の <c>byte[]</c> の <c>==</c> は参照の比較で、読み戻した配列はどれも別のインスタンスなので、どの行にも一致しない。
    /// docs の約束（「大文字小文字・空白・日付部品」の節のバイナリの段落＝どの実装先もバイトの列で比べる。インメモリ実装も中身で比べる）を
    /// 期待にする。
    /// </remarks>
    /// <param name="predicate">述語</param>
    /// <param name="fixedBinaryLengths">
    /// 固定長のバイナリの列（プロパティ名 → 長さ）。その列と比べる値は、その列へ保存したときの値（<see cref="AsStoredInFixedBinary"/>）へ
    /// 直してから比べる（docs のバイナリの段落の SQL Server の固定長の列の規則）。列どうしの比較は直さない
    /// </param>
    internal static LambdaExpression ComparingBytes(
        LambdaExpression predicate,
        IReadOnlyDictionary<string, int>? fixedBinaryLengths = null
    ) => (LambdaExpression)new ByteComparisonRewriter(fixedBinaryLengths).Visit(predicate);

    /// <summary>2 つのバイナリがバイトの列で等しいか（NULL どうしは等しく、片方だけの NULL は等しくない）</summary>
    private static bool SameBytes(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    /// <summary>並びに、バイトの列で等しいバイナリがあるか</summary>
    private static bool ContainsBytes(IEnumerable<byte[]?> values, byte[]? value) =>
        values.Any(item => SameBytes(item, value));

    /// <summary>並びの要素をそれぞれ固定長の列へ保存したときの値に直したとき、バイトの列で等しいバイナリがあるか</summary>
    private static bool ContainsBytesAsStored(
        IEnumerable<byte[]?> values,
        byte[]? value,
        int length
    ) => values.Any(item => SameBytes(AsStoredInFixedBinary(item, length), value));

    /// <summary>
    /// 固定長のバイナリの列（<c>binary(n)</c>）へ保存したときの値：短い値は 0x00 で n バイトに詰め、超えた分が 0x00 だけの長い値は
    /// n バイトに切る。ほかの長い値は保存できないのでそのまま（長さが違うのでどの行とも等しくない）。docs のバイナリの段落の写し
    /// </summary>
    private static byte[]? AsStoredInFixedBinary(byte[]? value, int length)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length < length)
        {
            var padded = new byte[length];
            value.CopyTo(padded, 0);
            return padded;
        }

        return value.Length > length && value.AsSpan(length).IndexOfAnyExcept((byte)0) < 0
            ? value[..length]
            : value;
    }

    /// <summary><see cref="ComparingBytes"/> の書き換え</summary>
    private sealed class ByteComparisonRewriter(
        IReadOnlyDictionary<string, int>? fixedBinaryLengths
    ) : ExpressionVisitor
    {
        /// <summary>式が固定長のバイナリの列（述語の引数のプロパティ）なら、その長さ</summary>
        private int? FixedLengthOf(Expression expression) =>
            expression is MemberExpression { Expression: ParameterExpression } member
            && fixedBinaryLengths?.TryGetValue(member.Member.Name, out var length) == true
                ? length
                : null;

        /// <summary>値の側を、相手が固定長の列なら、その列へ保存したときの値へ直す式（列どうしは直さない）</summary>
        private Expression AsStored(Expression value, Expression other) =>
            FixedLengthOf(other) is int length
            && value is not MemberExpression { Expression: ParameterExpression }
                ? Expression.Call(
                    typeof(TranslatorParityRuntimeTestsBase).GetMethod(
                        nameof(AsStoredInFixedBinary),
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Static
                    )!,
                    Visit(value),
                    Expression.Constant(length)
                )
                : Visit(value);

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (
                node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual
                && node.Left.Type == typeof(byte[])
                && node.Right.Type == typeof(byte[])
            )
            {
                Expression same = Expression.Call(
                    typeof(TranslatorParityRuntimeTestsBase).GetMethod(
                        nameof(SameBytes),
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Static
                    )!,
                    AsStored(node.Left, node.Right),
                    AsStored(node.Right, node.Left)
                );

                return node.NodeType == ExpressionType.Equal ? same : Expression.Not(same);
            }

            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // リストの Contains（インスタンス）と Enumerable.Contains（静的）の両方
            var (source, value) = node switch
            {
                { Method.Name: "Contains", Object: { } list, Arguments.Count: 1 } => (
                    list,
                    node.Arguments[0]
                ),
                { Method.Name: "Contains", Object: null, Arguments.Count: 2 } => (
                    node.Arguments[0],
                    node.Arguments[1]
                ),
                _ => (null, null),
            };

            if (
                source is not null
                && value?.Type == typeof(byte[])
                && typeof(IEnumerable<byte[]?>).IsAssignableFrom(source.Type)
            )
            {
                if (FixedLengthOf(value) is int length)
                {
                    return Expression.Call(
                        typeof(TranslatorParityRuntimeTestsBase).GetMethod(
                            nameof(ContainsBytesAsStored),
                            System.Reflection.BindingFlags.NonPublic
                                | System.Reflection.BindingFlags.Static
                        )!,
                        Visit(source),
                        Visit(value),
                        Expression.Constant(length)
                    );
                }

                return Expression.Call(
                    typeof(TranslatorParityRuntimeTestsBase).GetMethod(
                        nameof(ContainsBytes),
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Static
                    )!,
                    Visit(source),
                    Visit(value)
                );
            }

            return base.VisitMethodCall(node);
        }
    }

    // ===== 述語の表 =====

    /// <summary>
    /// 述語の表（<see cref="TranslatorParityPredicateRows"/>）の 1 行が、C# と同じ行を返すことを検証する。
    /// </summary>
    /// <remarks>
    /// 行は式の翻訳の NULL の補償・否定の反転と畳み込み・IN の null の要素・空の IN・bool 列の短縮形などの形を持つ。
    /// small_child の行は、親子のシード（<see cref="SeedCrossTypeFamilyAsync"/>）を入れてから比べる。big_parent の行は
    /// <see cref="WideBigParents"/>、整数部の大きい probes の行は probes のシードに <see cref="WideProbes"/> を足してから比べる。
    /// </remarks>
    [Theory(DisplayName = "[翻訳パリティ] 述語の表の行が C# と同じ行を返す")]
    [MemberData(
        nameof(TranslatorParityPredicateRows.Ids),
        MemberType = typeof(TranslatorParityPredicateRows)
    )]
    public async Task Predicate_MatchesCSharp(string id)
    {
        await SetUpAsync();

        switch (TranslatorParityPredicateRows.Table[id].Input)
        {
            case ProbePredicate probe:
                await ShouldMatchCSharpAsync(probe.Predicate);
                break;

            case SmallChildPredicate child:
                await SeedCrossTypeFamilyAsync();
                await ShouldMatchCSharpAsync(
                    Repository<ISmallChildRepository>(),
                    child.Predicate,
                    c => c.Id,
                    CrossTypeChildCount
                );
                break;

            case BigParentPredicate parent:
                var parents = Repository<IBigParentRepository>();
                foreach (var seed in WideBigParents())
                {
                    await parents.InsertAsync(seed, Ct);
                }

                await ShouldMatchCSharpAsync(
                    parents,
                    parent.Predicate,
                    p => p.Id,
                    WideBigParents().Count
                );
                break;

            case WideProbePredicate wide:
                var probes = Repository<IProbeRepository>();
                foreach (var seed in WideProbes())
                {
                    await probes.InsertAsync(seed, Ct);
                }

                await ShouldMatchCSharpAsync(
                    probes,
                    wide.Predicate,
                    p => p.ProbeId,
                    SeedProbes().Count + WideProbes().Count
                );
                break;

            default:
                throw new InvalidOperationException($"行 {id} の入力の型を扱えない");
        }
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
    /// SQL Server の取得は列名を角括弧で囲む（Include の無い取得はプレーン SELECT の列リスト、Include のある取得は
    /// FOR JSON の列ごとの別名）。囲まないと予約語で構文エラーになり、<c>]</c> を二重にしないと文が壊れる。
    /// FOR JSON の別名の側は <see cref="SqlServerPlainQueryRuntimeTests"/> の値の比較が同じ列名で確かめる。
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

    /// <summary>
    /// ナビゲーションプロパティを使う条件・並び替えを、どの実装先も NotSupportedException で断ることを検証する。
    /// </summary>
    /// <remarks>
    /// ナビゲーションは列ではない。SQL の実装先は翻訳できずに断る。インメモリは行だけを持ち、保存行のナビゲーションは
    /// 常に空なので、評価すると <c>x.OrderLines.Any()</c> は 0 件・<c>x.Order == null</c> は全件を黙って返す。
    /// 3 つの実装先で同じく断ることを固定する（外部キーの列で絞る、が共通の書き換え）。
    /// </remarks>
    [Fact(
        DisplayName = "[翻訳パリティ] ナビゲーションを使う条件・並び替えは NotSupportedException で断る"
    )]
    public async Task NavigationInConditionOrOrdering_IsRejected()
    {
        await SetUpAsync();

        await Repository<IOrderRepository>()
            .InsertAsync(new OrderEntity { OrderId = 1, Group = "first" }, Ct);
        await Repository<IOrderLineRepository>()
            .InsertAsync(new OrderLineEntity { LineId = 10, OrderId = 1 }, Ct);

        var anyChild = () =>
            Repository<IOrderRepository>().Query().Where(o => o.OrderLines.Any()).ToListAsync(Ct);
        var nullParent = () =>
            Repository<IOrderLineRepository>().Query().Where(l => l.Order == null).ToListAsync(Ct);
        var countByParent = () =>
            Repository<IOrderLineRepository>().Query().Where(l => l.Order == null).CountAsync(Ct);
        var orderByParent = () =>
            Repository<IOrderLineRepository>().Query().OrderBy(l => l.Order.Group).ToListAsync(Ct);

        await anyChild.Should().ThrowAsync<NotSupportedException>();
        await nullParent.Should().ThrowAsync<NotSupportedException>();
        await countByParent.Should().ThrowAsync<NotSupportedException>();
        await orderByParent.Should().ThrowAsync<NotSupportedException>();
    }

    // ===== CLR の型が違うキーの Include =====

    /// <summary><see cref="SeedCrossTypeFamilyAsync"/> が入れる small_child の行の数</summary>
    private const int CrossTypeChildCount = 4;

    /// <summary>
    /// 主キーが long（bigint）・外部キーが int の親子を投入する（SQL Server はこの外部キー制約を作らないが、値の組は同じ）。
    /// </summary>
    private async Task SeedCrossTypeFamilyAsync()
    {
        var parents = Repository<IBigParentRepository>();
        foreach (var parent in CrossTypeParents())
        {
            await parents.InsertAsync(parent, Ct);
        }

        var children = Repository<ISmallChildRepository>();
        foreach (var child in CrossTypeChildren())
        {
            await children.InsertAsync(child, Ct);
        }
    }

    /// <summary>big_parent のシード（呼ぶたびに新しいインスタンスを返す）</summary>
    internal static IReadOnlyList<BigParentEntity> CrossTypeParents() =>
        [
            new() { Id = 1, Label = "a" },
            new() { Id = 2, Label = "b" },
            new() { Id = 3, Label = "c" },
        ];

    /// <summary>
    /// 述語の表の big_parent の行（<see cref="BigParentPredicate"/>）のシード：<c>long</c> の上限と下限、整数部が 17 桁と 16 桁の値、
    /// 0・1・-1（呼ぶたびに新しいインスタンスを返す。親子のシード <see cref="CrossTypeParents"/> とは別）
    /// </summary>
    internal static IReadOnlyList<BigParentEntity> WideBigParents() =>
        [
            new() { Id = 1, Label = "one" },
            new() { Id = 0, Label = "zero" },
            new() { Id = -1, Label = "minus-one" },
            new() { Id = long.MaxValue, Label = "max" },
            new() { Id = long.MinValue, Label = "min" },
            new() { Id = 10_000_000_000_000_000, Label = "17-digits" },
            new() { Id = 9_999_999_999_999_999, Label = "16-digits" },
        ];

    /// <summary>
    /// 述語の表の整数部の大きい probes の行（<see cref="WideProbePredicate"/>）で、probes のシードに足す行：<c>probe_id</c> が int の
    /// 上限と下限（<c>decimal(10,2)</c> の amount の整数部 8 桁に入らない）と、<c>probe_id</c> と amount が同じ値の行
    /// （呼ぶたびに新しいインスタンスを返す）
    /// </summary>
    internal static IReadOnlyList<ProbeEntity> WideProbes() =>
        [
            Probe(int.MaxValue, null, null, null, null, true, amount: 1.5m),
            Probe(int.MinValue, null, null, null, null, false, amount: -1.5m),
            Probe(12, null, null, null, null, true, amount: 12m),
        ];

    /// <summary>small_child のシード（外部キー 1・1・2・NULL。呼ぶたびに新しいインスタンスを返す）</summary>
    internal static IReadOnlyList<SmallChildEntity> CrossTypeChildren() =>
        [
            new() { Id = 10, BigParentId = 1 },
            new() { Id = 11, BigParentId = 1 },
            new() { Id = 20, BigParentId = 2 },
            new() { Id = 30, BigParentId = null },
        ];

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
