using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>キーと表名の形（SQL Server の系統の 2 者比較の図の親子 3 組）</summary>
public enum SqlServerKeyShape
{
    /// <summary>文字列の主キー＝GuidKey の値オブジェクト（<c>vouchers</c>・<c>voucher_uses</c>）</summary>
    GuidKey,

    /// <summary><c>binary(4)</c> の主キー（<c>badges</c>・<c>badge_marks</c>。長さちょうどのキー）</summary>
    BinaryKey,

    /// <summary><c>binary(4)</c> の主キーに短い値を書く（SQL Server は 0x00 で詰めて保存する）</summary>
    BinaryShortKey,

    /// <summary>ドットを含む表名（<c>dbo.zones</c>・<c>dbo.zone_notes</c>）</summary>
    DottedTable,
}

/// <summary>キーと表名の形のケースが使う面</summary>
public enum SqlServerKeyShapeSurface
{
    /// <summary>リモート面の手（CRUD・グラフ保存。リモートの EF Core のサーバーでも流す）</summary>
    Remote,

    /// <summary>全機能面の手（クエリ・Include・BulkInsert）</summary>
    Full,

    /// <summary>親を子ごと消すグラフ保存（リモートの EF Core のサーバーでも流す）</summary>
    Cascade,
}

/// <summary>キーと表名の形のケース（名前は <c>key-shape-{形}[-full|-cascade]</c>）</summary>
public sealed record SqlServerKeyShapeCase(
    SqlServerKeyShape Shape,
    SqlServerKeyShapeSurface Surface
)
{
    /// <summary>ケースの名前</summary>
    public string Name =>
        "key-shape-"
        + Shape switch
        {
            SqlServerKeyShape.GuidKey => "guidkey",
            SqlServerKeyShape.BinaryKey => "binary",
            SqlServerKeyShape.BinaryShortKey => "binary-short-key",
            _ => "dotted",
        }
        + Surface switch
        {
            SqlServerKeyShapeSurface.Full => "-full",
            SqlServerKeyShapeSurface.Cascade => "-cascade",
            _ => string.Empty,
        };

    /// <summary>全部のケース（短い binary のキーは全機能面の 1 つだけ）</summary>
    public static IReadOnlyList<SqlServerKeyShapeCase> All { get; } =
    [
        .. new[]
        {
            SqlServerKeyShape.GuidKey,
            SqlServerKeyShape.BinaryKey,
            SqlServerKeyShape.DottedTable,
        }.SelectMany(shape =>
            Enum.GetValues<SqlServerKeyShapeSurface>()
                .Select(surface => new SqlServerKeyShapeCase(shape, surface))
        ),
        new(SqlServerKeyShape.BinaryShortKey, SqlServerKeyShapeSurface.Full),
    ];

    /// <summary>名前のケース（無ければ null）</summary>
    public static SqlServerKeyShapeCase? Find(string name) =>
        All.SingleOrDefault(keyShapeCase => keyShapeCase.Name == name);
}

public sealed partial class BackendParitySqlServerRuntimeTests
{
    /// <summary>キーと表名の形のケースを流す実装先</summary>
    private enum KeyShapeHost
    {
        Ado,
        EfCore,
        RemoteEfCore,
        InMemory,
    }

    /// <summary>
    /// キーと表名の形のケースを、空のスキーマ（インメモリは空のストア）から流して行を返す（手ごとの戻り値と、最後の保存先の全行）
    /// </summary>
    private async Task<IReadOnlyList<string>> KeyShapeAsync(
        SqlServerKeyShapeCase keyShapeCase,
        KeyShapeHost host
    )
    {
        if (host is KeyShapeHost.RemoteEfCore)
        {
            await _fixture.ResetSchemaAsync(Ct);
            await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);
            await using var remote = await CompanionHost.RemoteEfCoreAsync(
                _fixture.ConnectionString,
                []
            );

            return await RunKeyShapeAsync(keyShapeCase, remote.Client, fullSurface: false);
        }

        await using var provider = host switch
        {
            KeyShapeHost.InMemory => new ServiceCollection()
                .AddGeneratedInMemoryRepositories(seedSampleData: false)
                .BuildServiceProvider(),
            _ => Build(efCore: host is KeyShapeHost.EfCore, withHooks: false),
        };

        return await RunKeyShapeAsync(keyShapeCase, provider, fullSurface: true);
    }

    private static Task<IReadOnlyList<string>> RunKeyShapeAsync(
        SqlServerKeyShapeCase keyShapeCase,
        IServiceProvider services,
        bool fullSurface
    ) =>
        keyShapeCase.Shape switch
        {
            SqlServerKeyShape.GuidKey => Vouchers.RunAsync(
                keyShapeCase.Surface,
                services,
                fullSurface
            ),
            SqlServerKeyShape.BinaryKey => Badges.RunAsync(
                keyShapeCase.Surface,
                services,
                fullSurface
            ),
            SqlServerKeyShape.BinaryShortKey => ShortBadges.RunAsync(
                keyShapeCase.Surface,
                services,
                fullSurface
            ),
            _ => Zones.RunAsync(keyShapeCase.Surface, services, fullSurface),
        };

    /// <summary>
    /// キーと表名の形のケースの QuickER 版の行（ほかの実装先は基準と比べる）。短い binary のキーは、SQL Server が 0x00 で詰めて
    /// 保存した値で読み戻る（docs のインメモリの節「実 DB との既知の乖離」）
    /// </summary>
    private static IReadOnlyList<string> KeyShapeExpectedLines(SqlServerKeyShapeCase keyShapeCase)
    {
        var (parentTable, childTable, keyText) = keyShapeCase.Shape switch
        {
            SqlServerKeyShape.GuidKey => (
                "vouchers",
                "voucher_uses",
                (Func<int, string>)VoucherKeyText
            ),
            SqlServerKeyShape.BinaryKey => (
                "badges",
                "badge_marks",
                key => "FBFFBF" + key.ToString("X2", CultureInfo.InvariantCulture)
            ),
            SqlServerKeyShape.BinaryShortKey => (
                "badges",
                "badge_marks",
                key => key.ToString("X2", CultureInfo.InvariantCulture) + "000000"
            ),
            _ => ("dbo.zones", "dbo.zone_notes", key => key.ToString(CultureInfo.InvariantCulture)),
        };

        string Parent(int key, string text) => $"{parentTable}/{keyText(key)} text={text}";

        string Child(int id, int parent, string text) =>
            $"{childTable}/{id} parent={keyText(parent)} text={text}";

        return keyShapeCase.Surface switch
        {
            SqlServerKeyShapeSurface.Remote =>
            [
                $"get {Parent(1, "a")}",
                "get-missing null",
                "update-loaded updated=True",
                "update-detached updated=True",
                "update-missing updated=False",
                "save-new-graph saved=2 parent=Unchanged child=Unchanged",
                $"save-edit-graph saved=3 parent=Unchanged removed=Removed children=[{childTable}/11:Unchanged]",
                "delete-child deleted=True",
                "delete-parent deleted=True",
                .. keyShapeCase.Shape == SqlServerKeyShape.GuidKey
                    ? ["generated-key length=36 found=True deleted=True"]
                    : Array.Empty<string>(),
                Parent(1, "a3"),
                Parent(3, "c"),
                Child(30, 3, "c"),
            ],
            SqlServerKeyShapeSurface.Full =>
            [
                $"get {Parent(1, "a")}",
                $"query-equal {Parent(1, "a")}",
                $"query-in {Parent(1, "a")}|{Parent(2, "b")}",
                $"include-children {Parent(1, "a")} children=[{childTable}/10,{childTable}/11]",
                $"include-parent {childTable}/10->{parentTable}/{keyText(1)}|{childTable}/11->{parentTable}/{keyText(1)}"
                    + $"|{childTable}/20->{parentTable}/{keyText(2)}",
                "bulk-insert inserted=2",
                Parent(1, "a"),
                Parent(2, "b"),
                Parent(5, "bulk"),
                Parent(6, "bulk"),
                Child(10, 1, "x"),
                Child(11, 1, "y"),
                Child(20, 2, "z"),
            ],
            _ =>
            [
                "remove-parent-graph saved=3 children=2 parent=Removed",
                Parent(2, "b"),
                Child(20, 2, "z"),
            ],
        };
    }

    /// <summary>GuidKey の親のキー（番号から決まる GUID の文字列）</summary>
    private static string VoucherKeyText(int key) =>
        $"00000000-0000-4000-8000-{key.ToString("D12", CultureInfo.InvariantCulture)}";

    private static readonly SqlServerKeyShapeBinding<
        VoucherEntity,
        VoucherIdValue,
        VoucherUseEntity,
        VoucherUseIdValue,
        IVoucherRemoteRepository,
        IVoucherUseRemoteRepository,
        IVoucherRepository,
        IVoucherUseRepository
    > Vouchers = new()
    {
        ParentTable = "vouchers",
        ChildTable = "voucher_uses",
        ParentKey = key => VoucherIdValue.Create(VoucherKeyText(key)),
        ChildKey = VoucherUseIdValue.Create,
        ParentKeyText = k => k.Value,
        KeyOfParent = p => p.VoucherId,
        KeyOfChild = c => c.VoucherUseId.Value,
        ParentOfChild = c => c.VoucherId,
        NewParent = (key, text) =>
            new VoucherEntity { VoucherId = key, VoucherLabel = VoucherLabelValue.Create(text) },
        NewChild = (child, parent, text) =>
            new VoucherUseEntity
            {
                VoucherUseId = child,
                VoucherId = parent,
                VoucherUseText = VoucherUseTextValue.Create(text),
            },
        TextOfParent = p => p.VoucherLabel?.Value,
        TextOfChild = c => c.VoucherUseText?.Value,
        SetParentText = (p, text) => p.VoucherLabel = VoucherLabelValue.Create(text),
        Children = p => p.VoucherUses,
        ParentNavigation = c => c.Voucher,
        KeyEquals = key => p => p.VoucherId == key,
        KeyIn = keys => p => keys.Contains(p.VoucherId),
        IncludeChildrenOf = q => q.Include(p => p.VoucherUses),
        IncludeParentOf = q => q.Include(c => c.Voucher),
        GeneratedKey = VoucherIdValue.Create,
        RebuildKey = VoucherIdValue.Create,
    };

    private static readonly SqlServerKeyShapeBinding<
        BadgeEntity,
        BadgeHashValue,
        BadgeMarkEntity,
        BadgeMarkIdValue,
        IBadgeRemoteRepository,
        IBadgeMarkRemoteRepository,
        IBadgeRepository,
        IBadgeMarkRepository
    > Badges = BadgeBinding(key => [0xFB, 0xFF, 0xBF, (byte)key]);

    /// <summary>短い binary のキー（1 バイト＝SQL Server は 0x00 を 3 つ足して保存する）</summary>
    private static readonly SqlServerKeyShapeBinding<
        BadgeEntity,
        BadgeHashValue,
        BadgeMarkEntity,
        BadgeMarkIdValue,
        IBadgeRemoteRepository,
        IBadgeMarkRemoteRepository,
        IBadgeRepository,
        IBadgeMarkRepository
    > ShortBadges = BadgeBinding(key => [(byte)key]);

    private static SqlServerKeyShapeBinding<
        BadgeEntity,
        BadgeHashValue,
        BadgeMarkEntity,
        BadgeMarkIdValue,
        IBadgeRemoteRepository,
        IBadgeMarkRemoteRepository,
        IBadgeRepository,
        IBadgeMarkRepository
    > BadgeBinding(Func<int, byte[]> bytes) =>
        new()
        {
            ParentTable = "badges",
            ChildTable = "badge_marks",
            ParentKey = key => BadgeHashValue.Create(bytes(key)),
            ChildKey = BadgeMarkIdValue.Create,
            ParentKeyText = k => Convert.ToHexString(k.Value),
            KeyOfParent = p => p.BadgeHash,
            KeyOfChild = c => c.BadgeMarkId.Value,
            ParentOfChild = c => c.BadgeHash,
            NewParent = (key, text) =>
                new BadgeEntity { BadgeHash = key, BadgeTitle = BadgeTitleValue.Create(text) },
            NewChild = (child, parent, text) =>
                new BadgeMarkEntity
                {
                    BadgeMarkId = child,
                    BadgeHash = parent,
                    BadgeMarkText = BadgeMarkTextValue.Create(text),
                },
            TextOfParent = p => p.BadgeTitle?.Value,
            TextOfChild = c => c.BadgeMarkText?.Value,
            SetParentText = (p, text) => p.BadgeTitle = BadgeTitleValue.Create(text),
            Children = p => p.BadgeMarks,
            ParentNavigation = c => c.Badge,
            KeyEquals = key => p => p.BadgeHash == key,
            KeyIn = keys => p => keys.Contains(p.BadgeHash),
            IncludeChildrenOf = q => q.Include(p => p.BadgeMarks),
            IncludeParentOf = q => q.Include(c => c.Badge),
        };

    private static readonly SqlServerKeyShapeBinding<
        DboZoneEntity,
        ZoneIdValue,
        DboZoneNoteEntity,
        ZoneNoteIdValue,
        IDboZoneRemoteRepository,
        IDboZoneNoteRemoteRepository,
        IDboZoneRepository,
        IDboZoneNoteRepository
    > Zones = new()
    {
        ParentTable = "dbo.zones",
        ChildTable = "dbo.zone_notes",
        ParentKey = ZoneIdValue.Create,
        ChildKey = ZoneNoteIdValue.Create,
        ParentKeyText = k => k.Value.ToString(CultureInfo.InvariantCulture),
        KeyOfParent = p => p.ZoneId,
        KeyOfChild = c => c.ZoneNoteId.Value,
        ParentOfChild = c => c.ZoneId,
        NewParent = (key, text) =>
            new DboZoneEntity { ZoneId = key, ZoneName = ZoneNameValue.Create(text) },
        NewChild = (child, parent, text) =>
            new DboZoneNoteEntity
            {
                ZoneNoteId = child,
                ZoneId = parent,
                ZoneNoteBody = ZoneNoteBodyValue.Create(text),
            },
        TextOfParent = p => p.ZoneName?.Value,
        TextOfChild = c => c.ZoneNoteBody?.Value,
        SetParentText = (p, text) => p.ZoneName = ZoneNameValue.Create(text),
        Children = p => p.DboZoneNotes,
        ParentNavigation = c => c.DboZone,
        KeyEquals = key => p => p.ZoneId == key,
        KeyIn = keys => p => keys.Contains(p.ZoneId),
        IncludeChildrenOf = q => q.Include(p => p.DboZoneNotes),
        IncludeParentOf = q => q.Include(c => c.DboZone),
    };
}

/// <summary>
/// SQL Server の系統の 1 つの形の親子の型・キーの作り方・行の書式と、ケースの手の並び（実装先どうしの一致の
/// <see cref="KeyShapeBinding{TParent, TParentKey, TChild, TChildKey}"/> と同じ手を、ConcurrencyFixture の型で流す。
/// 生成物の型はフィクスチャごとの名前空間で別の型なので、束縛を共有できない）
/// </summary>
internal sealed class SqlServerKeyShapeBinding<
    TParent,
    TParentKey,
    TChild,
    TChildKey,
    TRemoteParents,
    TRemoteChildren,
    TFullParents,
    TFullChildren
>
    where TParent : EntityBaseCore, new()
    where TChild : EntityBaseCore, new()
    where TRemoteParents : IRemoteRepository<TParent, TParentKey>
    where TRemoteChildren : IRemoteRepository<TChild, TChildKey>
    where TFullParents : IRepository<TParent, TParentKey>
    where TFullChildren : IRepository<TChild, TChildKey>
{
    private static System.Threading.CancellationToken Ct =>
        Xunit.TestContext.Current.CancellationToken;

    public required string ParentTable { get; init; }
    public required string ChildTable { get; init; }
    public required Func<int, TParentKey> ParentKey { get; init; }
    public required Func<int, TChildKey> ChildKey { get; init; }
    public required Func<TParentKey, string> ParentKeyText { get; init; }
    public required Func<TParent, TParentKey?> KeyOfParent { get; init; }
    public required Func<TChild, int> KeyOfChild { get; init; }
    public required Func<TChild, TParentKey?> ParentOfChild { get; init; }
    public required Func<TParentKey, string, TParent> NewParent { get; init; }
    public required Func<TChildKey, TParentKey, string, TChild> NewChild { get; init; }
    public required Func<TParent, string?> TextOfParent { get; init; }
    public required Func<TChild, string?> TextOfChild { get; init; }
    public required Action<TParent, string> SetParentText { get; init; }
    public required Func<TParent, ICollection<TChild>> Children { get; init; }
    public required Func<TChild, TParent?> ParentNavigation { get; init; }
    public required Func<TParentKey, Expression<Func<TParent, bool>>> KeyEquals { get; init; }
    public required Func<List<TParentKey>, Expression<Func<TParent, bool>>> KeyIn { get; init; }
    public required Func<
        SqlQuery<TParent>,
        IncludableSqlQuery<TParent, TChild>
    > IncludeChildrenOf { get; init; }
    public required Func<
        SqlQuery<TChild>,
        IncludableSqlQuery<TChild, TParent>
    > IncludeParentOf { get; init; }

    /// <summary>引数なしで新しいキーを作る（GuidKey だけ）</summary>
    public Func<TParentKey>? GeneratedKey { get; init; }

    /// <summary>キーの文字列から新しいキーのインスタンスを作り直す（GuidKey だけ）</summary>
    public Func<string, TParentKey>? RebuildKey { get; init; }

    private string ParentKeyOf(TParent parent) =>
        $"{ParentTable}/{(KeyOfParent(parent) is { } key ? ParentKeyText(key) : "null")}";

    private string ChildKeyOf(TChild child) =>
        $"{ChildTable}/{KeyOfChild(child).ToString(CultureInfo.InvariantCulture)}";

    private string ParentRow(TParent parent) =>
        $"{ParentKeyOf(parent)} text={TextOfParent(parent)}";

    private string ChildRow(TChild child) =>
        $"{ChildKeyOf(child)} parent={(ParentOfChild(child) is { } key ? ParentKeyText(key) : "null")} "
        + $"text={TextOfChild(child)}";

    /// <summary>面の手の並びを流して行を返す（手ごとの戻り値と、最後の保存先の全行）</summary>
    public async Task<IReadOnlyList<string>> RunAsync(
        SqlServerKeyShapeSurface surface,
        IServiceProvider services,
        bool fullSurface
    )
    {
        var parents = services.GetRequiredService<TRemoteParents>();
        var children = services.GetRequiredService<TRemoteChildren>();
        var lines = new List<string>();

        async Task Step(string label, Func<Task<string>> call)
        {
            try
            {
                lines.Add($"{label} {await call()}");
            }
            catch (Exception ex)
            {
                lines.Add($"{label} throws {ex.GetType().Name}");
            }
        }

        await parents.InsertAsync(NewParent(ParentKey(1), "a"), Ct);
        await parents.InsertAsync(NewParent(ParentKey(2), "b"), Ct);
        await children.InsertAsync(NewChild(ChildKey(10), ParentKey(1), "x"), Ct);

        switch (surface)
        {
            case SqlServerKeyShapeSurface.Remote:
            {
                await Step(
                    "get",
                    async () =>
                        await parents.GetByIdAsync(ParentKey(1), Ct) is { } stored
                            ? ParentRow(stored)
                            : "null"
                );
                await Step(
                    "get-missing",
                    async () =>
                        await parents.GetByIdAsync(ParentKey(9), Ct) is { } stored
                            ? ParentRow(stored)
                            : "null"
                );
                await Step(
                    "update-loaded",
                    async () =>
                    {
                        var parent = (await parents.GetByIdAsync(ParentKey(1), Ct))!;
                        SetParentText(parent, "a2");
                        return $"updated={await parents.UpdateAsync(parent, cancellationToken: Ct)}";
                    }
                );
                await Step(
                    "update-detached",
                    async () =>
                        $"updated={await parents.UpdateAsync(NewParent(ParentKey(2), "b2"), cancellationToken: Ct)}"
                );
                await Step(
                    "update-missing",
                    async () =>
                        $"updated={await parents.UpdateAsync(NewParent(ParentKey(9), "z"), cancellationToken: Ct)}"
                );
                await Step(
                    "save-new-graph",
                    async () =>
                    {
                        var parent = NewParent(ParentKey(3), "c");
                        parent.MarkAdded();
                        var child = NewChild(ChildKey(30), ParentKey(3), "c");
                        child.MarkAdded();
                        Children(parent).Add(child);
                        var saved = await parents.SaveAsync(parent, cancellationToken: Ct);
                        return $"saved={saved} parent={parent.RowState} child={child.RowState}";
                    }
                );
                await Step(
                    "save-edit-graph",
                    async () =>
                    {
                        var parent = (await parents.GetByIdAsync(ParentKey(1), Ct))!;
                        var removed = (await children.GetByIdAsync(ChildKey(10), Ct))!;
                        Children(parent).Add(removed);
                        SetParentText(parent, "a3");
                        parent.MarkUpdated();
                        removed.MarkRemoved();
                        var added = NewChild(ChildKey(11), ParentKey(1), "a3");
                        added.MarkAdded();
                        Children(parent).Add(added);
                        var saved = await parents.SaveAsync(parent, cancellationToken: Ct);
                        var left = string.Join(
                            ",",
                            Children(parent)
                                .Select(c => $"{ChildKeyOf(c)}:{c.RowState}")
                                .Order(StringComparer.Ordinal)
                        );
                        return $"saved={saved} parent={parent.RowState} removed={removed.RowState} children=[{left}]";
                    }
                );
                await Step(
                    "delete-child",
                    async () => $"deleted={await children.DeleteAsync(ChildKey(11), Ct)}"
                );
                await Step(
                    "delete-parent",
                    async () => $"deleted={await parents.DeleteAsync(ParentKey(2), Ct)}"
                );

                if (GeneratedKey is not null && RebuildKey is not null)
                {
                    await Step(
                        "generated-key",
                        async () =>
                        {
                            var key = GeneratedKey();
                            await parents.InsertAsync(NewParent(key, "g"), Ct);
                            var text = ParentKeyText(key);
                            var reread = await parents.GetByIdAsync(RebuildKey(text), Ct);
                            var deleted = await parents.DeleteAsync(RebuildKey(text), Ct);
                            return $"length={text.Length} found={reread is not null} deleted={deleted}";
                        }
                    );
                }

                break;
            }

            case SqlServerKeyShapeSurface.Full:
            {
                var fullParents = services.GetRequiredService<TFullParents>();
                var fullChildren = services.GetRequiredService<TFullChildren>();
                await children.InsertAsync(NewChild(ChildKey(11), ParentKey(1), "y"), Ct);
                await children.InsertAsync(NewChild(ChildKey(20), ParentKey(2), "z"), Ct);
                await Step(
                    "get",
                    async () =>
                        await parents.GetByIdAsync(ParentKey(1), Ct) is { } stored
                            ? ParentRow(stored)
                            : "null"
                );
                await Step(
                    "query-equal",
                    async () =>
                        string.Join(
                            "|",
                            (
                                await fullParents
                                    .Query()
                                    .Where(KeyEquals(ParentKey(1)))
                                    .ToListAsync(Ct)
                            )
                                .Select(ParentRow)
                                .Order(StringComparer.Ordinal)
                        )
                );
                await Step(
                    "query-in",
                    async () =>
                        string.Join(
                            "|",
                            (
                                await fullParents
                                    .Query()
                                    .Where(KeyIn([ParentKey(1), ParentKey(2)]))
                                    .ToListAsync(Ct)
                            )
                                .Select(ParentRow)
                                .Order(StringComparer.Ordinal)
                        )
                );
                await Step(
                    "include-children",
                    async () =>
                        string.Join(
                            "|",
                            (
                                await IncludeChildrenOf(
                                        fullParents.Query().Where(KeyEquals(ParentKey(1)))
                                    )
                                    .ToListAsync(Ct)
                            )
                                .Select(p =>
                                    $"{ParentRow(p)} children=[{string.Join(",", Children(p).Select(ChildKeyOf).Order(StringComparer.Ordinal))}]"
                                )
                                .Order(StringComparer.Ordinal)
                        )
                );
                await Step(
                    "include-parent",
                    async () =>
                        string.Join(
                            "|",
                            (await IncludeParentOf(fullChildren.Query()).ToListAsync(Ct))
                                .Select(c =>
                                    $"{ChildKeyOf(c)}->{(ParentNavigation(c) is { } parent ? ParentKeyOf(parent) : "null")}"
                                )
                                .Order(StringComparer.Ordinal)
                        )
                );
                await Step(
                    "bulk-insert",
                    async () =>
                        $"inserted={await fullParents.BulkInsertAsync([NewParent(ParentKey(5), "bulk"), NewParent(ParentKey(6), "bulk")], Ct)}"
                );
                break;
            }

            case SqlServerKeyShapeSurface.Cascade:
            {
                await children.InsertAsync(NewChild(ChildKey(11), ParentKey(1), "y"), Ct);
                await children.InsertAsync(NewChild(ChildKey(20), ParentKey(2), "z"), Ct);
                await Step(
                    "remove-parent-graph",
                    async () =>
                    {
                        var parent = (await parents.GetByIdAsync(ParentKey(1), Ct))!;
                        var parentKeyText = ParentKeyText(ParentKey(1));

                        foreach (
                            var child in (await children.GetAllAsync(Ct)).Where(c =>
                                ParentOfChild(c) is { } key && ParentKeyText(key) == parentKeyText
                            )
                        )
                        {
                            Children(parent).Add(child);
                        }

                        var count = Children(parent).Count;
                        parent.MarkRemoved();
                        var saved = await parents.SaveAsync(parent, cancellationToken: Ct);
                        return $"saved={saved} children={count} parent={parent.RowState}";
                    }
                );
                break;
            }
        }

        // 保存先の全行（親の表・子の表の順、それぞれキーの文字列の序数順）
        lines.AddRange(
            (await parents.GetAllAsync(Ct)).Select(ParentRow).Order(StringComparer.Ordinal)
        );
        lines.AddRange(
            (await children.GetAllAsync(Ct)).Select(ChildRow).Order(StringComparer.Ordinal)
        );

        return lines;
    }
}
