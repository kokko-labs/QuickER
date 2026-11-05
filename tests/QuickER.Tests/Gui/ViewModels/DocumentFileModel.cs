using QuickER.Documents;
using QuickER.Resources;
using QuickER.Services;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>ディスクに現れた版（ハーネスと参照モデルが同じ名前で呼ぶ）</summary>
/// <param name="Content">中身の種類</param>
/// <param name="Tables">図として読めたときの表の名前の並び</param>
/// <param name="Queries">図として読めたときの名前付きクエリの数</param>
internal sealed record DocVersion(DocContentKind Content, IReadOnlyList<string> Tables, int Queries)
{
    /// <summary>JsonStorageService.TryLoad が図として受け入れるか</summary>
    public bool Loads =>
        Content
            is DocContentKind.Diagram
                or DocContentKind.DiagramWithBom
                or DocContentKind.Newer
                or DocContentKind.NullCollections;

    /// <summary>将来版として読めるか（開く前の確認・自動再読込の拒否）</summary>
    public bool IsNewerFormat => Content == DocContentKind.Newer;

    /// <summary>JsonStorageService.IsNewerFormatFile（上書き前の将来版の確認）が真か</summary>
    public bool IsNewerFormatFile =>
        Content is DocContentKind.Newer or DocContentKind.UnreadableVersion;

    /// <summary>開けなかったときの文言の 1 行目の書式（開く）</summary>
    public string OpenFailureFormat =>
        Content switch
        {
            DocContentKind.DuplicateId => Strings.Open_DuplicateId,
            DocContentKind.MissingRequiredText => Strings.Open_MissingRequiredText,
            _ => Strings.Open_Failed,
        };
}

/// <summary>自動保存の文書メタ（gui-settings.json の CurrentDocument）</summary>
/// <param name="Path">紐づくファイル</param>
/// <param name="LastKnown">最終既知の版</param>
/// <param name="IsDirty">未保存か</param>
/// <param name="AutoSave">一緒に書いた自動保存の図の版（旧版のメタは null）</param>
/// <param name="PathUpper">パスを大文字の綴りで持っているか</param>
internal sealed record DocMeta(
    DocFile? Path,
    string? LastKnown,
    bool IsDirty,
    string? AutoSave,
    bool PathUpper = false
);

/// <summary>確認ダイアログの 1 回（警告か通常か・本文）</summary>
internal sealed record DocConfirm(bool Warning, string Message);

/// <summary>
/// 文書の保存・読込・外部変更・自動保存の参照モデル（製品の規則を CLAUDE.md と docs から独立に書き直したもの）
/// </summary>
/// <remarks>
/// <para>
/// 版は名前（ラベル）で持つ。同じバイト列かどうかはハーネスが知っているので、比較は <see cref="_same"/> を通す
/// （VM が同じ内容を 2 回書くと別の名前の同じバイト列になる）。VM が書いた版の名前は「vm-save@手.回」「vm-autosave@手」で、
/// ハーネスが製品の口で付けた名前と同じ規則で付ける。
/// </para>
/// <para>
/// 1 手は VM で先に流し、その後でモデルを流して突き合わせる（同じバイト列の判定に VM が書いた版が要るため）。
/// モデルは製品を呼ばず、ハーネスの状態も変えない。
/// </para>
/// </remarks>
internal sealed class DocumentFileModel
{
    private readonly Func<string?, string?, bool> _same;
    private readonly Func<DocFile, string> _pathOf;

    /// <summary>モデルを作る</summary>
    /// <param name="same">2 つの版が同じバイト列か（どちらかが null なら両方 null のときだけ真）</param>
    /// <param name="pathOf">ファイルの実パス（文言の書式に入れる）</param>
    public DocumentFileModel(Func<string?, string?, bool> same, Func<DocFile, string> pathOf)
    {
        _same = same;
        _pathOf = pathOf;
    }

    // ---------------- 持ち越す状態 ----------------

    /// <summary>紐づくファイル（無題は null）</summary>
    public DocFile? Path { get; private set; }

    /// <summary>
    /// パスを大文字の綴りで持っているか（名前を付けて保存で大文字だけ違う同じパスを選んだ後）。
    /// 製品は CurrentFilePath を文字列で持つので、同じファイルでも綴りが変われば「別の文書へ切り替わった」として
    /// 「続行」の抑止を捨てる
    /// </summary>
    public bool PathUpper { get; private set; }

    /// <summary>未保存か</summary>
    public bool Dirty { get; private set; }

    /// <summary>Undo の履歴があるか</summary>
    public bool CanUndo => _history.Count > 0;

    /// <summary>Undo の履歴（編集の前の表の並び。新しいものが末尾）</summary>
    private readonly List<List<string>> _history = [];

    /// <summary>進行中のドラッグがあるか</summary>
    public bool DragActive { get; private set; }

    /// <summary>メモリの図の表の名前の並び</summary>
    public List<string> Tables { get; private set; } = [];

    /// <summary>メモリの図の名前付きクエリの数</summary>
    public int Queries { get; private set; }

    /// <summary>最終既知の版</summary>
    public string? LastKnown { get; private set; }

    /// <summary>「続行」で無視した版</summary>
    public string? Ignored { get; private set; }

    /// <summary>ファイルごとのディスクの版（無ければ null）</summary>
    public Dictionary<DocFile, string?> Disk { get; } =
        new()
        {
            [DocFile.A] = null,
            [DocFile.B] = null,
            [DocFile.Unwritable] = null,
        };

    /// <summary>自動保存の図（last_diagram.json）の版</summary>
    public string? AutoSaveFile { get; private set; }

    /// <summary>保存済みの文書メタ（一度も書いていなければ null）</summary>
    public DocMeta? Meta { get; private set; }

    /// <summary>ステータスバーの文言</summary>
    public string Status { get; private set; } = Strings.Status_Ready;

    /// <summary>版の中身（名前 → 中身）</summary>
    public Dictionary<string, DocVersion> Versions { get; } = new(StringComparer.Ordinal);

    // ---------------- 1 手の出力 ----------------

    /// <summary>この手で出た確認（順に）</summary>
    public List<DocConfirm> Confirms { get; } = [];

    /// <summary>この手で出たエラーの 1 行目（順に）</summary>
    public List<string> Errors { get; } = [];

    /// <summary>この手で出た完了の通知（モーダル・順に）</summary>
    public List<string> Informations { get; } = [];

    /// <summary>この手でステータスバーへ通知したか</summary>
    public bool Notified { get; private set; }

    /// <summary>この手で fit-to-window を求めたか</summary>
    public bool FitRequested { get; private set; }

    /// <summary>この手で保存ダイアログを開いた回数</summary>
    public int SaveDialogs { get; private set; }

    /// <summary>この手で進行中のドラッグを打ち切ったか（打ち切る経路を通ったか）</summary>
    public bool CanvasInteractionsCancelled { get; private set; }

    /// <summary>この手で VM が書いた版（順に）</summary>
    public List<(DocFile File, string Label)> Writes { get; } = [];

    // ---------------- 1 手の文脈 ----------------

    private int _step;
    private int _saveCount;
    private DocStep _current = new(new NewOp());
    private bool _raceDone;
    private bool _duringConfirmDone;
    private bool _handling;
    private bool _pending;

    /// <summary>外部の書き込みの版の名前と中身を控える（ハーネスが書いたのと同じ版）</summary>
    public void RegisterExternal(ExternalWriteOp write) =>
        Versions[write.Label] = ExternalVersion(write);

    /// <summary>外部の書き込みの中身（モデルが読む形）</summary>
    public static DocVersion ExternalVersion(ExternalWriteOp write)
    {
        if (write.Build is { } build)
        {
            var schema = build().Schema;
            return new(
                write.Content,
                schema.Entities.Select(e => e.TableName).ToList(),
                schema.Queries.Count
            );
        }

        return new(write.Content, write.Table is null ? [] : [write.Table], 0);
    }

    /// <summary>1 手を流す（出力は Confirms などに残る）</summary>
    /// <param name="index">手の番号（VM が書いた版の名前に使う）</param>
    /// <param name="step">手</param>
    public void Apply(int index, DocStep step)
    {
        _step = index;
        _saveCount = 0;
        _current = step;
        _raceDone = false;
        _duringConfirmDone = false;
        Confirms.Clear();
        Errors.Clear();
        Informations.Clear();
        Writes.Clear();
        Notified = false;
        FitRequested = false;
        SaveDialogs = 0;
        CanvasInteractionsCancelled = false;

        ApplyOp(step.Op);
    }

    private void ApplyOp(DocOp op)
    {
        switch (op)
        {
            case NewOp:
                New();
                break;
            case OpenOp open:
                Open(open.File);
                break;
            case SaveOp save:
                Save(save.DialogTarget);
                break;
            case SaveAsOp saveAs:
                SaveDialogs++;
                SaveToPath(saveAs.Target, saveAs.UpperCase && saveAs.Target == DocFile.A);
                break;
            case AddEntityOp:
                Edited();
                Tables.Add("NewTable");
                break;
            case RenameOp rename:
                if (rename.Index < Tables.Count)
                {
                    Edited();
                    Tables[rename.Index] = rename.Name;
                }

                break;
            case UndoOp:
                if (_history.Count > 0)
                {
                    Tables = _history[^1];
                    _history.RemoveAt(_history.Count - 1);
                    Dirty = true;
                }

                break;
            case SetPathOp setPath:
                SetPath(setPath.File);
                break;
            case SetWidthOp:
                break;
            case AutoFitWidthsOp autoFit:
                if (autoFit.Changes)
                {
                    Dirty = true;
                }

                break;
            case DragBeginOp drag:
                if (drag.Index < Tables.Count)
                {
                    DragActive = true;
                }

                break;
            case ImportMermaidOp import:
                ImportMermaid(import.Tables);
                break;
            case ExternalAutoSaveWriteOp autoSaveWrite:
                var autoSchema = autoSaveWrite.Build().Schema;
                Versions[autoSaveWrite.Label] = new DocVersion(
                    autoSaveWrite.Content,
                    autoSchema.Entities.Select(e => e.TableName).ToList(),
                    autoSchema.Queries.Count
                );
                AutoSaveFile = autoSaveWrite.Label;
                break;
            case ReplaceQueriesOp queries:
                Queries = queries.Count;
                Dirty = true;
                AutoSave(metaLost: false);
                break;
            case ReplaceFromModuleOp replace:
                Tables = [.. replace.Tables];
                Queries = 0;
                Dirty = true;
                _history.Clear();
                FitRequested = true;
                break;
            case ExternalWriteOp write:
                RegisterExternal(write);
                Disk[write.File] = write.Label;
                break;
            case ExternalRestoreOp restore:
                Disk[restore.File] = restore.Label;
                break;
            case ExternalDeleteOp delete:
                Disk[delete.File] = null;
                break;
            case DeliverOp deliver:
                Deliver(deliver);
                break;
            case AutoSaveOp autoSave:
                AutoSave(autoSave.MetaLost);
                break;
            case StripMetaHashOp:
                Meta = (Meta ?? new DocMeta(null, null, false, null)) with { AutoSave = null };
                break;
            case RestartOp:
                Restart();
                break;
            case ExportToCurrentOp:
                if (Path is not null)
                {
                    SaveDialogs++;
                    Errors.Add(Strings.Export_TargetIsCurrentDocument);
                }

                break;
            default:
                throw new InvalidOperationException($"モデルが知らない手: {op}");
        }
    }

    /// <summary>追跡する編集の前に、編集の前の表の並びを履歴へ積む</summary>
    private void Edited()
    {
        _history.Add([.. Tables]);
        Dirty = true;
    }

    /// <summary>Mermaid の取込（失うものがあれば置換の確認・丸ごと置換・完了のモーダル）</summary>
    private void ImportMermaid(IReadOnlyList<string> tables)
    {
        if (!HasNothingToLose)
        {
            var message = string.Format(Strings.Import_ReplaceConfirm, Strings.Format_Mermaid);

            if (Queries > 0)
            {
                message +=
                    Environment.NewLine
                    + Environment.NewLine
                    + string.Format(Strings.Import_QueriesRemovedWarning, Queries);
            }

            if (!Confirm(Dirty, message))
            {
                return;
            }
        }

        Tables = [.. tables];
        Queries = 0;
        Dirty = true;
        _history.Clear();
        FitRequested = true;
        Informations.Add(string.Format(Strings.Import_Completed, Strings.Format_Mermaid));
    }

    private void Notify(string message)
    {
        Status = message;
        Notified = true;
    }

    /// <summary>確認を出す（警告の確認の最初の 1 回では、最中に流す手を流す）</summary>
    private bool Confirm(bool warning, string message)
    {
        Confirms.Add(new DocConfirm(warning, message));

        if (warning && !_duringConfirmDone && _current.DuringConfirm.Count > 0)
        {
            _duringConfirmDone = true;

            foreach (var nested in _current.DuringConfirm)
            {
                ApplyOp(nested);
            }
        }

        return _current.Answer;
    }

    private bool HasNothingToLose => Tables.Count == 0 && Queries == 0 && !Dirty;

    private void Race(DocHook hook)
    {
        if (_raceDone || _current.Race is not { } race || race.Hook != hook)
        {
            return;
        }

        _raceDone = true;
        ApplyOp(race.Write);
    }

    private void New()
    {
        if (!HasNothingToLose && !Confirm(Dirty, Strings.Confirm_ClearDiagram))
        {
            return;
        }

        LoadInto([], 0, fit: true);
        SetPath(null);
        LastKnown = null;
        Dirty = false;
    }

    private void SetPath(DocFile? path, bool upper = false)
    {
        if (Path != path || PathUpper != upper)
        {
            // 別の文書へ切り替わったら「続行」の抑止を捨てる（OnCurrentFilePathChanged）
            Ignored = null;
        }

        Path = path;
        PathUpper = upper;
    }

    private void LoadInto(IReadOnlyList<string> tables, int queries, bool fit)
    {
        Tables = [.. tables];
        Queries = queries;
        _history.Clear();

        if (fit)
        {
            FitRequested = true;
        }
    }

    private void UpdateIdentity(DocFile path, string label, bool upper = false)
    {
        SetPath(path, upper);
        LastKnown = label;
        Ignored = null;
        Dirty = false;
    }

    private void Open(DocFile file)
    {
        if (!HasNothingToLose && !Confirm(Dirty, Strings.Confirm_OpenDiagram))
        {
            return;
        }

        var label = Disk[file];
        var version = label is null ? null : Versions[label];

        if (version is null || !version.Loads)
        {
            var format = version?.OpenFailureFormat ?? Strings.Open_Failed;
            Errors.Add(string.Format(format, _pathOf(file)));
            return;
        }

        if (
            version.IsNewerFormat
            && !Confirm(
                true,
                string.Format(
                    Strings.Confirm_NewerDocumentFormat,
                    DiagramDocument.CurrentVersion + 1,
                    DiagramDocument.CurrentVersion
                )
            )
        )
        {
            return;
        }

        LoadInto(version.Tables, version.Queries, fit: true);
        Race(DocHook.AfterLoadRead);
        UpdateIdentity(file, label!);
        Notify(Strings.Status_Opened);
        CheckAgainstDisk();
    }

    private void Save(DocFile? dialogTarget)
    {
        if (Path is { } path)
        {
            SaveToPath(path, PathUpper);
            return;
        }

        SaveDialogs++;

        if (dialogTarget is { } target)
        {
            SaveToPath(target);
        }
    }

    private void SaveToPath(DocFile target, bool upper = false)
    {
        if (!ConfirmOverwrite(target))
        {
            return;
        }

        if (target == DocFile.Unwritable)
        {
            Errors.Add(Strings.Save_Failed);
            return;
        }

        var label = $"vm-save@{_step}.{++_saveCount}";
        Versions[label] = new DocVersion(DocContentKind.Diagram, [.. Tables], Queries);
        Disk[target] = label;
        Writes.Add((target, label));
        Race(DocHook.AfterSaveWrite);
        UpdateIdentity(target, label, upper);
        Notify(Strings.Status_Saved);
        CheckAgainstDisk();
    }

    private bool ConfirmOverwrite(DocFile target)
    {
        if (Disk[target] is not { } onDisk)
        {
            return true;
        }

        if (Versions[onDisk].IsNewerFormatFile)
        {
            return Confirm(true, Strings.Confirm_OverwriteNewerFormat);
        }

        if (target != Path || _same(onDisk, LastKnown) || _same(onDisk, Ignored))
        {
            return true;
        }

        return Confirm(true, Strings.Confirm_OverwriteExternalChange);
    }

    private void Deliver(DeliverOp deliver)
    {
        if (Path is null || deliver.OtherPath)
        {
            return;
        }

        switch (deliver.Change)
        {
            case DocumentFileChangeKind.Deleted:
                Notify(Strings.Status_ExternalFileDeleted);
                break;
            case DocumentFileChangeKind.Renamed:
                Notify(Strings.Status_ExternalFileRenamed);
                break;
            case DocumentFileChangeKind.WatchInterrupted:
                Notify(Strings.Status_ExternalWatchInterrupted);
                break;
            case DocumentFileChangeKind.WatchResumed:
                Notify(Strings.Status_ExternalWatchResumed);
                break;
            case DocumentFileChangeKind.Modified:
                var hash = deliver.Hash switch
                {
                    DocEventHash.CurrentDisk => Disk[Path.Value],
                    DocEventHash.Unknown => DocumentFileHarness.UnknownHashLabel,
                    _ => null,
                };
                HandleExternal(hash);
                break;
        }
    }

    private void HandleExternal(string? hash)
    {
        if (hash is not null && _same(hash, LastKnown))
        {
            return;
        }

        if (_handling)
        {
            _pending = true;
            return;
        }

        _handling = true;

        try
        {
            HandleExternalCore(hash);
        }
        finally
        {
            _handling = false;
        }

        if (_pending)
        {
            _pending = false;
            CheckAgainstDisk();
        }
    }

    private void HandleExternalCore(string? hash)
    {
        CanvasInteractionsCancelled = true;
        DragActive = false;

        if (!Dirty)
        {
            if (Reload())
            {
                Notify(Strings.Status_ExternalReloaded);
            }

            return;
        }

        if (hash is not null && _same(hash, Ignored))
        {
            return;
        }

        if (Confirm(true, Strings.Confirm_ExternalChangeReload))
        {
            Reload();
        }
        else
        {
            Ignored = hash;
        }
    }

    private bool Reload()
    {
        if (Path is not { } path)
        {
            return false;
        }

        var label = Disk[path];
        var version = label is null ? null : Versions[label];

        if (version is null || !version.Loads || version.IsNewerFormat)
        {
            Notify(Strings.Status_ExternalReloadFailed);
            return false;
        }

        LoadInto(version.Tables, version.Queries, fit: false);
        Race(DocHook.AfterLoadRead);
        UpdateIdentity(path, label!, PathUpper);
        return true;
    }

    private void CheckAgainstDisk()
    {
        if (Path is not { } path || Disk[path] is not { } onDisk || _same(onDisk, LastKnown))
        {
            return;
        }

        HandleExternal(onDisk);
    }

    private void AutoSave(bool metaLost)
    {
        var label = $"vm-autosave@{_step}";
        Versions[label] = new DocVersion(DocContentKind.Diagram, [.. Tables], Queries);
        AutoSaveFile = label;

        if (!metaLost)
        {
            Meta = new DocMeta(Path, LastKnown, Dirty, label, PathUpper);
        }
    }

    private void Restart()
    {
        // 新しい VM：空の図・無題・クリーン・通知なし
        Status = Strings.Status_Ready;
        Tables = [];
        Queries = 0;
        _history.Clear();
        DragActive = false;
        Path = null;
        PathUpper = false;
        LastKnown = null;
        Ignored = null;
        Dirty = false;
        _handling = false;
        _pending = false;

        if (AutoSaveFile is { } autoSaved && Versions[autoSaved].Loads)
        {
            var version = Versions[autoSaved];
            LoadInto(version.Tables, version.Queries, fit: true);
            var meta = Meta ?? new DocMeta(null, null, false, null);

            if (meta.AutoSave is not null && !_same(meta.AutoSave, autoSaved))
            {
                Dirty = true;
                Notify(Strings.Status_AutoSaveRestoredUntitled);
            }
            else
            {
                Path = meta.Path;
                PathUpper = meta.PathUpper;
                LastKnown = meta.LastKnown;
                Dirty = meta.IsDirty;
            }
        }

        CheckAgainstDisk();
    }
}
