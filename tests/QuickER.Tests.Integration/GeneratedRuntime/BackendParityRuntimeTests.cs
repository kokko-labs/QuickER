using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 実装先どうしの一致テスト：同じ操作の列を QuickER 版（SQLite 実ファイル）・EF Core（SQLite 実ファイル）・インメモリ・
/// リモート（in-process Kestrel）へ流し、戻り値・保存先の全行・呼び出し側のグラフ・Save フックの呼び出しが基準の
/// QuickER 版とそろうことを表明する（Docker 不要＝CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// 実装先は DI の登録を差し替えるだけで交換できる、というのが生成コードの約束なので、同じ呼び出しが実装先によって違う
/// 行を書いたり、違う例外になったりするのは、利用者から見ると黙ったデータの違いになる。ここで比べるのは docs に違いが
/// 書かれていない面だけで、書いてある違い（リモートに無い操作・インメモリの生 SQL・EF Core の blob の Stream・
/// 取得した値の除外 blob・EF Core のフックが再試行の回数だけ走ること）は比べ方の側で吸収する。
/// </para>
/// <para>
/// 今わかっている違いは <see cref="BackendParityKnownDifferences"/> が名指しし、直す区切りで一覧から外す。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class BackendParityRuntimeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Theory(
        DisplayName = "[Parity] シナリオの結果は、既知の違いを除いて全実装先で QuickER 版とそろう"
    )]
    [MemberData(nameof(BackendParityScenarios.Names), MemberType = typeof(BackendParityScenarios))]
    public async Task Scenario_MatchesBaselineExceptKnownDifferences(string name)
    {
        var scenario = BackendParityScenarios.Get(name);
        var result = await scenario.RunAsync(Ct);
        var known = BackendParityKnownDifferences
            .All.Where(e => e.Scenario == name)
            .Select(e => (e.Backend, e.Aspect))
            .ToHashSet();

        var unexpected = result
            .Violations.Where(v => !known.Contains((v.Backend, v.Aspect)))
            .ToList();
        var nowPassing = known.Where(k => !result.Groups.Contains(k)).ToList();

        unexpected
            .Should()
            .BeEmpty(
                "載っていない違いは無いはず:\n"
                    + string.Join(
                        "\n",
                        unexpected
                            .GroupBy(v => (v.Backend, v.Aspect))
                            .Select(g => g.First().Describe())
                    )
            );
        nowPassing
            .Should()
            .BeEmpty(
                "既知の違いの一覧に載っているのに違いが出なかった（直ったなら一覧から外す）:\n"
                    + string.Join("\n", nowPassing)
            );
    }

    [Fact(
        DisplayName = "[Parity] 既知の違いの一覧は、存在するシナリオと流れる実装先を名指しし、指摘と区切りを持つ"
    )]
    public void KnownDifferences_NameExistingScenariosAndBackends()
    {
        foreach (var entry in BackendParityKnownDifferences.All)
        {
            var scenario = BackendParityScenarios.All.SingleOrDefault(s =>
                s.Name == entry.Scenario
            );
            scenario.Should().NotBeNull(entry.Scenario);
            scenario!.Backends.Should().Contain(entry.Backend, entry.ToString());

            if (entry.Aspect == ParityStep.ExpectAspect)
            {
                // 期待は基準の QuickER 版にも掛かる（決定へ基準自身が追いついていないことがある）
                scenario.Ops.Should().Contain(op => op is ExpectOp, entry.ToString());
            }
            else
            {
                entry.Backend.Should().NotBe(ParityBackendKind.Ado, "QuickER 版は基準");
                ParityStep.Aspects.Should().Contain(entry.Aspect);
                scenario.CompareWithBaseline.Should().BeTrue(entry.ToString());
            }

            entry.Finding.Should().NotBeNullOrWhiteSpace();

            // 0 は直さないと決めた差（受容）＝一覧に残り続ける
            entry.Section.Should().BeGreaterThanOrEqualTo(0);
        }

        BackendParityKnownDifferences
            .All.Select(e => (e.Scenario, e.Backend, e.Aspect))
            .Should()
            .OnlyHaveUniqueItems();
    }

    /// <summary>
    /// 乱数の操作の列：種ごとに 20〜40 手の列を作り、4 つの実装先（範囲によっては 3 つ）で流して、基準の QuickER 版と比べる。
    /// </summary>
    /// <remarks>
    /// 種の数は既定 200（<c>QUICKER_PARITY_SEEDS</c> で変える）。<c>QUICKER_PARITY_SEED</c> で 1 つの種だけを流せる（失敗の
    /// 再現用）。決まった形の一覧に残っている指摘の違いは <see cref="BackendParityKnownDifferences.RandomTolerances"/> が
    /// 一時的に見逃し、見逃した種の数を出力する。
    /// </remarks>
    [Fact(DisplayName = "[Parity] 乱数の操作の列は、全実装先で QuickER 版とそろう")]
    public async Task RandomSequences_MatchBaseline()
    {
        var seeds = SeedsToRun();
        var failures = new List<string>();
        var tolerated = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var seed in seeds)
        {
            var scenario = BackendParityGenerator.Create(seed);
            var result = await scenario.RunAsync(Ct);
            var unexplained = new List<ParityViolation>();

            foreach (var violation in result.Violations)
            {
                var tolerance = BackendParityKnownDifferences.RandomTolerances.FirstOrDefault(t =>
                    t.Covers(violation)
                );

                if (tolerance is null)
                {
                    unexplained.Add(violation);
                }
                else
                {
                    tolerated[tolerance.Finding] =
                        tolerated.GetValueOrDefault(tolerance.Finding) + 1;
                }
            }

            if (unexplained.Count > 0)
            {
                failures.Add(
                    $"seed {seed} ({scenario.Name}):\n"
                        + string.Join("\n", scenario.Ops.Select((op, i) => $"  {i, 2} {op}"))
                        + "\n"
                        + string.Join(
                            "\n",
                            unexplained
                                .GroupBy(v => (v.Backend, v.Aspect))
                                .Select(g => g.First().Describe())
                        )
                );

                if (failures.Count >= 3)
                {
                    break;
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"seeds={seeds.Count} tolerated=[{string.Join(", ", tolerated.Select(kv => $"{kv.Key}:{kv.Value}"))}]"
        );
        failures.Should().BeEmpty(string.Join("\n\n", failures));
    }

    [Fact(
        DisplayName = "[Parity] 乱数の列の見逃しは、決まった形の一覧に同じ指摘が残っている間だけ"
    )]
    public void RandomTolerances_ReferToOpenFindings()
    {
        foreach (var tolerance in BackendParityKnownDifferences.RandomTolerances)
        {
            BackendParityKnownDifferences
                .All.Should()
                .Contain(
                    e => e.Finding == tolerance.Finding && e.Section == tolerance.Section,
                    $"{tolerance.Finding} が直ったら（決まった形の一覧から外したら）、乱数の列の見逃しも外す"
                );
        }
    }

    [Fact(DisplayName = "[Parity] 乱数の操作の列は種ごとに決まり、3 つの範囲をすべて作る")]
    public void Generator_IsDeterministicAndCoversAllModes()
    {
        var first = BackendParityGenerator.Create(7);
        var second = BackendParityGenerator.Create(7);
        first
            .Ops.Select(op => op.ToString())
            .Should()
            .Equal(second.Ops.Select(op => op.ToString()));

        Enumerable
            .Range(0, 3)
            .Select(seed => BackendParityGenerator.Create(seed).Name.Split('-')[^1])
            .Should()
            .BeEquivalentTo(Enum.GetNames<ParityGeneratorMode>());
    }

    /// <summary>流す種（<c>QUICKER_PARITY_SEED</c> が 1 つの種、<c>QUICKER_PARITY_SEEDS</c> が数）</summary>
    private static IReadOnlyList<int> SeedsToRun()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("QUICKER_PARITY_SEED"), out var single))
        {
            return [single];
        }

        var count = int.TryParse(
            Environment.GetEnvironmentVariable("QUICKER_PARITY_SEEDS"),
            out var configured
        )
            ? configured
            : 200;

        return [.. Enumerable.Range(1, count)];
    }

    [Fact(DisplayName = "[Parity] シナリオの名前は重複せず、どれも QuickER 版（基準）で流れる")]
    public void Scenarios_HaveUniqueNamesAndRunOnTheBaseline()
    {
        BackendParityScenarios.All.Select(s => s.Name).Should().OnlyHaveUniqueItems();
        BackendParityScenarios
            .All.Should()
            .AllSatisfy(s => s.Backends.Should().Contain(ParityBackendKind.Ado, s.Name));
    }

    /// <summary>
    /// 比べ方の前提：インメモリの Save フックは、保存のすべての Before を走らせてから After をまとめて走らせ、QuickER 版は
    /// 行ごとに Before と After を並べる。
    /// </summary>
    /// <remarks>
    /// インメモリは保存の段で書き込みをステージングし、After の段を後から流す作りなので、Before と After の混ざり方は実装先の
    /// 違いとして持つ（決定 2026-10-13＝docs の Save フック節に書く）。比べ方は Before と After を別々の列にしてこの違いを
    /// 吸収しているので、前提が変わったら（インメモリが行ごとに並べるようになったら）ここで気づいて、正規化と docs を見直す。
    /// </remarks>
    [Fact(
        DisplayName = "[Parity] 前提: インメモリは Before をすべて走らせてから After をまとめ、QuickER 版は行ごとに並べる"
    )]
    public async Task Premise_InMemoryRunsAllBeforeHooksFirst()
    {
        var scenario = BackendParityScenarios.Get("graph-edit-through-root");
        var result = await scenario.RunAsync(Ct, [ParityBackendKind.InMemory]);

        static IReadOnlyList<string> SaveHooks(IReadOnlyList<ParityStep> trace) =>
            trace.Single(s => s.OpName == "Save").RawHooks;

        static bool AllBeforeFirst(IReadOnlyList<string> hooks) =>
            hooks
                .SkipWhile(h => h.StartsWith("Before:", StringComparison.Ordinal))
                .All(h => h.StartsWith("After:", StringComparison.Ordinal));

        var inMemory = SaveHooks(result.Traces[ParityBackendKind.InMemory]);
        var ado = SaveHooks(result.Traces[ParityBackendKind.Ado]);

        inMemory.Count.Should().BeGreaterThan(2);
        AllBeforeFirst(inMemory).Should().BeTrue(string.Join(", ", inMemory));
        AllBeforeFirst(ado).Should().BeFalse(string.Join(", ", ado));
    }

    /// <summary>
    /// 対照：1 つの実装先だけでグラフの RowState を変える・保存先の行を消す操作を混ぜると、その実装先のその観点だけが
    /// 違いとして数えられる（比べ方が違いを見落とさないこと）。
    /// </summary>
    [Fact(
        DisplayName = "[Parity] 対照: 1 つの実装先だけに混ぜた違いは、その実装先のその観点だけで検出される"
    )]
    public async Task Control_DivergenceInOneBackend_IsDetectedThere()
    {
        var scenario = new ParityScenario(
            "control-divergence",
            [new SeedOp(new CustomerRow(1, "Alice")), new SeedOp(new OrderRow(10, 1, 1m))],
            [
                new LoadOp("A", "orders", 10),
                new DivergeOp(ParityBackendKind.InMemory, DivergeKind.SlotState),
                new DivergeOp(ParityBackendKind.Remote, DivergeKind.StoredRow),
            ]
        );

        var result = await scenario.RunAsync(Ct);

        result
            .Groups.Should()
            .BeEquivalentTo(
                new[] { (ParityBackendKind.InMemory, "slots"), (ParityBackendKind.Remote, "state") }
            );
    }

    /// <summary>対照のための違いの種類</summary>
    private enum DivergeKind
    {
        SlotState,
        StoredRow,
    }

    /// <summary>対照：指定の実装先でだけ、スロットの RowState を変えるか、保存先の行を消す</summary>
    private sealed record DivergeOp(ParityBackendKind Target, DivergeKind Kind) : ParityOp
    {
        public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
        {
            if (backend.Kind == Target)
            {
                if (Kind == DivergeKind.SlotState)
                {
                    backend.Slots["A"].MarkRemoved();
                }
                else
                {
                    await backend.Outside.Orders.DeleteAsync(
                        QuickER.Tests.GeneratedBackendParityFixture.OrderIdValue.Create(10),
                        ct
                    );
                }
            }

            return "ok";
        }
    }
}
