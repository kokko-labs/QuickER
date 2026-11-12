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
/// 書き込みは、書き手の DB の現在の内容を見て、その DB で成功する操作だけを選ぶ。作らない形（失敗する書き込み・同じキーの挿入など）は
/// <see cref="SyncModelNotGenerated"/> に宣言する。返す前に、主キーと一意な値の分を <see cref="CheckWriterContract"/> で確かめ、
/// 破れば生成器の誤り（署名 <c>gen/writer-contract:*</c>）として名指しする。
/// </para>
/// <para>
/// キーは書き手ごとに別の範囲から採る（端末 1＝1000 台・端末 2＝2000 台・サーバーの他者＝3000 台・端末 3＝4000 台・初期データ＝1 桁）。
/// 世界の端末の数は種の番号から決まる（<see cref="SyncModelWorldShape"/>）。端末 1・2 の世界の端末の選び方は、端末 3 を足す前の
/// 引き方のまま（既存の種の列を変えない）。
/// 値（名前・品目の code・題名・メモ・blob）は種の中で一意にし、同じ値へ戻る書き込み（ABA）で表明が誤って通らないようにする
/// （品目の挿入の既定の code は <see cref="FreshItemCode"/>）。
/// 日時のキーは固定の基準時刻からの通し番号＋種から作る端数で、Kind（Utc / Local / Unspecified）も種から選ぶ。
/// 端数の最後の桁は 0 にしない（格納の文字列の末尾の 0 の省き方に照合を依存させない）。
/// </para>
/// <para>
/// 本流の乱数（操作の種類・対象・値）と別に、種から決まる 2 本目の乱数（<see cref="SideSeed"/>）で、本流が選んだ手の
/// キー・一意な値・blob の大きさ・除外する表を低い確率で置き換える（本流の乱数の引き方は変えない）。置き換える形は、
/// ほかの書き手の DB にあって自分の DB に無いキー（<see cref="StolenKeyRate"/>）、一意な値の別表記（<see cref="SpellingRate"/>・
/// <see cref="UniqueSpelling"/>）、大きな blob（<see cref="LargePayloadRate"/>）、2 表以上の除外（<see cref="MultipleExclusionRate"/>）。
/// 置き換えた手は書き手の DB で成功する手のまま（自分の DB に同じキー・同じ値〔序数〕が無いものだけを選ぶ）。
/// 初期データの 2 番目のフォルダーの名前と 2 番目の品目の code は非 ASCII の文字で始める（非 ASCII の大文字小文字の別表記の
/// 土台を種の初めから置く）。
/// </para>
/// <para>
/// 広い世界の種の端末の最初の行（<see cref="Preloads"/>）は、3 本目の乱数（<see cref="PreloadSeed"/>）だけで引く。Repository を
/// 通らずに入れた行のキーは端末のキーの範囲の別の帯（<see cref="SyncModelWorldShape.UntrackedKeyOffset"/>）に置き、ほかの書き手の
/// 同じキー・一意な値の別表記の候補にしない（サーバーにあるキーの追跡しない行・追跡しない行と同じ一意な値の行を作らない）。
/// </para>
/// </remarks>
internal sealed class SyncModelGenerator
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified);

    // ラベルのキーの頭（大文字小文字・'-'・'_' を混ぜる＝照合順序でキー順が変わり得る文字）
    private static readonly string[] LabelPrefixes = ["a-", "A_", "b", "B-", "_c", "-d", "z"];

    /// <summary>
    /// 非 ASCII の一意な値の頭（大文字小文字が 1 文字ずつ対応する BMP の文字だけ。SQL Server の照合順序でサロゲートペアの
    /// 比べ方は版で変わるので入れない）
    /// </summary>
    public static readonly string[] NonAsciiPrefixes = ["é", "Ж", "ñ", "Ω"];

    /// <summary>大きな blob のバイト数（ストリームのコピーの区切り 81,920 バイトの 2 つ分を越え、区切りの倍数にしない）</summary>
    public const int LargePayloadBytes = 81_920 * 2 + 7;

    /// <summary>挿入の手で、ほかの書き手の DB にあって自分の DB に無いキーを選ぶ割合（2 本目の乱数）</summary>
    public const double StolenKeyRate = 0.2;

    /// <summary>一意な値を新しく書く手で、値を別表記へ置き換える割合（2 本目の乱数）</summary>
    public const double SpellingRate = 0.3;

    /// <summary>
    /// 別表記の形の選び方（2 本目の乱数で 1 つ選ぶ。非 ASCII の新しい値と、その大文字小文字の別表記を厚くする＝別表記は
    /// どこかの DB にある非 ASCII の値を必要とする）
    /// </summary>
    private static readonly UniqueSpelling[] SpellingKinds =
    [
        UniqueSpelling.Exact,
        UniqueSpelling.AsciiCase,
        UniqueSpelling.NonAscii,
        UniqueSpelling.NonAsciiCase,
        UniqueSpelling.NonAsciiCase,
        UniqueSpelling.TrailingSpace,
        UniqueSpelling.Empty,
    ];

    /// <summary>blob を書く手で、大きな blob にする割合（2 本目の乱数）</summary>
    public const double LargePayloadRate = 0.25;

    /// <summary>本流が何も除外しなかった同期を、2〜3 表の除外にする割合（2 本目の乱数）</summary>
    public const double MultipleExclusionRate = 0.1;

    /// <summary>2 本目の乱数の種（本流の種から決定的に作る）</summary>
    /// <param name="seed">本流の種</param>
    public static int SideSeed(int seed) => unchecked(seed * 7919 + 104729);

    /// <summary>端末の最初の行の中身を引く乱数の種（本流の種から決定的に作る。本流・2 本目の乱数と混ぜない）</summary>
    /// <param name="seed">本流の種</param>
    public static int PreloadSeed(int seed) => unchecked(seed * 104723 + 7907);

    private readonly Random _random;
    private readonly Random _side;
    private readonly Random _preload;
    private IReadOnlyDictionary<ModelActor, ModelDbState> _states =
        new Dictionary<ModelActor, ModelDbState>();
    private readonly Dictionary<ModelActor, int> _nextId = new()
    {
        [ModelActor.Device1] = 1000,
        [ModelActor.Device2] = 2000,
        [ModelActor.Server] = 3000,
        [ModelActor.Device3] = 4000,
    };
    private int _counter;

    // この生成器が品目の挿入の既定の code として出した値（盗んだキーで同じ値を 2 回出さないため。FreshItemCode）
    private readonly HashSet<string> _issuedItemCodes = new(StringComparer.Ordinal);

    // 端末ごとに、この生成器が挿入した行（表, キー）。メモリに持っていた実体で消す操作の候補
    private readonly Dictionary<ModelActor, List<(string Table, string Key)>> _held = new()
    {
        [ModelActor.Device1] = [],
        [ModelActor.Device2] = [],
        [ModelActor.Device3] = [],
    };

    /// <summary>
    /// 種の組（種を 4 で割った余り）。0＝失敗の注入なし（区切り 29-1 の操作だけ）・1＝キーだけのスタブなどの操作を足す・
    /// 2＝失敗の注入を足す・3＝両方。組ごとに乱数の引き方を分けるので、組 0 の種の操作の列は、操作を足しても変わらない。
    /// </summary>
    /// <remarks>
    /// モードとポリシーは種の中で固定し、乱数でなく種の番号から決める（種を 4 で割った商を、モード×ポリシーの 6 組へ順に
    /// 配る）。種の組と合わせて、続いた 24 種ごとにモード×ポリシー×種の組の全部の組が 1 回ずつ出る（乱数で選ぶと、既定の
    /// 60 種で特定の組がたまたま出ないことがあった）。
    /// </remarks>
    public SyncModelGenerator(int seed)
    {
        _random = new Random(seed);
        _side = new Random(SideSeed(seed));
        _preload = new Random(PreloadSeed(seed));
        var modes = Enum.GetValues<SyncMode>();
        var policies = Enum.GetValues<SyncConflictPolicy>();
        var settings = seed / 4 % (modes.Length * policies.Length);
        Mode = modes[settings / policies.Length];
        Policy = policies[settings % policies.Length];
        Extras = seed % 4 is 1 or 3;
        Faults = seed % 4 is 2 or 3;
        Shape = SyncModelWorldShape.For(seed);
    }

    /// <summary>この種の世界の形（種の番号から決める。乱数は引かない）</summary>
    public SyncModelWorldShape Shape { get; }

    /// <summary>
    /// 本流の乱数で端末を 1 台選ぶ（同期・失敗の仕掛けの対象）。端末 1・2 の世界は既存の種の列を変えない引き方のまま
    /// </summary>
    private ModelActor PickDevice() =>
        Shape.Devices.Count == 2
            ? _random.Next(2) == 0
                ? ModelActor.Device1
                : ModelActor.Device2
            : Shape.Devices[_random.Next(Shape.Devices.Count)];

    /// <summary>
    /// 書き込みの書き手（本流の乱数の 1 回の値から）。端末で 0.84 を等分し、残りがサーバーの他者。端末 1・2 の世界は
    /// 既存の種の列を変えない境目（0.42・0.84）のまま
    /// </summary>
    private ModelActor WriterFor(double roll)
    {
        const double devicesShare = 0.84;

        if (roll >= devicesShare)
        {
            return ModelActor.Server;
        }

        if (Shape.Devices.Count == 2)
        {
            return roll < 0.42 ? ModelActor.Device1 : ModelActor.Device2;
        }

        var index = (int)(roll / (devicesShare / Shape.Devices.Count));

        return Shape.Devices[Math.Min(index, Shape.Devices.Count - 1)];
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
            var name = id == 2 ? NonAsciiPrefixes[0] + Value("f") : Value("f");
            ops.Add(new AddFolderOp(ModelActor.Server, ModelWriteVia.Direct, id, parent, name));
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
                    id == 2 ? $"{NonAsciiPrefixes[1]}c0-{id}" : $"c0-{id}",
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
    /// 端末ごとの最初の形と中身（<see cref="Setup"/> の後、1 歩目の前に 1 回呼ぶ）。中身は 3 本目の乱数
    /// （<see cref="PreloadSeed"/>）だけで引く（本流・2 本目の乱数を引かない）。空から始める端末の中身は空
    /// </summary>
    /// <param name="setup">初期データ（<see cref="Setup"/> の戻り値）</param>
    public IReadOnlyList<SyncModelPreload> Preloads(IReadOnlyList<ModelWriteOp> setup) =>
        [
            .. Shape.Devices.Select(
                (device, i) =>
                    new SyncModelPreload(
                        device,
                        Shape.Preloads[i],
                        Shape.Preloads[i] switch
                        {
                            ModelPreload.Journaled => JournaledPreload(device, setup),
                            ModelPreload.Untracked => UntrackedPreload(device),
                            _ => [],
                        }
                    )
            ),
        ];

    /// <summary>
    /// 最初の同期の前に Repository 越しに書く手（端末の DB は空）。フォルダーの親子・ラベル・品目・イベントを足し、一部を
    /// 名前の変更・品目の削除（足した直後に消す＝打ち消し合う記録）で書き換える。一部は初期データとぶつける：最初の
    /// フォルダー・品目のキーを初期データのキーにする（サーバーで DuplicateOnServer）か、名前・code を初期データの値にする
    /// （UniqueValueTakenOnServer）
    /// </summary>
    private List<ModelWriteOp> JournaledPreload(
        ModelActor device,
        IReadOnlyList<ModelWriteOp> setup
    )
    {
        var initialFolders = setup.OfType<AddFolderOp>().ToList();
        var initialItems = setup.OfType<AddItemOp>().ToList();
        var ops = new List<ModelWriteOp>();
        var folders = new Dictionary<int, int?>();
        var collided = false;

        ModelWriteVia Via() => _preload.Next(3) == 0 ? ModelWriteVia.Graph : ModelWriteVia.Direct;

        T PickPreload<T>(IReadOnlyList<T> values) => values[_preload.Next(values.Count)];

        for (var count = _preload.Next(2, 4); folders.Count < count; )
        {
            var roll = _preload.NextDouble();
            var id = !collided && roll < 0.3 ? PickPreload(initialFolders).Id : NewId(device);
            var name =
                !collided && roll is >= 0.3 and < 0.55
                    ? PickPreload(initialFolders).Name
                    : Value("f");
            collided |= roll < 0.55;
            int? parent =
                folders.Count == 0 || _preload.Next(2) == 0 ? null : PickPreload([.. folders.Keys]);
            ops.Add(new AddFolderOp(device, Via(), id, parent, name));
            folders[id] = parent;
        }

        string? label = null;

        if (_preload.Next(2) == 0)
        {
            label = PreloadLabelCode(device);
            ops.Add(
                new AddLabelOp(
                    device,
                    ModelWriteVia.Direct,
                    label,
                    PickPreload([.. folders.Keys]),
                    Value("l")
                )
            );
        }

        var items = new List<int>();
        collided = false;

        for (var count = _preload.Next(2, 4); items.Count < count; )
        {
            var roll = _preload.NextDouble();
            var id = !collided && roll < 0.3 ? PickPreload(initialItems).Id : NewId(device);
            var code =
                !collided && roll is >= 0.3 and < 0.55
                    ? PickPreload(initialItems).Code
                    : Value("pc");
            collided |= roll < 0.55;
            ops.Add(
                new AddItemOp(
                    device,
                    Via(),
                    id,
                    PickPreload([.. folders.Keys]),
                    _preload.Next(2) == 0 ? label : null,
                    code,
                    Value("t"),
                    PreloadPayload()
                )
            );
            items.Add(id);
        }

        var referenced = new HashSet<int>();

        if (_preload.Next(2) == 0)
        {
            var item = PickPreload(items);
            referenced.Add(item);
            ops.Add(
                new AddEventOp(device, ModelWriteVia.Direct, PreloadEventTime(), item, Value("m"))
            );
        }

        if (_preload.Next(2) == 0)
        {
            var folder = PickPreload([.. folders.Keys]);
            ops.Add(new UpdateFolderOp(device, Via(), folder, folders[folder], Value("f")));
        }

        var removable = items.Where(item => !referenced.Contains(item)).ToList();

        if (removable.Count > 0 && _preload.Next(3) == 0)
        {
            ops.Add(new DeleteItemOp(device, ModelWriteVia.Direct, PickPreload(removable)));
        }

        return ops;
    }

    /// <summary>
    /// 最初の同期の前に Repository を通らずに入れる行（フォルダーの親子・ラベル・blob を持つ品目）。キーは端末のキーの範囲の
    /// 追跡しない帯（サーバーに無いキー）、名前・code は種の中で一意な新しい値
    /// </summary>
    private List<ModelWriteOp> UntrackedPreload(ModelActor device)
    {
        var start =
            device switch
            {
                ModelActor.Device1 => 1000,
                ModelActor.Device2 => 2000,
                ModelActor.Device3 => 4000,
                _ => throw new ArgumentOutOfRangeException(nameof(device), device, null),
            } + SyncModelWorldShape.UntrackedKeyOffset;
        var ops = new List<ModelWriteOp>();
        var folders = new List<int>();

        for (var count = _preload.Next(1, 4); folders.Count < count; )
        {
            var id = start + folders.Count + 1;
            int? parent = folders.Count == 0 || _preload.Next(2) == 0 ? null : folders[^1];
            ops.Add(new AddFolderOp(device, ModelWriteVia.Direct, id, parent, Value("uf")));
            folders.Add(id);
        }

        string? label = null;

        if (_preload.Next(2) == 0)
        {
            label = PreloadLabelCode(device);
            ops.Add(
                new AddLabelOp(
                    device,
                    ModelWriteVia.Direct,
                    label,
                    folders[_preload.Next(folders.Count)],
                    Value("ul")
                )
            );
        }

        for (int i = 0, count = _preload.Next(1, 4); i < count; i++)
        {
            ops.Add(
                new AddItemOp(
                    device,
                    ModelWriteVia.Direct,
                    start + 50 + i + 1,
                    folders[_preload.Next(folders.Count)],
                    _preload.Next(2) == 0 ? label : null,
                    Value("uc"),
                    Value("t"),
                    PreloadPayload()
                )
            );
        }

        return ops;
    }

    /// <summary>最初の行のラベルの code（<see cref="LabelCode"/> と同じ形を 3 本目の乱数で引く）</summary>
    private string PreloadLabelCode(ModelActor device)
    {
        var who = device switch
        {
            ModelActor.Device1 => "p",
            ModelActor.Device2 => "q",
            _ => "r",
        };

        return $"{LabelPrefixes[_preload.Next(LabelPrefixes.Length)]}{who}{++_counter}";
    }

    /// <summary>最初の行の blob（<see cref="Payload"/> と同じ形を 3 本目の乱数で引く。大きな blob にはしない）</summary>
    private string PreloadPayload()
    {
        var bytes = new byte[6];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), ++_counter);
        bytes[4] = (byte)_preload.Next(256);
        bytes[5] = (byte)_preload.Next(1, 256);

        return Convert.ToHexString(bytes);
    }

    /// <summary>最初の行のイベントの日時（<see cref="EventTime"/> と同じ形を 3 本目の乱数で引く）</summary>
    private DateTime PreloadEventTime()
    {
        var fraction = _preload.Next(0, 1_000_000) * 10 + _preload.Next(1, 10);
        var value = BaseTime.AddSeconds(++_counter).AddTicks(fraction);

        return DateTime.SpecifyKind(value, (DateTimeKind)_preload.Next(3));
    }

    /// <summary>
    /// 次の 1 歩（書き込み 1 つか 2 つ、または同期 1 回）を作る。
    /// </summary>
    /// <param name="states">書き手ごとの DB の現在の内容</param>
    public IReadOnlyList<ModelOp> Next(IReadOnlyDictionary<ModelActor, ModelDbState> states)
    {
        _states = states;

        if (Faults && _random.NextDouble() < 0.12)
        {
            var device = PickDevice();
            var kinds = Enum.GetValues<ModelFaultKind>()
                .Where(kind => kind != ModelFaultKind.ApplySkip)
                .ToArray();

            return [new ArmFaultOp(device, kinds[_random.Next(kinds.Length)])];
        }

        if (_random.NextDouble() < 0.18)
        {
            return [NextSync()];
        }

        var actor = WriterFor(_random.NextDouble());
        var state = states[actor];

        for (var attempt = 0; attempt < 30; attempt++)
        {
            var ops =
                Extras && actor != ModelActor.Server && _random.NextDouble() < 0.25
                    ? TryExtra(actor, state)
                    : TryWrite(actor, state);

            if (ops is not null)
            {
                CheckWriterContract(actor, state, ops);

                return ops;
            }
        }

        return [NextSync()];
    }

    /// <summary>同期の操作のダウンロードのバッチの大きさ</summary>
    public static readonly int[] Batches = [1, 2, 3, 500];

    private ModelSyncOp NextSync()
    {
        var device = PickDevice();
        var kindRoll = _random.NextDouble();
        var kind =
            kindRoll < 0.8 ? ModelSyncKind.Sync
            : kindRoll < 0.9 ? ModelSyncKind.Upload
            : ModelSyncKind.Download;
        IReadOnlyList<string> excluded =
            _random.NextDouble() < 0.15 ? [Pick(ModelTables.All.ToList())] : [];

        // 2 本目の乱数で、本流が何も除外しなかった同期の一部を 2〜3 表の除外にする（本流の 1 表の除外は変えない。全部は
        // 外さない＝最低 1 表は範囲に残す）
        if (excluded.Count == 0 && _side.NextDouble() < MultipleExclusionRate)
        {
            var rest = ModelTables.All.ToList();
            var widened = new List<string>();

            for (var count = _side.Next(2, 4); widened.Count < count; )
            {
                var table = rest[_side.Next(rest.Count)];
                rest.Remove(table);
                widened.Add(table);
            }

            excluded = widened;
        }

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
                    :
                    [
                        new WritePayloadOp(actor, Pick(items), Payload(mayBeLarge: true))
                        {
                            ExpectFailure = true,
                        },
                    ];
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
                        Payload(mayBeLarge: true)
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
                        Payload(mayBeLarge: true)
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
                var (id, stolen) = KeyFor(actor, ModelTables.Folders, NewId(actor));
                var (name, spelling) = UniqueFor(actor, items: false, Value("f"));

                return
                [
                    new AddFolderOp(actor, via, id, parent, name)
                    {
                        StolenKey = stolen,
                        Spelling = spelling,
                    },
                ];
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

                var (name, spelling) = UniqueFor(actor, items: false, Value("f"));

                return [new UpdateFolderOp(actor, via, id, parent, name) { Spelling = spelling }];
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

                var (id, stolen) = KeyFor(actor, ModelTables.Items, NewId(actor));
                var folder = Pick(folders);
                var label = labels.Count == 0 || _random.Next(2) == 0 ? null : Pick(labels);
                var (code, spelling) = UniqueFor(actor, items: true, FreshItemCode(actor, id));

                return
                [
                    new AddItemOp(
                        actor,
                        via,
                        id,
                        folder,
                        label,
                        code,
                        Value("t"),
                        Payload(mayBeLarge: true)
                    )
                    {
                        StolenKey = stolen,
                        Spelling = spelling,
                    },
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
                    : [new WritePayloadOp(actor, Pick(items), Payload(mayBeLarge: true))];

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
                        Payload(mayBeLarge: true)
                    ),
                ];
            }

            case 13:
            {
                if (items.Count == 0)
                {
                    return null;
                }

                var at = EventTime();
                var item = Pick(items);
                var (key, stolen) = EventKeyFor(actor, at);

                return [new AddEventOp(actor, via, key, item, Value("m")) { StolenKey = stolen }];
            }

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

    /// <summary>形：空けた値を取った行の値を、さらに別の行が取る（3 行の連鎖。入れ替えは作らない＝<see cref="SyncModelNotGenerated"/>）</summary>
    public const string ReleaseTakeChainShape = "release-take-chain";

    /// <summary>形：行を消して一意な値を空け、別の行がその値を更新で取る（4m-B3 の材料）</summary>
    public const string DeleteTakeShape = "delete-take";

    /// <summary>形：消す行の一意な値を別の行が取って別の親へ付け替え、元の親（葉になる）を消す（4m-D4）</summary>
    public const string DeleteTakeReparentShape = "delete-take-reparent";

    /// <summary>
    /// 一意な値の付け替えの形（品目の code かフォルダーの name）を 1 つ作る。どれも書き手の DB で各手が成り立つ順に並べ、
    /// 値を取る手は、それより前の手が値を空けた後に置く（入れ替えは作らない＝<see cref="SyncModelNotGenerated"/>）。
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
            ModelActor.Device3 => "r",
            _ => "s",
        };

        return $"{LabelPrefixes[_random.Next(LabelPrefixes.Length)]}{who}{++_counter}";
    }

    private static string ItemCode(ModelActor actor, int id) =>
        $"c{(int)actor}-{id.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// 品目の挿入の既定の code（種の中で一意）。自分の新しいキーは単調に増えるので <see cref="ItemCode"/> のまま重ならないが、
    /// 盗んだキー（<see cref="KeyFor"/>）は同じ書き手が同じキーをもう一度選べる（自分で消した後にまた候補になる）。すでに
    /// 出した値なら通し番号を足す（その形の種だけが変わる）
    /// </summary>
    private string FreshItemCode(ModelActor actor, int id)
    {
        var code = ItemCode(actor, id);

        if (_issuedItemCodes.Add(code))
        {
            return code;
        }

        code = $"{code}-{(++_counter).ToString(CultureInfo.InvariantCulture)}";
        _issuedItemCodes.Add(code);

        return code;
    }

    /// <summary>
    /// 書き込みの手が書き手の DB で成功する約束（<see cref="SyncModelGenerator"/> の remarks）のうち、主キーと一意な値の分を
    /// 確かめる。手の列を順に当てた書き手の DB の写しで、挿入するキーがまだ無いことと、フォルダーの name・品目の code を
    /// ほかの行が持っていないこと（序数）を見る。失敗を期待する手（<see cref="ModelWriteOp.ExpectFailure"/>）は当てない。
    /// 破っていれば <see cref="SyncModelGeneratorContractException"/>（生成器の誤りを、書き込みの失敗 <c>op/failed</c> と分けて名指しする）
    /// </summary>
    /// <param name="actor">書き手</param>
    /// <param name="state">書き手の DB の現在の内容</param>
    /// <param name="ops">生成器が返す手の列</param>
    internal static void CheckWriterContract(
        ModelActor actor,
        ModelDbState state,
        IReadOnlyList<ModelOp> ops
    )
    {
        var folders = state.Folders.ToDictionary(p => p.Key, p => p.Value.Name);
        var items = state.Items.ToDictionary(p => p.Key, p => p.Value.Code);
        var labels = state.Labels.Keys.ToHashSet(StringComparer.Ordinal);
        var events = state.Events.Keys.ToHashSet(StringComparer.Ordinal);

        void Fail(ModelWriteOp op, string what, string table) =>
            throw new SyncModelGeneratorContractException(
                $"gen/writer-contract:{op.GetType().Name}:{what}:{table}",
                $"{actor}'s DB refuses {op.Describe()} ({what} already taken in {table})"
            );

        void Put(ModelWriteOp op, Dictionary<int, string> rows, int id, string value, bool insert)
        {
            var table = op.Table;

            if (insert && rows.ContainsKey(id))
            {
                Fail(op, "key", table);
            }

            if (
                rows.Any(p =>
                    p.Key != id && string.Equals(p.Value, value, StringComparison.Ordinal)
                )
            )
            {
                Fail(op, "unique", table);
            }

            rows[id] = value;
        }

        foreach (var op in ops.OfType<ModelWriteOp>().Where(op => !op.ExpectFailure))
        {
            switch (op)
            {
                case AddFolderOp add:
                    Put(op, folders, add.Id, add.Name, insert: true);
                    break;
                case UpdateFolderOp update:
                    Put(op, folders, update.Id, update.Name, insert: false);
                    break;
                case DeleteFolderOp delete:
                    folders.Remove(delete.Id);
                    break;
                case AddItemOp add:
                    Put(op, items, add.Id, add.Code, insert: true);
                    break;
                case UpdateItemOp update:
                    Put(op, items, update.Id, update.Code, insert: false);
                    break;
                case DeleteItemOp delete:
                    items.Remove(delete.Id);
                    break;
                case AddLabelOp add when !labels.Add(add.Code):
                    Fail(op, "key", op.Table);
                    break;
                case DeleteLabelOp delete:
                    labels.Remove(delete.Code);
                    break;
                case AddEventOp add when !events.Add(add.KeyText):
                    Fail(op, "key", op.Table);
                    break;
                case DeleteEventOp delete:
                    events.Remove(delete.KeyText);
                    break;
            }
        }
    }

    /// <summary>
    /// blob の値（先頭の 4 バイトが種の中の通し番号＝種の中で一意）。<paramref name="mayBeLarge"/> なら 2 本目の乱数で
    /// <see cref="LargePayloadBytes"/> バイトへ伸ばす（先頭の 6 バイトは同じ。本流の乱数の引き方は変えない）
    /// </summary>
    private string Payload(bool mayBeLarge = false)
    {
        var bytes = new byte[6];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), ++_counter);
        bytes[4] = (byte)_random.Next(256);
        bytes[5] = (byte)_random.Next(1, 256);

        if (mayBeLarge && _side.NextDouble() < LargePayloadRate)
        {
            var large = new byte[LargePayloadBytes];
            bytes.CopyTo(large, 0);

            for (var i = bytes.Length; i < large.Length; i++)
            {
                large[i] = unchecked((byte)(bytes[4] + i * 31));
            }

            bytes = large;
        }

        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// キーの範囲の持ち主（端末 1＝1000 台・端末 2＝2000 台・サーバーの他者＝3000 台・端末 3＝4000 台。初期データの 1 桁は
    /// null。フックが付け直したキー〔<see cref="SyncModelDevice.RekeyOffset"/> を足した値〕はサーバーの他者に数える）
    /// </summary>
    private static ModelActor? RangeOwner(int id) =>
        id switch
        {
            > 4000 and < 5000 => ModelActor.Device3,
            > 3000 => ModelActor.Server,
            > 2000 => ModelActor.Device2,
            > 1000 => ModelActor.Device1,
            _ => null,
        };

    /// <summary>
    /// 挿入の手のキー。2 本目の乱数で、ほかの書き手の範囲のキーのうち、ほかの書き手の DB にあって自分の DB に無いもの
    /// （持ち主の採番がもう越えたキー＝持ち主の後の挿入が自分の DB で失敗しない）を選ぶ。無ければ自分の新しいキー
    /// </summary>
    private (int Id, bool Stolen) KeyFor(ModelActor actor, string table, int own)
    {
        if (_side.NextDouble() >= StolenKeyRate)
        {
            return (own, false);
        }

        var mine = IntKeys(_states[actor], table);
        var candidates = _states
            .Where(pair => pair.Key != actor)
            .SelectMany(pair => IntKeys(pair.Value, table))
            .Where(key =>
                RangeOwner(key) is { } owner
                && owner != actor
                && !mine.Contains(key)
                && !SyncModelWorldShape.IsUntrackedKey(key)
            )
            .Distinct()
            .Order()
            .ToList();

        return candidates.Count == 0
            ? (own, false)
            : (candidates[_side.Next(candidates.Count)], true);
    }

    private static HashSet<int> IntKeys(ModelDbState state, string table) =>
        table == ModelTables.Folders ? [.. state.Folders.Keys] : [.. state.Items.Keys];

    /// <summary>
    /// イベントの挿入の手のキー。2 本目の乱数で、ほかの書き手の DB にあって自分の DB に無いイベントの日時を選ぶ（日時の
    /// キーに範囲は無い）。無ければ本流が作った日時
    /// </summary>
    private (DateTime At, bool Stolen) EventKeyFor(ModelActor actor, DateTime own)
    {
        if (_side.NextDouble() >= StolenKeyRate)
        {
            return (own, false);
        }

        var mine = _states[actor].Events;
        var candidates = _states
            .Where(pair => pair.Key != actor)
            .SelectMany(pair => pair.Value.Events)
            .Where(pair => !mine.ContainsKey(pair.Key))
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.First().Value.At)
            .ToList();

        return candidates.Count == 0
            ? (own, false)
            : (candidates[_side.Next(candidates.Count)], true);
    }

    /// <summary>
    /// 一意な値（フォルダーの name・品目の code）を新しく書く手の値。2 本目の乱数で別表記へ置き換える（自分の DB に序数で
    /// 同じ値が無いものだけ。サーバーの他者は、どの照合順序でもぶつからない非 ASCII の新しい値だけ）。置き換えられなければ
    /// <paramref name="fresh"/>
    /// </summary>
    private (string Value, UniqueSpelling Spelling) UniqueFor(
        ModelActor actor,
        bool items,
        string fresh
    )
    {
        if (_side.NextDouble() >= SpellingRate)
        {
            return (fresh, UniqueSpelling.Fresh);
        }

        var kind =
            actor == ModelActor.Server
                ? UniqueSpelling.NonAscii
                : SpellingKinds[_side.Next(SpellingKinds.Length)];
        var limit = items ? 20 : 40;
        var own = UniqueValues(_states[actor], items).ToHashSet(StringComparer.Ordinal);

        // 別表記の元にするのは追跡する行の値だけ（追跡しない行の値は、新しい値かを確かめる known にだけ入れる）
        var all = _states
            .Values.SelectMany(state => UniqueValues(state, items, includeUntracked: false))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var known = _states
            .Values.SelectMany(state => UniqueValues(state, items))
            .ToHashSet(StringComparer.Ordinal);

        // 自分の DB に無く、どこにも無い（別表記として新しい）値だけ
        List<string> Variants(Func<string, string?> make) =>
            all.Select(make)
                .OfType<string>()
                .Where(value => value.Length <= limit && !known.Contains(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();

        string? PickSide(IReadOnlyList<string> values) =>
            values.Count == 0 ? null : values[_side.Next(values.Count)];

        string NonAsciiFresh() => NonAsciiPrefixes[_side.Next(NonAsciiPrefixes.Length)] + fresh;

        var value = kind switch
        {
            UniqueSpelling.Exact => PickSide(all.Where(value => !own.Contains(value)).ToList()),
            UniqueSpelling.AsciiCase => PickSide(Variants(FlipAsciiCase)),
            UniqueSpelling.NonAscii => NonAsciiFresh(),
            UniqueSpelling.NonAsciiCase => PickSide(Variants(FlipNonAsciiCase)),
            UniqueSpelling.TrailingSpace => PickSide(Variants(v => v + " ")),
            _ => own.Contains("") ? null : "",
        };

        if (value is null && kind == UniqueSpelling.NonAsciiCase)
        {
            // 大文字小文字を持つ非 ASCII の値がまだどこにも無い：先に新しい非 ASCII の値を作る（後の手がその別表記を書く）
            (value, kind) = (NonAsciiFresh(), UniqueSpelling.NonAscii);
        }

        return value is null || value.Length > limit
            ? (fresh, UniqueSpelling.Fresh)
            : (value, kind);
    }

    private static IEnumerable<string> UniqueValues(
        ModelDbState state,
        bool items,
        bool includeUntracked = true
    ) =>
        items
            ? state
                .Items.Where(p => includeUntracked || !SyncModelWorldShape.IsUntrackedKey(p.Key))
                .Select(p => p.Value.Code)
            : state
                .Folders.Where(p => includeUntracked || !SyncModelWorldShape.IsUntrackedKey(p.Key))
                .Select(p => p.Value.Name);

    /// <summary>ASCII の英字の大文字小文字を入れ替える（ASCII の英字が無ければ null）</summary>
    public static string? FlipAsciiCase(string value)
    {
        var chars = value.ToCharArray();
        var changed = false;

        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            {
                chars[i] = (char)(chars[i] ^ 0x20);
                changed = true;
            }
        }

        return changed ? new string(chars) : null;
    }

    /// <summary>非 ASCII の文字の大文字小文字を入れ替える（入れ替わる非 ASCII の文字が無ければ null）</summary>
    public static string? FlipNonAsciiCase(string value)
    {
        var chars = value.ToCharArray();
        var changed = false;

        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] <= '\u007F')
            {
                continue;
            }

            var flipped = char.IsUpper(chars[i])
                ? char.ToLowerInvariant(chars[i])
                : char.ToUpperInvariant(chars[i]);

            if (flipped != chars[i])
            {
                chars[i] = flipped;
                changed = true;
            }
        }

        return changed ? new string(chars) : null;
    }

    private DateTime EventTime()
    {
        var fraction = _random.Next(0, 1_000_000) * 10 + _random.Next(1, 10);
        var value = BaseTime.AddSeconds(++_counter).AddTicks(fraction);
        var kind = (DateTimeKind)_random.Next(3);

        return DateTime.SpecifyKind(value, kind);
    }
}

/// <summary>一意な値を新しく書く手の値の形（<see cref="SyncModelGenerator.SpellingRate"/> の割合で Fresh 以外へ置き換える）</summary>
internal enum UniqueSpelling
{
    /// <summary>種の中で一意な ASCII の新しい値（置き換えなし）</summary>
    Fresh,

    /// <summary>ほかの書き手の DB にある値そのもの（2 人が独立に同じ値を作る）</summary>
    Exact,

    /// <summary>どこかの DB にある値の ASCII の英字の大文字小文字だけを入れ替えた値</summary>
    AsciiCase,

    /// <summary>非 ASCII の文字で始まる新しい値（<see cref="SyncModelGenerator.NonAsciiPrefixes"/>）</summary>
    NonAscii,

    /// <summary>どこかの DB にある値の非 ASCII の文字の大文字小文字だけを入れ替えた値</summary>
    NonAsciiCase,

    /// <summary>どこかの DB にある値の末尾に空白を 1 つ足した値</summary>
    TrailingSpace,

    /// <summary>空の文字列</summary>
    Empty,
}

/// <summary>
/// 生成器が書き手の DB で成功しない手を作った（生成器の誤り。<see cref="SyncModelGenerator.CheckWriterContract"/>）
/// </summary>
/// <param name="signature">違反の署名（<c>gen/writer-contract:{手}:{key|unique}:{表}</c>）</param>
/// <param name="message">手と理由</param>
internal sealed class SyncModelGeneratorContractException(string signature, string message)
    : Exception(message)
{
    /// <summary>違反の署名</summary>
    public string Signature { get; } = signature;
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
