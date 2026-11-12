using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>保存の後にこの kit が持つ code（code を変えない kit は初めの code）</summary>
    public string FinalCode() => NewCode ?? InitialCode;
}

/// <summary>乱数の保存の部品（kit_parts の行）の役</summary>
public enum SavePartRole
{
    /// <summary>消す kit のコレクションに残し、kit と一緒にカスケードで消す</summary>
    Cascade,

    /// <summary>自分の kit のコレクションで更新する（kit_code は変えない）</summary>
    Update,

    /// <summary>自分の kit のコレクションで単独に消す</summary>
    Delete,

    /// <summary>別の kit へ付け替える（kit_code を書く先の code にし、書く先のコレクションに持たせる）</summary>
    Move,

    /// <summary>新しい部品を書く先のコレクションに足す</summary>
    New,
}

/// <summary>新しい部品の外部キーの書き方</summary>
public enum SavePartForeignKey
{
    /// <summary>書く先の kit の code を書く</summary>
    Explicit,

    /// <summary>既定値（null）のまま足し、グラフ保存に親のキーで埋めさせる</summary>
    Filled,
}

/// <summary>乱数の保存の部品 1 つ</summary>
/// <param name="PartId">部品のキー</param>
/// <param name="OwnerKitId">初めに参照する kit（新しい部品は null）</param>
/// <param name="Role">役</param>
/// <param name="Condition">読んだ後に他者がしたこと（Fresh か Gone）</param>
/// <param name="TargetKitId">部品を持たせる kit（付け替えと新しい部品は書く先、ほかは初めの kit）</param>
/// <param name="ForeignKey">新しい部品の外部キーの書き方（新しい部品でなければ Explicit）</param>
public sealed record SavePartPlan(
    int PartId,
    int? OwnerKitId,
    SavePartRole Role,
    SaveRowCondition Condition,
    int TargetKitId,
    SavePartForeignKey ForeignKey
);

/// <summary>乱数の保存の 1 種（kit の行・渡す順・保存の引数・Save フック・部品）</summary>
/// <param name="Seed">種</param>
/// <param name="Arguments">保存の引数の組</param>
/// <param name="Hooks">kits の Save フック（飛ばす kit の Before）を登録するか</param>
/// <param name="Rows">行（グラフ保存へ渡す根の順）</param>
/// <param name="Parts">部品（コレクションへ持たせる順）</param>
public sealed record SqlServerSaveScenario(
    int Seed,
    SaveArguments Arguments,
    bool Hooks,
    IReadOnlyList<SaveRowPlan> Rows,
    IReadOnlyList<SavePartPlan> Parts
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
        )
        + string.Concat(
            Parts.Select(part =>
                $"\n  part {part.PartId} {part.Role} {part.Condition}"
                + $" kit {(part.OwnerKitId is { } owner ? owner.ToString(CultureInfo.InvariantCulture) : "-")}->{part.TargetKitId}"
                + (part.Role == SavePartRole.New ? $" fk={part.ForeignKey}" : "")
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
/// 最後に部品（kit_parts）の書き込みを、3 本目の乱数の列（<see cref="PartSeed"/>）で重ねる（kits の行は変えない）。
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
        var partPlans = AddParts(new Random(PartSeed(seed)), rows);

        return new SqlServerSaveScenario(seed, arguments, hooks, rows, partPlans);
    }

    /// <summary>部品の書き込みを引く乱数の列の種（本流と手放しと取りの列と別にする）</summary>
    public static int PartSeed(int seed) => unchecked(seed * 6007 + 3571);

    /// <summary>消さない既存の kit に足す部品のキーの始まり（消す kit の部品のキーと重ならない）</summary>
    private const int FirstOwnedPartId = 50;

    /// <summary>新しい部品のキーの始まり</summary>
    private const int FirstNewPartId = 80;

    /// <summary>
    /// kits の行を並べ終えた後に、部品の書き込みを重ねる（kits の行は変えない）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 消す kit の部品（数は本流の列が引いた <see cref="SaveRowPlan.Parts"/>・キーは 1 から渡す順に振る）は、カスケードで消すか
    /// 別の kit へ付け替える。他者が消していない既存の kit のうち、code を変えない kit（<see cref="SaveRowRole.Rename"/>）には
    /// 部品を 0〜2 個足して更新・単独の削除・付け替えを、消す kit の code を取る後回しの kit（<see cref="SaveRowRole.TakeDeletedCode"/>）
    /// には 0〜2 個足して単独の削除・付け替えを引く（後回しの kit の code の書き込みは削除と 1 段目の書き込みの後なので、どの実装先も
    /// 通る。残すと参照された code の変更になる）。最後に新しい部品を 0〜2 個足す。
    /// </para>
    /// <para>
    /// 1 段目で code を手放す kit（code を変える kit・手放す code を取る kit・入れ替える kit）には部品を持たせない。
    /// 同じ保存で部品を別の kit へ付け替えると、EF Core 版は代替キーを先に書くので外部キーに断られ、QuickER 版は付け替え先を先に
    /// 渡せば通る（docs に書いた受け入れた差。決まった形のケースが両方の値を固定する）。
    /// </para>
    /// <para>
    /// 部品を書く先にしない kit：付け替えでは消す kit（カスケードで消える）・付け替える部品の初めの code を取る kit（部品の外部キーの
    /// 値が変わらず、元の持ち主が値を手放すか消える間も参照が残るので、どの実装先も断る）。付け替えと新しい部品の両方で、
    /// Before のフックが飛ばす kit のうち code を変えるか新しい kit（kit を書かないので部品の外部キーが必ず断られる）。
    /// </para>
    /// <para>
    /// 他者が消した部品は、どの実装先でも断られる順（<see cref="RefusedEverywhere"/>）を含む保存に置かない（Fresh に戻す）。
    /// そうした保存では、QuickER 版は前にある部品の競合を、EF Core 版は先に書く代替キーの断りを報告する（docs に書いた受け入れた
    /// 差。決まった形のケースが両方の値を固定する）。
    /// </para>
    /// </remarks>
    private static List<SavePartPlan> AddParts(Random random, List<SaveRowPlan> rows)
    {
        var parts = new List<SavePartPlan>();
        var nextDeletedPart = 1;

        foreach (var row in rows.Where(row => !row.IsNew()))
        {
            for (var index = 0; index < row.Parts; index++)
            {
                var partId = nextDeletedPart++;
                var targets = MoveTargets(row);
                var move = targets.Count > 0 && random.Next(2) == 0;
                parts.Add(
                    move
                        ? MovedPart(partId, row, targets)
                        : new SavePartPlan(
                            partId,
                            row.KitId,
                            SavePartRole.Cascade,
                            SaveRowCondition.Fresh,
                            row.KitId,
                            SavePartForeignKey.Explicit
                        )
                );
            }
        }

        var nextOwnedPart = FirstOwnedPartId;

        foreach (
            var row in rows.Where(row =>
                row.Role is SaveRowRole.Rename or SaveRowRole.TakeDeletedCode
                && row.Condition != SaveRowCondition.Gone
            )
        )
        {
            var keepsCode = row.Role == SaveRowRole.Rename;
            var count = random.Next(10) switch
            {
                < 2 => 0,
                < 5 => 1,
                < 8 => 2,
                _ => 3,
            };

            for (var index = 0; index < count; index++)
            {
                var targets = MoveTargets(row);
                var role = random.Next(3) switch
                {
                    0 => keepsCode ? SavePartRole.Update : SavePartRole.Move,
                    1 => SavePartRole.Delete,
                    _ => SavePartRole.Move,
                };

                if (role == SavePartRole.Move && targets.Count == 0)
                {
                    role = keepsCode ? SavePartRole.Update : SavePartRole.Delete;
                }

                var partId = nextOwnedPart++;

                if (role == SavePartRole.Move)
                {
                    parts.Add(MovedPart(partId, row, targets));
                }
                else
                {
                    var condition =
                        role == SavePartRole.Update && random.Next(2) == 0
                            ? SaveRowCondition.Gone
                            : SaveRowCondition.Fresh;
                    parts.Add(
                        new SavePartPlan(
                            partId,
                            row.KitId,
                            role,
                            condition,
                            row.KitId,
                            SavePartForeignKey.Explicit
                        )
                    );
                }
            }
        }

        var newCount = random.Next(4) switch
        {
            0 => 0,
            1 => 1,
            _ => 2,
        };
        var newTargets = rows.Where(row => !SkippedWithoutKit(row)).ToList();

        for (var index = 0; index < newCount && newTargets.Count > 0; index++)
        {
            // 新しい kit があれば半分はそこへ足す（新しい kit とその新しい部品）
            var newKits = newTargets.Where(target => target.IsNew()).ToList();
            var target =
                newKits.Count > 0 && random.Next(3) == 0
                    ? newKits[random.Next(newKits.Count)]
                    : Pick(newTargets);
            parts.Add(
                new SavePartPlan(
                    FirstNewPartId + index,
                    null,
                    SavePartRole.New,
                    SaveRowCondition.Fresh,
                    target.KitId,
                    random.Next(2) == 0 ? SavePartForeignKey.Explicit : SavePartForeignKey.Filled
                )
            );
        }

        // コレクションへ持たせる順（Fisher-Yates）
        for (var index = parts.Count - 1; index > 0; index--)
        {
            var other = random.Next(index + 1);
            (parts[index], parts[other]) = (parts[other], parts[index]);
        }

        if (RefusedEverywhere(rows))
        {
            for (var index = 0; index < parts.Count; index++)
            {
                parts[index] = parts[index] with { Condition = SaveRowCondition.Fresh };
            }
        }

        return parts;

        SavePartPlan MovedPart(int partId, SaveRowPlan owner, List<SaveRowPlan> targets) =>
            new(
                partId,
                owner.KitId,
                SavePartRole.Move,
                random.Next(5) == 0 ? SaveRowCondition.Gone : SaveRowCondition.Fresh,
                Pick(targets).KitId,
                SavePartForeignKey.Explicit
            );

        // 書く先を選ぶ（4 分の 3 は他の kit の code を取る kit から、役を等しく選んでからその役の kit を選ぶ＝外部キーが同じ保存で
        // 持ち主の変わる値を指す形を、まれな役でも出す）
        SaveRowPlan Pick(List<SaveRowPlan> targets)
        {
            var takerRoles = targets
                .Where(target =>
                    target.Role
                        is SaveRowRole.TakeDeletedCode
                            or SaveRowRole.TakeReleasedCode
                            or SaveRowRole.SwapCode
                            or SaveRowRole.NewTakesReleasedCode
                )
                .GroupBy(target => target.Role)
                .OrderBy(group => group.Key)
                .ToList();

            if (takerRoles.Count == 0 || random.Next(4) == 0)
            {
                return targets[random.Next(targets.Count)];
            }

            var role = takerRoles[random.Next(takerRoles.Count)].ToList();

            return role[random.Next(role.Count)];
        }

        // 付け替える部品の書く先にできる kit
        List<SaveRowPlan> MoveTargets(SaveRowPlan owner) =>
            [
                .. rows.Where(row =>
                    row.Role != SaveRowRole.Delete
                    && !SkippedWithoutKit(row)
                    && row.KitId != owner.KitId
                    && row.TakenCode() != owner.InitialCode
                ),
            ];

        // Before のフックが飛ばして、部品が参照する code の kit が書かれない
        static bool SkippedWithoutKit(SaveRowPlan row) =>
            row.Skipped && (row.IsNew() || row.NewCode is not null);
    }

    /// <summary>
    /// どの実装先でも断られる順を含む保存か（値を取る kit が、その値をまだ持つ kit より前に書かれるか、持つ kit を Before の
    /// フックが飛ばすか、code を入れ替える kit がある。持つ kit を他者が消していれば値は空いている）
    /// </summary>
    /// <remarks>
    /// 広めに数える（消えた kit・版の古い kit が先に競合で止まる形と、入れ替える 2 つの kit を両方とも飛ばす形も含む）。部品の他者の削除を置かないだけなので、広めでも取りこぼし
    /// は無い。
    /// </remarks>
    internal static bool RefusedEverywhere(IReadOnlyList<SaveRowPlan> rows) =>
        rows.Any(taker => taker.Role == SaveRowRole.SwapCode)
        || rows.Any(taker =>
            !taker.Skipped
            && taker.Role
                is SaveRowRole.TakeDeletedCode
                    or SaveRowRole.TakeReleasedCode
                    or SaveRowRole.NewTakesReleasedCode
            && rows.FirstOrDefault(holder =>
                !ReferenceEquals(holder, taker)
                && !holder.IsNew()
                && holder.InitialCode == taker.TakenCode()
            )
                is { Condition: not SaveRowCondition.Gone } holder
            && (
                holder.Skipped
                || (
                    holder.Role != SaveRowRole.Delete
                    && PositionOf(rows, taker) < PositionOf(rows, holder)
                )
            )
        );

    private static int PositionOf(IReadOnlyList<SaveRowPlan> rows, SaveRowPlan row)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (ReferenceEquals(rows[index], row))
            {
                return index;
            }
        }

        return -1;
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
    /// 取る形は既存の役が持つ。部品は手放しと取りを重ねた後に引き、1 段目で code を手放す kit には持たせないので、手放す code を
    /// 参照する部品は無い）。
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
