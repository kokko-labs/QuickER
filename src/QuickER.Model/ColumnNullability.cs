namespace QuickER.Model;

/// <summary>
/// 列が NULL を持てるかの判定（主キーの列は、図の <see cref="Column.IsNullable"/> に依らず NULL を持てない）
/// </summary>
/// <remarks>
/// <para>
/// DB の主キーは NULL を持てない。図は主キーの列に NULL 許容の印を持てる（図のファイルの手書きなどで作れる）が、
/// DDL はその列を NOT NULL で作る。DDL・同期スクリプト・差分・同期計画・C# 生成・
/// GUI の列の読込が同じ規則で読むため、判定をここに 1 つだけ置く（写しはソースの規則 <c>column-nullability-copies</c> が禁じる）。
/// </para>
/// <para>
/// 取込（Excel 定義書の <c>TableDefinitionDocumentImporter</c>・C# リバースの <c>CSharpReverseParser</c>）も、
/// 列を組んだ直後にこの判定で NULL 許容の印を決め直し、主キーの列を NOT NULL で読む（DBML・Mermaid・DB の取込と同じ結果）。
/// </para>
/// <para>
/// <see cref="Column"/> は JSON で保存する POCO なので、プロパティにせず静的な判定にする
/// （読み取り専用のプロパティも書き出しの対象になり、図のファイルの形が変わる）。
/// </para>
/// </remarks>
public static class ColumnNullability
{
    /// <summary>列が NULL を持てるか（NULL 許容の印があり、主キーの列でない）</summary>
    /// <param name="column">列</param>
    public static bool AllowsNull(Column column)
    {
        ArgumentNullException.ThrowIfNull(column);

        return column.IsNullable && !column.IsPrimaryKey;
    }
}
