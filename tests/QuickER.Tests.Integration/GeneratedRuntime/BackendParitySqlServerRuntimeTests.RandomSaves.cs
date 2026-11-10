using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SQL Server の系統の乱数の保存：種ごとに kits 3〜6 行の役・他者の変更・Before のフックの飛ばし・渡す順を引いて、グラフ保存 1 回と
/// 保存し直しを QuickER 版・EF Core・リモート（EF Core のサーバー）へ流し、基準の QuickER 版と比べる。
/// </summary>
public sealed partial class BackendParitySqlServerRuntimeTests
{
    /// <summary>
    /// 乱数の種（<c>QUICKER_PARITY_SQLSERVER_SAVE_SEED</c> が 1 つの種、<c>QUICKER_PARITY_SQLSERVER_SAVE_SEEDS</c> が数・既定 8・
    /// 夜間 200・1 から）
    /// </summary>
    public static SeedSettings RandomSaveSeeds { get; } =
        new(
            "QUICKER_PARITY_SQLSERVER_SAVE_SEEDS",
            8,
            200,
            singleVariable: "QUICKER_PARITY_SQLSERVER_SAVE_SEED"
        );

    /// <summary>今わかっている違い（範囲は <see cref="BackendParitySqlServerSaveDimensions.RandomScope"/>・署名は「実装先-differs」）</summary>
    private static readonly KnownFailureSet RandomSaveKnownSet = new(
        "BackendParity.sqlserver-saves",
        []
    );

    /// <summary>違いのあった種をいくつまで流して報告するか（それ以上は流さない）</summary>
    private const int MaxFailedRandomSaveSeeds = 3;

    [Fact(
        DisplayName = "[Parity/SqlServer] 乱数の保存は、EF Core とリモート（EF Core のサーバー）で QuickER 版とそろう"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task RandomSaves_MatchBaseline()
    {
        var plan = SeedSource.Resolve(RandomSaveSeeds);
        plan.WriteWarnings();
        var report = new EngineReport("BackendParity.sqlserver-saves", MaxFailedRandomSaveSeeds);
        var coverage = new CoverageCounter(BackendParitySqlServerSaveDimensions.Table);
        var arguments = new HashSet<SaveArguments>();
        var ranAllSeeds = true;

        foreach (var seed in plan.Seeds)
        {
            var scenario = BackendParitySqlServerSaveGenerator.Create(seed);
            BackendParitySqlServerSaveDimensions.Observe(coverage, scenario);
            arguments.Add(scenario.Arguments);

            var baseline = await RunRandomSaveAsync(scenario, RandomSaveBackend.QuickEr);
            var efCore = await RunRandomSaveAsync(scenario, RandomSaveBackend.EfCore);
            var remote = await RunRandomSaveAsync(scenario, RandomSaveBackend.RemoteEfCore);
            var detail =
                $"{scenario.Describe()}\nQuickER:\n  {string.Join("\n  ", baseline)}\nEF Core:\n  {string.Join("\n  ", efCore)}\nRemote (EF Core server):\n  {string.Join("\n  ", remote)}";
            List<EngineFailure> observed = [];

            if (
                BackendParitySqlServerSaveComparison.Difference(scenario, baseline, efCore)
                is not null
            )
            {
                observed.Add(
                    new EngineFailure(
                        BackendParitySqlServerSaveDimensions.RandomScope,
                        "efcore-differs",
                        detail,
                        seed
                    )
                );
            }

            if (
                BackendParitySqlServerSaveComparison.Difference(scenario, baseline, remote)
                is not null
            )
            {
                observed.Add(
                    new EngineFailure(
                        BackendParitySqlServerSaveDimensions.RandomScope,
                        "remote-efcore-differs",
                        detail,
                        seed
                    )
                );
            }

            var known = RandomSaveKnownSet.Classify(
                observed,
                plan,
                scope => scope == BackendParitySqlServerSaveDimensions.RandomScope
            );

            foreach (var failure in known.Unknown)
            {
                report.Add(failure, plan.ReproductionFor(seed));
            }

            if (report.Failures.Select(f => f.Seed).Distinct().Count() >= MaxFailedRandomSaveSeeds)
            {
                ranAllSeeds = false;
                break;
            }
        }

        // 網羅の宣言の誤りは種の結果より先に赤にする
        var coverageReport = coverage.Report();
        coverageReport.WriteSummary();
        coverageReport.WriteGapUsage(
            BackendParitySqlServerSaveDimensions.Gaps,
            BackendParitySqlServerSaveDimensions.RandomScope
        );
        report.Failures.Should().BeEmpty(report.Render());

        if (!ranAllSeeds || !plan.IsDefaultCount)
        {
            return;
        }

        // 保存の引数の 4 通りは種の番号から決まるので、既定の種の数ならどれも流れる
        CoverageGuard.ContainsAll(
            "[Parity/SqlServer] 乱数の保存の引数の組",
            arguments,
            Enum.GetValues<SaveArguments>()
        );

        // 網羅しない組の照合は夜間の既定の種の数でだけ行う（普段の 8 種では 3 因子の組が埋まらない）
        if (plan.Profile == EngineProfile.Nightly)
        {
            coverageReport.AssertGaps(
                BackendParitySqlServerSaveDimensions.Gaps,
                BackendParitySqlServerSaveDimensions.RandomScope,
                plan
            );
        }
    }

    /// <summary>乱数の保存を流す実装先</summary>
    private enum RandomSaveBackend
    {
        QuickEr,
        EfCore,
        RemoteEfCore,
    }

    /// <summary>
    /// 1 つの乱数の保存を空のスキーマから流し、結果・書いた行の手元の版が保存先と同じか・保存先を並べる。保存が成功したら、
    /// 消さなかった行の名前を変えて Optimistic でもう一度保存した結果と保存先も並べる
    /// </summary>
    private async Task<IReadOnlyList<string>> RunRandomSaveAsync(
        SqlServerSaveScenario scenario,
        RandomSaveBackend backend
    )
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);

        List<object> hooks = scenario.Hooks
            ? [new SkipKits([.. scenario.Rows.Where(row => row.Skipped).Select(row => row.KitId)])]
            : [];
        await using var host = backend switch
        {
            RandomSaveBackend.RemoteEfCore => await CompanionHost.RemoteEfCoreAsync(
                _fixture.ConnectionString,
                hooks
            ),
            _ => new CompanionHost(
                Build(backend == RandomSaveBackend.EfCore, withHooks: false, hooks: hooks),
                null
            ),
        };
        var kits = host.Client.GetRequiredService<IKitRemoteRepository>();
        var parts = host.Client.GetRequiredService<IKitPartRemoteRepository>();
        var existing = scenario.Rows.Where(row => row.Role != SaveRowRole.New).ToList();
        var nextPart = 1;

        // 初めの行は、実装先に依らず QuickER 版（Save フックなし）で入れる（比べるのは保存の手だけ）
        await using (
            var seeding = new ServiceCollection()
                .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
                .BuildServiceProvider()
        )
        {
            var seedKits = seeding.GetRequiredService<IKitRepository>();
            var seedParts = seeding.GetRequiredService<IKitPartRepository>();

            foreach (var row in existing)
            {
                await seedKits.InsertAsync(
                    new KitEntity
                    {
                        KitId = KitIdValue.Create(row.KitId),
                        Code = CodeValue.Create(row.InitialCode),
                        Name = NameValue.Create($"k{row.KitId}"),
                    },
                    Ct
                );
            }

            foreach (var row in existing)
            {
                for (var part = 0; part < row.Parts; part++)
                {
                    await seedParts.InsertAsync(
                        new KitPartEntity
                        {
                            PartId = PartIdValue.Create(nextPart++),
                            KitCode = CodeValue.Create(row.InitialCode),
                        },
                        Ct
                    );
                }
            }
        }

        // 読む（消す行は部品をコレクションに持たせる）
        var entities = new Dictionary<int, KitEntity>();
        var loadedParts = (await parts.GetAllAsync(Ct)).ToList();

        foreach (var row in existing)
        {
            var kit = (await kits.GetByIdAsync(KitIdValue.Create(row.KitId), Ct))!;

            if (row.Role == SaveRowRole.Delete)
            {
                foreach (var part in loadedParts.Where(p => p.KitCode?.Value == row.InitialCode))
                {
                    kit.KitParts.Add(part);
                }
            }

            entities[row.KitId] = kit;
        }

        // 読んだ後の他者の変更
        foreach (var row in existing)
        {
            switch (row.Condition)
            {
                case SaveRowCondition.Gone:
                    await _fixture.ExecuteAsync($"DELETE FROM kits WHERE kit_id = {row.KitId}", Ct);
                    break;

                case SaveRowCondition.Stale:
                    await _fixture.ExecuteAsync(
                        $"UPDATE kits SET name = N'other' WHERE kit_id = {row.KitId}",
                        Ct
                    );
                    break;
            }
        }

        // 保存の前の変更（渡す順のまま根を並べる）
        var roots = new List<KitEntity>();

        foreach (var row in scenario.Rows)
        {
            KitEntity kit;

            if (row.Role == SaveRowRole.New)
            {
                kit = new KitEntity
                {
                    KitId = KitIdValue.Create(row.KitId),
                    Code = CodeValue.Create(row.InitialCode),
                    Name = NameValue.Create($"new{row.KitId}"),
                };
                kit.MarkAdded();
                entities[row.KitId] = kit;
            }
            else
            {
                kit = entities[row.KitId];

                if (row.Role == SaveRowRole.Delete)
                {
                    kit.MarkRemoved();
                }
                else
                {
                    if (row.NewCode is { } code)
                    {
                        kit.Code = CodeValue.Create(code);
                    }

                    kit.Name = NameValue.Create($"edited{row.KitId}");
                    kit.MarkUpdated();
                }
            }

            roots.Add(kit);
        }

        var outcome = await RandomSaveOutcomeAsync(() =>
            kits.SaveAsync(
                roots,
                insertWhenUpdateMissing: scenario.InsertWhenUpdateMissing,
                mode: scenario.Mode,
                cancellationToken: Ct
            )
        );
        List<string> lines = [outcome];
        var written = scenario
            .Rows.Where(row => row.Role != SaveRowRole.Delete)
            .OrderBy(row => row.KitId)
            .ToList();

        foreach (var row in written)
        {
            lines.Add(
                $"kit {row.KitId} version-matches-store={await VersionMatchesStoreAsync(entities[row.KitId])}"
            );
        }

        lines.AddRange(await KitsStateAsync());

        if (outcome.StartsWith("rows=", StringComparison.Ordinal))
        {
            foreach (var row in written)
            {
                var kit = entities[row.KitId];
                kit.Name = NameValue.Create($"again{row.KitId}");

                if (!kit.IsAdded)
                {
                    kit.MarkUpdated();
                }
            }

            lines.Add(
                $"again {await RandomSaveOutcomeAsync(() => kits.SaveAsync([.. written.Select(row => entities[row.KitId])], mode: ConcurrencyMode.Optimistic, cancellationToken: Ct))}"
            );
            lines.AddRange(await KitsStateAsync());
        }

        return lines;
    }

    /// <summary>
    /// 呼び出しの結果の行（競合は理由に加えてキーも書く＝<see cref="BackendParitySqlServerSaveComparison"/> が候補と照らす。
    /// ほかは <see cref="OutcomeAsync"/> と同じ）
    /// </summary>
    private static async Task<string> RandomSaveOutcomeAsync(Func<Task<int>> call)
    {
        try
        {
            return $"rows={await call()}";
        }
        catch (SaveConflictException conflict)
        {
            return BackendParitySqlServerSaveComparison.ConflictLine(conflict);
        }
        catch (Exception ex)
        {
            return await OutcomeAsync(() => Task.FromException<int>(ex));
        }
    }

    /// <summary>指定した kit の操作（更新・削除・挿入）を Before で飛ばす</summary>
    private sealed class SkipKits(IReadOnlyList<int> kitIds) : ISaveHook<KitEntity>
    {
        public Task<bool> BeforeSaveAsync(
            KitEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(!kitIds.Contains(entity.KitId.Value));
    }
}
