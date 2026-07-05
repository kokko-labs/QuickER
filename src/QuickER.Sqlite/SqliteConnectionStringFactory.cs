using Microsoft.Data.Sqlite;
using QuickER.Provider;

namespace QuickER.Sqlite;

/// <summary>共通接続設定 <see cref="DbConnectionSettings"/> から SQLite の接続文字列を構築する</summary>
/// <remarks>
/// SQLite はファイル型 DB のため、サーバー系フィールド（Host / Port / 認証）は使わず
/// <see cref="DbConnectionSettings.FilePath"/> のみを用いる。取込専用のため <c>Mode=ReadOnly</c> で構築する
/// （既定の <c>ReadWriteCreate</c> だと誤ったパス指定で空の DB ファイルが自動生成される事故を招くため、
/// 存在しないパスは即座に失敗させる）。
/// </remarks>
public static class SqliteConnectionStringFactory
{
    /// <summary>共通接続設定から ADO.NET の接続文字列を構築する</summary>
    public static string Build(DbConnectionSettings settings)
    {
        var b = new SqliteConnectionStringBuilder
        {
            DataSource = settings.FilePath,
            // 取込専用。誤パス時の空 DB 自動生成を防ぐため読み取り専用で開く
            Mode = SqliteOpenMode.ReadOnly,
        };

        return b.ConnectionString;
    }
}
