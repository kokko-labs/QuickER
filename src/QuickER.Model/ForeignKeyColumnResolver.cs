namespace QuickER.Model;

/// <summary>リレーション作成時に参照先（子）テーブルの外部キー列の既定候補を解決する共通リゾルバ</summary>
/// <remarks>
/// <para>
/// GUI のリレーション作成・内蔵チャットと MCP の <c>add_relationship</c>（列を省略したとき）・
/// Mermaid の取込が共有する唯一の実装。GUI は ViewModel を <c>ToModel()</c> で意味モデルへ写して渡す
/// （列の Id はそのまま運ばれるので、返る列ペアは ViewModel の列を指す）。
/// どの経路からも参照できるよう、<see cref="ConstraintNames"/> と同じくモデル側に置く。
/// </para>
/// <para>
/// 1 列ごとの優先順位は以下のとおり:
/// <list type="number">
///   <item>参照元テーブル名由来の FK 名（&lt;親テーブル名&gt;Id / &lt;親テーブル名&gt;_id 単数形化も試行）と完全一致</item>
///   <item>参照元キー列と同名</item>
///   <item>外部キーとしてマークされている列</item>
///   <item>①の名前との後方一致（BillingCustomerId のようなロール付き FK 名）</item>
/// </list>
/// いずれにも該当しなければ null（列未割当）とし、無関係な列を外部キー扱いにしない。
/// 他リレーションで参照先として使用済みの列は候補から除外し、
/// 同率の場合は参照元キー列とデータ型が一致する列 → 宣言順で決める。
/// 自己参照では Parent 系の名前（ParentId / parent_id / Parent+キー列名）も①の候補へ加える。
/// </para>
/// <para>
/// 子の主キー列を候補にするのは、名前が強く一致するときだけ: ①（親テーブル名から導いた名前）と、
/// 複合主キーの親で同名の子列を先に割り当てる段（<see cref="ResolveColumnPairs"/>。名前が単独の <c>id</c> の列は除く）。注文明細の主キー
/// <c>(OrderId, LineNo)</c> の <c>OrderId</c> のような識別関係の外部キー列は、ここで拾う。
/// ②〜④では主キー列を外す（単一主キーの親の②で許すと、子の主キーが単独の <c>id</c> のときに親の <c>id</c> と
/// 取り違える）。自己参照ではどの段でも外す（①の名前が自分自身の主キーを指すため）。
/// </para>
/// </remarks>
public static class ForeignKeyColumnResolver
{
    /// <summary>参照先テーブルの列 1 つ分の判定材料</summary>
    /// <param name="Name">カラム名</param>
    /// <param name="IsPrimaryKey">主キーかどうか（true なら①の名前一致のときだけ候補にする。自己参照では除外）</param>
    /// <param name="IsForeignKey">外部キーとしてマーク済みかどうか</param>
    /// <param name="DataType">データ型（同率時のタイブレークに使用）</param>
    /// <param name="IsUsedByOtherRelationship">既に他リレーション（またはこのペア化の先の列）の参照先列かどうか（true なら候補から除外）</param>
    public readonly record struct CandidateColumn(
        string Name,
        bool IsPrimaryKey,
        bool IsForeignKey,
        string? DataType,
        bool IsUsedByOtherRelationship
    );

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

        // 優先ランク（上から順に評価し、最初に候補が見つかったランクで確定する）。
        // 子の主キー列を候補にするのは①だけ（識別関係の外部キー列。自己参照では自分の主キーを指すので外す）
        var ranks = new (Func<CandidateColumn, bool> Matches, bool AllowPrimaryKey)[]
        {
            (
                column =>
                    expectedNames.Any(name =>
                        string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase)
                    ),
                !isSelfReference
            ),
            (column => IsSameName(column, sourceKeyColumnName), false),
            (column => column.IsForeignKey, false),
            (
                column =>
                    expectedNames.Any(name =>
                        column.Name.Length > name.Length
                        && column.Name.EndsWith(name, StringComparison.OrdinalIgnoreCase)
                    ),
                false
            ),
        };

        foreach (var (matches, allowPrimaryKey) in ranks)
        {
            var index = PickBest(targetColumns, matches, sourceKeyDataType, allowPrimaryKey);

            if (index is not null)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>親の主キー全列を順に対応付けた既定の列ペア一覧を組み立てる</summary>
    /// <param name="source">参照元（親）エンティティ</param>
    /// <param name="target">参照先（子）エンティティ</param>
    /// <param name="existingRelationships">既存リレーション一覧（参照先として使用済みの列を候補から除外するために用いる）</param>
    /// <remarks>
    /// <para>
    /// 親の主キー列を実効順（<see cref="Entity.GetPrimaryKeyColumnsInOrder"/>）で辿り、列ごとに子列を引き当てる。
    /// 選んだ子列はその時点で候補から外す（1 つの子列を 2 度使う外部キーは作れない）。
    /// 引き当てられなかった親列はペアに含めない。返す列ペアの並びは主キーの実効順。
    /// </para>
    /// <para>
    /// 主キーが 2 列以上のときは、先に全主キー列を「同名の子列」だけで割り当て（②を先に見る）、残った列を
    /// ①〜④の順位で解く。①（親テーブル名由来の名前）を先に見ると、<c>Orders(OrderId, Revision)</c> の
    /// 2 列目も <c>OrderId</c> を引き、<c>Projects(TenantId, ProjectId)</c> では <c>TenantId</c> が子の
    /// <c>ProjectId</c> を取ってしまうため。同名の割り当てを全列について先に済ませるのは、前の列が下位の順位
    /// （③の外部キー印など）で後ろの列の同名の子列を奪わないようにするため。
    /// 主キーが 1 列のときは同名の割り当てを先に行わず、①〜④の順位だけで決める。
    /// </para>
    /// <para>
    /// 同名を先に割り当てる段では子の主キー列も候補にする（自己参照を除く）。<c>Projects(TenantId, ProjectId)</c> から
    /// <c>Tasks(TenantId, ProjectId, TaskId)</c>（3 列とも主キー）への識別関係が、2 列ともペアになる。
    /// ただし名前が単独の <c>id</c> の子の主キー列は除く。<c>id</c> という列はそれぞれのテーブル自身の代理キーを指すので
    /// （<c>Projects.Id</c> はプロジェクトの、<c>Tasks.Id</c> はタスクの番号）、同名でも対応しない
    /// （単一主キーの親で、子の主キーが単独の <c>id</c> のときに対応づけないのと同じ線引き）。
    /// <c>Projects(TenantId, Id)</c> から <c>Tasks(TenantId, Id, ProjectId)</c> へは、<c>Id</c> が残りの順位（①の <c>ProjectId</c>）で解かれる。
    /// </para>
    /// </remarks>
    public static List<RelationshipColumnPair> ResolveColumnPairs(
        Entity source,
        Entity target,
        IEnumerable<Relationship> existingRelationships
    )
    {
        // 既存リレーションの全構成列（複合外部キーなら 2 列以上）を使用済みとして候補から外す
        var usedColumnIds = existingRelationships
            .Where(relationship => relationship.TargetEntityId == target.Id)
            .SelectMany(relationship => relationship.ColumnPairs)
            .Select(pair => pair.TargetColumnId)
            .ToHashSet();
        var keyColumns = source.GetPrimaryKeyColumnsInOrder();
        var chosen = new Column?[keyColumns.Count];

        void Choose(int keyIndex, int? targetIndex)
        {
            if (targetIndex is null)
            {
                return;
            }

            var column = target.Columns[targetIndex.Value];
            chosen[keyIndex] = column;
            usedColumnIds.Add(column.Id);
        }

        if (keyColumns.Count > 1)
        {
            var isSelfReference = source.Id == target.Id;

            for (var i = 0; i < keyColumns.Count; i++)
            {
                var keyColumn = keyColumns[i];
                Choose(
                    i,
                    PickBest(
                        BuildCandidates(target, usedColumnIds),
                        column =>
                            IsSameName(column, keyColumn.Name)
                            && !(column.IsPrimaryKey && IsBareId(column.Name)),
                        keyColumn.DataType,
                        allowPrimaryKey: !isSelfReference
                    )
                );
            }
        }

        for (var i = 0; i < keyColumns.Count; i++)
        {
            if (chosen[i] is not null)
            {
                continue;
            }

            Choose(
                i,
                ResolveTargetColumnIndex(
                    source.TableName,
                    keyColumns[i].Name,
                    keyColumns[i].DataType,
                    BuildCandidates(target, usedColumnIds),
                    source.Id == target.Id
                )
            );
        }

        var pairs = new List<RelationshipColumnPair>();

        for (var i = 0; i < keyColumns.Count; i++)
        {
            if (chosen[i] is { } targetColumn)
            {
                pairs.Add(new RelationshipColumnPair(keyColumns[i].Id, targetColumn.Id));
            }
        }

        return pairs;
    }

    /// <summary>参照元テーブル名から参照先テーブルに期待する FK 列名の候補一覧を求める</summary>
    /// <remarks>
    /// パスカルケース（CustomerId）とスネークケース（customer_id）の両方を生成し、
    /// テーブル名が複数形の場合は末尾単語を単数形化した変体（Customers → CustomerId）も加える。
    /// 単数形が語尾から 1 つに決まらない語は、<see cref="IdentifierNameHelper.SingularizeCandidates"/> の全候補の変体を加える
    /// （Courses → CourseId と CoursId）
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
            names.Add(
                string.Join("_", sourceWords.Select(static word => word.ToLowerInvariant())) + "_id"
            );
        }

        AddVariants(words);

        // 単数形が語尾から 1 つに決まらない語（courses / statuses）は、候補をすべて期待名に加える
        foreach (var singularLastWord in IdentifierNameHelper.SingularizeCandidates(words[^1]))
        {
            if (!string.Equals(singularLastWord, words[^1], StringComparison.OrdinalIgnoreCase))
            {
                var singularWords = new List<string>(words) { [^1] = singularLastWord };
                AddVariants(singularWords);
            }
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>参照先テーブルの列を、使用済みの判定を付けた判定材料へ写す</summary>
    private static List<CandidateColumn> BuildCandidates(
        Entity target,
        HashSet<Guid> usedColumnIds
    ) =>
        target
            .Columns.Select(column => new CandidateColumn(
                column.Name,
                column.IsPrimaryKey,
                column.IsForeignKey,
                column.DataType,
                usedColumnIds.Contains(column.Id)
            ))
            .ToList();

    /// <summary>候補（使用済みと、許さないときは主キーを除く）のうち条件に合う列を 1 つ選び、その位置を返す</summary>
    /// <param name="targetColumns">参照先テーブルの列の判定材料</param>
    /// <param name="matches">この段の一致条件</param>
    /// <param name="sourceKeyDataType">参照元キー列のデータ型（タイブレーク用）</param>
    /// <param name="allowPrimaryKey">子の主キー列も候補にするか（名前が強く一致する段だけ true）</param>
    /// <returns>条件に合う列が無ければ null。複数あればデータ型が一致する列 → 宣言順で決める</returns>
    private static int? PickBest(
        IReadOnlyList<CandidateColumn> targetColumns,
        Func<CandidateColumn, bool> matches,
        string? sourceKeyDataType,
        bool allowPrimaryKey
    )
    {
        var hits = Enumerable
            .Range(0, targetColumns.Count)
            .Where(i =>
                (allowPrimaryKey || !targetColumns[i].IsPrimaryKey)
                && !targetColumns[i].IsUsedByOtherRelationship
                && matches(targetColumns[i])
            )
            .ToList();

        if (hits.Count == 0)
        {
            return null;
        }

        // 同率はデータ型一致を優先し、残りは宣言順（OrderByDescending は安定ソート）
        return hits.OrderByDescending(i =>
                IsSameDataType(targetColumns[i].DataType, sourceKeyDataType)
            )
            .First();
    }

    /// <summary>列名がテーブル自身の代理キーを表す単独の <c>id</c> か（大文字小文字は区別しない）</summary>
    private static bool IsBareId(string columnName) =>
        string.Equals(columnName, "id", StringComparison.OrdinalIgnoreCase);

    /// <summary>候補列が参照元キー列と同名か（大文字小文字は区別しない）</summary>
    private static bool IsSameName(CandidateColumn column, string? sourceKeyColumnName) =>
        sourceKeyColumnName is not null
        && string.Equals(column.Name, sourceKeyColumnName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 2 つのデータ型表記が同じ型宣言を表すかを判定する（前後の空白と大文字小文字の差を無視。
    /// 規則は <see cref="DbTypeText.AreEquivalent"/>。どちらかが空なら一致としない）
    /// </summary>
    private static bool IsSameDataType(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && DbTypeText.AreEquivalent(left, right);
}
