using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>リレーション作成時に参照先（子）テーブルの外部キー列の既定候補を解決する共通リゾルバ</summary>
/// <remarks>
/// 手動作成・AI 生成・Codex チャットの 3 経路で共用する。優先順位は以下のとおり:
/// <list type="number">
///   <item>参照元テーブル名由来の FK 名（&lt;親テーブル名&gt;Id / &lt;親テーブル名&gt;_id 単数形化も試行）と完全一致</item>
///   <item>参照元キー列と同名</item>
///   <item>外部キーとしてマークされている列</item>
///   <item>①の名前との後方一致（BillingCustomerId のようなロール付き FK 名）</item>
/// </list>
/// いずれにも該当しなければ null（列未割当）とし、無関係な列を外部キー扱いにしない。
/// 主キー列と他リレーションで参照先として使用済みの列は候補から除外し、
/// 同率の場合は参照元キー列とデータ型が一致する列 → 宣言順で決める。
/// 自己参照では Parent 系の名前（ParentId / parent_id / Parent+キー列名）も①の候補へ加える。
/// </remarks>
public static class ForeignKeyColumnResolver
{
    /// <summary>参照先テーブルの列 1 つ分の判定材料</summary>
    /// <param name="Name">カラム名</param>
    /// <param name="IsPrimaryKey">主キーかどうか（true なら候補から除外）</param>
    /// <param name="IsForeignKey">外部キーとしてマーク済みかどうか</param>
    /// <param name="DataType">データ型（同率時のタイブレークに使用）</param>
    /// <param name="IsUsedByOtherRelationship">既に他リレーションの参照先列かどうか（true なら候補から除外）</param>
    public readonly record struct CandidateColumn(string Name, bool IsPrimaryKey, bool IsForeignKey, string? DataType, bool IsUsedByOtherRelationship);

    /// <summary>参照先テーブルの列一覧から外部キー列の既定候補を解決し、その位置を返す</summary>
    /// <param name="sourceTableName">参照元（親）テーブル名</param>
    /// <param name="sourceKeyColumnName">参照元のキー列名（通常は PK 列 無ければ null）</param>
    /// <param name="sourceKeyDataType">参照元キー列のデータ型（タイブレーク用 無ければ null）</param>
    /// <param name="targetColumns">参照先テーブルの全列（宣言順）</param>
    /// <param name="isSelfReference">自己参照リレーションかどうか</param>
    /// <returns><paramref name="targetColumns"/> 内の採用列の位置 候補が無ければ null</returns>
    public static int? ResolveTargetColumnIndex(
        string sourceTableName,
        string? sourceKeyColumnName,
        string? sourceKeyDataType,
        IReadOnlyList<CandidateColumn> targetColumns,
        bool isSelfReference
    )
    {
        var expectedNames = new List<string>(BuildExpectedForeignKeyNames(sourceTableName));

        if (isSelfReference)
        {
            expectedNames.Add("ParentId");
            expectedNames.Add("parent_id");

            if (!string.IsNullOrWhiteSpace(sourceKeyColumnName))
            {
                expectedNames.Add("Parent" + sourceKeyColumnName);
            }
        }

        var eligible = Enumerable.Range(0, targetColumns.Count).Where(i => !targetColumns[i].IsPrimaryKey && !targetColumns[i].IsUsedByOtherRelationship).ToList();

        // 優先ランク（上から順に評価し、最初に候補が見つかったランクで確定する）
        var ranks = new Func<CandidateColumn, bool>[]
        {
            column => expectedNames.Any(name => string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase)),
            column => sourceKeyColumnName is not null && string.Equals(column.Name, sourceKeyColumnName, StringComparison.OrdinalIgnoreCase),
            column => column.IsForeignKey,
            column => expectedNames.Any(name => column.Name.Length > name.Length && column.Name.EndsWith(name, StringComparison.OrdinalIgnoreCase)),
        };

        foreach (var matches in ranks)
        {
            var hits = eligible.Where(i => matches(targetColumns[i])).ToList();

            if (hits.Count == 0)
            {
                continue;
            }

            // 同率はデータ型一致を優先し、残りは宣言順（OrderByDescending は安定ソート）
            return hits.OrderByDescending(i => IsSameDataType(targetColumns[i].DataType, sourceKeyDataType)).First();
        }

        return null;
    }

    /// <summary>参照元キー列を既定（PK 列）として参照先の外部キー列を解決する</summary>
    public static ColumnViewModel? ResolveTargetColumn(EntityViewModel source, EntityViewModel target, IEnumerable<RelationshipViewModel> existingRelationships)
    {
        return ResolveTargetColumn(source, target, source.Columns.FirstOrDefault(c => c.IsPrimaryKey), existingRelationships);
    }

    /// <summary>参照元キー列を指定して参照先の外部キー列を解決する</summary>
    /// <param name="sourceKeyColumn">参照元のキー列（明示指定がある場合はその列、無ければ PK 列）</param>
    /// <param name="existingRelationships">既存リレーション一覧（参照先として使用済みの列を候補から除外するために用いる）</param>
    public static ColumnViewModel? ResolveTargetColumn(
        EntityViewModel source,
        EntityViewModel target,
        ColumnViewModel? sourceKeyColumn,
        IEnumerable<RelationshipViewModel> existingRelationships
    )
    {
        var usedColumnIds = existingRelationships
            .Where(r => ReferenceEquals(r.Target, target) && r.TargetColumnId is not null)
            .Select(r => r.TargetColumnId!.Value)
            .ToHashSet();

        var candidates = target
            .Columns.Select(c => new CandidateColumn(c.Name, c.IsPrimaryKey, c.IsForeignKey, c.DataType, usedColumnIds.Contains(c.Id)))
            .ToList();

        var index = ResolveTargetColumnIndex(source.TableName, sourceKeyColumn?.Name, sourceKeyColumn?.DataType, candidates, ReferenceEquals(source, target));

        return index is null ? null : target.Columns[index.Value];
    }

    /// <summary>参照元テーブル名から参照先テーブルに期待する FK 列名の候補一覧を求める</summary>
    /// <remarks>
    /// パスカルケース（CustomerId）とスネークケース（customer_id）の両方を生成し、
    /// テーブル名が複数形の場合は末尾単語を単数形化した変体（Customers → CustomerId）も加える
    /// </remarks>
    public static IReadOnlyList<string> BuildExpectedForeignKeyNames(string sourceTableName)
    {
        var words = IdentifierNameHelper.SplitIdentifierWords(sourceTableName);

        if (words.Count == 0)
        {
            return [];
        }

        var names = new List<string>();

        void AddVariants(List<string> sourceWords)
        {
            names.Add(string.Concat(sourceWords.Select(IdentifierNameHelper.ToPascalWord)) + "Id");
            names.Add(string.Join("_", sourceWords.Select(static word => word.ToLowerInvariant())) + "_id");
        }

        AddVariants(words);

        var singularLastWord = IdentifierNameHelper.SingularizeWord(words[^1]);

        if (!string.Equals(singularLastWord, words[^1], StringComparison.OrdinalIgnoreCase))
        {
            var singularWords = new List<string>(words) { [^1] = singularLastWord };
            AddVariants(singularWords);
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>2 つのデータ型表記が実質同じかどうかを判定する（空白と大文字小文字の差を無視）</summary>
    private static bool IsSameDataType(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
