using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 乱数の種から、モデルの操作の列を作る（時刻・乱数の出どころは種だけ）。
/// </summary>
/// <remarks>
/// <para>
/// 書き込みは、書き手の DB の現在の内容を見て、その DB で成功する操作だけを選ぶ（親の無い行・子の残る親の削除・
/// 一意制約の重複は作らない）。失敗する書き込みは「失敗した操作の記録」の別の段（失敗の注入）で扱う。
/// </para>
/// <para>
/// キーは書き手ごとに別の範囲から採る（端末 1＝1000 台・端末 2＝2000 台・サーバーの他者＝3000 台・初期データ＝1 桁）。
/// 値（名前・題名・メモ・blob）は種の中で一意にし、同じ値へ戻る書き込み（ABA）で表明が誤って通らないようにする。
/// 日時のキーは固定の基準時刻からの通し番号＋種から作る端数で、Kind（Utc / Local / Unspecified）も種から選ぶ。
/// 端数の最後の桁は 0 にしない（格納の文字列の末尾の 0 の省き方に照合を依存させない）。
/// </para>
/// </remarks>
internal sealed class SyncModelGenerator
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified);

    // ラベルのキーの頭（大文字小文字・'-'・'_' を混ぜる＝照合順序でキー順が変わり得る文字）
    private static readonly string[] LabelPrefixes = ["a-", "A_", "b", "B-", "_c", "-d", "z"];

    private readonly Random _random;
    private readonly Dictionary<ModelActor, int> _nextId = new()
    {
        [ModelActor.Device1] = 1000,
        [ModelActor.Device2] = 2000,
        [ModelActor.Server] = 3000,
    };
    private int _counter;

    // 端末ごとに、この生成器が挿入した行（表, キー）。メモリに持っていた実体で消す操作の候補
    private readonly Dictionary<ModelActor, List<(string Table, string Key)>> _held = new()
    {
        [ModelActor.Device1] = [],
        [ModelActor.Device2] = [],
    };

    /// <summary>
    /// 種の組（種を 4 で割った余り）。0＝失敗の注入なし（区切り 29-1 の操作だけ）・1＝キーだけのスタブなどの操作を足す・
    /// 2＝失敗の注入を足す・3＝両方。組ごとに乱数の引き方を分けるので、組 0 の種の操作の列は、操作を足しても変わらない。
    /// 外部の ISyncTable 実装の代役は乱数の種では使わない（エンジンの内部の面を持たない実装は、外部キーで断られた書き込みの
    /// 報告などを持たず、公開の面どおりに例外を投げる＝その差は不具合ではない）。代役は決定的なシナリオだけが使う。
    /// </summary>
    /// <remarks>
    /// モードとポリシーは種の中で固定し、乱数でなく種の番号から決める（種を 4 で割った商を、モード×ポリシーの 6 組へ順に
    /// 配る）。種の組と合わせて、続いた 24 種ごとにモード×ポリシー×種の組の全部の組が 1 回ずつ出る（乱数で選ぶと、既定の
    /// 60 種で特定の組がたまたま出ないことがあった）。
    /// </remarks>
    public SyncModelGenerator(int seed)
    {
        _random = new Random(seed);
        var modes = Enum.GetValues<SyncMode>();
        var policies = Enum.GetValues<SyncConflictPolicy>();
        var settings = seed / 4 % (modes.Length * policies.Length);
        Mode = modes[settings / policies.Length];
        Policy = policies[settings % policies.Length];
        Extras = seed % 4 is 1 or 3;
        Faults = seed % 4 is 2 or 3;
    }

    /// <summary>同期の操作で削除伝搬を切る割合</summary>
    public const double PropagateDeletesOffRate = 0.25;

    /// <summary>キーだけのスタブ・持っていた実体での削除・同じキーでの作り直し・失敗する書き込みを足すか</summary>
    public bool Extras { get; }

    /// <summary>失敗を仕掛ける操作を足すか</summary>
    public bool Faults { get; }

    /// <summary>この種の同期のモード（種の中で固定）</summary>
    public SyncMode Mode { get; }

    /// <summary>この種の競合ポリシー（種の中で固定）</summary>
    public SyncConflictPolicy Policy { get; }

    /// <summary>初期データ（サーバーの他者の書き込み）</summary>
    public IReadOnlyList<ModelWriteOp> Setup()
    {
        var ops = new List<ModelWriteOp>();
        var folders = new List<int>();

        for (var id = 1; id <= 4; id++)
        {
            int? parent = folders.Count == 0 || _random.Next(2) == 0 ? null : Pick(folders);
            ops.Add(
                new AddFolderOp(ModelActor.Server, ModelWriteVia.Direct, id, parent, Value("f"))
            );
            folders.Add(id);
        }

        var labels = new List<string>();

        for (var i = 0; i < 2; i++)
        {
            var code = LabelCode(ModelActor.Server);
            ops.Add(
                new AddLabelOp(
                    ModelActor.Server,
                    ModelWriteVia.Direct,
                    code,
                    Pick(folders),
                    Value("l")
                )
            );
            labels.Add(code);
        }

        var items = new List<int>();

        for (var id = 1; id <= 4; id++)
        {
            ops.Add(
                new AddItemOp(
                    ModelActor.Server,
                    ModelWriteVia.Direct,
                    id,
                    Pick(folders),
                    _random.Next(2) == 0 ? null : Pick(labels),
                    $"c0-{id}",
                    Value("t"),
                    Payload()
                )
            );
            items.Add(id);
        }

        for (var i = 0; i < 3; i++)
        {
            ops.Add(
                new AddEventOp(
                    ModelActor.Server,
                    ModelWriteVia.Direct,
                    EventTime(),
                    Pick(items),
                    Value("m")
                )
            );
        }

        return ops;
    }

    /// <summary>
    /// 次の 1 歩（書き込み 1 つか 2 つ、または同期 1 回）を作る。
    /// </summary>
    /// <param name="states">書き手ごとの DB の現在の内容</param>
    public IReadOnlyList<ModelOp> Next(IReadOnlyDictionary<ModelActor, ModelDbState> states)
    {
        if (Faults && _random.NextDouble() < 0.12)
        {
            var device = _random.Next(2) == 0 ? ModelActor.Device1 : ModelActor.Device2;
            var kinds = Enum.GetValues<ModelFaultKind>()
                .Where(kind => kind != ModelFaultKind.ApplySkip)
                .ToArray();

            return [new ArmFaultOp(device, kinds[_random.Next(kinds.Length)])];
        }

        if (_random.NextDouble() < 0.18)
        {
            return [NextSync()];
        }

        var roll = _random.NextDouble();
        var actor =
            roll < 0.42 ? ModelActor.Device1
            : roll < 0.84 ? ModelActor.Device2
            : ModelActor.Server;
        var state = states[actor];

        for (var attempt = 0; attempt < 30; attempt++)
        {
            var ops =
                Extras && actor != ModelActor.Server && _random.NextDouble() < 0.25
                    ? TryExtra(actor, state)
                    : TryWrite(actor, state);

            if (ops is not null)
            {
                return ops;
            }
        }

        return [NextSync()];
    }

    /// <summary>同期の操作のダウンロードのバッチの大きさ</summary>
    public static readonly int[] Batches = [1, 2, 3, 500];

    private ModelSyncOp NextSync()
    {
        var device = _random.Next(2) == 0 ? ModelActor.Device1 : ModelActor.Device2;
        var kindRoll = _random.NextDouble();
        var kind =
            kindRoll < 0.8 ? ModelSyncKind.Sync
            : kindRoll < 0.9 ? ModelSyncKind.Upload
            : ModelSyncKind.Download;
        IReadOnlyList<string> excluded =
            _random.NextDouble() < 0.15 ? [Pick(ModelTables.All.ToList())] : [];
        var batch = Batches[_random.Next(Batches.Length)];
        var propagateDeletes = _random.NextDouble() >= PropagateDeletesOffRate;

        // blob は常に運ぶ。運ばないランを挟むと、そのランで降りた行・上がった行の blob はその後の運ぶランでも動かない
        // （文書どおりの割り切り＝運ぶかは端末ごとにそろえる）ため、収束の表明が成り立たない
        return new ModelSyncOp(device, kind, Mode, Policy, excluded, true, batch, propagateDeletes);
    }

    private IReadOnlyList<ModelOp>? TryWrite(ModelActor actor, ModelDbState state)
    {
        var ops = TryWriteCore(actor, state);

        if (ops is not null && actor != ModelActor.Server)
        {
            foreach (var op in ops)
            {
                if (op is AddFolderOp or AddLabelOp or AddItemOp or AddEventOp)
                {
                    var write = (ModelWriteOp)op;
                    _held[actor].Add((write.Table, write.KeyText));
                }
            }
        }

        return ops;
    }

    /// <summary>
    /// キーだけのスタブでの削除・持っていた実体での削除・同じキーでの作り直し・失敗する書き込み（端末だけ）。
    /// どれも書き手の DB で成り立つ形だけを選ぶ（失敗する書き込みは、業務の書き込みが失敗する形として選ぶ）。
    /// </summary>
    private IReadOnlyList<ModelOp>? TryExtra(ModelActor actor, ModelDbState state)
    {
        var leaves = LeafRows(state);

        switch (_random.Next(5))
        {
            case 0:
            {
                if (leaves.Count == 0)
                {
                    return null;
                }

                var (table, key) = Pick(leaves);

                return [new DeleteStubOp(actor, table, key)];
            }

            case 1:
            {
                var held = _held[actor].Where(leaves.Contains).ToList();

                if (held.Count == 0)
                {
                    return null;
                }

                var (table, key) = Pick(held);
                _held[actor].Remove((table, key));

                return [new DeleteHeldOp(actor, table, key)];
            }

            case 2:
                return Reinsert(actor, state, leaves);

            case 3:
            {
                var items = state.Items.Keys.Order().ToList();

                return items.Count == 0
                    ? null
                    : [new WritePayloadOp(actor, Pick(items), Payload()) { ExpectFailure = true }];
            }

            default:
            {
                // 一意な code の重複で業務の挿入が失敗する（記録だけが残る）
                var items = state.Items.Keys.Order().ToList();

                if (items.Count == 0)
                {
                    return null;
                }

                var existing = state.Items[Pick(items)];

                return
                [
                    new AddItemOp(
                        actor,
                        ModelWriteVia.Direct,
                        NewId(actor),
                        existing.FolderId,
                        null,
                        existing.Code,
                        Value("t"),
                        Payload()
                    )
                    {
                        ExpectFailure = true,
                    },
                ];
            }
        }
    }

    /// <summary>葉の行（その行を参照する行が書き手の DB に無いもの）</summary>
    private static List<(string Table, string Key)> LeafRows(ModelDbState state)
    {
        var leaves = new List<(string Table, string Key)>();
        leaves.AddRange(
            state
                .Folders.Keys.Order()
                .Where(f =>
                    !state.Folders.Values.Any(v => v.ParentId == f)
                    && !state.Labels.Values.Any(v => v.FolderId == f)
                    && !state.Items.Values.Any(v => v.FolderId == f)
                )
                .Select(f => (ModelTables.Folders, ModelDbState.Key(f)))
        );
        leaves.AddRange(
            state
                .Labels.Keys.Order(StringComparer.Ordinal)
                .Where(l => !state.Items.Values.Any(v => v.LabelCode == l))
                .Select(l => (ModelTables.Labels, l))
        );
        leaves.AddRange(
            state
                .Items.Keys.Order()
                .Where(i => !state.Events.Values.Any(v => v.ItemId == i))
                .Select(i => (ModelTables.Items, ModelDbState.Key(i)))
        );
        leaves.AddRange(
            state.Events.Keys.Order(StringComparer.Ordinal).Select(e => (ModelTables.Events, e))
        );

        return leaves;
    }

    /// <summary>葉の行を消して、同じキーで作り直す（同期を挟まない 2 つの書き込み）</summary>
    private IReadOnlyList<ModelOp>? Reinsert(
        ModelActor actor,
        ModelDbState state,
        List<(string Table, string Key)> leaves
    )
    {
        if (leaves.Count == 0)
        {
            return null;
        }

        var via = _random.Next(2) == 0 ? ModelWriteVia.Direct : ModelWriteVia.Graph;
        var (table, key) = Pick(leaves);

        switch (table)
        {
            case ModelTables.Folders:
            {
                var id = int.Parse(key, CultureInfo.InvariantCulture);
                var row = state.Folders[id];

                return
                [
                    new DeleteFolderOp(actor, via, id),
                    new AddFolderOp(actor, via, id, row.ParentId, Value("f")),
                ];
            }

            case ModelTables.Labels:
            {
                var row = state.Labels[key];

                return
                [
                    new DeleteLabelOp(actor, via, key),
                    new AddLabelOp(actor, via, key, row.FolderId, Value("l")),
                ];
            }

            case ModelTables.Items:
            {
                var id = int.Parse(key, CultureInfo.InvariantCulture);
                var row = state.Items[id];

                return
                [
                    new DeleteItemOp(actor, via, id),
                    new AddItemOp(
                        actor,
                        via,
                        id,
                        row.FolderId,
                        row.LabelCode,
                        row.Code,
                        Value("t"),
                        Payload()
                    ),
                ];
            }

            default:
            {
                var row = state.Events[key];

                return
                [
                    new DeleteEventOp(actor, via, row.At),
                    new AddEventOp(actor, via, row.At, row.ItemId, Value("m")),
                ];
            }
        }
    }

    private IReadOnlyList<ModelOp>? TryWriteCore(ModelActor actor, ModelDbState state)
    {
        var via = _random.Next(2) == 0 ? ModelWriteVia.Direct : ModelWriteVia.Graph;
        var folders = state.Folders.Keys.Order().ToList();
        var labels = state.Labels.Keys.Order(StringComparer.Ordinal).ToList();
        var items = state.Items.Keys.Order().ToList();
        var events = state.Events.Keys.Order(StringComparer.Ordinal).ToList();

        switch (_random.Next(16))
        {
            case 15:
                // 一意な値の付け替え（品目の code・フォルダーの name）
                return TryUniqueShape(actor, via, state);

            case 0
            or 1:
            {
                int? parent = folders.Count == 0 || _random.Next(3) == 0 ? null : Pick(folders);

                return [new AddFolderOp(actor, via, NewId(actor), parent, Value("f"))];
            }

            case 2:
            {
                if (folders.Count == 0)
                {
                    return null;
                }

                var id = Pick(folders);
                var parent = state.Folders[id].ParentId;

                if (_random.Next(2) == 0)
                {
                    var candidates = folders.Where(f => !IsDescendantOrSelf(state, f, id)).ToList();
                    parent =
                        candidates.Count == 0 || _random.Next(4) == 0 ? null : Pick(candidates);
                }

                return [new UpdateFolderOp(actor, via, id, parent, Value("f"))];
            }

            case 3:
            {
                var leaves = folders
                    .Where(f =>
                        !state.Folders.Values.Any(v => v.ParentId == f)
                        && !state.Labels.Values.Any(v => v.FolderId == f)
                        && !state.Items.Values.Any(v => v.FolderId == f)
                    )
                    .ToList();

                return leaves.Count == 0 ? null : [new DeleteFolderOp(actor, via, Pick(leaves))];
            }

            case 4:
                return folders.Count == 0
                    ? null
                    : [new AddLabelOp(actor, via, LabelCode(actor), Pick(folders), Value("l"))];

            case 5:
            {
                if (labels.Count == 0)
                {
                    return null;
                }

                var code = Pick(labels);
                var folder =
                    _random.Next(3) == 0 && folders.Count > 0
                        ? Pick(folders)
                        : state.Labels[code].FolderId;

                return [new UpdateLabelOp(actor, via, code, folder, Value("l"))];
            }

            case 6:
            {
                var free = labels
                    .Where(l => !state.Items.Values.Any(v => v.LabelCode == l))
                    .ToList();

                return free.Count == 0 ? null : [new DeleteLabelOp(actor, via, Pick(free))];
            }

            case 7
            or 8:
            {
                if (folders.Count == 0)
                {
                    return null;
                }

                var id = NewId(actor);

                return
                [
                    new AddItemOp(
                        actor,
                        via,
                        id,
                        Pick(folders),
                        labels.Count == 0 || _random.Next(2) == 0 ? null : Pick(labels),
                        ItemCode(actor, id),
                        Value("t"),
                        Payload()
                    ),
                ];
            }

            case 9:
            {
                if (items.Count == 0)
                {
                    return null;
                }

                var id = Pick(items);
                var row = state.Items[id];
                var folder =
                    _random.Next(5) == 0 && folders.Count > 0 ? Pick(folders) : row.FolderId;
                var label = row.LabelCode;

                if (_random.Next(5) == 0)
                {
                    label = labels.Count == 0 || _random.Next(3) == 0 ? null : Pick(labels);
                }

                return [new UpdateItemOp(actor, via, id, folder, label, row.Code, Value("t"))];
            }

            case 10:
            {
                var free = items.Where(i => !state.Events.Values.Any(v => v.ItemId == i)).ToList();

                return free.Count == 0 ? null : [new DeleteItemOp(actor, via, Pick(free))];
            }

            case 11:
                return items.Count == 0
                    ? null
                    : [new WritePayloadOp(actor, Pick(items), Payload())];

            case 12:
            {
                // 一意な列の「削除→同じ値で挿入」（同期を挟まない 2 つの書き込み）。ほかの操作より頻度を下げる
                // （この形の既知の失敗が種の大半を止めて、後ろの不具合を隠さないように）
                if (_random.Next(4) != 0)
                {
                    return null;
                }

                var free = items.Where(i => !state.Events.Values.Any(v => v.ItemId == i)).ToList();

                if (free.Count == 0)
                {
                    return null;
                }

                var old = Pick(free);
                var row = state.Items[old];

                return
                [
                    new DeleteItemOp(actor, via, old),
                    new AddItemOp(
                        actor,
                        via,
                        NewId(actor),
                        row.FolderId,
                        row.LabelCode,
                        row.Code,
                        Value("t"),
                        Payload()
                    ),
                ];
            }

            case 13:
                return items.Count == 0
                    ? null
                    : [new AddEventOp(actor, via, EventTime(), Pick(items), Value("m"))];

            default:
            {
                if (events.Count == 0)
                {
                    return null;
                }

                var key = Pick(events);
                var row = state.Events[key];

                if (_random.Next(2) == 0)
                {
                    return [new DeleteEventOp(actor, via, row.At)];
                }

                var item = _random.Next(5) == 0 && items.Count > 0 ? Pick(items) : row.ItemId;

                return [new UpdateEventOp(actor, via, row.At, item, Value("m"))];
            }
        }
    }

    /// <summary>形：更新で一意な値を空け、別の行がその値を更新で取る（4m-D3）</summary>
    public const string ReleaseTakeShape = "release-take";

    /// <summary>形：空けた値を取った行の値を、さらに別の行が取る（3 行の連鎖。循環＝入れ替えは作らない）</summary>
    public const string ReleaseTakeChainShape = "release-take-chain";

    /// <summary>形：行を消して一意な値を空け、別の行がその値を更新で取る（4m-B3 の材料）</summary>
    public const string DeleteTakeShape = "delete-take";

    /// <summary>形：消す行の一意な値を別の行が取って別の親へ付け替え、元の親（葉になる）を消す（4m-D4）</summary>
    public const string DeleteTakeReparentShape = "delete-take-reparent";

    /// <summary>
    /// 一意な値の付け替えの形（品目の code かフォルダーの name）を 1 つ作る。どれも書き手の DB で各手が成り立つ順に並べ、
    /// 値を取る手は、それより前の手が値を空けた後に置く（2 行の入れ替えは作らない＝同じバッチの入れ替えは docs の既知の割り切り）。
    /// </summary>
    private ModelShapeStep? TryUniqueShape(ModelActor actor, ModelWriteVia via, ModelDbState state)
    {
        var items = _random.Next(2) == 0;
        var table = items ? ModelTables.Items : ModelTables.Folders;
        var shape = _random.Next(4) switch
        {
            0 => ReleaseTakeShape,
            1 => ReleaseTakeChainShape,
            2 => DeleteTakeShape,
            _ => DeleteTakeReparentShape,
        };
        var ops = shape switch
        {
            ReleaseTakeShape => ReleaseTake(actor, via, state, items, 2),
            ReleaseTakeChainShape => ReleaseTake(actor, via, state, items, 3),
            DeleteTakeShape => DeleteTake(actor, via, state, items),
            _ => DeleteTakeReparent(actor, via, state, items),
        };

        return ops is null ? null : new ModelShapeStep(shape, table, ops);
    }

    /// <summary>
    /// <paramref name="length"/> 行の連鎖：先頭の行の値を新しい値へ、続く行はそれぞれ直前の行の元の値へ更新する。
    /// 半分の確率で、2 番目の行（最初に値を取る行）を先に別の更新で触る（取る側の記録を先にする＝4m-D3 の記録の順）
    /// </summary>
    private List<ModelOp>? ReleaseTake(
        ModelActor actor,
        ModelWriteVia via,
        ModelDbState state,
        bool items,
        int length
    )
    {
        var keys = items ? state.Items.Keys.Order().ToList() : state.Folders.Keys.Order().ToList();

        if (keys.Count < length)
        {
            return null;
        }

        var chain = new List<int>();

        for (var i = 0; i < length; i++)
        {
            var rest = keys.Except(chain).ToList();
            chain.Add(Pick(rest));
        }

        var ops = new List<ModelOp>();

        if (_random.Next(2) == 0)
        {
            ops.Add(Touch(actor, via, state, items, chain[1]));
        }

        ops.Add(WithValue(actor, via, state, items, chain[0], items ? Value("c") : Value("f")));

        for (var i = 1; i < length; i++)
        {
            ops.Add(
                WithValue(actor, via, state, items, chain[i], UniqueOf(state, items, chain[i - 1]))
            );
        }

        return ops;
    }

    /// <summary>葉の行を消して値を空け、別の行がその値を更新で取る</summary>
    private List<ModelOp>? DeleteTake(
        ModelActor actor,
        ModelWriteVia via,
        ModelDbState state,
        bool items
    )
    {
        var leaves = items ? FreeItems(state) : LeafFolders(state);

        if (leaves.Count == 0)
        {
            return null;
        }

        var deleted = Pick(leaves);
        var takers = (items ? state.Items.Keys.ToList() : state.Folders.Keys.ToList())
            .Where(k => k != deleted)
            .Order()
            .ToList();

        if (takers.Count == 0)
        {
            return null;
        }

        var taker = Pick(takers);

        return
        [
            Delete(actor, via, items, deleted),
            WithValue(actor, via, state, items, taker, UniqueOf(state, items, deleted)),
        ];
    }

    /// <summary>
    /// 親 P の子 X を消し、P のほかの子を全部別の親 Q へ付け替え（最初の子が X の値を取る）、葉になった P を消す
    /// </summary>
    private List<ModelOp>? DeleteTakeReparent(
        ModelActor actor,
        ModelWriteVia via,
        ModelDbState state,
        bool items
    )
    {
        var folders = state.Folders.Keys.Order().ToList();

        // P：付け替える表の子（品目ならその品目、フォルダーなら子のフォルダー）が 2 つ以上あり、そのうち X が消せる子。
        // P を参照するほかの行（ラベル・もう一方の種類の子）も Q へ付け替えてから P を消す
        var candidates = folders
            .Select(p => (Parent: p, Children: Children(state, items, p)))
            .Where(c =>
                c.Children.Count >= 2 && c.Children.Any(child => IsLeafChild(state, items, child))
            )
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var (parent, children) = Pick(candidates);
        var deleted = Pick(children.Where(child => IsLeafChild(state, items, child)).ToList());
        var targets = folders.Where(q => !IsDescendantOrSelf(state, q, parent)).ToList();

        if (targets.Count == 0)
        {
            return null;
        }

        var target = Pick(targets);
        var moved = children.Where(child => child != deleted).ToList();
        var ops = new List<ModelOp> { Delete(actor, via, items, deleted) };

        for (var i = 0; i < moved.Count; i++)
        {
            var value = i == 0 ? UniqueOf(state, items, deleted) : UniqueOf(state, items, moved[i]);
            ops.Add(WithValue(actor, via, state, items, moved[i], value, target));
        }

        // もう一方の種類の子とラベルも Q へ付け替える（一意な値は変えない）
        foreach (var other in Children(state, !items, parent))
        {
            ops.Add(
                WithValue(actor, via, state, !items, other, UniqueOf(state, !items, other), target)
            );
        }

        foreach (
            var label in state
                .Labels.Where(l => l.Value.FolderId == parent)
                .Select(l => l.Key)
                .Order(StringComparer.Ordinal)
        )
        {
            ops.Add(new UpdateLabelOp(actor, via, label, target, Value("l")));
        }

        ops.Add(new DeleteFolderOp(actor, via, parent));

        return ops;
    }

    /// <summary>親のフォルダーの子（品目ならそのフォルダーの品目・フォルダーなら子のフォルダー）</summary>
    private static List<int> Children(ModelDbState state, bool items, int parent) =>
        items
            ? state.Items.Where(p => p.Value.FolderId == parent).Select(p => p.Key).Order().ToList()
            : state
                .Folders.Where(p => p.Value.ParentId == parent)
                .Select(p => p.Key)
                .Order()
                .ToList();

    /// <summary>消せる子か（品目はイベントが参照しない・フォルダーは葉）</summary>
    private static bool IsLeafChild(ModelDbState state, bool items, int key) =>
        items ? !state.Events.Values.Any(e => e.ItemId == key) : LeafFolders(state).Contains(key);

    /// <summary>イベントが参照しない品目</summary>
    private static List<int> FreeItems(ModelDbState state) =>
        state.Items.Keys.Order().Where(i => !state.Events.Values.Any(v => v.ItemId == i)).ToList();

    /// <summary>葉のフォルダー（子のフォルダー・ラベル・品目のどれからも参照されない）</summary>
    private static List<int> LeafFolders(ModelDbState state) =>
        state
            .Folders.Keys.Order()
            .Where(f =>
                !state.Folders.Values.Any(v => v.ParentId == f)
                && !state.Labels.Values.Any(v => v.FolderId == f)
                && !state.Items.Values.Any(v => v.FolderId == f)
            )
            .ToList();

    /// <summary>行の一意な値（品目の code・フォルダーの name）</summary>
    private static string UniqueOf(ModelDbState state, bool items, int key) =>
        items ? state.Items[key].Code : state.Folders[key].Name;

    /// <summary>行の一意な値を <paramref name="value"/> へ（品目は題名も新しくする・<paramref name="parent"/> があれば付け替える）</summary>
    private ModelWriteOp WithValue(
        ModelActor actor,
        ModelWriteVia via,
        ModelDbState state,
        bool items,
        int key,
        string value,
        int? parent = null
    )
    {
        if (items)
        {
            var row = state.Items[key];

            return new UpdateItemOp(
                actor,
                via,
                key,
                parent ?? row.FolderId,
                row.LabelCode,
                value,
                Value("t")
            );
        }

        return new UpdateFolderOp(actor, via, key, parent ?? state.Folders[key].ParentId, value);
    }

    /// <summary>一意な値を変えない更新（品目は題名を新しく・フォルダーは同じ内容で書き直す＝記録だけが付く）</summary>
    private ModelWriteOp Touch(
        ModelActor actor,
        ModelWriteVia via,
        ModelDbState state,
        bool items,
        int key
    ) =>
        items
            ? new UpdateItemOp(
                actor,
                via,
                key,
                state.Items[key].FolderId,
                state.Items[key].LabelCode,
                state.Items[key].Code,
                Value("t")
            )
            : new UpdateFolderOp(
                actor,
                via,
                key,
                state.Folders[key].ParentId,
                state.Folders[key].Name
            );

    private static ModelWriteOp Delete(ModelActor actor, ModelWriteVia via, bool items, int key) =>
        items ? new DeleteItemOp(actor, via, key) : new DeleteFolderOp(actor, via, key);

    private static bool IsDescendantOrSelf(ModelDbState state, int candidate, int ancestor)
    {
        int? current = candidate;
        var guard = 0;

        while (current is { } c && guard++ < 1000)
        {
            if (c == ancestor)
            {
                return true;
            }

            current = state.Folders.TryGetValue(c, out var row) ? row.ParentId : null;
        }

        return false;
    }

    private T Pick<T>(IReadOnlyList<T> values) => values[_random.Next(values.Count)];

    private int NewId(ModelActor actor) => ++_nextId[actor];

    private string Value(string prefix) =>
        $"{prefix}{(++_counter).ToString(CultureInfo.InvariantCulture)}";

    private string LabelCode(ModelActor actor)
    {
        var who = actor switch
        {
            ModelActor.Device1 => "p",
            ModelActor.Device2 => "q",
            _ => "s",
        };

        return $"{LabelPrefixes[_random.Next(LabelPrefixes.Length)]}{who}{++_counter}";
    }

    private static string ItemCode(ModelActor actor, int id) =>
        $"c{(int)actor}-{id.ToString(CultureInfo.InvariantCulture)}";

    private string Payload()
    {
        var bytes = new byte[6];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), ++_counter);
        bytes[4] = (byte)_random.Next(256);
        bytes[5] = (byte)_random.Next(1, 256);

        return Convert.ToHexString(bytes);
    }

    private DateTime EventTime()
    {
        var fraction = _random.Next(0, 1_000_000) * 10 + _random.Next(1, 10);
        var value = BaseTime.AddSeconds(++_counter).AddTicks(fraction);
        var kind = (DateTimeKind)_random.Next(3);

        return DateTime.SpecifyKind(value, kind);
    }
}

/// <summary>
/// 生成器が一意な値の付け替えの形として返した 1 歩（操作の列と、形の名前・対象の表）。次元表の観測が形を数えるのに使う
/// （観測は返した値を読むだけで乱数を引かない）
/// </summary>
/// <param name="Shape">形の名前（<see cref="SyncModelGenerator.ReleaseTakeShape"/> など）</param>
/// <param name="Table">一意な値を付け替える表</param>
/// <param name="Ops">操作の列</param>
internal sealed record ModelShapeStep(string Shape, string Table, IReadOnlyList<ModelOp> Ops)
    : IReadOnlyList<ModelOp>
{
    public ModelOp this[int index] => Ops[index];

    public int Count => Ops.Count;

    public IEnumerator<ModelOp> GetEnumerator() => Ops.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}
