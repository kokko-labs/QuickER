using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>値の往復（QuickER 版 SQL Server・EF Core 版 SQL Server）の実装先</summary>
public enum SqlServerValueBackend
{
    /// <summary>QuickER 版 Repository（SQL Server）</summary>
    QuickEr,

    /// <summary>EF Core 版 Repository（SQL Server）</summary>
    EfCore,
}

/// <summary>
/// 実装先どうしの一致の値の往復（SQL Server の系統）の次元表・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// ケースは書いた値 1 つ（実装先 × 書く経路 × 列 × 値の形）。決まった形の網羅は成り立つ組をすべて 1 回ずつ流し、乱数の種は 1 行に
/// 複数の列の値を混ぜた行と複数行の BulkInsert を流す。観測は書き込みの宣言を読むだけで、生成器の乱数を引かない。
/// </remarks>
internal static class BackendParitySqlServerValueDimensions
{
    /// <summary>因子：実装先</summary>
    public const string Backend = "Backend";

    /// <summary>因子：書く経路</summary>
    public const string Path = "Path";

    /// <summary>因子：列</summary>
    public const string Column = "Column";

    /// <summary>因子：値の形</summary>
    public const string Shape = "Shape";

    /// <summary>
    /// 網羅の範囲（決まった形の網羅は既定で全組を出し、乱数の種は夜間の既定の種の数でだけ照合する。普段の 8 種は断られる形を
    /// 2 回に 1 回だけ 1 つ混ぜるので、書く経路 × 断られる形の組が欠ける）
    /// </summary>
    public const string Scope = "BackendParity.sqlserver-values";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.GeneratedRuntime,
            [
                new Factor(
                    Backend,
                    [
                        .. Enum.GetValues<SqlServerValueBackend>()
                            .Select(value => new Level(value.ToString())),
                    ],
                    "書いて読み戻す実装先"
                ),
                new Factor(
                    Path,
                    [
                        .. Enum.GetValues<SqlServerValuePath>()
                            .Select(value => new Level(value.ToString())),
                    ],
                    "書く経路（InsertAsync・UpdateAsync・SaveAsync の挿入と更新・BulkInsertAsync）"
                ),
                new Factor(
                    Column,
                    [
                        .. Enum.GetValues<SqlServerValueColumn>()
                            .Select(value => new Level(value.ToString())),
                    ],
                    "書く列（SQL Server の型）"
                ),
                new Factor(
                    Shape,
                    [
                        .. Enum.GetValues<SqlServerValueColumn>()
                            .SelectMany(SqlServerValueShapes.For)
                            .Distinct(StringComparer.Ordinal)
                            .Select(shape => new Level(shape)),
                    ],
                    "値の形（小数秒の丸めと繰り上がり・範囲の端・スケールの外・桁あふれ・長さの超過・文字の種類）"
                ),
            ],
            [],
            IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.GeneratedRuntime,
            [
                .. new[] { SqlServerValuePath.Update, SqlServerValuePath.SaveUpdate }.SelectMany(
                    path =>
                        new[]
                        {
                            CoverageGap.AcceptedTuple(
                                Scope,
                                "VR",
                                ExcludedUpdateReason,
                                (Path, path.ToString()),
                                (Column, nameof(SqlServerValueColumn.Payload))
                            ),
                            CoverageGap.AcceptedTuple(
                                Scope,
                                "VR",
                                ExcludedUpdateReason,
                                (Path, path.ToString()),
                                (Shape, SqlServerValueShapes.Long)
                            ),
                        }
                ),
            ]
        );

    /// <summary>除外する無制限バイナリ列を UPDATE の値で書かない理由</summary>
    private const string ExcludedUpdateReason =
        "除外する無制限バイナリ列は UPDATE の値で書けない（QuickER 版は値を残したままの更新を InvalidOperationException で断り、Stream のアクセサで書く。src/QuickER.Runtime/CLAUDE.md「無制限バイナリ列の除外」）。長い blob はこの列だけの形";

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.GeneratedRuntime,
            "ParityValuesSqlServer",
            "実装先どうしの一致（SQL Server の系統の値の往復：小数秒の丸め・decimal の丸めと桁あふれ・文字列とバイナリの長さと文字・BulkInsert を含む書く経路）",
            Table,
            0,
            [
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerValueDimensions.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerValueGenerator.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerValueExpectations.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerValueRuntimeTests.cs",
            ],
            [
                "src/QuickER.Runtime",
                "src/QuickER.Runtime.SqlServer",
                "src/QuickER.Runtime.EntityFrameworkCore",
                "src/QuickER.Provider.SqlServer",
            ]
        );

    /// <summary>1 回の書き込みの値を、実装先ごとのケースとして数える</summary>
    public static void Observe(
        CoverageCounter counter,
        SqlServerValueBackend backend,
        SqlServerValueWrite write
    )
    {
        foreach (var cell in write.Rows.SelectMany(row => row.Cells))
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Backend] = backend.ToString(),
                    [Path] = write.Path.ToString(),
                    [Column] = cell.Column.ToString(),
                    [Shape] = cell.Shape,
                }
            );
        }
    }

    /// <summary>値の形はその列の型にしか無い（列の型に無い形は起こり得ない）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment) =>
        !assignment.TryGetValue(Column, out var column)
        || !assignment.TryGetValue(Shape, out var shape)
        || SqlServerValueShapes.For(Enum.Parse<SqlServerValueColumn>(column)).Contains(shape);
}
