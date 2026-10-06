namespace QuickER.Model;

/// <summary>
/// 制約名の安全化と、名前を持たない制約の既定名（外部キー・主キー）を組み立てる規則の正本
/// </summary>
/// <remarks>
/// <para>
/// DDL 生成・DB 同期・GUI のリレーション作成・内蔵チャットと MCP のツールが、同じテーブルに対して同じ名前を
/// 作らなければならない。とくに同期は「DDL が作ったはずの名前」で制約を探して外すので、片方だけ規則が
/// ずれると、存在しない名前の制約を外そうとして失敗する。
/// </para>
/// <para>
/// <b>既定名はここのメソッドで組み立てること</b>（<c>$"FK_{…}"</c> / <c>$"PK_{…}"</c> の補間や
/// <c>"FK_" + …</c> の連結を呼び出し側で書かない）。規則が写されるとそれぞれが別々に古くなるため、
/// 写しが無いことは <c>ConstraintNamesGuardTests</c> がソースを走査して確かめる。
/// 一意制約の合成名（<see cref="UniqueConstraint.SynthesizeName"/>）も同じ <see cref="SafeName"/> を通す。
/// </para>
/// <para>
/// モデル側に置くのは、Provider を参照しない MCP ツールホストも同じ規則を使うため
/// （<see cref="UniqueConstraint.SynthesizeName"/> と同じ理由）。
/// 識別子の囲み方（クォート）は方言ごとに本当に違うので、ここへは入れず各方言に残す。
/// </para>
/// </remarks>
public static class ConstraintNames
{
    /// <summary>名前を制約名の部品として安全な形へ整える</summary>
    /// <param name="name">テーブル名・列名（<c>null</c> は空文字として扱う）</param>
    /// <returns>前後の空白を除き、<c>.</c> と空白を <c>_</c> へ置き換えた名前</returns>
    /// <remarks>
    /// 前後の空白を先に除くのは、同期の計画がテーブル名を <c>Trim</c> してから制約名を組み立てるため
    /// （除かないと先頭の空白が <c>_</c> になり、DDL の名前と一致しない）。
    /// 冪等（2 回通しても変わらない）なので、安全化済みの名前をもう一度通しても結果は同じ。
    /// </remarks>
    public static string SafeName(string? name) =>
        (name ?? string.Empty).Trim().Replace(".", "_").Replace(" ", "_");

    /// <summary>名前を持たない外部キーの既定名を組み立てる</summary>
    /// <param name="childTable">参照する側（外部キーを持つ側）のテーブル名</param>
    /// <param name="parentTable">参照される側のテーブル名</param>
    /// <returns><c>FK_{子}_{親}</c>（それぞれ <see cref="SafeName"/> を通したもの）</returns>
    public static string ForeignKey(string childTable, string parentTable) =>
        "FK_" + SafeName(childTable) + "_" + SafeName(parentTable);

    /// <summary>外部キーの実効名（保存された名前、無ければ <see cref="ForeignKey"/> の既定名）を返す</summary>
    /// <param name="constraintName">保存された制約名（<c>null</c> や空白だけなら名前を持たない扱い）</param>
    /// <param name="childTable">参照する側（外部キーを持つ側）のテーブル名</param>
    /// <param name="parentTable">参照される側のテーブル名</param>
    /// <returns>保存された名前（そのまま）か、<c>FK_{子}_{親}</c></returns>
    /// <remarks>
    /// DDL・同期スクリプトが外部キーに付ける名前と、同期の差分が live の外部キーと突き合わせる名前は
    /// 同じでなければならない。差分だけが保存された名前を素のまま比べると、名前を持たないリレーションと、
    /// DDL が既定名で作った live の外部キーが別物に見え、何も変えていなくても追加と削除が出る
    /// （既定のまま実行すると同じ名前の外部キーを足そうとして失敗する）。
    /// 「名前が空なら既定名」の分岐を呼び出し側で書かず、ここを通すこと。
    /// </remarks>
    public static string EffectiveForeignKey(
        string? constraintName,
        string childTable,
        string parentTable
    ) =>
        string.IsNullOrWhiteSpace(constraintName)
            ? ForeignKey(childTable, parentTable)
            : constraintName;

    /// <summary>主キーの既定名を組み立てる</summary>
    /// <param name="tableName">テーブル名</param>
    /// <returns><c>PK_{テーブル}</c>（<see cref="SafeName"/> を通したもの）</returns>
    public static string PrimaryKey(string tableName) => "PK_" + SafeName(tableName);

    /// <summary>図の中で既に使われている制約名を、DDL が付ける名前で集める</summary>
    /// <param name="diagram">対象の図</param>
    /// <returns>外部キー・主キー・一意制約の名前の集合（大文字小文字を区別しない）</returns>
    /// <remarks>
    /// <para>
    /// 外部キーは保存された名前、無ければ <see cref="ForeignKey"/> の既定名。主キーは主キー列を持つテーブルの
    /// <see cref="PrimaryKey"/>。一意制約は保存された名前、無ければ <see cref="UniqueConstraint.SynthesizeName"/>
    /// （構成列を解決できない一意制約は DDL が出さないので数えない）。
    /// </para>
    /// <para>
    /// 主キーと一意制約まで数えるのは、SQL Server と Oracle では制約名がスキーマ単位で種類をまたいで
    /// 共有されるため（外部キーに <c>PK_Orders</c> と名付けると DDL が失敗する）。
    /// 大文字小文字を区別しないのは、SQL Server の既定の照合順序と MySQL で大文字小文字だけ違う名前も衝突するため。
    /// </para>
    /// </remarks>
    public static IReadOnlySet<string> CollectExisting(ErDiagram diagram)
    {
        var entities = diagram.Entities;
        var names = CollectTableConstraints(entities);

        foreach (var relationship in diagram.Relationships)
        {
            if (!string.IsNullOrWhiteSpace(relationship.ConstraintName))
            {
                names.Add(relationship.ConstraintName);
                continue;
            }

            var child = entities.FirstOrDefault(entity => entity.Id == relationship.TargetEntityId);
            var parent = entities.FirstOrDefault(entity =>
                entity.Id == relationship.SourceEntityId
            );

            if (child is not null && parent is not null)
            {
                names.Add(ForeignKey(child.TableName, parent.TableName));
            }
        }

        return names;
    }

    /// <summary>テーブルが持つ制約（主キー・一意制約）の名前を、DDL が付ける名前で集める</summary>
    /// <param name="entities">対象のテーブル</param>
    /// <returns>主キー・一意制約の名前の集合（大文字小文字を区別しない・呼び出し側で追加してよい）</returns>
    /// <remarks>
    /// <see cref="CollectExisting"/> から外部キーを除いた部分で、規則は同じ。
    /// リレーションの名前をまとめて決める処理（Mermaid のラベルの採否・<see cref="NameUnnamedForeignKeys"/>）が、
    /// 予約済みの名前の出発点に使う
    /// </remarks>
    public static HashSet<string> CollectTableConstraints(IEnumerable<Entity> entities)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            if (entity.Columns.Any(column => column.IsPrimaryKey))
            {
                names.Add(PrimaryKey(entity.TableName));
            }

            foreach (var constraint in entity.UniqueConstraints)
            {
                if (!string.IsNullOrWhiteSpace(constraint.Name))
                {
                    names.Add(constraint.Name);
                    continue;
                }

                var columnNames = constraint
                    .ColumnIds.Select(columnId =>
                        entity.Columns.FirstOrDefault(column => column.Id == columnId)?.Name
                    )
                    .ToList();

                if (columnNames.Count > 0 && columnNames.All(name => name is not null))
                {
                    names.Add(UniqueConstraint.SynthesizeName(entity.TableName, columnNames!));
                }
            }
        }

        return names;
    }

    /// <summary>リレーションごとに、DDL・同期が外部キーへ付ける名前を求める（図は書き換えない）</summary>
    /// <param name="entities">図のテーブル（リレーションの親子と、主キー・一意制約の名前を引く）</param>
    /// <param name="relationships">図のリレーション</param>
    /// <returns>
    /// リレーション（参照の同一性で引く）→ 名前。名前を持つリレーションは保存された名前、持たないリレーションは
    /// 既定名 <see cref="ForeignKey"/>、それがほかの制約とぶつかるときは <see cref="UniqueForeignKey"/> の名前。
    /// 名前を持たず親子を引けないリレーションは含まない
    /// </returns>
    /// <remarks>
    /// <para>
    /// 予約済みの名前は、主キー・一意制約の名前（<see cref="CollectTableConstraints"/>）と、名前を持つリレーションの名前。
    /// 名前を持たないリレーションを渡された順に見て、決まった名前を予約へ加えていく。
    /// </para>
    /// <para>
    /// 同じテーブルの組に名前の無いリレーションが 2 本あると、既定名だけでは同じ名前になり DDL の 2 本目が失敗する。
    /// DDL 生成・同期の差分と同期スクリプトがここを通し、同じ図から同じ名前を作る（差分だけが別の規則で名前を
    /// 求めると、DDL が作った外部キーを別物とみなして追加と削除を出し続ける）。
    /// 図を丸ごと組み立てる取込が名前を図へ書き込むときは <see cref="NameUnnamedForeignKeys"/>（規則は同じ）を使う。
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<Relationship, string> ResolveForeignKeyNames(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships
    )
    {
        // Relationship は等値を上書きしないので、辞書は参照の同一性で引く
        var names = new Dictionary<Relationship, string>();

        foreach (
            var (relationship, resolved) in ResolveForeignKeyNamesCore(entities, relationships)
        )
        {
            names.TryAdd(relationship, resolved.Name);
        }

        return names;
    }

    /// <summary>
    /// 名前を持たないリレーションのうち、既定名がほかの制約とぶつかるものに一意な名前を付ける
    /// </summary>
    /// <param name="entities">図のテーブル（リレーションの親子と、主キー・一意制約の名前を引く）</param>
    /// <param name="relationships">対象のリレーション（<b>その場で書き換える</b>）</param>
    /// <remarks>
    /// <para>
    /// 名前の決め方は <see cref="ResolveForeignKeyNames"/> と同じ。既定名のままでよいリレーションは名前を持たないまま
    /// （DDL 生成時に既定名が合成される＝名前の無いリレーションは往復しても名前の無いまま）にし、
    /// 一意化した名前だけを <see cref="Relationship.ConstraintName"/> へ代入する。名前を持つリレーションの名前は変えない。
    /// </para>
    /// <para>
    /// 図を丸ごと組み立てる取込（Mermaid・DBML・Excel 定義書）と C# リバースが、組み立ての最後に呼ぶ。
    /// 子の列名を一意化に使うので、列ペアを決めた後に呼ぶこと。親子を引けないリレーションは飛ばす
    /// </para>
    /// </remarks>
    public static void NameUnnamedForeignKeys(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships
    )
    {
        foreach (
            var (relationship, resolved) in ResolveForeignKeyNamesCore(entities, relationships)
        )
        {
            if (!resolved.IsDefault && string.IsNullOrWhiteSpace(relationship.ConstraintName))
            {
                relationship.ConstraintName = resolved.Name;
            }
        }
    }

    /// <summary>
    /// <see cref="ResolveForeignKeyNames"/> と <see cref="NameUnnamedForeignKeys"/> が共有する名前の決め方
    /// （名前と、それが既定名のままかを返す）
    /// </summary>
    private static List<
        KeyValuePair<Relationship, (string Name, bool IsDefault)>
    > ResolveForeignKeyNamesCore(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships
    )
    {
        var tables = entities.ToList();
        var targets = relationships.ToList();
        var reserved = CollectTableConstraints(tables);
        var tablesById = new Dictionary<Guid, Entity>();
        var resolved = new List<KeyValuePair<Relationship, (string, bool)>>(targets.Count);

        foreach (var table in tables)
        {
            tablesById.TryAdd(table.Id, table);
        }

        foreach (var relationship in targets)
        {
            if (!string.IsNullOrWhiteSpace(relationship.ConstraintName))
            {
                reserved.Add(relationship.ConstraintName);
            }
        }

        foreach (var relationship in targets)
        {
            if (!string.IsNullOrWhiteSpace(relationship.ConstraintName))
            {
                resolved.Add(new(relationship, (relationship.ConstraintName, false)));
                continue;
            }

            if (
                !tablesById.TryGetValue(relationship.SourceEntityId, out var parent)
                || !tablesById.TryGetValue(relationship.TargetEntityId, out var child)
            )
            {
                continue;
            }

            var defaultName = ForeignKey(child.TableName, parent.TableName);

            if (reserved.Add(defaultName))
            {
                resolved.Add(new(relationship, (defaultName, true)));
                continue;
            }

            var childColumnNames = relationship
                .ColumnPairs.Select(pair =>
                    child.Columns.FirstOrDefault(column => column.Id == pair.TargetColumnId)?.Name
                )
                .OfType<string>();
            var name = UniqueForeignKey(
                child.TableName,
                parent.TableName,
                childColumnNames,
                reserved
            );
            reserved.Add(name);
            resolved.Add(new(relationship, (name, false)));
        }

        return resolved;
    }

    /// <summary>図の中の既存の制約名と衝突しない外部キーの名前を組み立てる</summary>
    /// <param name="childTable">参照する側（外部キーを持つ側）のテーブル名</param>
    /// <param name="parentTable">参照される側のテーブル名</param>
    /// <param name="childColumnNames">外部キーを構成する子の列名（宣言順・無ければ空）</param>
    /// <param name="existingNames">既存の制約名（<see cref="CollectExisting"/> の結果）</param>
    /// <returns>
    /// 空いていれば <see cref="ForeignKey"/> の既定名。ふさがっていれば子の列名を <c>_</c> でつないで足した名前
    /// （<c>FK_Orders_Customers_ShippingCustomerId</c>）。それもふさがっているか列が無いときは、さらに
    /// <c>_2</c>、<c>_3</c>… を足した最初の空き
    /// </returns>
    /// <remarks>
    /// 同じテーブルの組に役割の違う外部キー（請求先・配送先など）を 2 本張ると、既定名だけでは同じ名前になり、
    /// DDL の 2 本目が失敗し、名前でリレーションを特定することもできなくなる。比較は大文字小文字を区別しない。
    /// 結果は入力から一意に決まる
    /// </remarks>
    public static string UniqueForeignKey(
        string childTable,
        string parentTable,
        IEnumerable<string> childColumnNames,
        IEnumerable<string> existingNames
    )
    {
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var name = ForeignKey(childTable, parentTable);

        if (!taken.Contains(name))
        {
            return name;
        }

        var columns = childColumnNames.Select(SafeName).Where(column => column.Length > 0).ToList();

        if (columns.Count > 0)
        {
            name += "_" + string.Join("_", columns);

            if (!taken.Contains(name))
            {
                return name;
            }
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = name + "_" + suffix;

            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
