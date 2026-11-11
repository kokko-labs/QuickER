using QuickER.Documents;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>文書の状態機械のエンジンの次元表・網羅の数え方・台帳の行</summary>
/// <remarks>
/// <para>
/// 1 手を 1 ケースとして、手の前の文書の状態（無題か・未保存か）・現在のファイルのディスクの状態（最終既知の版と比べて）・
/// 手の種類・確認の応答（聞かれなかったかも含む）・手の間だけ文書のファイルに掛かったアクセスの条件の 5 因子を数える。
/// </para>
/// <para>
/// 数え方は参照モデルだけで手の列をなぞる（VM もディスクも使わない）。手の前の状態はモデルの状態、応答は「その手で確認が
/// 出たか」をモデルの出力から読む。版どうしの同一性は名前の一致で見る（VM が同じ内容を 2 回書いた版を別の版と数える違いは、
/// 数えるだけなので許す）。
/// </para>
/// </remarks>
internal static class DocumentFileDimensions
{
    /// <summary>手の種類</summary>
    public const string Op = "Op";

    /// <summary>手の前の文書（無題・紐づき × クリーン・ダーティ）</summary>
    public const string Document = "Document";

    /// <summary>手の前の、現在のファイルのディスクの状態</summary>
    public const string Disk = "Disk";

    /// <summary>確認への応答（聞かれなかった手は NotAsked）</summary>
    public const string Answer = "Answer";

    /// <summary>手が読み書きする文書のファイルに、手の間だけ掛かったアクセスの条件（掛からなければ None）</summary>
    public const string Access = "Access";

    /// <summary>
    /// 文書のファイル（開く・保存先・現在のファイル）を読み書きし得る手。ほかの手はアクセスの条件を持たない
    /// （条件は手が読み書きするファイルに掛けるもので、読み書きしない手には掛ける先が無い）
    /// </summary>
    private static readonly HashSet<string> TouchesDocumentFile = new(StringComparer.Ordinal)
    {
        "Open",
        "Save",
        "SaveAs",
        $"Deliver{DocumentFileChangeKind.Modified}",
        "Restart",
    };

    /// <summary>確認を出さない手（応答の因子が NotAsked しか取り得ない）</summary>
    private static readonly HashSet<string> NeverAsks = new(StringComparer.Ordinal)
    {
        "Edit",
        "EditUntracked",
        "Replace",
        "ExternalWrite",
        "ExternalRestore",
        "ExternalDelete",
        $"Deliver{DocumentFileChangeKind.Deleted}",
        $"Deliver{DocumentFileChangeKind.Renamed}",
        $"Deliver{DocumentFileChangeKind.WatchInterrupted}",
        $"Deliver{DocumentFileChangeKind.WatchResumed}",
        "AutoSave",
        "AutoSaveMetaLost",
        "StripMetaHash",
        "ExportToCurrent",
        "SetPath",
        "Undo",
        "DragBegin",
    };

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.DocumentFile,
            [
                new Factor(
                    Op,
                    [
                        new Level("New"),
                        new Level("Open"),
                        new Level("Save"),
                        new Level("SaveAs"),
                        new Level("Edit"),
                        new Level("EditUntracked"),
                        new Level("Replace"),
                        new Level("ExternalWrite"),
                        new Level("ExternalRestore"),
                        new Level("ExternalDelete"),
                        .. Enum.GetValues<DocumentFileChangeKind>()
                            .Select(kind => new Level($"Deliver{kind}", AxisMember.Of(kind))),
                        new Level("AutoSave"),
                        new Level("AutoSaveMetaLost"),
                        new Level("StripMetaHash"),
                        new Level("Restart"),
                        new Level("ExportToCurrent"),
                        new Level("SetPath"),
                        new Level("Undo"),
                        new Level("DragBegin"),
                        new Level("ImportMermaid"),
                    ],
                    "手の種類（監視イベントの配送は変更の種類ごと）"
                ),
                new Factor(
                    Document,
                    [
                        new Level("UntitledClean"),
                        new Level("UntitledDirty"),
                        new Level("BoundClean"),
                        new Level("BoundDirty"),
                    ],
                    "手の前の文書（無題か・未保存か）"
                ),
                new Factor(
                    Disk,
                    [
                        new Level("Untitled"),
                        new Level("Same"),
                        new Level("Ignored"),
                        new Level("Missing"),
                        new Level("Diagram"),
                        new Level("Newer"),
                        new Level("UnreadableVersion"),
                        new Level(
                            nameof(DocumentLoadError.InvalidJson),
                            AxisMember.Of(DocumentLoadError.InvalidJson)
                        ),
                        new Level(
                            nameof(DocumentLoadError.NotDiagramDocument),
                            AxisMember.Of(DocumentLoadError.NotDiagramDocument)
                        ),
                        new Level(
                            nameof(DocumentLoadError.DuplicateId),
                            AxisMember.Of(DocumentLoadError.DuplicateId)
                        ),
                        new Level(
                            nameof(DocumentLoadError.MissingRequiredText),
                            AxisMember.Of(DocumentLoadError.MissingRequiredText)
                        ),
                    ],
                    "手の前の、現在のファイルのディスク（最終既知・無視した版と同じか、違うならその中身の種類）"
                ),
                new Factor(
                    Answer,
                    [new Level("Yes"), new Level("No"), new Level("NotAsked")],
                    "確認への応答（その手で確認が出なければ NotAsked）"
                ),
                new Factor(
                    Access,
                    [
                        new Level("None"),
                        new Level(nameof(DocAccessKind.ShareNone)),
                        new Level(nameof(DocAccessKind.ShareRead)),
                        new Level(nameof(DocAccessKind.ReadOnly)),
                    ],
                    "手の間だけ文書のファイルに掛かったアクセスの条件（読めない・書けない。掛からなければ None）"
                ),
            ],
            null,
            IsValid
        );

    /// <summary>原理的に起こり得ない組</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        // 無題の文書には現在のファイルが無く、紐づいた文書のディスクの状態は Untitled にならない
        var document = assignment.GetValueOrDefault(Document);
        var disk = assignment.GetValueOrDefault(Disk);

        if (
            document is not null
            && disk is not null
            && document.StartsWith("Untitled", StringComparison.Ordinal) != (disk == "Untitled")
        )
        {
            return false;
        }

        // 「続行」で無視した版は、クリーンへ戻るどの経路でも捨てられる（保存・開く・再読込は UpdateDocumentIdentity で、
        // 新規作成と復元はパスの変更・新しい VM で）。紐づいたクリーンな文書のディスクが無視した版のままになることは無い
        if (document == "BoundClean" && disk == "Ignored")
        {
            return false;
        }

        // 確認を出さない手は、応答の因子が NotAsked しか取り得ない
        if (
            assignment.TryGetValue(Op, out var op)
            && assignment.TryGetValue(Answer, out var answer)
            && NeverAsks.Contains(op)
            && answer != "NotAsked"
        )
        {
            return false;
        }

        // 文書のファイルを読み書きしない手は、アクセスの条件が None しか取り得ない
        if (
            assignment.TryGetValue(Op, out var touching)
            && assignment.TryGetValue(Access, out var access)
            && !TouchesDocumentFile.Contains(touching)
            && access != "None"
        )
        {
            return false;
        }

        return true;
    }

    /// <summary>乱数の手の列と網羅の照合の範囲</summary>
    public const string RandomScope = "document-file";

    /// <summary>既知の失敗の一覧（空で保つ。製品の不具合が出たら載せずに止めて報告する）</summary>
    public static KnownFailureSet KnownFailures => new(EngineIds.DocumentFile, []);

    /// <summary>網羅しない組の一覧（シナリオと普段の既定の種の数の乱数の手の列で欠ける組）</summary>
    /// <remarks>
    /// 普段の種の並びに出にくい組（出やすさの低い手と短命なディスクの状態の組）は、決まった形のシナリオで埋める。
    /// 乱数の生成器を変えて種の指紋が変わり、欠ける組が出たら、一覧に足すより先にシナリオで埋められないかを見る。
    /// </remarks>
    public static CoverageGapSet Gaps => CoverageGap.Set(EngineIds.DocumentFile, []);

    /// <summary>決まった形のシナリオの台帳の行</summary>
    public static EngineDescriptor ScenariosDescriptor =>
        new(
            EngineIds.DocumentFile,
            "Scenarios",
            "文書の状態機械の決まった形のシナリオ（元の単発のテストの手の列）",
            Table,
            DocumentFileScenarios.Table.Count,
            SourceFiles,
            Covers
        );

    /// <summary>乱数の手の列の台帳の行</summary>
    public static EngineDescriptor RandomDescriptor =>
        new(
            EngineIds.DocumentFile,
            "Random",
            "文書の状態機械の乱数の手の列（種ごとの手の列を VM と参照モデルで流す）",
            Table,
            0,
            SourceFiles,
            Covers
        );

    /// <summary>エンジンのソース</summary>
    private static IReadOnlyList<string> SourceFiles =>
        [
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileDimensions.cs",
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileGenerator.cs",
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileHarness.cs",
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileInvariants.cs",
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileModel.cs",
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileOps.cs",
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileProbe.cs",
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileRunner.cs",
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.cs",
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.Document.cs",
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.ExternalChange.cs",
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.HashRace.cs",
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.OverwriteGuard.cs",
            "tests/QuickER.Tests/Gui/ViewModels/DocumentFileTests.cs",
        ];

    /// <summary>エンジンが守る src の範囲</summary>
    private static IReadOnlyList<string> Covers =>
        [
            "src/QuickER.Gui/ViewModels/MainViewModel.ImportExport.cs",
            "src/QuickER.Gui/Services/DocumentContentHash.cs",
            "src/QuickER.Document/JsonStorageService.cs",
        ];

    /// <summary>手の列を参照モデルだけでなぞり、1 手ずつ数える</summary>
    public static void Observe(CoverageCounter counter, IReadOnlyList<DocStep> steps)
    {
        var model = new DocumentFileModel(
            (left, right) => string.Equals(left, right, StringComparison.Ordinal),
            file => file.ToString()
        );

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var document =
                (model.Path is null ? "Untitled" : "Bound") + (model.Dirty ? "Dirty" : "Clean");
            var disk = DiskLevel(model);

            model.Apply(index, step);

            var answer =
                model.Confirms.Count == 0 ? "NotAsked"
                : step.Answer ? "Yes"
                : "No";

            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Op] = step.Op.Kind,
                    [Document] = document,
                    [Disk] = disk,
                    [Answer] = answer,
                    [Access] = model.AppliedAccess?.KindName ?? "None",
                }
            );
        }
    }

    private static string DiskLevel(DocumentFileModel model)
    {
        if (model.Path is not { } path)
        {
            return "Untitled";
        }

        if (model.Disk[path] is not { } label)
        {
            return "Missing";
        }

        if (label == model.LastKnown)
        {
            return "Same";
        }

        if (label == model.Ignored)
        {
            return "Ignored";
        }

        return model.Versions[label].Content switch
        {
            DocContentKind.Diagram
            or DocContentKind.DiagramWithLayout
            or DocContentKind.DiagramPartialLayout
            or DocContentKind.DiagramWithBom
            or DocContentKind.NullCollections => "Diagram",
            DocContentKind.Newer => "Newer",
            DocContentKind.UnreadableVersion => "UnreadableVersion",
            DocContentKind.Corrupt => nameof(DocumentLoadError.InvalidJson),
            DocContentKind.NotDiagram or DocContentKind.NullSchema => nameof(
                DocumentLoadError.NotDiagramDocument
            ),
            DocContentKind.DuplicateId => nameof(DocumentLoadError.DuplicateId),
            DocContentKind.MissingRequiredText => nameof(DocumentLoadError.MissingRequiredText),
            // BrokenReference は乱数の手の列が作らず、決まった形の開くの行（現在のファイルにならない）だけが使うので、
            // ここ（現在のファイルの中身）には来ない。来たら水準の宣言の漏れなので投げる
            _ => throw new ArgumentOutOfRangeException(nameof(model), label, null),
        };
    }
}
