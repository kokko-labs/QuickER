using System.Collections.ObjectModel;

namespace QuickER.Dialects;

/// <summary>表を作る文（<c>CREATE TABLE</c>）の中に書いた一意制約の、方言ごとの扱いの宣言</summary>
/// <remarks>
/// 既定値を持たない（5 方言を明示して書き、知らない方言名は例外にする）。docs の方言の表 <c>sync-capabilities</c> の行と
/// <c>DialectDocsTableTests.SyncCapabilitiesTable_MatchesDeclarations</c> が照合する。
/// </remarks>
public static class CreateTableUniqueConstraints
{
    /// <summary>
    /// 方言名（<see cref="DialectNames"/> の正規の綴り）→ 表を作る文の中で、主キーか先に書いた一意制約と同じ列の並びの一意制約を
    /// DB が黙って捨てるか
    /// </summary>
    /// <remarks>
    /// PostgreSQL は同じ定義の索引を作らないので、その一意制約をエラーなしに捨てる（<c>ALTER TABLE … ADD CONSTRAINT</c> で足せば
    /// 別の制約として持つ）。SQLite も 1 つの索引にまとめるので取込に現れない（SQLite は <c>ALTER TABLE</c> で一意制約を足せない）。
    /// SQL Server・MySQL は別の制約として持ち、Oracle は捨てずに断る（ORA-02261）。
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, bool> DropsDuplicateByDialect =
        new ReadOnlyDictionary<string, bool>(
            new Dictionary<string, bool>
            {
                { DialectNames.SqlServer, false },
                { DialectNames.PostgreSql, true },
                { DialectNames.MySql, false },
                { DialectNames.Oracle, false },
                { DialectNames.Sqlite, true },
            }
        );

    /// <summary>
    /// 表を作る文の中で、主キーか先に書いた一意制約と同じ列の並びの一意制約を DB が黙って捨てるか
    /// </summary>
    /// <param name="dialect">方言名（大文字小文字を問わない）</param>
    /// <exception cref="ArgumentException">知らない方言名</exception>
    public static bool DropsDuplicateColumnLists(string dialect)
    {
        var name =
            DialectNames.Find(dialect)
            ?? throw new ArgumentException($"Unknown dialect name: '{dialect}'.", nameof(dialect));

        return DropsDuplicateByDialect[name];
    }
}
