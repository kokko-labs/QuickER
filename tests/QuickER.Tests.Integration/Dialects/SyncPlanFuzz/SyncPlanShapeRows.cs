using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の形の行（D4）：主キーの変更・参照する外部キーの数・型の変更・明示の削除・一意制約の数の 5 因子の網羅の配列から、
/// 方言ごとに決定的な行を作る（網は全行、実 DB の総当たりは普段は 2 因子の網羅に要る行だけ・夜間は全行）
/// </summary>
/// <remarks>
/// <para>
/// 行の図は親の表 <c>p</c>（複合主キー <c>(id, k)</c>・整数の列 <c>u</c> と <c>w</c>）と、親の主キーを参照する子の表
/// <c>c1</c>・<c>c2</c>（参照する外部キーの数の水準だけ）。<c>p.u</c> に名前付きの一意制約を一意制約の数の水準だけ張る。
/// 差分は全部選ぶ。網羅の配列は 2 因子の組の全部と、P1（主キーの並べ替え × 参照 2 本 × 子だけの型の変更）・Q6（一意制約 2 つ ×
/// 一意制約の削除 × 一意制約の列の型の変更）を含む 3 因子の集まりの組を含む（<see cref="MissingTriples"/> が表明する）。
/// </para>
/// <para>
/// Oracle は同じ列の並びに一意制約を 2 つ張れない（ORA-02261・DB の事実）ので、一意制約 2 つの行を作らない。
/// </para>
/// </remarks>
internal static class SyncPlanShapeRows
{
    /// <summary>主キーの変更</summary>
    public const string PkChange = "PkChange";

    /// <summary>親の主キーを参照する外部キーの数</summary>
    public const string ReferencingFks = "ReferencingFks";

    /// <summary>型の変更（子の列だけ・親の列だけ・親と最初の子だけ・親と全部の子・一意制約の列）</summary>
    public const string TypeChange = "TypeChange";

    /// <summary>明示の削除（外部キー・一意制約）</summary>
    public const string ExplicitOp = "ExplicitOp";

    /// <summary>親の列 u に張る一意制約の数</summary>
    public const string UniqueMultiplicity = "UniqueMultiplicity";

    /// <summary>行の Id の頭</summary>
    public const string IdPrefix = "d4-";

    /// <summary>網羅の配列の種（決定的）</summary>
    private const int ArraySeed = 4;

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.SyncPlan,
            [
                Levels(
                    PkChange,
                    "主キーの変更",
                    "None",
                    "Reorder",
                    "Extend",
                    "Shrink",
                    "Move",
                    "Remove"
                ),
                Levels(ReferencingFks, "親の主キーを参照する外部キーの数", "0", "1", "2"),
                Levels(
                    TypeChange,
                    "型の変更（子の列だけ・親の列だけ・親と最初の子だけ・親と全部の子・一意制約の列）",
                    "None",
                    "ChildOnly",
                    "ParentOnly",
                    "PartialBothEnds",
                    "BothEnds",
                    "UniqueColumn"
                ),
                Levels(ExplicitOp, "明示の削除", "None", "DropForeignKey", "DropUnique"),
                Levels(UniqueMultiplicity, "親の列 u に張る一意制約の数", "0", "1", "2"),
            ],
            [
                new FactorGroup(PkChange, ReferencingFks, TypeChange),
                new FactorGroup(UniqueMultiplicity, ExplicitOp, TypeChange),
            ],
            IsValid
        );

    /// <summary>網羅の配列（全行）</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> AllRows { get; } =
        CoveringArray.Generate(Table, ArraySeed);

    /// <summary>2 因子の組の全部を含む、全行の部分（貪欲に選ぶ・並びは全行の順）</summary>
    public static IReadOnlySet<int> PairwiseRowIndexes { get; } = SelectPairwise();

    /// <summary>必ず含む 3 つ組（P1・Q6）</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> RequiredShapes { get; } =
    [
        new Dictionary<string, string>
        {
            [PkChange] = "Reorder",
            [ReferencingFks] = "2",
            [TypeChange] = "ChildOnly",
        },
        new Dictionary<string, string>
        {
            [UniqueMultiplicity] = "2",
            [ExplicitOp] = "DropUnique",
            [TypeChange] = "UniqueColumn",
        },
    ];

    /// <summary>台帳の行（宣言は全行の後に置く＝静的な初期化の順。形の行の次元表。行は同期計画の総当たりと能力の組の網の決定的な行として流れる）</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.SyncPlan,
            "ShapeRows",
            "同期計画の形の行（D4・網羅の配列から作る決定的な行）",
            Table,
            AllRows.Count,
            [
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanShapeRows.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanScenarios.cs",
            ],
            ["src/QuickER.Provider/SchemaDiffService.cs", "src/QuickER.Provider/SyncPlanner.cs"]
        );

    /// <summary>全方言の行</summary>
    public static IEnumerable<ScenarioRow<SyncPlanScenarioInput>> Rows =>
        DialectNames.All.SelectMany(RowsFor);

    /// <summary>全行に含まれない必須の 3 つ組（空なら全部含む）</summary>
    public static IEnumerable<string> MissingTriples() =>
        RequiredShapes
            .Where(shape => !AllRows.Any(row => shape.All(pair => row[pair.Key] == pair.Value)))
            .Select(shape => string.Join(" × ", shape.Select(p => $"{p.Key}={p.Value}")));

    /// <summary>行の Id（方言・全行の中の番号）</summary>
    public static string IdOf(string dialect, int index) =>
        $"{IdPrefix}{dialect}-{index.ToString("00", CultureInfo.InvariantCulture)}";

    /// <summary>方言の行</summary>
    private static IEnumerable<ScenarioRow<SyncPlanScenarioInput>> RowsFor(string dialect)
    {
        for (var index = 0; index < AllRows.Count; index++)
        {
            var row = AllRows[index];

            // Oracle は同じ列の並びに一意制約を 2 つ張れない（ORA-02261・DB の事実）
            if (dialect == DialectNames.Oracle && row[UniqueMultiplicity] == "2")
            {
                continue;
            }

            yield return new ScenarioRow<SyncPlanScenarioInput>(
                IdOf(dialect, index),
                null,
                $"形の行：{string.Join(" × ", row.Select(p => $"{p.Key}={p.Value}"))}",
                new SyncPlanScenarioInput(
                    dialect,
                    fuzzDialect => new SyncPlanScenarioCase(
                        fuzzDialect,
                        live => Build(live, row),
                        target => Mutate(target, row),
                        null
                    ),
                    PairwiseRowIndexes.Contains(index)
                )
            );
        }
    }

    /// <summary>DB 側の図</summary>
    private static void Build(ScenarioLive live, IReadOnlyDictionary<string, string> row)
    {
        var p = live.Table("p");
        live.Int(p, "id", pk: true);
        live.Int(p, "k", pk: true);
        var u = live.Int(p, "u", value: r => (r * 10).ToString(CultureInfo.InvariantCulture));
        live.Int(p, "w", value: r => (r * 100).ToString(CultureInfo.InvariantCulture));

        foreach (var name in UniqueNames(row))
        {
            live.Unique(p, name, u);
        }

        for (var i = 1; i <= Count(row[ReferencingFks]); i++)
        {
            var child = live.Table($"c{i}");
            live.Int(child, "id", pk: true);
            live.Ref(child, p, "p_ref", $"fk_c{i}_p");
        }
    }

    /// <summary>目標の図への変更</summary>
    private static void Mutate(ScenarioTarget target, IReadOnlyDictionary<string, string> row)
    {
        var refs = Count(row[ReferencingFks]);

        switch (row[TypeChange])
        {
            case "ChildOnly":
                target.SetType("c1", "p_ref_0", FuzzTypeKind.BigInt);
                break;
            case "ParentOnly":
                target.SetType("p", "id", FuzzTypeKind.BigInt);
                break;
            case "PartialBothEnds":
                target.SetType("p", "id", FuzzTypeKind.BigInt);
                target.SetType("c1", "p_ref_0", FuzzTypeKind.BigInt);
                break;
            case "BothEnds":
                target.SetType("p", "id", FuzzTypeKind.BigInt);

                for (var i = 1; i <= refs; i++)
                {
                    target.SetType($"c{i}", "p_ref_0", FuzzTypeKind.BigInt);
                }

                break;
            case "UniqueColumn":
                target.SetType("p", "u", FuzzTypeKind.BigInt);
                break;
        }

        switch (row[PkChange])
        {
            case "Reorder":
                target.SetPrimaryKey("p", "k", "id");
                break;
            case "Extend":
                target.SetPrimaryKey("p", "id", "k", "w");
                break;
            case "Shrink":
                target.SetPrimaryKey("p", "id");
                break;
            case "Move":
                target.SetPrimaryKey("p", "w");
                break;
            case "Remove":
                target.RemovePrimaryKey("p");
                break;
        }

        switch (row[ExplicitOp])
        {
            case "DropForeignKey":
                target.DropForeignKey(target.ForeignKey("c1", "p_ref_0"));
                break;
            case "DropUnique":
                target.DropUnique("p", UniqueNames(row)[0]);
                break;
        }
    }

    /// <summary>原理的に起こり得ない組（型の変更・明示の削除の相手が無い）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        bool Has(string factor, params string[] levels) =>
            assignment.TryGetValue(factor, out var level) && levels.Contains(level);

        // 子の列の型の変更と外部キーの削除は、親の主キーを参照する外部キーが要る
        if (
            Has(ReferencingFks, "0")
            && (
                Has(TypeChange, "ChildOnly", "PartialBothEnds", "BothEnds")
                || Has(ExplicitOp, "DropForeignKey")
            )
        )
        {
            return false;
        }

        // 親と最初の子だけの型の変更は、変えない兄弟の外部キーが要る（参照 1 本なら両端の変更と同じ）
        if (Has(ReferencingFks, "0", "1") && Has(TypeChange, "PartialBothEnds"))
        {
            return false;
        }

        // 一意制約の列の型の変更と一意制約の削除は、一意制約が要る
        return !(
            Has(UniqueMultiplicity, "0")
            && (Has(TypeChange, "UniqueColumn") || Has(ExplicitOp, "DropUnique"))
        );
    }

    /// <summary>2 因子の組の全部を含むように、全行から貪欲に選ぶ</summary>
    private static IReadOnlySet<int> SelectPairwise()
    {
        var uncovered = Table.RequiredPairs().ToHashSet();
        var chosen = new SortedSet<int>();

        while (uncovered.Count > 0)
        {
            var best = Enumerable
                .Range(0, AllRows.Count)
                .Where(i => !chosen.Contains(i))
                .Select(i => (Index: i, Count: uncovered.Count(t => t.IsContainedIn(AllRows[i]))))
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Index)
                .First();

            if (best.Count == 0)
            {
                throw new InvalidOperationException("網羅の配列が 2 因子の組を含まない");
            }

            chosen.Add(best.Index);
            uncovered.RemoveWhere(t => t.IsContainedIn(AllRows[best.Index]));
        }

        return chosen;
    }

    private static string[] UniqueNames(IReadOnlyDictionary<string, string> row) =>
        Count(row[UniqueMultiplicity]) switch
        {
            0 => [],
            1 => ["uq_p_u_a"],
            _ => ["uq_p_u_a", "uq_p_u_b"],
        };

    private static int Count(string level) => int.Parse(level, CultureInfo.InvariantCulture);

    private static Factor Levels(string name, string description, params string[] levels) =>
        new(name, [.. levels.Select(level => new Level(level))], description);
}
