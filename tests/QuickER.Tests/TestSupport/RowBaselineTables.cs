using QuickER.Tests.CodeGen.CSharp;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// QuickER.Tests の札の対象の表の一覧（減るだけの写し <c>row-baseline.json</c> と照合する。TestSupport/CLAUDE.md
/// 「分岐の軸の台帳と新しい行の札」）
/// </summary>
/// <remarks>
/// Integration の表（同期の期待の行・決定的な行・網羅の行の手で書いた行・述語の表・形の行）は Integration の一覧
/// （<c>tests/QuickER.Tests.Integration/RowBaselineTables.cs</c>）が持つ（このアセンブリから Integration の表は見えない）。
/// 表を足すときは、写しの JSON にその表の項目（今の Id の全部）を手で足す（書き直しは写しに無い表を書かない）。
/// </remarks>
internal static class RowBaselineTables
{
    /// <summary>写しのファイル（リポジトリの根からの相対パス）</summary>
    public const string BaselinePath = "tests/QuickER.Tests/TestSupport/row-baseline.json";

    /// <summary>札の対象の表（今の Id と札）</summary>
    public static IReadOnlyList<BaselineTable> All() =>
        [
            new(
                nameof(PostgreSqlQueryParameterInterceptorTests),
                PostgreSqlQueryParameterInterceptorTests.BaselineEntries
            ),
            new(
                nameof(SqlServerQueryParameterInterceptorTests),
                SqlServerQueryParameterInterceptorTests.BaselineEntries
            ),
        ];
}
