using System.Collections.Concurrent;
using System.Text;

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
    /// 名前を、序数（<see cref="StringComparison.Ordinal"/>）で比べたときの答えが <see cref="AreSame"/> と同じになる形へ畳む
    /// （名前を連結したシグネチャ・複合キーを作るため）
    /// </summary>
    /// <param name="name">名前（null は空の名前として扱う）</param>
    /// <returns>
    /// 前後の空白を除き、大文字小文字の違いを畳んだ形。同じプロセスの中で比べるためだけの形で、図・SQL・画面へ書かず、
    /// 保存もしない（ASCII 以外の字の代表は、そのプロセスで最初に畳んだ綴りになる）
    /// </returns>
    /// <remarks>
    /// <para>
    /// <see cref="ToStoredForm"/> した名前を 1 文字（サロゲートペアは 1 文字）ずつ、<see cref="StringComparison.OrdinalIgnoreCase"/>
    /// が同じとみなす字の代表へ置き換える。代表は OrdinalIgnoreCase を比較器にした表で引き当てる（ASCII の英字は大文字、
    /// それ以外は最初に表へ入った綴り）（代表を決めるのも OrdinalIgnoreCase だけなので、答えが食い違わない）。
    /// 対になっていないサロゲートはそのまま残す。
    /// </para>
    /// <para>
    /// 小文字化（<c>ToLowerInvariant</c>）・大文字化（<c>ToUpperInvariant</c>・<see cref="Rune.ToUpperInvariant"/>）で畳むと、
    /// OrdinalIgnoreCase と答えが食い違う字がある。小文字化はケルビン記号 U+212A・オーム記号 U+2126・オングストローム記号
    /// U+212B・U+03F4・U+1E9E を別の字と同じにし、大文字化は U+017F ſ を S と同じにする。さらに大文字化は ICU のデータに
    /// 依存し、OrdinalIgnoreCase が同じとみなす字（Unicode 16 のガライ文字 U+10D50 と U+10D70 など）を畳まない環境がある。
    /// キーの序数一致と <see cref="AreSame"/> が一致することは、Unicode のスカラー値の全部で <c>SchemaNamesKeyTests</c> が固定する。
    /// </para>
    /// </remarks>
    public static string Key(string? name)
    {
        var stored = ToStoredForm(name);
        var builder = new StringBuilder(stored.Length);

        for (var i = 0; i < stored.Length; )
        {
            if (!Rune.TryGetRuneAt(stored, i, out var rune))
            {
                // 対になっていないサロゲートは大文字小文字を持たない
                builder.Append(stored[i]);
                i++;
                continue;
            }

            var text = stored.Substring(i, rune.Utf16SequenceLength);
            builder.Append(FoldedLetters.GetOrAdd(text, text));
            i += rune.Utf16SequenceLength;
        }

        return builder.ToString();
    }

    /// <summary>字 → その字の OrdinalIgnoreCase の類の代表（<see cref="Key"/> が使う）</summary>
    /// <remarks>
    /// ASCII の大文字を先に入れておき、英字の類の代表を大文字に固定する（U+0131 ı のように ASCII の英字と同じ類に入る
    /// ASCII 以外の字もあるので、ASCII だけを表の外で畳むことはしない）。
    /// </remarks>
    private static readonly ConcurrentDictionary<string, string> FoldedLetters = new(
        Enumerable
            .Range('A', 26)
            .Select(c => KeyValuePair.Create(((char)c).ToString(), ((char)c).ToString())),
        StringComparer.OrdinalIgnoreCase
    );

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
