using System.Text;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>入力の列の乱数の手の列を作る（種ごとに決定的）</summary>
/// <remarks>
/// <para>
/// 1 種は数個の区切り（エピソード）からなる。区切りは「フォーカスを移す手（クリック・Tab・別のウィンドウの行き来・
/// 移さない）→ Space を押さないマウスの操作（あれば）→ Space の押下の間の手（リピート・中ボタンや左ボタンのドラッグ・
/// ホイール・Ctrl・Tab・キャンバスの付け外しを混ぜる）→ Space の解放」。物理的にあり得る並び（押していないキーを離さない・
/// ボタンは 1 つずつ・終わりにはすべて離す）だけを作る。
/// </para>
/// <para>
/// Space を押さないマウスの操作は、キャンバスのドラッグ（途中に Space・Ctrl・外部の取り消し・キャンバスの外への持ち出しを
/// 挟める）・中ボタンのパン・ホイール・列一覧の操作（行ヘッダーのクリック・並べ替え・キャンバスの外の押下から列一覧や
/// キャンバスへの持ち込み）・ドラッグ中の Delete・カードの上のホバー・キャンバスの付け外し。
/// </para>
/// <para>
/// フォーカスをクリックで移す手と、キャンバスの外（列一覧・プロパティパネル）の押下は、何も押していないときにだけ置く
/// （ハーネスは列一覧の行を出すために先頭のテーブルのカードをクリックすることがあり、Space や Ctrl を押したままだと
/// そのクリックがパンや選択の切り替えになる）。キャンバスの外の押下の後は、フォーカスの行き先を対象で言えないので、
/// 次の Space の押下の前に必ずフォーカスを移す。別のウィンドウの行き来とキャンバスの付け外しはマウスのボタンを押して
/// いないときにだけ置く（押したまま非アクティブになる・外れるとキャプチャが OS の都合で外れ、仕様のモデルで扱わない）。
/// ドラッグ中の Delete は、先頭のテーブルだけを選んでから置き、1 種に 1 回まで（テーブルが無くならない）。
/// </para>
/// </remarks>
internal static class CanvasInputGenerator
{
    /// <summary>種の数（普段 40・夜間 400・変数 <c>QUICKER_CANVAS_INPUT_SEEDS</c>・1 種だけは <c>QUICKER_CANVAS_INPUT_SEED</c>）</summary>
    public static SeedSettings Seeds { get; } =
        new("QUICKER_CANVAS_INPUT_SEEDS", 40, 400, singleVariable: "QUICKER_CANVAS_INPUT_SEED");

    /// <summary>1 種の区切りの数</summary>
    public const int Episodes = 3;

    private static readonly CanvasTarget[] Targets = Enum.GetValues<CanvasTarget>();

    /// <summary>Tab で移れる対象（キャンバスは MainWindow のタブ順に入らない＝どの要素から Tab を押しても入らない）</summary>
    private static readonly CanvasTarget[] TabTargets =
    [
        .. Targets.Where(target => target != CanvasTarget.Canvas),
    ];

    /// <summary>キャンバスの上の場所（Space の押下の間にも押せる）</summary>
    private static readonly CanvasSpot[] CanvasPlaces =
    [
        CanvasSpot.Blank,
        CanvasSpot.Card,
        CanvasSpot.CardGrip,
        CanvasSpot.Line,
    ];

    /// <summary>種から手の列を作る</summary>
    public static IReadOnlyList<CanvasStep> Generate(int seed)
    {
        var random = new Random(seed);
        var steps = new List<CanvasStep>();
        var deleted = false;

        for (var episode = 0; episode < Episodes; episode++)
        {
            AddFocusMove(random, steps, force: false);

            if (random.Next(10) < 4 && AddFreeGesture(random, steps, ref deleted))
            {
                AddFocusMove(random, steps, force: true);
            }

            AddSpaceEpisode(random, steps);
        }

        return steps;
    }

    /// <summary>種の入力の要約（手の列。乱数を引かない）</summary>
    public static string Summary(int seed)
    {
        var text = new StringBuilder();

        foreach (var step in Generate(seed))
        {
            text.Append(step).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>何も押していないときのフォーカスの移し方（force なら必ず移す）</summary>
    private static void AddFocusMove(Random random, List<CanvasStep> steps, bool force)
    {
        var pick = random.Next(force ? 15 : 20);

        if (pick < 8)
        {
            steps.Add(CanvasStep.ClickFocus(Targets[random.Next(Targets.Length)]));
        }
        else if (pick < 15)
        {
            steps.Add(CanvasStep.TabTo(TabTargets[random.Next(TabTargets.Length)]));
        }
        else if (pick < 17)
        {
            steps.Add(CanvasStep.Reactivate);
        }
    }

    /// <summary>Space を押さないマウスの操作（キャンバスの外を押したら true＝次にフォーカスを移す）</summary>
    private static bool AddFreeGesture(Random random, List<CanvasStep> steps, ref bool deleted)
    {
        switch (random.Next(9))
        {
            case 8:
                // グループ移動（先頭のテーブルを選び、Ctrl のクリックで 2 番目を足し、先頭のカードを Ctrl なしでドラッグ）
                steps.Add(CanvasStep.ClickFocus(CanvasTarget.Canvas));
                steps.Add(CanvasStep.CtrlDown);
                steps.Add(CanvasStep.LeftDown(CanvasSpot.Card2));
                steps.Add(CanvasStep.LeftUp);
                steps.Add(CanvasStep.CtrlUp);
                steps.Add(CanvasStep.LeftDown(CanvasSpot.Card));
                steps.Add(CanvasStep.Move(Delta(random), Delta(random)));

                if (random.Next(3) == 0)
                {
                    steps.Add(CanvasStep.ExternalCancel);
                }

                steps.Add(CanvasStep.Move(Delta(random), Delta(random)));
                steps.Add(CanvasStep.LeftUp);
                return false;
            case 0:
            case 1:
                AddCanvasDrag(random, steps);
                return false;
            case 2:
                AddDrag(random, steps, left: false, inner: null);
                return false;
            case 3:
                if (random.Next(2) == 0)
                {
                    steps.Add(CanvasStep.Wheel(Notch(random)));
                }
                else
                {
                    steps.Add(CanvasStep.CtrlDown);
                    steps.Add(CanvasStep.Wheel(Notch(random)));
                    steps.Add(CanvasStep.CtrlUp);
                }

                return false;
            case 4:
            case 5:
                AddGridGesture(random, steps);
                return true;
            case 6:
                if (!deleted && random.Next(2) == 0)
                {
                    // ドラッグ中の Delete（先頭のテーブルだけを選んでから、そのカードをドラッグする）
                    deleted = true;
                    steps.Add(CanvasStep.ClickFocus(CanvasTarget.Canvas));
                    steps.Add(CanvasStep.LeftDown(CanvasSpot.Card));
                    steps.Add(CanvasStep.Move(Delta(random), Delta(random)));
                    steps.Add(CanvasStep.Delete);
                    steps.Add(CanvasStep.Move(Delta(random), Delta(random)));
                    steps.Add(CanvasStep.LeftUp);
                    return false;
                }

                steps.Add(CanvasStep.Reload);
                return false;
            default:
                // カードの上のホバー（右端のつかみと内側）
                steps.Add(
                    CanvasStep.MoveTo(random.Next(2) == 0 ? CanvasSpot.CardGrip : CanvasSpot.Card)
                );
                steps.Add(
                    CanvasStep.MoveTo(random.Next(2) == 0 ? CanvasSpot.CardGrip : CanvasSpot.Card)
                );
                return false;
        }
    }

    /// <summary>キャンバスの上の左ドラッグ（途中に Space・Ctrl・外部の取り消し・キャンバスの外への持ち出しを挟める）</summary>
    private static void AddCanvasDrag(Random random, List<CanvasStep> steps)
    {
        steps.Add(CanvasStep.LeftDown(CanvasPlaces[random.Next(CanvasPlaces.Length)]));
        steps.Add(CanvasStep.Move(Delta(random), Delta(random)));

        switch (random.Next(6))
        {
            case 0:
                // 押した後の Space（パン待機の間の移動）
                steps.Add(CanvasStep.SpaceDown);
                steps.Add(CanvasStep.Move(Delta(random), Delta(random)));
                steps.Add(CanvasStep.SpaceUp);
                break;
            case 1:
                steps.Add(CanvasStep.CtrlDown);
                steps.Add(CanvasStep.Move(Delta(random), Delta(random)));
                steps.Add(CanvasStep.CtrlUp);
                break;
            case 2:
                steps.Add(CanvasStep.ExternalCancel);
                break;
            case 3:
                // キャンバスの外へ持ち出して離す
                steps.Add(CanvasStep.MoveTo(CanvasSpot.Outside));
                break;
        }

        steps.Add(CanvasStep.Move(Delta(random), Delta(random)));
        steps.Add(CanvasStep.LeftUp);
    }

    /// <summary>列一覧の操作（行ヘッダーのクリック・並べ替え・キャンバスの外の押下からの持ち込み）</summary>
    private static void AddGridGesture(Random random, List<CanvasStep> steps)
    {
        switch (random.Next(4))
        {
            case 0:
                // 行ヘッダーのクリック（動かさずに離す）
                steps.Add(
                    CanvasStep.LeftDown(
                        random.Next(2) == 0 ? CanvasSpot.RowHeader : CanvasSpot.RowOther
                    )
                );
                steps.Add(CanvasStep.LeftUp);
                break;
            case 1:
                // 並べ替え（先頭の行を 2 行目へ）
                steps.Add(CanvasStep.LeftDown(CanvasSpot.RowHeader));
                steps.Add(CanvasStep.MoveTo(CanvasSpot.RowOther));
                steps.Add(CanvasStep.LeftUp);
                break;
            case 2:
                // キャンバスの外で押して、列一覧の行へ持ち込む
                steps.Add(CanvasStep.LeftDown(CanvasSpot.Outside));
                steps.Add(
                    CanvasStep.MoveTo(
                        random.Next(2) == 0 ? CanvasSpot.RowHeader : CanvasSpot.RowOther
                    )
                );
                steps.Add(CanvasStep.LeftUp);
                break;
            default:
                // キャンバスの外で押して、キャンバスへ持ち込む
                steps.Add(CanvasStep.LeftDown(CanvasSpot.Outside));
                steps.Add(
                    CanvasStep.MoveTo(random.Next(2) == 0 ? CanvasSpot.Blank : CanvasSpot.Card)
                );
                steps.Add(CanvasStep.Move(Delta(random), Delta(random)));
                steps.Add(CanvasStep.LeftUp);
                break;
        }
    }

    /// <summary>Space の押下から解放までの手</summary>
    private static void AddSpaceEpisode(Random random, List<CanvasStep> steps)
    {
        var repeats = random.Next(10) switch
        {
            < 3 => 0,
            < 6 => 1,
            _ => 2 + random.Next(3),
        };
        var ctrl = random.Next(10) < 3;
        var gesture = random.Next(5);
        var tab = random.Next(10) == 0;

        // リピートを 1〜2 回ずつの束に分け、区切りの中の差し込める位置へ散らす
        var chunks = new List<int>();

        while (repeats > 0)
        {
            var chunk = Math.Min(repeats, 1 + random.Next(2));
            chunks.Add(chunk);
            repeats -= chunk;
        }

        var inner = new List<CanvasStep>();

        if (ctrl)
        {
            inner.Add(CanvasStep.CtrlDown);
        }

        switch (gesture)
        {
            case 0:
                AddDrag(random, inner, left: true, tab ? StableTarget(random) : null);
                break;
            case 1:
                AddDrag(random, inner, left: false, tab ? StableTarget(random) : null);
                break;
            case 2:
                inner.Add(CanvasStep.Wheel(Notch(random)));

                if (random.Next(2) == 0)
                {
                    inner.Add(CanvasStep.Wheel(Notch(random)));
                }

                break;
            case 3:
                // 押下の間のキャンバスの付け外し（ボタンを押していないとき）
                if (random.Next(3) == 0)
                {
                    inner.Add(CanvasStep.Reload);
                }

                break;
            default:
                if (tab)
                {
                    inner.Add(CanvasStep.TabTo(StableTarget(random)));
                }

                break;
        }

        if (ctrl)
        {
            inner.Add(CanvasStep.CtrlUp);
        }

        foreach (var chunk in chunks)
        {
            inner.Insert(random.Next(inner.Count + 1), CanvasStep.Repeat(chunk));
        }

        steps.Add(CanvasStep.SpaceDown);
        steps.AddRange(inner);
        steps.Add(CanvasStep.SpaceUp);
    }

    /// <summary>キャンバスの上でのボタンの押下・移動・解放（押下の後に Tab を 1 つ挟める）</summary>
    private static void AddDrag(
        Random random,
        List<CanvasStep> steps,
        bool left,
        CanvasTarget? inner
    )
    {
        var spot = CanvasPlaces[random.Next(CanvasPlaces.Length)];
        steps.Add(left ? CanvasStep.LeftDown(spot) : CanvasStep.MiddleDown(spot));

        if (inner is { } target)
        {
            steps.Add(CanvasStep.TabTo(target));
        }

        var moves = 1 + random.Next(3);

        for (var index = 0; index < moves; index++)
        {
            steps.Add(CanvasStep.Move(Delta(random), Delta(random)));
        }

        steps.Add(left ? CanvasStep.LeftUp : CanvasStep.MiddleUp);
    }

    /// <summary>
    /// 操作の途中の Tab の行き先（図の選択に依らず在り、Tab で入れる部品だけ。列一覧と入力欄はテーブルを選んでいないと無く、
    /// キャンバスはタブ順に入らない）
    /// </summary>
    private static CanvasTarget StableTarget(Random random) =>
        StableTargets[random.Next(StableTargets.Length)];

    private static readonly CanvasTarget[] StableTargets =
    [
        CanvasTarget.Button,
        CanvasTarget.Toggle,
    ];

    /// <summary>移動の量（クリックの閾値 3 px の内と外の両方を出す）</summary>
    private static int Delta(Random random) =>
        random.Next(4) == 0 ? random.Next(-2, 3) : random.Next(-40, 41);

    private static int Notch(Random random) => random.Next(2) == 0 ? 1 : -1;
}
