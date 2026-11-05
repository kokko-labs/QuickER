using System.Globalization;
using System.Text;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>文書の状態機械の乱数の手の列を作る（種ごとに決定的）</summary>
/// <remarks>
/// <para>
/// 手の重みは「外部の書き込み → 監視の配送・保存・開く」の組み合わせが出やすいように置く。手の選び方は、それまでの手を参照モデル
/// （版の同一性は名前の一致）でなぞった状態を読む（例：表が無ければ名前の変更を選ばない）。読むのは乱数を引かない決定的な状態だけで、
/// 種の指紋が守るのは乱数の引き方と手の列そのもの。
/// </para>
/// <para>
/// 手はどの状態でも定義される形にしてある（範囲外の表の番号・無題の書き出しは、ハーネスとモデルの両方で何もしない）。
/// 実行時の版の同一性（バイト列）が名前の一致と食い違って状態がずれても、手が壊れない。
/// </para>
/// </remarks>
internal static class DocumentFileGenerator
{
    /// <summary>種の数（普段 150・夜間 1500・変数 <c>QUICKER_DOCFILE_SEEDS</c>・1 種だけは <c>QUICKER_DOCFILE_SEED</c>）</summary>
    public static SeedSettings Seeds { get; } =
        new("QUICKER_DOCFILE_SEEDS", 150, 1500, singleVariable: "QUICKER_DOCFILE_SEED");

    /// <summary>1 種の手の数</summary>
    public const int Length = 40;

    /// <summary>種から手の列を作る</summary>
    public static IReadOnlyList<DocStep> Generate(int seed)
    {
        var random = new Random(seed);
        var model = new DocumentFileModel(
            (left, right) => string.Equals(left, right, StringComparison.Ordinal),
            file => file.ToString()
        );
        var steps = new List<DocStep>(Length);
        var labels = 0;
        var seenOnA = new List<string>();

        string NewLabel() => string.Create(CultureInfo.InvariantCulture, $"x{++labels}");

        for (var index = 0; index < Length; index++)
        {
            var step = Next(random, model, NewLabel, seenOnA);
            steps.Add(step);
            model.Apply(index, step);

            if (model.Disk[DocFile.A] is { } onA && !seenOnA.Contains(onA))
            {
                seenOnA.Add(onA);
            }
        }

        return steps;
    }

    /// <summary>手の種類（重みの表の行）</summary>
    private enum Pick
    {
        Add,
        Rename,
        Undo,
        Queries,
        Replace,
        Import,
        New,
        Open,
        Save,
        SaveAs,
        Write,
        Restore,
        Delete,
        Modified,
        OtherEvent,
        AutoSave,
        StripMetaHash,
        Restart,
        Export,
        Drag,
        SetPath,
    }

    /// <summary>
    /// 手の重み（合計 100）。外部の書き込みを厚くし、ディスクが最終既知から外れた状態で各手が流れるようにする
    /// （外れた状態は次の配送・保存・開くで戻りやすく短命なので、書き込みが薄いと手 × ディスクの組が埋まらない）
    /// </summary>
    private static (Pick Pick, int Weight)[] Weights =>
        [
            (Pick.Add, 7),
            (Pick.Rename, 3),
            (Pick.Undo, 3),
            (Pick.Queries, 3),
            (Pick.Replace, 2),
            (Pick.Import, 2),
            (Pick.New, 2),
            (Pick.Open, 6),
            (Pick.Save, 7),
            (Pick.SaveAs, 3),
            (Pick.Write, 20),
            (Pick.Restore, 3),
            (Pick.Delete, 3),
            (Pick.Modified, 7),
            (Pick.OtherEvent, 9),
            (Pick.AutoSave, 4),
            (Pick.StripMetaHash, 2),
            (Pick.Restart, 4),
            (Pick.Export, 3),
            (Pick.Drag, 3),
            (Pick.SetPath, 4),
        ];

    private static DocStep Next(
        Random random,
        DocumentFileModel model,
        Func<string> newLabel,
        List<string> seenOnA
    )
    {
        var answer = random.Next(10) < 6;
        var roll = random.Next(100);
        var pick = Pick.Add;

        foreach (var (candidate, weight) in Weights)
        {
            if (roll < weight)
            {
                pick = candidate;
                break;
            }

            roll -= weight;
        }

        var step = pick switch
        {
            Pick.Add => Doc.Add(),
            Pick.Rename => Doc.Rename(random.Next(Math.Max(1, model.Tables.Count)), newLabel()),
            Pick.Undo => model.CanUndo || random.Next(2) == 0 ? Doc.Undo() : Doc.Add(),
            Pick.Queries => Doc.ReplaceQueries(random.Next(3), orphan: true),
            Pick.Replace => Doc.ReplaceFromModule(newLabel()),
            Pick.Import => Doc.ImportMermaid(newLabel()),
            Pick.New => Doc.New(),
            Pick.Open => WithRace(
                random,
                Doc.Open(random.Next(4) == 0 ? DocFile.B : DocFile.A),
                DocHook.AfterLoadRead,
                newLabel
            ),
            Pick.Save => WithRace(
                random,
                Doc.Save(PickTarget(random)),
                DocHook.AfterSaveWrite,
                newLabel
            ),
            Pick.SaveAs => random.Next(4) switch
            {
                0 => Doc.SaveAs(DocFile.A, upperCase: true),
                1 => Doc.SaveAs(DocFile.B),
                2 => Doc.SaveAs(DocFile.Unwritable),
                _ => Doc.SaveAs(DocFile.A),
            },
            Pick.Write => new DocStep(
                ExternalWrite(random, newLabel(), random.Next(5) == 0 ? DocFile.B : DocFile.A)
            ),
            Pick.Restore => seenOnA.Count > 0
                ? Doc.Restore(DocFile.A, seenOnA[random.Next(seenOnA.Count)])
                : Doc.Write(DocFile.A, newLabel()),
            Pick.Delete => Doc.Delete(random.Next(4) == 0 ? DocFile.B : DocFile.A),
            Pick.Modified => random.Next(10) switch
            {
                0 => Doc.Modified()
                    .RaceAt(DocHook.AfterLoadRead, Doc.WriteOp(DocFile.A, newLabel())),
                2 => new DocStep(
                    new DeliverOp(
                        DocumentFileChangeKind.Modified,
                        DocEventHash.CurrentDisk,
                        CaseVariant: true
                    )
                ),
                1 => Doc.Modified()
                    .DuringFirstConfirm(
                        ExternalWrite(random, newLabel(), DocFile.A),
                        Doc.ModifiedOp()
                    ),
                _ => Doc.Modified(),
            },
            Pick.OtherEvent => random.Next(6) switch
            {
                0 => Doc.Modified(DocEventHash.Unknown),
                1 => new DocStep(
                    new DeliverOp(
                        DocumentFileChangeKind.Modified,
                        DocEventHash.Unknown,
                        OtherPath: true
                    )
                ),
                2 => Doc.Event(DocumentFileChangeKind.Deleted),
                3 => Doc.Event(DocumentFileChangeKind.Renamed),
                4 => Doc.Event(DocumentFileChangeKind.WatchInterrupted),
                _ => Doc.Event(DocumentFileChangeKind.WatchResumed),
            },
            Pick.AutoSave => random.Next(2) == 0 ? Doc.AutoSave(metaLost: true) : Doc.AutoSave(),
            Pick.StripMetaHash => Doc.StripMetaHash(),
            Pick.Restart => Doc.Restart(),
            Pick.Export => Doc.ExportToCurrent(),
            Pick.Drag => Doc.DragBegin(random.Next(Math.Max(1, model.Tables.Count)), 40, 30),
            _ => Doc.SetPath(random.Next(3) == 0 ? DocFile.B : DocFile.A),
        };

        return answer ? step : step.No();
    }

    /// <summary>1 割の確率で、口に割り込みの書き込みを足す</summary>
    private static DocStep WithRace(
        Random random,
        DocStep step,
        DocHook hook,
        Func<string> newLabel
    ) => random.Next(10) == 0 ? step.RaceAt(hook, Doc.WriteOp(DocFile.A, newLabel())) : step;

    private static DocFile? PickTarget(Random random) =>
        random.Next(8) switch
        {
            0 => null,
            1 => DocFile.B,
            2 => DocFile.Unwritable,
            _ => DocFile.A,
        };

    /// <summary>外部の書き込み（中身の種類は図が 3 割・残りを将来版と読めない形に振る）</summary>
    private static ExternalWriteOp ExternalWrite(Random random, string label, DocFile file)
    {
        var roll = random.Next(100);
        var content = roll switch
        {
            < 30 => DocContentKind.Diagram,
            < 44 => DocContentKind.Newer,
            < 54 => DocContentKind.UnreadableVersion,
            < 64 => DocContentKind.Corrupt,
            < 72 => DocContentKind.NotDiagram,
            < 81 => DocContentKind.DuplicateId,
            < 90 => DocContentKind.MissingRequiredText,
            < 94 => DocContentKind.DiagramWithBom,
            < 97 => DocContentKind.NullCollections,
            _ => DocContentKind.NullSchema,
        };
        var table = content
            is DocContentKind.Diagram
                or DocContentKind.DiagramWithBom
                or DocContentKind.Newer
            ? label
            : null;

        return new ExternalWriteOp(file, content, table, label);
    }

    /// <summary>種の指紋の要約（手の列を 1 手 1 行で）</summary>
    public static string Summary(int seed)
    {
        var text = new StringBuilder();

        foreach (var step in Generate(seed))
        {
            text.Append(step).Append('\n');
        }

        return text.ToString();
    }
}
