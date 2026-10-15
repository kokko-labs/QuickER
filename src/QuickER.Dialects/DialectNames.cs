using System.Collections.ObjectModel;

namespace QuickER.Dialects;

/// <summary>
/// 方言（対象 DBMS）の名前の正本。図の対象 DBMS（<c>ErDiagram.TargetDbms</c>）・各プロバイダの <c>ProviderName</c>・
/// MCP のツールが受け付ける値（<c>create_diagram</c> の <c>target_dbms</c>、<c>generate_csharp</c> / <c>generate_ddl</c> の
/// <c>provider</c>、<c>set_query</c> の <c>sql</c> のキー）が共有する
/// </summary>
/// <remarks>
/// ツールの説明（enum・説明文）と、受け付ける値の検証の両方がここを読む（写しを作ると、方言を足したときに
/// 説明と検証が食い違う）。一覧は外から書き換えられない形で公開する（配列を公開すると、どこかの書き換えで
/// 受け付ける方言がプロセス全体で黙って変わる）。
/// </remarks>
public static class DialectNames
{
    /// <summary>SQL Server</summary>
    public const string SqlServer = "sqlserver";

    /// <summary>PostgreSQL</summary>
    public const string PostgreSql = "postgresql";

    /// <summary>MySQL</summary>
    public const string MySql = "mysql";

    /// <summary>Oracle</summary>
    public const string Oracle = "oracle";

    /// <summary>SQLite</summary>
    public const string Sqlite = "sqlite";

    /// <summary>方言の名前（ツールの説明に載せる順）</summary>
    public static IReadOnlyList<string> All { get; } =
        new ReadOnlyCollection<string>([SqlServer, PostgreSql, MySql, Oracle, Sqlite]);

    /// <summary>大文字小文字を無視して名前を引き、正規の綴りを返す（どれとも一致しなければ null）</summary>
    /// <param name="name">調べる名前</param>
    public static string? Find(string? name) =>
        All.FirstOrDefault(candidate =>
            string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)
        );
}
