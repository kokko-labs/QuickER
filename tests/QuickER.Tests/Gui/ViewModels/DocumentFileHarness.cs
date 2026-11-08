using System.IO;
using System.Text;
using QuickER.Behaviors;
using QuickER.Documents;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>1 手の後の VM とディスクの観測（モデルとの照合・不変条件・元のテストの表明が読む）</summary>
internal sealed record DocObservation
{
    public required DocumentFileHarness Harness { get; init; }

    public required MainViewModel Vm { get; init; }

    public required StubDialogService Dialogs { get; init; }

    public required string? Path { get; init; }

    public required DocFile? PathFile { get; init; }

    public required bool IsDirty { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<string> Tables { get; init; }

    public required int Queries { get; init; }

    public required string Status { get; init; }

    public required bool Notified { get; init; }

    public required bool CanUndo { get; init; }

    public required IReadOnlyList<DocConfirm> Confirms { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }

    public required IReadOnlyList<string> Informations { get; init; }

    public required bool DragActive { get; init; }

    public required int MaxConfirmDepth { get; init; }

    public required bool FitRequested { get; init; }

    public required int SaveDialogs { get; init; }

    public required string? LastKnownHash { get; init; }

    public required string? IgnoredHash { get; init; }

    public required IReadOnlyDictionary<DocFile, string?> DiskHashes { get; init; }

    public required string? AutoSaveHash { get; init; }

    public required CurrentDocumentSettings Meta { get; init; }

    /// <summary>この手の警告の確認の本文（順に）</summary>
    public IReadOnlyList<string> WarningConfirms =>
        Confirms.Where(c => c.Warning).Select(c => c.Message).ToList();

    /// <summary>この手の通常の確認の本文（順に）</summary>
    public IReadOnlyList<string> PlainConfirms =>
        Confirms.Where(c => !c.Warning).Select(c => c.Message).ToList();

    /// <summary>ファイルを図として読んだ表の名前（読めなければ例外）</summary>
    public IReadOnlyList<string> DiskTables(DocFile file) =>
        JsonStorageService
            .Load(Harness.PathOf(file))
            .Schema.Entities.Select(e => e.TableName)
            .ToList();

    /// <summary>ファイルの図の Version</summary>
    public int DiskVersion(DocFile file) => JsonStorageService.Load(Harness.PathOf(file)).Version;

    /// <summary>ファイルの文字列</summary>
    public string DiskText(DocFile file) => File.ReadAllText(Harness.PathOf(file));

    /// <summary>ファイルのバイト列</summary>
    public byte[] DiskBytes(DocFile file) => File.ReadAllBytes(Harness.PathOf(file));

    /// <summary>版のバイト列</summary>
    public byte[] BytesOf(string label) => Harness.BytesOf(label);

    /// <summary>ファイルの実パス</summary>
    public string PathOf(DocFile file) => Harness.PathOf(file);
}

/// <summary>開く・保存ダイアログの結果を手ごとに差し替えられるテストダブル</summary>
internal sealed class SteppingFileDialogService : IFileDialogService
{
    public FileDialogResult? OpenResult { get; set; }

    public FileDialogResult? SaveResult { get; set; }

    public int SaveDialogCallCount { get; set; }

    public FileDialogResult? PickOpenFile(string filter) => OpenResult;

    public FileDialogResult? PickSaveFile(
        string filter,
        string defaultExt,
        string? initialFileName = null,
        string? initialDirectory = null
    )
    {
        SaveDialogCallCount++;
        return SaveResult;
    }

    public string? PickFolder(string title, string? initialDirectory = null) => null;
}

/// <summary>
/// 文書の状態機械のハーネス（一時フォルダ・実ファイル・VM・版の台帳・製品の口への差し込み）
/// </summary>
/// <remarks>
/// <para>
/// 版の台帳：ハーネスが書いたバイト列と VM が書いたバイト列に名前（ラベル）を付け、ハッシュで引けるようにする。
/// 同じバイト列には複数の名前が付き得る（VM が同じ内容を 2 回書く・自動保存と保存が同じ内容）ので、比べるのは
/// ハッシュ（<see cref="Same"/>）。
/// </para>
/// <para>
/// 製品の口：監視は止め（<see cref="MainViewModel.DisableFileWatchingForTests"/>）、監視イベントは
/// <see cref="MainViewModel.RaiseExternalChangeForTests"/> で配送する。上書き保存と開く・再読込の口
/// （<c>AfterSaveWriteForTests</c>・<c>AfterLoadReadForTests</c>）は常に差し込み、VM が書いた／読んだ版を控え、
/// 割り込みの書き込みを 1 回だけ流す。
/// </para>
/// </remarks>
internal sealed class DocumentFileHarness : IDisposable
{
    /// <summary>どの版とも一致しない監視イベントのハッシュの版の名前</summary>
    public const string UnknownHashLabel = "#unknown";

    private const string UnknownHash = "deadbeef";

    private readonly TestTempFolder _temp = new();

    private readonly Dictionary<string, string> _hashOfLabel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _labelOfHash = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _bytesOfLabel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DocContentKind> _kindOfLabel = new(StringComparer.Ordinal);

    // 1 手の文脈
    private int _step;
    private int _saveCount;
    private DocStep _current = new(new NewOp());
    private bool _raceDone;
    private bool _duringConfirmDone;
    private int _confirmDepth;
    private DocFile? _openTarget;
    private DocFile? _saveTarget;
    private bool _fitRequested;

    public DocumentFileHarness()
    {
        DragBehavior.CancelActiveDrag();
        RubberBandBehavior.CancelActiveSelection();
        _hashOfLabel[UnknownHashLabel] = UnknownHash;
        Disk = new()
        {
            [DocFile.A] = null,
            [DocFile.B] = null,
            [DocFile.Unwritable] = null,
        };
        (Vm, Dialogs, Files) = CreateViewModel();
    }

    /// <summary>いまの VM（再起動で作り直す）</summary>
    public MainViewModel Vm { get; private set; }

    /// <summary>いまの VM の確認ダイアログのテストダブル</summary>
    public StubDialogService Dialogs { get; private set; }

    /// <summary>いまの VM のファイルダイアログのテストダブル</summary>
    public SteppingFileDialogService Files { get; private set; }

    /// <summary>ハーネスが知っているファイルごとの版（外部の書き込みと VM の上書き保存で動く）</summary>
    public Dictionary<DocFile, string?> Disk { get; }

    /// <summary>一時フォルダのフルパス</summary>
    public string Folder => _temp.Path;

    /// <summary>自動保存の図（last_diagram.json）のパス</summary>
    public string AutoSavePath => System.IO.Path.Combine(_temp.Path, "last_diagram.json");

    /// <summary>gui-settings.json のパス</summary>
    public string SettingsPath =>
        System.IO.Path.Combine(_temp.Path, GuiAppSettingsStore.DefaultFileName);

    // ---------------- 不変条件のための記録（1 手ごと） ----------------

    /// <summary>この手で VM が書いた版（書いた先・書く前の版・書いた版・その時点までの確認の数）</summary>
    public List<(
        DocFile File,
        string? Replaced,
        string Label,
        int ConfirmsBefore
    )> VmWrites { get; } = [];

    /// <summary>この手で VM が最後に読み書きした版（上書き保存・開く・再読込の口を通ったとき）</summary>
    public string? LastIo { get; private set; }

    /// <summary>この手で始めたドラッグの表と開始位置（無ければ null）</summary>
    public (EntityViewModel Entity, double X, double Y)? DragStart { get; private set; }

    /// <summary>手の始めの変更の世代</summary>
    public int StartChangeGeneration { get; private set; }

    /// <summary>手の始めのファイルごとのハッシュ</summary>
    public Dictionary<DocFile, string?> StartHashes { get; } = [];

    /// <summary>ファイルが手の始めから変わっていないか（バイト列のハッシュで比べる）</summary>
    public bool UnchangedSinceStepStart(DocFile file) =>
        StartHashes[file] == DocumentContentHash.TryCompute(PathOf(file));

    /// <summary>この手で確認が入れ子になった深さの最大</summary>
    public int MaxConfirmDepth { get; private set; }

    /// <summary>ファイルの実パス</summary>
    public string PathOf(DocFile file) =>
        file switch
        {
            DocFile.A => System.IO.Path.Combine(_temp.Path, "Doc.json"),
            DocFile.B => System.IO.Path.Combine(_temp.Path, "Other.json"),
            DocFile.Unwritable => System.IO.Path.Combine(
                _temp.Path,
                "missing-directory",
                "Doc.json"
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(file), file, null),
        };

    /// <summary>大文字だけ違う A の実パス</summary>
    public string UpperCasePathOfA => System.IO.Path.Combine(_temp.Path, "DOC.JSON");

    /// <summary>実パスからファイルを引く（大文字小文字を区別しない・知らないパスは null）</summary>
    public DocFile? FileOf(string? path) =>
        path is null
            ? null
            : Enum.GetValues<DocFile>()
                .Cast<DocFile?>()
                .FirstOrDefault(file =>
                    string.Equals(
                        System.IO.Path.GetFullPath(PathOf(file!.Value)),
                        System.IO.Path.GetFullPath(path),
                        StringComparison.OrdinalIgnoreCase
                    )
                );

    /// <summary>2 つの版が同じバイト列か（null どうしは真）</summary>
    public bool Same(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(HashOf(left), HashOf(right), StringComparison.Ordinal);
    }

    /// <summary>版のハッシュ（知らない名前は例外）</summary>
    public string HashOf(string label) =>
        _hashOfLabel.TryGetValue(label, out var hash)
            ? hash
            : throw new InvalidOperationException($"ハーネスが知らない版: {label}");

    /// <summary>ハッシュの版の名前（知らなければ ?先頭 8 文字）</summary>
    public string LabelOf(string? hash) =>
        hash is null ? "(none)"
        : _labelOfHash.TryGetValue(hash, out var label) ? label
        : "?" + hash[..Math.Min(8, hash.Length)];

    /// <summary>版のバイト列</summary>
    public byte[] BytesOf(string label) => _bytesOfLabel[label];

    /// <summary>版を図として読む（一時フォルダに書いて JsonStorageService.Load で読む）</summary>
    public DiagramDocument Decode(string label)
    {
        var path = System.IO.Path.Combine(_temp.Path, "decode.json");
        File.WriteAllBytes(path, BytesOf(label));
        return JsonStorageService.Load(path);
    }

    /// <summary>版が将来版の確認の対象か（将来版・版を解釈できない。VM が書いた版は現行の版）</summary>
    public bool IsNewerFormatFile(string label) =>
        _kindOfLabel.TryGetValue(label, out var kind)
        && kind is DocContentKind.Newer or DocContentKind.UnreadableVersion;

    private (MainViewModel, StubDialogService, SteppingFileDialogService) CreateViewModel()
    {
        var dialogs = new StubDialogService();
        var files = new SteppingFileDialogService();
        var vm = new MainViewModel(dialogs, files: files);
        vm.UsePersistenceForTests(new GuiAppSettingsStore(_temp.Path), AutoSavePath);
        vm.DisableFileWatchingForTests();
        vm.FitToWindowRequested += (_, _) => _fitRequested = true;
        vm.AfterSaveWriteForTests = OnAfterSaveWrite;
        vm.AfterLoadReadForTests = OnAfterLoadRead;
        dialogs.OnConfirm = OnConfirm;
        dialogs.OnConfirmWarning = OnConfirmWarning;
        return (vm, dialogs, files);
    }

    /// <summary>1 手を VM とディスクへ流す</summary>
    public void Apply(int index, DocStep step)
    {
        _step = index;
        _saveCount = 0;
        _current = step;
        _raceDone = false;
        _duringConfirmDone = false;
        _confirmDepth = 0;
        _openTarget = null;
        _saveTarget = null;
        _fitRequested = false;
        MaxConfirmDepth = 0;
        VmWrites.Clear();
        LastIo = null;
        Dialogs.ConfirmMessages.Clear();
        Dialogs.WarningConfirmMessages.Clear();
        Dialogs.ErrorMessages.Clear();
        Dialogs.InformationMessages.Clear();
        Dialogs.ConfirmResult = step.Answer;
        Files.SaveDialogCallCount = 0;
        _confirmLog.Clear();

        StartChangeGeneration = Vm.UndoRedo.ChangeGeneration;

        foreach (var file in Enum.GetValues<DocFile>())
        {
            StartHashes[file] = DocumentContentHash.TryCompute(PathOf(file));
        }

        ApplyOp(step.Op);
    }

    private readonly List<DocConfirm> _confirmLog = [];

    private void ApplyOp(DocOp op)
    {
        switch (op)
        {
            case NewOp:
                Vm.NewDiagramCommand.Execute(null);
                break;
            case OpenOp open:
                _openTarget = open.File;
                Files.OpenResult = new FileDialogResult(PathOf(open.File), 1);
                Vm.OpenCommand.Execute(null);
                break;
            case SaveOp save:
                _saveTarget = FileOf(Vm.CurrentFilePath) ?? save.DialogTarget;
                Files.SaveResult = save.DialogTarget is { } target
                    ? new FileDialogResult(PathOf(target), 1)
                    : null;
                Vm.SaveCommand.Execute(null);
                break;
            case SaveAsOp saveAs:
                _saveTarget = saveAs.Target;
                Files.SaveResult = new FileDialogResult(
                    saveAs.UpperCase && saveAs.Target == DocFile.A
                        ? UpperCasePathOfA
                        : PathOf(saveAs.Target),
                    1
                );
                Vm.SaveAsCommand.Execute(null);
                break;
            case AddEntityOp:
                Vm.AddEntityCommand.Execute(null);
                break;
            case RenameOp rename:
                if (rename.Index < Vm.Entities.Count)
                {
                    Vm.Entities[rename.Index].TableName = rename.Name;
                }

                break;
            case ReplaceQueriesOp queries:
                Vm.ReplaceQueries(
                    Enumerable
                        .Range(0, queries.Count)
                        .Select(i => new QueryDefinition
                        {
                            EntityId =
                                queries.Orphan || Vm.Entities.Count == 0
                                    ? Guid.Empty
                                    : Vm.Entities[0].Id,
                            Name = $"Query{i}",
                        })
                        .ToList()
                );
                break;
            case ReplaceFromModuleOp replace:
                var diagram = new ErDiagram { TargetDbms = "sqlserver" };
                diagram.Entities.AddRange(replace.Tables.Select(t => new Entity { TableName = t }));
                Vm.ReplaceDiagramFromModule(diagram);
                break;
            case ExternalWriteOp write:
                WriteExternal(write);
                break;
            case ExternalRestoreOp restore:
                File.WriteAllBytes(PathOf(restore.File), BytesOf(restore.Label));
                Disk[restore.File] = restore.Label;
                break;
            case ExternalDeleteOp delete:
                File.Delete(PathOf(delete.File));
                Disk[delete.File] = null;
                break;
            case DeliverOp deliver:
                Deliver(deliver);
                break;
            case AutoSaveOp { MetaLost: true }:
                using (
                    new FileStream(
                        SettingsPath,
                        FileMode.OpenOrCreate,
                        FileAccess.Read,
                        FileShare.None
                    )
                )
                {
                    Vm.AutoSave();
                }

                break;
            case AutoSaveOp:
                Vm.AutoSave();
                break;
            case StripMetaHashOp:
                var store = new GuiAppSettingsStore(_temp.Path);
                var settings = store.Load();
                settings.CurrentDocument.AutoSaveHash = null;

                if (!store.Save(settings))
                {
                    throw new InvalidOperationException("メタを書き直せない");
                }

                break;
            case ExternalAutoSaveWriteOp autoSaveWrite:
                JsonStorageService.SaveAtomic(AutoSavePath, autoSaveWrite.Build());
                Register(autoSaveWrite.Label, File.ReadAllBytes(AutoSavePath));
                _kindOfLabel[autoSaveWrite.Label] = autoSaveWrite.Content;
                break;
            case SetPathOp setPath:
                Vm.CurrentFilePath = PathOf(setPath.File);
                break;
            case UndoOp:
                Vm.UndoCommand.Execute(null);
                break;
            case SetWidthOp setWidth:
                Vm.Entities[setWidth.Index].Width = setWidth.Width;
                break;
            case AutoFitWidthsOp:
                Vm.AutoFitEntityWidthsCommand.Execute(null);
                break;
            case DragBeginOp drag when drag.Index >= Vm.Entities.Count:
                break;
            case DragBeginOp drag:
                var dragged = Vm.Entities[drag.Index];
                DragStart = (dragged, dragged.X, dragged.Y);
                DragBehavior.BeginInteractionForTests(
                    dragged,
                    startX: dragged.X,
                    startY: dragged.Y
                );
                dragged.X += drag.DeltaX;
                dragged.Y += drag.DeltaY;
                break;
            case ImportMermaidOp import:
                var mermaid = System.IO.Path.Combine(_temp.Path, "diagram.mmd");
                File.WriteAllText(mermaid, MermaidText(import.Tables));
                Files.OpenResult = new FileDialogResult(mermaid, 1);
                Vm.ImportDiagramCommand.Execute(null);
                break;
            case RestartOp:
                DragBehavior.CancelActiveDrag();
                Vm.Dispose();
                (Vm, Dialogs, Files) = CreateViewModel();
                Dialogs.ConfirmResult = _current.Answer;
                Vm.Initialize();
                break;
            case ExportToCurrentOp:
                if (Vm.CurrentFilePath is { } current)
                {
                    Files.SaveResult = new FileDialogResult(current.ToUpperInvariant(), 4);
                    Vm.ExportDiagramCommand.Execute(null);
                }

                break;
            default:
                throw new InvalidOperationException($"ハーネスが知らない手: {op}");
        }
    }

    private void Deliver(DeliverOp deliver)
    {
        var path = deliver switch
        {
            { OtherPath: true } => System.IO.Path.Combine(_temp.Path, "Stray.json"),
            { CaseVariant: true } when Vm.CurrentFilePath is { } current => current
            == current.ToUpperInvariant()
                ? current.ToLowerInvariant()
                : current.ToUpperInvariant(),
            _ => null,
        };
        var hash = deliver.Hash switch
        {
            DocEventHash.CurrentDisk => Vm.CurrentFilePath is { } current
                ? DocumentContentHash.TryCompute(current)
                : null,
            DocEventHash.Unknown => UnknownHash,
            _ => null,
        };

        Vm.RaiseExternalChangeForTests(deliver.Change, hash, path);
    }

    /// <summary>外部の書き込み（版の名前で台帳へ控える）</summary>
    private void WriteExternal(ExternalWriteOp write)
    {
        if (_bytesOfLabel.ContainsKey(write.Label))
        {
            throw new InvalidOperationException(
                $"版の名前 {write.Label} が重なっている（同じ内容を書き戻すなら ExternalRestoreOp）"
            );
        }

        var path = PathOf(write.File);

        if (write.Build is { } build)
        {
            JsonStorageService.SaveAtomic(path, build());
        }
        else if (write.RawText is { } raw)
        {
            File.WriteAllText(path, raw);
        }
        else
        {
            switch (write.Content)
            {
                case DocContentKind.Diagram:
                    JsonStorageService.SaveAtomic(path, Document(write.Table!, 0));
                    break;
                case DocContentKind.Newer:
                    JsonStorageService.SaveAtomic(
                        path,
                        Document(write.Table!, DiagramDocument.CurrentVersion + 1)
                    );
                    break;
                case DocContentKind.DiagramWithBom:
                    var json = JsonStorageService.ToJson(Document(write.Table!, 0));
                    File.WriteAllText(path, json, new UTF8Encoding(true));
                    break;
                default:
                    File.WriteAllText(path, DefaultText(write.Content));
                    break;
            }
        }

        Register(write.Label, File.ReadAllBytes(path));
        _kindOfLabel[write.Label] = write.Content;
        Disk[write.File] = write.Label;
    }

    /// <summary>
    /// 既定の文字列で重複させる表の Id（空の Guid にしない＝空は Id の欠落として先に断られ、Id の重複に届かない）
    /// </summary>
    private static readonly Guid DuplicatedEntityId = new("6d3b1c0a-2f4e-4a8b-9c5d-7e1f2a3b4c5d");

    /// <summary>既定の文字列で、どこも指さない参照に使う表の Id（図の中のどの表の Id とも違う）</summary>
    private static readonly Guid MissingEntityId = new("8a2e4f6b-1c3d-4e5f-8a9b-0c1d2e3f4a5b");

    /// <summary>種類ごとの既定の文字列（図でないもの）</summary>
    public static string DefaultText(DocContentKind content) =>
        content switch
        {
            DocContentKind.UnreadableVersion => "{\"Version\":\"2\",\"Schema\":{\"Entities\":[]}}",
            DocContentKind.Corrupt => "{ this is not json",
            DocContentKind.NotDiagram => "{\"unrelated\":true}",
            DocContentKind.DuplicateId => $$"""
                {
                  "Version": 1,
                  "Schema": {
                    "TargetDbms": "sqlserver",
                    "Entities": [
                      { "Id": "{{DuplicatedEntityId}}", "TableName": "Orders", "Columns": [] },
                      { "Id": "{{DuplicatedEntityId}}", "TableName": "OrderLines", "Columns": [] }
                    ]
                  }
                }
                """,
            DocContentKind.BrokenReference => $$"""
                {
                  "Version": 1,
                  "Schema": {
                    "TargetDbms": "sqlserver",
                    "Entities": [
                      { "Id": "{{DuplicatedEntityId}}", "TableName": "Orders", "Columns": [] }
                    ],
                    "Relationships": [
                      { "SourceEntityId": "{{MissingEntityId}}", "TargetEntityId": "{{DuplicatedEntityId}}" }
                    ]
                  }
                }
                """,
            DocContentKind.MissingRequiredText => """
                {
                  "Version": 1,
                  "Schema": {
                    "TargetDbms": "sqlserver",
                    "Entities": [
                      { "TableName": null, "Columns": [] }
                    ]
                  }
                }
                """,
            DocContentKind.NullCollections => """{"Version":1,"Schema":{"Entities":null}}""",
            DocContentKind.NullSchema => """{"Version":1,"Schema":null}""",
            _ => throw new ArgumentOutOfRangeException(nameof(content), content, null),
        };

    /// <summary>表ごとに int の主キー列を 1 つ持つ Mermaid（元のテストの取込のファイルと同じ形）</summary>
    public static string MermaidText(IReadOnlyList<string> tables) =>
        string.Join(
            Environment.NewLine,
            [
                "erDiagram",
                .. tables.SelectMany(table =>
                    new[] { $"    {table} {{", $"        int {table}Id PK", "    }" }
                ),
            ]
        ) + Environment.NewLine;

    /// <summary>表 1 つの図（元のテストの WriteDiagram と同じ形）</summary>
    private static DiagramDocument Document(string table, int version)
    {
        var document = new DiagramDocument
        {
            Schema = new ErDiagram
            {
                Entities = { new Entity { TableName = table } },
                TargetDbms = "sqlserver",
            },
            Layout = null,
        };

        if (version != 0)
        {
            document.Version = version;
        }

        return document;
    }

    private void Register(string label, byte[] bytes)
    {
        var hash = DocumentContentHash.ComputeForBytes(bytes);
        _hashOfLabel[label] = hash;
        _bytesOfLabel[label] = bytes;
        _labelOfHash.TryAdd(hash, label);
    }

    private void OnAfterSaveWrite()
    {
        // 書いた先は手の保存先（上書き保存は現在のパス・無題は保存ダイアログの選択・名前を付けて保存は選択）
        var written =
            _saveTarget
            ?? throw new InvalidOperationException("保存の手でないのに上書き保存の口を通った");
        var bytes = File.ReadAllBytes(PathOf(written));
        var label = $"vm-save@{_step}.{++_saveCount}";
        Register(label, bytes);
        VmWrites.Add((written, Disk[written], label, _confirmLog.Count));
        Disk[written] = label;
        LastIo = label;
        Race(DocHook.AfterSaveWrite);
    }

    private void OnAfterLoadRead()
    {
        var file = _openTarget ?? FileOf(Vm.CurrentFilePath);
        _openTarget = null;
        LastIo = file is { } read ? Disk[read] : null;
        Race(DocHook.AfterLoadRead);
    }

    private void Race(DocHook hook)
    {
        if (_raceDone || _current.Race is not { } race || race.Hook != hook)
        {
            return;
        }

        _raceDone = true;
        WriteExternal(race.Write);
    }

    private void OnConfirm() => _confirmLog.Add(new DocConfirm(false, Dialogs.ConfirmMessages[^1]));

    private void OnConfirmWarning()
    {
        _confirmLog.Add(new DocConfirm(true, Dialogs.WarningConfirmMessages[^1]));
        _confirmDepth++;
        MaxConfirmDepth = Math.Max(MaxConfirmDepth, _confirmDepth);

        try
        {
            if (!_duringConfirmDone && _current.DuringConfirm.Count > 0)
            {
                _duringConfirmDone = true;

                foreach (var nested in _current.DuringConfirm)
                {
                    ApplyOp(nested);
                }
            }
        }
        finally
        {
            _confirmDepth--;
        }
    }

    /// <summary>手の後の観測（自動保存の図が変わっていれば版として控える）</summary>
    public DocObservation Observe(int generationBefore)
    {
        if (File.Exists(AutoSavePath))
        {
            Register($"vm-autosave@{_step}", File.ReadAllBytes(AutoSavePath));
        }

        return new DocObservation
        {
            Harness = this,
            Vm = Vm,
            Dialogs = Dialogs,
            Path = Vm.CurrentFilePath,
            PathFile = FileOf(Vm.CurrentFilePath),
            IsDirty = Vm.IsDirty,
            Title = Vm.WindowTitle,
            Tables = Vm.Entities.Select(e => e.TableName).ToList(),
            Queries = Vm.Queries.Count,
            Status = Vm.StatusMessage,
            Notified = Vm.StatusGenerationForTests != generationBefore,
            CanUndo = Vm.UndoRedo.CanUndo,
            Confirms = [.. _confirmLog],
            Errors = [.. Dialogs.ErrorMessages],
            Informations = [.. Dialogs.InformationMessages],
            DragActive = DragBehavior.IsInteractionActiveForTests,
            MaxConfirmDepth = MaxConfirmDepth,
            FitRequested = _fitRequested,
            SaveDialogs = Files.SaveDialogCallCount,
            LastKnownHash = DocumentFileProbe.LastKnownHash(Vm),
            IgnoredHash = DocumentFileProbe.IgnoredHash(Vm),
            DiskHashes = new Dictionary<DocFile, string?>
            {
                [DocFile.A] = DocumentContentHash.TryCompute(PathOf(DocFile.A)),
                [DocFile.B] = DocumentContentHash.TryCompute(PathOf(DocFile.B)),
                [DocFile.Unwritable] = DocumentContentHash.TryCompute(PathOf(DocFile.Unwritable)),
            },
            AutoSaveHash = DocumentContentHash.TryCompute(AutoSavePath),
            Meta = new GuiAppSettingsStore(_temp.Path).Load().CurrentDocument,
        };
    }

    public void Dispose()
    {
        try
        {
            Vm.Dispose();
        }
        catch
        {
            // 後始末の失敗はテストの結果に影響させない
        }

        DragBehavior.CancelActiveDrag();
        RubberBandBehavior.CancelActiveSelection();
        _temp.Dispose();
    }
}
