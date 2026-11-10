using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 代替キーの行と、同じグラフ保存のほかの行の組（SQL Server の系統）。代替キーを書いた後に、同じ保存のほかの行の理由で
/// 試行をやり直す経路を、QuickER 版と EF Core の一致で確かめる。
/// </summary>
/// <remarks>
/// 軸は 3 つ：代替キーの行の形（<see cref="AlternateKeyRowShape"/>）× 同じ保存のほかの行（<see cref="AlternateKeyCompanion"/>）×
/// 並行性の扱い（Optimistic・ForceOverwrite）。組はすべて流す（<see cref="AlternateKeyCompanionCase.All"/>）。ほかの行が無い組は
/// 決まった形のケース <c>alternate-key-*</c> が持つ。
/// </remarks>
public sealed partial class BackendParitySqlServerRuntimeTests
{
    /// <summary>plain の形で、ほかの行（kit 2）が消えて insertWhenUpdateMissing で挿入される保存の行</summary>
    private static string[] AlternateKeyGoneCompanionPlain =>
        [
            "rows=2",
            "version-matches-store=True",
            "kits/1 code=K9 name=k1",
            "kits/2 code=K2 name=companion",
            "again rows=1",
            "kits/1 code=K9 name=again",
            "kits/2 code=K2 name=companion",
        ];

    /// <summary>taken-from-deleted の形で、後回しの 2 本目（kit 3）が消えて insertWhenUpdateMissing で挿入される保存の行</summary>
    private static string[] AlternateKeyGoneCompanionTakenFromDeleted =>
        [
            "rows=6",
            "version-matches-store=True",
            "kits/2 code=K1 name=k2",
            "kits/3 code=K4 name=companion",
            "again rows=1",
            "kits/2 code=K1 name=again",
            "kits/3 code=K4 name=companion",
        ];

    /// <summary>代替キーの行の形</summary>
    private enum AlternateKeyRowShape
    {
        /// <summary>kit 1 の code を、誰も持たない値へ変える（1 つめの文脈で書く）</summary>
        Plain,

        /// <summary>
        /// 部品ごと消す kit 1 の code を kit 2 が取る（EF Core は削除の後に 2 つめの文脈で書く）。ほかの行は、同じ保存で消す
        /// kit 4 の code を取る kit 3 に置き、2 つめの文脈の中で書く
        /// </summary>
        TakenFromDeleted,
    }

    /// <summary>同じ保存のほかの行（代替キーを書く行と一緒に渡す更新の行）</summary>
    private enum AlternateKeyCompanion
    {
        /// <summary>読んだ後に他者が消した行（insertWhenUpdateMissing なし）</summary>
        Gone,

        /// <summary>読んだ後に他者が消した行を insertWhenUpdateMissing で保存する（消えた行は挿入に切り替わる）</summary>
        GoneInsertWhenMissing,

        /// <summary><see cref="GoneInsertWhenMissing"/> を、kits の Save フックを登録した経路で保存する</summary>
        GoneInsertWhenMissingHooks,

        /// <summary>読んだ後に他者が名前を変えて版を進めた行</summary>
        Stale,

        /// <summary>Before のフックが更新を飛ばす行</summary>
        HookSkipped,
    }

    /// <summary>代替キーの行と同じ保存のほかの行の組の 1 ケース</summary>
    private sealed record AlternateKeyCompanionCase(
        AlternateKeyRowShape Shape,
        AlternateKeyCompanion Companion,
        ConcurrencyMode Mode
    )
    {
        /// <summary>軸の全組（形 × ほかの行 × 並行性の扱い）</summary>
        public static IReadOnlyList<AlternateKeyCompanionCase> All { get; } =
        [
            .. from shape in Enum.GetValues<AlternateKeyRowShape>()
            from companion in Enum.GetValues<AlternateKeyCompanion>()
            from mode in new[] { ConcurrencyMode.Optimistic, ConcurrencyMode.ForceOverwrite }
            select new AlternateKeyCompanionCase(shape, companion, mode),
        ];

        /// <summary>ケースの名前</summary>
        public string Name =>
            $"alternate-key-{ShapeName(Shape)}-with-{CompanionName(Companion)}-{ModeName(Mode)}";

        /// <summary>保存に insertWhenUpdateMissing を渡すか</summary>
        public bool InsertWhenUpdateMissing =>
            Companion
                is AlternateKeyCompanion.GoneInsertWhenMissing
                    or AlternateKeyCompanion.GoneInsertWhenMissingHooks;

        /// <summary>名前のケース（無ければ null）</summary>
        public static AlternateKeyCompanionCase? Find(string name) =>
            All.SingleOrDefault(companionCase => companionCase.Name == name);

        private static string ShapeName(AlternateKeyRowShape shape) =>
            shape switch
            {
                AlternateKeyRowShape.Plain => "plain",
                AlternateKeyRowShape.TakenFromDeleted => "taken-from-deleted",
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            };

        private static string CompanionName(AlternateKeyCompanion companion) =>
            companion switch
            {
                AlternateKeyCompanion.Gone => "gone",
                AlternateKeyCompanion.GoneInsertWhenMissing => "gone-insert-when-missing",
                AlternateKeyCompanion.GoneInsertWhenMissingHooks =>
                    "gone-insert-when-missing-hooks",
                AlternateKeyCompanion.Stale => "stale",
                AlternateKeyCompanion.HookSkipped => "hook-skipped",
                _ => throw new ArgumentOutOfRangeException(nameof(companion)),
            };

        private static string ModeName(ConcurrencyMode mode) =>
            mode switch
            {
                ConcurrencyMode.Optimistic => "optimistic",
                ConcurrencyMode.ForceOverwrite => "force-overwrite",
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
    }

    /// <summary>
    /// 代替キーの行とほかの行を、グラフ保存 1 回で保存する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AlternateKeyRowShape.Plain"/>：kit 1（K1）の code を K9 に、kit 2 の名前を変える。
    /// <see cref="AlternateKeyRowShape.TakenFromDeleted"/>：部品 10・11 に参照される kit 1（K1）と kit 4（K4）を消し、kit 2 の code を
    /// K1 に、kit 3 の code を K4 にして名前を変える（kit 2 と kit 3 は削除の後に書く後回しの行）。ほかの行は kit 2（Plain）か
    /// kit 3（TakenFromDeleted）で、読んだ後に消える・版が進む・Before のフックが更新を飛ばす。
    /// </para>
    /// <para>
    /// 結果・代替キーを書いた行の手元の版が保存先と同じか・保存先を並べ、保存が成功したら、代替キーを書いた行の名前を変えて
    /// もう一度 Optimistic で保存する（手元の版が古いと偽の競合になる）。
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<string>> AlternateKeyWithCompanionAsync(
        bool efCore,
        AlternateKeyCompanionCase companionCase,
        bool remoteEfCore = false
    )
    {
        var takenFromDeleted = companionCase.Shape == AlternateKeyRowShape.TakenFromDeleted;
        var writerId = takenFromDeleted ? 2 : 1;
        var companionId = takenFromDeleted ? 3 : 2;
        List<object> hooks = companionCase.Companion switch
        {
            AlternateKeyCompanion.GoneInsertWhenMissingHooks => [new NoOpKitHook()],
            AlternateKeyCompanion.HookSkipped => [new SkipKitUpdate(companionId)],
            _ => [],
        };
        await using var host = remoteEfCore
            ? await CompanionHost.RemoteEfCoreAsync(_fixture.ConnectionString, hooks)
            : new CompanionHost(Build(efCore, withHooks: false, hooks: hooks), null);
        var kits = host.Client.GetRequiredService<IKitRemoteRepository>();
        var parts = host.Client.GetRequiredService<IKitPartRemoteRepository>();

        foreach (var id in takenFromDeleted ? new[] { 1, 2, 3, 4 } : [1, 2])
        {
            await kits.InsertAsync(NewKit(id), Ct);
        }

        List<KitEntity> deleted = [];

        if (takenFromDeleted)
        {
            await parts.InsertAsync(NewPart(10), Ct);
            await parts.InsertAsync(NewPart(11), Ct);
            var withParts = (await kits.GetByIdAsync(KitIdValue.Create(1), Ct))!;

            foreach (var id in new[] { 10, 11 })
            {
                withParts.KitParts.Add((await parts.GetByIdAsync(PartIdValue.Create(id), Ct))!);
            }

            deleted.Add(withParts);
            deleted.Add((await kits.GetByIdAsync(KitIdValue.Create(4), Ct))!);
        }

        var writer = (await kits.GetByIdAsync(KitIdValue.Create(writerId), Ct))!;
        var companion = (await kits.GetByIdAsync(KitIdValue.Create(companionId), Ct))!;

        switch (companionCase.Companion)
        {
            case AlternateKeyCompanion.Gone
            or AlternateKeyCompanion.GoneInsertWhenMissing
            or AlternateKeyCompanion.GoneInsertWhenMissingHooks:
                await _fixture.ExecuteAsync($"DELETE FROM kits WHERE kit_id = {companionId}", Ct);
                break;

            case AlternateKeyCompanion.Stale:
                await _fixture.ExecuteAsync(
                    $"UPDATE kits SET name = N'other' WHERE kit_id = {companionId}",
                    Ct
                );
                break;
        }

        foreach (var row in deleted)
        {
            row.MarkRemoved();
        }

        writer.Code = CodeValue.Create(takenFromDeleted ? "K1" : "K9");
        writer.MarkUpdated();

        if (takenFromDeleted)
        {
            companion.Code = CodeValue.Create("K4");
        }

        companion.Name = NameValue.Create("companion");
        companion.MarkUpdated();

        var mode = companionCase.Mode;
        var outcome = await OutcomeAsync(() =>
            kits.SaveAsync(
                [writer, companion, .. deleted],
                insertWhenUpdateMissing: companionCase.InsertWhenUpdateMissing,
                mode: mode,
                cancellationToken: Ct
            )
        );
        List<string> lines =
        [
            outcome,
            $"version-matches-store={await VersionMatchesStoreAsync(writer)}",
            .. await KitsStateAsync(),
        ];

        if (outcome.StartsWith("rows=", StringComparison.Ordinal))
        {
            writer.Name = NameValue.Create("again");
            writer.MarkUpdated();
            lines.Add(
                $"again {await OutcomeAsync(() => kits.SaveAsync(writer, mode: ConcurrencyMode.Optimistic, cancellationToken: Ct))}"
            );
            lines.AddRange(await KitsStateAsync());
        }

        return lines;

        static KitEntity NewKit(int id) =>
            new()
            {
                KitId = KitIdValue.Create(id),
                Code = CodeValue.Create($"K{id}"),
                Name = NameValue.Create($"k{id}"),
            };

        static KitPartEntity NewPart(int id) =>
            new() { PartId = PartIdValue.Create(id), KitCode = CodeValue.Create("K1") };
    }

    /// <summary>1 つのケースを空のスキーマから、サーバーが EF Core 版 Repository のリモートで流す</summary>
    private async Task<IReadOnlyList<string>> RunOnRemoteEfCoreAsync(
        AlternateKeyCompanionCase companionCase
    )
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);

        return await AlternateKeyWithCompanionAsync(
            efCore: true,
            companionCase,
            remoteEfCore: true
        );
    }

    /// <summary>
    /// ケースが操作するリポジトリの持ち主：QuickER 版・EF Core はそのプロバイダ、リモートはクライアントのプロバイダと、
    /// EF Core 版 Repository を持つサーバー（Save フックはサーバーに登録する）
    /// </summary>
    private sealed class CompanionHost(ServiceProvider client, InProcessRemoteServer? server)
        : IAsyncDisposable
    {
        /// <summary>リポジトリを解決するプロバイダ</summary>
        public ServiceProvider Client { get; } = client;

        /// <summary>サーバーが EF Core 版 Repository のリモートを起動する</summary>
        public static async Task<CompanionHost> RemoteEfCoreAsync(
            string connectionString,
            IReadOnlyList<object> hooks
        )
        {
            var server = await InProcessRemoteServer.StartAsync(
                services =>
                {
                    services.AddGeneratedEfCoreRepositories(options =>
                        options.UseSqlServer(connectionString).IgnoreServiceProviderCacheLimit()
                    );

                    foreach (var hook in hooks)
                    {
                        services.AddSaveHook(hook);
                    }
                },
                app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
                Ct
            );
            var client = new ServiceCollection()
                .AddGeneratedHttpRemoteRepositories(server.BaseAddress(RemotePaths.DefaultPrefix))
                .BuildServiceProvider();

            return new CompanionHost(client, server);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();

            if (server is not null)
            {
                await server.DisposeAsync();
            }
        }
    }

    /// <summary>何もしない kits の Save フック（EF Core の kits の保存をフックありの経路へ回す）</summary>
    private sealed class NoOpKitHook : ISaveHook<KitEntity>;

    /// <summary>指定した kit の更新（Update）だけを Before で飛ばす</summary>
    private sealed class SkipKitUpdate(int kitId) : ISaveHook<KitEntity>
    {
        public Task<bool> BeforeSaveAsync(
            KitEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(!(operation == SaveOperation.Update && entity.KitId.Value == kitId));
    }
}
