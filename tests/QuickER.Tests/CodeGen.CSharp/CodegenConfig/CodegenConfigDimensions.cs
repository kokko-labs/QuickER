using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 生成の構成のエンジン（<see cref="EngineIds.CodegenConfig"/>）の次元表・網羅の配列・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// <para>
/// 因子は 3 つに分かれる。生成オプション（15 因子。<see cref="CodeGenerationOptions"/> の真偽 17 項目はどれかの因子か水準の
/// Source に載る）・図の形（7 因子。<see cref="ShapeDiagram"/> が組む）・入口（<see cref="CodegenEntrance"/>）。
/// </para>
/// <para>
/// 普段の行は <see cref="Table"/> から <see cref="CoveringArray.Generate"/> で作る網羅の配列（2 因子の組の全部と、3 つの集まりの
/// 3 因子の組の全部）。夜間の行は <see cref="NightlyTable"/>（オプションの 15 因子を 1 つの 3 因子の集まりにした表）から作る。
/// どちらも種は定数で、種の指紋（engine-fingerprints.json）には載せない（乱数で入力を作るのは配列の組み立てだけで、
/// 行そのものは <see cref="Rows"/> の並びとして決まる）。
/// </para>
/// </remarks>
internal static class CodegenConfigDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "codegen-config-matrix";

    /// <summary>普段の網羅の配列の種</summary>
    public const int MatrixSeed = 20261104;

    /// <summary>夜間の網羅の配列の種</summary>
    public const int NightlySeed = 20261105;

    /// <summary>真偽の因子の「有効」</summary>
    public const string On = "on";

    /// <summary>真偽の因子の「無効」</summary>
    public const string Off = "off";

    // ===== 生成オプションの因子 =====

    /// <summary>EditModel の生成</summary>
    public const string EditModels = "EditModels";

    /// <summary>Mapper の生成</summary>
    public const string Mappers = "Mappers";

    /// <summary>DB アクセス（なし／QuickER 版 Repository／EF Core／両方）</summary>
    public const string DbAccess = "DbAccess";

    /// <summary>QuickER 版 Repository の方言</summary>
    public const string Dialects = "Dialects";

    /// <summary>インメモリ Repository</summary>
    public const string InMemory = "InMemory";

    /// <summary>リモート（なし／契約／サービス）</summary>
    public const string Remote = "Remote";

    /// <summary>双方向同期</summary>
    public const string Sync = "Sync";

    /// <summary>データアノテーション</summary>
    public const string Annotations = "Annotations";

    /// <summary>親参照ナビゲーションの [JsonIgnore]</summary>
    public const string JsonIgnore = "JsonIgnore";

    /// <summary>値オブジェクト</summary>
    public const string ValueObjects = "ValueObjects";

    /// <summary>文字列の主キーの GuidKey</summary>
    public const string GuidKey = "GuidKey";

    /// <summary>出力の形（1 ファイル／分割／層別）</summary>
    public const string Output = "Output";

    /// <summary>ランタイムのパッケージ参照</summary>
    public const string Packages = "Packages";

    /// <summary>API リファレンス（なし／英語／日本語／両方）</summary>
    public const string ApiDocs = "ApiDocs";

    /// <summary>無制限バイナリ列の除外</summary>
    public const string ExcludeBinary = "ExcludeBinary";

    // ===== 図の形の因子 =====

    /// <summary>主の表（customers）のキーの形</summary>
    public const string Key = "Key";

    /// <summary>関係の形</summary>
    public const string Relation = "Relation";

    /// <summary>DB が値を作る列</summary>
    public const string DbGenerated = "DbGenerated";

    /// <summary>バイナリ列</summary>
    public const string Binary = "Binary";

    /// <summary>一意制約</summary>
    public const string Unique = "Unique";

    /// <summary>名前付きクエリ</summary>
    public const string Query = "Query";

    /// <summary>図の方言（型解決のプロバイダ）</summary>
    public const string TypeDialect = "TypeDialect";

    // ===== 入口 =====

    /// <summary>入口</summary>
    public const string Entrance = "Entrance";

    // ===== 水準 =====

    /// <summary>DB アクセスの水準</summary>
    public static class DbAccessLevels
    {
        /// <summary>DB アクセスなし</summary>
        public const string None = "none";

        /// <summary>QuickER 版 Repository</summary>
        public const string Quicker = "quicker";

        /// <summary>EF Core</summary>
        public const string EfCore = "efcore";

        /// <summary>QuickER 版 Repository と EF Core の両方（パリティ用・CLI／オプション直指定でのみ可能）</summary>
        public const string Both = "both";
    }

    /// <summary>方言の水準（QuickER 版 Repository が対応する 2 方言の組）</summary>
    public static class DialectLevels
    {
        /// <summary>SQL Server だけ</summary>
        public const string SqlServer = DialectNames.SqlServer;

        /// <summary>SQLite だけ</summary>
        public const string Sqlite = DialectNames.Sqlite;

        /// <summary>SQL Server と SQLite（マルチターゲット）</summary>
        public const string Both = DialectNames.SqlServer + "+" + DialectNames.Sqlite;
    }

    /// <summary>リモートの水準</summary>
    public static class RemoteLevels
    {
        /// <summary>なし</summary>
        public const string None = "none";

        /// <summary>リモート面の契約だけ</summary>
        public const string Contracts = "contracts";

        /// <summary>HTTP のクライアント／サーバー（契約を含意する）</summary>
        public const string Services = "services";
    }

    /// <summary>出力の形の水準</summary>
    public static class OutputLevels
    {
        /// <summary>1 ファイル</summary>
        public const string Single = "single";

        /// <summary>分類ごとの分割</summary>
        public const string Split = "split";

        /// <summary>層別フォルダ（分割を含意する）</summary>
        public const string Layered = "layered";
    }

    /// <summary>API リファレンスの水準</summary>
    public static class ApiDocsLevels
    {
        /// <summary>出さない</summary>
        public const string Off = "off";

        /// <summary>英語</summary>
        public const string English = "english";

        /// <summary>日本語</summary>
        public const string Japanese = "japanese";

        /// <summary>英語と日本語</summary>
        public const string Both = "both";
    }

    /// <summary>キーの形の水準</summary>
    public static class KeyLevels
    {
        /// <summary>整数の単一主キー</summary>
        public const string Int = "int";

        /// <summary>整数 2 列の複合主キー</summary>
        public const string Composite = "composite";

        /// <summary>可変長文字列の単一主キー</summary>
        public const string String = "string";
    }

    /// <summary>関係の形の水準</summary>
    public static class RelationLevels
    {
        /// <summary>親子 1 対多だけ</summary>
        public const string OneToMany = "one-to-many";

        /// <summary>主の表の自己参照を足す</summary>
        public const string Self = "self";

        /// <summary>主の表との 1 対 1 の表を足す</summary>
        public const string OneToOne = "one-to-one";

        /// <summary>子の表の子（3 表の連鎖）を足す</summary>
        public const string Chain = "chain";
    }

    /// <summary>DB が値を作る列の水準</summary>
    public static class DbGeneratedLevels
    {
        /// <summary>なし</summary>
        public const string None = "none";

        /// <summary>行バージョン（SQL Server 以外の方言では日時の列になる）</summary>
        public const string RowVersion = "rowversion";

        /// <summary>計算列</summary>
        public const string Computed = "computed";

        /// <summary>主キーでない採番列</summary>
        public const string Identity = "identity";
    }

    /// <summary>バイナリ列の水準</summary>
    public static class BinaryLevels
    {
        /// <summary>なし</summary>
        public const string None = "none";

        /// <summary>長さのあるバイナリ（方言が持たなければ無制限になる）</summary>
        public const string Bounded = "bounded";

        /// <summary>無制限バイナリ</summary>
        public const string Unbounded = "unbounded";
    }

    /// <summary>一意制約の水準</summary>
    public static class UniqueLevels
    {
        /// <summary>なし</summary>
        public const string None = "none";

        /// <summary>1 列の一意制約</summary>
        public const string Single = "single";

        /// <summary>NULL 許容の列を含む 2 列の一意制約</summary>
        public const string CompositeNullable = "composite-nullable";
    }

    /// <summary>名前付きクエリの水準</summary>
    public static class QueryLevels
    {
        /// <summary>なし</summary>
        public const string None = "none";

        /// <summary>簡易 DSL</summary>
        public const string Dsl = "dsl";

        /// <summary>生 SQL（SQL Server と SQLite の SQL を持つ）</summary>
        public const string RawSql = "rawsql";

        /// <summary>手動実装</summary>
        public const string Manual = "manual";
    }

    /// <summary>普段の 3 因子の集まり（Errors と Success で共有）</summary>
    private static readonly IReadOnlyList<FactorGroup> RegularGroups =
    [
        new FactorGroup(Output, Packages, DbAccess),
        new FactorGroup(DbAccess, Dialects, Sync),
        new FactorGroup(Remote, DbAccess, Output),
    ];

    /// <summary>夜間の 3 因子の集まり（オプションの 15 因子を 1 つの集まりにする）</summary>
    private static readonly IReadOnlyList<FactorGroup> NightlyGroups =
    [
        new FactorGroup(
            EditModels,
            Mappers,
            DbAccess,
            Dialects,
            InMemory,
            Remote,
            Sync,
            Annotations,
            JsonIgnore,
            ValueObjects,
            GuidKey,
            Output,
            Packages,
            ApiDocs,
            ExcludeBinary
        ),
    ];

    /// <summary>Errors の次元表（全組の網羅。生成時エラーになる組も流し、診断の予測を照合する）</summary>
    public static DimensionTable Table { get; } =
        new(EngineIds.CodegenConfig, Factors(), RegularGroups, IsValid);

    /// <summary>
    /// Success の次元表（因子・水準・3 因子の集まりは <see cref="Table"/> と同じ。生成が通る構成の領域だけを網羅する）
    /// </summary>
    /// <remarks>
    /// <c>isValid</c> へは「原理的に起こり得ない組」の規則（<see cref="IsValid"/>）と、表の領域を決める述語
    /// （<see cref="CodegenSuccessDomain.Contains"/>）の論理積を渡す。領域の述語は isValid の原則とは別の仕組みで、
    /// 領域の外の組（生成時エラーになる組）は <see cref="Table"/> が網羅する。
    /// </remarks>
    public static DimensionTable SuccessTable { get; } =
        new(EngineIds.CodegenConfig, Factors(), RegularGroups, IsValidInSuccessDomain);

    /// <summary>夜間の Errors の次元表</summary>
    public static DimensionTable NightlyTable { get; } =
        new(EngineIds.CodegenConfig, Factors(), NightlyGroups, IsValid);

    /// <summary>夜間の Success の次元表（領域の述語は <see cref="SuccessTable"/> と同じ）</summary>
    public static DimensionTable NightlySuccessTable { get; } =
        new(EngineIds.CodegenConfig, Factors(), NightlyGroups, IsValidInSuccessDomain);

    /// <summary>普段の Errors の行（網羅の配列の並び）</summary>
    public static IReadOnlyList<CodegenRow> Rows => LazyRows.Value;

    /// <summary>普段の Success の行</summary>
    public static IReadOnlyList<CodegenRow> SuccessRows => LazySuccessRows.Value;

    /// <summary>夜間の Errors の行</summary>
    public static IReadOnlyList<CodegenRow> NightlyRows => LazyNightlyRows.Value;

    /// <summary>夜間の Success の行</summary>
    public static IReadOnlyList<CodegenRow> NightlySuccessRows => LazyNightlySuccessRows.Value;

    private static readonly Lazy<IReadOnlyList<CodegenRow>> LazyRows = new(() =>
        ToRows("m", CoveringArray.Generate(Table, MatrixSeed))
    );

    private static readonly Lazy<IReadOnlyList<CodegenRow>> LazySuccessRows = new(() =>
        ToRows("s", CoveringArray.Generate(SuccessTable, MatrixSeed))
    );

    private static readonly Lazy<IReadOnlyList<CodegenRow>> LazyNightlyRows = new(() =>
        ToRows("n", CoveringArray.Generate(NightlyTable, NightlySeed))
    );

    private static readonly Lazy<IReadOnlyList<CodegenRow>> LazyNightlySuccessRows = new(() =>
        ToRows("t", CoveringArray.Generate(NightlySuccessTable, NightlySeed))
    );

    /// <summary>Errors の網羅しない組の一覧（網羅の配列は網羅すべき組をすべて含むので空）</summary>
    public static CoverageGapSet Gaps { get; } = CoverageGap.Set(EngineIds.CodegenConfig, []);

    /// <summary>Success の網羅しない組の一覧（領域の中の網羅すべき組をすべて含むので空）</summary>
    public static CoverageGapSet SuccessGaps { get; } =
        CoverageGap.Set(EngineIds.CodegenConfig, []);

    /// <summary>Success の網羅しない組の照合の範囲</summary>
    public const string SuccessScope = "codegen-config-success";

    /// <summary>決まった行の表の次元（段 × 入口。網羅の計数には使わない）</summary>
    public static DimensionTable CasesTable { get; } =
        new(
            EngineIds.CodegenConfig,
            [
                new Factor(
                    "Stage",
                    [
                        new Level("diagnostics"),
                        new Level("files"),
                        new Level("compile"),
                        new Level("probe"),
                        new Level("entrance"),
                    ],
                    "決まった行が守っている段"
                ),
                new Factor(
                    Entrance,
                    CodegenEntrances.Names.Select(name => new Level(name)).ToList(),
                    "入口"
                ),
            ]
        );

    /// <summary>エンジンのテストのソース（台帳の行が共有する）</summary>
    private static readonly IReadOnlyList<string> SourceFiles =
    [
        "tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenConfigDimensions.cs",
        "tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenRow.cs",
        "tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenSuccessDomain.cs",
        "tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/ShapeDiagram.cs",
        "tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenEntrances.cs",
        "tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenRunner.cs",
        "tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenCases.cs",
        "tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenMatrixTests.cs",
        "tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenCaseTests.cs",
        "tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/Judges",
    ];

    /// <summary>エンジンが守る src の範囲（実際に通る入口を含む）</summary>
    private static readonly IReadOnlyList<string> Covers =
    [
        "src/QuickER.CodeGen.CSharp",
        "src/QuickER.CodeGen.UI",
        "src/QuickER.Provider/DiagramCodeGenerator.cs",
        "src/QuickER.Cli/GenerationExecutor.cs",
        "src/QuickER.Cli/GenerationConfigLoader.cs",
        "src/QuickER.Cli/CodeGenToolSet.cs",
    ];

    /// <summary>台帳の行（Errors：全組の網羅の配列）</summary>
    public static EngineDescriptor ErrorsDescriptor { get; } =
        new(
            EngineIds.CodegenConfig,
            "Errors",
            "生成の構成の網羅の配列（全組。生成時エラーの組を含め、診断の予測を照合する）",
            Table,
            0,
            SourceFiles,
            Covers
        );

    /// <summary>台帳の行（Success：生成が通る構成の領域の網羅の配列）</summary>
    public static EngineDescriptor SuccessDescriptor { get; } =
        new(
            EngineIds.CodegenConfig,
            "Success",
            "生成の構成の網羅の配列（生成が通る構成。コンパイル・意味・探針・関係を数える）",
            SuccessTable,
            0,
            SourceFiles,
            Covers
        );

    /// <summary>台帳の行（決まった行の表）</summary>
    public static EngineDescriptor CasesDescriptor { get; } =
        new(
            EngineIds.CodegenConfig,
            "Cases",
            "生成の構成の決まった行（元のテストの図・オプション・期待）",
            CasesTable,
            CodegenCases.Table.Count,
            SourceFiles,
            Covers
        );

    /// <summary>行 1 つを数える（行の割り当てをそのまま渡す）</summary>
    /// <param name="counter">数える先</param>
    /// <param name="row">行</param>
    public static void Observe(CoverageCounter counter, CodegenRow row) =>
        counter.Observe(row.Levels);

    private static List<Factor> Factors() =>
        [
            OnOff(
                EditModels,
                "EditModel を生成するか",
                nameof(CodeGenerationOptions.GenerateEditModels)
            ),
            OnOff(Mappers, "Mapper を生成するか", nameof(CodeGenerationOptions.GenerateMappers)),
            new Factor(
                DbAccess,
                [
                    new Level(DbAccessLevels.None),
                    new Level(
                        DbAccessLevels.Quicker,
                        OptionMember(nameof(CodeGenerationOptions.GenerateRepositories))
                    ),
                    new Level(
                        DbAccessLevels.EfCore,
                        OptionMember(nameof(CodeGenerationOptions.GenerateEfCoreRepositories))
                    ),
                    new Level(DbAccessLevels.Both),
                ],
                "DB アクセス（GUI ではラジオの排他。両方は CLI／オプション直指定だけ）"
            ),
            new Factor(
                Dialects,
                [
                    new Level(DialectLevels.SqlServer),
                    new Level(DialectLevels.Sqlite),
                    new Level(DialectLevels.Both),
                ],
                "QuickER 版 Repository の方言（RepositoryDialects・真偽でない）"
            ),
            OnOff(
                InMemory,
                "インメモリ Repository を生成するか",
                nameof(CodeGenerationOptions.GenerateInMemoryRepositories)
            ),
            new Factor(
                Remote,
                [
                    new Level(RemoteLevels.None),
                    new Level(
                        RemoteLevels.Contracts,
                        OptionMember(nameof(CodeGenerationOptions.GenerateRemoteContracts))
                    ),
                    new Level(
                        RemoteLevels.Services,
                        OptionMember(nameof(CodeGenerationOptions.GenerateRemoteServices))
                    ),
                ],
                "リモート（サービスは契約を含意する）"
            ),
            OnOff(
                Sync,
                "双方向同期を生成するか",
                nameof(CodeGenerationOptions.GenerateSyncSupport)
            ),
            OnOff(
                Annotations,
                "データアノテーションを付けるか（GUI では画面に出ない）",
                nameof(CodeGenerationOptions.IncludeDataAnnotations)
            ),
            OnOff(
                JsonIgnore,
                "親参照ナビゲーションへ [JsonIgnore] を付けるか（GUI では画面に出ない）",
                nameof(CodeGenerationOptions.IncludeJsonIgnoreOnParentNavigation)
            ),
            OnOff(
                ValueObjects,
                "値オブジェクトを生成するか",
                nameof(CodeGenerationOptions.GenerateValueObjects)
            ),
            OnOff(
                GuidKey,
                "文字列の主キーを GuidKey にするか",
                nameof(CodeGenerationOptions.UseGuidKeyForStringPrimaryKey)
            ),
            new Factor(
                Output,
                [
                    new Level(OutputLevels.Single),
                    new Level(
                        OutputLevels.Split,
                        OptionMember(nameof(CodeGenerationOptions.SplitFilesByCategory))
                    ),
                    new Level(
                        OutputLevels.Layered,
                        OptionMember(nameof(CodeGenerationOptions.LayeredOutput))
                    ),
                ],
                "出力の形"
            ),
            OnOff(
                Packages,
                "ランタイムをパッケージ参照にするか",
                nameof(CodeGenerationOptions.UseRuntimePackages)
            ),
            new Factor(
                ApiDocs,
                [
                    new Level(ApiDocsLevels.Off),
                    new Level(
                        ApiDocsLevels.English,
                        OptionMember(nameof(CodeGenerationOptions.GenerateApiDocs))
                    ),
                    new Level(ApiDocsLevels.Japanese),
                    new Level(ApiDocsLevels.Both),
                ],
                "API リファレンス（ApiDocsLanguage を水準で切り替える）"
            ),
            OnOff(
                ExcludeBinary,
                "無制限バイナリ列を除外するか",
                nameof(CodeGenerationOptions.ExcludeUnboundedBinaryColumns)
            ),
            new Factor(
                Key,
                [
                    new Level(KeyLevels.Int),
                    new Level(KeyLevels.Composite),
                    new Level(KeyLevels.String),
                ],
                "主の表のキーの形"
            ),
            new Factor(
                Relation,
                [
                    new Level(RelationLevels.OneToMany),
                    new Level(RelationLevels.Self),
                    new Level(RelationLevels.OneToOne),
                    new Level(RelationLevels.Chain),
                ],
                "関係の形"
            ),
            new Factor(
                DbGenerated,
                [
                    new Level(DbGeneratedLevels.None),
                    new Level(DbGeneratedLevels.RowVersion),
                    new Level(DbGeneratedLevels.Computed),
                    new Level(DbGeneratedLevels.Identity),
                ],
                "DB が値を作る列"
            ),
            new Factor(
                Binary,
                [
                    new Level(BinaryLevels.None),
                    new Level(BinaryLevels.Bounded),
                    new Level(BinaryLevels.Unbounded),
                ],
                "バイナリ列"
            ),
            new Factor(
                Unique,
                [
                    new Level(UniqueLevels.None),
                    new Level(UniqueLevels.Single),
                    new Level(UniqueLevels.CompositeNullable),
                ],
                "一意制約"
            ),
            new Factor(
                Query,
                [
                    new Level(QueryLevels.None),
                    new Level(QueryLevels.Dsl),
                    new Level(QueryLevels.RawSql),
                    new Level(QueryLevels.Manual),
                ],
                "名前付きクエリ"
            ),
            new Factor(
                TypeDialect,
                ShapeDiagram.TypeDialects.Select(name => new Level(name)).ToList(),
                "図の方言（型解決のプロバイダ）"
            ),
            new Factor(
                Entrance,
                CodegenEntrances.Names.Select(name => new Level(name)).ToList(),
                "入口（正本は CodegenEntrance）"
            ),
        ];

    private static Factor OnOff(string name, string description, string property) =>
        new(name, [new Level(On), new Level(Off)], description, OptionMember(property));

    private static AxisMember OptionMember(string property) =>
        AxisMember.Property(typeof(CodeGenerationOptions), property);

    /// <summary>Success の表の isValid（原理的に起こり得ない組の規則と、領域の述語の論理積）</summary>
    private static bool IsValidInSuccessDomain(IReadOnlyDictionary<string, string> assignment) =>
        IsValid(assignment) && CodegenSuccessDomain.Contains(assignment);

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    /// <remarks>
    /// 生成時エラーになる組（同期で方言が 2 つでない・契約の無いリモート・EF Core とマルチターゲット・EditModel 無しの Mapper など）は
    /// ここに書かない。Errors の表ではその行は診断の期待（<see cref="Judges.DiagnosticRules"/>）が「このエラーになる」を表明し、
    /// Success の表では領域の述語（<see cref="CodegenSuccessDomain"/>）が外す。領域の述語が完全な行を組むときにも使うので internal。
    /// </remarks>
    internal static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        // GUI の DB アクセスはラジオの排他（なし／QuickER 版 Repository／EF Core）で、両方を同時に選ぶ入力が画面に無い
        // （src/QuickER.CodeGen.CSharp/CLAUDE.md「DB アクセスの排他と単独出力」：両方 ON は CLI／オプション直指定でのみ可能）
        if (
            assignment.TryGetValue(Entrance, out var entrance)
            && entrance == CodegenEntrances.NameOf(CodegenEntrance.Gui)
            && assignment.TryGetValue(DbAccess, out var dbAccess)
            && dbAccess == DbAccessLevels.Both
        )
        {
            return false;
        }

        return true;
    }

    private static List<CodegenRow> ToRows(
        string prefix,
        IReadOnlyList<IReadOnlyDictionary<string, string>> assignments
    ) =>
        assignments
            .Select(
                (assignment, index) =>
                    new CodegenRow(
                        string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"{prefix}{index:D3}"
                        ),
                        assignment
                    )
            )
            .ToList();
}
