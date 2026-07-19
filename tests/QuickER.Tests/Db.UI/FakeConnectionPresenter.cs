using QuickER.Db.UI;
using QuickER.Provider;

namespace QuickER.Tests.Db.UI;

/// <summary>指定した確定結果を返す接続ダイアログ提示フェイク</summary>
internal sealed class FakeConnectionPresenter(DbConnectionDialogResult? result)
    : IDbConnectionDialogPresenter
{
    public DbConnectionDialogResult? Show(
        DbConnectionDialogMode mode,
        IDatabaseProvider? fixedProvider = null,
        string? title = null
    ) => result;
}
