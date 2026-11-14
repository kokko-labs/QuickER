using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>
/// 方言の型の表（5 方言を行列の軸に、型の解析・書き出しの境界・正規化・マッパー・方言切替・型トークン・型の表記の安全性を
/// 入力と期待の行で固定する）
/// </summary>
/// <remarks>
/// <para>
/// 主題ごとの行は <c>DialectTypeRows.{主題}.cs</c> の private メソッドが返し、<see cref="AllRows"/> に足す。
/// 行の書き方は <see cref="DialectTypeRow"/>、期待の写しの書き方は <see cref="DialectTypeRunner"/> を参照する。
/// </para>
/// <para>
/// 主題のファイルの静的なデータは式形式のプロパティ（<c>=&gt;</c>）にする（partial の初期化順で null になるため）。
/// </para>
/// </remarks>
internal static partial class DialectTypeRows
{
    /// <summary>表</summary>
    public static ScenarioTable<DialectTypeCase> Table { get; } =
        new(EngineIds.Dialect, AllRows().Select(row => row.ToScenario()));

    /// <summary>(行の Id, 方言) の組（Theory のデータ。方言なしの観点は方言 <see cref="DialectTypeAxis.None"/>）</summary>
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();

        foreach (var row in Table)
        {
            foreach (var dialect in row.Input.Expected.Keys.Order(StringComparer.Ordinal))
            {
                data.Add(row.Id, dialect);
            }
        }

        return data;
    }

    /// <summary>行（既存の行から導く行は、元の行をすべて作った後に足す）</summary>
    private static IEnumerable<DialectTypeRow> AllRows()
    {
        List<DialectTypeRow> bases =
        [
            .. MatrixRows(),
            .. MatrixTextRows(),
            .. MatrixCanonicalRows(),
            .. SqlServerRows(),
            .. PostgreSqlRows(),
            .. MySqlRows(),
            .. OracleRows(),
            .. SqliteRows(),
            .. TokenRows(),
            .. ConvertRows(),
            .. SafeTextRows(),
            .. ParamTypeRows(),
            .. FullWidthRows(),
            .. LengthlessRows(),
            .. HighPrecisionDecimalRows(),
        ];

        return [.. bases, .. MatrixDerivedRows(bases)];
    }

    /// <summary>
    /// 有効桁が 15 を超える decimal の列（精度 28・スケール 10）は精度とスケールを保つ decimal へ写す（5 方言。宣言のセルの
    /// decimal(12,3) は精度が 15 以下なので、方言の型の表の実 DB の段の書き込みの範囲の手順が、double の有効桁を超える範囲の中の値を
    /// 書くのにこの行を使う）
    /// </summary>
    private static IEnumerable<DialectTypeRow> HighPrecisionDecimalRows() =>
        new (string Dialect, string Slug, string Native)[]
        {
            (DialectNames.SqlServer, "decimal-28-10", "decimal(28,10)"),
            (DialectNames.PostgreSql, "numeric-28-10", "numeric(28,10)"),
            (DialectNames.MySql, "decimal-28-10", "decimal(28,10)"),
            (DialectNames.Oracle, "number-28-10", "NUMBER(28,10)"),
            (DialectNames.Sqlite, "decimal-28-10", "DECIMAL(28,10)"),
        }.Select(item =>
            DialectTypeRow
                .Map(
                    $"map-{item.Dialect}-{item.Slug}",
                    "有効桁が 15 を超える decimal の列は精度とスケールを保つ decimal へマップされる",
                    item.Native
                )
                .Expect(item.Dialect, "ok type=decimal fallback=0 p=28 s=10 efcol=-")
        );
}
