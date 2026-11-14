namespace QuickER.Provider.Sqlite;

/// <summary>ビューが読めるかの判定の文（同期の実行器と計画の段の判定 <see cref="SqliteLiveDependentCheck"/> が共有する）</summary>
/// <remarks>
/// SQLite はビューを作るときに参照先を検証しないので、読めない（壊れた）ビューは既存の DB にもあり得る。各ビューを
/// <c>SELECT * … LIMIT 0</c> で準備させ、失敗したものを読めないビューとする。実行器は同期の前後で、計画の判定はスキーマだけを写した
/// メモリの DB で同期の前後の形で、同じ文を流して比べる（判定の文を写さない＝実行器が巻き戻すビューと計画が外すビューが割れない）。
/// </remarks>
internal static class SqliteViews
{
    /// <summary>ビューの名前を列挙する文</summary>
    public const string ListSql = "SELECT name FROM sqlite_master WHERE type = 'view';";

    /// <summary>ビューを読めるか試す文（行は読まない）</summary>
    /// <param name="view">ビューの名前</param>
    public static string ReadProbeSql(string view) =>
        $"SELECT * FROM {SqliteIdentifier.QuoteSimple(view)} LIMIT 0;";
}
