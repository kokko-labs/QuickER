using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>乱数の操作の列が使う操作の範囲（どの実装先で流すかが決まる）</summary>
public enum ParityGeneratorMode
{
    /// <summary>リモート面にある操作だけ（4 つの実装先すべて）</summary>
    Common,

    /// <summary>全機能面の操作も使う（Include の取得・列の条件の一括削除。リモートを除く 3 つ）</summary>
    Full,

    /// <summary>除外 blob の Stream アクセサも使う（EF Core を除く 3 つ）</summary>
    Blob,
}

/// <summary>
/// 一致テストの乱数の操作の列を作る。保存先の行と、各スロットが何を持っているかをモデルとして持ち、成り立つ操作だけを並べる。
/// </summary>
/// <remarks>
/// <para>
/// 次の手は作らない（docs に書いてある違い・決定で受容した差・決まった形のシナリオが受け持つ指摘）：
/// </para>
/// <list type="bullet">
///   <item>既存の行を組み立て直した UPDATE（EF Core は全列を書くので除外 blob を上書きする＝docs）。UPDATE は読み込んだ行だけ</item>
///   <item>insertWhenUpdateMissing・生 SQL・ナビゲーションの条件（#33・#36・#37 は決まった形のシナリオが受け持つ）</item>
///   <item>列の途中の、子を持つ親の <c>DeleteAsync(id)</c>・親の無い行の挿入（P1・P2＝インメモリは外部キーを検査せず、参照動作も
///   再現しない。受容した差で、docs に書く）。この 2 つは列の末尾にだけ置く（<see cref="AppendAcceptedDeviationTail"/>）。
///   グラフの根を消すのは、その子をすべてグラフに持っているときだけ</item>
///   <item>他者が消した行・まだ挿入していない（Added の）行への削除（行なし削除の非対称＝受容した差）。
///   スロットの行が消されたら、そのスロットは読み直すまで使わない</item>
///   <item>NOT NULL の除外 blob への NULL の書き込み（インメモリが NOT NULL を検査するかは測っていない。外部キーと同じく実 DB と食い違う見込みなので作らない）</item>
/// </list>
/// <para>
/// 制約違反（主キー・一意な名前の重複）はわざと混ぜる。どの実装先も断り、何も書かないことを比べる。
/// </para>
/// </remarks>
public sealed partial class BackendParityGenerator
{
    private const int MinSteps = 20;
    private const int MaxSteps = 40;
    private static readonly string[] SlotNames = ["A", "B"];
    private static readonly decimal[] Amounts = [5m, 99.99m, 100m, 250.5m];

    private readonly Random _random;
    private readonly ParityGeneratorMode _mode;
    private readonly Dictionary<int, string> _customers = new();
    private readonly Dictionary<int, (int Customer, decimal Amount)> _orders = new();
    private readonly Dictionary<int, int> _lines = new();
    private readonly Dictionary<string, SlotModel> _slots = new(StringComparer.Ordinal);
    private readonly HashSet<int> _reservedLines = new();
    private readonly List<ParityOp> _ops = new();
    private int _step;

    private BackendParityGenerator(int seed)
    {
        _random = new Random(seed);
        _mode = (ParityGeneratorMode)(seed % 3);
    }

    /// <summary>種から操作の列のシナリオを作る（同じ種は同じ列）</summary>
    public static ParityScenario Create(int seed)
    {
        var generator = new BackendParityGenerator(seed);
        var seeds = generator.SeedData();
        var count = generator._random.Next(MinSteps, MaxSteps + 1);

        while (generator._ops.Count < count)
        {
            generator.Next();
        }

        generator.AppendTail();

        return new ParityScenario(
            $"seed-{seed}-{generator._mode}",
            seeds,
            generator._ops,
            generator._hookRule is { } rule ? new ParityBackendOptions(HookRules: [rule]) : null,
            CutPendingDeviations: true
        );
    }

    /// <summary>種まき（顧客 2・注文 3・明細 3）とモデルの初期値</summary>
    private List<ParityOp> SeedData()
    {
        _customers[1] = "C1";
        _customers[2] = "C2";
        _orders[10] = (1, 100m);
        _orders[11] = (1, 5m);
        _orders[12] = (2, 250.5m);
        _lines[100] = 10;
        _lines[101] = 10;
        _lines[102] = 12;

        return
        [
            new SeedOp(new CustomerRow(1, "C1", "vip")),
            new SeedOp(new CustomerRow(2, "C2")),
            new SeedOp(new OrderRow(10, 1, 100m, "first", Payload: [0x01, 0x02], Thumb: [0x0A])),
            new SeedOp(new OrderRow(11, 1, 5m)),
            new SeedOp(new OrderRow(12, 2, 250.5m, Thumb: [0x0C])),
            new SeedOp(new LineRow(100, 10, "pen", 2, 0.5m)),
            new SeedOp(new LineRow(101, 10, "ink", 5)),
            new SeedOp(new LineRow(102, 12, "mug", 1)),
            .. SeedCustomerData(),
        ];
    }

    /// <summary>成り立つ操作を 1 つ選んで足す（選べなければ何も足さず、呼び出し側がもう一度呼ぶ）</summary>
    private void Next()
    {
        _step++;
        var candidates = new List<(int Weight, Action Emit)>
        {
            (3, InsertCustomer),
            (4, InsertOrder),
            (4, InsertLine),
            (6, Load),
            (2, ViolateConstraint),
            (3, DeleteLeaf),
            (2, OutsideEditMemo),
        };

        var usable = _slots.Where(s => !s.Value.Stale).ToList();

        if (usable.Count > 0)
        {
            candidates.Add((4, AttachLine));
            candidates.Add((3, AddLine));
            candidates.Add((4, EditMemo));
            candidates.Add((4, EditQuantity));
            candidates.Add((3, RemoveLine));
            candidates.Add((2, RemoveRoot));
            candidates.Add((6, Save));
            candidates.Add((2, UpdateRoot));
        }

        if (usable.Count == 2 && usable[0].Value.OrderId != usable[1].Value.OrderId)
        {
            candidates.Add((2, SaveMany));
        }

        AddCustomerCandidates(candidates);

        if (_mode == ParityGeneratorMode.Full)
        {
            candidates.Add((3, LoadWithLines));
            candidates.Add((1, ExecuteDeleteByAmount));
        }

        if (_mode == ParityGeneratorMode.Blob)
        {
            candidates.Add((4, WriteBlob));
            candidates.Add((3, ReadBlob));
        }

        var total = candidates.Sum(c => c.Weight);
        var pick = _random.Next(total);

        foreach (var (weight, emit) in candidates)
        {
            if (pick < weight)
            {
                emit();

                return;
            }

            pick -= weight;
        }
    }

    // ---- 受け入れた手の差（列の末尾＝AppendTail が選ぶ） ----

    /// <summary>
    /// 子を持つ親（明細のある注文・注文のある顧客）を DeleteAsync(id) で消す（DB は ON DELETE CASCADE で子も消す）。顧客は、注文の
    /// ほかに紹介先・連絡先から参照されていないものだけ（それらの外部キーは CASCADE でないので DB が断る）
    /// </summary>
    private bool ParentDeleteWithChildren()
    {
        var parents = new List<(string Table, int Id)>();
        parents.AddRange(
            _orders.Keys.Where(id => _lines.Any(l => l.Value == id)).Select(id => ("orders", id))
        );
        parents.AddRange(
            _customers
                .Keys.Where(id =>
                    _orders.Any(o => o.Value.Customer == id)
                    && !_referrers.Any(r => r.Value == id && r.Key != id)
                    && _contacts.Values.All(c => c.RelationsTo(id, _codes).Count == 0)
                    && !RemovalPending(id)
                )
                .Select(id => ("customers", id))
        );

        if (parents.Count == 0)
        {
            return false;
        }

        var (table, key) = PickFrom(parents);
        _ops.Add(
            new DeviatingOp(new DeleteOp(table, key), ParityDeviation.ParentDeleteWithChildren)
        );
        var orders =
            table == "orders"
                ? [key]
                : _orders.Where(o => o.Value.Customer == key).Select(o => o.Key).ToList();

        foreach (var order in orders)
        {
            foreach (var line in _lines.Where(l => l.Value == order).Select(l => l.Key).ToList())
            {
                _lines.Remove(line);
                MarkStaleHolding(line, except: null);
            }

            _orders.Remove(order);
            MarkStaleHoldingOrder(order);
        }

        if (table == "customers")
        {
            RemoveCustomer(key);
        }

        return true;
    }

    /// <summary>親の無い明細（注文 99）か、無い顧客（9）を指す注文を挿入する（DB は外部キーで断る＝モデルは変えない）</summary>
    private void OrphanInsert()
    {
        if (_random.Next(2) == 0)
        {
            var line = Fresh(100, 150, k => _lines.ContainsKey(k) || _reservedLines.Contains(k));

            if (line is not null)
            {
                _ops.Add(
                    new DeviatingOp(
                        new InsertOp(new LineRow(line.Value, 99, $"o{_step}", 1)),
                        ParityDeviation.OrphanInsert
                    )
                );
            }

            return;
        }

        var order = Fresh(10, 30, _orders.ContainsKey);

        if (order is not null)
        {
            _ops.Add(
                new DeviatingOp(
                    new InsertOp(new OrderRow(order.Value, 9, 1m)),
                    ParityDeviation.OrphanInsert
                )
            );
        }
    }

    // ---- 挿入・制約違反 ----

    private void InsertCustomer()
    {
        var id = Fresh(1, 9, _customers.ContainsKey);

        if (id is null)
        {
            return;
        }

        var name = $"C{id}";
        var referrer = MaybeCustomer();
        _customers[id.Value] = name;
        _codes[id.Value] = CustomerRow.DefaultCode(id.Value);
        _referrers[id.Value] = referrer;
        _ops.Add(new InsertOp(new CustomerRow(id.Value, name, Pick(null, "n"), referrer)));
    }

    private void InsertOrder()
    {
        var id = Fresh(10, 30, _orders.ContainsKey);
        var live = LiveCustomers();

        if (id is null || live.Count == 0)
        {
            return;
        }

        var customer = PickFrom(live);
        var amount = Pick(Amounts);
        _orders[id.Value] = (customer, amount);
        _ops.Add(
            new InsertOp(
                new OrderRow(
                    id.Value,
                    customer,
                    amount,
                    Pick(null, $"m{_step}"),
                    Payload: Pick<byte[]?>(null, [(byte)_step]),
                    Thumb: [(byte)(_step + 1)],
                    At: Pick<DateTime?>(null, new DateTime(2026, 1, 2, 3, 4, 5)),
                    Paid: _random.Next(2) == 0
                )
            )
        );
    }

    private void InsertLine()
    {
        var id = Fresh(100, 150, k => _lines.ContainsKey(k) || _reservedLines.Contains(k));

        if (id is null || _orders.Count == 0)
        {
            return;
        }

        // 根に削除の印が付いた注文へは足さない（保存で DB の ON DELETE CASCADE が消す子になる＝P1）
        var targets = _orders.Keys.Where(o => !PendingRemoval(o)).ToList();

        if (targets.Count == 0)
        {
            return;
        }

        var order = PickFrom(targets);
        _lines[id.Value] = order;
        _ops.Add(
            new InsertOp(
                new LineRow(
                    id.Value,
                    order,
                    $"i{_step}",
                    _random.Next(1, 9),
                    Pick<decimal?>(null, 1.25m)
                )
            )
        );
    }

    /// <summary>主キーの重複か一意な名前の重複で、どの実装先も断る挿入（モデルは変えない）</summary>
    private void ViolateConstraint()
    {
        if (_customers.Count == 0)
        {
            return;
        }

        var existing = PickFrom([.. _customers.Keys]);

        if (_random.Next(2) == 0)
        {
            _ops.Add(new InsertOp(new CustomerRow(existing, $"dup{_step}")));
        }
        else
        {
            var id = Fresh(1, 9, _customers.ContainsKey);

            if (id is not null)
            {
                _ops.Add(new InsertOp(new CustomerRow(id.Value, _customers[existing])));
            }
        }
    }

    // ---- 読み込み・グラフの編集 ----

    private void Load()
    {
        var slot = Pick(SlotNames);

        if (_random.Next(6) == 0)
        {
            // 無い行を読む（どの実装先も null）
            _slots.Remove(slot);
            _ops.Add(new LoadOp(slot, "orders", 99));

            return;
        }

        if (_orders.Count == 0)
        {
            return;
        }

        var order = PickFrom([.. _orders.Keys]);
        _slots[slot] = new SlotModel(order);
        _ops.Add(new LoadOp(slot, "orders", order));
    }

    private void LoadWithLines()
    {
        if (_orders.Count == 0)
        {
            return;
        }

        var slot = Pick(SlotNames);
        var order = PickFrom([.. _orders.Keys]);
        var model = new SlotModel(order);

        foreach (var line in _lines.Where(l => l.Value == order).Select(l => l.Key))
        {
            model.Lines[line] = RowState.Unchanged;
        }

        _slots[slot] = model;
        _ops.Add(new LoadOrderWithLinesOp(slot, order));
    }

    private void AttachLine()
    {
        var (name, slot) = UsableSlot();
        var candidates = _lines
            .Where(l => l.Value == slot.OrderId && !slot.Lines.ContainsKey(l.Key))
            .Select(l => l.Key)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var line = PickFrom(candidates);
        slot.Lines[line] = RowState.Unchanged;
        _ops.Add(new AttachLineOp(name, line));
    }

    private void AddLine()
    {
        var (name, slot) = UsableSlot();

        if (PendingRemoval(slot.OrderId))
        {
            return;
        }

        var id = Fresh(100, 150, k => _lines.ContainsKey(k) || _reservedLines.Contains(k));

        if (id is null)
        {
            return;
        }

        _reservedLines.Add(id.Value);
        slot.Lines[id.Value] = RowState.Added;
        _ops.Add(new AddLineOp(name, new LineRow(id.Value, slot.OrderId, $"a{_step}", 1)));
    }

    private void EditMemo()
    {
        var (name, slot) = UsableSlot();

        if (slot.Root == RowState.Removed)
        {
            return;
        }

        slot.Root = RowState.Updated;
        _ops.Add(new EditMemoOp(name, slot.OrderId, Pick(null, $"e{_step}")));
    }

    private void EditQuantity()
    {
        var (name, slot) = UsableSlot();
        var candidates = slot
            .Lines.Where(l => l.Value != RowState.Removed)
            .Select(l => l.Key)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var line = PickFrom(candidates);

        if (slot.Lines[line] == RowState.Unchanged)
        {
            slot.Lines[line] = RowState.Updated;
        }

        _ops.Add(new EditQuantityOp(name, line, _step % 9 + 1));
    }

    private void RemoveLine()
    {
        var (name, slot) = UsableSlot();

        // Added の行は消さない（まだ無い行の削除＝行なし削除の非対称）
        var candidates = slot
            .Lines.Where(l => l.Value is RowState.Unchanged or RowState.Updated)
            .Select(l => l.Key)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var line = PickFrom(candidates);
        slot.Lines[line] = RowState.Removed;
        _ops.Add(new MarkRemovedOp(name, $"order_lines/{line}"));
    }

    private void RemoveRoot()
    {
        var (name, slot) = UsableSlot();

        // 子をすべてグラフに持っているときだけ（持たない子は DB の ON DELETE CASCADE が消す＝P1）
        var allChildrenHeld = _lines
            .Where(l => l.Value == slot.OrderId)
            .All(l => slot.Lines.ContainsKey(l.Key));

        if (slot.Root == RowState.Removed || !allChildrenHeld)
        {
            return;
        }

        slot.Root = RowState.Removed;
        _ops.Add(new MarkRemovedOp(name, $"orders/{slot.OrderId}"));
    }

    // ---- 保存 ----

    private void Save()
    {
        var (name, slot) = UsableSlot();

        if (!Saveable(slot))
        {
            return;
        }

        _ops.Add(
            new SaveOp(
                name,
                new(Mode: Pick(ConcurrencyMode.Optimistic, ConcurrencyMode.ForceOverwrite))
            )
        );
        ApplySave(name, slot);
    }

    private void SaveMany()
    {
        if (!SlotNames.All(n => Saveable(_slots[n])))
        {
            return;
        }

        _ops.Add(new SaveManyOrdersOp(SlotNames));

        foreach (var name in SlotNames)
        {
            ApplySave(name, _slots[name]);
        }
    }

    private void UpdateRoot()
    {
        var (name, slot) = UsableSlot();

        if (slot.Root == RowState.Removed)
        {
            return;
        }

        _ops.Add(
            new UpdateOrderOp(
                name,
                Pick(ConcurrencyMode.Optimistic, ConcurrencyMode.ForceOverwrite)
            )
        );
    }

    /// <summary>グラフ保存が成功した後のモデル（子から根へ、削除は子孫ごと）</summary>
    private void ApplySave(string name, SlotModel slot)
    {
        foreach (var (line, state) in slot.Lines.ToList())
        {
            switch (state)
            {
                case RowState.Added:
                    _lines[line] = slot.OrderId;
                    _reservedLines.Remove(line);
                    slot.Lines[line] = RowState.Unchanged;
                    break;
                case RowState.Updated:
                    slot.Lines[line] = RowState.Unchanged;
                    break;
                case RowState.Removed:
                    _lines.Remove(line);
                    slot.Lines.Remove(line);
                    MarkStaleHolding(line, except: name);
                    break;
            }
        }

        if (slot.Root == RowState.Removed)
        {
            foreach (var line in slot.Lines.Keys)
            {
                _lines.Remove(line);
            }

            _orders.Remove(slot.OrderId);
            _slots.Remove(name);
            MarkStaleHoldingOrder(slot.OrderId);

            return;
        }

        slot.Root = RowState.Unchanged;
    }

    // ---- 削除・ほかの書き手 ----

    /// <summary>
    /// 子を持たない行を、リポジトリかほかの書き手で消す（子を持つ親は消さない＝P1。顧客は注文・紹介先・連絡先のどれからも
    /// 参照されていないものだけ。スロットのグラフが指している顧客も消さない）
    /// </summary>
    private void DeleteLeaf()
    {
        var leaves = new List<(string Table, int Id)>();
        leaves.AddRange(_lines.Keys.Select(id => ("order_lines", id)));
        leaves.AddRange(
            _orders.Keys.Where(id => _lines.All(l => l.Value != id)).Select(id => ("orders", id))
        );
        var pending = PendingTargets();
        leaves.AddRange(
            _customers
                .Keys.Where(id => !IsReferenced(id) && !pending.Contains(id))
                .Select(id => ("customers", id))
        );
        leaves.AddRange(_contacts.Keys.Select(id => ("contacts", id)));

        if (leaves.Count == 0)
        {
            return;
        }

        var (table, key) = PickFrom(leaves);
        _ops.Add(_random.Next(2) == 0 ? new DeleteOp(table, key) : new OutsideDeleteOp(table, key));

        switch (table)
        {
            case "order_lines":
                _lines.Remove(key);
                MarkStaleHolding(key, except: null);
                break;
            case "orders":
                _orders.Remove(key);
                MarkStaleHoldingOrder(key);
                break;
            case "contacts":
                _contacts.Remove(key);
                MarkStaleHoldingContact(key, except: null);
                break;
            default:
                RemoveCustomer(key);
                break;
        }
    }

    private void OutsideEditMemo()
    {
        if (_orders.Count == 0)
        {
            return;
        }

        _ops.Add(new OutsideEditMemoOp(PickFrom([.. _orders.Keys]), $"o{_step}"));
    }

    private void ExecuteDeleteByAmount()
    {
        _ops.Add(
            new ExecuteDeleteOrdersOp(ParityOrderPredicate.AmountAtLeast100, CascadeDelete: true)
        );

        foreach (
            var order in _orders.Where(o => o.Value.Amount >= 100m).Select(o => o.Key).ToList()
        )
        {
            foreach (var line in _lines.Where(l => l.Value == order).Select(l => l.Key).ToList())
            {
                _lines.Remove(line);
            }

            _orders.Remove(order);
            MarkStaleHoldingOrder(order);
        }
    }

    // ---- blob ----

    private void WriteBlob()
    {
        var order = _random.Next(5) == 0 || _orders.Count == 0 ? 99 : PickFrom([.. _orders.Keys]);

        // NOT NULL の thumb へ NULL は書かない（インメモリが NOT NULL を検査するかは測っていない）
        _ops.Add(
            _random.Next(2) == 0
                ? new WriteBlobOp(order, "payload", Pick<byte[]?>(null, [(byte)_step, 0xAA]))
                : new WriteBlobOp(order, "thumb", [(byte)_step, 0xBB])
        );
    }

    private void ReadBlob()
    {
        var order = _random.Next(5) == 0 || _orders.Count == 0 ? 99 : PickFrom([.. _orders.Keys]);
        _ops.Add(new ReadBlobOp(order, Pick("payload", "thumb")));
    }

    // ---- 補助 ----

    /// <summary>どれかのスロットが、その注文の根に削除の印を付けている</summary>
    private bool PendingRemoval(int order) =>
        _slots.Values.Any(s => !s.Stale && s.OrderId == order && s.Root == RowState.Removed);

    /// <summary>
    /// 保存してよいスロットか：根を消すなら、その注文の子をすべてグラフに持っている（持たない子は DB の
    /// ON DELETE CASCADE が消す＝P1。印を付けた後で他の手が子を足した場合に備えて、保存の時点でも確かめる）
    /// </summary>
    private bool Saveable(SlotModel slot) =>
        slot.Root != RowState.Removed
        || _lines.Where(l => l.Value == slot.OrderId).All(l => slot.Lines.ContainsKey(l.Key));

    /// <summary>その行を持つスロットを、読み直すまで使わない印を付ける</summary>
    private void MarkStaleHolding(int line, string? except)
    {
        foreach (var (name, slot) in _slots)
        {
            if (name != except && slot.Lines.ContainsKey(line))
            {
                slot.Stale = true;
            }
        }
    }

    /// <summary>その注文を根に持つスロットを外す（根が無いグラフは使わない）</summary>
    private void MarkStaleHoldingOrder(int order)
    {
        foreach (
            var name in _slots.Where(s => s.Value.OrderId == order).Select(s => s.Key).ToList()
        )
        {
            _slots[name].Stale = true;
        }
    }

    private (string Name, SlotModel Slot) UsableSlot()
    {
        var usable = _slots.Where(s => !s.Value.Stale).ToList();
        var pick = usable[_random.Next(usable.Count)];

        return (pick.Key, pick.Value);
    }

    private int? Fresh(int from, int to, Func<int, bool> taken)
    {
        var free = Enumerable.Range(from, to - from).Where(k => !taken(k)).ToList();

        return free.Count == 0 ? null : free[_random.Next(free.Count)];
    }

    private T Pick<T>(params T[] items) => items[_random.Next(items.Length)];

    private T PickFrom<T>(List<T> items) => items[_random.Next(items.Count)];

    /// <summary>スロットが持っているグラフのモデル</summary>
    private sealed class SlotModel(int orderId)
    {
        public int OrderId { get; } = orderId;

        public RowState Root { get; set; } = RowState.Unchanged;

        public Dictionary<int, RowState> Lines { get; } = new();

        /// <summary>持っている行が消された（読み直すまで使わない）</summary>
        public bool Stale { get; set; }
    }
}

/// <summary>ほかの書き手が注文のメモを書き換える（同じ保存先・Save フックなし・版の無い表なので後勝ち）</summary>
public sealed record OutsideEditMemoOp(int OrderId, string Memo) : ParityOp
{
    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var order = await backend.Outside.Orders.GetByIdAsync(OrderIdValue.Create(OrderId), ct);

        if (order is null)
        {
            return "null";
        }

        order.Memo = MemoValue.Create(Memo);

        return $"updated={await backend.Outside.Orders.UpdateAsync(order, cancellationToken: ct)}";
    }
}
