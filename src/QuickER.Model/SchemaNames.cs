namespace QuickER.Model;

/// <summary>
/// 図に保存する名前（テーブル名・列名・制約名・名前付きクエリの名前・パラメータ名・射影のフィールド名・結果の型名）の
/// 同一性の規則の正本
/// </summary>
/// <remarks>
/// <para>
/// 規則は「前後の空白を除き、<see cref="StringComparison.OrdinalIgnoreCase"/> で一致すれば同じ名前」。保存する形は
/// 前後の空白を除いた形で、綴り（大文字・小文字）は書き換えない（PostgreSQL・Oracle の引用識別子では綴りが別の表を指すため）。
/// </para>
/// <para>
/// 名前の重複の判定・名前での引き当ては、素の <c>string.Equals</c>・<c>StringComparer</c>・<c>Trim</c> を書かずにここを通す
/// （入口ごとに規則を写すと、入口ごとに答えが割れる）。素の比較を書いた箇所はソース走査のガード
/// （<c>SchemaNamesGuardTests</c>）が検出する。
/// </para>
/// <para>
/// 対象外: データの値の比較（主キーの値・照合順序）。例外（区別する側に残す）は DB 取込のカタログの照合
/// （DB には大小だけ違う名前が共存し得る）・生 SQL のパラメータの束縛・生成した C# の識別子そのもの。
/// </para>
/// </remarks>
public static class SchemaNames
{
    /// <summary>2 つの名前が同じ名前か（前後の空白を除いて大文字小文字を無視して一致するか）</summary>
    /// <param name="first">名前（null は空の名前として扱う）</param>
    /// <param name="second">名前（null は空の名前として扱う）</param>
    public static bool AreSame(string? first, string? second) =>
        string.Equals(
            ToStoredForm(first),
            ToStoredForm(second),
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>名前を図へ保存する形（前後の空白を除く・綴りはそのまま。null は空文字）</summary>
    /// <param name="name">名前</param>
    public static string ToStoredForm(string? name) => (name ?? string.Empty).Trim();

    /// <summary>
    /// 名前を鍵にする辞書・集合・GroupBy 用の比較器（<see cref="AreSame"/> と同じ規則）
    /// </summary>
    public static IEqualityComparer<string?> Comparer { get; } = new SchemaNameComparer();

    private sealed class SchemaNameComparer : IEqualityComparer<string?>
    {
        public bool Equals(string? x, string? y) => AreSame(x, y);

        public int GetHashCode(string? obj) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(ToStoredForm(obj));
    }
}
