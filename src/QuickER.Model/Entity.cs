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

    private string _tableName = "NewTable";

    /// <summary>物理テーブル名（例: <c>Customer</c>）</summary>
    /// <remarks>
    /// <para>
    /// 代入した名前の前後の空白は除く（読込・GUI・ツール・取込のどの経路でも、モデルに入った時点で同じ形にする）。
    /// DB 同期はテーブル名を前後の空白を除いてから扱うため、空白つきの名前のままだと DDL とコード生成は
    /// <c>"Orders "</c> を作り・参照し、同期は <c>"Orders"</c> を探す、という食い違いが起きる。
    /// </para>
    /// <para>
    /// 列名・制約名は除かない。DDL と同期が同じ名前をそのまま使っており、食い違いが起きていたのはテーブル名だけのため。
    /// <c>null</c> はそのまま通す（手で編集した JSON の <c>null</c> は、読込の必須名の検査が場所を名指しして断る）。
    /// </para>
    /// </remarks>
    public string TableName
    {
        get => _tableName;
        set => _tableName = value?.Trim()!;
    }

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

    /// <summary>マージ取込の結果（このテーブル）へ、取込前の同じテーブルが明示していた主キーの順序を引き継ぐ</summary>
    /// <param name="existing">取込前の同じテーブル（Guid 引継で対応づいた現在の図のエンティティ）</param>
    /// <returns>順序を引き継いだとき <c>true</c>（<see cref="PrimaryKeyColumnIds"/> をその場で書き換える）</returns>
    /// <remarks>
    /// <para>
    /// 取込元が主キーの順序を運ばない（または運ばないことがある）取込で、取り込むたびに列宣言順へ戻らないようにする。
    /// 戻ると次の DB 同期で主キーの張り直し（AlterPrimaryKey）が出る。C# コード取込（順序を書いていない古い生成コード）と
    /// Excel 定義書（順序を書く欄が無い）のマージ取込が、同じこの規則を使う。
    /// </para>
    /// <para>
    /// 引き継ぐのは、<paramref name="existing"/> が順序を明示していて（<see cref="PrimaryKeyColumnIds"/> が空でない）、
    /// 主キー列の集合が列名で一致するときだけ。主キーの構成が変わったなら、それは取込元が語った変更なので古い順序を持ち込まない。
    /// 明示していない図は明示しないまま残す（列宣言順のフォールバックを書き込むと順序が固定され、以後の列の並べ替えが
    /// 主キーの順序に反映されなくなる）。
    /// </para>
    /// <para>
    /// 列名は大文字小文字まで一致で引き、列 Id はこのテーブル側の列へ張り替える。主キー列に同じ名前が 2 つ以上あると
    /// どれが誰か決まらないので引き継がない（C# コード取込のパーサーは重複する列名を警告つきで通す）。
    /// </para>
    /// </remarks>
    public bool InheritPrimaryKeyOrderFrom(Entity existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        if (existing.PrimaryKeyColumnIds.Count == 0)
        {
            return false;
        }

        var keys = Columns.Where(column => column.IsPrimaryKey).ToList();

        if (keys.DistinctBy(column => column.Name, StringComparer.Ordinal).Count() != keys.Count)
        {
            return false;
        }

        var keyByName = keys.ToDictionary(column => column.Name, StringComparer.Ordinal);
        var existingOrder = existing.GetPrimaryKeyColumnsInOrder();

        if (
            existingOrder.Count != keyByName.Count
            || existingOrder.Any(column => !keyByName.ContainsKey(column.Name))
        )
        {
            return false;
        }

        PrimaryKeyColumnIds = existingOrder.Select(column => keyByName[column.Name].Id).ToList();
        return true;
    }

    /// <summary>指定した列の集合が、このテーブルの候補キー（主キー、または一意制約のいずれか）と一致するか</summary>
    /// <param name="columnIds">外部キーが参照する親側の列（<see cref="Column.Id"/>）。並びは問わない</param>
    /// <returns>
    /// 主キー列の集合（膜＝<see cref="Column.IsPrimaryKey"/>）と一致するか、いずれかの
    /// <see cref="UniqueConstraint.ColumnIds"/> の集合と一致すれば true。空の集合は false
    /// </returns>
    /// <remarks>
    /// 外部キーは参照先の主キーか一意制約の列とちょうど一致しないと、多くの DBMS が DDL を拒否する
    /// （主キーの一部だけ・一意でない列）。MySQL のように一意でない索引列を参照できる DBMS もあるため、
    /// 呼び出し側はこれを拒否の根拠でなく注意の根拠に使う
    /// </remarks>
    public bool IsCandidateKey(IReadOnlyCollection<Guid> columnIds)
    {
        if (columnIds.Count == 0)
        {
            return false;
        }

        var requested = columnIds.ToHashSet();
        var primaryKey = Columns.Where(column => column.IsPrimaryKey).Select(column => column.Id);

        return requested.SetEquals(primaryKey)
            || UniqueConstraints.Any(constraint => requested.SetEquals(constraint.ColumnIds));
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
