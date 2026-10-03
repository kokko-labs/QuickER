using System.IO;

namespace QuickER.Services;

/// <summary>外部変更検知が通知する変更の種別</summary>
public enum DocumentFileChangeKind
{
    /// <summary>内容が変更された（作成・上書き・内容一致でない書き込み）</summary>
    Modified,

    /// <summary>ファイルが削除された</summary>
    Deleted,

    /// <summary>ファイルが別名へリネームされた（監視対象名から外れた）</summary>
    Renamed,

    /// <summary>監視を張れなくなった（監視対象フォルダの消失・権限喪失など）。間隔をおいて張り直しを試み続ける</summary>
    WatchInterrupted,

    /// <summary><see cref="WatchInterrupted"/> の後、張り直しに成功して監視が再開した</summary>
    WatchResumed,
}

/// <summary>外部変更検知イベントの引数（種別・対象パス・内容ハッシュ）</summary>
public sealed class DocumentFileChangedEventArgs : EventArgs
{
    /// <summary>変更の種別</summary>
    public DocumentFileChangeKind Kind { get; }

    /// <summary>変更対象ファイルのフルパス（監視開始時のパス）</summary>
    public string Path { get; }

    /// <summary>内容の SHA-256（16 進）。<see cref="DocumentFileChangeKind.Modified"/> のときのみ非 null</summary>
    public string? ContentHash { get; }

    /// <summary>引数を組み立てる</summary>
    public DocumentFileChangedEventArgs(
        DocumentFileChangeKind kind,
        string path,
        string? contentHash
    )
    {
        Kind = kind;
        Path = path;
        ContentHash = contentHash;
    }
}

/// <summary>
/// 単一ファイルの外部変更を監視し、デバウンス・内容比較・種別分類を経て通知するサービス。
/// </summary>
/// <remarks>
/// <para>
/// WPF 非依存の testable なクラス。<see cref="FileChanged"/> はスレッドプール上で発火するため、
/// UI スレッドへのマーシャリングは購読側（ViewModel）の責務とする。
/// </para>
/// <list type="bullet">
/// <item>親ディレクトリ＋ファイル名フィルタで <see cref="FileSystemWatcher"/> を張る（ファイル単位監視）</item>
/// <item>内容変更イベントのバーストはデバウンスで 1 回に合流する（MCP は呼び出しごとに保存するため連続書き込みが普通）</item>
/// <item>発火時に内容ハッシュを算出し、<see cref="ExpectedHashProvider"/> と一致すれば通知しない
/// （自己書き込み・無内容変更の抑制）。読み取りは書き込み途中の共有違反に備え短くリトライする</item>
/// <item>削除・リネームは種別付きで即時通知する（デバウンスしない）</item>
/// <item>監視器自身のエラー（内部バッファ溢れ等）では監視を張り直し、停止中の変更を 1 回照合して拾う</item>
/// <item>監視を張れない（監視開始・張り直しの失敗）ときはパスを保持したまま間隔をおいて張り直しを試み続け
/// （<see cref="DocumentFileChangeKind.WatchInterrupted"/> / <see cref="DocumentFileChangeKind.WatchResumed"/> を
/// 1 回ずつ通知する）、張り直せたら止まっていた間の変更を 1 回照合して拾う</item>
/// </list>
/// </remarks>
public sealed class DocumentFileWatcher : IDisposable
{
    /// <summary>内容変更バーストを合流させるデバウンス幅（ミリ秒）</summary>
    private readonly int _debounceMilliseconds;

    /// <summary>状態（監視器・タイマ・対象パス）の一貫性を守るロック</summary>
    private readonly object _gate = new();

    /// <summary>現在の監視器（未監視のときは null）</summary>
    private FileSystemWatcher? _watcher;

    /// <summary>内容変更のデバウンス用シングルショットタイマ</summary>
    private Timer? _debounceTimer;

    /// <summary>張り直しの最初の待ち時間（ミリ秒）。失敗のたびに倍にする</summary>
    private readonly int _retryInitialMilliseconds;

    /// <summary>張り直しの待ち時間の上限（ミリ秒）。回数の上限は設けない</summary>
    private readonly int _retryMaxMilliseconds;

    /// <summary>張り直しのシングルショットタイマ（監視を張れていないときだけ存在する）</summary>
    private Timer? _retryTimer;

    /// <summary>次の張り直しまでの待ち時間（ミリ秒。0＝張り直しを予約していない）</summary>
    private int _retryDelayMilliseconds;

    /// <summary>
    /// 現在のパスについて <see cref="DocumentFileChangeKind.WatchInterrupted"/> を通知済みか
    /// （中断・再開の通知を 1 回ずつに留め、再試行のたびには通知しない）
    /// </summary>
    private bool _interruptionNotified;

    /// <summary>監視対象ファイルのフルパス（未監視のときは null）</summary>
    private string? _path;

    /// <summary>監視対象ファイル名（大文字小文字を無視した突合用）</summary>
    private string? _fileName;

    /// <summary>一時停止中は全イベントを無視する（自己書き込み時に購読側が立てる）</summary>
    private volatile bool _suspended;

    /// <summary>破棄済みフラグ（破棄後のイベント発火を防ぐ）</summary>
    private volatile bool _disposed;

    /// <summary>外部変更を通知するイベント（スレッドプール上で発火する）</summary>
    public event EventHandler<DocumentFileChangedEventArgs>? FileChanged;

    /// <summary>
    /// 「最終既知ハッシュ」を供給するデリゲート。内容変更の発火時にこの値と算出ハッシュが一致すれば
    /// 自己書き込み・無内容変更として通知を抑止する（購読側の最新状態を都度読むため関数で受ける）。
    /// </summary>
    public Func<string?>? ExpectedHashProvider { get; set; }

    /// <summary>現在の監視器（テストから監視状態・張り直しを観測するための内部シーム）</summary>
    internal FileSystemWatcher? CurrentWatcher
    {
        get
        {
            lock (_gate)
            {
                return _watcher;
            }
        }
    }

    /// <summary>デバウンス幅と張り直しの間隔を指定して監視サービスを生成する</summary>
    /// <param name="debounceMilliseconds">内容変更バーストの合流幅（既定 400ms）</param>
    /// <param name="retryInitialMilliseconds">監視を張れないときの最初の張り直しまでの待ち時間（既定 2 秒）</param>
    /// <param name="retryMaxMilliseconds">張り直しの待ち時間の上限（既定 30 秒）</param>
    public DocumentFileWatcher(
        int debounceMilliseconds = 400,
        int retryInitialMilliseconds = 2000,
        int retryMaxMilliseconds = 30000
    )
    {
        _debounceMilliseconds = debounceMilliseconds;
        _retryInitialMilliseconds = retryInitialMilliseconds;
        _retryMaxMilliseconds = retryMaxMilliseconds;
    }

    /// <summary>指定ファイルの監視を開始する（既存の監視は停止して張り替える）</summary>
    /// <param name="path">監視対象ファイルのフルパス</param>
    /// <remarks>
    /// 監視を張れない（親ディレクトリが無い・監視器の生成失敗）ときは、パスを保持したまま間隔をおいて
    /// 張り直しを試み続け、<see cref="DocumentFileChangeKind.WatchInterrupted"/> を通知する
    /// （ネットワーク共有・USB の一時的な切断から戻ったら監視を再開する）。
    /// </remarks>
    public void Watch(string path)
    {
        bool interrupted;

        lock (_gate)
        {
            StopCore();

            if (_disposed)
            {
                return;
            }

            interrupted = !WatchCore(path) && ScheduleRetry();
        }

        if (interrupted)
        {
            RaiseImmediate(DocumentFileChangeKind.WatchInterrupted);
        }
    }

    /// <summary>監視を張れなかったパスの張り直しを予約する（ロック内から呼ぶこと）</summary>
    /// <returns>
    /// このパスについて初めて中断を通知すべき場合 true（呼び出し側がロックの外で
    /// <see cref="DocumentFileChangeKind.WatchInterrupted"/> を通知する）
    /// </returns>
    private bool ScheduleRetry()
    {
        // ファイル名を取り出せないパスは何度試しても張れないので、予約も通知もしない
        if (_path is null)
        {
            return false;
        }

        if (_retryDelayMilliseconds == 0)
        {
            _retryDelayMilliseconds = _retryInitialMilliseconds;
        }

        _retryTimer?.Dispose();
        _retryTimer = new Timer(_ => RetryWatch(), null, _retryDelayMilliseconds, Timeout.Infinite);

        if (_interruptionNotified)
        {
            return false;
        }

        _interruptionNotified = true;
        return true;
    }

    /// <summary>予約した張り直しを試みる（タイマのスレッドから呼ばれる）</summary>
    /// <remarks>
    /// 失敗したら待ち時間を倍にして（上限は <see cref="_retryMaxMilliseconds"/>）再予約する。回数の上限は設けない
    /// （1 回あたり監視対象フォルダの存在確認程度の費用で、ネットワーク共有の復帰は数分かかり得る）。
    /// 中断の通知は最初の 1 回だけで、再試行のたびには通知しない。成功したら再開を 1 回だけ通知し、
    /// 止まっていた間に起きた変更を 1 回だけ照合して拾う。
    /// </remarks>
    internal void RetryWatch()
    {
        bool resumed;

        lock (_gate)
        {
            if (_disposed || _path is null || _watcher is not null)
            {
                return;
            }

            if (!WatchCore(_path))
            {
                _retryDelayMilliseconds = Math.Min(
                    _retryDelayMilliseconds * 2,
                    _retryMaxMilliseconds
                );
                _retryTimer?.Change(_retryDelayMilliseconds, Timeout.Infinite);
                return;
            }

            _retryTimer?.Dispose();
            _retryTimer = null;
            _retryDelayMilliseconds = 0;
            resumed = _interruptionNotified;
            _interruptionNotified = false;
        }

        if (resumed)
        {
            RaiseImmediate(DocumentFileChangeKind.WatchResumed);
        }

        OnDebounceElapsed();
    }

    /// <summary>テスト専用: 張り直しを予約中か（監視を張れていないときに試み続けていることの観測点）</summary>
    internal bool IsRetryScheduledForTests
    {
        get
        {
            lock (_gate)
            {
                return _retryTimer is not null;
            }
        }
    }

    /// <summary>監視器を生成して張る（ロック内から・既存の監視器は停止済みであること）</summary>
    /// <returns>張れた場合 true。張れなかった場合も、ファイル名を取り出せるパスなら対象パスは保持する</returns>
    private bool WatchCore(string path)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        var fileName = System.IO.Path.GetFileName(path);

        // ディレクトリ・ファイル名を取り出せないパスは監視の対象にならない
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        // 張れなくても対象パスは保持する（張り直しの予約と、そのパスへの通知に使う）
        _path = path;
        _fileName = fileName;

        if (!Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            var watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter =
                    NotifyFilters.LastWrite
                    | NotifyFilters.FileName
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
            };

            watcher.Changed += OnChangedOrCreated;
            watcher.Created += OnChangedOrCreated;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;

            _watcher = watcher;
            return true;
        }
        catch
        {
            // 監視器の生成失敗（権限・ボリュームの喪失など）は主処理を妨げず、張り直しの予約へ回す
            return false;
        }
    }

    /// <summary>監視を停止する（未監視のときは何もしない）</summary>
    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    /// <summary>イベント通知を一時停止する（自己書き込みの前に立てる）</summary>
    public void Suspend() => _suspended = true;

    /// <summary>イベント通知を再開する（自己書き込みの後に戻す）</summary>
    public void Resume() => _suspended = false;

    /// <summary>監視器・タイマを破棄して状態を初期化する（ロック内から呼ぶこと）</summary>
    private void StopCore()
    {
        StopWatcher();
        _retryTimer?.Dispose();
        _retryTimer = null;
        _retryDelayMilliseconds = 0;
        _interruptionNotified = false;
        _path = null;
        _fileName = null;
    }

    /// <summary>監視器とデバウンスタイマだけを破棄する（対象パス・張り直しの状態は残す。ロック内から呼ぶこと）</summary>
    private void StopWatcher()
    {
        if (_watcher is not null)
        {
            _watcher.Changed -= OnChangedOrCreated;
            _watcher.Created -= OnChangedOrCreated;
            _watcher.Deleted -= OnDeleted;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error -= OnWatcherError;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }

    /// <summary>内容変更・作成イベント。デバウンスへ流し込む</summary>
    private void OnChangedOrCreated(object sender, FileSystemEventArgs e)
    {
        if (_suspended || _disposed)
        {
            return;
        }

        ScheduleModified();
    }

    /// <summary>削除イベント。種別付きで即時通知し、保留中のデバウンスを打ち切る</summary>
    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        if (_suspended || _disposed)
        {
            return;
        }

        CancelPendingDebounce();
        RaiseImmediate(DocumentFileChangeKind.Deleted);
    }

    /// <summary>
    /// リネームイベント。監視対象名へ変わった（＝再作成相当）なら内容変更として扱い、
    /// 監視対象名から外れた（＝別名へ移動）ならリネームとして即時通知する。
    /// </summary>
    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (_suspended || _disposed)
        {
            return;
        }

        var targetName = _fileName;

        if (targetName is null)
        {
            return;
        }

        // 監視対象名へリネームされた（temp → 本名の原子的置換など）＝内容が現れた扱い
        if (string.Equals(e.Name, targetName, StringComparison.OrdinalIgnoreCase))
        {
            ScheduleModified();
            return;
        }

        // 監視対象名から別名へ外れた＝実質的に消えた
        if (string.Equals(e.OldName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            CancelPendingDebounce();
            RaiseImmediate(DocumentFileChangeKind.Renamed);
        }
    }

    /// <summary>
    /// 監視器自身のエラー（内部バッファ溢れ・監視対象ボリュームの一時的な喪失など）。監視を張り直す。
    /// </summary>
    /// <remarks>
    /// Error は「以後この監視器は通知を出さない」ことを意味するため、購読しないと外部変更検知が
    /// 無言のまま恒久的に止まる。張り直しは <see cref="FileSystemWatcher"/> 自身のイベントスレッドから
    /// 行うと停止待ちで固まり得るので、別スレッドへ逃がす。
    /// </remarks>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => RestartAfterError());
    }

    /// <summary>エラーで停止した監視を同じパス・同じ設定で張り直し、停止中に起きた変更を 1 回だけ拾う</summary>
    /// <remarks>
    /// 張り直しに失敗した場合（監視対象ディレクトリの消失・権限喪失など）は <see cref="Watch"/> と
    /// 同じくパスを保持したまま張り直しを予約し、中断を 1 回だけ通知する（主処理を妨げない）。
    /// 一時停止中でも張り直しは行う（張り直さないと <see cref="Resume"/> 後も監視が死んだままになる）。
    /// 停止中の変更の拾い上げは <see cref="OnDebounceElapsed"/> に委ね、通常の検知と同じ規則
    /// （一時停止の尊重・最終既知ハッシュとの比較）を通す。
    /// </remarks>
    internal void RestartAfterError()
    {
        bool restarted;
        var interrupted = false;

        // 現在パスの読み取りから張り直しまでをロック内で完結させる（この間に呼び出し側が
        // 別のパスへ張り替えていたら、その結果を古いパスで上書きしてしまうため）
        lock (_gate)
        {
            if (_disposed || _path is null)
            {
                return;
            }

            StopWatcher();
            restarted = WatchCore(_path);

            if (!restarted)
            {
                interrupted = ScheduleRetry();
            }
        }

        if (interrupted)
        {
            RaiseImmediate(DocumentFileChangeKind.WatchInterrupted);
        }

        if (!restarted)
        {
            return;
        }

        // 監視が止まっていた間に起きた変更は通知されていないため、張り直し直後に 1 回だけ照合する
        OnDebounceElapsed();
    }

    /// <summary>内容変更のデバウンスタイマを（再）起動する</summary>
    private void ScheduleModified()
    {
        lock (_gate)
        {
            if (_disposed || _watcher is null)
            {
                return;
            }

            if (_debounceTimer is null)
            {
                _debounceTimer = new Timer(
                    _ => OnDebounceElapsed(),
                    null,
                    _debounceMilliseconds,
                    Timeout.Infinite
                );
            }
            else
            {
                _debounceTimer.Change(_debounceMilliseconds, Timeout.Infinite);
            }
        }
    }

    /// <summary>保留中のデバウンスタイマを打ち切る（発火させない）</summary>
    private void CancelPendingDebounce()
    {
        lock (_gate)
        {
            _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>デバウンス満了。内容ハッシュを算出し、期待値と異なるときのみ内容変更を通知する</summary>
    private void OnDebounceElapsed()
    {
        string path;

        lock (_gate)
        {
            if (_disposed || _suspended || _path is null)
            {
                return;
            }

            path = _path;
        }

        // 書き込み途中の共有違反に備え短くリトライして算出する。消滅していれば null（削除側で通知済み）
        var hash = DocumentContentHash.TryComputeWithRetry(path);

        if (hash is null)
        {
            return;
        }

        // 自己書き込み・無内容変更（最終既知ハッシュと同一）は通知しない
        if (string.Equals(hash, ExpectedHashProvider?.Invoke(), StringComparison.Ordinal))
        {
            return;
        }

        RaiseChanged(new DocumentFileChangedEventArgs(DocumentFileChangeKind.Modified, path, hash));
    }

    /// <summary>削除・リネームを種別付きで即時通知する（ハッシュなし）</summary>
    private void RaiseImmediate(DocumentFileChangeKind kind)
    {
        string? path;

        lock (_gate)
        {
            path = _path;
        }

        if (path is null)
        {
            return;
        }

        RaiseChanged(new DocumentFileChangedEventArgs(kind, path, null));
    }

    /// <summary>破棄済みでなければイベントを発火する</summary>
    private void RaiseChanged(DocumentFileChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        FileChanged?.Invoke(this, args);
    }

    /// <summary>監視を停止して資源を解放する</summary>
    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
