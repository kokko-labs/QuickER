using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 全機能面の <c>BulkInsertAsync</c> で行をまとめて挿入する（リモート面には無い）。戻り値は <c>inserted=n</c>。
/// 行は同じ型（同じ表）にそろえ、組み立てたエンティティは Added にしてから渡す（直接の挿入が RowState を触らないことを見る形）。
/// null の要素はそのまま渡す（null の要素を飛ばす契約を見るため）。一覧（<c>List</c>）で渡すので、空の一覧は件数の分かるコレクションの
/// 近道（接続を開かず 0）を通る。
/// </summary>
/// <param name="Rows">挿入する行（null の要素を含めてよい）</param>
/// <param name="KeepAs">
/// 渡すと、組み立てたエンティティを <c>{KeepAs}{i}</c> のスロットへ置く（i は null でない要素だけを 0 から数える）。保存の後に
/// RowState と呼び出し側のナビゲーションを見るため
/// </param>
/// <param name="Table">null でない要素が 1 つも無いときに使う表（省略すると顧客）</param>
public sealed record BulkInsertOp(
    IReadOnlyList<ParityRow?> Rows,
    string? KeepAs = null,
    string Table = "customers"
) : ParityOp
{
    public override string Name => "BulkInsert";

    public override bool Supports(ParityBackendKind kind) => !ParityBackendKinds.IsRemote(kind);

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var entities = Rows.Select(row => row?.Build()).ToList();
        var built = entities.Where(e => e is not null).Select(e => e!).ToList();

        foreach (var entity in built)
        {
            entity.MarkAdded();
        }

        if (KeepAs is not null)
        {
            for (var i = 0; i < built.Count; i++)
            {
                backend.Slots[$"{KeepAs}{i}"] = built[i];
            }
        }

        var table = built.Count == 0 ? Table : TableOf(built[0]);
        var inserted = table switch
        {
            "customers" => await backend.FullCustomers.BulkInsertAsync(
                entities.Select(e => (CustomerEntity)e!).ToList(),
                ct
            ),
            "orders" => await backend.FullOrders.BulkInsertAsync(
                entities.Select(e => (OrderEntity)e!).ToList(),
                ct
            ),
            "order_lines" => await backend.FullLines.BulkInsertAsync(
                entities.Select(e => (OrderLineEntity)e!).ToList(),
                ct
            ),
            _ => throw new ArgumentException(table),
        };

        return $"inserted={inserted}";
    }

    /// <summary>エンティティの表の名前（連絡先は全機能面のリポジトリを持たないので扱わない）</summary>
    private static string TableOf(EntityBaseCore entity) =>
        entity switch
        {
            CustomerEntity => "customers",
            OrderEntity => "orders",
            OrderLineEntity => "order_lines",
            _ => throw new ArgumentException(entity.GetType().Name, nameof(entity)),
        };
}

/// <summary>
/// 親の顧客の紹介先のコレクションに子の顧客を置き（子の外部キーは <paramref name="Child"/> のまま・親への参照は置かない）、親・子・
/// <paramref name="Extra"/> の順に 1 回の全機能面の BulkInsert で入れる（リモートには無い）。スロット <c>b0</c>＝親・<c>b1</c>＝子。
/// <paramref name="ParentCollectionAsHashSet"/> なら、親の紹介先のコレクションを <c>HashSet</c> に差し替えて置く
/// （ナビゲーションの型は <c>ICollection&lt;T&gt;</c> なので、<c>IList</c> でないコレクションも代入できる）
/// </summary>
/// <remarks>
/// 外部キーとナビゲーションが食い違う行を一括挿入へ渡したとき、どの実装先も外部キーのとおりに書き、呼び出し側のナビゲーションと
/// 外部キーを変えないことを見る（スロットの比べ方が親の子のコレクションと子の外部キーを書く）。
/// </remarks>
public sealed record BulkInsertWithNavigationOp(
    CustomerRow Parent,
    CustomerRow Child,
    IReadOnlyList<CustomerRow>? Extra = null,
    bool ParentCollectionAsHashSet = false
) : ParityOp
{
    public override string Name => "BulkInsertWithNavigation";

    public override bool Supports(ParityBackendKind kind) => !ParityBackendKinds.IsRemote(kind);

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var parent = (CustomerEntity)Parent.Build();
        var child = (CustomerEntity)Child.Build();

        if (ParentCollectionAsHashSet)
        {
            parent.Customers = new HashSet<CustomerEntity>();
        }

        parent.Customers.Add(child);
        List<CustomerEntity> rows =
        [
            parent,
            child,
            .. (Extra ?? []).Select(row => (CustomerEntity)row.Build()),
        ];

        foreach (var row in rows)
        {
            row.MarkAdded();
        }

        backend.Slots["b0"] = parent;
        backend.Slots["b1"] = child;

        return $"inserted={await backend.FullCustomers.BulkInsertAsync(rows, ct)}";
    }
}

/// <summary>
/// 組み立てたエンティティを Added にしてリモート面の <c>InsertAsync</c> で挿入し、そのエンティティそのものをスロットの根に置く
/// （直接の挿入が RowState を触らないことと、同じインスタンスを続けてグラフ保存する形を見るため）。戻り値は
/// <see cref="InsertOp"/> と同じ形
/// </summary>
public sealed record InsertToSlotOp(string Slot, ParityRow Row) : ParityOp
{
    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var entity = Row.Build();
        entity.MarkAdded();
        backend.Slots[Slot] = entity;

        switch (entity)
        {
            case CustomerEntity c:
                await backend.Customers.InsertAsync(c, ct);
                break;
            case OrderEntity o:
                await backend.Orders.InsertAsync(o, ct);
                break;
            case OrderLineEntity l:
                await backend.Lines.InsertAsync(l, ct);
                break;
            case ContactEntity c:
                await backend.Contacts.InsertAsync(c, ct);
                break;
            default:
                throw new InvalidOperationException();
        }

        return ParityFormat.Graph(entity)[0];
    }
}

/// <summary>全機能面の Include で読む方向（親への参照・2 段の親・子のコレクション）</summary>
public enum ParityParentInclude
{
    /// <summary>注文 → 顧客（<c>Include(o =&gt; o.Customer)</c>）</summary>
    OrderCustomer,

    /// <summary>明細 → 注文（<c>Include(l =&gt; l.Order)</c>）</summary>
    LineOrder,

    /// <summary>明細 → 注文 → 顧客（<c>Include(l =&gt; l.Order).ThenInclude(o =&gt; o.Customer)</c>）</summary>
    LineOrderCustomer,

    /// <summary>顧客 → 注文（子のコレクション。<c>Include(c =&gt; c.Orders)</c>）</summary>
    CustomerOrders,
}

/// <summary>
/// 全機能面で 1 行を Include つきで読み、スロットの根に置く（リモートには無い）。戻り値は <c>loaded</c> に、読んだ親の列
/// （<c>parent=[…]</c>・<c>grandparent=[…]</c>）を添える（スロットの比べ方は根から子への方向しか書かないので、親への参照は
/// ここで比べる）。親からその子のコレクションへ戻る ThenInclude は持たない（EF Core の追跡しないクエリが循環として断る）。
/// </summary>
/// <remarks>
/// 読んだグラフはそのまま置く（EF Core が逆向きのナビゲーションを埋める fix-up も含めて、比べる側で見るため）。ただし子の
/// コレクションはキーの順に並べ直す（Include が返す子の並びは決まらない＝<see cref="LoadOrderWithLinesOp"/> と同じ理由）。
/// </remarks>
public sealed record LoadWithParentOp(
    string Slot,
    string Table,
    int Id,
    ParityParentInclude Include
) : ParityOp
{
    public override string Name => $"LoadWithParent:{Include}";

    public override bool Supports(ParityBackendKind kind) => !ParityBackendKinds.IsRemote(kind);

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        EntityBaseCore? root = (Table, Include) switch
        {
            ("orders", ParityParentInclude.OrderCustomer) => await backend
                .FullOrders.Query()
                .Include(o => o.Customer)
                .GetByIdAsync(OrderIdValue.Create(Id), ct),
            ("order_lines", ParityParentInclude.LineOrder) => await backend
                .FullLines.Query()
                .Include(l => l.Order)
                .GetByIdAsync(LineIdValue.Create(Id), ct),
            ("order_lines", ParityParentInclude.LineOrderCustomer) => await backend
                .FullLines.Query()
                .Include(l => l.Order)
                    .ThenInclude(o => o.Customer)
                .GetByIdAsync(LineIdValue.Create(Id), ct),
            ("customers", ParityParentInclude.CustomerOrders) => await backend
                .FullCustomers.Query()
                .Include(c => c.Orders)
                .GetByIdAsync(CustomerIdValue.Create(Id), ct),
            _ => throw new ArgumentException($"{Table} cannot be read with {Include}."),
        };

        if (root is null)
        {
            backend.Slots.Remove(Slot);

            return "null";
        }

        if (root is CustomerEntity customer)
        {
            SortOrders(customer);
        }

        backend.Slots[Slot] = root;

        return Describe(root);
    }

    /// <summary>読んだ親の列と、ThenInclude した親の子のキーを戻り値に書く</summary>
    private string Describe(EntityBaseCore root)
    {
        switch (root)
        {
            case OrderEntity order:
                return $"loaded parent=[{Columns(order.Customer)}]";
            case OrderLineEntity line:
            {
                var parent = line.Order;
                var text = $"loaded parent=[{Columns(parent)}]";

                return Include == ParityParentInclude.LineOrderCustomer
                    ? $"{text} grandparent=[{Columns(parent?.Customer)}]"
                    : text;
            }
            default:
                return "loaded";
        }
    }

    /// <summary>親の列（未読なら <c>none</c>。除外 blob は書かない）</summary>
    private static string Columns(EntityBaseCore? entity) =>
        entity switch
        {
            null => "none",
            CustomerEntity c => ParityFormat.Customer(c),
            OrderEntity o => ParityFormat.Order(o, includeBlobs: false),
            _ => ParityFormat.KeyOf(entity),
        };

    /// <summary>顧客の注文のコレクションをキーの順に並べ直す</summary>
    private static void SortOrders(CustomerEntity customer)
    {
        var sorted = customer.Orders.OrderBy(o => o.OrderId.Value).ToList();
        customer.Orders.Clear();

        foreach (var order in sorted)
        {
            customer.Orders.Add(order);
        }
    }
}

/// <summary>
/// スロットのグラフの行（<paramref name="Key"/>）の親への参照（明細 → 注文・注文 → 顧客・連絡先 → owner の顧客）を、別のスロット
/// <paramref name="As"/> の根として持っておく（保存の後に親の RowState を見るため）。親が読まれていなければ <c>null</c> を返し、
/// そのスロットを空にする
/// </summary>
public sealed record KeepParentOp(string Slot, string Key, string As) : ParityOp
{
    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        EntityBaseCore? parent = Find(backend, Slot, Key) switch
        {
            OrderLineEntity l => l.Order,
            OrderEntity o => o.Customer,
            ContactEntity c => c.Owner,
            var other => throw new InvalidOperationException(
                $"'{ParityFormat.KeyOf(other)}' has no parent reference."
            ),
        };

        if (parent is null)
        {
            backend.Slots.Remove(As);

            return Task.FromResult("null");
        }

        backend.Slots[As] = parent;

        return Task.FromResult("kept");
    }
}

/// <summary><see cref="QueryOrdersOp"/> の条件（式は列挙で選ぶ。値は手の引数で渡す）</summary>
public enum ParityOrderFilter
{
    /// <summary>条件なし</summary>
    None,

    /// <summary><c>o.Memo != null</c></summary>
    MemoIsNotNull,

    /// <summary>
    /// <c>o.Memo == v</c>（<c>v</c> は <c>MemoValue.Create(Text)</c> を入れた変数。<see cref="QueryOrdersOp.Literal"/> のときは
    /// <c>o.Memo == MemoValue.Create("…")</c>＝式の中で作る）
    /// </summary>
    MemoEquals,

    /// <summary><c>o.Memo != v</c>（値の渡し方は <see cref="MemoEquals"/> と同じ）</summary>
    MemoNotEquals,

    /// <summary><c>o.Memo!.Value == text</c>（値オブジェクトを開いた文字列の等値）</summary>
    MemoValueEquals,

    /// <summary><c>o.Memo!.Contains(text)</c></summary>
    MemoContains,

    /// <summary><c>o.Memo!.StartsWith(text)</c></summary>
    MemoStartsWith,

    /// <summary><c>o.Memo!.EndsWith(text)</c></summary>
    MemoEndsWith,

    /// <summary><c>o.Amount.Value &gt;= amount</c></summary>
    AmountAtLeast,

    /// <summary><c>o.Amount.Value &gt;= amount &amp;&amp; o.Memo!.Contains(text)</c></summary>
    AmountAtLeastAndMemoContains,

    /// <summary><c>ids.Contains(o.OrderId)</c>（<c>List&lt;OrderIdValue&gt;</c>＝IN）</summary>
    IdIn,

    /// <summary>
    /// <c>memos.Contains(o.Memo)</c>（<c>List&lt;MemoValue?&gt;</c>＝値オブジェクトの列の IN。要素は
    /// <see cref="QueryOrdersOp.Texts"/> で渡し、null の要素は null のまま入れる）
    /// </summary>
    MemoIn,

    /// <summary><c>!memos.Contains(o.Memo)</c>（値オブジェクトの列の NOT IN。要素の渡し方は <see cref="MemoIn"/> と同じ）</summary>
    MemoNotIn,

    /// <summary>
    /// <c>o.Memo!.Contains(memo)</c>（値オブジェクトを受ける多重定義。<c>memo</c> は <c>MemoValue.Create(Text)</c> か null。
    /// <see cref="QueryOrdersOp.Literal"/> のときは式の中の <c>MemoValue.Create("…")</c>、Text が null なら
    /// <c>(MemoValue)null!</c> の定数）
    /// </summary>
    MemoContainsVo,

    /// <summary><c>o.Memo!.StartsWith(memo)</c>（値オブジェクトを受ける多重定義。値の渡し方は <see cref="MemoContainsVo"/> と同じ）</summary>
    MemoStartsWithVo,

    /// <summary><c>o.Memo!.EndsWith(memo)</c>（値オブジェクトを受ける多重定義。値の渡し方は <see cref="MemoContainsVo"/> と同じ）</summary>
    MemoEndsWithVo,
}

/// <summary><see cref="QueryOrdersOp"/> の並び</summary>
public enum ParityOrderSort
{
    /// <summary>注文 ID の昇順</summary>
    IdAsc,

    /// <summary>注文 ID の降順</summary>
    IdDesc,

    /// <summary>メモの昇順（NULL を含む文字列の列）</summary>
    MemoAsc,

    /// <summary>メモの降順</summary>
    MemoDesc,

    /// <summary>顧客 ID の昇順、続けて注文 ID の降順（並びの 2 つ目のキー）</summary>
    CustomerThenIdDesc,
}

/// <summary><see cref="QueryOrdersOp"/> の終端</summary>
public enum ParityQueryTerminal
{
    /// <summary><c>ToListAsync</c>（注文 ID を並びのまま <c>ids=[…]</c>）</summary>
    Ids,

    /// <summary><c>FirstOrDefaultAsync</c>（<c>first=…</c> か <c>first=none</c>）</summary>
    First,

    /// <summary><c>CountAsync</c></summary>
    Count,

    /// <summary><c>AnyAsync</c></summary>
    Any,

    /// <summary>明細を <c>Include</c> して <c>ToListAsync</c>（<c>ids=[10:100+101,…]</c>＝注文ごとの明細のキー）</summary>
    IdsWithLines,
}

/// <summary>
/// 全機能面の <c>Query()</c> に、条件・並び・窓（Skip / Take）を当てて終端を呼ぶ（リモートには無い）。戻り値は終端ごとの文字列
/// （<see cref="ParityQueryTerminal"/>）。
/// </summary>
/// <param name="Filter">条件</param>
/// <param name="Text">条件の文字列（メモの値・パターン。null も渡せる＝null のパターンを断る契約を見る）</param>
/// <param name="Literal">
/// 文字列と金額を、変数でなく式の中の定数として書くか（コンパイラが書く <c>Contains("0%")</c> と同じ形の式を組む）。LIKE の
/// パターンは定数とパラメータで組み方が違うので、元のテストの形に合わせて選ぶ
/// </param>
/// <param name="Amount">金額の条件</param>
/// <param name="Ids">IN の条件の注文 ID</param>
/// <param name="Sort">並び</param>
/// <param name="Skip">Skip</param>
/// <param name="Take">Take</param>
/// <param name="Terminal">終端</param>
/// <param name="Texts">値オブジェクトの列の IN の要素の文字列（null の要素を含めてよい＝null の要素の畳み込みを見る）</param>
public sealed record QueryOrdersOp(
    ParityOrderFilter Filter = ParityOrderFilter.None,
    string? Text = null,
    bool Literal = false,
    decimal? Amount = null,
    IReadOnlyList<int>? Ids = null,
    ParityOrderSort Sort = ParityOrderSort.IdAsc,
    int? Skip = null,
    int? Take = null,
    ParityQueryTerminal Terminal = ParityQueryTerminal.Ids,
    IReadOnlyList<string?>? Texts = null
) : ParityOp
{
    public override string Name =>
        $"QueryOrders:{Filter}:{Sort}:skip={Skip?.ToString(CultureInfo.InvariantCulture) ?? "-"}"
        + $":take={Take?.ToString(CultureInfo.InvariantCulture) ?? "-"}:{Terminal}";

    public override bool Supports(ParityBackendKind kind) => !ParityBackendKinds.IsRemote(kind);

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var query = backend.FullOrders.Query();

        if (Predicate() is { } predicate)
        {
            query = query.Where(predicate);
        }

        query = Sort switch
        {
            ParityOrderSort.IdAsc => query.OrderBy(o => o.OrderId),
            ParityOrderSort.IdDesc => query.OrderByDescending(o => o.OrderId),
            ParityOrderSort.MemoAsc => query.OrderBy(o => o.Memo),
            ParityOrderSort.MemoDesc => query.OrderByDescending(o => o.Memo),
            ParityOrderSort.CustomerThenIdDesc => query
                .OrderBy(o => o.CustomerId)
                .OrderByDescending(o => o.OrderId),
            _ => throw new ArgumentOutOfRangeException(nameof(Sort)),
        };

        if (Skip is { } skip)
        {
            query = query.Skip(skip);
        }

        if (Take is { } take)
        {
            query = query.Take(take);
        }

        switch (Terminal)
        {
            case ParityQueryTerminal.Ids:
                return $"ids=[{string.Join(",", (await query.ToListAsync(ct)).Select(o => Id(o)))}]";
            case ParityQueryTerminal.First:
                return await query.FirstOrDefaultAsync(ct) is { } first
                    ? $"first={Id(first)}"
                    : "first=none";
            case ParityQueryTerminal.Count:
                return $"count={await query.CountAsync(ct)}";
            case ParityQueryTerminal.Any:
                return $"any={await query.AnyAsync(ct)}";
            case ParityQueryTerminal.IdsWithLines:
            {
                var orders = await query.Include(o => o.OrderLines).ToListAsync(ct);
                var text = orders.Select(o =>
                    $"{Id(o)}:{string.Join("+", o.OrderLines.Select(l => l.LineId.Value).Order())}"
                );

                return $"ids=[{string.Join(",", text)}]";
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(Terminal));
        }
    }

    /// <summary>条件の式（条件なしは null）</summary>
    private Expression<Func<OrderEntity, bool>>? Predicate()
    {
        var text = Text;
        var amount = Amount ?? 0m;

        if (Literal)
        {
            return Filter switch
            {
                ParityOrderFilter.None => null,
                ParityOrderFilter.MemoEquals => WithConstants(o => o.Memo == MemoValue.Create("")),
                ParityOrderFilter.MemoNotEquals => WithConstants(o =>
                    o.Memo != MemoValue.Create("")
                ),
                ParityOrderFilter.MemoValueEquals => WithConstants(o => o.Memo!.Value == ""),
                ParityOrderFilter.MemoContains => WithConstants(o => o.Memo!.Contains("")),
                ParityOrderFilter.MemoStartsWith => WithConstants(o => o.Memo!.StartsWith("")),
                ParityOrderFilter.MemoEndsWith => WithConstants(o => o.Memo!.EndsWith("")),
                ParityOrderFilter.AmountAtLeast => WithConstants(o => o.Amount.Value >= 0m),
                ParityOrderFilter.AmountAtLeastAndMemoContains => WithConstants(o =>
                    o.Amount.Value >= 0m && o.Memo!.Contains("")
                ),
                ParityOrderFilter.MemoContainsVo => text is null
                    ? o => o.Memo!.Contains((MemoValue)null!)
                    : WithConstants(o => o.Memo!.Contains(MemoValue.Create(""))),
                ParityOrderFilter.MemoStartsWithVo => text is null
                    ? o => o.Memo!.StartsWith((MemoValue)null!)
                    : WithConstants(o => o.Memo!.StartsWith(MemoValue.Create(""))),
                ParityOrderFilter.MemoEndsWithVo => text is null
                    ? o => o.Memo!.EndsWith((MemoValue)null!)
                    : WithConstants(o => o.Memo!.EndsWith(MemoValue.Create(""))),
                _ => throw new ArgumentException($"{Filter} has no literal form."),
            };
        }

        var memo = text is null ? null : MemoValue.Create(text);
        var ids = (Ids ?? []).Select(OrderIdValue.Create).ToList();
        var memos = (Texts ?? [])
            .Select(element => element is null ? null : MemoValue.Create(element))
            .ToList();

        return Filter switch
        {
            ParityOrderFilter.None => null,
            ParityOrderFilter.MemoIsNotNull => o => o.Memo != null,
            ParityOrderFilter.MemoEquals => o => o.Memo == memo,
            ParityOrderFilter.MemoNotEquals => o => o.Memo != memo,
            ParityOrderFilter.MemoValueEquals => o => o.Memo!.Value == text,
            ParityOrderFilter.MemoContains => o => o.Memo!.Contains(text!),
            ParityOrderFilter.MemoStartsWith => o => o.Memo!.StartsWith(text!),
            ParityOrderFilter.MemoEndsWith => o => o.Memo!.EndsWith(text!),
            ParityOrderFilter.AmountAtLeast => o => o.Amount.Value >= amount,
            ParityOrderFilter.AmountAtLeastAndMemoContains => o =>
                o.Amount.Value >= amount && o.Memo!.Contains(text!),
            ParityOrderFilter.IdIn => o => ids.Contains(o.OrderId),
            ParityOrderFilter.MemoIn => o => memos.Contains(o.Memo),
            ParityOrderFilter.MemoNotIn => o => !memos.Contains(o.Memo),
            ParityOrderFilter.MemoContainsVo => o => o.Memo!.Contains(memo!),
            ParityOrderFilter.MemoStartsWithVo => o => o.Memo!.StartsWith(memo!),
            ParityOrderFilter.MemoEndsWithVo => o => o.Memo!.EndsWith(memo!),
            _ => throw new ArgumentOutOfRangeException(nameof(Filter)),
        };
    }

    /// <summary>
    /// コンパイラが書いた式の文字列と金額の定数を、この手の値の定数へ差し替える（変数でなく定数で渡した式＝元のテストの
    /// <c>Contains("0%")</c> と同じ形を、値を変えて組む）
    /// </summary>
    private Expression<Func<OrderEntity, bool>> WithConstants(
        Expression<Func<OrderEntity, bool>> template
    ) => (Expression<Func<OrderEntity, bool>>)new ConstantSwap(Text, Amount).Visit(template);

    private static string Id(OrderEntity order) =>
        order.OrderId.Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>文字列と decimal の定数を差し替える</summary>
    private sealed class ConstantSwap(string? text, decimal? amount) : ExpressionVisitor
    {
        protected override Expression VisitConstant(ConstantExpression node) =>
            node.Type == typeof(string) ? Expression.Constant(text, typeof(string))
            : node.Type == typeof(decimal) ? Expression.Constant(amount ?? 0m, typeof(decimal))
            : node;
    }
}

/// <summary><see cref="ProjectOrdersOp"/> の射影</summary>
public enum ParityProjection
{
    /// <summary>列だけの射影（<c>new { o.OrderId, o.Amount }</c>＝刈り込める経路）</summary>
    Columns,

    /// <summary>Include したナビゲーションを読む射影（<c>new { o.OrderId, CustomerName = o.Customer.Name }</c>＝刈り込めない経路）</summary>
    CustomerName,

    /// <summary>除外 blob を参照する列の射影（<c>new { o.OrderId, o.Payload }</c>＝刈り込んだ SELECT に除外列を含める経路）</summary>
    Payload,

    /// <summary>
    /// エンティティを丸ごと引数に取る射影（セレクタから列を抜き出せない＝全列を読んでから射影するフォールバック。
    /// <c>WithUnboundedBinary()</c> なし＝除外列は未取得のまま射影される）
    /// </summary>
    PayloadFallback,

    /// <summary><see cref="PayloadFallback"/> に <c>WithUnboundedBinary()</c> を付ける（フォールバックの経路でも除外列を読む）</summary>
    PayloadFallbackWithUnboundedBinary,
}

/// <summary>
/// 全機能面の <c>ToProjectionListAsync</c> で注文を射影する（リモートには無い）。条件は顧客（<paramref name="Customer"/>）・並びは
/// 注文 ID の昇順（Include は条件と並びの間に当てる）。戻り値は <c>rows=[10:100,11:50]</c> の形（注文 ID と射影した値）
/// </summary>
public sealed record ProjectOrdersOp(ParityProjection Projection, int? Customer = null) : ParityOp
{
    public override string Name => $"ProjectOrders:{Projection}";

    public override bool Supports(ParityBackendKind kind) => !ParityBackendKinds.IsRemote(kind);

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var query = backend.FullOrders.Query();

        if (Customer is { } id)
        {
            var customer = CustomerIdValue.Create(id);
            query = query.Where(o => o.CustomerId == customer);
        }

        IEnumerable<string> rows = Projection switch
        {
            ParityProjection.Columns => (
                await query
                    .OrderBy(o => o.OrderId)
                    .ToProjectionListAsync(o => new { o.OrderId, o.Amount }, ct)
            ).Select(r => $"{r.OrderId.Value}:{Number(r.Amount.Value)}"),
            ParityProjection.CustomerName => (
                await query
                    .Include(o => o.Customer)
                    .OrderBy(o => o.OrderId)
                    .ToProjectionListAsync(
                        o => new { o.OrderId, CustomerName = o.Customer.Name },
                        ct
                    )
            ).Select(r => $"{r.OrderId.Value}:{r.CustomerName.Value}"),
            ParityProjection.Payload => (
                await query
                    .OrderBy(o => o.OrderId)
                    .ToProjectionListAsync(o => new { o.OrderId, o.Payload }, ct)
            ).Select(r => $"{r.OrderId.Value}:{ParityBlobs.Hex(r.Payload?.Value)}"),
            ParityProjection.PayloadFallback => await query
                .OrderBy(o => o.OrderId)
                .ToProjectionListAsync(o => PayloadOf(o), ct),
            ParityProjection.PayloadFallbackWithUnboundedBinary => await query
                .WithUnboundedBinary()
                .OrderBy(o => o.OrderId)
                .ToProjectionListAsync(o => PayloadOf(o), ct),
            _ => throw new ArgumentOutOfRangeException(nameof(Projection)),
        };

        return $"rows=[{string.Join(",", rows)}]";
    }

    /// <summary>射影のセレクタから列を抜き出せなくする（エンティティ全体を引数に取る）。戻り値は <c>10:0102</c> の形</summary>
    private static string PayloadOf(OrderEntity order) =>
        $"{order.OrderId.Value}:{ParityBlobs.Hex(order.Payload?.Value)}";

    private static string Number(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);
}
