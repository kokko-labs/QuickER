using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>方言の型の表の実 DB の段（<see cref="DialectTypeRealDbTestsBase"/>）の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 1 ケース＝手順（<see cref="DialectTypeRealDbProbe"/>）1 つ。観測は手順の宣言（方言・種類）だけを読むので DB に触れない。
/// 種を持たないので、網羅しない組は毎回照合する（<see cref="DialectTypeRealDbTableTests"/>）。
/// </remarks>
internal static class DialectTypeRealDbDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "dialect-types-real-db";

    /// <summary>方言</summary>
    public const string Dialect = "Dialect";

    /// <summary>手順の種類</summary>
    public const string Probe = "Probe";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.Dialect,
            [
                new Factor(
                    Dialect,
                    DialectNames.All.Select(name => new Level(name)).ToList(),
                    "方言（派生クラスごとに 1 つ）"
                ),
                new Factor(
                    Probe,
                    [
                        new Level(
                            DialectTypeRealDbProbeKinds.AtLimit,
                            AxisMember.Property(
                                typeof(SyncDialectCapabilities),
                                nameof(SyncDialectCapabilities.NormalizeTypeNotation)
                            )
                        ),
                        new Level(DialectTypeRealDbProbeKinds.OverLimit),
                        new Level(DialectTypeRealDbProbeKinds.Driver),
                        new Level(DialectTypeRealDbProbeKinds.EfCore),
                        new Level(DialectTypeRealDbProbeKinds.EfCoreWriteRange),
                        new Level(DialectTypeRealDbProbeKinds.AdoWriteRange),
                    ],
                    "手順の種類（上限ちょうどは取り込み直した表記を同期の能力の型の表記の正規化と比べる。EF Core は生成 DbContext と同じ構成のモデルで読み書きする。書き込みの範囲は列の型の範囲の端と外の値を書き込みの経路ごとに書き、黙って別の値を保存しないことを確かめる。QuickER 版の束縛は同じ値を QuickER 版 Repository と同じ束縛で書く）"
                ),
            ],
            isValid: IsValid
        );

    /// <summary>
    /// 網羅しない組の一覧（方言 × 手順の種類の、起こり得る組を手順が持つ。方言が原理的に持てない組は <see cref="IsValid"/> が外し、
    /// 製品の事実で手順を作れない組だけをここに受け入れる）
    /// </summary>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.Dialect,
            [
                CoverageGap.AcceptedTuple(
                    Scope,
                    "DW",
                    "SQL Server の QuickER 版 Repository の書き込みの経路（InsertAsync・UpdateAsync・SaveAsync の挿入と更新・BulkInsertAsync）での"
                        + " decimal の丸めと整数部の桁あふれは、実装先どうしの一致の値の往復（GeneratedRuntime/ParityValuesSqlServer の列 Amount＝"
                        + "decimal(10,2)、値の形 Max・Min・RoundDown・RoundUp・Half・RoundsToOverflow・Overflow）が、生成した Repository を通して照合する。"
                        + "SqlParameter の明示型（[SqlColumnType] の精度・スケール）と、SqlBulkCopy へ渡す値の直しは生成コードの中にある。この段の手順は"
                        + "生成コードを通らないので、写しを置かずにそちらへ任せる",
                    (Dialect, DialectNames.SqlServer),
                    (Probe, DialectTypeRealDbProbeKinds.AdoWriteRange)
                ),
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.Dialect,
            "TypesRealDb",
            "方言の型の表の実 DB の段（上限の対の DDL の適用と取り込み直し・マッパーとドライバの CLR 型の一致・EF Core の読み書きと書き込みの範囲）",
            Table,
            0,
            [
                "tests/QuickER.Tests.Integration/Dialects/DialectTypeRealDbRows.cs",
                "tests/QuickER.Tests.Integration/Dialects/DialectTypeRealDbTests.cs",
                "tests/QuickER.Tests.Integration/Dialects/DialectTypeRealDbDimensions.cs",
                "tests/QuickER.Tests.Integration/Dialects/DialectTypeRealDbTableTests.cs",
                "tests/QuickER.Tests/Provider/DialectTypeRows.cs",
            ],
            [
                "src/QuickER.Provider.SqlServer",
                "src/QuickER.Provider.PostgreSql",
                "src/QuickER.Provider.MySql",
                "src/QuickER.Provider.Oracle",
                "src/QuickER.Provider.Sqlite",
            ]
        );

    /// <summary>手順を 1 つ数える</summary>
    /// <param name="counter">数える先</param>
    /// <param name="probe">手順</param>
    public static void Observe(CoverageCounter counter, DialectTypeRealDbProbe probe) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Dialect] = probe.Dialect,
                [Probe] = probe.Kind,
            }
        );

    /// <summary>
    /// 原理的に起こり得ない組を外す：SQLite は型の表記の長さ・精度に上限を持たず（宣言の型をそのまま覚える）、DB が断る
    /// 上限が無いので、上限の対の手順を持てない（マッパーの手順＝ドライバ・EF Core・書き込みの範囲・QuickER 版の束縛だけを持つ）。
    /// QuickER 版 Repository（ADO）は SQL Server と SQLite だけが持ち（PostgreSQL・MySQL・Oracle の列を読み書きするのは生成 EF Core の
    /// コードとそのプロバイダだけ＝ルートの CLAUDE.md の索引「型マッパーは読み取る経路が読み書きできる型へ写す」）、ほかの 3 方言には
    /// QuickER 版の書き込みの経路が無いので、QuickER 版の束縛の手順を持てない
    /// </summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        if (
            !assignment.TryGetValue(Dialect, out var dialect)
            || !assignment.TryGetValue(Probe, out var probe)
        )
        {
            return true;
        }

        if (dialect == DialectNames.Sqlite)
        {
            return probe
                is DialectTypeRealDbProbeKinds.Driver
                    or DialectTypeRealDbProbeKinds.EfCore
                    or DialectTypeRealDbProbeKinds.EfCoreWriteRange
                    or DialectTypeRealDbProbeKinds.AdoWriteRange;
        }

        return probe != DialectTypeRealDbProbeKinds.AdoWriteRange
            || dialect == DialectNames.SqlServer;
    }
}
