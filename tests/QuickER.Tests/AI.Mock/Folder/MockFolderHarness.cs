using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Platform;
using QuickER.Tests.TestSupport;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Folder;

/// <summary>
/// モックフォルダの保存単位の表の 1 行の組（行ごとの一時フォルダ・ストア・セッション・偽のエンジン）と、
/// どの手の後にも掛ける不変条件の見張り
/// </summary>
/// <remarks>
/// <para>
/// 行はストアとセッションの手をこの組のメソッドで打つ。組は手の前後のディスクとメモリを比べ、違反を記録する
/// （行の本体の後に <see cref="AssertInvariants"/> がまとめて表明する。手が投げた例外はそのまま投げ直す）。
/// </para>
/// <para>不変条件（モデルとは独立に判定する。出典は src/QuickER.AI.Mock の行）：</para>
/// <list type="number">
/// <item>
/// 失敗した手（例外・失敗の結果・失敗として転送されたターン）の後は、<c>mock.json</c> のバイト列とメモリのマニフェストが手の前と同じ
/// （出典：MockFolderStore.cs:598-603 の Commit が書いてから差し替える・:286/:337-338・:414-419・:451-453、
/// MockFolderDesignSession.cs:142-162・:350-356）。
/// メモリは公開の <c>Manifest</c>（次の確定まで同じ複製を使い回す。MockFolderStore.cs:57）で見るので、失敗した変更がメモリへ漏れたかは、
/// 失敗の後に成功する手を続けて確定させた後で確かめる（失敗の行は次の保存まで流す）。
/// </item>
/// <item>
/// メモリのマニフェストは <c>mock.json</c> と一緒にしか変わらない：ストアが <c>mock.json</c> を書いてからは、どの手の後も
/// メモリのマニフェストと <c>Open</c> し直したマニフェストが同じ（出典：MockFolderStore.cs:598-603・:667-673）。
/// <c>mock.json</c>・画面の HTML・<c>style.css</c> は BOM なし（出典：MockFolderStore.cs:23）。
/// </item>
/// <item>
/// どの手の後も、フォルダに原子的な書き込みの一時ファイルが残らない（出典：src/QuickER.Platform/AtomicFile.cs の Write の finally・CreateText）。
/// </item>
/// <item>
/// 宣言と実体の整合：宣言した画面には HTML があり、HTML には宣言がある。許すのは失敗で残る一時の食い違い 2 つだけ＝
/// (a) <c>SaveScreen</c> が <c>mock.json</c> で失敗して残る「宣言の無い HTML」（出典：MockFolderStore.cs:332-338）、
/// (b) <c>RemoveScreen</c> が <c>mock.json</c> で失敗して残る「HTML の無い宣言」（出典：MockFolderStore.cs:375-383・:398）。
/// どちらもその画面への次の保存・削除の成功で解ける。手で書いた <c>mock.json</c> を開いた行（<see cref="WriteManifestByHand"/>）には掛けない。
/// </item>
/// <item>
/// <c>style.css</c> の内容は最後に成功した <c>SaveStylesheet</c> の内容（無ければファイルが無い）。許すのは失敗で残る一時の食い違い 1 つだけ＝
/// (c) <c>SaveStylesheet</c> が <c>mock.json</c> で失敗して残る「新しい CSS」（出典：MockFolderStore.cs:405-419＝CSS を先に書き、
/// マニフェストは差し替えない。src/QuickER.AI.Mock/CLAUDE.md「保存単位とフォルダ」の「ファイル → mock.json の順」と合う）。
/// </item>
/// <item>
/// <c>SourceSchema</c> は、直接の <c>UpdateSourceSchema</c> の成功か、初回ターン（会話の開始）が成功で終わったときにだけ変わり、
/// 変わる値は開始のときに控えたスキーマ（新規はスナップショットが空のときだけ控える）
/// （出典：MockFolderDesignSession.cs:142-162・:199・:238・:260・:266-270）。
/// </item>
/// <item>エンジンのターンの完了 1 回につき、セッションの <c>TurnCompleted</c> がちょうど 1 回（出典：MockFolderDesignSession.cs:130・:142-162）。</item>
/// <item>
/// 保存の通知（<c>ScreenSaved</c>・<c>ScreenRemoved</c>・<c>StylesheetSaved</c>）は、そのツールが成功を返したときだけ、ツールの実行の中で
/// ちょうど 1 回出る（get_screen・知らないツール・失敗の結果では出ない）（出典：MockFolderDesignSession.cs:399・:428・:441・:387-391・:350-356）。
/// </item>
/// </list>
/// </remarks>
internal sealed class MockFolderHarness : IDisposable
{
    /// <summary>固定時刻（改訂のタイムスタンプを決定的にする）</summary>
    public static readonly DateTimeOffset FixedNow = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

    private readonly TestTempFolder _temp = new();
    private readonly List<string> _violations = [];

    /// <summary>許した「宣言の無い HTML」（不変条件 4 (a)）</summary>
    private readonly HashSet<string> _orphanHtmlAllowed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>許した「HTML の無い宣言」（不変条件 4 (b)）</summary>
    private readonly HashSet<string> _missingHtmlAllowed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>許した「新しい CSS」（不変条件 5 (c)）</summary>
    private readonly HashSet<string> _cssAllowed = new(StringComparer.Ordinal);

    /// <summary>最後に成功した SaveStylesheet の内容（無ければ null）</summary>
    private string? _expectedCss;

    /// <summary>ストアが mock.json を書いてからか（不変条件 2）</summary>
    private bool _manifestWrittenByStore;

    /// <summary>手で書いた mock.json を開いたか（不変条件 4・5 を掛けない）</summary>
    private bool _handEdited;

    /// <summary>最後に見張った SourceSchema（不変条件 6）</summary>
    private string? _lastSchema;

    /// <summary>初回ターンが開いているか（開始から最初の完了まで）と、そのとき控えたスキーマ（不変条件 6）</summary>
    private bool _initialOpen;
    private string? _initialPending;

    /// <summary>手の中で失敗として転送されたターンがあったか</summary>
    private bool _failedTurnInStep;

    /// <summary>ツールの実行の中か（不変条件 8）</summary>
    private bool _inExecute;

    private int _engineCompletions;

    /// <summary>モックフォルダ（一時フォルダの下の mock。作るのは CreateNew）</summary>
    public string Folder => Path.Combine(_temp.Path, "mock");

    /// <summary>mock.json のパス</summary>
    public string ManifestPath => Path.Combine(Folder, MockManifest.ManifestFileName);

    /// <summary>ストア（CreateNew・OpenStore の後）</summary>
    public MockFolderStore Store =>
        _store ?? throw new InvalidOperationException("ストアを作っていない");

    private MockFolderStore? _store;

    /// <summary>セッション（CreateSession の後）</summary>
    public MockFolderDesignSession Session =>
        _session ?? throw new InvalidOperationException("セッションを作っていない");

    private MockFolderDesignSession? _session;

    /// <summary>偽のエンジン（CreateSession の後）</summary>
    public FakeChatEngine Engine =>
        _engine ?? throw new InvalidOperationException("セッションを作っていない");

    private FakeChatEngine? _engine;

    /// <summary>セッションが転送したターンの完了</summary>
    public List<ErChatTurnResult> ForwardedTurns { get; } = [];

    /// <summary>セッションが転送した応答の断片</summary>
    public List<string> ForwardedDeltas { get; } = [];

    /// <summary>ScreenSaved の通知</summary>
    public List<MockScreenSavedEventArgs> ScreenSavedEvents { get; } = [];

    /// <summary>ScreenRemoved の通知</summary>
    public List<string> ScreenRemovedEvents { get; } = [];

    /// <summary>StylesheetSaved の通知</summary>
    public List<MockStylesheetSavedEventArgs> StylesheetSavedEvents { get; } = [];

    /// <summary>style.css を参照する完全な HTML を組み立てる</summary>
    public static string ScreenHtml(string body, bool withStylesheet = true)
    {
        var link = withStylesheet ? "<link rel=\"stylesheet\" href=\"style.css\">" : string.Empty;

        return $"<!DOCTYPE html><html><head>{link}</head><body>{body}</body></html>";
    }

    /// <summary>save_screen の引数 JSON を組み立てる（省略した項目は書かない）</summary>
    public static string SaveScreenArgs(
        string file,
        string name,
        string html,
        string? description = null,
        object[]? transitions = null,
        string? revisionNote = null,
        object[]? entities = null
    )
    {
        var payload = new Dictionary<string, object?>
        {
            ["file"] = file,
            ["name"] = name,
            ["html"] = html,
        };

        if (description is not null)
        {
            payload["description"] = description;
        }

        if (transitions is not null)
        {
            payload["transitions"] = transitions;
        }

        if (revisionNote is not null)
        {
            payload["revision_note"] = revisionNote;
        }

        if (entities is not null)
        {
            payload["entities"] = entities;
        }

        return JsonSerializer.Serialize(payload);
    }

    // --- ストアの手 ------------------------------------------------------

    /// <summary>モックフォルダを新しく作る（<paramref name="beforeManifestWrite"/> は確認と書き込みの間に差し込む処理）</summary>
    public MockFolderStore CreateNew(
        string title = "受注管理",
        string schema = "# schema",
        Action? beforeManifestWrite = null
    )
    {
        var before = Take();
        Action? injected = beforeManifestWrite is null
            ? null
            : () =>
            {
                beforeManifestWrite();
                // 別のプロセスが書いた後の状態を手の前とする（ストア自身の書き込みだけを見張る）
                before = Take();
            };

        return Step(
            "CreateNew",
            () =>
                MockFolderStore.CreateNew(
                    Folder,
                    title,
                    schema,
                    () => FixedNow,
                    beforeManifestWrite: injected
                ),
            store =>
            {
                _store = store;
                _lastSchema = store.Manifest.SourceSchema;
            },
            null,
            () => before
        );
    }

    /// <summary>手で書いた mock.json を置く（宣言と実体の整合・CSS の見張りを外す）</summary>
    public void WriteManifestByHand(string json)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(ManifestPath, json);
        _handEdited = true;
        _manifestWrittenByStore = false;
    }

    /// <summary>モックフォルダを開いて、以後の手のストアにする（失敗したら前のストアのまま。例外は投げ直す）</summary>
    /// <param name="holdExclusively">手の間だけ排他で開いておくファイル（無ければ null）</param>
    public MockFolderStore OpenStore(string? holdExclusively = null) =>
        Step(
            "OpenStore",
            () => MockFolderStore.Open(Folder, () => FixedNow),
            store =>
            {
                _store = store;
                _lastSchema = store.Manifest.SourceSchema;
            },
            null,
            holdExclusively: holdExclusively
        );

    /// <summary>画面の HTML を読む（読み取りの手。失敗しても mock.json もメモリも変わらないことを見張る）</summary>
    /// <param name="file">画面のファイル名</param>
    /// <param name="holdExclusively">手の間だけ排他で開いておくファイル（無ければ null）</param>
    public string? ReadScreen(string file, string? holdExclusively = null) =>
        Step(
            "GetScreenHtml",
            () => Store.GetScreenHtml(file),
            _ => { },
            null,
            holdExclusively: holdExclusively
        );

    /// <summary>モックフォルダを開き直したマニフェスト（ストアは替えない）</summary>
    public MockManifest Reopen() => MockFolderStore.Open(Folder, () => FixedNow).Manifest;

    /// <summary>画面を保存する</summary>
    public IReadOnlyList<string> SaveScreen(
        string file,
        string name,
        string description,
        string html,
        IReadOnlyList<MockTransition> transitions,
        string revisionNote,
        IReadOnlyList<MockScreenEntity>? entities = null
    ) =>
        Step(
            $"SaveScreen({file})",
            () =>
                Store.SaveScreen(
                    file,
                    name,
                    description,
                    html,
                    transitions,
                    revisionNote,
                    entities
                ),
            _ => ResolveScreen(file),
            ex => AllowOrphanOnIoFailure(ex, file)
        );

    /// <summary>画面を削除する</summary>
    public void RemoveScreen(string file) =>
        Step(
            $"RemoveScreen({file})",
            () =>
            {
                Store.RemoveScreen(file);
                return true;
            },
            _ => ResolveScreen(file),
            ex =>
            {
                if (IsIoFailure(ex))
                {
                    _missingHtmlAllowed.Add(file);
                }
            }
        );

    /// <summary>共有 CSS を保存する</summary>
    public IReadOnlyList<string> SaveStylesheet(string css, string revisionNote) =>
        Step(
            "SaveStylesheet",
            () => Store.SaveStylesheet(css, revisionNote),
            _ =>
            {
                _expectedCss = css;
                _cssAllowed.Clear();
            },
            ex =>
            {
                if (IsIoFailure(ex))
                {
                    _cssAllowed.Add(css);
                }
            }
        );

    /// <summary>スキーマのスナップショットを直接更新する</summary>
    public void UpdateSourceSchema(string schema) =>
        Step(
            "UpdateSourceSchema",
            () =>
            {
                Store.UpdateSourceSchema(schema);
                return true;
            },
            _ => _lastSchema = Store.Manifest.SourceSchema,
            null
        );

    /// <summary>mock.json を読み取り専用にして、マニフェストの書き込みを失敗させる（破棄で戻す）</summary>
    public IDisposable ManifestReadOnly()
    {
        File.SetAttributes(
            ManifestPath,
            File.GetAttributes(ManifestPath) | FileAttributes.ReadOnly
        );
        return new ReadOnlyRelease(ManifestPath);
    }

    private sealed class ReadOnlyRelease(string path) : IDisposable
    {
        public void Dispose() =>
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
    }

    // --- セッションの手 ----------------------------------------------------

    /// <summary>セッションを作る（偽のエンジン・通知の記録・完了の数え上げを付ける）</summary>
    public MockFolderDesignSession CreateSession()
    {
        var session = new MockFolderDesignSession(
            toolHost => _engine = new FakeChatEngine(toolHost),
            Store
        );
        _session = session;

        // セッションはコンストラクタでエンジンを購読済み。ここで足す購読はセッションの後に呼ばれる
        _engine!.TurnCompleted += (_, _) => _engineCompletions++;
        session.TurnCompleted += (_, result) => OnForwardedTurn(result);
        session.AssistantDeltaReceived += (_, delta) => ForwardedDeltas.Add(delta);
        session.ScreenSaved += (_, e) =>
        {
            RequireInExecute("ScreenSaved");
            ScreenSavedEvents.Add(e);
        };
        session.ScreenRemoved += (_, file) =>
        {
            RequireInExecute("ScreenRemoved");
            ScreenRemovedEvents.Add(file);
        };
        session.StylesheetSaved += (_, e) =>
        {
            RequireInExecute("StylesheetSaved");
            StylesheetSavedEvents.Add(e);
        };
        return session;
    }

    /// <summary>新規の会話を始める</summary>
    public Task StartNewAsync(ErDiagram diagram, string? instructions = null)
    {
        var pending = string.IsNullOrWhiteSpace(Store.Manifest.SourceSchema)
            ? MockSchemaSerializer.Serialize(diagram)
            : null;
        return StartAsync(
            "StartNew",
            pending,
            () =>
                Session.StartNewAsync(
                    diagram,
                    instructions,
                    null,
                    TestContext.Current.CancellationToken
                )
        );
    }

    /// <summary>再開の会話を始める</summary>
    public Task StartResumeAsync(ErDiagram diagram) =>
        StartAsync(
            "StartResume",
            MockSchemaSerializer.Serialize(diagram),
            () =>
                Session.StartResumeAsync(diagram, null, null, TestContext.Current.CancellationToken)
        );

    private async Task StartAsync(string name, string? pending, Func<Task> start)
    {
        var before = Take();
        _failedTurnInStep = false;
        _initialOpen = true;
        _initialPending = pending;

        try
        {
            await start();
        }
        catch
        {
            _initialOpen = false;
            CheckAfter(name, before, failed: true);
            throw;
        }

        CheckAfter(name, before, failed: _failedTurnInStep);
    }

    /// <summary>修正の指示を送る</summary>
    public async Task SendFeedbackAsync(string feedback)
    {
        var before = Take();
        _failedTurnInStep = false;

        try
        {
            await Session.SendFeedbackAsync(
                feedback,
                cancellationToken: TestContext.Current.CancellationToken
            );
        }
        catch
        {
            CheckAfter("SendFeedback", before, failed: true);
            throw;
        }

        CheckAfter("SendFeedback", before, failed: _failedTurnInStep);
    }

    /// <summary>保留したターンを成功で完了させる</summary>
    public void CompleteHeldTurn() => TurnStep("CompleteHeldTurn", () => Engine.CompleteHeldTurn());

    /// <summary>保留したターンを失敗（null なら中断）で完了させる</summary>
    public void FailHeldTurn(string? error) =>
        TurnStep("FailHeldTurn", () => Engine.FailHeldTurn(error));

    private void TurnStep(string name, Action act)
    {
        var before = Take();
        _failedTurnInStep = false;
        act();
        CheckAfter(name, before, failed: _failedTurnInStep);
    }

    /// <summary>ツールを実行する（通知の数を見張る）</summary>
    public (string Result, bool Success) Execute(string toolName, string argumentsJson)
    {
        var before = Take();
        var saved = ScreenSavedEvents.Count;
        var removed = ScreenRemovedEvents.Count;
        var stylesheets = StylesheetSavedEvents.Count;
        var arguments = ReadArguments(argumentsJson);
        (string Result, bool Success) outcome;
        _inExecute = true;

        try
        {
            outcome = Session.Execute(toolName, argumentsJson);
        }
        finally
        {
            _inExecute = false;
        }

        var raised = (
            Saved: ScreenSavedEvents.Count - saved,
            Removed: ScreenRemovedEvents.Count - removed,
            Stylesheet: StylesheetSavedEvents.Count - stylesheets
        );
        var expected = !outcome.Success
            ? (0, 0, 0)
            : toolName switch
            {
                MockFolderDesignTools.SaveScreenToolName => (1, 0, 0),
                MockFolderDesignTools.RemoveScreenToolName => (0, 1, 0),
                MockFolderDesignTools.SaveStylesheetToolName => (0, 0, 1),
                _ => (0, 0, 0),
            };

        if (raised != expected)
        {
            _violations.Add(
                $"不変条件 8：{toolName}（成功 {outcome.Success}）の通知が {raised}（期待 {expected}）"
            );
        }

        if (raised.Saved == 1 && ScreenSavedEvents[^1].File != arguments.File)
        {
            _violations.Add(
                $"不変条件 8：ScreenSaved の画面 {ScreenSavedEvents[^1].File} が引数 {arguments.File} と違う"
            );
        }

        if (raised.Removed == 1 && ScreenRemovedEvents[^1] != arguments.File)
        {
            _violations.Add(
                $"不変条件 8：ScreenRemoved の画面 {ScreenRemovedEvents[^1]} が引数 {arguments.File} と違う"
            );
        }

        if (outcome.Success)
        {
            switch (toolName)
            {
                case MockFolderDesignTools.SaveScreenToolName:
                    ResolveScreen(arguments.File);
                    break;
                case MockFolderDesignTools.RemoveScreenToolName:
                    ResolveScreen(arguments.File);
                    break;
                case MockFolderDesignTools.SaveStylesheetToolName:
                    _expectedCss = arguments.Css;
                    _cssAllowed.Clear();
                    break;
            }
        }
        else if (IsFileAccessFailure(outcome.Result))
        {
            switch (toolName)
            {
                case MockFolderDesignTools.SaveScreenToolName when !IsDeclared(arguments.File):
                    _orphanHtmlAllowed.Add(arguments.File);
                    break;
                case MockFolderDesignTools.RemoveScreenToolName:
                    _missingHtmlAllowed.Add(arguments.File);
                    break;
                case MockFolderDesignTools.SaveStylesheetToolName:
                    _cssAllowed.Add(arguments.Css);
                    break;
            }
        }

        CheckAfter($"Execute({toolName})", before, failed: !outcome.Success);
        return outcome;
    }

    // --- 不変条件 --------------------------------------------------------

    /// <summary>行の本体の後に、見張った不変条件をまとめて表明する</summary>
    public void AssertInvariants() => _violations.Should().BeEmpty();

    public void Dispose()
    {
        // 読み取り専用の属性を戻してから消す（戻さないと削除が断られてフォルダが残る）
        if (Directory.Exists(_temp.Path))
        {
            foreach (
                var path in Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories)
            )
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
        }

        _temp.Dispose();
    }

    /// <summary>手の前の状態</summary>
    private sealed record Snapshot(byte[]? ManifestBytes, string? ManifestJson);

    private Snapshot Take() =>
        new(
            File.Exists(ManifestPath) ? File.ReadAllBytes(ManifestPath) : null,
            _store is null ? null : Serialize(_store.Manifest)
        );

    private static string Serialize(MockManifest manifest) =>
        JsonSerializer.Serialize(manifest, MockManifest.SerializerOptions);

    /// <summary>ストアの手を 1 つ打ち、手の後の不変条件を見る（例外は投げ直す）</summary>
    /// <param name="name">手の名前（違反の文に使う）</param>
    /// <param name="act">手</param>
    /// <param name="onSuccess">成功したときにモデルへ反映する処理</param>
    /// <param name="onFailure">失敗したときにモデルへ反映する処理（無ければ null）</param>
    /// <param name="before">
    /// 手の前の状態の取り出し（null なら手の直前に取った状態。確認と書き込みの間に差し込む処理が取り直すときに渡す）
    /// </param>
    /// <param name="holdExclusively">
    /// 手の間だけ共有なし（<see cref="FileShare.None"/>）で開いておくファイル（別のプロセスが掴んだ形。無ければ null）。
    /// 手の前の状態を取ってから開き、手が成功しても失敗しても見張りの前に finally で閉じる（見張りは mock.json を読むため）
    /// </param>
    private T Step<T>(
        string name,
        Func<T> act,
        Action<T> onSuccess,
        Action<Exception>? onFailure,
        Func<Snapshot>? before = null,
        string? holdExclusively = null
    )
    {
        var taken = Take();
        var getBefore = before ?? (() => taken);
        T result;

        try
        {
            using var held = holdExclusively is null
                ? null
                : new FileStream(holdExclusively, FileMode.Open, FileAccess.Read, FileShare.None);
            result = act();
        }
        catch (Exception ex)
        {
            onFailure?.Invoke(ex);
            CheckAfter(name, getBefore(), failed: true);
            throw;
        }

        onSuccess(result);
        CheckAfter(name, getBefore(), failed: false);
        return result;
    }

    private void CheckAfter(string name, Snapshot before, bool failed)
    {
        var after = Take();
        var bytesChanged = !SameBytes(before.ManifestBytes, after.ManifestBytes);

        // 不変条件 1：失敗した手は mock.json もメモリも変えない
        if (failed)
        {
            if (bytesChanged)
            {
                _violations.Add($"不変条件 1：失敗した {name} が mock.json を変えた");
            }

            if (before.ManifestJson is not null && before.ManifestJson != after.ManifestJson)
            {
                _violations.Add($"不変条件 1：失敗した {name} がメモリのマニフェストを変えた");
            }
        }
        else if (bytesChanged && _store is not null)
        {
            _manifestWrittenByStore = true;
        }

        // 不変条件 2：ストアが書いてからは、メモリと開き直しが一致し、BOM が無い
        if (_manifestWrittenByStore && _store is not null)
        {
            var reopened = Serialize(MockFolderStore.Open(Folder, () => FixedNow).Manifest);

            if (reopened != after.ManifestJson)
            {
                _violations.Add(
                    $"不変条件 2：{name} の後のメモリのマニフェストが mock.json と違う"
                );
            }

            if (HasBom(after.ManifestBytes))
            {
                _violations.Add($"不変条件 2：{name} の後の mock.json に BOM がある");
            }
        }

        if (!Directory.Exists(Folder))
        {
            return;
        }

        // 不変条件 3：一時ファイルが残らない
        foreach (var path in Directory.EnumerateFiles(Folder))
        {
            if (AtomicFile.IsTemporaryFileName(Path.GetFileName(path)))
            {
                _violations.Add(
                    $"不変条件 3：{name} の後に一時ファイル {Path.GetFileName(path)} が残った"
                );
            }
        }

        if (_handEdited || _store is null)
        {
            return;
        }

        // 不変条件 4：宣言と実体の整合（許した一時の食い違いだけを除く）
        var declared = _store
            .Manifest.Screens.Select(screen => screen.File)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var htmlFiles = Directory
            .EnumerateFiles(Folder, "*.html")
            .Select(path => Path.GetFileName(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in declared.Where(file => !htmlFiles.Contains(file)))
        {
            if (!_missingHtmlAllowed.Contains(file))
            {
                _violations.Add($"不変条件 4：{name} の後、宣言した {file} に HTML が無い");
            }
        }

        foreach (var file in htmlFiles.Where(file => !declared.Contains(file)))
        {
            if (!_orphanHtmlAllowed.Contains(file))
            {
                _violations.Add($"不変条件 4：{name} の後、HTML {file} に宣言が無い");
            }
        }

        foreach (var file in htmlFiles)
        {
            if (HasBom(File.ReadAllBytes(Path.Combine(Folder, file))))
            {
                _violations.Add($"不変条件 2：{name} の後の {file} に BOM がある");
            }
        }

        // 不変条件 5：style.css は最後に成功した保存の内容（許した新しい CSS だけを除く）
        var cssPath = Path.Combine(Folder, MockManifest.StylesheetFileName);
        var css = File.Exists(cssPath) ? File.ReadAllText(cssPath) : null;

        if (css != _expectedCss && (css is null || !_cssAllowed.Contains(css)))
        {
            _violations.Add(
                $"不変条件 5：{name} の後の style.css が {css ?? "(無し)"}（期待 {_expectedCss ?? "(無し)"}）"
            );
        }

        if (css is not null && HasBom(File.ReadAllBytes(cssPath)))
        {
            _violations.Add($"不変条件 2：{name} の後の style.css に BOM がある");
        }

        // 不変条件 6：SourceSchema は見張った経路の外で変わらない
        if (_store.Manifest.SourceSchema != _lastSchema)
        {
            _violations.Add($"不変条件 6：{name} が SourceSchema を初回ターンの成功の外で変えた");
            _lastSchema = _store.Manifest.SourceSchema;
        }

        // 不変条件 7：エンジンの完了とセッションの転送が同じ数
        if (_session is not null && _engineCompletions != ForwardedTurns.Count)
        {
            _violations.Add(
                $"不変条件 7：{name} の後、エンジンの完了 {_engineCompletions} 回に対して転送 {ForwardedTurns.Count} 回"
            );
        }
    }

    private void OnForwardedTurn(ErChatTurnResult result)
    {
        ForwardedTurns.Add(result);

        if (!result.Success)
        {
            _failedTurnInStep = true;
        }

        // 不変条件 6：変わってよいのは、開いている初回ターンの成功で、控えたスキーマへだけ
        var current = Store.Manifest.SourceSchema;

        if (
            current != _lastSchema
            && !(_initialOpen && result.Success && current == _initialPending)
        )
        {
            _violations.Add(
                $"不変条件 6：ターンの完了（成功 {result.Success}・初回 {_initialOpen}）で SourceSchema が変わった"
            );
        }

        _lastSchema = current;
        _initialOpen = false;
    }

    private void RequireInExecute(string name)
    {
        if (!_inExecute)
        {
            _violations.Add($"不変条件 8：{name} がツールの実行の外で出た");
        }
    }

    private bool IsDeclared(string file) =>
        _store is not null
        && _store.Manifest.Screens.Any(screen =>
            string.Equals(screen.File, file, StringComparison.OrdinalIgnoreCase)
        );

    private void ResolveScreen(string file)
    {
        _orphanHtmlAllowed.Remove(file);
        _missingHtmlAllowed.Remove(file);
    }

    private void AllowOrphanOnIoFailure(Exception ex, string file)
    {
        if (IsIoFailure(ex) && !IsDeclared(file))
        {
            _orphanHtmlAllowed.Add(file);
        }
    }

    private static bool IsIoFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    /// <summary>ツールの失敗の結果がフォルダの読み書きの失敗か（文言は resx の書式の前半で見る＝UI の言語に依らない）</summary>
    private static bool IsFileAccessFailure(string result) =>
        result.StartsWith(
            MockStrings.Mock_ToolFileAccessFailedResult.Split("{0}")[0],
            StringComparison.Ordinal
        );

    private static (string File, string Css) ReadArguments(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return (string.Empty, string.Empty);
            }

            string Read(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? string.Empty
                    : string.Empty;

            return (Read("file"), Read("css"));
        }
        catch (JsonException)
        {
            return (string.Empty, string.Empty);
        }
    }

    private static bool SameBytes(byte[]? a, byte[]? b) =>
        a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);

    private static bool HasBom(byte[]? bytes) =>
        bytes is { Length: >= 3 } && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
}
