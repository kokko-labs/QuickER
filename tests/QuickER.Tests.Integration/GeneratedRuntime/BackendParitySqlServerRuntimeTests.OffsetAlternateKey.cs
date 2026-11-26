using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 日時のオフセットを持つ代替キー（ConcurrencyFixture の <c>slots.starts_at</c>＝datetimeoffset(7)）のオフセットだけを変える保存
/// （SQL Server の系統・3 回目の効果測定の HB6）
/// </summary>
/// <remarks>
/// <c>DateTimeOffset</c> の等値は瞬間だけを比べるので、同じ瞬間でオフセットだけが違う値は等しいとみなされる。列はオフセットを持つので、QuickER 版は
/// UPDATE で新しいオフセットを書く。EF Core 版は代替キーを自分で書く（<c>WriteOwnColumnsAsync</c>）ときに保存先の値と瞬間だけで比べ、何も書かずに
/// 成功を返していた。軸：保存の経路（UpdateAsync・グラフ保存）。値オブジェクトの列（ConcurrencyFixture は値オブジェクトあり）を通るので、
/// 値オブジェクトの中の DateTimeOffset の比べ方も同じケースが見る。
/// </remarks>
public sealed partial class BackendParitySqlServerRuntimeTests
{
    /// <summary>オフセットだけを変える保存のケースの名前（保存の経路ごと）</summary>
    private static IEnumerable<string> OffsetOnlyAlternateKeyCaseNames =>
        ["alternate-key-offset-only-update", "alternate-key-offset-only-graph-save"];

    /// <summary>
    /// オフセットだけを変える保存の基準の行（新しいオフセットで保存され、続く保存も通る。式の本体で返す＝別の部分ファイルの静的な初期化の順に
    /// 依らない）
    /// </summary>
    private static string[] OffsetOnlySaved =>
        [
            "rows=1",
            "slots/1 starts_at=2024-01-01T01:00:00.0000000+00:00 label=s1",
            "again rows=1",
            "slots/1 starts_at=2024-01-01T01:00:00.0000000+00:00 label=again",
        ];

    /// <summary>
    /// slot 1（10:00+09:00）を読み、同じ瞬間の 01:00+00:00 へ変えて保存し、保存先のオフセットを並べる。続けて同じ値で保存し直す
    /// </summary>
    private async Task<IReadOnlyList<string>> OffsetOnlyAlternateKeyAsync(
        bool efCore,
        bool graphSave
    )
    {
        await using var provider = Build(efCore, withHooks: false);
        var slots = provider.GetRequiredService<ISlotRepository>();
        var start = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.FromHours(9));
        await slots.InsertAsync(
            new SlotEntity
            {
                SlotId = SlotIdValue.Create(1),
                StartsAt = StartsAtValue.Create(start),
                SlotLabel = SlotLabelValue.Create("s1"),
            },
            Ct
        );

        var slot = (await slots.GetByIdAsync(SlotIdValue.Create(1), Ct))!;
        slot.StartsAt = StartsAtValue.Create(start.ToOffset(TimeSpan.Zero));
        slot.MarkUpdated();

        Task<int> Save() =>
            graphSave
                ? slots.SaveAsync(slot, cancellationToken: Ct)
                : UpdateAsInt(slots.UpdateAsync(slot, ConcurrencyMode.Optimistic, Ct));

        List<string> lines = [await OutcomeAsync(Save), .. await SlotsStateAsync()];
        slot.SlotLabel = SlotLabelValue.Create("again");
        slot.MarkUpdated();
        lines.Add($"again {await OutcomeAsync(Save)}");
        lines.AddRange(await SlotsStateAsync());

        return lines;
    }

    /// <summary>保存先の slots の全行（キー順。starts_at はオフセットつきの往復の書式）</summary>
    private async Task<IReadOnlyList<string>> SlotsStateAsync()
    {
        await using var provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();
        var slots = await provider.GetRequiredService<ISlotRepository>().GetAllAsync(Ct);

        return
        [
            .. slots
                .OrderBy(s => s.SlotId.Value)
                .Select(s =>
                    $"slots/{s.SlotId.Value} starts_at={s.StartsAt.Value.ToString("o", CultureInfo.InvariantCulture)} label={s.SlotLabel?.Value}"
                ),
        ];
    }
}
