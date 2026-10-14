using System.Collections.ObjectModel;

namespace QuickER.Mcp;

/// <summary>
/// ツールが受け付ける対象 DBMS（方言）の名前の正本。<c>create_diagram</c> の <c>target_dbms</c>、
/// <c>generate_csharp</c> / <c>generate_ddl</c> の <c>provider</c>、<c>set_query</c> の <c>sql</c> のキーが共有する
/// </summary>
/// <remarks>
/// ツールの説明（enum・説明文）と、受け付ける値の検証の両方がここを読む（写しを作ると、方言を足したときに
/// 説明と検証が食い違う）。外から書き換えられない一覧として公開する（配列を公開すると、どこかの書き換えで
/// 受け付ける方言がプロセス全体で黙って変わる）。名前は各プロバイダの <c>ProviderName</c> と同じ。
/// </remarks>
public static class TargetDbmsNames
{
    /// <summary>対象 DBMS の名前（ツールの説明に載せる順）</summary>
    public static IReadOnlyList<string> All { get; } =
        new ReadOnlyCollection<string>(["sqlserver", "postgresql", "mysql", "oracle", "sqlite"]);

    /// <summary>大文字小文字を無視して名前を引き、正規の綴りを返す（どれとも一致しなければ null）</summary>
    /// <param name="name">調べる名前</param>
    public static string? Find(string? name) =>
        All.FirstOrDefault(candidate =>
            string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)
        );
}
