using Velopack;
using Velopack.Sources;

namespace QuickER.Services;

/// <summary>
/// Velopack を用いた <see cref="IAppUpdater"/> の実装。
/// </summary>
/// <remarks>
/// フィード文字列が <c>https://github.com/</c> で始まる場合は <see cref="GithubSource"/> を、
/// それ以外（ローカルパス・単純 URL）は文字列コンストラクタで <see cref="UpdateManager"/> を構築する。
/// <see cref="CheckForUpdateAsync"/> で得た <see cref="UpdateInfo"/> を内部に保持し、
/// <see cref="DownloadAsync"/> / <see cref="ApplyAndRestart"/> で使い回す。
/// </remarks>
public sealed class VelopackAppUpdater : IAppUpdater
{
    /// <summary>更新の問い合わせ・ダウンロード・適用を行う Velopack のマネージャ</summary>
    private readonly UpdateManager _manager;

    /// <summary>直前の <see cref="CheckForUpdateAsync"/> で見つかった更新情報（未検出なら null）</summary>
    private UpdateInfo? _pendingUpdate;

    /// <summary>フィード文字列から更新マネージャを構築する</summary>
    /// <param name="feed">更新フィード（GitHub リポジトリ URL・ローカルパス・単純 URL のいずれか）</param>
    public VelopackAppUpdater(string feed)
    {
        // GitHub リポジトリ URL のときは専用ソース（API 経由でリリースを解決）を使う。
        // それ以外はローカルパス／単純 URL としてそのまま文字列コンストラクタへ渡す。
        if (feed.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            _manager = new UpdateManager(new GithubSource(feed, null, false));
        }
        else
        {
            _manager = new UpdateManager(feed);
        }
    }

    /// <inheritdoc />
    public bool IsInstalled => _manager.IsInstalled;

    /// <inheritdoc />
    public async Task<string?> CheckForUpdateAsync()
    {
        _pendingUpdate = await _manager.CheckForUpdatesAsync();

        // 更新がなければ null。あれば対象バージョンを表示用文字列にして返す
        return _pendingUpdate?.TargetFullRelease.Version.ToString();
    }

    /// <inheritdoc />
    public async Task DownloadAsync()
    {
        // Check していない・更新なしの状態で呼ばれたら何もしない（防御的）
        if (_pendingUpdate is null)
        {
            return;
        }

        await _manager.DownloadUpdatesAsync(_pendingUpdate);
    }

    /// <inheritdoc />
    public void ApplyAndRestart()
    {
        if (_pendingUpdate is null)
        {
            return;
        }

        // 更新を適用してアプリを再起動する（このメソッドからは戻らない）
        _manager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
    }
}
