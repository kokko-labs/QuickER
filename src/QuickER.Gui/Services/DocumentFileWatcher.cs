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

    /// <summary>デバウンス幅を指定して監視サービスを生成する</summary>
    /// <param name="debounceMilliseconds">内容変更バーストの合流幅（既定 400ms）</param>
    public DocumentFileWatcher(int debounceMilliseconds = 400)
    {
        _debounceMilliseconds = debounceMilliseconds;
    }

    /// <summary>指定ファイルの監視を開始する（既存の監視は停止して張り替える）</summary>
    /// <param name="path">監視対象ファイルのフルパス</param>
    /// <remarks>親ディレクトリが存在しない場合は監視を張らない（再作成は将来のパス設定時に拾う）</remarks>
    public void Watch(string path)
    {
        lock (_gate)
        {
            StopCore();

            if (_disposed)
            {
                return;
            }

            var directory = System.IO.Path.GetDirectoryName(path);
            var fileName = System.IO.Path.GetFileName(path);

            // 親ディレクトリが無い（未作成のパス）ときは監視を張れない。呼び出し側が後で再設定する
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            {
                return;
            }

            if (!Directory.Exists(directory))
            {
                return;
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
                watcher.EnableRaisingEvents = true;

                _watcher = watcher;
                _path = path;
                _fileName = fileName;
            }
            catch
            {
                // 監視器の生成失敗（権限・パス不正など）は機能無効化にとどめ、主処理を妨げない
                StopCore();
            }
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
        if (_watcher is not null)
        {
            _watcher.Changed -= OnChangedOrCreated;
            _watcher.Created -= OnChangedOrCreated;
            _watcher.Deleted -= OnDeleted;
            _watcher.Renamed -= OnRenamed;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _path = null;
        _fileName = null;
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
