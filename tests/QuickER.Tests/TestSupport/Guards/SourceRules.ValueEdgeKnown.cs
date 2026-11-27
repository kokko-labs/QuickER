namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の表：値の端の値の写し（<c>value-edge-literal-copies</c>）の既知の一覧</summary>
/// <remarks>
/// <para>
/// 一覧に残すのは、移さない理由がはっきりしたもの（固定のシードの行・期待の計算・型の範囲の定義・表示の文言）だけで、理由の先頭は「（残す）」。
/// エンジン・表の入力の値は、値のプール（<c>TestSupport/ValuePools</c> の <c>DateTimeEdges</c>・<c>TimeSpanEdges</c>・<c>FloatingEdges</c>・
/// <c>PowerOfTwoEdges</c>・<c>TextEdges</c> と <c>ValueEdges</c>）から引く形に移してあり、一覧には載せない。
/// </para>
/// </remarks>
internal static partial class SourceRules
{
    private const string UnitTests = "tests/QuickER.Tests/";
    private const string IntegrationTests = "tests/QuickER.Tests.Integration/";

    /// <summary>残す印</summary>
    private const string Keep = "（残す）";

    private const string MessageText =
        Keep
        + "失敗の報告・表示の見出し・区切りの文言で、DB へ書く値でも比べる値でもない（短い日本語の文言が構文の上で同じ形になるだけ）";

    /// <summary>空振りの検知：値のプールの置き場（正本）に、この規則の見つける形が拾う形があること（件数まで厳密）</summary>
    private static IReadOnlyList<GuardKnown> ValueEdgeWitness() =>
        [
            Witnessed("ValueEdgeSamples.cs", ValueEdgeLiterals.OneTickEdge, 3),
            Witnessed("ValueEdgeSamples.cs", ValueEdgeLiterals.PowerOfTwoEdge, 4),
            Witnessed("ValueEdgeSamples.cs", ValueEdgeLiterals.NonAsciiText, 18),
            Witnessed("ValueEdgeSamples.cs", ValueEdgeLiterals.TimeSpanEdge, 2),
            Witnessed("ValueEdgeSamples.cs", ValueEdgeLiterals.DatetimeStep, 1),
            Witnessed("ValueEdgeSamples.cs", ValueEdgeLiterals.DateRangeEdge, 6),
            Witnessed("ValueEdgeSamples.cs", ValueEdgeLiterals.FloatingEdge, 2),
        ];

    private static GuardKnown Witnessed(string file, string what, int count) =>
        new(
            ValuePoolsFolder + "/" + file,
            what,
            count,
            "値のプールの正本そのもの（水準の値を持つ所）。この形が拾えなくなると、規則が空振りしている"
        );

    private static IReadOnlyList<GuardKnown> ValueEdgeKnown() =>
        [
            // ---- Integration：実装先どうしの一致 ----
            new(
                IntegrationTests + "GeneratedRuntime/BackendParitySqlServerValueExpectations.cs",
                ValueEdgeLiterals.TimeSpanEdge,
                1,
                Keep
                    + "time の列が持てない値の判定（0 以上 1 日未満）で、SQL Server の範囲そのものを使う期待の計算"
            ),
            // ---- Integration：同期のキーのページング ----
            new(
                IntegrationTests + "GeneratedRuntime/SyncKeyPagingRows.cs",
                ValueEdgeLiterals.OneTickEdge,
                1,
                Keep
                    + "同期のキーの値は「同期のキーの値」の区切り（SyncKeyValues・未起動）で棚卸しする。実 SQL Server へ書くハーネスの閉包の中の行で DB なしに観測できず（宣言の Unobserved）、プールから引く形はその区切りで決める"
            ),
            new(
                IntegrationTests + "GeneratedRuntime/SyncKeyPagingRows.cs",
                ValueEdgeLiterals.NonAsciiText,
                3,
                Keep
                    + "同期のキーの区切り（SyncKeyValues・未起動）で棚卸しする。行 306 の文字列のキーの順序の入力（é・日本・全角の Z）"
            ),
            new(
                IntegrationTests + "GeneratedRuntime/SyncKeyPagingRows.cs",
                ValueEdgeLiterals.DateRangeEdge,
                11,
                Keep
                    + "同期のキーの区切り（SyncKeyValues・未起動）で棚卸しする。datetime・datetime2・datetimeoffset のキー順ページングの境目（1・1752・1753・9999 年）"
            ),
            // ---- Integration：式の翻訳と値の束縛 ----
            new(
                IntegrationTests + "GeneratedRuntime/TranslatorParityBindingRun.cs",
                ValueEdgeLiterals.NonAsciiText,
                2,
                MessageText
            ),
            // ---- QuickER.Tests：型の表記の表 ----
            new(
                UnitTests + "Provider/DialectTypeRows.FullWidth.cs",
                ValueEdgeLiterals.NonAsciiText,
                4,
                Keep
                    + "型の表記の解析へ渡す全角数字の綴り（５０・１０・２・３）。表記の入力で、DB の列へ書く値ではない（文字列の水準の軸とは別）"
            ),
        ];
}
