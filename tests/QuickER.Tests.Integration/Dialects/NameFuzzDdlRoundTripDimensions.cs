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

    /// <summary>方言の識別子の長さの上限（<see cref="QuickER.Provider.SyncDialectCapabilities.IdentifierLengthLimit"/>）</summary>
    public const string IdentifierLimit = "IdentifierLengthLimit";

    /// <summary>上限を超える識別子を DB が黙って切り詰めるか（<see cref="QuickER.Provider.SyncDialectCapabilities.TruncatesLongIdentifiers"/>）</summary>
    public const string Truncates = "TruncatesLongIdentifiers";

    /// <summary>
    /// 方言ごとの上限と切り詰めの水準（書き下ろし。上限は「単位:最大:表のファイル名の上限:BMP の外の文字を表せるか」）。
    /// 能力は方言ごとに固定の値なので、派生ごとの固定の水準として数える（能力の値どうしの組は数えない）。プロバイダの宣言と
    /// 食い違うと、観測が宣言の誤りとして赤にする
    /// </summary>
    public static IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, string>
    > DeclaredLevels { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["sqlserver"] = Levels("Utf16CodeUnits:128:-:True", "False"),
            ["postgresql"] = Levels("Utf8Bytes:63:-:True", "True"),
            ["mysql"] = Levels("Characters:64:251:False", "False"),
            ["oracle"] = Levels("Utf8Bytes:128:-:True", "False"),
            ["sqlite"] = Levels("Unlimited:0:-:True", "False"),
        };

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
                new Factor(
                    IdentifierLimit,
                    DeclaredLevels
                        .Values.Select(levels => levels[IdentifierLimit])
                        .Distinct(StringComparer.Ordinal)
                        .Select(level => new Level(level))
                        .ToList(),
                    "方言の識別子の長さの上限（派生ごとに固定の水準）",
                    AxisMember.Property(
                        typeof(QuickER.Provider.SyncDialectCapabilities),
                        IdentifierLimit
                    )
                ),
                new Factor(
                    Truncates,
                    [new Level("True"), new Level("False")],
                    "上限を超える識別子を DB が黙って切り詰めるか（派生ごとに固定の水準）",
                    AxisMember.Property(typeof(QuickER.Provider.SyncDialectCapabilities), Truncates)
                ),
            ]
        );

    /// <summary>上限の水準の名前（単位:最大:表のファイル名の上限:BMP の外の文字を表せるか）</summary>
    /// <param name="limit">方言の上限</param>
    public static string LimitLevel(QuickER.Dialects.IdentifierLengthLimit limit) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{limit.Unit}:{limit.Max}:{(limit.TableFileNameMax is { } fileMax ? fileMax.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}:{limit.RepresentsSupplementaryCharacters}"
        );

    private static IReadOnlyDictionary<string, string> Levels(string limit, string truncates) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IdentifierLimit] = limit,
            [Truncates] = truncates,
        };

    /// <summary>網羅しない組の一覧（範囲は方言の名前）</summary>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.Name,
            new[] { "sqlserver", "postgresql", "mysql", "oracle", "sqlite" }.SelectMany(dialect =>
                (CoverageGapEntry[])
                    [
                        CoverageGap.Matching(
                            dialect,
                            "P1-3",
                            "P2",
                            "既定の種で流す名前は代表の名前（NameFuzzDiagrams.Representative＝性質ごとに 1 つ）だけで、どの性質も持たない名前を含まない（ランダムな名前を足すのは既定を超えた種だけ）",
                            Role,
                            NameClass,
                            (_, nameClass) => nameClass == "Plain"
                        ),
                        .. new[] { IdentifierLimit, Truncates }.Select(capability =>
                            CoverageGap.Matching(
                                dialect,
                                "P1-3",
                                "P2",
                                "既定の種で流す名前は代表の名前（NameFuzzDiagrams.Representative＝性質ごとに 1 つ）だけで、どの性質も持たない名前を含まない（Role × Plain と同じ欠けが、派生ごとに固定した能力の水準との組にも出る）",
                                capability,
                                NameClass,
                                (_, nameClass) => nameClass == "Plain"
                            )
                        ),
                        CoverageGap.AcceptedTuple(
                            dialect,
                            "P1-3",
                            "図はテーブル名を最初のドットでスキーマとテーブルに分けて読み（SchemaQualifiedName）、DB の取込も名前にドットを含むテーブルを取り込まない（DottedTableNameExcluded）ので、ドットを含むテーブル名は製品の設計上 1 つの名前として往復しない（総当たりは IsOutOfDesign で流さない。エンジンを直しても作れない組）",
                            (Role, nameof(NameRole.Table)),
                            (NameClass, "Dot")
                        ),
                    ]
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

    /// <summary>流す名前と置き場所を 1 つ数える（読むだけ。上限と切り詰めはプロバイダの宣言を読む）</summary>
    /// <param name="counter">数える先</param>
    /// <param name="name">名前</param>
    /// <param name="role">置き場所</param>
    /// <param name="capabilities">方言の能力</param>
    public static void Observe(
        CoverageCounter counter,
        string name,
        NameRole role,
        QuickER.Provider.SyncDialectCapabilities capabilities
    ) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Role] = role.ToString(),
                [NameClass] = NameFuzzDiagrams.Classify(name),
                [IdentifierLimit] = LimitLevel(capabilities.IdentifierLengthLimit),
                [Truncates] = capabilities.TruncatesLongIdentifiers.ToString(),
            }
        );
}
