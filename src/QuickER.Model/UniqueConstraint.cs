using System.Collections.Generic;

namespace QuickER.Model;

/// <summary>
/// テーブルに定義された一意制約（<c>UNIQUE</c>）1 件を表すモデル
/// JSON シリアライズの対象
/// </summary>
/// <remarks>
/// 構成列は <see cref="Column.Id"/> の一覧で保持し、順序は宣言順（DDL へ出力する列の並び）を表す。
/// 主キーは <see cref="Column.IsPrimaryKey"/> が表現するため、ここには含めない。
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
    public string? Name { get; set; }

    /// <summary>構成列の <see cref="Column.Id"/> 一覧（宣言順）</summary>
    public List<Guid> ColumnIds { get; set; } = new();

    /// <summary>制約名が未設定（<see cref="Name"/> が <c>null</c>）のときに用いる合成名を組み立てる</summary>
    /// <param name="tableName">対象テーブル名</param>
    /// <param name="columnNames">構成列名（宣言順）</param>
    /// <returns><c>UQ_{テーブル名}_{列名…}</c> 形式の合成名</returns>
    /// <remarks>
    /// 「名前なし＝列構成から合成する」は <see cref="Name"/> の意味論そのものなのでモデル側に置く。
    /// 識別子として安全な形への正規化（記号の置換）は方言ごとに異なるため呼び出し側の責務で、
    /// DDL 生成は <c>QuickER.Provider.UniqueConstraintNaming</c> が方言別の安全化を適用したうえで本メソッドを呼ぶ。
    /// </remarks>
    public static string SynthesizeName(string tableName, IEnumerable<string> columnNames) =>
        "UQ_" + tableName + string.Concat(columnNames.Select(column => "_" + column));

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
