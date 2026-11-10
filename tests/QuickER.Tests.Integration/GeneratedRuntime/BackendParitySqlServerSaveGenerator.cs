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
/// <param name="Condition">読んだ後に他者がしたこと（<see cref="SaveRowRole.New"/> は常に <see cref="SaveRowCondition.Fresh"/>）</param>
/// <param name="Skipped">Before のフックがこの kit の操作を飛ばすか</param>
/// <param name="InitialCode">保存先の初めの code（<see cref="SaveRowRole.New"/> は挿入する code）</param>
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
);

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
/// 次の形は作らない：他者が消した行の削除（行なし削除の非対称＝実装先で結果が割れることを受け入れた差）・部品に参照された
/// code の変更と部品を持つ行の他者による削除（外部キーで全体が断られ、ほかの行を比べなくなる。参照された代替キーの更新は決まった
/// 形のケース <c>alternate-key-referenced-*</c> が持つ）。
/// </para>
/// </remarks>
public static class BackendParitySqlServerSaveGenerator
{
    private const int MinRows = 3;
    private const int MaxRows = 6;

    /// <summary>新しい kit のキーの始まり（既存の kit と重ならない）</summary>
    private const int FirstNewKitId = 100;

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

        return new SqlServerSaveScenario(seed, arguments, hooks, rows);
    }
}
