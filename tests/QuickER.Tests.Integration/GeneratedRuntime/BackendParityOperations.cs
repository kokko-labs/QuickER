using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>種まき・挿入に使う 1 行の値（実装先ごとに新しいエンティティを組み立てる）</summary>
public abstract record ParityRow
{
    /// <summary>この値で新しいエンティティを組み立てる（RowState は既定のまま）</summary>
    public abstract EntityBaseCore Build();
}

/// <summary>顧客の行（<paramref name="Referrer"/> は紹介者＝自己参照の親の顧客）</summary>
public sealed record CustomerRow(int Id, string Name, string? Note = null, int? Referrer = null)
    : ParityRow
{
    public override EntityBaseCore Build() =>
        new CustomerEntity
        {
            CustomerId = CustomerIdValue.Create(Id),
            Name = NameValue.Create(Name),
            Note = Note is null ? null : NoteValue.Create(Note),
            ReferrerId = Referrer is null ? null : CustomerIdValue.Create(Referrer.Value),
        };
}

/// <summary>
/// 注文の行（除外 blob は挿入で書く値。<c>Thumb</c> は NOT NULL）。<paramref name="UnsetBlobs"/> のときは除外 blob を
/// 未取得状態（<c>Payload</c>＝null・<c>Thumb</c>＝空の値）で組み立てる＝UPDATE の経路へ回す根はこの形にする
/// （QuickER 版は、除外列に値の入った行の UPDATE を断る＝docs に書いてある契約）。
/// </summary>
public sealed record OrderRow(
    int Id,
    int Customer,
    decimal Amount,
    string? Memo = null,
    byte[]? Payload = null,
    byte[]? Thumb = null,
    DateTime? At = null,
    bool Paid = false,
    bool UnsetBlobs = false
) : ParityRow
{
    public override EntityBaseCore Build() =>
        new OrderEntity
        {
            OrderId = OrderIdValue.Create(Id),
            CustomerId = CustomerIdValue.Create(Customer),
            Amount = AmountValue.Create(Amount),
            Memo = Memo is null ? null : MemoValue.Create(Memo),
            OrderedAt = At is null ? null : OrderedAtValue.Create(At.Value),
            IsPaid = IsPaidValue.Create(Paid),
            Payload = UnsetBlobs || Payload is null ? null : PayloadValue.Create(Payload),
            Thumb = ThumbValue.Create(UnsetBlobs ? [] : Thumb ?? [0xEE]),
        };
}

/// <summary>明細の行</summary>
public sealed record LineRow(int Id, int Order, string Item, int Qty, decimal? Discount = null)
    : ParityRow
{
    public override EntityBaseCore Build() =>
        new OrderLineEntity
        {
            LineId = LineIdValue.Create(Id),
            OrderId = OrderIdValue.Create(Order),
            Item = ItemValue.Create(Item),
            Quantity = QuantityValue.Create(Qty),
            Discount = Discount is null ? null : DiscountValue.Create(Discount.Value),
        };
}

/// <summary>
/// 一致テストの 1 つの操作。実装先ごとに同じ順で流し、戻り値（または例外）を文字列で返す。
/// </summary>
/// <remarks>
/// 実装先に無い操作（リモートの <c>Query()</c>・インメモリの生 SQL・EF Core の blob の Stream）は
/// <see cref="Supports"/> が false を返し、その操作を含むシナリオはその実装先で流さない（docs に書いてある違い）。
/// </remarks>
public abstract record ParityOp
{
    /// <summary>違いの署名に使う操作の名前</summary>
    public virtual string Name => GetType().Name.Replace("Op", "", StringComparison.Ordinal);

    /// <summary>この実装先にある操作か</summary>
    public virtual bool Supports(ParityBackendKind kind) => true;

    /// <summary>操作を流し、戻り値を文字列で返す（例外は呼び出し側が整える）</summary>
    public abstract Task<string> RunAsync(ParityBackend backend, CancellationToken ct);

    /// <summary>根の型に合うリモート面のリポジトリで、単一のグラフ保存を呼ぶ</summary>
    protected static Task<int> SaveRootAsync(
        ParityBackend backend,
        EntityBaseCore root,
        ParitySaveOptions options,
        CancellationToken ct
    ) =>
        root switch
        {
            CustomerEntity c => backend.Customers.SaveAsync(
                c,
                options.CascadeSave,
                options.CascadeDelete,
                options.InsertWhenUpdateMissing,
                options.Mode,
                ct
            ),
            OrderEntity o => backend.Orders.SaveAsync(
                o,
                options.CascadeSave,
                options.CascadeDelete,
                options.InsertWhenUpdateMissing,
                options.Mode,
                ct
            ),
            OrderLineEntity l => backend.Lines.SaveAsync(
                l,
                options.CascadeSave,
                options.CascadeDelete,
                options.InsertWhenUpdateMissing,
                options.Mode,
                ct
            ),
            _ => throw new ArgumentException(root.GetType().Name, nameof(root)),
        };

    /// <summary>スロットの根を返す（無ければテストの書き誤り）</summary>
    protected static EntityBaseCore Root(ParityBackend backend, string slot) =>
        backend.Slots.TryGetValue(slot, out var root)
            ? root
            : throw new InvalidOperationException($"Slot '{slot}' is empty.");

    /// <summary>スロットのグラフから、キーが一致する行を探す（根から子への方向）</summary>
    protected static EntityBaseCore Find(ParityBackend backend, string slot, string key)
    {
        var stack = new Stack<EntityBaseCore>([Root(backend, slot)]);

        while (stack.Count > 0)
        {
            var entity = stack.Pop();

            if (ParityFormat.KeyOf(entity) == key)
            {
                return entity;
            }

            foreach (var child in entity.EnumerateCascadeChildren())
            {
                stack.Push(child);
            }
        }

        throw new InvalidOperationException($"'{key}' is not in slot '{slot}'.");
    }
}

/// <summary>グラフ保存の引数</summary>
public sealed record ParitySaveOptions(
    bool CascadeSave = true,
    bool CascadeDelete = true,
    bool InsertWhenUpdateMissing = false,
    ConcurrencyMode Mode = ConcurrencyMode.Optimistic
);

/// <summary>種まき（ほかの書き手が挿入する。比べる対象の操作ではない）</summary>
public sealed record SeedOp(ParityRow Row) : ParityOp
{
    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        switch (Row.Build())
        {
            case CustomerEntity c:
                await backend.Outside.Customers.InsertAsync(c, ct);
                break;
            case OrderEntity o:
                await backend.Outside.Orders.InsertAsync(o, ct);
                break;
            case OrderLineEntity l:
                await backend.Outside.Lines.InsertAsync(l, ct);
                break;
        }

        return "ok";
    }
}

/// <summary>リモート面の <c>InsertAsync</c></summary>
public sealed record InsertOp(ParityRow Row) : ParityOp
{
    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        switch (Row.Build())
        {
            case CustomerEntity c:
                await backend.Customers.InsertAsync(c, ct);
                return ParityFormat.Graph(c)[0];
            case OrderEntity o:
                await backend.Orders.InsertAsync(o, ct);
                return ParityFormat.Graph(o)[0];
            case OrderLineEntity l:
                await backend.Lines.InsertAsync(l, ct);
                return ParityFormat.Graph(l)[0];
            default:
                throw new InvalidOperationException();
        }
    }
}

/// <summary>リモート面の <c>GetByIdAsync</c> で 1 行を読み、スロットへ置く（行が無ければスロットを空にする）</summary>
public sealed record LoadOp(string Slot, string Table, int Id) : ParityOp
{
    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        EntityBaseCore? entity = Table switch
        {
            "customers" => await backend.Customers.GetByIdAsync(CustomerIdValue.Create(Id), ct),
            "orders" => await backend.Orders.GetByIdAsync(OrderIdValue.Create(Id), ct),
            "order_lines" => await backend.Lines.GetByIdAsync(LineIdValue.Create(Id), ct),
            _ => throw new ArgumentException(Table),
        };

        if (entity is null)
        {
            backend.Slots.Remove(Slot);

            return "null";
        }

        backend.Slots[Slot] = entity;

        return "loaded";
    }
}

/// <summary>全機能面で注文を明細ごと Include して読み、スロットへ置く（リモートには無い）</summary>
public sealed record LoadOrderWithLinesOp(string Slot, int Id) : ParityOp
{
    public override bool Supports(ParityBackendKind kind) => kind != ParityBackendKind.Remote;

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var order = await backend
            .FullOrders.Query()
            .Include(o => o.OrderLines)
            .GetByIdAsync(OrderIdValue.Create(Id), ct);

        if (order is null)
        {
            backend.Slots.Remove(Slot);

            return "null";
        }

        // 子をキーの順に並べ直す（Include が返す子の並びは、SQL でも ORDER BY が無ければ決まらず、インメモリは削除の後で
        // 並びが変わる。並びのまま保存するとフックの順序まで変わるので、比べる前に操作の側でそろえる）
        var lines = order.OrderLines.OrderBy(l => l.LineId.Value).ToList();
        order.OrderLines.Clear();

        foreach (var line in lines)
        {
            order.OrderLines.Add(line);
        }

        backend.Slots[Slot] = order;

        return "loaded";
    }
}

/// <summary>明細をリモート面で 1 行読み、スロットの根（注文）のコレクションへ足す（リモートでもグラフを作れる形）</summary>
public sealed record AttachLineOp(string Slot, int LineId) : ParityOp
{
    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var line = await backend.Lines.GetByIdAsync(LineIdValue.Create(LineId), ct);

        if (line is null)
        {
            return "null";
        }

        ((OrderEntity)Root(backend, Slot)).OrderLines.Add(line);

        return "attached";
    }
}

/// <summary>新しいエンティティを組み立て、指定の状態にしてスロットの根に置く</summary>
public sealed record NewRootOp(string Slot, ParityRow Row, RowState State) : ParityOp
{
    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var entity = Row.Build();
        ApplyState(entity, State);
        backend.Slots[Slot] = entity;

        return Task.FromResult("built");
    }

    /// <summary>RowState を公開の Mark 系メソッドで立てる</summary>
    internal static void ApplyState(EntityBaseCore entity, RowState state)
    {
        switch (state)
        {
            case RowState.Added:
                entity.MarkAdded();
                break;
            case RowState.Updated:
                entity.MarkUpdated();
                break;
            case RowState.Removed:
                entity.MarkRemoved();
                break;
            case RowState.Unchanged:
                entity.MarkUnchanged();
                break;
        }
    }
}

/// <summary>新しい明細を Added でスロットの根（注文）のコレクションへ足す</summary>
public sealed record AddLineOp(string Slot, LineRow Row) : ParityOp
{
    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var line = Row.Build();
        line.MarkAdded();
        ((OrderEntity)Root(backend, Slot)).OrderLines.Add((OrderLineEntity)line);

        return Task.FromResult("added");
    }
}

/// <summary>顧客をリモート面で 1 行読み、スロットの根（顧客）の紹介先のコレクションへ足す</summary>
public sealed record AttachChildCustomerOp(string Slot, int CustomerId) : ParityOp
{
    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var child = await backend.Customers.GetByIdAsync(CustomerIdValue.Create(CustomerId), ct);

        if (child is null)
        {
            return "null";
        }

        ((CustomerEntity)Root(backend, Slot)).Customers.Add(child);

        return "attached";
    }
}

/// <summary>新しい顧客を Added でスロットの根（顧客）の紹介先のコレクションへ足す</summary>
public sealed record AddChildCustomerOp(string Slot, CustomerRow Row) : ParityOp
{
    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var child = (CustomerEntity)Row.Build();
        child.MarkAdded();
        ((CustomerEntity)Root(backend, Slot)).Customers.Add(child);

        return Task.FromResult("added");
    }
}

/// <summary>
/// 行を組み立てて指定の状態にし、スロットのグラフの行（<paramref name="ParentKey"/>）の子のコレクションへ足す
/// （顧客の下の顧客・顧客の下の注文・注文の下の明細）。保存先の行を読まずに組み立てるので、付け替えた子の元の親は
/// 保存先にしか無い（リモートや切り離したエンティティの形）
/// </summary>
public sealed record AddChildOp(string Slot, string ParentKey, ParityRow Row, RowState State)
    : ParityOp
{
    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var child = Row.Build();
        NewRootOp.ApplyState(child, State);

        switch (Find(backend, Slot, ParentKey), child)
        {
            case (CustomerEntity parent, CustomerEntity customer):
                parent.Customers.Add(customer);
                break;
            case (CustomerEntity parent, OrderEntity order):
                parent.Orders.Add(order);
                break;
            case (OrderEntity parent, OrderLineEntity line):
                parent.OrderLines.Add(line);
                break;
            default:
                throw new InvalidOperationException(
                    $"'{ParentKey}' cannot hold {child.GetType().Name}."
                );
        }

        return Task.FromResult("added");
    }
}

/// <summary>スロットのグラフの注文のメモを書き換え、Updated にする（Added のままの行は Added のまま）</summary>
public sealed record EditMemoOp(string Slot, int OrderId, string? Memo) : ParityOp
{
    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var order = (OrderEntity)Find(backend, Slot, $"orders/{OrderId}");
        order.Memo = Memo is null ? null : MemoValue.Create(Memo);

        if (order.RowState != RowState.Added)
        {
            order.MarkUpdated();
        }

        return Task.FromResult("edited");
    }
}

/// <summary>スロットのグラフの明細の数量を書き換え、Updated にする</summary>
public sealed record EditQuantityOp(string Slot, int LineId, int Qty) : ParityOp
{
    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var line = (OrderLineEntity)Find(backend, Slot, $"order_lines/{LineId}");
        line.Quantity = QuantityValue.Create(Qty);

        if (line.RowState != RowState.Added)
        {
            line.MarkUpdated();
        }

        return Task.FromResult("edited");
    }
}

/// <summary>スロットのグラフの行に削除の印を付ける</summary>
public sealed record MarkRemovedOp(string Slot, string Key) : ParityOp
{
    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        Find(backend, Slot, Key).MarkRemoved();

        return Task.FromResult("marked");
    }
}

/// <summary>スロットの根を、根の型のリポジトリでグラフ保存する</summary>
public sealed record SaveOp(string Slot, ParitySaveOptions? Options = null) : ParityOp
{
    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct) =>
        $"rows={await SaveRootAsync(backend, Root(backend, Slot), Options ?? new(), ct)}";
}

/// <summary>スロットの中の 1 行（根でなくてよい）を、その型のリポジトリから直接グラフ保存する</summary>
public sealed record SaveMemberOp(string Slot, string Key, ParitySaveOptions? Options = null)
    : ParityOp
{
    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct) =>
        $"rows={await SaveRootAsync(backend, Find(backend, Slot, Key), Options ?? new(), ct)}";
}

/// <summary>複数のスロットの根（すべて注文）を、注文のリポジトリの複数形のグラフ保存で 1 回に保存する</summary>
public sealed record SaveManyOrdersOp(
    IReadOnlyList<string> SlotNames,
    ParitySaveOptions? Options = null
) : ParityOp
{
    public override string Name => "SaveManyOrders";

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var options = Options ?? new();
        var roots = SlotNames.Select(s => (OrderEntity)Root(backend, s)).ToList();
        var rows = await backend.Orders.SaveAsync(
            roots,
            options.CascadeSave,
            options.CascadeDelete,
            options.InsertWhenUpdateMissing,
            options.Mode,
            ct
        );

        return $"rows={rows}";
    }
}

/// <summary>複数のスロットの根（すべて顧客）を、顧客のリポジトリの複数形のグラフ保存で 1 回に保存する</summary>
public sealed record SaveManyCustomersOp(IReadOnlyList<string> SlotNames) : ParityOp
{
    public override string Name => "SaveManyCustomers";

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var roots = SlotNames.Select(s => (CustomerEntity)Root(backend, s)).ToList();
        var rows = await backend.Customers.SaveAsync(roots, cancellationToken: ct);

        return $"rows={rows}";
    }
}

/// <summary>スロットの根（注文）を <c>UpdateAsync</c> で更新する</summary>
public sealed record UpdateOrderOp(string Slot, ConcurrencyMode Mode = ConcurrencyMode.Optimistic)
    : ParityOp
{
    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct) =>
        $"updated={await backend.Orders.UpdateAsync((OrderEntity)Root(backend, Slot), Mode, ct)}";
}

/// <summary>
/// 受け入れた手の差（<see cref="BackendParityKnownDifferences.AcceptedDeviations"/>）を持つ手の印。中の手をそのまま流し、
/// 名前・流せる実装先も中の手のもの（印を付けた手だけが種の指紋の要約で変わる）
/// </summary>
public sealed record DeviatingOp(ParityOp Inner, ParityDeviation Deviation) : ParityOp
{
    public override string Name => Inner.Name;

    public override bool Supports(ParityBackendKind kind) => Inner.Supports(kind);

    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct) =>
        Inner.RunAsync(backend, ct);
}

/// <summary>リモート面の <c>DeleteAsync(id)</c></summary>
public sealed record DeleteOp(string Table, int Id) : ParityOp
{
    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var deleted = Table switch
        {
            "customers" => await backend.Customers.DeleteAsync(CustomerIdValue.Create(Id), ct),
            "orders" => await backend.Orders.DeleteAsync(OrderIdValue.Create(Id), ct),
            "order_lines" => await backend.Lines.DeleteAsync(LineIdValue.Create(Id), ct),
            _ => throw new ArgumentException(Table),
        };

        return $"deleted={deleted}";
    }
}

/// <summary>ほかの書き手が行を消す（同じ保存先・Save フックなし）</summary>
public sealed record OutsideDeleteOp(string Table, int Id) : ParityOp
{
    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var deleted = Table switch
        {
            "customers" => await backend.Outside.Customers.DeleteAsync(
                CustomerIdValue.Create(Id),
                ct
            ),
            "orders" => await backend.Outside.Orders.DeleteAsync(OrderIdValue.Create(Id), ct),
            "order_lines" => await backend.Outside.Lines.DeleteAsync(LineIdValue.Create(Id), ct),
            _ => throw new ArgumentException(Table),
        };

        return $"deleted={deleted}";
    }
}

/// <summary>全機能面の生 SQL で注文を読み、最初の行をスロットへ置く（QuickER 版と EF Core だけ）</summary>
public sealed record QueryOrdersBySqlOp(string Slot, string Sql) : ParityOp
{
    public override bool Supports(ParityBackendKind kind) =>
        kind is ParityBackendKind.Ado or ParityBackendKind.EfCore;

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var rows = await backend.FullOrders.QueryBySqlAsync(Sql, cancellationToken: ct);

        if (rows.Count == 0)
        {
            backend.Slots.Remove(Slot);

            return "rows=0";
        }

        backend.Slots[Slot] = rows[0];

        return $"rows={rows.Count}";
    }
}

/// <summary><c>ExecuteDeleteAsync</c> の条件</summary>
public enum ParityOrderPredicate
{
    /// <summary>列だけを読む条件（<c>amount &gt;= 100</c>）</summary>
    AmountAtLeast100,

    /// <summary>カスケードのナビゲーションを読む条件（<c>OrderLines.Any()</c>）</summary>
    HasLines,
}

/// <summary>リモート面の <c>GetByIdAsync</c> か <c>DeleteAsync</c> を、null のキーで呼ぶ</summary>
public sealed record NullKeyOp(string Table, bool Delete) : ParityOp
{
    public override string Name => $"NullKey:{Table}:{(Delete ? "Delete" : "GetById")}";

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct) =>
        (Table, Delete) switch
        {
            ("customers", false) =>
                $"found={await backend.Customers.GetByIdAsync(null!, ct) is not null}",
            ("customers", true) => $"deleted={await backend.Customers.DeleteAsync(null!, ct)}",
            ("orders", false) =>
                $"found={await backend.Orders.GetByIdAsync(null!, ct) is not null}",
            ("orders", true) => $"deleted={await backend.Orders.DeleteAsync(null!, ct)}",
            _ => throw new ArgumentException(Table),
        };
}

/// <summary>
/// 注文の除外 blob の Stream アクセサ（とファイル糖衣）を、null のキーで呼ぶ（EF Core は Stream アクセサを持たない＝NotSupported）
/// </summary>
public sealed record NullKeyBlobOp(bool Write, bool ViaFile) : ParityOp
{
    public override string Name =>
        $"NullKeyBlob:{(Write ? "Write" : "Read")}{(ViaFile ? ":File" : "")}";

    public override bool Supports(ParityBackendKind kind) => kind != ParityBackendKind.EfCore;

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        // ファイル糖衣は、ファイルに触れる前に断る（書き込み元のファイルが無くても、null のキーを先に報告する）
        var missing = Path.Combine(Path.GetTempPath(), $"quicker-null-key-{Guid.NewGuid():N}.bin");

        return (Write, ViaFile) switch
        {
            (false, false) =>
                $"read={await backend.Orders.ReadPayloadAsync(null!, new MemoryStream(), ct)}",
            (true, false) =>
                $"written={await backend.Orders.WritePayloadAsync(null!, new MemoryStream([1, 2]), null, ct)}",
            (false, true) =>
                $"read={await backend.Orders.ReadPayloadToFileAsync(null!, missing, ct)}",
            (true, true) =>
                $"written={await backend.Orders.WritePayloadFromFileAsync(null!, missing, ct)}",
        };
    }
}

/// <summary>ほかの操作を、取り消し済みのトークンで流す</summary>
public sealed record CanceledOp(ParityOp Inner) : ParityOp
{
    public override string Name => "Canceled:" + Inner.Name;

    public override bool Supports(ParityBackendKind kind) => Inner.Supports(kind);

    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct) =>
        Inner.RunAsync(backend, new CancellationToken(canceled: true));
}

/// <summary>ページングした <c>Query()</c> の終端</summary>
public enum ParityPagedTerminal
{
    /// <summary><c>ToListAsync</c>（読んだ行の数を返す）</summary>
    Fetch,

    /// <summary><c>CountAsync</c></summary>
    Count,

    /// <summary><c>AnyAsync</c></summary>
    Any,

    /// <summary><c>ExecuteDeleteAsync</c>（カスケードなし）</summary>
    Delete,

    /// <summary><c>ExecuteDeleteAsync</c>（カスケードあり）</summary>
    CascadeDelete,
}

/// <summary>
/// 全機能面の <c>Query()</c> に、条件（<paramref name="MinAmount"/> 以上の金額）・注文 ID の昇順・Skip・Take を当てて、
/// 終端を呼ぶ（リモートには無い）
/// </summary>
public sealed record PagedOrdersOp(
    ParityPagedTerminal Terminal,
    int? Skip = null,
    int? Take = null,
    decimal? MinAmount = null
) : ParityOp
{
    public override string Name =>
        $"PagedOrders:{Terminal}:skip={Skip?.ToString() ?? "-"}:take={Take?.ToString() ?? "-"}";

    public override bool Supports(ParityBackendKind kind) => kind != ParityBackendKind.Remote;

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var query = backend.FullOrders.Query();

        if (MinAmount is { } min)
        {
            query = query.Where(o => o.Amount.Value >= min);
        }

        query = query.OrderBy(o => o.OrderId);

        if (Skip is { } skip)
        {
            query = query.Skip(skip);
        }

        if (Take is { } take)
        {
            query = query.Take(take);
        }

        return Terminal switch
        {
            ParityPagedTerminal.Fetch => $"fetched={(await query.ToListAsync(ct)).Count}",
            ParityPagedTerminal.Count => $"count={await query.CountAsync(ct)}",
            ParityPagedTerminal.Any => $"any={await query.AnyAsync(ct)}",
            ParityPagedTerminal.Delete => $"rows={await query.ExecuteDeleteAsync(false, ct)}",
            ParityPagedTerminal.CascadeDelete => $"rows={await query.ExecuteDeleteAsync(true, ct)}",
            _ => throw new ArgumentOutOfRangeException(nameof(Terminal)),
        };
    }
}

/// <summary>全機能面の <c>Query().Where(…).ExecuteDeleteAsync</c> で注文を消す（リモートには無い）</summary>
public sealed record ExecuteDeleteOrdersOp(ParityOrderPredicate Predicate, bool CascadeDelete)
    : ParityOp
{
    public override bool Supports(ParityBackendKind kind) => kind != ParityBackendKind.Remote;

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var query = Predicate switch
        {
            ParityOrderPredicate.AmountAtLeast100 => backend
                .FullOrders.Query()
                .Where(o => o.Amount.Value >= 100m),
            ParityOrderPredicate.HasLines => backend
                .FullOrders.Query()
                .Where(o => o.OrderLines.Any()),
            _ => throw new ArgumentOutOfRangeException(nameof(Predicate)),
        };

        return $"rows={await query.ExecuteDeleteAsync(CascadeDelete, ct)}";
    }
}

/// <summary>
/// 全機能面の <c>Query().ExecuteDeleteAsync</c> で顧客をすべて消す（リモートには無い）。顧客のカスケードは自己参照を含む
/// ＝循環するので、カスケードありでは全実装先が NotSupportedException で断る
/// </summary>
public sealed record ExecuteDeleteCustomersOp(bool CascadeDelete) : ParityOp
{
    public override bool Supports(ParityBackendKind kind) => kind != ParityBackendKind.Remote;

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct) =>
        $"rows={await backend.FullCustomers.Query().Where(c => c.CustomerId.Value >= 1).ExecuteDeleteAsync(CascadeDelete, ct)}";
}

/// <summary>注文の除外 blob をリモート面の Stream アクセサで書く（EF Core は NotSupported＝docs に書いてある違い）</summary>
public sealed record WriteBlobOp(int OrderId, string Column, byte[]? Data) : ParityOp
{
    public override bool Supports(ParityBackendKind kind) => kind != ParityBackendKind.EfCore;

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        using var source = Data is null ? null : new MemoryStream(Data);
        var id = OrderIdValue.Create(OrderId);
        var written = Column switch
        {
            "payload" => await backend.Orders.WritePayloadAsync(id, source, null, ct),
            "thumb" => await backend.Orders.WriteThumbAsync(id, source, null, ct),
            _ => throw new ArgumentException(Column),
        };

        return $"written={written}";
    }
}

/// <summary>注文の除外 blob をリモート面の Stream アクセサで読む（EF Core は NotSupported）</summary>
public sealed record ReadBlobOp(int OrderId, string Column) : ParityOp
{
    public override bool Supports(ParityBackendKind kind) => kind != ParityBackendKind.EfCore;

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        using var destination = new MemoryStream();
        var id = OrderIdValue.Create(OrderId);
        var read = Column switch
        {
            "payload" => await backend.Orders.ReadPayloadAsync(id, destination, ct),
            "thumb" => await backend.Orders.ReadThumbAsync(id, destination, ct),
            _ => throw new ArgumentException(Column),
        };

        return $"read={read} bytes={Convert.ToHexString(destination.ToArray())}";
    }
}

/// <summary>期待を確かめるときに渡す、実装先と直前の手の観測</summary>
/// <param name="Kind">実装先</param>
/// <param name="Backend">実装先（スロットを読む）</param>
/// <param name="Previous">直前の手（期待の手を除く）の観測（戻り値・保存先・フックの生の呼び出し）</param>
public sealed record ParityExpectation(
    ParityBackendKind Kind,
    ParityBackend Backend,
    ParityStep Previous
);

/// <summary>
/// 決定した振る舞いへの期待。基準の QuickER 版と比べるのでなく、実装先ごとに直接確かめる（基準自身が決定に
/// 追いついていない間も、どちらへそろえるかを取り違えないため）。満たさなければ <see cref="Check"/> が理由を返す。
/// </summary>
/// <remarks>流す仕組みが直接評価する（<see cref="RunAsync"/> は呼ばれない）。</remarks>
public sealed record ExpectOp(string Label, Func<ParityExpectation, string?> Check) : ParityOp
{
    /// <summary>期待の手の名前の接頭辞</summary>
    public const string NamePrefix = "Expect:";

    public override string Name => NamePrefix + Label;

    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct) =>
        throw new InvalidOperationException("The runner evaluates expectations directly.");
}

/// <summary>スロットのグラフの中の 1 行への参照を、別のスロットの根として持っておく（保存の後もその行を見るため）</summary>
public sealed record KeepOp(string Slot, string Key, string As) : ParityOp
{
    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        backend.Slots[As] = Find(backend, Slot, Key);

        return Task.FromResult("kept");
    }
}
