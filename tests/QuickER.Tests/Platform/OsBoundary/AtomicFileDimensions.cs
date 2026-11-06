using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>原子的な書き込みの表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 操作 × 保存先の形 × 失敗を数える。原理的に起こり得ない組（名前を分類する操作に保存先の状態の形など）は <c>isValid</c> で外す
/// （<see cref="TargetsOf"/>・<see cref="NaturalFailure"/>）。
/// </remarks>
internal static class AtomicFileDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "os-boundary-atomic-file";

    /// <summary>操作</summary>
    public const string Op = "Op";

    /// <summary>保存先・読む先・名前の形</summary>
    public const string Target = "Target";

    /// <summary>起こす失敗</summary>
    public const string Failure = "Failure";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.OsBoundary,
            [
                new Factor(
                    Op,
                    Enum.GetValues<AtomicOp>().Select(e => new Level(e.ToString())).ToList(),
                    "原子的な書き込みの操作"
                ),
                new Factor(
                    Target,
                    Enum.GetValues<AtomicTarget>().Select(e => new Level(e.ToString())).ToList(),
                    "保存先・読む先・名前の形"
                ),
                new Factor(
                    Failure,
                    Enum.GetValues<AtomicFailure>().Select(e => new Level(e.ToString())).ToList(),
                    "起こす失敗"
                ),
            ],
            isValid: IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    /// <remarks>
    /// どれも元の単発のテストが持たず、P3-10 では行を足していない組。足すなら表の行で足し、ここから消す。
    /// 次元表は 2 因子の組で数えるので、項目も因子の組ごとに書く。
    /// </remarks>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.OsBoundary,
            [
                Pair(
                    Op,
                    Target,
                    "WriteAllBytes は Write への薄い委譲で、WriteAllText も同じ Write を通る（置換・一時ファイルの掃除は同じ経路）。"
                        + "元のテストは保存先の形ごとの行を 1 つの操作でしか持たず、別の操作で同じ形を流す行は足していない",
                    (
                        AtomicOp.WriteAllBytes,
                        [
                            AtomicTarget.NoFile,
                            AtomicTarget.ExistingLarger,
                            AtomicTarget.DirectoryAtPath,
                            AtomicTarget.FixedTmpSibling,
                            AtomicTarget.LockedFile,
                            AtomicTarget.LongName,
                            AtomicTarget.SurrogateAtCut,
                        ]
                    ),
                    (
                        AtomicOp.Write,
                        [
                            AtomicTarget.NoFile,
                            AtomicTarget.ExistingLarger,
                            AtomicTarget.DirectoryAtPath,
                            AtomicTarget.FixedTmpSibling,
                            AtomicTarget.LockedFile,
                            AtomicTarget.LongName,
                            AtomicTarget.SurrogateAtCut,
                        ]
                    ),
                    (AtomicOp.WriteAllText, [AtomicTarget.SurrogateAtCut])
                ),
                Pair(
                    Op,
                    Failure,
                    "WriteAllBytes は Write への薄い委譲で、WriteAllText も同じ Write を通る。差し替えの失敗・ディレクトリの保存先は"
                        + "WriteAllText の行が見ていて、別の操作で同じ失敗を起こす行は足していない",
                    (
                        AtomicOp.WriteAllBytes,
                        [AtomicFailure.ReplaceFails, AtomicFailure.TargetIsDirectory]
                    ),
                    (AtomicOp.Write, [AtomicFailure.ReplaceFails, AtomicFailure.TargetIsDirectory])
                ),
                Pair(
                    Op,
                    Target,
                    "書き込み先の親フォルダが無いときの振る舞い（フォルダを作るのか失敗するのか）を製品の XmlDoc が決めておらず、"
                        + "元のテストも書く操作では持たない。期待を導けないので行を足していない",
                    (AtomicOp.WriteAllText, [AtomicTarget.MissingParentFolder]),
                    (AtomicOp.WriteAllBytes, [AtomicTarget.MissingParentFolder]),
                    (AtomicOp.Write, [AtomicTarget.MissingParentFolder]),
                    (AtomicOp.CreateText, [AtomicTarget.MissingParentFolder])
                ),
                Pair(
                    Op,
                    Target,
                    "CreateText は置き換えず、衝突で断る別の経路（ReplaceOnce）を通る。元のテストは保存先が無い形と既存の形の 2 つだけで、"
                        + "長い既存・ディレクトリ・一時ファイルの同居・掴まれた保存先・長い名前の行は持たない",
                    (
                        AtomicOp.CreateText,
                        [
                            AtomicTarget.ExistingLarger,
                            AtomicTarget.DirectoryAtPath,
                            AtomicTarget.FixedTmpSibling,
                            AtomicTarget.LockedFile,
                            AtomicTarget.LongName,
                            AtomicTarget.SurrogateAtCut,
                        ]
                    )
                ),
                Pair(
                    Op,
                    Failure,
                    "CreateText は置き換えず、衝突で断る別の経路（ReplaceOnce）を通る。保存先がディレクトリのときの失敗の行は元のテストが持たない",
                    (AtomicOp.CreateText, [AtomicFailure.TargetIsDirectory])
                ),
                Pair(
                    Target,
                    Failure,
                    "CreateText は置き換えず、衝突で断る別の経路（ReplaceOnce）を通る。長い既存・掴まれた保存先との衝突の行は元のテストが持たない",
                    (AtomicTarget.ExistingLarger, [AtomicFailure.CreateConflict]),
                    (AtomicTarget.LockedFile, [AtomicFailure.CreateConflict])
                ),
                Pair(
                    Op,
                    Target,
                    "読み取りの行は、読む先が無い・在る・親が無い・掴まれた 4 つの形だけを元のテストが持つ。"
                        + "ディレクトリと長い名前の読み取りは持たない",
                    (AtomicOp.TryReadAllText, [AtomicTarget.DirectoryAtPath, AtomicTarget.LongName])
                ),
                Pair(
                    Target,
                    Failure,
                    "読み取りの行は、読む先がディレクトリの形を元のテストが持たない（ディレクトリで失敗しない読み取りの結果を数える）",
                    (AtomicTarget.DirectoryAtPath, [AtomicFailure.None])
                ),
                Pair(
                    Failure,
                    Target,
                    "書き手のコールバックの失敗を流すのは Write の既存のファイルの 1 形だけ。コールバックの失敗は保存先の形に依らず"
                        + "一時ファイルの掃除と例外の伝播で扱うが、ほかの形で流す行は足していない",
                    (
                        AtomicFailure.WriterThrows,
                        [
                            AtomicTarget.NoFile,
                            AtomicTarget.ExistingLarger,
                            AtomicTarget.FixedTmpSibling,
                            AtomicTarget.MissingParentFolder,
                            AtomicTarget.LongName,
                            AtomicTarget.SurrogateAtCut,
                        ]
                    )
                ),
            ]
        );

    /// <summary>因子の組ごとに、値を名指しした組をまとめて 1 つの項目にする（理由が同じ組）</summary>
    private static CoverageGapEntry Pair(
        string first,
        string second,
        string reason,
        params (object First, object[] Seconds)[] pairs
    )
    {
        var set = pairs
            .SelectMany(p => p.Seconds.Select(s => (p.First.ToString()!, s.ToString()!)))
            .ToHashSet();
        return CoverageGap.Matching(
            Scope,
            "P3-10",
            "P4",
            reason,
            first,
            second,
            (a, b) => set.Contains((a, b))
        );
    }

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.OsBoundary,
            "AtomicFile",
            "原子的な書き込みの表（操作 × 保存先の形 × 失敗 → 結果。QuickER.Platform の AtomicFile）",
            Table,
            AtomicFileRows.Table.Count,
            [
                "tests/QuickER.Tests/Platform/OsBoundary/AtomicFileDimensions.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/AtomicFileRows.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/AtomicFileRows.Write.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/AtomicFileRows.Read.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/AtomicFileRows.Names.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/AtomicFileTableTests.cs",
            ],
            ["src/QuickER.Platform/AtomicFile.cs"]
        );

    /// <summary>1 行を流した後、操作・保存先の形・失敗を数える</summary>
    public static void Observe(CoverageCounter counter, AtomicCase row) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Op] = row.Op.ToString(),
                [Target] = row.Target.ToString(),
                [Failure] = row.Failure.ToString(),
            }
        );

    /// <summary>書く操作か</summary>
    private static bool IsWrite(AtomicOp op) =>
        op
            is AtomicOp.WriteAllText
                or AtomicOp.WriteAllBytes
                or AtomicOp.Write
                or AtomicOp.CreateText;

    /// <summary>
    /// 操作が受け取る保存先・名前の形。ファイルの状態（長さ・一時ファイルの同居など）は書く操作にだけ意味があり、
    /// 一時ファイルの形の分類は <c>IsTemporaryFileName</c> にだけ意味がある
    /// </summary>
    public static IReadOnlyCollection<AtomicTarget> TargetsOf(AtomicOp op) =>
        op switch
        {
            AtomicOp.WriteAllText
            or AtomicOp.WriteAllBytes
            or AtomicOp.Write
            or AtomicOp.CreateText =>
            [
                AtomicTarget.NoFile,
                AtomicTarget.ExistingFile,
                AtomicTarget.ExistingLarger,
                AtomicTarget.DirectoryAtPath,
                AtomicTarget.FixedTmpSibling,
                AtomicTarget.LockedFile,
                AtomicTarget.MissingParentFolder,
                AtomicTarget.LongName,
                AtomicTarget.SurrogateAtCut,
            ],
            AtomicOp.TryReadAllText =>
            [
                AtomicTarget.NoFile,
                AtomicTarget.ExistingFile,
                AtomicTarget.DirectoryAtPath,
                AtomicTarget.LockedFile,
                AtomicTarget.MissingParentFolder,
                AtomicTarget.LongName,
            ],
            AtomicOp.TemporaryPathFor => [AtomicTarget.LongName, AtomicTarget.SurrogateAtCut],
            AtomicOp.IsTemporaryFileName =>
            [
                AtomicTarget.TemporaryFormName,
                AtomicTarget.NonTemporaryName,
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
        };

    /// <summary>
    /// 保存先の形から必ず決まる失敗（無ければ <see cref="AtomicFailure.None"/>）。
    /// 保存先がディレクトリ・排他で掴まれている・置き換えない書き込みの先に既存のファイルがあれば、書く操作は必ず失敗する
    /// </summary>
    public static AtomicFailure NaturalFailure(AtomicOp op, AtomicTarget target)
    {
        if (!IsWrite(op))
        {
            return AtomicFailure.None;
        }

        return target switch
        {
            AtomicTarget.DirectoryAtPath => AtomicFailure.TargetIsDirectory,
            AtomicTarget.LockedFile when op == AtomicOp.CreateText => AtomicFailure.CreateConflict,
            AtomicTarget.LockedFile => AtomicFailure.ReplaceFails,
            AtomicTarget.ExistingFile
            or AtomicTarget.ExistingLarger when op == AtomicOp.CreateText =>
                AtomicFailure.CreateConflict,
            _ => AtomicFailure.None,
        };
    }

    /// <summary>
    /// 原理的に起こり得ない組：操作が受け取らない保存先の形、保存先の形から決まる失敗と食い違う失敗、
    /// コールバックを持たない操作の書き込み中の失敗
    /// </summary>
    /// <remarks>
    /// 次元表は因子の一部だけの割り当て（2 因子の組）も渡すので、足りない因子には「成り立つ水準がある」ことだけを見る
    /// （どの水準で補っても成り立たない組だけを外す）。
    /// </remarks>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        var ops = assignment.TryGetValue(Op, out var opName)
            ? [Enum.Parse<AtomicOp>(opName)]
            : Enum.GetValues<AtomicOp>();
        var targets = assignment.TryGetValue(Target, out var targetName)
            ? [Enum.Parse<AtomicTarget>(targetName)]
            : Enum.GetValues<AtomicTarget>();
        var failures = assignment.TryGetValue(Failure, out var failureName)
            ? [Enum.Parse<AtomicFailure>(failureName)]
            : Enum.GetValues<AtomicFailure>();

        return ops.Any(op =>
            targets.Any(target => failures.Any(failure => IsValidTriple(op, target, failure)))
        );
    }

    /// <summary>3 因子がそろった組が成り立つか</summary>
    private static bool IsValidTriple(AtomicOp op, AtomicTarget target, AtomicFailure failure)
    {
        if (!TargetsOf(op).Contains(target))
        {
            return false;
        }

        var natural = NaturalFailure(op, target);

        if (failure == natural)
        {
            return true;
        }

        // 書き込み中の失敗は書き手のコールバックを持つ Write だけで、先がもともと成功する形のときだけ重ねられる
        return failure == AtomicFailure.WriterThrows
            && op == AtomicOp.Write
            && natural == AtomicFailure.None;
    }
}
