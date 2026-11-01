using System.Collections.Generic;
using QuickER.Dialects;

namespace QuickER.Model;

/// <summary>
/// テーブルに定義された一意制約（<c>UNIQUE</c>）1 件を表すモデル
/// JSON シリアライズの対象
/// </summary>
/// <remarks>
/// 構成列は <see cref="Column.Id"/> の一覧で保持し、順序は宣言順（DDL へ出力する列の並び）を表す。
/// 主キーはどの列が構成列かを <see cref="Column.IsPrimaryKey"/> が、その順序を
/// <see cref="Entity.PrimaryKeyColumnIds"/> が表現するため、ここには含めない。
/// </remarks>
public class UniqueConstraint
{
    /// <summary>一意制約の一意識別子</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>DB から取り込んだ制約名（未設定の場合は <c>null</c>＝DDL 生成時に列構成から合成する）</summary>
    /// <remarks>
    /// SQLite の <c>CREATE TABLE</c> 内 <c>UNIQUE</c> 句は <c>sqlite_autoindex_*</c> という自動名しか持たず
    /// 意味を持たないため、取込時は <c>null</c> にする。
    /// </remarks>
    public string? Name
    {
        get => _name;
        set => _name = value?.Trim();
    }

    private string? _name;

    /// <summary>構成列の <see cref="Column.Id"/> 一覧（宣言順）</summary>
    public List<Guid> ColumnIds { get; set; } = new();

    /// <summary>制約名が未設定（<see cref="Name"/> が <c>null</c>）のときに用いる合成名を組み立てる</summary>
    /// <param name="tableName">対象テーブル名</param>
    /// <param name="columnNames">構成列名（宣言順）</param>
    /// <param name="limit">識別子の長さの上限（どの上限を渡すかの経路は <see cref="ConstraintNames"/> の XmlDoc）</param>
    /// <returns>
    /// <c>UQ_{テーブル名}_{列名…}</c> 形式の合成名（各部品は <see cref="ConstraintNames.SafeName"/> を通したもの。
    /// 上限を超えるときは <see cref="IdentifierLengthLimit.Fit"/> で縮める）
    /// </returns>
    /// <remarks>
    /// 「名前なし＝列構成から合成する」は <see cref="Name"/> の意味論そのものなのでモデル側に置く。
    /// 安全化（前後の空白の除去と記号の置換）もここで行う。安全化は冪等なので、方言の <c>SafeName</c> を先に通して
    /// から呼んでも結果は変わらない。
    /// この名前はほかの制約の名前とぶつかり得るので、DDL・同期・生成コード・ツールの要約は、ぶつかるときに番号を足す
    /// <see cref="ConstraintNames.ResolveUniqueConstraintNames"/> を通す（直接呼ぶのはその解決の関数だけ）。
    /// </remarks>
    public static string SynthesizeName(
        string tableName,
        IEnumerable<string> columnNames,
        IdentifierLengthLimit limit
    ) => limit.Fit(UnshortenedName(tableName, columnNames));

    /// <summary><see cref="SynthesizeName"/> の、上限に収める前の名前</summary>
    internal static string UnshortenedName(string tableName, IEnumerable<string> columnNames) =>
        "UQ_"
        + ConstraintNames.SafeName(tableName)
        + string.Concat(columnNames.Select(column => "_" + ConstraintNames.SafeName(column)));

    /// <summary>構成列の Id を、<paramref name="owner"/> の列名（宣言順）へ解決する</summary>
    /// <param name="owner">この制約を持つテーブル</param>
    /// <param name="columnNames">解決できた場合の構成列名（宣言順）。失敗時は空リスト</param>
    /// <returns>構成列が 1 つ以上あり、そのすべてを解決できたら <c>true</c>（できない制約は DDL が出さない）</returns>
    public bool TryResolveColumnNames(Entity owner, out List<string> columnNames)
    {
        columnNames = [];

        if (ColumnIds.Count == 0)
        {
            return false;
        }

        foreach (var columnId in ColumnIds)
        {
            var column = owner.Columns.FirstOrDefault(c => c.Id == columnId);

            if (column is null)
            {
                columnNames = [];
                return false;
            }

            columnNames.Add(column.Name);
        }

        return true;
    }

    /// <summary>一意制約を複製する</summary>
    /// <param name="preserveId"><c>true</c> の場合は同じ ID を維持し、<c>false</c> の場合は新しい ID を割り当てる</param>
    /// <param name="columnIdMap">
    /// 旧カラム ID → 新カラム ID の対応表（<see cref="Entity.Clone"/> がカラムを新 ID で複製した場合に渡す）。
    /// <c>null</c> または対応が無い ID はそのまま維持する
    /// </param>
    /// <returns>複製された <see cref="UniqueConstraint"/></returns>
    public UniqueConstraint Clone(
        bool preserveId,
        IReadOnlyDictionary<Guid, Guid>? columnIdMap = null
    ) =>
        new()
        {
            Id = preserveId ? Id : Guid.NewGuid(),
            Name = Name,
            ColumnIds = ColumnIds
                .Select(id =>
                    columnIdMap is not null && columnIdMap.TryGetValue(id, out var mapped)
                        ? mapped
                        : id
                )
                .ToList(),
        };
}
