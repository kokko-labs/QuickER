namespace QuickER.Services;

/// <summary>
/// アプリの自動更新（チェック・ダウンロード・適用）を抽象化するインターフェース。
/// </summary>
/// <remarks>
/// Velopack への直接依存をこの seam の裏に隔離し、<see cref="UpdateService"/> を
/// UI・Velopack 非依存で単体テストできるようにする（実装は <see cref="VelopackAppUpdater"/>）。
/// </remarks>
public interface IAppUpdater
{
    /// <summary>
    /// 現在のプロセスが Velopack でインストールされた状態か（＝更新を適用できるか）。
    /// 開発時の直接実行やポータブル実行では <c>false</c> になる。
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>
    /// 更新の有無を問い合わせる。新バージョンがあればユーザー提示用の表示文字列を返し、
    /// なければ <c>null</c> を返す。取得した更新情報は内部に保持し、以降の
    /// <see cref="DownloadAsync"/> / <see cref="ApplyAndRestart"/> で使う。
    /// </summary>
    Task<string?> CheckForUpdateAsync();

    /// <summary>直前の <see cref="CheckForUpdateAsync"/> で見つかった更新をダウンロードする</summary>
    Task DownloadAsync();

    /// <summary>ダウンロード済みの更新を適用し、アプリを再起動する（このメソッドから戻らない）</summary>
    void ApplyAndRestart();
}
