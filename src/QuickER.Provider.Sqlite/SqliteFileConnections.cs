using Microsoft.Data.Sqlite;

namespace QuickER.Provider.Sqlite;

/// <summary>QuickER 自身が SQLite のファイルを開く接続を作る唯一の場所（接続をプールしない）</summary>
/// <remarks>
/// <para>
/// Microsoft.Data.Sqlite は既定で接続をプールし、閉じた接続もプールに残ってファイルを開いたままにする。
/// SQLite は Windows でファイルを削除の共有なしで開くので、新規作成・取込・同期の直後に利用者がそのファイルを
/// 削除・移動・名前の変更をすると共有違反で断られる。そのため、受けた接続文字列に <c>Pooling=False</c> を立て、
/// 閉じた接続がそのままファイルを閉じるようにする（接続文字列に <c>Pooling=True</c> があっても上書きする）。
/// </para>
/// <para>
/// 新規作成・取込・同期はどれも操作 1 回で接続を 1 本しか開かないので、プールを切っても性能は変わらない。
/// 閉じた接続はプールへ戻らないので、接続に設定した PRAGMA（外部キー検査など）も接続と一緒に消え、後から開いた接続へ残らない。
/// 生成コードのランタイム（QuickER.Runtime.Sqlite）は利用者のアプリの接続なので、ここを通さない。
/// 計画の段のビューとトリガーの判定（<see cref="SqliteLiveDependentCheck"/>）が開くメモリの DB の接続も、ここで作る（作り手を 1 つに保つ）。
/// </para>
/// </remarks>
internal static class SqliteFileConnections
{
    /// <summary>接続文字列にプールなしを立てた、まだ開いていない接続を作る</summary>
    /// <param name="connectionString">SQLite の接続文字列（<c>Pooling</c> の指定は上書きする）</param>
    public static SqliteConnection Create(string connectionString) =>
        new(
            new SqliteConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString
        );
}
