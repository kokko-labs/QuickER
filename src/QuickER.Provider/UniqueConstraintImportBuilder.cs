using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// テーブルごとの一意制約（主キー以外）を、制約単位の構成列を投入しながら組み立てる共通ビルダー
/// （DB 方言横断で同一の集約ロジックを担う）
/// </summary>
/// <remarks>
/// <para>
/// 一意制約の構成列を宣言順に読み出しながら <see cref="Add"/> で 1 行ずつ投入し、
/// <see cref="Build"/> で「テーブルキー → 一意制約一覧」を得る。制約名と列の宣言順は
/// そのまま保持する（モデル <see cref="UniqueConstraint"/> の正本になるため、
/// 旧実装のような列名のソートはしない）。
/// </para>
/// <para>
/// 取込対象は「真の <c>UNIQUE</c> 制約」に統一する（素の一意インデックス・フィルター付き・
/// プレフィックス／関数インデックスは含めない）。どの行を投入するかの線引きは各方言の
/// インポーターがクエリ側で行う。
/// </para>
/// </remarks>
public sealed class UniqueConstraintImportBuilder
{
    /// <summary>(テーブルキー, 集約キー) → 構成列（宣言順に投入）</summary>
    /// <remarks>
    /// キーはタプルで持つ（文字列連結キーだと、区切り文字を含むテーブル名を
    /// <see cref="Build"/> で分解する際に誤切断され、一意制約が別テーブルへ紐付く）。
    /// </remarks>
    private readonly Dictionary<
        (string TableKey, string ConstraintKey),
        (string? PersistedName, List<string> Columns)
    > _current = new();

    /// <summary>投入順（＝各制約が最初に現れた順）を保つための集約キー一覧</summary>
    private readonly List<(string TableKey, string ConstraintKey)> _order = new();

    /// <summary>一意制約の構成列を 1 行投入する（複合列は同一 <paramref name="constraintKey"/> で宣言順に複数回呼ぶ）</summary>
    /// <param name="tableKey">テーブルキー</param>
    /// <param name="constraintKey">集約キー（制約名 / インデックス名。テーブル内で一意であればよい）</param>
    /// <param name="column">構成列名</param>
    /// <param name="persistedName">
    /// モデルへ保存する制約名。意味のある名前を持たない方言（SQLite の <c>sqlite_autoindex_*</c>）は <c>null</c> を渡す
    /// </param>
    public void Add(string tableKey, string constraintKey, string column, string? persistedName)
    {
        var compositeKey = (tableKey, constraintKey);

        if (!_current.TryGetValue(compositeKey, out var entry))
        {
            entry = (persistedName, new List<string>());
            _current[compositeKey] = entry;
            _order.Add(compositeKey);
        }

        entry.Columns.Add(column);
    }

    /// <summary>投入済みの構成列から「テーブルキー → 一意制約一覧（投入順）」を組み立てる</summary>
    public Dictionary<string, List<ImportedUniqueConstraint>> Build()
    {
        var result = new Dictionary<string, List<ImportedUniqueConstraint>>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var key in _order)
        {
            var entry = _current[key];

            if (!result.TryGetValue(key.TableKey, out var list))
            {
                list = new List<ImportedUniqueConstraint>();
                result[key.TableKey] = list;
            }

            list.Add(new ImportedUniqueConstraint(entry.PersistedName, entry.Columns.ToArray()));
        }

        return result;
    }

    /// <summary>取込んだ一意制約を各エンティティの <see cref="Entity.UniqueConstraints"/> へ載せる</summary>
    /// <param name="tables">テーブルキー → 取込中のテーブルエントリ</param>
    /// <param name="constraints"><see cref="Build"/> の結果</param>
    /// <remarks>
    /// 列名 → カラム ID の解決に失敗する列を 1 つでも含む制約はスキップする（列一覧に現れない
    /// 式インデックス等を意味モデルへ載せないため）。5 方言のインポーターが共有する変換経路。
    /// </remarks>
    public static void Attach(
        IReadOnlyDictionary<string, SchemaTableEntry> tables,
        IReadOnlyDictionary<string, List<ImportedUniqueConstraint>> constraints
    )
    {
        foreach (var (tableKey, list) in constraints)
        {
            if (!tables.TryGetValue(tableKey, out var entry))
            {
                continue;
            }

            foreach (var constraint in list)
            {
                if (constraint.ColumnNames.Length == 0)
                {
                    continue;
                }

                var columnIds = new List<Guid>(constraint.ColumnNames.Length);
                var allResolved = true;

                foreach (var columnName in constraint.ColumnNames)
                {
                    if (!entry.ColumnsByName.TryGetValue(columnName, out var column))
                    {
                        allResolved = false;
                        break;
                    }

                    columnIds.Add(column.Id);
                }

                if (!allResolved)
                {
                    continue;
                }

                entry.Entity.UniqueConstraints.Add(
                    new UniqueConstraint { Name = constraint.Name, ColumnIds = columnIds }
                );
            }
        }
    }
}

/// <summary>取込んだ一意制約 1 件（モデルへ載せる前の方言中立表現）</summary>
/// <param name="Name">モデルへ保存する制約名（意味のある名前が無ければ <c>null</c>）</param>
/// <param name="ColumnNames">構成列名（宣言順）</param>
public sealed record ImportedUniqueConstraint(string? Name, string[] ColumnNames);
