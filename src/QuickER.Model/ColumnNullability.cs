namespace QuickER.Model;

/// <summary>
/// 列が NULL を持てるかの判定（主キーの列は、図の <see cref="Column.IsNullable"/> に依らず NULL を持てない）
/// </summary>
/// <remarks>
/// <para>
/// DB の主キーは NULL を持てない。図は主キーの列に NULL 許容の印を持てる（Excel 定義書の取込・C# リバース・
/// 図のファイルの手書きで作れる）が、DDL はその列を NOT NULL で作る。DDL・同期スクリプト・差分・同期計画・C# 生成・
/// GUI の列の読込が同じ規則で読むため、判定をここに 1 つだけ置く（写しはソースの規則 <c>column-nullability-copies</c> が禁じる）。
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
