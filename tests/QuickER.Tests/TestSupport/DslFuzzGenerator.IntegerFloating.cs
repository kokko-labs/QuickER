using QuickER.Model;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 整数の列と、浮動小数点の型トークン（<c>float32</c>・<c>float64</c>）で型付けしたパラメータを比べる原子（浮動小数点の種の単位の
/// <c>Z{i}</c> と、浮動小数点の網羅の単位の <c>KZ{i}</c>）
/// </summary>
/// <remarks>
/// <para>
/// 作る形：浮動小数点の単位の相方の NULL を許す int・bigint の列と、浮動小数点のパラメータの比較。C# は列の値を浮動小数点へ変換して
/// 比べる（float は 2^24、double は 2^53 を超える整数を丸める）ので、値は丸めると行の値に重なるもの（<c>16777216f</c> は 16777217 の行と、
/// <c>2^53</c> は 2^53+1 の行と等しい）を選ぶ。NOT NULL の bigint の列には、2^53 と 2^53+2 の <c>float64</c> のリストの IN・NOT IN も
/// 作る（種の単位と網羅の単位の両方）。
/// </para>
/// <para>
/// 浮動小数点の単位だけが呼ぶ（既存の単位の乱数の引き方を変えない）。本流・使い回しと並び替え・列の型に入らない値・bigint と decimal の
/// 列と別の乱数の列（<see cref="IntegerFloatingSeed"/>）の生成器のインスタンスで作り、パラメータ名の接頭辞も分ける
/// （<see cref="IntegerFloatingParameterPrefix"/>）。値オブジェクトの単位では作らない（整数を包む値オブジェクトの列は浮動小数点の
/// パラメータを型の検査が断る）。SQLite の単位は <c>float32</c> を使わない（SQLite の型トークン <c>float32</c> は REAL＝double で、
/// 単精度のパラメータを作れない＝作らない形の宣言）。
/// </para>
/// </remarks>
public sealed partial class DslFuzzGenerator
{
    /// <summary>整数の列と浮動小数点のパラメータの原子の乱数の列の種（ほかの乱数の列と別にする）</summary>
    public static int IntegerFloatingSeed(int seed) => unchecked(seed * 7919 + 49979687);

    /// <summary>整数の列と浮動小数点のパラメータのパラメータ名の接頭辞（<c>p</c>・<c>q</c>・<c>r</c>・<c>w</c> と重ねない）</summary>
    public const string IntegerFloatingParameterPrefix = "z";

    /// <summary>
    /// <c>float32</c> で型付けする値（16777216f は 16777217 の行と等しく、16777218f は 16777217 の行より大きい＝float へ丸めた行と比べる。
    /// 1e8f は 100000001 の行と等しい＝10 進で短く書けて decimal へ正確に変換できる値で、整数の列の型でも束縛できる値の形）
    /// </summary>
    private static readonly object[] IntegralFloats = [16777216f, 16777218f, 0f, -1f, 100000000f];

    /// <summary>
    /// <c>float64</c> で型付けする値（2^53 は 2^53+1 の行と等しく、2^53+2 は 2^53+1 の行より大きい＝double へ丸めた行と比べる。
    /// 1e16 は 10000000000000001 の行と等しい＝10 進で短く書けて decimal へ正確に変換できる値で、整数の列の型でも束縛できる値の形）
    /// </summary>
    private static readonly object[] IntegralDoubles =
    [
        9007199254740992d,
        9007199254740994d,
        0d,
        -1d,
        10000000000000000d,
    ];

    /// <summary>網羅の単位が演算子ごとに必ず作る値の位置（丸めると行に重なる値・その次の値・decimal へ正確に変換できる丸めの値）</summary>
    private static readonly int[] RoundingIndexes = [0, 1, 4];

    /// <summary>種の単位の末尾に足す原子のクエリ <c>Z{i}</c>（綴りは番号で回す）</summary>
    /// <remarks>
    /// 形は網羅の単位（<see cref="IntegerFloatingCoverageQueries"/>）より絞る：<c>float32</c> の = と &gt;、<c>float64</c> の = と &lt;=。
    /// </remarks>
    /// <param name="columns">列（先頭が主キー）</param>
    /// <param name="float32">単精度のパラメータも作るか（SQLite の単位は作らない）</param>
    public List<DslQuery> IntegerFloatingQueries(IReadOnlyList<DslColumn> columns, bool float32) =>
        IntegerFloatingAtoms(columns, float32, seedSubset: true)
            .Select((atom, i) => AtomQuery($"Z{i}", atom, SpellingAt(i)))
            .ToList();

    /// <summary>
    /// 網羅の単位の原子のクエリ <c>KZ{i}</c>（6 つの演算子 × <c>float32</c>・<c>float64</c> × 丸めると行に重なる値とその次の値）
    /// </summary>
    /// <param name="columns">列（先頭が主キー）</param>
    /// <param name="float32">単精度のパラメータも作るか（SQLite の単位は作らない）</param>
    public List<DslQuery> IntegerFloatingCoverageQueries(
        IReadOnlyList<DslColumn> columns,
        bool float32
    ) =>
        IntegerFloatingAtoms(columns, float32, seedSubset: false)
            .Select((atom, i) => AtomQuery($"KZ{i}", atom, SpellingAt(i)))
            .ToList();

    /// <summary>
    /// NULL を許す int・bigint の列ごとの、浮動小数点の型トークンのパラメータとの比較と、NOT NULL の bigint の列の float64 のリストの
    /// IN・NOT IN（値オブジェクトの単位では作らない）
    /// </summary>
    private IEnumerable<DslAtom> IntegerFloatingAtoms(
        IReadOnlyList<DslColumn> columns,
        bool float32,
        bool seedSubset
    )
    {
        if (valueObjects)
        {
            yield break;
        }

        var singleToken = DslFuzz.TypeToken(DslClr.Single);
        var doubleToken = DslFuzz.TypeToken(DslClr.Double);

        foreach (
            var column in FilterableColumns(columns)
                .Where(column => column.Clr is DslClr.Int32 or DslClr.Int64 && column.Nullable)
        )
        {
            if (seedSubset)
            {
                if (float32)
                {
                    yield return OutOfTypeCompare(column, DslOp.Eq, IntegralFloats, singleToken);
                    yield return OutOfTypeCompare(column, DslOp.Gt, IntegralFloats, singleToken);
                }

                yield return OutOfTypeCompare(column, DslOp.Eq, IntegralDoubles, doubleToken);
                yield return OutOfTypeCompare(column, DslOp.Le, IntegralDoubles, doubleToken);

                continue;
            }

            // 網羅の単位は丸めると行に重なる値（2^24・2^53）とその次の値を、演算子ごとに必ず 1 つずつ作る（乱数で選ぶと、丸めの形を
            // 試さない演算子が残る）
            foreach (var op in Enum.GetValues<DslOp>())
            {
                foreach (var index in RoundingIndexes)
                {
                    if (float32)
                    {
                        yield return OutOfTypeCompare(
                            column,
                            op,
                            [IntegralFloats[index]],
                            singleToken
                        );
                    }

                    yield return OutOfTypeCompare(
                        column,
                        op,
                        [IntegralDoubles[index]],
                        doubleToken
                    );
                }
            }
        }

        // NOT NULL の bigint の列と float64 のリストの IN・NOT IN（型の検査は NOT NULL の列なら列型から要素型への暗黙変換を通す。
        // C# は List<double>.Contains へ列を double へ広げて渡すので、2^53+1 の行は 2^53 の要素と等しい。NULL を許す列は要素型の一致が
        // 要るので double のリストを作れない）
        foreach (
            var column in FilterableColumns(columns)
                .Where(column => column.Clr == DslClr.Int64 && !column.Nullable)
        )
        {
            foreach (var negated in new[] { false, true })
            {
                yield return new DslIn(
                    negated,
                    new DslParam(
                        parameterPrefix + _parameterIndex++,
                        [IntegralDoubles[0], IntegralDoubles[1], IntegralDoubles[4]],
                        IsList: true,
                        ByColumn: false,
                        column
                    )
                    {
                        TypeToken = doubleToken,
                    }
                )
                {
                    Column = column,
                };
            }
        }

        if (seedSubset)
        {
            yield break;
        }

        // 鏡の形（網羅の単位だけ）：浮動小数点の列と整数の型トークンのパラメータ。C# は値を列の型へ変換して比べるので、float の列と
        // 比べる 16777217 は 16777216f、double の列と比べる 2^53+1 は 2^53 になる（行には丸めた先の値を必ず入れる＝PutRoundingValues）
        var int32Token = DslFuzz.TypeToken(DslClr.Int32);
        var int64Token = DslFuzz.TypeToken(DslClr.Int64);

        foreach (var column in FilterableColumns(columns).Where(column => column.IsFloating))
        {
            foreach (var op in Enum.GetValues<DslOp>())
            {
                yield return column.Clr == DslClr.Single
                    ? OutOfTypeCompare(column, op, MirrorInt32Values, int32Token)
                    : OutOfTypeCompare(column, op, MirrorInt64Values, int64Token);
            }
        }
    }

    /// <summary>鏡の形で float の列と比べる int32 の値（float へ変換すると 16777216f に丸まる）</summary>
    private static readonly object[] MirrorInt32Values = [16777217];

    /// <summary>鏡の形で double の列と比べる int64 の値（double へ変換すると 2^53 に丸まる）</summary>
    private static readonly object[] MirrorInt64Values = [9007199254740993L];
}
