using QuickER.Provider;

namespace QuickER.Db.UI;

/// <summary>接続ダイアログの確定結果（接続設定と選択された方言）</summary>
/// <param name="Settings">確定した接続設定</param>
/// <param name="Provider">確定時に選択されていたプロバイダ</param>
public sealed record DbConnectionDialogResult(
    DbConnectionSettings Settings,
    IDatabaseProvider Provider
);
