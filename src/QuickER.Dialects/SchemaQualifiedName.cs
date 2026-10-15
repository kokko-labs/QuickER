using System.Diagnostics.CodeAnalysis;

namespace QuickER.Dialects;

/// <summary>スキーマで修飾されたテーブル名（<c>schema.table</c>）を分ける規則の正本</summary>
/// <remarks>
/// 図のテーブル名は最初の <c>.</c> でスキーマの部分と表の部分に分ける（2 つ目以降の <c>.</c> は表の名前の一部。
/// <c>.</c> の前後が空でもそのまま返す）。DDL・同期・識別子の長さの判定・生成コードの同期 SQL が別々の規則で分けると、
/// <c>a.b.c</c> のような名前で別々の表を指す。生成ランタイムの <c>EntitySaveMetadata.QuoteTableName</c> はこのアセンブリを
/// 参照できない（ランタイムのパッケージは依存ゼロ）ので写しを持ち、一致は <c>SchemaQualifiedNameParityTests</c> が固定する。
/// </remarks>
public static class SchemaQualifiedName
{
    /// <summary>スキーマで修飾された名前か（<c>.</c> を含むか）</summary>
    public static bool IsQualified(string tableName)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        return tableName.Contains('.');
    }

    /// <summary>最初の <c>.</c> で分ける（<c>.</c> が無ければスキーマは <c>null</c> で、表の部分は名前全体）</summary>
    public static (string? Schema, string Name) Split(string tableName)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        var dot = tableName.IndexOf('.');

        return dot < 0 ? (null, tableName) : (tableName[..dot], tableName[(dot + 1)..]);
    }

    /// <summary>スキーマで修飾された名前なら最初の <c>.</c> で分けて true を返す（修飾されていなければ表の部分は名前全体）</summary>
    public static bool TrySplit(
        string tableName,
        [NotNullWhen(true)] out string? schema,
        out string name
    )
    {
        (schema, name) = Split(tableName);
        return schema is not null;
    }
}
