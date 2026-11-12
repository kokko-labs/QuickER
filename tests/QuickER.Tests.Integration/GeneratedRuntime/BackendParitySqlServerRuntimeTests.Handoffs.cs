using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同じグラフ保存の中の代替キーの手放しと取り（SQL Server の系統）：2 行の入れ替えと、更新する行が手放す code を挿入する行が
/// 取る形と、手放す code を参照する部品を別の kit へ付け替える形を、渡す順ごとに、他者が消した部品の後ろでどの実装先でも断られる
/// 取りの順を 1 つ、QuickER 版と EF Core とリモート（EF Core のサーバー）で比べる。
/// </summary>
public sealed partial class BackendParitySqlServerRuntimeTests
{
    /// <summary>入れ替えの保存の行（DB が最初の更新で断り、保存先は初めのまま）</summary>
    private static string[] AlternateKeySwapRejected =>
        ["throws store-rejected", "kits/1 code=K1 name=k1", "kits/2 code=K2 name=k2"];

    /// <summary>代替キーの手放しと取りの形</summary>
    private enum HandoffShape
    {
        /// <summary>kit 1（K1）と kit 2（K2）が code を入れ替える</summary>
        Swap,

        /// <summary>kit 1 が K1 を K9 へ変えて手放し、新しい kit 100 を K1 で挿入する</summary>
        NewTakesReleased,

        /// <summary>
        /// kit 1 が K1 を K9 へ変えて手放し、読んだ後に他者が消した kit 2 を、code を K1 に変えて insertWhenUpdateMissing で
        /// 保存する（消えた行は挿入に切り替わる）
        /// </summary>
        GoneInsertTakesReleased,

        /// <summary>
        /// kit 1 が K1 を K9 へ変えて手放し、K1 を参照する部品 10 を同じ保存で kit 2 へ付け替える（取る行＝部品を持たせる kit 2）
        /// </summary>
        PartMovedOffReleased,

        /// <summary>
        /// kit 2 の下で更新する部品 10 を読んだ後に他者が消し、後ろに渡した kit 1 が、さらに後ろの kit 3 が手放す K3 を取る
        /// （取る行が先＝どの実装先でも一意制約に断られる順）
        /// </summary>
        GonePartThenTaker,
    }

    /// <summary>代替キーの手放しと取りの 1 ケース</summary>
    private sealed record HandoffCase(HandoffShape Shape, bool TakerFirst, ConcurrencyMode Mode)
    {
        /// <summary>軸の組（入れ替えは渡す順に依らず両方が取る行なので、並行性の扱いで分ける）</summary>
        public static IReadOnlyList<HandoffCase> All { get; } =
        [
            new(HandoffShape.Swap, TakerFirst: true, ConcurrencyMode.Optimistic),
            new(HandoffShape.Swap, TakerFirst: true, ConcurrencyMode.ForceOverwrite),
            new(HandoffShape.NewTakesReleased, TakerFirst: false, ConcurrencyMode.Optimistic),
            new(HandoffShape.NewTakesReleased, TakerFirst: true, ConcurrencyMode.Optimistic),
            new(
                HandoffShape.GoneInsertTakesReleased,
                TakerFirst: false,
                ConcurrencyMode.Optimistic
            ),
            new(HandoffShape.GoneInsertTakesReleased, TakerFirst: true, ConcurrencyMode.Optimistic),
            new(HandoffShape.PartMovedOffReleased, TakerFirst: false, ConcurrencyMode.Optimistic),
            new(HandoffShape.PartMovedOffReleased, TakerFirst: true, ConcurrencyMode.Optimistic),
            new(HandoffShape.GonePartThenTaker, TakerFirst: true, ConcurrencyMode.Optimistic),
        ];

        /// <summary>ケースの名前</summary>
        public string Name =>
            Shape switch
            {
                HandoffShape.Swap => $"alternate-key-swap-{ModeName(Mode)}",
                HandoffShape.NewTakesReleased =>
                    $"alternate-key-new-takes-released-{OrderName(TakerFirst)}",
                HandoffShape.GoneInsertTakesReleased =>
                    $"alternate-key-gone-insert-takes-released-{OrderName(TakerFirst)}",
                HandoffShape.PartMovedOffReleased =>
                    $"alternate-key-part-moved-off-released-{OrderName(TakerFirst)}",
                HandoffShape.GonePartThenTaker => "alternate-key-gone-part-then-taker-first",
                _ => throw new ArgumentOutOfRangeException(nameof(Shape)),
            };

        /// <summary>名前のケース（無ければ null）</summary>
        public static HandoffCase? Find(string name) =>
            All.SingleOrDefault(handoffCase => handoffCase.Name == name);

        private static string OrderName(bool takerFirst) =>
            takerFirst ? "taker-first" : "releaser-first";

        private static string ModeName(ConcurrencyMode mode) =>
            mode switch
            {
                ConcurrencyMode.Optimistic => "optimistic",
                ConcurrencyMode.ForceOverwrite => "force-overwrite",
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
    }

    /// <summary>代替キーの手放しと取りを、グラフ保存 1 回で保存し、結果と保存先を並べる</summary>
    private async Task<IReadOnlyList<string>> HandoffAsync(
        bool efCore,
        HandoffCase handoffCase,
        bool remoteEfCore = false
    )
    {
        if (remoteEfCore)
        {
            await _fixture.ResetSchemaAsync(Ct);
            await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);
        }

        await using var host = remoteEfCore
            ? await CompanionHost.RemoteEfCoreAsync(_fixture.ConnectionString, [])
            : new CompanionHost(Build(efCore, withHooks: false), null);
        var kits = host.Client.GetRequiredService<IKitRemoteRepository>();
        var parts = host.Client.GetRequiredService<IKitPartRemoteRepository>();

        foreach (
            var id in handoffCase.Shape == HandoffShape.GonePartThenTaker
                ? new[] { 1, 2, 3 }
                : [1, 2]
        )
        {
            await kits.InsertAsync(
                new KitEntity
                {
                    KitId = KitIdValue.Create(id),
                    Code = CodeValue.Create($"K{id}"),
                    Name = NameValue.Create($"k{id}"),
                },
                Ct
            );
        }

        // 部品 10：付け替える形は kit 1（K1）を、消える部品の形は kit 2（K2）を参照する
        if (
            handoffCase.Shape is HandoffShape.PartMovedOffReleased or HandoffShape.GonePartThenTaker
        )
        {
            await parts.InsertAsync(
                new KitPartEntity
                {
                    PartId = PartIdValue.Create(10),
                    KitCode = CodeValue.Create(
                        handoffCase.Shape == HandoffShape.PartMovedOffReleased ? "K1" : "K2"
                    ),
                },
                Ct
            );
        }

        var first = (await kits.GetByIdAsync(KitIdValue.Create(1), Ct))!;
        var second = (await kits.GetByIdAsync(KitIdValue.Create(2), Ct))!;
        KitEntity taker;
        KitEntity[]? fixedRoots = null;
        var insertWhenUpdateMissing = false;

        switch (handoffCase.Shape)
        {
            case HandoffShape.Swap:
                first.Code = CodeValue.Create("K2");
                second.Code = CodeValue.Create("K1");
                first.MarkUpdated();
                second.MarkUpdated();
                taker = first;
                break;

            case HandoffShape.NewTakesReleased:
                first.Code = CodeValue.Create("K9");
                first.MarkUpdated();
                taker = new KitEntity
                {
                    KitId = KitIdValue.Create(100),
                    Code = CodeValue.Create("K1"),
                    Name = NameValue.Create("new100"),
                };
                taker.MarkAdded();
                break;

            case HandoffShape.GoneInsertTakesReleased:
                await _fixture.ExecuteAsync("DELETE FROM kits WHERE kit_id = 2", Ct);
                first.Code = CodeValue.Create("K9");
                first.MarkUpdated();
                second.Code = CodeValue.Create("K1");
                second.MarkUpdated();
                taker = second;
                insertWhenUpdateMissing = true;
                break;

            case HandoffShape.PartMovedOffReleased:
            {
                var moved = (await parts.GetByIdAsync(PartIdValue.Create(10), Ct))!;
                first.Code = CodeValue.Create("K9");
                first.MarkUpdated();
                second.MarkUpdated();
                moved.KitCode = CodeValue.Create("K2");
                moved.MarkUpdated();
                second.KitParts.Add(moved);
                taker = second;
                break;
            }

            case HandoffShape.GonePartThenTaker:
            {
                var third = (await kits.GetByIdAsync(KitIdValue.Create(3), Ct))!;
                var gone = (await parts.GetByIdAsync(PartIdValue.Create(10), Ct))!;
                await _fixture.ExecuteAsync("DELETE FROM kit_parts WHERE part_id = 10", Ct);
                second.MarkUpdated();
                gone.MarkUpdated();
                second.KitParts.Add(gone);
                first.Code = CodeValue.Create("K3");
                first.MarkUpdated();
                third.Code = CodeValue.Create("K9");
                third.MarkUpdated();
                taker = first;
                fixedRoots = [second, first, third];
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(handoffCase));
        }

        KitEntity[] roots = handoffCase.Shape switch
        {
            _ when fixedRoots is not null => fixedRoots,
            HandoffShape.Swap => [first, second],
            _ when handoffCase.TakerFirst => [taker, first],
            _ => [first, taker],
        };
        var outcome = await RandomSaveOutcomeAsync(() =>
            kits.SaveAsync(
                roots,
                insertWhenUpdateMissing: insertWhenUpdateMissing,
                mode: handoffCase.Mode,
                cancellationToken: Ct
            )
        );

        return [outcome, .. await KitsStateAsync()];
    }
}
