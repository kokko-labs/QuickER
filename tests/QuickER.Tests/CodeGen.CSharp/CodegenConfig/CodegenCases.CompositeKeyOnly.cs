using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：Repository を作れる表（主キーが 1 列の表）が 1 つも無い図。固定の土台はオプションだけで決まり、
/// 表の主キーの形に依らずコンパイルできる
/// </summary>
/// <remarks>
/// <para>
/// 元のテストを持たない行：修正 H の受け入れ。Repository の共通契約だけが「Repository を作る表が 1 つ以上」のときに出て、
/// 図に依らず出る EF Core・インメモリ・クエリの土台がそれを参照してコンパイルできない（DI の登録のローカル関数が使われない警告も含む）。
/// </para>
/// <para>
/// 網羅の配列の図の形（<see cref="ShapeDiagram"/>）にも、Repository を作る表が 0 になる水準 <see cref="CodegenConfigDimensions.KeyLevels.CompositeOnly"/>・
/// <see cref="CodegenConfigDimensions.KeyLevels.KeylessOnly"/> がある。それでもこの行を残すのは 2 つの理由から：
/// 主キーの順序を逆にした EF Core のモデルの主キーの並びは配列に無い事実で、DI の登録のローカル関数のゲートを外したときに
/// 赤になる組はキー × DB アクセス × リモート・方言の 3 因子で、配列の 3 因子の集まりに入っていない（配列が拾うかは種に依る）。
/// </para>
/// <para>
/// 図は複合主キーの表 <c>pairs(a, b)</c> の 1 表だけ（主キーの順序を <c>b, a</c> に上書き）と、主キーの無い表 <c>logs</c> の 1 表だけ。
/// どの行もコンパイル（エラー 0・警告 0）を掛け、EF Core を出す行はモデルの主キーの並びも見る。
/// </para>
/// </remarks>
internal static partial class CodegenCases
{
    /// <summary>Repository を作れる表が無い図の行</summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> CompositeKeyOnlyRows() =>
        [
            CompositeKeyOnlyRow(
                "compile-composite-key-only-efcore",
                "主キーが複合の表だけの図 × EF Core 版だけ：コンパイルでき、HasKey は主キーの実効順",
                CompositeKeyOnlyDiagram,
                CompositeKeyOnlyOptions() with
                {
                    GenerateEfCoreRepositories = true,
                },
                efCoreModel: true
            ),
            CompositeKeyOnlyRow(
                "compile-composite-key-only-efcore-with-repositories",
                "主キーが複合の表だけの図 × EF Core 版＋QuickER 版：コンパイルでき、HasKey は主キーの実効順",
                CompositeKeyOnlyDiagram,
                CompositeKeyOnlyOptions() with
                {
                    GenerateEfCoreRepositories = true,
                    GenerateRepositories = true,
                },
                efCoreModel: true
            ),
            CompositeKeyOnlyRow(
                "compile-composite-key-only-all-surfaces",
                "主キーが複合の表だけの図 × QuickER 版＋EF Core 版＋インメモリ＋リモートサービス：コンパイルできる（DI の登録のローカル関数を出さない）",
                CompositeKeyOnlyDiagram,
                CompositeKeyOnlyAllSurfacesOptions()
            ),
            CompositeKeyOnlyRow(
                "compile-composite-key-only-multitarget-split",
                "主キーが複合の表だけの図 × QuickER 版 sqlserver+sqlite＋リモート契約＋分割：コンパイルできる（keyed の登録を 2 方言で通る）",
                CompositeKeyOnlyDiagram,
                CompositeKeyOnlyOptions() with
                {
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                    GenerateRemoteContracts = true,
                    SplitFilesByCategory = true,
                }
            ),
            CompositeKeyOnlyRow(
                "compile-composite-key-only-packages",
                "主キーが複合の表だけの図 × 全部＋リモートサービス＋パッケージ参照：コンパイルできる",
                CompositeKeyOnlyDiagram,
                CompositeKeyOnlyAllSurfacesOptions() with
                {
                    UseRuntimePackages = true,
                }
            ),
            CompositeKeyOnlyRow(
                "compile-keyless-only-all-surfaces",
                "主キーの無い表だけの図 × QuickER 版＋EF Core 版＋インメモリ＋リモートサービス：コンパイルできる",
                KeylessOnlyDiagram,
                CompositeKeyOnlyAllSurfacesOptions()
            ),
        ];

    /// <summary>行を組む（直接の入口・コンパイルする。EF Core を出す行はモデルの主キーの並びを見る）</summary>
    private static ScenarioRow<CodegenCase> CompositeKeyOnlyRow(
        string id,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        bool efCoreModel = false
    ) =>
        new(
            id,
            null,
            description,
            new CodegenCase(
                diagram,
                options,
                DialectNames.SqlServer,
                DiagnosticExpectation.Contains(),
                FileRules: [CompileGenerated],
                Compile: true,
                Probes: efCoreModel ? [CodegenProbe.EfCoreModel] : null,
                ProbeFacts: efCoreModel
                    ? new CodegenProbeFacts(
                        ModelFacts: [EfCoreModelFact.PrimaryKey("PairEntity", ["B", "A"])]
                    )
                    : null
            )
        );

    /// <summary>行の共通のオプション（DB アクセスは行ごとに足す）</summary>
    private static CodeGenerationOptions CompositeKeyOnlyOptions() =>
        new() { RootNamespace = "Sample.Domain" };

    /// <summary>DB アクセスの 3 つとリモートサービスを全部出すオプション</summary>
    private static CodeGenerationOptions CompositeKeyOnlyAllSurfacesOptions() =>
        CompositeKeyOnlyOptions() with
        {
            GenerateRepositories = true,
            GenerateEfCoreRepositories = true,
            GenerateInMemoryRepositories = true,
            GenerateRemoteServices = true,
        };

    /// <summary>複合主キーの表 <c>pairs(a, b)</c> だけの図（主キーの実効順は <c>b, a</c>＝列の宣言順の逆）</summary>
    private static ErDiagram CompositeKeyOnlyDiagram()
    {
        var a = CompositeKeyOnlyColumn("a", primaryKey: true);
        var b = CompositeKeyOnlyColumn("b", primaryKey: true);

        return new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "pairs",
                    Columns = [a, b, CompositeKeyOnlyColumn("note", primaryKey: false)],
                    PrimaryKeyColumnIds = [b.Id, a.Id],
                },
            ],
        };
    }

    /// <summary>主キーの無い表 <c>logs</c> だけの図</summary>
    private static ErDiagram KeylessOnlyDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = "logs",
                    Columns =
                    [
                        CompositeKeyOnlyColumn("logged_at", primaryKey: false),
                        CompositeKeyOnlyColumn("message", primaryKey: false),
                    ],
                },
            ],
        };

    /// <summary>NOT NULL の int の列</summary>
    private static Column CompositeKeyOnlyColumn(string name, bool primaryKey) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataType = "int",
            IsPrimaryKey = primaryKey,
            IsNullable = false,
        };
}
