using QuickER.Gui.Abstractions;
using QuickER.Resources;

namespace QuickER.Services;

/// <summary>
/// 起動時の更新チェックを統括するサービス（UI・Velopack 非依存で単体テスト可能）。
/// </summary>
/// <remarks>
/// フィード未設定・非インストール実行（開発時／ポータブル）・ネットワーク等の例外は黙ってスキップし、
/// 起動を阻害しない。更新があればユーザーの承諾を得てからダウンロードし、終わったら
/// 「今すぐ再起動して適用するか」をもう一度確認する。
/// <para>
/// <b>適用は必ず通常の終了経路の後</b>（<see cref="PostExitActions"/>＝正常終了の最後）で行う。
/// ダウンロードは非同期で、承諾後も利用者は編集を続けられるため、その場でプロセスを終わらせると
/// メインウィンドウの <c>Closing</c>（復旧用の自動保存・機能モジュールの終了連鎖）が飛び、編集内容が失われ
/// AI の子プロセスが孤児になる。再起動を承諾したら終了を要求するだけで、適用はその終了の最後に始まる。
/// 断ったら次に閉じたときに適用する。終了がキャンセルされたら適用しない（登録は次の正常終了まで残る）。
/// </para>
/// Velopack 依存は <see cref="IAppUpdater"/> の裏へ隔離し、ここには持ち込まない。
/// </remarks>
public sealed class UpdateService
{
    /// <summary>更新の承諾を得るための確認ダイアログ</summary>
    private readonly IDialogService _dialogService;

    /// <summary>フィード文字列から <see cref="IAppUpdater"/> を生成するファクトリ</summary>
    private readonly Func<string, IAppUpdater> _updaterFactory;

    /// <summary>フィード解決関数（未設定なら null を返す。テストで差し替え可能）</summary>
    private readonly Func<string?> _resolveFeed;

    /// <summary>起動時の更新確認が有効かを返す関数（設定のオプトアウト。省略時は常に有効）</summary>
    private readonly Func<bool> _isEnabled;

    /// <summary>正常終了の最後に行う処理の登録先（更新の適用をここへ登録する）</summary>
    private readonly PostExitActions _postExitActions;

    /// <summary>通常の終了経路（メインウィンドウの Close）を要求する処理</summary>
    private readonly Action _requestShutdown;

    /// <summary>依存を注入して <see cref="UpdateService"/> を構築する</summary>
    /// <param name="dialogService">更新の承諾を得る確認ダイアログ</param>
    /// <param name="updaterFactory">フィード文字列から更新実行体を生成するファクトリ</param>
    /// <param name="resolveFeed">フィード解決関数（未設定なら null を返す）</param>
    /// <param name="postExitActions">
    /// 正常終了の最後に行う処理の登録先（本番は <see cref="WpfApplicationExit.Pending"/>）
    /// </param>
    /// <param name="requestShutdown">
    /// 通常の終了経路を要求する処理（本番は <see cref="WpfApplicationExit.RequestClose"/>）
    /// </param>
    /// <param name="isEnabled">
    /// 起動時の更新確認が有効かを返す関数（<c>null</c> なら常に有効）。
    /// 本番では <see cref="GuiAppSettings.CheckForUpdatesOnStartup"/> を読む
    /// </param>
    public UpdateService(
        IDialogService dialogService,
        Func<string, IAppUpdater> updaterFactory,
        Func<string?> resolveFeed,
        PostExitActions postExitActions,
        Action requestShutdown,
        Func<bool>? isEnabled = null
    )
    {
        _dialogService = dialogService;
        _updaterFactory = updaterFactory;
        _resolveFeed = resolveFeed;
        _postExitActions = postExitActions;
        _requestShutdown = requestShutdown;
        _isEnabled = isEnabled ?? (static () => true);
    }

    /// <summary>
    /// 起動時に更新を確認し、あればユーザーの承諾を得てダウンロードし、正常終了の最後に適用するよう登録する。
    /// 設定でのオプトアウト・フィード未設定・非インストール実行・更新なし・拒否・各種例外の
    /// いずれでも起動を阻害しない。
    /// </summary>
    public async Task CheckOnStartupAsync()
    {
        // 設定でオプトアウトされていればフィードの解決すら行わない（＝ネットワークへ一切出ない）
        if (!_isEnabled())
        {
            return;
        }

        // フィード未設定（配布ビルドで定数が空の構成）なら何もしない
        var feed = _resolveFeed();

        if (feed is null)
        {
            return;
        }

        try
        {
            var updater = _updaterFactory(feed);

            // 非インストール実行（開発時の直接実行・ポータブル）では更新を適用できないため何もしない
            if (!updater.IsInstalled)
            {
                return;
            }

            // 更新がなければ何もしない
            var version = await updater.CheckForUpdateAsync();

            if (version is null)
            {
                return;
            }

            // ユーザーの承諾を得てから初めてダウンロードする
            var accepted = _dialogService.Confirm(
                string.Format(Strings.Update_ConfirmMessage, version),
                Strings.Update_ConfirmTitle
            );

            if (!accepted)
            {
                return;
            }

            await updater.DownloadAsync();

            // 今すぐ再起動するかを確かめる（ダウンロード中も編集を続けられるので、終了の時期は利用者が決める）。
            // どちらでも適用は正常終了の最後＝Closing の自動保存と終了連鎖の後で、終了がキャンセルされれば走らない
            var restartNow = _dialogService.Confirm(
                Strings.Update_RestartConfirmMessage,
                Strings.Update_ConfirmTitle
            );

            _postExitActions.ScheduleUpdateApply(updater.ApplyOnExit, restartNow);

            if (restartNow)
            {
                _requestShutdown();
            }
        }
        catch
        {
            // ネットワーク断・フィード不正・Velopack 内部エラー等は握りつぶす。
            // 更新チェックの失敗でアプリの起動を阻害しないことを最優先する。
        }
    }
}
