using QuickER.Gui.Abstractions;
using QuickER.Resources;

namespace QuickER.Services;

/// <summary>
/// 起動時の更新チェックを統括するサービス（UI・Velopack 非依存で単体テスト可能）。
/// </summary>
/// <remarks>
/// フィード未設定・非インストール実行（開発時／ポータブル）・ネットワーク等の例外は黙ってスキップし、
/// 起動を阻害しない。更新があればユーザーの承諾を得てからダウンロード→再起動適用する。
/// Velopack 依存は <see cref="IAppUpdater"/> の裏へ隔離し、ここには持ち込まない。
/// </remarks>
public sealed class UpdateService
{
    /// <summary>更新の承諾を得るための確認ダイアログ</summary>
    private readonly IDialogService _dialogService;

    /// <summary>フィード文字列から <see cref="IAppUpdater"/> を生成するファクトリ</summary>
    private readonly Func<string, IAppUpdater> _updaterFactory;

    /// <summary>環境変数取得関数（フィード解決に用いる。テストで差し替え可能）</summary>
    private readonly Func<string, string?> _getEnvironmentVariable;

    /// <summary>依存を注入して <see cref="UpdateService"/> を構築する</summary>
    /// <param name="dialogService">更新の承諾を得る確認ダイアログ</param>
    /// <param name="updaterFactory">フィード文字列から更新実行体を生成するファクトリ</param>
    /// <param name="getEnvironmentVariable">環境変数取得関数（フィード解決に用いる）</param>
    public UpdateService(
        IDialogService dialogService,
        Func<string, IAppUpdater> updaterFactory,
        Func<string, string?> getEnvironmentVariable
    )
    {
        _dialogService = dialogService;
        _updaterFactory = updaterFactory;
        _getEnvironmentVariable = getEnvironmentVariable;
    }

    /// <summary>
    /// 起動時に更新を確認し、あればユーザーの承諾を得てダウンロード→再起動適用する。
    /// フィード未設定・非インストール実行・更新なし・拒否・各種例外のいずれでも起動を阻害しない。
    /// </summary>
    public async Task CheckOnStartupAsync()
    {
        // フィード未設定（リポジトリ未公開かつ環境変数なし）なら何もしない
        var feed = UpdateFeed.Resolve(_getEnvironmentVariable);

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

            // ユーザーの承諾を得てから初めてダウンロード・適用する
            var accepted = _dialogService.Confirm(
                string.Format(Strings.Update_ConfirmMessage, version),
                Strings.Update_ConfirmTitle
            );

            if (!accepted)
            {
                return;
            }

            await updater.DownloadAsync();
            updater.ApplyAndRestart();
        }
        catch
        {
            // ネットワーク断・フィード不正・Velopack 内部エラー等は握りつぶす。
            // 更新チェックの失敗でアプリの起動を阻害しないことを最優先する。
        }
    }
}
