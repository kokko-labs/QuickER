using System.Collections.Generic;

namespace QuickER.Model;

/// <summary>
/// ER 図上のテーブル（エンティティ）1 件を表すモデル
/// JSON シリアライズの対象
/// </summary>
public class Entity
{
    /// <summary>エンティティの一意識別子（<see cref="Relationship"/> からの参照に使用する）</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>物理テーブル名（例: <c>Customer</c>）</summary>
    public string TableName { get; set; } = "NewTable";

    /// <summary>備考メモ（プロパティパネルから編集する）</summary>
    public string Memo { get; set; } = string.Empty;

    /// <summary>テーブルの説明（SQL Server の拡張プロパティ <c>MS_Description</c> と同期する）</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>エンティティに属するカラム一覧</summary>
    public List<Column> Columns { get; set; } = new();

    /// <summary>テーブルに定義された一意制約（主キーを除く）の一覧</summary>
    public List<UniqueConstraint> UniqueConstraints { get; set; } = [];

    /// <summary>主キー構成列の順序（<see cref="Column.Id"/> の一覧・DDL の <c>PRIMARY KEY</c> 句へ出力する並び）</summary>
    /// <remarks>
    /// どの列が主キーかの正本は <see cref="Column.IsPrimaryKey"/> で、このリストは順序の上書き情報。
    /// 空のときは列宣言順（<see cref="Columns"/> の並び）を主キーの順序とする。
    /// 実効順の解決は <see cref="GetPrimaryKeyColumnsInOrder"/> が行う。
    /// </remarks>
    public List<Guid> PrimaryKeyColumnIds { get; set; } = new();

    /// <summary>主キー構成列を実効順（DDL の <c>PRIMARY KEY</c> 句へ出力する並び）で取り出す</summary>
    /// <returns><see cref="Column.IsPrimaryKey"/> が <c>true</c> の列を実効順に並べたリスト</returns>
    /// <remarks>
    /// <see cref="PrimaryKeyColumnIds"/> 内の位置（無ければ末尾扱い）を第 1 キー、列宣言順を第 2 キーとする安定ソートで並べる
    /// （<c>OrderBy</c> は安定ソートのため、同順位の列は <see cref="Columns"/> の並びがそのまま残る）。
    /// この規則から次が導かれる:
    /// リストに載っていない主キー列は列宣言順のまま末尾へ回り、
    /// リストに載っていても主キーでない列は対象そのものから外れ、
    /// このエンティティの列を指さない ID はどの列の位置にもならないため無視され、
    /// リストが空なら全体が列宣言順になる。
    /// </remarks>
    public List<Column> GetPrimaryKeyColumnsInOrder()
    {
        // 列 ID → 順序リスト内の位置。重複 ID は先勝ち（先に現れた位置を採る）
        var orderById = new Dictionary<Guid, int>(PrimaryKeyColumnIds.Count);

        for (var i = 0; i < PrimaryKeyColumnIds.Count; i++)
        {
            orderById.TryAdd(PrimaryKeyColumnIds[i], i);
        }

        return Columns
            .Where(c => c.IsPrimaryKey)
            .OrderBy(c => orderById.TryGetValue(c.Id, out var index) ? index : int.MaxValue)
            .ToList();
    }

    /// <summary>主キーの実効順が列宣言順と食い違うときに限り、実効順の主キー列名を返す</summary>
    /// <returns>食い違う場合は <see cref="GetPrimaryKeyColumnsInOrder"/> の列名一覧、一致する場合は <c>null</c></returns>
    /// <remarks>
    /// 順序を明示する必要があるエンティティだけを告知するための判定
    /// （食い違いが無いときは列の並びがそのまま主キーの並びであり、重ねて示す情報が無い）。
    /// </remarks>
    public List<string>? GetReorderedPrimaryKeyColumnNames()
    {
        var ordered = GetPrimaryKeyColumnsInOrder();
        var declared = Columns.Where(c => c.IsPrimaryKey);

        // 実効順は宣言順の安定ソートなので、参照の並びが一致するかどうかだけで食い違いを判定できる
        return ordered.SequenceEqual(declared) ? null : ordered.Select(c => c.Name).ToList();
    }

    /// <summary>カラム・一意制約も含めてエンティティを複製する</summary>
    /// <param name="preserveId"><c>true</c> の場合は同じ ID を維持し、<c>false</c> の場合は新しい ID を割り当てる（カラム・一意制約にも同様に適用される）</param>
    /// <returns>複製された <see cref="Entity"/></returns>
    /// <remarks>
    /// <paramref name="preserveId"/> が <c>false</c> のときはカラム ID も新規採番されるため、
    /// 一意制約の <see cref="UniqueConstraint.ColumnIds"/> と <see cref="PrimaryKeyColumnIds"/> は
    /// 複製後のカラム ID へ張り替える
    /// （張り替えないと複製側の制約・主キー順序が元エンティティのカラムを指し続け、参照が壊れる）。
    /// </remarks>
    public Entity Clone(bool preserveId)
    {
        // 先にカラムを複製し、旧 ID → 新 ID の対応表を作る（一意制約の張り替えに使う）
        var columns = new List<Column>(Columns.Count);
        var columnIdMap = new Dictionary<Guid, Guid>(Columns.Count);

        foreach (var column in Columns)
        {
            var cloned = column.Clone(preserveId);
            columns.Add(cloned);
            columnIdMap[column.Id] = cloned.Id;
        }

        return new Entity
        {
            Id = preserveId ? Id : Guid.NewGuid(),
            TableName = TableName,
            Memo = Memo,
            Description = Description,
            Columns = columns,
            UniqueConstraints = UniqueConstraints
                .Select(constraint => constraint.Clone(preserveId, columnIdMap))
                .ToList(),
            // 主キーの順序も列 ID 参照のため対応表で張り替える（対応が無い ID はそのまま維持）
            PrimaryKeyColumnIds = PrimaryKeyColumnIds
                .Select(id => columnIdMap.TryGetValue(id, out var mapped) ? mapped : id)
                .ToList(),
        };
    }
}
