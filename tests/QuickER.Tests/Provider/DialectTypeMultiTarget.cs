using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

namespace QuickER.Tests.Provider;

/// <summary>
/// マルチターゲット（QuickER 版 Repository の方言が 2 つ以上）の型の一致：図の型の名前は方言ごとの型マッパーの全部に通り、
/// C# の型（型名と参照型か）が食い違うと生成が <c>CodeGen_Error_TypeMismatch</c> で断られる
/// </summary>
/// <remarks>
/// <para>
/// 名前は手で書かず、宣言から集める：マルチターゲットに選べる方言（<see cref="CodeGenerationOptions.SupportedRepositoryDialects"/>）
/// ごとに、型カタログの選択肢（<see cref="ITypeCatalog.DataTypes"/>）と、方言の型の表のその方言の行のうち型マッパーの行の入力と
/// 解析が通る（期待 <c>ok</c>）解析の行の入力。型を足すと、選択肢か表の行のどちらかに現れて自動で対象に入る。
/// </para>
/// <para>
/// 判定は製品の経路をそのまま通す（写しを作らない）：図の方言ごとに、名前ごとに 1 表（キーとその名前の型の列）を並べた図を作り、CLI・GUI と同じ入口
/// <see cref="DiagramCodeGenerator.Generate(IColumnTypeMapper, ITypeCatalog, IReadOnlyDictionary{string, IColumnTypeMapper}, ErDiagram, CodeGenerationOptions)"/>
/// を 1 回流して、型の食い違いの診断（resx の書式で種類と名指す表・列を取り出す＝DiagnosticMatcher。診断はコードを持たない）がある名前を集める。行バージョンの統一
/// （<c>MultiDialectTypeReconciler.ReconcileRowVersionTypes</c>）や、両方が代替の string になる組は製品のとおりに通る。
/// </para>
/// </remarks>
internal static class DialectTypeMultiTarget
{
    /// <summary>
    /// 受け入れた食い違い（型の名前・図の方言の組と、事実の理由）。宣言にあるのに食い違わない組も断る（陳腐化）
    /// </summary>
    public static IReadOnlyList<(string Name, string DiagramDialect, string Reason)> Accepted =>
        [
            .. new[] { "real", "float(1)", "float(24)", "FLOAT ( 24 )" }.Select(name =>
                (name, DialectNames.SqlServer, SinglePrecisionReason)
            ),
            .. new[] { "real", "REAL", "REAL(10)" }.Select(name =>
                (name, DialectNames.Sqlite, SqliteRealReason)
            ),
            .. new[]
            {
                "BLOB",
                "blob",
                "blob(1000)",
                "BOOLEAN",
                "INTEGER",
                "DOUBLE",
                "DOUBLE PRECISION",
                "DOUBLE  PRECISION",
                "double precision",
                "FLOAT8",
            }.Select(name => (name, DialectNames.Sqlite, SqliteOnlySpellingReason)),
        ];

    /// <summary>SQL Server の図の単精度（real・float(1)〜float(24)）</summary>
    private const string SinglePrecisionReason =
        "SQL Server のマッパーは単精度の float、SQLite のマッパーは REAL アフィニティの倍精度の double へ写す"
        + "（src/QuickER.Provider.Sqlite/CLAUDE.md「型表記」）。SQLite のマッパーは SQL Server の real と SQLite の REAL を"
        + "見分けられず、float へ寄せると SQLite だけの図の倍精度の列を壊すので、生成を断るのが正しい"
        + "（docs：マルチターゲットでは float(53) を使う）";

    /// <summary>SQLite の図の REAL（SQL Server のマッパーは SQL Server の単精度の real と読む）</summary>
    private const string SqliteRealReason =
        "SQLite の REAL は倍精度の double、SQL Server のマッパーは SQL Server の単精度の real と読んで float へ写す"
        + "（SQL Server の図の単精度と同じ精度の食い違い。docs：マルチターゲットでは FLOAT を使う）";

    /// <summary>SQLite の伝統の綴り（SQL Server の型ではない）</summary>
    private const string SqliteOnlySpellingReason =
        "SQLite の綴りで SQL Server の型ではなく（SQL Server のマッパーは代替の string へ倒す）、SQL Server の DDL にも出せない。"
        + "SQL Server のマッパーに足さず、生成を断るのが正しい";

    /// <summary>食い違い 1 件（図の方言・型の名前・製品の診断の文）</summary>
    public sealed record Mismatch(string DiagramDialect, string Name, string Message);

    /// <summary>図の方言ごとに対象にした名前（自己検査用）と、食い違い</summary>
    public sealed record Result(
        IReadOnlyDictionary<string, IReadOnlyList<string>> NamesByDialect,
        IReadOnlyList<Mismatch> Mismatches,
        IReadOnlyList<string> OtherErrors
    );

    /// <summary>
    /// 製品の生成を図の方言ごとに 1 回流した結果（呼ぶたびに作る。診断の文はいまの UI カルチャの言語で出るので、結果を覚えると
    /// 違う言語で流した行が前の言語の結果を読む）
    /// </summary>
    public static Result Run() => Compute();

    /// <summary>その方言の図に書ける型の名前（重複なし・序数の並び）</summary>
    public static IReadOnlyList<string> NamesOf(string dialect)
    {
        var names = new SortedSet<string>(
            DialectTypeAxis.Get(dialect).Catalog.DataTypes,
            StringComparer.Ordinal
        );

        foreach (var row in DialectTypeRows.Table)
        {
            var input = row.Input;

            if (
                input.Input is not string text
                || !input.Expected.TryGetValue(dialect, out var expected)
            )
            {
                continue;
            }

            if (
                input.Facet == DialectTypeFacets.Map
                || (
                    input.Facet == DialectTypeFacets.Parse
                    && expected.StartsWith("ok", StringComparison.Ordinal)
                )
            )
            {
                names.Add(text);
            }
        }

        return [.. names];
    }

    private static Result Compute()
    {
        var dialects = CodeGenerationOptions.SupportedRepositoryDialects;
        var mappers = dialects.ToDictionary(
            dialect => dialect,
            dialect => DialectTypeAxis.Get(dialect).Provider.TypeMapper,
            StringComparer.OrdinalIgnoreCase
        );
        var namesByDialect = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var mismatches = new List<Mismatch>();
        var otherErrors = new List<string>();

        foreach (var dialect in dialects)
        {
            var provider = DialectTypeAxis.Get(dialect).Provider;
            var names = NamesOf(dialect);
            namesByDialect[dialect] = names;

            // 名前ごとに 1 表（表の中の列どうしの検査＝行バージョンの列は 1 表に 1 本、などに巻き込まれないため）
            var diagram = new ErDiagram();
            var tableNames = new Dictionary<string, string>(StringComparer.Ordinal);

            for (var i = 0; i < names.Count; i++)
            {
                var tableName = $"mt_{i:D4}";
                tableNames[tableName] = names[i];
                diagram.Entities.Add(
                    new Entity
                    {
                        TableName = tableName,
                        Columns =
                        {
                            new Column
                            {
                                Name = "id",
                                DataType = provider.TypeCatalog.DefaultDataType,
                                IsPrimaryKey = true,
                                IsNullable = false,
                            },
                            new Column
                            {
                                Name = "value",
                                DataType = names[i],
                                IsNullable = true,
                            },
                        },
                    }
                );
            }

            var result = DiagramCodeGenerator.Generate(
                provider.TypeMapper,
                provider.TypeCatalog,
                mappers,
                diagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "MultiTarget.Types",
                    GenerateRepositories = true,
                    RepositoryDialects = dialects,
                }
            );

            foreach (
                var diagnostic in result.Diagnostics.Where(diagnostic =>
                    diagnostic.Severity == GenerationDiagnosticSeverity.Error
                )
            )
            {
                // 診断はコードを持たないので、種類と名指す表・列を resx の書式で取り出す（文の断片では照合しない）。
                // 型の食い違いの書式の引数は {0} 表・{1} 列・{2} 方言・{3} C# 型・{4} 方言・{5} C# 型。文の中の並びは
                // UI カルチャで変わる（英語は列が先）ので、番号の順の Arguments で引く
                var identified = DiagnosticMatcher.Identify(diagnostic);

                if (
                    identified.Key == nameof(Strings.CodeGen_Error_TypeMismatch)
                    && identified.Arguments.Count == 6
                    && tableNames.TryGetValue(identified.Arguments[0], out var name)
                    && identified.Arguments[1] == "value"
                )
                {
                    mismatches.Add(new Mismatch(dialect, name, diagnostic.Message));
                }
                else
                {
                    otherErrors.Add($"{dialect} の図：{diagnostic.Message}");
                }
            }
        }

        return new Result(namesByDialect, mismatches, otherErrors);
    }
}
