using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EF Core の SaveChanges が書く列を持たない表（キーの列と DB が値を作る列だけ）の更新（SQL Server の系統）。EF Core は
/// そうした行に UPDATE を送らないので、EF Core 版 Repository が自分で書く。行の有無・版の照合・保存の後の版が QuickER 版と
/// そろうことを、QuickER 版・EF Core・リモート（EF Core のサーバー）で比べる。
/// </summary>
/// <remarks>
/// 軸は 3 つ：表の形（<see cref="OwnWriteShape"/>）× 読んだ後に他者がしたこと（<see cref="OwnWriteCondition"/>）× 保存の経路
/// （<see cref="OwnWritePath"/>）。キーだけの表は版を持たないので、版を進める他者の変更は起こり得ない（その組だけ外す）。
/// </remarks>
public sealed partial class BackendParitySqlServerRuntimeTests
{
    /// <summary>表の形</summary>
    private enum OwnWriteShape
    {
        /// <summary><c>tags</c>：キー・代替キー（code を変える）・版</summary>
        AlternateKeyAndVersion,

        /// <summary><c>stamps</c>：キーと版だけ（何も変えずに更新する）</summary>
        KeyAndVersion,

        /// <summary><c>markers</c>：キーだけ（何も変えずに更新する）</summary>
        KeyOnly,
    }

    /// <summary>読んだ後に他者がしたこと</summary>
    private enum OwnWriteCondition
    {
        /// <summary>何もしていない</summary>
        Fresh,

        /// <summary>版を進めた（値は変えない）</summary>
        Stale,

        /// <summary>消した</summary>
        Gone,
    }

    /// <summary>保存の経路</summary>
    private enum OwnWritePath
    {
        /// <summary><c>UpdateAsync</c>（Optimistic）</summary>
        Update,

        /// <summary>グラフ保存（Optimistic）</summary>
        GraphSave,

        /// <summary>グラフ保存（Optimistic ＋ insertWhenUpdateMissing）</summary>
        GraphSaveInsertWhenMissing,
    }

    /// <summary>書く列を持たない表の更新の 1 ケース</summary>
    private sealed record OwnWriteCase(
        OwnWriteShape Shape,
        OwnWriteCondition Condition,
        OwnWritePath Path
    )
    {
        /// <summary>軸の組（キーだけの表の版を進める変更は起こり得ないので外す）</summary>
        public static IReadOnlyList<OwnWriteCase> All { get; } =
        [
            .. from shape in Enum.GetValues<OwnWriteShape>()
            from condition in Enum.GetValues<OwnWriteCondition>()
            from path in Enum.GetValues<OwnWritePath>()
            where !(shape == OwnWriteShape.KeyOnly && condition == OwnWriteCondition.Stale)
            select new OwnWriteCase(shape, condition, path),
        ];

        /// <summary>ケースの名前</summary>
        public string Name =>
            $"own-write-{Kebab(Shape.ToString())}-{Kebab(Condition.ToString())}-{Kebab(Path.ToString())}";

        /// <summary>名前のケース（無ければ null）</summary>
        public static OwnWriteCase? Find(string name) =>
            All.SingleOrDefault(ownWriteCase => ownWriteCase.Name == name);

        private static string Kebab(string pascal) =>
            string.Concat(
                pascal.Select(
                    (c, index) =>
                        char.IsUpper(c) && index > 0
                            ? "-" + char.ToLowerInvariant(c)
                            : char.ToLowerInvariant(c).ToString()
                )
            );
    }

    /// <summary>1 つのケースを空のスキーマから流す（<paramref name="remoteEfCore"/> ならサーバーが EF Core 版 Repository のリモート）</summary>
    private async Task<IReadOnlyList<string>> OwnWriteAsync(
        bool efCore,
        OwnWriteCase ownWriteCase,
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

        return ownWriteCase.Shape switch
        {
            OwnWriteShape.AlternateKeyAndVersion => await OwnWriteRowAsync(
                host.Client.GetRequiredService<ITagRemoteRepository>(),
                new TagEntity { TagId = TagIdValue.Create(1), Code = CodeValue.Create("T1") },
                id => TagIdValue.Create(id),
                tag => tag.Code = CodeValue.Create("T9"),
                tag => tag.RowVer?.Value,
                "UPDATE tags SET code = code WHERE tag_id = 1",
                "DELETE FROM tags WHERE tag_id = 1",
                ownWriteCase
            ),
            OwnWriteShape.KeyAndVersion => await OwnWriteRowAsync(
                host.Client.GetRequiredService<IStampRemoteRepository>(),
                new StampEntity { StampId = StampIdValue.Create(1) },
                id => StampIdValue.Create(id),
                _ => { },
                stamp => stamp.RowVer?.Value,
                "UPDATE stamps SET stamp_id = stamp_id WHERE stamp_id = 1",
                "DELETE FROM stamps WHERE stamp_id = 1",
                ownWriteCase
            ),
            _ => await OwnWriteRowAsync(
                host.Client.GetRequiredService<IMarkerRemoteRepository>(),
                new MarkerEntity { MarkerId = MarkerIdValue.Create(1) },
                id => MarkerIdValue.Create(id),
                _ => { },
                _ => null,
                null,
                "DELETE FROM markers WHERE marker_id = 1",
                ownWriteCase
            ),
        };
    }

    /// <summary>
    /// 行 1 を入れて読み、他者の変更の後で <paramref name="edit"/> を当てて保存する。結果・手元の版が保存先と同じか・保存先を並べ、
    /// 保存できたらもう一度同じ経路（insertWhenUpdateMissing なし）で保存した結果と保存先も並べる
    /// </summary>
    private async Task<IReadOnlyList<string>> OwnWriteRowAsync<TEntity, TKey>(
        IRemoteRepository<TEntity, TKey> repository,
        TEntity seed,
        Func<int, TKey> key,
        Action<TEntity> edit,
        Func<TEntity, byte[]?> version,
        string? staleSql,
        string goneSql,
        OwnWriteCase ownWriteCase
    )
        where TEntity : EntityBaseCore, new()
    {
        await repository.InsertAsync(seed, Ct);
        var entity = (await repository.GetByIdAsync(key(1), Ct))!;

        switch (ownWriteCase.Condition)
        {
            case OwnWriteCondition.Stale:
                await _fixture.ExecuteAsync(staleSql!, Ct);
                break;

            case OwnWriteCondition.Gone:
                await _fixture.ExecuteAsync(goneSql, Ct);
                break;
        }

        edit(entity);
        entity.MarkUpdated();

        Task<int> Save(bool insertWhenUpdateMissing) =>
            ownWriteCase.Path == OwnWritePath.Update
                ? UpdateAsInt(repository.UpdateAsync(entity, ConcurrencyMode.Optimistic, Ct))
                : repository.SaveAsync(
                    entity,
                    insertWhenUpdateMissing: insertWhenUpdateMissing,
                    cancellationToken: Ct
                );

        var outcome = await OutcomeAsync(() =>
            Save(ownWriteCase.Path == OwnWritePath.GraphSaveInsertWhenMissing)
        );
        List<string> lines = [outcome, .. await OwnWriteStateAsync(version(entity))];

        if (outcome == "rows=1")
        {
            entity.MarkUpdated();
            lines.Add($"again {await OutcomeAsync(() => Save(insertWhenUpdateMissing: false))}");
            lines.AddRange(await OwnWriteStateAsync(version(entity)));
        }

        return lines;
    }

    /// <summary>保存先の tags・stamps・markers の行と、手元の版（無ければ null）がどれかの保存先の行の版と同じか</summary>
    private async Task<IReadOnlyList<string>> OwnWriteStateAsync(byte[]? localVersion)
    {
        await using var provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();
        var tags = await provider.GetRequiredService<ITagRepository>().GetAllAsync(Ct);
        var stamps = await provider.GetRequiredService<IStampRepository>().GetAllAsync(Ct);
        var markers = await provider.GetRequiredService<IMarkerRepository>().GetAllAsync(Ct);
        var storedVersions = tags.Select(tag => tag.RowVer.Value)
            .Concat(stamps.Select(stamp => stamp.RowVer.Value))
            .ToList();

        return
        [
            localVersion is null
                ? "version-matches-store=n/a"
                : $"version-matches-store={storedVersions.Any(stored => stored.SequenceEqual(localVersion))}",
            .. tags.OrderBy(tag => tag.TagId.Value)
                .Select(tag => $"tags/{tag.TagId.Value} code={tag.Code.Value}"),
            .. stamps
                .OrderBy(stamp => stamp.StampId.Value)
                .Select(stamp => $"stamps/{stamp.StampId.Value}"),
            .. markers
                .OrderBy(marker => marker.MarkerId.Value)
                .Select(marker => $"markers/{marker.MarkerId.Value}"),
        ];
    }
}
