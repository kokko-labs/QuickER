using QuickER.Dialects;

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
/// <para>
/// <b>既定名は識別子の長さの上限に収める</b>（<see cref="IdentifierLengthLimit.Fit"/>＝上限内ならそのまま、超えるときだけ
/// 末尾を縮める前の名前のハッシュに置き換える）。上限は呼び出し側が引数で渡し、省略できない。渡す上限の経路は 2 つだけ。
/// </para>
/// <list type="bullet">
/// <item>
/// 図へ保存しない既定名（DDL・同期スクリプトが付ける主キー名・外部キー名・一意制約の合成名と、差分が live と突き合わせる名前）:
/// <b>DDL・同期の方言の上限</b>。DDL 生成・同期スクリプトは各方言の識別子クラスの <c>Limit</c>
/// （<c>DdlGeneratorBase.IdentifierLimit</c> / <c>SyncScriptBuilderBase.IdentifierLimit</c>）、差分と同期の計画は
/// <c>SyncDialectCapabilities.IdentifierLengthLimit</c> を渡す（どれも同じ値を指す＝<c>IdentifierLengthLimitDeclarationTests</c>）。
/// DDL の方言は図の対象 DBMS と食い違い得る（MCP の <c>generate_ddl</c> は方言を指定できる）ので、図からは引かない。
/// </item>
/// <item>
/// 図へ保存する既定名（ツールと GUI がリレーションに付ける外部キー名・取込と C# リバースの <see cref="NameUnnamedForeignKeys"/>）と、
/// 図の名前として見せる合成名（C# 生成の <c>[UniqueConstraint(Name = …)]</c>・ツールの要約・プロパティパネル）:
/// <b>図の対象 DBMS の上限</b>（呼び出し側が <see cref="IdentifierLengthLimits.ForDbms"/> に <see cref="ErDiagram.TargetDbms"/> を渡して引く）。
/// 図を受け取る <see cref="CollectExisting"/> / <see cref="IsTaken"/> / <see cref="ResolveForeignKeyNamesById"/> も上限を引数で受ける
/// （Model は方言名から上限を引かない＝<c>ModelDialectLookupGuardTests</c>）。
/// </item>
/// </list>
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
    /// <param name="limit">識別子の長さの上限（経路は型の XmlDoc）</param>
    /// <returns><c>FK_{子}_{親}</c>（それぞれ <see cref="SafeName"/> を通したもの。上限を超えるときは <see cref="IdentifierLengthLimit.Fit"/> で縮める）</returns>
    public static string ForeignKey(
        string childTable,
        string parentTable,
        IdentifierLengthLimit limit
    ) => limit.Fit(UnshortenedForeignKey(childTable, parentTable));

    /// <summary>縮める前の外部キーの既定名</summary>
    private static string UnshortenedForeignKey(string childTable, string parentTable) =>
        "FK_" + SafeName(childTable) + "_" + SafeName(parentTable);

    /// <summary>外部キーの実効名（保存された名前、無ければ <see cref="ForeignKey"/> の既定名）を返す</summary>
    /// <param name="constraintName">保存された制約名（<c>null</c> や空白だけなら名前を持たない扱い）</param>
    /// <param name="childTable">参照する側（外部キーを持つ側）のテーブル名</param>
    /// <param name="parentTable">参照される側のテーブル名</param>
    /// <param name="limit">既定名に使う識別子の長さの上限（経路は型の XmlDoc。保存された名前は縮めない）</param>
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
        string parentTable,
        IdentifierLengthLimit limit
    ) =>
        string.IsNullOrWhiteSpace(constraintName)
            ? ForeignKey(childTable, parentTable, limit)
            : constraintName;

    /// <summary>主キーの既定名を組み立てる</summary>
    /// <param name="tableName">テーブル名</param>
    /// <param name="limit">識別子の長さの上限（経路は型の XmlDoc）</param>
    /// <returns><c>PK_{テーブル}</c>（<see cref="SafeName"/> を通したもの。上限を超えるときは <see cref="IdentifierLengthLimit.Fit"/> で縮める）</returns>
    public static string PrimaryKey(string tableName, IdentifierLengthLimit limit) =>
        limit.Fit("PK_" + SafeName(tableName));

    /// <summary>図の中で既に使われている制約名を、DDL が付ける名前で集める</summary>
    /// <param name="diagram">対象の図</param>
    /// <param name="limit">既定名に使う識別子の長さの上限（図の対象 DBMS の上限。経路は型の XmlDoc）</param>
    /// <returns>外部キー・主キー・一意制約の名前の集合（大文字小文字を区別しない）</returns>
    /// <remarks>
    /// <para>
    /// 外部キーは保存された名前、無ければ <see cref="ForeignKey"/> の既定名（名前を持たず外部キーを作らない関係＝
    /// <see cref="DefinesForeignKey"/> が偽のものは、DDL が名前を付けないので数えない）。主キーは主キー列を持つテーブルの
    /// <see cref="PrimaryKey"/>。一意制約は保存された名前、無ければ <see cref="UniqueConstraint.SynthesizeName"/>
    /// （構成列を解決できない一意制約は DDL が出さないので数えない）。
    /// </para>
    /// <para>
    /// 主キーと一意制約まで数えるのは、SQL Server と Oracle では制約名がスキーマ単位で種類をまたいで
    /// 共有されるため（外部キーに <c>PK_Orders</c> と名付けると DDL が失敗する）。
    /// 大文字小文字を区別しないのは、SQL Server の既定の照合順序と MySQL で大文字小文字だけ違う名前も衝突するため。
    /// </para>
    /// <para>既定名は図の対象 DBMS の上限で縮めた名前で数える（図へ保存する名前を決めるための集合なので）。</para>
    /// </remarks>
    public static IReadOnlySet<string> CollectExisting(
        ErDiagram diagram,
        IdentifierLengthLimit limit
    )
    {
        var entities = diagram.Entities;
        var names = CollectTableConstraints(entities, limit);

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

            if (
                child is not null
                && parent is not null
                && DefinesForeignKey(relationship, parent, child)
            )
            {
                names.Add(ForeignKey(child.TableName, parent.TableName, limit));
            }
        }

        return names;
    }

    /// <summary>
    /// 一意制約へ付けようとする名前が、図の中の既存の制約名（外部キー・主キー・一意制約）と衝突するか
    /// </summary>
    /// <param name="diagram">図</param>
    /// <param name="name">付けようとする名前（比較は <see cref="SchemaNames.AreSame"/> の規則）</param>
    /// <param name="replacingUniqueConstraintId">
    /// 名前を付け替える既存の一意制約の Id（自分自身の現在の名前とは衝突させない）。新規の制約なら <c>null</c>
    /// </param>
    /// <param name="limit">既定名に使う識別子の長さの上限（図の対象 DBMS の上限。経路は型の XmlDoc）</param>
    /// <remarks>
    /// 外部キーの名前を付けるツール（<c>add_relationship</c>）と同じ規則を、一意制約の名前にも使う
    /// （制約名は SQL Server と Oracle では種類をまたいでスキーマ単位で共有される）。
    /// </remarks>
    public static bool IsTaken(
        ErDiagram diagram,
        string name,
        Guid? replacingUniqueConstraintId,
        IdentifierLengthLimit limit
    )
    {
        var names = new HashSet<string>(CollectExisting(diagram, limit), SchemaNames.Comparer);

        if (replacingUniqueConstraintId is { } id)
        {
            foreach (var entity in diagram.Entities)
            {
                var own = entity.UniqueConstraints.FirstOrDefault(constraint =>
                    constraint.Id == id
                );

                if (own is null)
                {
                    continue;
                }

                var columnNames = own
                    .ColumnIds.Select(columnId =>
                        entity.Columns.FirstOrDefault(column => column.Id == columnId)?.Name
                    )
                    .OfType<string>();
                names.Remove(
                    own.Name
                        ?? UniqueConstraint.SynthesizeName(entity.TableName, columnNames, limit)
                );
            }
        }

        return names.Contains(name);
    }

    /// <summary>テーブルが持つ制約（主キー・一意制約）の名前を、DDL が付ける名前で集める</summary>
    /// <param name="entities">対象のテーブル</param>
    /// <param name="limit">既定名に使う識別子の長さの上限（経路は型の XmlDoc）</param>
    /// <returns>主キー・一意制約の名前の集合（大文字小文字を区別しない・呼び出し側で追加してよい）</returns>
    /// <remarks>
    /// <see cref="CollectExisting"/> から外部キーを除いた部分で、規則は同じ。
    /// リレーションの名前をまとめて決める処理（Mermaid のラベルの採否・<see cref="NameUnnamedForeignKeys"/>）が、
    /// 予約済みの名前の出発点に使う
    /// </remarks>
    public static HashSet<string> CollectTableConstraints(
        IEnumerable<Entity> entities,
        IdentifierLengthLimit limit
    )
    {
        var names = new HashSet<string>(SchemaNames.Comparer);

        foreach (var entity in entities)
        {
            if (entity.Columns.Any(column => column.IsPrimaryKey))
            {
                names.Add(PrimaryKey(entity.TableName, limit));
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
                    names.Add(
                        UniqueConstraint.SynthesizeName(entity.TableName, columnNames!, limit)
                    );
                }
            }
        }

        return names;
    }

    /// <summary>リレーションごとに、DDL・同期が外部キーへ付ける名前を求める（図は書き換えない）</summary>
    /// <param name="entities">図のテーブル（リレーションの親子と、主キー・一意制約の名前を引く）</param>
    /// <param name="relationships">図のリレーション</param>
    /// <param name="limit">既定名に使う識別子の長さの上限（経路は型の XmlDoc）</param>
    /// <returns>
    /// リレーション（参照の同一性で引く）→ 名前。名前を持つリレーションは保存された名前、持たないリレーションは
    /// 既定名 <see cref="ForeignKey"/>、それがほかの制約とぶつかるときは <see cref="UniqueForeignKey"/> の名前。
    /// 名前を持たず、親子を引けないか外部キーを作らない（<see cref="DefinesForeignKey"/> が偽）リレーションは含まない
    /// </returns>
    /// <remarks>
    /// <para>
    /// 予約済みの名前は、主キー・一意制約の名前（<see cref="CollectTableConstraints"/>）と、名前を持つリレーションの名前。
    /// 名前を持たないリレーションを渡された順に見て、決まった名前を予約へ加えていく。
    /// 名前を持たない多対多・列ペアの無い関係は既定名を取らない（DDL が外部キーを作らないので、取ると同じ組の
    /// 本物の外部キーが一意化した名前へずれる）。名前を持つ関係は外部キーを作らなくても予約する（保存された名前は
    /// 利用者に見える名前なので重ねない）。
    /// </para>
    /// <para>
    /// 名前を持たない外部キーどうしは並び順で名前が決まる。名前の無い多対多を後から 1 対多へ切り替えて列ペアを付けると、
    /// それより後ろに並ぶ同じ組の名前の無い外部キーが一意化した名前へずれ、名前を保存する方言の同期では作り直しになる
    /// （名前の無いリレーションは取込・古いファイル・手編集でしか生じない。GUI とツールで作るリレーションは名前を持つ）。
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
        IEnumerable<Relationship> relationships,
        IdentifierLengthLimit limit
    ) =>
        ResolveForeignKeyNameDetails(entities, relationships, limit)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Name);

    /// <summary>
    /// 図のリレーションの Id ごとに、DDL・同期が外部キーへ付ける名前を求める（<see cref="ResolveForeignKeyNames"/> の、
    /// 図の対象 DBMS の上限で求める形）
    /// </summary>
    /// <param name="diagram">図</param>
    /// <param name="limit">既定名に使う識別子の長さの上限（図の対象 DBMS の上限。経路は型の XmlDoc）</param>
    /// <returns>リレーションの Id → 名前（含まれるリレーションは <see cref="ResolveForeignKeyNames"/> と同じ）</returns>
    /// <remarks>
    /// ER 図操作ツールが、名前の無いリレーションを要約に示し、その名前で <c>remove_relationship</c> から選べるようにするために使う
    /// （GUI のツールは ViewModel から作り直した図を渡すので、参照でなく Id で引く）。
    /// </remarks>
    public static IReadOnlyDictionary<Guid, string> ResolveForeignKeyNamesById(
        ErDiagram diagram,
        IdentifierLengthLimit limit
    ) =>
        ResolveForeignKeyNames(diagram.Entities, diagram.Relationships, limit)
            .ToDictionary(pair => pair.Key.Id, pair => pair.Value);

    /// <summary>
    /// 新しい外部キーの名前を決める（ER 図操作ツールの <c>constraint_name</c> の規則。GUI と MCP が共有する）
    /// </summary>
    /// <param name="requested">指定された名前（null・空・空白だけは省略として扱い、前後の空白は除く）</param>
    /// <param name="childTable">参照する側（子）のテーブル名</param>
    /// <param name="parentTable">参照される側（親）のテーブル名</param>
    /// <param name="childColumnNames">外部キーを構成する子の列名（宣言順）</param>
    /// <param name="existingNames">図の中の既存の制約名（<see cref="CollectExisting"/>）</param>
    /// <param name="limit">既定名に使う識別子の長さの上限（図の対象 DBMS の上限）</param>
    /// <returns>
    /// 決めた名前と、指定された名前が既存と衝突したか（衝突したときは黙って付け替えず、呼び出し側が断る＝付け替えると
    /// その名前での <c>remove_relationship</c> が外れ、受け入れると DDL が失敗する）
    /// </returns>
    public static (string Name, bool Taken) ResolveRequestedForeignKey(
        string? requested,
        string childTable,
        string parentTable,
        IEnumerable<string> childColumnNames,
        IReadOnlySet<string> existingNames,
        IdentifierLengthLimit limit
    )
    {
        var trimmed = requested?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return (
                UniqueForeignKey(childTable, parentTable, childColumnNames, existingNames, limit),
                false
            );
        }

        return (trimmed, existingNames.Contains(trimmed));
    }

    /// <summary>
    /// <see cref="ResolveForeignKeyNames"/> と同じ名前を、縮める前の名前と、保存された名前かどうかと一緒に求める
    /// </summary>
    /// <remarks>
    /// 同期の差分が、旧版の QuickER が長い既定名で作った PostgreSQL の外部キー（PostgreSQL が黙って切り詰めた名前）を
    /// 同じ外部キーとみなすために、縮める前の名前を使う（<c>SyncDialectCapabilities.TruncatesLongIdentifiers</c>）。
    /// </remarks>
    public static IReadOnlyDictionary<
        Relationship,
        ResolvedForeignKeyName
    > ResolveForeignKeyNameDetails(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships,
        IdentifierLengthLimit limit
    )
    {
        // Relationship は等値を上書きしないので、辞書は参照の同一性で引く
        var names = new Dictionary<Relationship, ResolvedForeignKeyName>();

        foreach (
            var (relationship, resolved) in ResolveForeignKeyNamesCore(
                entities,
                relationships,
                limit
            )
        )
        {
            names.TryAdd(relationship, resolved);
        }

        return names;
    }

    /// <summary>
    /// 名前を持たないリレーションのうち、既定名がほかの制約とぶつかるものに一意な名前を付ける
    /// </summary>
    /// <param name="entities">図のテーブル（リレーションの親子と、主キー・一意制約の名前を引く）</param>
    /// <param name="relationships">対象のリレーション（<b>その場で書き換える</b>）</param>
    /// <param name="limit">名前に使う識別子の長さの上限＝図の対象 DBMS の上限（経路は型の XmlDoc）</param>
    /// <remarks>
    /// <para>
    /// 名前の決め方は <see cref="ResolveForeignKeyNames"/> と同じ。既定名のままでよいリレーションは名前を持たないまま
    /// （DDL 生成時に既定名が合成される＝名前の無いリレーションは往復しても名前の無いまま）にし、
    /// 一意化した名前だけを <see cref="Relationship.ConstraintName"/> へ代入する。名前を持つリレーションの名前は変えない。
    /// </para>
    /// <para>
    /// 図を丸ごと組み立てる取込（Mermaid・DBML・Excel 定義書）と C# リバースが、組み立ての最後に呼ぶ。
    /// 子の列名を一意化に使うので、列ペアを決めた後に呼ぶこと。親子を引けないリレーションと、外部キーを作らないリレーションは飛ばす
    /// </para>
    /// </remarks>
    public static void NameUnnamedForeignKeys(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships,
        IdentifierLengthLimit limit
    )
    {
        foreach (
            var (relationship, resolved) in ResolveForeignKeyNamesCore(
                entities,
                relationships,
                limit
            )
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
    /// （名前と、縮める前の名前と、それが保存された名前か・既定名のままかを返す）
    /// </summary>
    private static List<
        KeyValuePair<Relationship, ResolvedForeignKeyName>
    > ResolveForeignKeyNamesCore(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships,
        IdentifierLengthLimit limit
    )
    {
        var tables = entities.ToList();
        var targets = relationships.ToList();
        var reserved = CollectTableConstraints(tables, limit);
        var tablesById = new Dictionary<Guid, Entity>();
        var resolved = new List<KeyValuePair<Relationship, ResolvedForeignKeyName>>(targets.Count);

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
                resolved.Add(
                    new(
                        relationship,
                        new ResolvedForeignKeyName(
                            relationship.ConstraintName,
                            relationship.ConstraintName,
                            IsStored: true,
                            IsDefault: false
                        )
                    )
                );
                continue;
            }

            // 外部キーを作らない関係は DDL が名前を付けないので、既定名を取らない（取ると本物の外部キーがずれる）
            if (
                !tablesById.TryGetValue(relationship.SourceEntityId, out var parent)
                || !tablesById.TryGetValue(relationship.TargetEntityId, out var child)
                || !DefinesForeignKey(relationship, parent, child)
            )
            {
                continue;
            }

            var unshortenedDefault = UnshortenedForeignKey(child.TableName, parent.TableName);
            var defaultName = limit.Fit(unshortenedDefault);

            if (reserved.Add(defaultName))
            {
                resolved.Add(
                    new(
                        relationship,
                        new ResolvedForeignKeyName(
                            defaultName,
                            unshortenedDefault,
                            IsStored: false,
                            IsDefault: true
                        )
                    )
                );
                continue;
            }

            var childColumnNames = relationship
                .ColumnPairs.Select(pair =>
                    child.Columns.FirstOrDefault(column => column.Id == pair.TargetColumnId)?.Name
                )
                .OfType<string>();
            var (name, unshortened) = UniqueForeignKeyCore(
                child.TableName,
                parent.TableName,
                childColumnNames,
                reserved,
                limit
            );
            reserved.Add(name);
            resolved.Add(
                new(
                    relationship,
                    new ResolvedForeignKeyName(name, unshortened, IsStored: false, IsDefault: false)
                )
            );
        }

        return resolved;
    }

    /// <summary>リレーションが DDL・同期で外部キー制約になるかを判定する</summary>
    /// <param name="relationship">対象のリレーション</param>
    /// <param name="parent">参照される側（親）のテーブル</param>
    /// <param name="child">参照する側（子）のテーブル</param>
    /// <returns>
    /// 多対多でなく、列ペアが 1 組以上あり、すべての列ペアの両端の列を親子から引けるとき <c>true</c>
    /// </returns>
    /// <remarks>
    /// DDL・差分・同期計画が外部キーとして扱うリレーションの判定（Provider の <c>ForeignKeyColumnPairResolver</c>）と
    /// 同じ規則。Model は Provider を参照できないので写しになるため、両者の一致は
    /// <c>ForeignKeyDefinitionParityTests</c> が固定する
    /// </remarks>
    public static bool DefinesForeignKey(Relationship relationship, Entity parent, Entity child) =>
        relationship.Type != RelationshipType.ManyToMany
        && relationship.ColumnPairs.Count > 0
        && relationship.ColumnPairs.All(pair =>
            parent.Columns.Any(column => column.Id == pair.SourceColumnId)
            && child.Columns.Any(column => column.Id == pair.TargetColumnId)
        );

    /// <summary>図の中の既存の制約名と衝突しない外部キーの名前を組み立てる</summary>
    /// <param name="childTable">参照する側（外部キーを持つ側）のテーブル名</param>
    /// <param name="parentTable">参照される側のテーブル名</param>
    /// <param name="childColumnNames">外部キーを構成する子の列名（宣言順・無ければ空）</param>
    /// <param name="existingNames">既存の制約名（<see cref="CollectExisting"/> の結果）</param>
    /// <param name="limit">識別子の長さの上限（経路は型の XmlDoc。図へ保存する名前なので図の対象 DBMS の上限）</param>
    /// <returns>
    /// 空いていれば <see cref="ForeignKey"/> の既定名。ふさがっていれば子の列名を <c>_</c> でつないで足した名前
    /// （<c>FK_Orders_Customers_ShippingCustomerId</c>）。それもふさがっているか列が無いときは、さらに
    /// <c>_2</c>、<c>_3</c>… を足した最初の空き
    /// </returns>
    /// <remarks>
    /// 同じテーブルの組に役割の違う外部キー（請求先・配送先など）を 2 本張ると、既定名だけでは同じ名前になり、
    /// DDL の 2 本目が失敗し、名前でリレーションを特定することもできなくなる。比較は大文字小文字を区別しない。
    /// 結果は入力から一意に決まる。候補はそれぞれ <see cref="IdentifierLengthLimit.Fit"/> で縮めてから空きを確かめる
    /// （縮めた名前どうしは、縮める前の名前のハッシュが違うので重ならない）
    /// </remarks>
    public static string UniqueForeignKey(
        string childTable,
        string parentTable,
        IEnumerable<string> childColumnNames,
        IEnumerable<string> existingNames,
        IdentifierLengthLimit limit
    ) =>
        UniqueForeignKeyCore(
            childTable,
            parentTable,
            childColumnNames,
            new HashSet<string>(existingNames, SchemaNames.Comparer),
            limit
        ).Name;

    /// <summary><see cref="UniqueForeignKey"/> の本体（縮めた名前と、縮める前の名前を返す）</summary>
    private static (string Name, string Unshortened) UniqueForeignKeyCore(
        string childTable,
        string parentTable,
        IEnumerable<string> childColumnNames,
        HashSet<string> taken,
        IdentifierLengthLimit limit
    )
    {
        var name = UnshortenedForeignKey(childTable, parentTable);

        if (!taken.Contains(limit.Fit(name)))
        {
            return (limit.Fit(name), name);
        }

        var columns = childColumnNames.Select(SafeName).Where(column => column.Length > 0).ToList();

        if (columns.Count > 0)
        {
            name += "_" + string.Join("_", columns);

            if (!taken.Contains(limit.Fit(name)))
            {
                return (limit.Fit(name), name);
            }
        }

        // 候補は Fit で縮めてから空きを確かめるので、Fit が候補の違いを保てないと空きが見つからない。
        // 黙って回り続けず（GUI ごと固まる）、上限で例外にして表に出す
        for (var suffix = 2; suffix <= MaxNumberedCandidates; suffix++)
        {
            var candidate = name + "_" + suffix;

            if (!taken.Contains(limit.Fit(candidate)))
            {
                return (limit.Fit(candidate), candidate);
            }
        }

        throw new InvalidOperationException(
            $"Could not find a free foreign key name for '{childTable}' -> '{parentTable}' "
                + $"after {MaxNumberedCandidates - 1} numbered candidates."
        );
    }

    /// <summary>外部キーの名前を一意にするときに試す番号の上限（<see cref="UniqueForeignKey"/>）</summary>
    /// <remarks>
    /// 実際の図でここまで届くことは無い（同じテーブルの組の外部キーがこの本数を超えない）。
    /// 縮め方（<see cref="IdentifierLengthLimit.Fit"/>）が候補の違いを保てなくなったときに、終わらない繰り返しを例外へ変えるための上限
    /// </remarks>
    public const int MaxNumberedCandidates = 1000;
}

/// <summary>外部キーに付ける名前の解決結果</summary>
/// <param name="Name">DDL・同期が付ける名前（既定名は上限に収めたもの）</param>
/// <param name="UnshortenedName">上限に収める前の名前（保存された名前ならそれと同じ）</param>
/// <param name="IsStored">リレーションに保存された名前か</param>
/// <param name="IsDefault">既定名 <c>FK_{子}_{親}</c> のままか（一意化した名前・保存された名前は <c>false</c>）</param>
public sealed record ResolvedForeignKeyName(
    string Name,
    string UnshortenedName,
    bool IsStored,
    bool IsDefault
);
