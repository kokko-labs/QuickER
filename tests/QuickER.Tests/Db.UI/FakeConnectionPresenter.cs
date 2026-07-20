using QuickER.Db.UI;
using QuickER.Provider;

namespace QuickER.Tests.Db.UI;

/// <summary>指定した確定結果を返し、呼び出し引数を記録する接続ダイアログ提示フェイク</summary>
internal sealed class FakeConnectionPresenter(DbConnectionDialogResult? result)
    : IDbConnectionDialogPresenter
{
    /// <summary>直近の呼び出しに渡された用途（未呼び出しなら null）</summary>
    public DbConnectionDialogMode? LastMode { get; private set; }

    /// <summary>直近の呼び出しに渡された「新規 SQLite ファイル作成を許可するか」フラグ（未呼び出しなら null）</summary>
    public bool? LastAllowSqliteFileCreation { get; private set; }

    public DbConnectionDialogResult? Show(
        DbConnectionDialogMode mode,
        IDatabaseProvider? fixedProvider = null,
        string? title = null,
        bool allowSqliteFileCreation = false
    )
    {
        LastMode = mode;
        LastAllowSqliteFileCreation = allowSqliteFileCreation;

        return result;
    }
}
