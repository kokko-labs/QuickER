using System.Linq;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 外部キーの構成列行を「FK 保有テーブル（子）＋制約名」ごとに集約し、<see cref="Relationship"/> 一覧へ変換する共通ビルダー
/// （DB 方言横断で同一の集約・1 対 1 判定ロジックを担う）
/// </summary>
/// <remarks>
/// <para>
/// 使い方: 外部キー構成列を序数順に読み出しながら <see cref="Add"/> で 1 行ずつ投入し、
/// 最後に <see cref="Build"/> でリレーション一覧を得る。「FK 保有テーブル＋制約名」単位で親子テーブル・
/// 構成列・参照アクションを 1 件へまとめ、複合外部キーの列対応を復元する。
/// </para>
/// <para>
/// 制約名はテーブルをまたいで一意とは限らない（例: PostgreSQL の制約名一意性はテーブル単位）ため、
/// 集約キーには制約名単体ではなく FK 保有テーブルのキーを合成する。異なるテーブルが同名の FK 制約を
/// 持っていても、別々のリレーションとして正しく分離される。
/// </para>
/// <para>
/// 参照先（子側 FK 保有テーブル）の FK 列集合が、そのテーブルの主キーまたは一意制約と一致する場合は
/// 1 対 1、それ以外は 1 対多と判定する。生成されるリレーションは参照先（PK 側）を起点、
/// FK 保有テーブルを終点として表現する。
/// </para>
/// <para>
/// <see cref="Build"/> は投入順（=最初に各「FK 保有テーブル＋制約名」が出現した順）を保ったリレーション一覧を返す。
/// </para>
/// <para>
/// 複合外部キー（列ペアが 2 組以上）は、意味モデルが単一の列ペアしか表現できないため列対応を失う。
/// <see cref="Build"/> はその劣化を <see cref="CompositeForeignKeyWarnings"/> へ記録する（リレーション一覧の
/// 内容自体は従来どおり）。
/// </para>
/// </remarks>
public sealed class ForeignKeyRelationshipBuilder
{
    /// <summary>(FK 保有テーブルキー, 制約名) → 集約中の外部キー情報（ConstraintName は元の制約名単体を保持する）</summary>
    /// <remarks>
    /// キーはタプルで持つ（文字列連結キーは、テーブル名や制約名に区切り文字が含まれると
    /// 別々の FK が同一キーへ潰れる／分解時に誤切断される恐れがある）。
    /// </remarks>
    private readonly Dictionary<
        (string TableKey, string ConstraintName),
        (
            string ParentKey,
            string RefKey,
            string ConstraintName,
            List<string> ParentCols,
            List<string> RefCols,
            ForeignKeyReferentialAction OnDelete,
            ForeignKeyReferentialAction OnUpdate
        )
    > _grouped = new();

    /// <summary>直近の <see cref="Build"/> で検出した複合外部キーの劣化警告（<see cref="Build"/> ごとに作り直す）</summary>
    private readonly List<CompositeForeignKeyImportWarning> _compositeWarnings = new();

    /// <summary>
    /// 直近の <see cref="Build"/> で検出した複合外部キー（列ペア 2 組以上）の劣化警告。
    /// </summary>
    /// <remarks>
    /// <see cref="Build"/> を呼ぶ前は空。表示層はこれを取込結果へ載せて利用者へ提示する。
    /// </remarks>
    public IReadOnlyList<CompositeForeignKeyImportWarning> CompositeForeignKeyWarnings =>
        _compositeWarnings;

    /// <summary>外部キー構成列を 1 行投入する（同一テーブル・同一 <paramref name="fkName"/> の複合列は複数回呼ぶ）</summary>
    /// <param name="fkName">外部キー制約名（テーブルをまたいだ一意性は保証されない）</param>
    /// <param name="parentKey">FK 保有テーブル（子）のテーブルキー</param>
    /// <param name="parentCol">FK 保有テーブル側の構成列名</param>
    /// <param name="refKey">参照先テーブル（親・PK 側）のテーブルキー</param>
    /// <param name="refCol">参照先テーブル側の構成列名</param>
    /// <param name="onDelete">親行削除時の参照アクション（同一 FK の初回行の値を採用する）</param>
    /// <param name="onUpdate">親キー更新時の参照アクション（同一 FK の初回行の値を採用する）</param>
    public void Add(
        string fkName,
        string parentKey,
        string parentCol,
        string refKey,
        string refCol,
        ForeignKeyReferentialAction onDelete,
        ForeignKeyReferentialAction onUpdate
    )
    {
        // 集約キーは「FK 保有テーブル＋制約名」の複合（制約名単体はテーブル横断で一意とは限らないため）
        var groupKey = (parentKey, fkName);

        if (!_grouped.TryGetValue(groupKey, out var g))
        {
            g = (
                parentKey,
                refKey,
                fkName,
                new List<string>(),
                new List<string>(),
                onDelete,
                onUpdate
            );
        }

        g.ParentCols.Add(parentCol);
        g.RefCols.Add(refCol);
        _grouped[groupKey] = g;
    }

    /// <summary>集約済みの外部キーをリレーション一覧へ変換する</summary>
    /// <param name="tables">テーブルキー → 取込中のテーブルエントリ</param>
    /// <param name="uniqueSets">テーブルキー → 主キー以外の一意制約列集合（1 対 1 判定に用いる）</param>
    /// <returns>投入順を保持したリレーション一覧。解決できないテーブル参照はスキップする</returns>
    /// <remarks>
    /// 複合外部キーを検出した場合は <see cref="CompositeForeignKeyWarnings"/> へ記録する
    /// （複数回呼んでも重複しないよう、呼び出しのたびに作り直す）。
    /// </remarks>
    public List<Relationship> Build(
        IReadOnlyDictionary<string, SchemaTableEntry> tables,
        IReadOnlyDictionary<string, List<string[]>> uniqueSets
    )
    {
        var rels = new List<Relationship>();
        _compositeWarnings.Clear();

        foreach (var (_, g) in _grouped)
        {
            if (!tables.TryGetValue(g.ParentKey, out var parent))
            {
                continue;
            }

            if (!tables.TryGetValue(g.RefKey, out var refer))
            {
                continue;
            }

            // 複合外部キーは意味モデルが列対応を表現できず単一リレーションへ劣化するため、警告として記録する
            if (g.ParentCols.Count > 1)
            {
                _compositeWarnings.Add(
                    new CompositeForeignKeyImportWarning(
                        g.ConstraintName,
                        parent.Entity.TableName,
                        g.ParentCols.ToArray(),
                        refer.Entity.TableName,
                        g.RefCols.ToArray()
                    )
                );
            }

            // FK を構成する子側の列に IsForeignKey フラグを立てる
            foreach (var pc in g.ParentCols)
            {
                if (parent.ColumnsByName.TryGetValue(pc, out var pcol))
                {
                    pcol.IsForeignKey = true;
                }
            }

            // FK 列集合が主キーまたは一意制約と一致すれば 1 対 1 とみなす
            var sortedParent = g
                .ParentCols.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var pkCols = parent
                .Entity.Columns.Where(c => c.IsPrimaryKey)
                .Select(c => c.Name)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var uniqueOnParent = uniqueSets.TryGetValue(g.ParentKey, out var sets)
                ? sets
                : new List<string[]>();

            var isOneToOne =
                SameSet(sortedParent, pkCols) || uniqueOnParent.Any(s => SameSet(sortedParent, s));

            rels.Add(
                new Relationship
                {
                    SourceEntityId = refer.Entity.Id, // 参照先 (PK 側) を起点として表示
                    TargetEntityId = parent.Entity.Id, // FK 保有テーブル
                    Type = isOneToOne ? RelationshipType.OneToOne : RelationshipType.OneToMany,
                    SourceColumnId =
                        g.RefCols.Count == 1
                        && refer.ColumnsByName.TryGetValue(g.RefCols[0], out var refColumn)
                            ? refColumn.Id
                            : null,
                    TargetColumnId =
                        g.ParentCols.Count == 1
                        && parent.ColumnsByName.TryGetValue(g.ParentCols[0], out var parentColumn)
                            ? parentColumn.Id
                            : null,
                    ConstraintName = g.ConstraintName,
                    OnDelete = g.OnDelete,
                    OnUpdate = g.OnUpdate,
                }
            );
        }

        return rels;
    }

    /// <summary>2 つのソート済み列名集合が大文字小文字無視で完全一致するか判定する（空集合は不一致）</summary>
    public static bool SameSet(string[] a, string[] b) =>
        a.Length > 0
        && a.Length == b.Length
        && a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// テーブルごとの一意制約（主キー以外）列集合を、制約単位の構成列を投入しながら組み立てる共通ビルダー
/// （1 対 1 判定に用いる。DB 方言横断で同一の集約ロジックを担う）
/// </summary>
/// <remarks>
/// 一意制約 / 一意インデックスの構成列を序数順に読み出しながら <see cref="Add"/> で 1 行ずつ投入し、
/// <see cref="Build"/> で「テーブルキー → 各一意制約の列名配列（大文字小文字無視の昇順）リスト」を得る。
/// 構成列が 1 件も無いテーブルでも、当該テーブルが登場していれば空リストのエントリを保持する。
/// </remarks>
public sealed class UniqueColumnSetBuilder
{
    /// <summary>テーブルキー → 一意制約の空リスト保持（列を持たない制約でもテーブル登場を記録する）</summary>
    private readonly Dictionary<string, List<string[]>> _result = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>(テーブルキー, 制約名) → 構成列（序数順に投入）</summary>
    /// <remarks>
    /// キーはタプルで持つ（文字列連結キーだと、区切り文字を含むテーブル名を
    /// <see cref="Build"/> で分解する際に誤切断され、一意制約集合が別テーブルへ紐付く）。
    /// </remarks>
    private readonly Dictionary<(string TableKey, string ConstraintName), List<string>> _current =
        new();

    /// <summary>一意制約の構成列を 1 行投入する（複合列は同一 <paramref name="constraintName"/> で複数回呼ぶ）</summary>
    /// <param name="tableKey">テーブルキー</param>
    /// <param name="constraintName">一意制約名 / 一意インデックス名</param>
    /// <param name="column">構成列名</param>
    public void Add(string tableKey, string constraintName, string column)
    {
        var compositeKey = (tableKey, constraintName);

        if (!_current.TryGetValue(compositeKey, out var list))
        {
            list = new List<string>();
            _current[compositeKey] = list;
        }

        list.Add(column);

        if (!_result.ContainsKey(tableKey))
        {
            _result[tableKey] = new List<string[]>();
        }
    }

    /// <summary>投入済みの構成列から「テーブルキー → 各一意制約の列名配列リスト」を組み立てる</summary>
    public Dictionary<string, List<string[]>> Build()
    {
        foreach (var kv in _current)
        {
            var tableKey = kv.Key.TableKey;

            if (!_result.TryGetValue(tableKey, out var lists))
            {
                lists = new List<string[]>();
                _result[tableKey] = lists;
            }

            lists.Add(kv.Value.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        return _result;
    }
}
