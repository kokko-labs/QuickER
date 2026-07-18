using QuickER.Db.UI.Resources;
using QuickER.Extensibility;
using QuickER.Sqlite;

namespace QuickER.Db.UI;

/// <summary>
/// データベースへ接続し、現在の図との差分同期ダイアログを開くコマンドサービス。
/// </summary>
/// <remarks>
/// アプリ本体 <c>MainViewModel</c> の <c>SyncToDatabase</c> / <c>CanSyncToDatabase</c> /
/// <c>SyncToDatabaseTooltip</c> から移設したフィーチャーモジュール本体。
/// SQLite は DB 同期未対応のため <see cref="CanRun"/> は false になり、
/// <see cref="CurrentTooltip"/> は未対応理由を返す（対応方言では通常の説明を返す）。
/// 対象 DBMS 切替に伴うボタン活性・ツールチップの再評価は、モジュール側が
/// <see cref="IErDiagramHost.TargetDbmsChanged"/> を購読して行う。
/// </remarks>
public sealed class DbSyncCommandService
{
    /// <summary>ER 図の取得・プロバイダ解決・対象 DBMS 読み取りを提供するホスト契約</summary>
    private readonly IErDiagramHost _host;

    /// <summary>DB 接続ダイアログの提示シーム</summary>
    private readonly IDbConnectionDialogPresenter _connectionPresenter;

    /// <summary>スキーマ同期ダイアログの提示シーム</summary>
    private readonly ISchemaSyncDialogPresenter _syncPresenter;

    /// <summary>依存を注入して生成する</summary>
    public DbSyncCommandService(
        IErDiagramHost host,
        IDbConnectionDialogPresenter connectionPresenter,
        ISchemaSyncDialogPresenter syncPresenter
    )
    {
        _host = host;
        _connectionPresenter = connectionPresenter;
        _syncPresenter = syncPresenter;
    }

    /// <summary>DB 同期を実行できるか（SQLite は同期未対応のため実行不可）</summary>
    public bool CanRun => _host.TargetDbms != SqliteProvider.ProviderName;

    /// <summary>DB 同期ボタンのツールチップ（未対応方言のときは理由、対応方言のときは通常の説明）</summary>
    public string CurrentTooltip =>
        CanRun ? Strings.Db_SyncWriteBack : Strings.Db_SyncSqliteUnsupported;

    /// <summary>データベースへ接続し、現在のダイアグラムとの差分同期ダイアログを開く（ツールバーボタンから実行）</summary>
    /// <remarks>同期先の方言は図の TargetDbms に固定する（接続ダイアログでは DBMS を選択できない）</remarks>
    public void Run()
    {
        // 同期先の方言は現在の対象 DBMS に固定する（解決不能なら未指定）
        var fixedProvider = _host.Providers.TryGet(_host.TargetDbms, out var provider)
            ? provider
            : null;

        var picked = _connectionPresenter.Show(
            DbConnectionDialogMode.Sync,
            fixedProvider: fixedProvider,
            title: Strings.Db_SyncTitle
        );

        if (picked is null)
        {
            return;
        }

        // 接続確定時点の図をスナップショットして同期の目標スキーマとする
        var target = _host.GetDiagram();
        _syncPresenter.Show(
            picked.Provider,
            picked.Settings,
            target.Entities,
            target.Relationships
        );
    }
}
