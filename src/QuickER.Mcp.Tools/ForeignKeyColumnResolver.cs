using QuickER.Model;

namespace QuickER.Mcp.Tools;

/// <summary>
/// リレーション作成時に参照先（子）テーブルの外部キー列の既定候補を解決する（Model 版）。
/// </summary>
/// <remarks>
/// GUI 側 <c>QuickER.Services.ForeignKeyColumnResolver</c> の ViewModel 非依存な中核ロジックを、
/// <see cref="Column"/> / <see cref="Entity"/> / <see cref="Relationship"/>（意味モデル）に対して再現した移植。
/// ファイルベースの MCP ホストが GUI と同一の外部キー列既定解決を返すために持つ（優先順位も同一）:
/// <list type="number">
///   <item>参照元テーブル名由来の FK 名（&lt;親テーブル名&gt;Id / &lt;親テーブル名&gt;_id・単数形化も試行）と完全一致</item>
///   <item>参照元キー列と同名</item>
///   <item>外部キーとしてマークされている列</item>
///   <item>①の名前との後方一致（BillingCustomerId のようなロール付き FK 名）</item>
/// </list>
/// いずれにも該当しなければ null（列未割当）とし、無関係な列を外部キー扱いにしない。
/// 主キー列と他リレーションで参照先として使用済みの列は候補から除外し、
/// 同率の場合は参照元キー列とデータ型が一致する列 → 宣言順で決める。
/// 将来 GUI 版とともに Model 層へ共通化する余地があるが、現時点では参照制約（Mcp.Tools は Model/Document/Mcp のみ参照）を優先し移植で対応する。
/// </remarks>
internal static class ForeignKeyColumnResolver
{
    /// <summary>親の主キー全列を順に対応付けた既定の列ペア一覧を組み立てる</summary>
    /// <param name="source">参照元（親）エンティティ</param>
    /// <param name="target">参照先（子）エンティティ</param>
    /// <param name="existingRelationships">既存リレーション一覧（参照先として使用済みの列を候補から除外するために用いる）</param>
    /// <remarks>
    /// GUI の作成フロー（<c>MainViewModel.BuildInitialColumnPairs</c>）と同一の意味論。親の主キー列を宣言順に
    /// 辿り、列ごとに <see cref="ResolveTargetColumn"/> で子列を引き当てる。引き当てられなかった列はペアに
    /// 含めず、複数の親列が同じ子列へ寄った場合は後続をペアなしにする（1 つの子列を 2 度使う外部キーは作れない）
    /// </remarks>
    public static List<RelationshipColumnPair> ResolveColumnPairs(
        Entity source,
        Entity target,
        IEnumerable<Relationship> existingRelationships
    )
    {
        var relationships = existingRelationships.ToList();
        var pairs = new List<RelationshipColumnPair>();
        var usedTargetColumnIds = new HashSet<Guid>();

        foreach (var sourceKeyColumn in source.Columns.Where(column => column.IsPrimaryKey))
        {
            var targetColumn = ResolveTargetColumn(source, target, sourceKeyColumn, relationships);

            if (targetColumn is null || !usedTargetColumnIds.Add(targetColumn.Id))
            {
                continue;
            }

            pairs.Add(new RelationshipColumnPair(sourceKeyColumn.Id, targetColumn.Id));
        }

        return pairs;
    }

    /// <summary>参照元キー列を指定して参照先の外部キー列を解決する</summary>
    /// <param name="source">参照元（親）エンティティ</param>
    /// <param name="target">参照先（子）エンティティ</param>
    /// <param name="sourceKeyColumn">参照元のキー列（明示指定がある場合はその列、無ければ PK 列）</param>
    /// <param name="existingRelationships">既存リレーション一覧（参照先として使用済みの列を候補から除外するために用いる）</param>
    /// <returns>解決した参照先の外部キー列。候補が無ければ null</returns>
    public static Column? ResolveTargetColumn(
        Entity source,
        Entity target,
        Column? sourceKeyColumn,
        IEnumerable<Relationship> existingRelationships
    )
    {
        // 既存リレーションの全構成列（複合外部キーなら 2 列以上）を使用済みとして候補から外す
        var usedColumnIds = existingRelationships
            .Where(r => r.TargetEntityId == target.Id)
            .SelectMany(r => r.ColumnPairs)
            .Select(pair => pair.TargetColumnId)
            .ToHashSet();

        var index = ResolveTargetColumnIndex(
            source.TableName,
            sourceKeyColumn?.Name,
            sourceKeyColumn?.DataType,
            target.Columns,
            usedColumnIds,
            ReferenceEquals(source, target) || source.Id == target.Id
        );

        return index is null ? null : target.Columns[index.Value];
    }

    /// <summary>参照先テーブルの列一覧から外部キー列の既定候補を解決し、その位置を返す</summary>
    private static int? ResolveTargetColumnIndex(
        string sourceTableName,
        string? sourceKeyColumnName,
        string? sourceKeyDataType,
        IReadOnlyList<Column> targetColumns,
        HashSet<Guid> usedColumnIds,
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

        var eligible = Enumerable
            .Range(0, targetColumns.Count)
            .Where(i =>
                !targetColumns[i].IsPrimaryKey && !usedColumnIds.Contains(targetColumns[i].Id)
            )
            .ToList();

        // 優先ランク（上から順に評価し、最初に候補が見つかったランクで確定する）
        var ranks = new Func<Column, bool>[]
        {
            column =>
                expectedNames.Any(name =>
                    string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase)
                ),
            column =>
                sourceKeyColumnName is not null
                && string.Equals(
                    column.Name,
                    sourceKeyColumnName,
                    StringComparison.OrdinalIgnoreCase
                ),
            column => column.IsForeignKey,
            column =>
                expectedNames.Any(name =>
                    column.Name.Length > name.Length
                    && column.Name.EndsWith(name, StringComparison.OrdinalIgnoreCase)
                ),
        };

        foreach (var matches in ranks)
        {
            var hits = eligible.Where(i => matches(targetColumns[i])).ToList();

            if (hits.Count == 0)
            {
                continue;
            }

            // 同率はデータ型一致を優先し、残りは宣言順（OrderByDescending は安定ソート）
            return hits.OrderByDescending(i =>
                    IsSameDataType(targetColumns[i].DataType, sourceKeyDataType)
                )
                .First();
        }

        return null;
    }

    /// <summary>参照元テーブル名から参照先テーブルに期待する FK 列名の候補一覧を求める</summary>
    /// <remarks>
    /// パスカルケース（CustomerId）とスネークケース（customer_id）の両方を生成し、
    /// テーブル名が複数形の場合は末尾単語を単数形化した変体（Customers → CustomerId）も加える
    /// </remarks>
    private static IReadOnlyList<string> BuildExpectedForeignKeyNames(string sourceTableName)
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
            names.Add(
                string.Join("_", sourceWords.Select(static word => word.ToLowerInvariant())) + "_id"
            );
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
