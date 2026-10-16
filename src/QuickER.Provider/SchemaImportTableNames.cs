using QuickER.Dialects;

namespace QuickER.Provider;

/// <summary>取込が図に入れるテーブル名を、取り込んでよいかの判定（5 方言の取込が共有する）</summary>
/// <remarks>
/// QuickER は図のテーブル名を最初の <c>.</c> でスキーマとテーブルに分けて読む（<see cref="SchemaQualifiedName"/>）。
/// 名前に <c>.</c> を含むテーブル（SQL Server ではスキーマ名に含むものも）を図に入れると別のテーブルとして読まれ、
/// 生成コードの読み書き・DDL・同期が別のテーブルを指す。そうしたテーブルは取り込まず、
/// <see cref="SchemaImportWarningKind.DottedTableNameExcluded"/> で名指しする。
/// 判定は、方言が図に入れる名前を作る関数（SQL Server の <c>dbo</c> を省く規則など）の結果と、DB のスキーマ・テーブルを比べる。
/// 図に入れる名前の規則を写さないので、名前を作る関数と判定が別々の規則で食い違わない。
/// </remarks>
public static class SchemaImportTableNames
{
    /// <summary>図に入れる名前を QuickER の読み方で分けたとき、DB のスキーマとテーブルへ戻るか</summary>
    /// <param name="diagramName">取込が図に入れる名前（方言の名前を作る関数の結果）</param>
    /// <param name="schema">
    /// DB のスキーマ名（スキーマを持たない方言・図に入れない方言は <c>null</c>）。図の名前がスキーマを省いていれば、スキーマは比べない
    /// </param>
    /// <param name="table">DB のテーブル名</param>
    public static bool RoundTrips(string diagramName, string? schema, string table)
    {
        var (readSchema, readTable) = SchemaQualifiedName.Split(diagramName);

        return string.Equals(readTable, table, StringComparison.Ordinal)
            && (readSchema is null || string.Equals(readSchema, schema, StringComparison.Ordinal));
    }

    /// <summary>戻らないテーブルなら、取り込まなかったことを告げる警告を足して <c>true</c> を返す</summary>
    /// <param name="diagramName">取込が図に入れる名前（方言の名前を作る関数の結果）</param>
    /// <param name="schema">DB のスキーマ名（警告にも添える。スキーマを図に入れない方言は <c>null</c>）</param>
    /// <param name="table">DB のテーブル名</param>
    /// <param name="warnings">警告の追加先</param>
    /// <returns><c>true</c> なら呼び出し側はそのテーブルを取り込まず、取り込まなかったテーブルとして覚える</returns>
    public static bool ExcludeIfNotRoundTrip(
        string diagramName,
        string? schema,
        string table,
        ICollection<SchemaImportWarning> warnings
    )
    {
        if (RoundTrips(diagramName, schema, table))
        {
            return false;
        }

        warnings.Add(
            new SchemaImportWarning(
                SchemaImportWarningKind.DottedTableNameExcluded,
                table,
                schema ?? string.Empty
            )
        );
        return true;
    }
}
