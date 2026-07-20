using Microsoft.Data.Sqlite;

namespace QuickER.Sqlite;

/// <summary>SQLite の空データベースファイルを新規作成するヘルパー</summary>
/// <remarks>
/// <para>
/// 通常の接続経路（取込 = <see cref="SqliteConnectionStringFactory"/> の <c>Mode=ReadOnly</c>・
/// 同期 = <see cref="SqliteSchemaSyncExecutor"/> の <c>Mode=ReadWrite</c>）は、いずれも
/// 「存在しないパスでは空 DB を自動生成しない」ガードを敷いている（誤ったパス指定による空 DB 事故の防止）。
/// このヘルパーはそのガードを唯一意図的に迂回する経路で、<c>Mode=ReadWriteCreate</c> で開いて即座に閉じ、
/// 有効な空 SQLite DB ファイルを作成する。
/// </para>
/// <para>
/// そのため、明示的なユーザー操作（接続ダイアログの「新規作成」ボタン）経由でのみ呼ぶこと。
/// 手入力や「参照」経由のパスに対しては呼ばない（存在必須の従来ガードを維持する）。
/// Db.UI 層から ADO（Microsoft.Data.Sqlite）を直接触らせないための切断面でもある。
/// </para>
/// </remarks>
public static class SqliteDatabaseFile
{
    /// <summary>指定パスに空の SQLite データベースファイルを作成する</summary>
    /// <remarks>
    /// <c>Mode=ReadWriteCreate</c> は存在しなければ新規作成し、既存ファイルがあれば何もせず開くだけで
    /// 内容を消さない（＝上書きではなく既存 DB への通常同期になる）。作成に失敗した場合（権限不足・
    /// 無効なパス等）は <see cref="SqliteException"/> 等の例外が伝播する（呼び出し側で提示する）。
    /// </remarks>
    /// <param name="path">作成するデータベースファイルのパス</param>
    public static void CreateEmpty(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ConnectionString;

        // 開いて即閉じるだけで空 DB ファイルが確定する（スキーマは同期エンジンが後段で作る）
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
    }
}
