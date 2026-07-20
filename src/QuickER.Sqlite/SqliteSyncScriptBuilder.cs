using QuickER.Provider;

namespace QuickER.Sqlite;

/// <summary>SQLite の DB 同期スクリプト生成の明示スタブ（初回スコープ外）</summary>
/// <remarks>
/// <para>
/// SQLite は <c>ALTER TABLE ADD CONSTRAINT</c> / 列型変更 / 列削除（旧バージョン）等が制限され、
/// スキーマ変更の多くは「新テーブル作成 → データ移送 → 旧テーブル削除 → リネーム」というテーブル再構築方式で
/// 実現する必要がある。この再構築は他方言の逐次 DDL とは設計が大きく異なるため、初回リリースでは同期を未対応とする。
/// </para>
/// <para>
/// 将来対応方針: 差分から影響テーブルを特定し、上記のテーブル再構築方式（<c>PRAGMA foreign_keys=OFF</c> →
/// 一時テーブル作成 → <c>INSERT ... SELECT</c> → 旧テーブル DROP → <c>ALTER TABLE RENAME</c>）で反映する予定。
/// </para>
/// </remarks>
public sealed class SqliteSyncScriptBuilder : ISyncScriptBuilder
{
    /// <summary>常に <see cref="NotSupportedException"/> を投げる（SQLite の同期は未対応）</summary>
    public string Build(SyncPlan plan) =>
        throw new NotSupportedException(
            QuickER.Provider.Resources.Strings.Sync_Sqlite_NotSupported
        );
}
