using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Provider.Sqlite;

/// <summary>SQLite の識別子整形ユーティリティ</summary>
/// <remarks>
/// クォート・エスケープ・スキーマ分解などを複数の Builder / Importer で共有する。
/// SQLite は識別子を二重引用符でクォートすることで大文字小文字・記号を保持する
/// （<see cref="SqlIdentifier.Bracket"/> 相当。PostgreSQL の <c>PgIdentifier</c> と対称）。
/// SQLite にはスキーマ修飾（<c>schema.table</c>）の概念が実質無いが、SQL Server 由来のスキーマ付き
/// テーブル名を受け取った場合でも壊れないよう、ドット分割クォートに対応する。
/// <para>
/// <b>この分割クォートは DDL（<c>CREATE TABLE</c> / <c>REFERENCES</c> 等）の <c>Quote</c> 専用</b>で、
/// <c>PRAGMA table_xinfo(...)</c> / <c>index_list(...)</c> / <c>index_info(...)</c> /
/// <c>foreign_key_list(...)</c> のように「1 つの識別子を書く場所」では使わないこと。PRAGMA の引数は
/// スキーマ修飾ではなく<b>単一のテーブル名／インデックス名そのもの</b>（取込範囲は単一 DB のため）で、
/// ドットは名前の一部でしかない。<c>Quote</c> を通すとドットのある名前（<c>"a.b"</c> テーブル等）が
/// <c>PRAGMA table_xinfo("a"."b")</c> のように誤って 2 分割クォートされ、構文エラーで取込全体が失敗する。
/// PRAGMA の引数には <see cref="QuoteSimple"/> を使うこと。
/// </para>
/// </remarks>
public static class SqliteIdentifier
{
    /// <summary>識別子の長さの上限（SQLite は上限なし。値の正本は <see cref="IdentifierLengthLimits"/>）</summary>
    public static IdentifierLengthLimit Limit => IdentifierLengthLimits.Sqlite;

    /// <summary>テーブル名を <c>"schema"."name"</c> または <c>"name"</c> 形式へクォートする（DDL 専用）</summary>
    /// <remarks>
    /// ドットをスキーマ区切りとして 2 分割する。PRAGMA の引数のように「ドットも含めた 1 つの識別子」を
    /// 渡す場所では使わないこと（<see cref="QuoteSimple"/> を使う）。
    /// </remarks>
    public static string Quote(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "\"\"";
        }

        // ドットを含む場合はスキーマ修飾名として 2 分割し、各部を個別にクォートする
        if (SchemaQualifiedName.TrySplit(name, out var schema, out var table))
        {
            return $"\"{Escape(schema)}\".\"{Escape(table)}\"";
        }

        return $"\"{Escape(name)}\"";
    }

    /// <summary>
    /// 単一識別子をクォートする（ドットを分割しない）。カラム名や、<c>PRAGMA</c> の引数のように
    /// 「ドットも含めて 1 つの名前」を書く場所（<c>table_xinfo</c> / <c>index_list</c> / <c>index_info</c> /
    /// <c>foreign_key_list</c> の引数）に使う。
    /// </summary>
    public static string QuoteSimple(string name) => $"\"{Escape(name)}\"";

    /// <summary>スキーマを付けない表の DB 名（SQLite はスキーマの無い表を main に作り、main で引く）</summary>
    private const string MainDatabase = "main";

    /// <summary>
    /// 外部キーの句（<c>REFERENCES</c>）に書く親の表名を、子と同じ DB の表なら表の部分だけクォートして返す。違う DB なら <c>null</c>
    /// </summary>
    /// <remarks>
    /// SQLite の外部キーの句は表名にスキーマを書けず（<c>REFERENCES "sales"."regions"</c> は構文エラー）、親は子と同じ DB の中で
    /// 解決する。そのため添付した別の DB の表は参照できない。DB 名は <see cref="SchemaQualifiedName"/> の分け方で取り、スキーマの無い名前は
    /// <c>main</c> とみなし、SQLite の照合（<see cref="NameEquals"/>）で比べる。DDL 生成・同期の表の追加・再構築はこの 1 か所を通す。
    /// </remarks>
    /// <param name="childTable">外部キーを持つ子の表名（図のまま）</param>
    /// <param name="parentTable">参照先の親の表名（図のまま）</param>
    public static string? QuoteReferencedTable(string childTable, string parentTable)
    {
        var childDatabase = SchemaQualifiedName.Split(childTable).Schema ?? MainDatabase;
        var (parentDatabase, parentPart) = SchemaQualifiedName.Split(parentTable);

        return NameEquals(childDatabase, parentDatabase ?? MainDatabase)
            ? QuoteSimple(parentPart)
            : null;
    }

    /// <summary>SQLite の識別子の照合（ASCII の英字だけ大文字小文字を同一視し、それ以外は序数で比べる）</summary>
    /// <remarks>SQLite は名前の大文字小文字を ASCII でだけ畳むので、<c>Äpfel</c> と <c>äpfel</c> は別の名前になる</remarks>
    public static bool NameEquals(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (AsciiLower(left[i]) != AsciiLower(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>ASCII の大文字だけを小文字にする</summary>
    private static char AsciiLower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;

    /// <summary>識別子内の <c>"</c> を SQLite の規則（二重化）に従ってエスケープする</summary>
    public static string Escape(string name) => (name ?? string.Empty).Replace("\"", "\"\"");
}
