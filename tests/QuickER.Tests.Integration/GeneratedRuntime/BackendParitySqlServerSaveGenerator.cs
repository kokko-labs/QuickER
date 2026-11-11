using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedConcurrencyFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>乱数の保存の行の役（グラフ保存に渡す前に、その kit にすること）</summary>
public enum SaveRowRole
{
    /// <summary>名前を変える（Updated）</summary>
    Rename,

    /// <summary>誰も持たない値へ代替キー（code）を変え、名前も変える（Updated）</summary>
    ChangeCode,

    /// <summary>同じ保存で消す kit の code を取り、名前も変える（Updated・削除の後に書く後回しの行）</summary>
    TakeDeletedCode,

    /// <summary>消す（Removed。部品を持つ kit は部品をコレクションに持たせてカスケードで消す）</summary>
    Delete,

    /// <summary>新しい kit を挿入する（Added）</summary>
    New,

    /// <summary>
    /// 同じ保存の更新する kit が手放す code を取り、名前も変える（Updated）。自分の code はまた手放され、別の kit が取ることがある（連鎖）
    /// </summary>
    TakeReleasedCode,

    /// <summary>同じ保存の更新する kit と code を入れ替え、名前も変える（Updated。2 行で 1 組）</summary>
    SwapCode,

    /// <summary>同じ保存の更新する kit が手放す code で、新しい kit を挿入する（Added）</summary>
    NewTakesReleasedCode,
}

/// <summary>乱数の保存の行を、読んだ後に他者がどうしたか</summary>
public enum SaveRowCondition
{
    /// <summary>何もしていない</summary>
    Fresh,

    /// <summary>消した</summary>
    Gone,

    /// <summary>名前を変えて版を進めた</summary>
    Stale,
}

/// <summary>保存の引数の組（並行性の扱い × insertWhenUpdateMissing）</summary>
public enum SaveArguments
{
    /// <summary>Optimistic</summary>
    Optimistic,

    /// <summary>Optimistic ＋ insertWhenUpdateMissing</summary>
    OptimisticInsertWhenMissing,

    /// <summary>ForceOverwrite</summary>
    ForceOverwrite,

    /// <summary>ForceOverwrite ＋ insertWhenUpdateMissing</summary>
    ForceOverwriteInsertWhenMissing,
}

/// <summary>乱数の保存の 1 行（kit 1 つ）</summary>
/// <param name="KitId">kit のキー</param>
/// <param name="Role">役</param>
/// <param name="Condition">読んだ後に他者がしたこと（新しい kit は常に <see cref="SaveRowCondition.Fresh"/>）</param>
/// <param name="Skipped">Before のフックがこの kit の操作を飛ばすか</param>
/// <param name="InitialCode">保存先の初めの code（新しい kit は挿入する code）</param>
/// <param name="NewCode">変えた後の code（変えない役は null）</param>
/// <param name="Parts">この kit の code を参照する部品の数（<see cref="SaveRowRole.Delete"/> だけが持ち、コレクションに持たせて消す）</param>
public sealed record SaveRowPlan(
    int KitId,
    SaveRowRole Role,
    SaveRowCondition Condition,
    bool Skipped,
    string InitialCode,
    string? NewCode,
    int Parts
)
{
    /// <summary>新しい kit（挿入する）か</summary>
    public bool IsNew() => Role is SaveRowRole.New or SaveRowRole.NewTakesReleasedCode;

    /// <summary>同じ保存の更新する kit が手放す code を取る役か</summary>
    public bool TakesReleasedCode() =>
        Role
            is SaveRowRole.TakeReleasedCode
                or SaveRowRole.SwapCode
                or SaveRowRole.NewTakesReleasedCode;

    /// <summary>この kit が保存で持とうとする code（新しい kit は挿入する code、更新する kit は変えた後の code）</summary>
    public string? TakenCode() => IsNew() ? InitialCode : NewCode;
}

/// <summary>乱数の保存の 1 種（kit の行・渡す順・保存の引数・Save フック）</summary>
/// <param name="Seed">種</param>
/// <param name="Arguments">保存の引数の組</param>
/// <param name="Hooks">kits の Save フック（飛ばす kit の Before）を登録するか</param>
/// <param name="Rows">行（グラフ保存へ渡す根の順）</param>
public sealed record SqlServerSaveScenario(
    int Seed,
    SaveArguments Arguments,
    bool Hooks,
    IReadOnlyList<SaveRowPlan> Rows
)
{
    /// <summary>並行性の扱い</summary>
    public ConcurrencyMode Mode =>
        Arguments is SaveArguments.ForceOverwrite or SaveArguments.ForceOverwriteInsertWhenMissing
            ? ConcurrencyMode.ForceOverwrite
            : ConcurrencyMode.Optimistic;

    /// <summary>insertWhenUpdateMissing</summary>
    public bool InsertWhenUpdateMissing =>
        Arguments
            is SaveArguments.OptimisticInsertWhenMissing
                or SaveArguments.ForceOverwriteInsertWhenMissing;

    /// <summary>報告に添える 1 行ずつの説明</summary>
    public string Describe() =>
        $"seed {Seed}: {Arguments} hooks={Hooks}\n"
        + string.Join(
            "\n",
            Rows.Select(row =>
                $"  kit {row.KitId} {row.Role} {row.Condition}"
                + (row.Skipped ? " skipped" : "")
                + $" code={row.InitialCode}"
                + (row.NewCode is null ? "" : $"->{row.NewCode}")
                + (row.Parts > 0 ? $" parts={row.Parts}" : "")
            )
        );
}

/// <summary>
/// SQL Server の系統（ConcurrencyFixture の kits / kit_parts・行バージョンと代替キー）の乱数の保存を作る。
/// 1 種はグラフ保存 1 回（とその後の Optimistic の保存し直し）で、行ごとの役・他者の変更・Before のフックの飛ばし・渡す順を
/// 乱数で引く。
/// </summary>
/// <remarks>
/// <para>
/// 保存の引数（4 通り）と Save フックの有無は乱数を引かず種の番号から決める（<c>(seed - 1) % 4</c> と <c>(seed - 1) / 4 % 2</c>）。
/// 普段の 8 種が引数とフックの全組を 1 回ずつ流す。
/// </para>
/// <para>
/// 役を引いた後に、更新する kit どうしの code の手放しと取り（<see cref="SaveRowRole.TakeReleasedCode"/>・
/// <see cref="SaveRowRole.SwapCode"/>・<see cref="SaveRowRole.NewTakesReleasedCode"/>）を重ねる。重ね方は種から作る別の乱数の列
/// （<see cref="HandoffSeed"/>）で引き、役・他者の変更・飛ばし・渡す順を引く本流の列は変えない。
/// </para>
/// <para>
/// 作らない形は <see cref="BackendParitySqlServerSaveNotGenerated"/> に宣言する。
/// </para>
/// </remarks>
public static class BackendParitySqlServerSaveGenerator
{
    private const int MinRows = 3;
    private const int MaxRows = 6;

    /// <summary>新しい kit のキーの始まり（既存の kit と重ならない）</summary>
    private const int FirstNewKitId = 100;

    /// <summary>手放す code を取るために足す新しい kit のキー（本流の新しい kit のキー 100〜105 と重ならない）</summary>
    private const int AddedNewKitId = FirstNewKitId + MaxRows;

    /// <summary>手放しと取りを引く乱数の列の種（本流の列と別にする）</summary>
    public static int HandoffSeed(int seed) => unchecked(seed * 7919 + 104729);

    /// <summary>種から 1 つの保存を作る（同じ種は同じ保存）</summary>
    public static SqlServerSaveScenario Create(int seed)
    {
        var random = new Random(seed);
        var arguments = (SaveArguments)((seed - 1) % 4);
        var hooks = (seed - 1) / 4 % 2 == 1;
        var count = random.Next(MinRows, MaxRows + 1);
        var roles = new List<(SaveRowRole Role, SaveRowCondition Condition)>();

        for (var index = 0; index < count; index++)
        {
            var role = random.Next(10) switch
            {
                < 2 => SaveRowRole.Rename,
                < 5 => SaveRowRole.ChangeCode,
                < 7 => SaveRowRole.TakeDeletedCode,
                < 9 => SaveRowRole.Delete,
                _ => SaveRowRole.New,
            };
            var condition = role switch
            {
                SaveRowRole.New => SaveRowCondition.Fresh,
                // 他者が消した行の削除は作らない（行なし削除の非対称）
                SaveRowRole.Delete => random.Next(3) == 0
                    ? SaveRowCondition.Stale
                    : SaveRowCondition.Fresh,
                _ => random.Next(4) switch
                {
                    0 => SaveRowCondition.Gone,
                    1 => SaveRowCondition.Stale,
                    _ => SaveRowCondition.Fresh,
                },
            };
            roles.Add((role, condition));
        }

        // 値を取る行は、まだ誰も取っていない消す行の code を取る（消す行が足りなければ誰も持たない値へ変える行にする）
        var deletedCodes = new Queue<int>(
            roles
                .Select((shape, index) => (shape.Role, Index: index))
                .Where(row => row.Role == SaveRowRole.Delete)
                .Select(row => row.Index)
        );
        var rows = new List<SaveRowPlan>();

        for (var index = 0; index < count; index++)
        {
            var (role, condition) = roles[index];
            var kitId = role == SaveRowRole.New ? FirstNewKitId + index : index + 1;
            var initialCode = $"K{kitId}";
            string? newCode = null;

            if (role == SaveRowRole.TakeDeletedCode)
            {
                if (deletedCodes.Count > 0)
                {
                    newCode = $"K{deletedCodes.Dequeue() + 1}";
                }
                else
                {
                    role = SaveRowRole.ChangeCode;
                }
            }

            if (role == SaveRowRole.ChangeCode)
            {
                newCode = $"C{seed}-{kitId}";
            }

            var parts = role == SaveRowRole.Delete ? random.Next(3) : 0;
            var skipped = hooks && random.Next(3) == 0;
            rows.Add(new SaveRowPlan(kitId, role, condition, skipped, initialCode, newCode, parts));
        }

        // 渡す順（Fisher-Yates）
        for (var index = rows.Count - 1; index > 0; index--)
        {
            var other = random.Next(index + 1);
            (rows[index], rows[other]) = (rows[other], rows[index]);
        }

        AddHandoffs(new Random(HandoffSeed(seed)), seed, hooks, rows);
        PassInsertedTakersAfterReleasers(
            rows,
            arguments
                is SaveArguments.OptimisticInsertWhenMissing
                    or SaveArguments.ForceOverwriteInsertWhenMissing
        );

        return new SqlServerSaveScenario(seed, arguments, hooks, rows);
    }

    /// <summary>
    /// 挿入する取る行（新しい kit と、insertWhenUpdateMissing で挿入へ切り替わる消えた kit）が、まだ code を持つ手放す行（他者が
    /// 消していない行）より前に渡されていたら、手放す行の直後へ移す（乱数を引かない）
    /// </summary>
    /// <remarks>
    /// その順の保存は実装先で結果が割れることを受け入れている（docs のグラフ保存の書く順と EF Core の代替キーの節）：QuickER 版は
    /// グラフの順に書くので挿入が一意制約に断られ、EF Core 版は代替キーを保存のほかの文より先に書くので挿入が通る。手放す行が
    /// 消えていれば code はもう空いているので、順は結果を変えない（移さない）。移すのは取る行だけで、取る行の手放す行は消えた行か
    /// 挿入しない行なので、移した行がまた移ることは無い。
    /// </remarks>
    private static void PassInsertedTakersAfterReleasers(
        List<SaveRowPlan> rows,
        bool insertWhenUpdateMissing
    )
    {
        for (var moved = true; moved; )
        {
            moved = false;

            for (var index = 0; index < rows.Count; index++)
            {
                var taker = rows[index];
                var inserted =
                    taker.IsNew()
                    || (insertWhenUpdateMissing && taker.Condition == SaveRowCondition.Gone);

                if (!taker.TakesReleasedCode() || !inserted)
                {
                    continue;
                }

                var releaserIndex = rows.FindIndex(other =>
                    !ReferenceEquals(other, taker)
                    && !other.IsNew()
                    && other.InitialCode == taker.TakenCode()
                );

                if (releaserIndex < index || rows[releaserIndex].Condition == SaveRowCondition.Gone)
                {
                    continue;
                }

                // 取り除くと手放す行が 1 つ前へずれるので、元の位置へ入れると手放す行の直後になる
                rows.RemoveAt(index);
                rows.Insert(releaserIndex, taker);
                moved = true;
                break;
            }
        }
    }

    /// <summary>
    /// 渡す順に並べた行へ、更新する kit どうしの code の手放しと取りを重ねる（形は 4 つ：重ねない・連鎖・入れ替え・新しい kit が
    /// 取る）。手放す kit は、名前だけを変える kit なら誰も持たない値へ code を変える kit にする。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 手放せる kit は名前を変える kit と誰も持たない値へ変える kit だけ（消す kit の code を取る kit は後回しの行で、削除の値を
    /// 取る形は既存の役が持つ。部品を持つのは消す kit だけなので、手放す code を参照する部品は無い）。
    /// </para>
    /// <para>
    /// 連鎖は 2〜3 行：1 行目が手放し、2 行目がそれを取り、3 行目があれば 2 行目の初めの code を取る。新しい kit が取る形は、
    /// 新しい kit が無ければ 1 行足し、渡す順のどこかへ入れる（本流の列が並べた後に入れるので、並べる手を変えない）。
    /// </para>
    /// </remarks>
    private static void AddHandoffs(Random random, int seed, bool hooks, List<SaveRowPlan> rows)
    {
        var updatable = rows.Select((row, index) => (row, index))
            .Where(item => item.row.Role is SaveRowRole.Rename or SaveRowRole.ChangeCode)
            .Select(item => item.index)
            .ToList();
        var news = rows.Select((row, index) => (row, index))
            .Where(item => item.row.Role == SaveRowRole.New)
            .Select(item => item.index)
            .ToList();
        var shape = random.Next(4);

        if (shape == 0 || updatable.Count == 0)
        {
            return;
        }

        // 新しい kit が取る形（新しい kit が無ければ 1 行足す）
        if (shape == 3)
        {
            var releaser = updatable[random.Next(updatable.Count)];
            Release(releaser);
            var code = rows[releaser].InitialCode;

            if (news.Count > 0)
            {
                var taker = news[random.Next(news.Count)];
                rows[taker] = rows[taker] with
                {
                    Role = SaveRowRole.NewTakesReleasedCode,
                    InitialCode = code,
                };
            }
            else
            {
                rows.Insert(
                    random.Next(rows.Count + 1),
                    new SaveRowPlan(
                        AddedNewKitId,
                        SaveRowRole.NewTakesReleasedCode,
                        SaveRowCondition.Fresh,
                        hooks && random.Next(3) == 0,
                        code,
                        null,
                        0
                    )
                );
            }

            return;
        }

        if (updatable.Count < 2)
        {
            return;
        }

        // 選ぶ順を乱数で並べ替え、先頭から使う
        for (var index = updatable.Count - 1; index > 0; index--)
        {
            var other = random.Next(index + 1);
            (updatable[index], updatable[other]) = (updatable[other], updatable[index]);
        }

        if (shape == 2)
        {
            var (first, second) = (updatable[0], updatable[1]);
            var firstCode = rows[first].InitialCode;
            rows[first] = rows[first] with
            {
                Role = SaveRowRole.SwapCode,
                NewCode = rows[second].InitialCode,
            };
            rows[second] = rows[second] with { Role = SaveRowRole.SwapCode, NewCode = firstCode };

            return;
        }

        var length = updatable.Count >= 3 && random.Next(2) == 0 ? 3 : 2;
        Release(updatable[0]);

        for (var step = 1; step < length; step++)
        {
            rows[updatable[step]] = rows[updatable[step]] with
            {
                Role = SaveRowRole.TakeReleasedCode,
                NewCode = rows[updatable[step - 1]].InitialCode,
            };
        }

        // 手放す kit が名前だけを変える kit なら、誰も持たない値へ code を変える kit にする
        void Release(int index)
        {
            if (rows[index].Role == SaveRowRole.Rename)
            {
                rows[index] = rows[index] with
                {
                    Role = SaveRowRole.ChangeCode,
                    NewCode = $"C{seed}-{rows[index].KitId}",
                };
            }
        }
    }
}
