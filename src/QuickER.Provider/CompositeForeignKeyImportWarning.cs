using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 複合外部キー（列ペアが 2 組以上）を取り込んだ際に、列の対応付けが失われたことを伝える構造化警告。
/// </summary>
/// <remarks>
/// <para>
/// 意味モデルの <see cref="Relationship"/> は単一の列ペア（<see cref="Relationship.SourceColumnId"/> /
/// <see cref="Relationship.TargetColumnId"/>）しか表現できないため、複合外部キーは
/// 「列対応を持たない 1 本のリレーション」へ劣化して取り込まれる（意図した割り切り）。
/// 取込のその場でその劣化に気づけるよう、対象となった外部キーの素材だけをここへ集める。
/// </para>
/// <para>
/// 表示文言は持たない（言語中立）。GUI / CLI の各表示層が自前の resx で整形する。
/// </para>
/// </remarks>
/// <param name="ConstraintName">外部キー制約名（SQLite のように制約名を持たない方言では合成名）</param>
/// <param name="ChildTable">外部キーを保有する側（子）のテーブル名</param>
/// <param name="ChildColumns">子側の構成列名（投入順＝序数順）</param>
/// <param name="ParentTable">参照先（親・PK 側）のテーブル名</param>
/// <param name="ParentColumns">親側の構成列名（<paramref name="ChildColumns"/> と同じ並び）</param>
public sealed record CompositeForeignKeyImportWarning(
    string ConstraintName,
    string ChildTable,
    IReadOnlyList<string> ChildColumns,
    string ParentTable,
    IReadOnlyList<string> ParentColumns
);
