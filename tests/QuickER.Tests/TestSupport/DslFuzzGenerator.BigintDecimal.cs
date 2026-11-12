using QuickER.Model;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// bigint の列と、decimal の型トークンで型付けしたパラメータを比べる原子（種の単位の <c>Y{i}</c> と、網羅の単位の <c>KY{i}</c>）
/// </summary>
/// <remarks>
/// <para>
/// 作る形：NULL を許す bigint の列と、decimal の型トークンのパラメータ（小数・<c>long</c> の範囲の外の値・<c>long</c> の範囲の中の整数）の
/// 比較。C# は列の値を decimal へ広げて比べる。行の値の池は <c>long</c> の上限と下限を持つので、整数部が 16 桁を超える行がいつもある
/// （SQL Server の EF Core 版が整数の式を decimal へ変換するときの CAST の型の桁を、どの値でも試す）。
/// </para>
/// <para>
/// byte・short・int の列と decimal のパラメータは、列の型に入らない値の原子（<see cref="OutOfTypeQueries"/>）が作る。bigint の列を
/// その列に混ぜると、その後ろの原子の値の引き方が変わるので、本流・使い回しと並び替え・列の型に入らない値の列と別の乱数の列
/// （<see cref="BigintDecimalSeed"/>）の生成器のインスタンスで作り、パラメータ名の接頭辞も分ける（<see cref="BigintDecimalParameterPrefix"/>）。
/// 値オブジェクトの単位では作らない（整数を包む値オブジェクトの列は decimal のパラメータを型の検査が断る）。
/// </para>
/// </remarks>
public sealed partial class DslFuzzGenerator
{
    /// <summary>bigint の列と decimal のパラメータの原子の乱数の列の種（本流・使い回しと並び替え・列の型に入らない値の列と別にする）</summary>
    public static int BigintDecimalSeed(int seed) => unchecked(seed * 7919 + 32452843);

    /// <summary>bigint の列と decimal のパラメータのパラメータ名の接頭辞（<c>p</c>・<c>q</c>・<c>r</c> と重ねない）</summary>
    public const string BigintDecimalParameterPrefix = "w";

    /// <summary><c>long</c> の範囲の中の整数の decimal の値（行の値の池の 0・1・-1・10 に重なる）</summary>
    private static readonly object[] IntegralDecimals = [0m, 1m, -1m, 10m];

    /// <summary>種の単位の末尾に足す原子のクエリ <c>Y{i}</c>（綴りは番号で回す）</summary>
    /// <remarks>
    /// 形は網羅の単位（<see cref="BigintDecimalCoverageQueries"/>）より絞る：小数の &lt; と &gt;=、範囲の外の =、範囲の中の整数の &lt;&gt;。
    /// </remarks>
    /// <param name="columns">列（先頭が主キー）</param>
    public List<DslQuery> BigintDecimalQueries(IReadOnlyList<DslColumn> columns) =>
        BigintDecimalAtoms(columns, seedSubset: true)
            .Select((atom, i) => AtomQuery($"Y{i}", atom, SpellingAt(i)))
            .ToList();

    /// <summary>網羅の単位の原子のクエリ <c>KY{i}</c>（6 つの演算子 × 小数・範囲の外・範囲の中の整数）</summary>
    /// <param name="columns">列（先頭が主キー）</param>
    public List<DslQuery> BigintDecimalCoverageQueries(IReadOnlyList<DslColumn> columns) =>
        BigintDecimalAtoms(columns, seedSubset: false)
            .Select((atom, i) => AtomQuery($"KY{i}", atom, SpellingAt(i)))
            .ToList();

    /// <summary>NULL を許す bigint の列ごとの、decimal の型トークンのパラメータとの比較（値オブジェクトの単位では作らない）</summary>
    private IEnumerable<DslAtom> BigintDecimalAtoms(
        IReadOnlyList<DslColumn> columns,
        bool seedSubset
    )
    {
        if (valueObjects)
        {
            yield break;
        }

        foreach (
            var column in FilterableColumns(columns)
                .Where(column => column.Clr == DslClr.Int64 && column.Nullable)
        )
        {
            if (seedSubset)
            {
                yield return OutOfTypeCompare(
                    column,
                    DslOp.Lt,
                    BeyondScaleFractions,
                    "decimal(18,6)"
                );
                yield return OutOfTypeCompare(
                    column,
                    DslOp.Ge,
                    BeyondScaleFractions,
                    "decimal(18,6)"
                );
                yield return OutOfTypeCompare(
                    column,
                    DslOp.Eq,
                    BeyondRangeFractions,
                    "decimal(38,6)"
                );
                yield return OutOfTypeCompare(column, DslOp.Ne, IntegralDecimals, "decimal(18,2)");

                continue;
            }

            foreach (var op in Enum.GetValues<DslOp>())
            {
                yield return OutOfTypeCompare(column, op, BeyondScaleFractions, "decimal(18,6)");
                yield return OutOfTypeCompare(column, op, BeyondRangeFractions, "decimal(38,6)");
                yield return OutOfTypeCompare(column, op, IntegralDecimals, "decimal(18,2)");
            }
        }
    }
}
