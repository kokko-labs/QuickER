using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace QuickER.Provider.Sqlite;

/// <summary>QuickER 自身が SQLite で結果を読まない文（DDL・PRAGMA）を実行する唯一の場所（結果を最後まで読み切る）</summary>
/// <remarks>
/// <para>
/// Microsoft.Data.Sqlite の <c>ExecuteNonQuery</c> は、複数の文のスクリプトで、行の無い表への <c>ADD COLUMN … NOT NULL</c>
/// （SQLite は行があれば断る検査の問い合わせを文の中に持つ）の後ろの文が断られると、例外を出さずにその文から後ろを実行しない。
/// 同期の実行器はトランザクションの中でスクリプトを流すので、断られた文の後ろを飛ばしたまま部分適用をコミットしてしまう。
/// 結果を <c>NextResult</c> で最後まで読むと、断られた文の例外が呼び出し元へ届く。
/// </para>
/// <para>
/// 文の数を見分けずに、1 文でもここを通す（ソースの規則 <c>sqlite-script-executions</c> が、SQLite の接続を作り・使うプロジェクトでの
/// <c>ExecuteNonQuery</c> の呼び出しを断る）。
/// </para>
/// </remarks>
internal static class SqliteScripts
{
    /// <summary>コマンドの文を最後まで実行する（どの文が断られても例外になる）</summary>
    /// <param name="command">SQLite の接続のコマンド（複数の文でよい）</param>
    /// <param name="ct">取り消し</param>
    public static async Task RunToEndAsync(DbCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.NextResultAsync(ct).ConfigureAwait(false)) { }
    }
}
