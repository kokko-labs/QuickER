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
/// 参照先（子側 FK 保有テーブル）の FK 列集合が、そのテーブルの主キーまたは一意制約
/// （エンティティに載った <see cref="Entity.UniqueConstraints"/>）と一致する場合は
/// 1 対 1、それ以外は 1 対多と判定する。生成されるリレーションは参照先（PK 側）を起点、
/// FK 保有テーブルを終点として表現する。
/// </para>
/// <para>
/// <see cref="Build"/> は投入順（=最初に各「FK 保有テーブル＋制約名」が出現した順）を保ったリレーション一覧を返す。
/// </para>
/// <para>
/// 複合外部キー（列ペアが 2 組以上）も <see cref="Relationship.ColumnPairs"/> へ全ペアをそのまま載せる
/// （意味モデルが複数列に対応したため、劣化は起きない）。
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
    /// <returns>
    /// 投入順を保持したリレーション一覧。テーブル参照・構成列のいずれかを解決できない外部キーはスキップする
    /// </returns>
    /// <remarks>
    /// <para>
    /// 1 対 1 判定に用いる一意制約は、エンティティに載った <see cref="Entity.UniqueConstraints"/> を参照する。
    /// そのため本メソッドの呼び出し前に <see cref="UniqueConstraintImportBuilder.Attach"/> を済ませておくこと。
    /// </para>
    /// <para>
    /// 集約した構成列は全ペアを <see cref="Relationship.ColumnPairs"/> へ宣言順で載せる。1 列でも
    /// 列 ID を解決できない外部キーは、劣化した定義を作らないようリレーションごとスキップする
    /// （DDL 生成・差分計算が「列ペアが正本・推測フォールバックなし」で動くのと同じ流儀）。
    /// </para>
    /// </remarks>
    public List<Relationship> Build(IReadOnlyDictionary<string, SchemaTableEntry> tables)
    {
        var rels = new List<Relationship>();

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

            // 集約済みの構成列（序数順）をそのまま列ペアへ変換する
            var columnPairs = ResolveColumnPairs(g.ParentCols, parent, g.RefCols, refer);

            if (columnPairs is null)
            {
                continue;
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
            var uniqueOnParent = ResolveUniqueColumnSets(parent.Entity);

            var isOneToOne =
                SameSet(sortedParent, pkCols) || uniqueOnParent.Any(s => SameSet(sortedParent, s));

            rels.Add(
                new Relationship
                {
                    SourceEntityId = refer.Entity.Id, // 参照先 (PK 側) を起点として表示
                    TargetEntityId = parent.Entity.Id, // FK 保有テーブル
                    Type = isOneToOne ? RelationshipType.OneToOne : RelationshipType.OneToMany,
                    ColumnPairs = columnPairs,
                    ConstraintName = g.ConstraintName,
                    OnDelete = g.OnDelete,
                    OnUpdate = g.OnUpdate,
                }
            );
        }

        return rels;
    }

    /// <summary>集約済みの構成列名（序数順）を、列 ID の列ペア一覧へ変換する</summary>
    /// <param name="childCols">FK 保有テーブル（子）側の構成列名（序数順）</param>
    /// <param name="child">FK 保有テーブル（子）のエントリ</param>
    /// <param name="parentCols">参照先テーブル（親）側の構成列名（序数順）</param>
    /// <param name="parent">参照先テーブル（親）のエントリ</param>
    /// <returns>
    /// 全構成列を解決できた場合は列ペア一覧。1 列でも解決できない、または構成列が 0 件なら <c>null</c>
    /// （＝この外部キーは取り込まない）
    /// </returns>
    private static List<RelationshipColumnPair>? ResolveColumnPairs(
        IReadOnlyList<string> childCols,
        SchemaTableEntry child,
        IReadOnlyList<string> parentCols,
        SchemaTableEntry parent
    )
    {
        // 構成列の対応が取れない（列数不一致・列なし）外部キーは復元できないため取り込まない
        if (childCols.Count == 0 || childCols.Count != parentCols.Count)
        {
            return null;
        }

        var pairs = new List<RelationshipColumnPair>(childCols.Count);

        for (var i = 0; i < childCols.Count; i++)
        {
            if (
                !parent.ColumnsByName.TryGetValue(parentCols[i], out var parentColumn)
                || !child.ColumnsByName.TryGetValue(childCols[i], out var childColumn)
            )
            {
                return null;
            }

            pairs.Add(new RelationshipColumnPair(parentColumn.Id, childColumn.Id));
        }

        return pairs;
    }

    /// <summary>エンティティの一意制約を「大文字小文字無視の昇順に並べた列名配列」の一覧へ展開する</summary>
    /// <remarks>
    /// 判定は集合一致（順序不問）なので、<see cref="SameSet"/> が比較できるようソートして返す。
    /// カラム ID を解決できない制約は判定材料から除外する。
    /// </remarks>
    private static List<string[]> ResolveUniqueColumnSets(Entity entity)
    {
        var sets = new List<string[]>();

        if (entity.UniqueConstraints.Count == 0)
        {
            return sets;
        }

        var columnNamesById = new Dictionary<Guid, string>();

        foreach (var column in entity.Columns)
        {
            columnNamesById[column.Id] = column.Name;
        }

        foreach (var constraint in entity.UniqueConstraints)
        {
            var names = new List<string>(constraint.ColumnIds.Count);
            var allResolved = true;

            foreach (var columnId in constraint.ColumnIds)
            {
                if (!columnNamesById.TryGetValue(columnId, out var name))
                {
                    allResolved = false;
                    break;
                }

                names.Add(name);
            }

            if (!allResolved || names.Count == 0)
            {
                continue;
            }

            sets.Add(names.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        return sets;
    }

    /// <summary>2 つのソート済み列名集合が大文字小文字無視で完全一致するか判定する（空集合は不一致）</summary>
    public static bool SameSet(string[] a, string[] b) =>
        a.Length > 0
        && a.Length == b.Length
        && a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase);
}
