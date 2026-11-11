using QuickER.Documents;
using QuickER.Services;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>文書の状態機械のエンジンが扱うファイル（ハーネスが一時フォルダの中の実パスへ写す）</summary>
internal enum DocFile
{
    /// <summary>主に開く文書（Doc.json）</summary>
    A,

    /// <summary>別の文書（Other.json）</summary>
    B,

    /// <summary>親フォルダの無い保存先（書き込みが必ず失敗する）</summary>
    Unwritable,
}

/// <summary>外部が書くファイルの中身の種類（モデルが読めるか・将来版かを決める）</summary>
internal enum DocContentKind
{
    /// <summary>現行の版の図（表 1 つ・配置なし）</summary>
    Diagram,

    /// <summary>現行の版の図（表 3 つ・全部の表が配置を持つ。<see cref="DocumentFileHarness.LayoutDocument"/>）</summary>
    DiagramWithLayout,

    /// <summary>
    /// 現行の版の図（表 4 つ・前の 2 つだけが配置を持ち、後の 2 つは配置が欠ける。<see cref="DocumentFileHarness.LayoutDocument"/>）
    /// </summary>
    DiagramPartialLayout,

    /// <summary>BOM 付きの現行の版の図（表 1 つ）</summary>
    DiagramWithBom,

    /// <summary>将来版（Version＝現行＋1）の図（表 1 つ）</summary>
    Newer,

    /// <summary>版を名乗るが解釈できない（"Version":"2"）</summary>
    UnreadableVersion,

    /// <summary>JSON として壊れている</summary>
    Corrupt,

    /// <summary>図でない JSON</summary>
    NotDiagram,

    /// <summary>エンティティ Id が重複する図</summary>
    DuplicateId,

    /// <summary>テーブル名が明示 null の図</summary>
    MissingRequiredText,

    /// <summary>コレクションが明示 null の図（空の図として読める）</summary>
    NullCollections,

    /// <summary>Schema が明示 null の図（図でないとして断られる）</summary>
    NullSchema,

    /// <summary>リレーションの親の表が図に無い Id を指す図（参照で断られる。乱数の手の列は作らず、決まった形の行だけが使う）</summary>
    BrokenReference,
}

/// <summary>割り込みの書き込みを差し込む製品の口</summary>
internal enum DocHook
{
    /// <summary>上書き保存の書き込みの直後（<c>AfterSaveWriteForTests</c>）</summary>
    AfterSaveWrite,

    /// <summary>開く・再読込の読み取りの直後（<c>AfterLoadReadForTests</c>）</summary>
    AfterLoadRead,
}

/// <summary>1 手の間だけ文書のファイルに掛けるアクセスの条件の種類</summary>
internal enum DocAccessKind
{
    /// <summary>ハーネスが FileShare.None で開いたまま（読めない・書けない）</summary>
    ShareNone,

    /// <summary>ハーネスが FileShare.Read で開いたまま（読める・書けない）</summary>
    ShareRead,

    /// <summary>読み取り専用の属性（読める・書けない）</summary>
    ReadOnly,
}

/// <summary>
/// 1 手の間だけ文書のファイルに掛けるアクセスの条件（ハーネスが手の前に掛け、観測の前に外す。手の前にファイルが無ければ掛けない）
/// </summary>
/// <param name="File">条件を掛けるファイル</param>
/// <param name="Kind">条件の種類</param>
internal sealed record DocAccess(DocFile File, DocAccessKind Kind)
{
    /// <summary>条件の下でファイルを読めるか（どの条件でも書けない）</summary>
    public bool Readable => Kind != DocAccessKind.ShareNone;

    /// <summary>報告と次元表の水準の名前</summary>
    public string KindName =>
        Kind switch
        {
            DocAccessKind.ShareNone => "ShareNone",
            DocAccessKind.ShareRead => "ShareRead",
            _ => "ReadOnly",
        };

    /// <summary>報告用の 1 語</summary>
    public override string ToString() => $"{KindName}:{File}";
}

/// <summary>監視イベントに載せる内容ハッシュの決め方</summary>
internal enum DocEventHash
{
    /// <summary>配送の時点のディスクのハッシュ（監視器と同じ。ファイルが無ければ null）</summary>
    CurrentDisk,

    /// <summary>ハッシュなし（Deleted・Renamed・監視の中断と再開）</summary>
    None,

    /// <summary>どの版とも一致しない固定の値（"deadbeef"）</summary>
    Unknown,
}

/// <summary>文書の状態機械の 1 手</summary>
internal abstract record DocOp
{
    /// <summary>手の種類の名前（次元表の水準・報告に使う）</summary>
    public abstract string Kind { get; }
}

/// <summary>新規作成</summary>
internal sealed record NewOp : DocOp
{
    public override string Kind => "New";
}

/// <summary>開く（開くダイアログで <paramref name="File"/> を選ぶ）</summary>
internal sealed record OpenOp(DocFile File) : DocOp
{
    public override string Kind => "Open";
}

/// <summary>上書き保存（無題なら保存ダイアログで <paramref name="DialogTarget"/> を選ぶ。null は取り消し）</summary>
internal sealed record SaveOp(DocFile? DialogTarget = null) : DocOp
{
    public override string Kind => "Save";
}

/// <summary>名前を付けて保存（<paramref name="UpperCase"/> は大文字だけ違う同じパスを選ぶ）</summary>
internal sealed record SaveAsOp(DocFile Target, bool UpperCase = false) : DocOp
{
    public override string Kind => "SaveAs";
}

/// <summary>表を 1 つ足す（AddEntityCommand・表名は NewTable）</summary>
internal sealed record AddEntityOp : DocOp
{
    public override string Kind => "Edit";
}

/// <summary>表の名前を変える（追跡する編集。範囲外の番号は何もしない）</summary>
internal sealed record RenameOp(int Index, string Name) : DocOp
{
    public override string Kind => "Edit";
}

/// <summary>名前付きクエリを差し替える（Undo の外の変更。製品は自動保存も行う）</summary>
/// <param name="Count">クエリの数</param>
/// <param name="Orphan">
/// true ならどの表にも属さないクエリにする（false は先頭の表に属する。表を消すとその表のクエリも消えるのは図の編集の規則で、
/// 文書の状態機械の外なので、乱数の手の列は属さないクエリを使う）
/// </param>
internal sealed record ReplaceQueriesOp(int Count, bool Orphan = false) : DocOp
{
    public override string Kind => "EditUntracked";
}

/// <summary>機能モジュールから図を丸ごと置き換える（パスは維持）</summary>
internal sealed record ReplaceFromModuleOp(IReadOnlyList<string> Tables) : DocOp
{
    public override string Kind => "Replace";

    public override string ToString() =>
        $"ReplaceFromModuleOp {{ Tables = [{string.Join(", ", Tables)}] }}";
}

/// <summary>外部のプロセスがファイルを書く</summary>
/// <param name="File">書くファイル</param>
/// <param name="Content">中身の種類</param>
/// <param name="Table">図の表の名前（図の種類のとき）</param>
/// <param name="Label">版の名前（シナリオの中で一意）</param>
/// <param name="RawText">そのまま書く文字列（null なら種類から作る）</param>
/// <param name="Build">書く図を組む（null でなければ JsonStorageService.SaveAtomic で書く。表と名前付きクエリの数はここから読む）</param>
internal sealed record ExternalWriteOp(
    DocFile File,
    DocContentKind Content,
    string? Table,
    string Label,
    string? RawText = null,
    Func<DiagramDocument>? Build = null
) : DocOp
{
    public override string Kind => "ExternalWrite";
}

/// <summary>外部のプロセスが自動保存の図（last_diagram.json）を書く（手で壊した作業状態）</summary>
internal sealed record ExternalAutoSaveWriteOp(
    DocContentKind Content,
    string Label,
    Func<DiagramDocument> Build
) : DocOp
{
    public override string Kind => "ExternalWrite";
}

/// <summary>現在のファイルのパスを直接設定する（CurrentFilePath の setter。最終既知ハッシュは触らない）</summary>
internal sealed record SetPathOp(DocFile File) : DocOp
{
    public override string Kind => "SetPath";
}

/// <summary>Undo（履歴の 1 件を戻す）</summary>
internal sealed record UndoOp : DocOp
{
    public override string Kind => "Undo";
}

/// <summary>表の幅を直接書き換える（Undo の外・未保存にしない）</summary>
internal sealed record SetWidthOp(int Index, double Width) : DocOp
{
    public override string Kind => "EditUntracked";
}

/// <summary>幅の自動調整（<paramref name="Changes"/> はシナリオが宣言する「幅が変わるか」。モデルはこれで未保存を決める）</summary>
internal sealed record AutoFitWidthsOp(bool Changes) : DocOp
{
    public override string Kind => "EditUntracked";
}

/// <summary>表のドラッグを始めて動かす（DragBehavior.BeginInteractionForTests の後に座標を足す。座標は履歴に無い。範囲外の番号は何もしない）</summary>
internal sealed record DragBeginOp(int Index, double DeltaX, double DeltaY) : DocOp
{
    public override string Kind => "DragBegin";
}

/// <summary>Mermaid を取り込む（図を丸ごと置き換える・パスは維持）</summary>
/// <param name="Tables">Mermaid の表の名前（各表は int の主キー列 1 つ）</param>
internal sealed record ImportMermaidOp(IReadOnlyList<string> Tables) : DocOp
{
    public override string Kind => "ImportMermaid";

    public override string ToString() =>
        $"ImportMermaidOp {{ Tables = [{string.Join(", ", Tables)}] }}";
}

/// <summary>外部のプロセスが、前に現れた版と同じバイト列を書き戻す</summary>
internal sealed record ExternalRestoreOp(DocFile File, string Label) : DocOp
{
    public override string Kind => "ExternalRestore";
}

/// <summary>外部のプロセスがファイルを消す</summary>
internal sealed record ExternalDeleteOp(DocFile File) : DocOp
{
    public override string Kind => "ExternalDelete";
}

/// <summary>監視イベントを VM へ配送する（<c>RaiseExternalChangeForTests</c>）</summary>
/// <param name="Change">変更の種類</param>
/// <param name="Hash">載せる内容ハッシュ</param>
/// <param name="OtherPath">true ならどの文書のパスでもない別のパス（Stray.json）宛ての遅れたイベントにする</param>
/// <param name="CaseVariant">
/// true なら開いているファイルのパスを大文字小文字だけ変えた綴りで宛てる（Windows の監視は綴りの違うパスで通知することがある。
/// 製品は大文字小文字を無視して同じファイル宛てとして扱う）
/// </param>
internal sealed record DeliverOp(
    DocumentFileChangeKind Change,
    DocEventHash Hash,
    bool OtherPath = false,
    bool CaseVariant = false
) : DocOp
{
    public override string Kind => $"Deliver{Change}";
}

/// <summary>自動保存（<paramref name="MetaLost"/> は gui-settings.json を排他で開いたまま＝メタだけ書けない）</summary>
internal sealed record AutoSaveOp(bool MetaLost = false) : DocOp
{
    public override string Kind => MetaLost ? "AutoSaveMetaLost" : "AutoSave";
}

/// <summary>保存済みのメタから AutoSaveHash を消す（ハッシュを持たない旧版のメタ）</summary>
internal sealed record StripMetaHashOp : DocOp
{
    public override string Kind => "StripMetaHash";
}

/// <summary>アプリを再起動する（VM を作り直して Initialize＝自動保存の復元と起動時の照合）</summary>
internal sealed record RestartOp : DocOp
{
    public override string Kind => "Restart";
}

/// <summary>開いている図のファイル（大文字にしたパス）へ JSON で書き出す（無題は何もしない）</summary>
internal sealed record ExportToCurrentOp : DocOp
{
    public override string Kind => "ExportToCurrent";
}

/// <summary>割り込みの書き込み（手の中で 1 回だけ、最初に口を通ったときに書く）</summary>
internal sealed record DocRace(DocHook Hook, ExternalWriteOp Write);

/// <summary>文書の状態機械の 1 手と、その手の確認の応答・割り込み・期待</summary>
/// <param name="Op">手</param>
internal sealed record DocStep(DocOp Op)
{
    /// <summary>この手で出る確認すべてへの応答（StubDialogService.ConfirmResult）</summary>
    public bool Answer { get; init; } = true;

    /// <summary>割り込みの書き込み（無ければ null）</summary>
    public DocRace? Race { get; init; }

    /// <summary>この手の最初の警告の確認の最中に流す手（外部の書き込み・監視イベント。1 回だけ）</summary>
    public IReadOnlyList<DocOp> DuringConfirm { get; init; } = [];

    /// <summary>
    /// この手の間だけ文書のファイルに掛けるアクセスの条件（無ければ null）。割り込みの書き込み・確認の最中の手とは併せない
    /// （ハーネスの外部の書き込みが条件に断られるため）
    /// </summary>
    public DocAccess? Access { get; init; }

    /// <summary>元のテストの表明（モデルとの照合と不変条件に加えて当てる）</summary>
    public IReadOnlyList<Action<DocObservation>> Expectations { get; init; } = [];

    /// <summary>報告用の 1 行</summary>
    public override string ToString()
    {
        var text = Op.ToString();

        if (!Answer)
        {
            text += " answer=no";
        }

        if (Race is not null)
        {
            text += $" race={Race.Hook}:{Race.Write.Label}";
        }

        if (DuringConfirm.Count > 0)
        {
            text += $" during-confirm=[{string.Join(", ", DuringConfirm)}]";
        }

        if (Access is not null)
        {
            text += $" access={Access}";
        }

        return text;
    }
}

/// <summary>手を組み立てる短い書き方（シナリオと生成器が使う）</summary>
internal static class Doc
{
    public static DocStep New() => new(new NewOp());

    public static DocStep Open(DocFile file = DocFile.A) => new(new OpenOp(file));

    public static DocStep Save(DocFile? dialogTarget = null) => new(new SaveOp(dialogTarget));

    public static DocStep SaveAs(DocFile target, bool upperCase = false) =>
        new(new SaveAsOp(target, upperCase));

    public static DocStep Add() => new(new AddEntityOp());

    public static DocStep Rename(int index, string name) => new(new RenameOp(index, name));

    public static DocStep ReplaceQueries(int count, bool orphan = false) =>
        new(new ReplaceQueriesOp(count, orphan));

    public static DocStep ReplaceFromModule(params string[] tables) =>
        new(new ReplaceFromModuleOp(tables));

    public static ExternalWriteOp WriteOp(
        DocFile file,
        string table,
        string? label = null,
        DocContentKind content = DocContentKind.Diagram
    ) => new(file, content, table, label ?? table);

    public static DocStep Write(
        DocFile file,
        string table,
        string? label = null,
        DocContentKind content = DocContentKind.Diagram
    ) => new(WriteOp(file, table, label, content));

    public static DocStep WriteRaw(
        DocFile file,
        DocContentKind content,
        string label,
        string text
    ) => new(new ExternalWriteOp(file, content, null, label, text));

    public static DocStep WriteKind(DocFile file, DocContentKind content, string label) =>
        new(new ExternalWriteOp(file, content, null, label));

    public static DocStep Restore(DocFile file, string label) =>
        new(new ExternalRestoreOp(file, label));

    public static DocStep Delete(DocFile file = DocFile.A) => new(new ExternalDeleteOp(file));

    public static DocStep Modified(DocEventHash hash = DocEventHash.CurrentDisk) =>
        new(new DeliverOp(DocumentFileChangeKind.Modified, hash));

    public static DeliverOp ModifiedOp() =>
        new(DocumentFileChangeKind.Modified, DocEventHash.CurrentDisk);

    public static DocStep Event(DocumentFileChangeKind change) =>
        new(new DeliverOp(change, DocEventHash.None));

    public static DocStep AutoSave(bool metaLost = false) => new(new AutoSaveOp(metaLost));

    public static DocStep StripMetaHash() => new(new StripMetaHashOp());

    public static DocStep Restart() => new(new RestartOp());

    public static DocStep WriteDocument(DocFile file, string label, Func<DiagramDocument> build) =>
        new(new ExternalWriteOp(file, DocContentKind.Diagram, null, label, Build: build));

    public static DocStep WriteAutoSave(
        DocContentKind content,
        string label,
        Func<DiagramDocument> build
    ) => new(new ExternalAutoSaveWriteOp(content, label, build));

    public static DocStep SetPath(DocFile file) => new(new SetPathOp(file));

    public static DocStep Undo() => new(new UndoOp());

    public static DocStep SetWidth(int index, double width) => new(new SetWidthOp(index, width));

    public static DocStep AutoFitWidths(bool changes) => new(new AutoFitWidthsOp(changes));

    public static DocStep DragBegin(int index, double deltaX, double deltaY) =>
        new(new DragBeginOp(index, deltaX, deltaY));

    public static DocStep ImportMermaid(params string[] tables) => new(new ImportMermaidOp(tables));

    public static DocStep ExportToCurrent() => new(new ExportToCurrentOp());

    /// <summary>この手の確認に「いいえ」（取り消し・続行）で答える</summary>
    public static DocStep No(this DocStep step) => step with { Answer = false };

    /// <summary>この手に割り込みの書き込みを足す</summary>
    public static DocStep RaceAt(this DocStep step, DocHook hook, ExternalWriteOp write) =>
        step with
        {
            Race = new DocRace(hook, write),
        };

    /// <summary>この手の間だけファイルにアクセスの条件を掛ける</summary>
    public static DocStep WithAccess(this DocStep step, DocFile file, DocAccessKind kind) =>
        step with
        {
            Access = new DocAccess(file, kind),
        };

    /// <summary>この手の最初の警告の確認の最中に流す手を足す</summary>
    public static DocStep DuringFirstConfirm(this DocStep step, params DocOp[] ops) =>
        step with
        {
            DuringConfirm = ops,
        };

    /// <summary>元のテストの表明を足す</summary>
    public static DocStep Expect(this DocStep step, Action<DocObservation> expectation) =>
        step with
        {
            Expectations = [.. step.Expectations, expectation],
        };
}
