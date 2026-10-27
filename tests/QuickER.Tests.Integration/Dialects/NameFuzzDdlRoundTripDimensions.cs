using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 名前の総当たりの DDL の往復（<see cref="NameFuzzDdlRoundTripTestsBase"/>）の次元表・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// <para>
/// ケースは、実際に流した名前 1 つ × 置き場所 1 つ（その方言で設計上置けない名前と、ツールが図に入れなかった名前を飛ばした後）。
/// 長さの上限の後ろでだけ違う 2 つの名前の組（<see cref="NameFuzzDiagrams.LongPairs"/>）は置き場所を持たないので数えない。
/// 観測は流す直前で、名前と置き場所を読むだけ（乱数を引かない）。
/// </para>
/// <para>
/// 範囲は方言の名前。網羅しない組の照合は既定の種の数のときだけ行い、既知の失敗の照合の後に回す。
/// </para>
/// </remarks>
internal static class NameFuzzDdlRoundTripDimensions
{
    /// <summary>名前の置き場所</summary>
    public const string Role = "Role";

    /// <summary>名前の性質（<see cref="NameFuzzDiagrams.Classify"/>）</summary>
    public const string NameClass = "NameClass";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.Name,
            [
                new Factor(
                    Role,
                    Enum.GetValues<NameRole>()
                        .Select(value => new Level(value.ToString(), AxisMember.Of(value)))
                        .ToList(),
                    "名前の置き場所（テーブル名・列名・一意制約名・外部キー名）"
                ),
                new Factor(
                    NameClass,
                    [
                        new Level("Plain"),
                        new Level("Long"),
                        new Level("Whitespace"),
                        new Level("Dot"),
                        new Level("Quote"),
                        new Level("Surrogate"),
                        new Level("NonAscii"),
                        new Level("LeadingDigit"),
                        new Level("Reserved"),
                    ],
                    "名前の性質（NameFuzzDiagrams.Classify の優先順の最初の 1 つ・どれも無ければ Plain）"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧（範囲は方言の名前）</summary>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.Name,
            new[] { "sqlserver", "postgresql", "mysql", "oracle", "sqlite" }.SelectMany(dialect =>
                new[]
                {
                    CoverageGap.Matching(
                        dialect,
                        "P1-3",
                        "P2",
                        "既定の種で流す名前は代表の名前（NameFuzzDiagrams.Representative＝性質ごとに 1 つ）だけで、どの性質も持たない名前を含まない（ランダムな名前を足すのは既定を超えた種だけ）",
                        Role,
                        NameClass,
                        (_, nameClass) => nameClass == "Plain"
                    ),
                    CoverageGap.AcceptedTuple(
                        dialect,
                        "P1-3",
                        "図はテーブル名を最初のドットでスキーマとテーブルに分けて読み（SchemaQualifiedName）、DB の取込も名前にドットを含むテーブルを取り込まない（DottedTableNameExcluded）ので、ドットを含むテーブル名は製品の設計上 1 つの名前として往復しない（総当たりは IsOutOfDesign で流さない。エンジンを直しても作れない組）",
                        (Role, nameof(NameRole.Table)),
                        (NameClass, "Dot")
                    ),
                }
            )
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.Name,
            "DdlRoundTrip",
            "名前の総当たりの DDL の往復（5 方言の実 DB）",
            Table,
            0,
            [
                "tests/QuickER.Tests/TestSupport/NameFuzz.cs",
                "tests/QuickER.Tests/TestSupport/NameFuzzDiagrams.cs",
                "tests/QuickER.Tests/TestSupport/NameFuzzKnownFailures.cs",
                "tests/QuickER.Tests.Integration/Dialects/NameFuzzDdlRoundTripDimensions.cs",
                "tests/QuickER.Tests.Integration/Dialects/NameFuzzDdlRoundTripIntegrationTests.cs",
            ],
            [
                "src/QuickER.Dialects/IdentifierLengthLimit.cs",
                "src/QuickER.Provider/SqlNameText.cs",
                "src/QuickER.Provider/SchemaDiffService.cs",
                "src/QuickER.Provider/DdlGeneratorBase.cs",
                "src/QuickER.Provider.MySql",
                "src/QuickER.Provider.Oracle",
                "src/QuickER.Provider.PostgreSql",
                "src/QuickER.Provider.SqlServer",
                "src/QuickER.Provider.Sqlite",
            ]
        );

    /// <summary>流す名前と置き場所を 1 つ数える（読むだけ）</summary>
    /// <param name="counter">数える先</param>
    /// <param name="name">名前</param>
    /// <param name="role">置き場所</param>
    public static void Observe(CoverageCounter counter, string name, NameRole role) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Role] = role.ToString(),
                [NameClass] = NameFuzzDiagrams.Classify(name),
            }
        );
}
