using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 双方向同期の支援コード（同期記述子・ジャーナル記録デコレータ・直結差分ソース）の生成素材を組み立てる部分クラス
/// </summary>
/// <remarks>
/// <para>
/// 同期支援は「サーバー＝SQL Server／ローカル＝SQLite」の 2 方言を同時に扱う唯一のスコープで、テンプレートの
/// 方言変数（<c>quote_open</c> 等）は 1 スコープ 1 方言しか持てない。そのため SQL 文はここで両方言分を
/// 文字列リテラルとして組み立て、テンプレートは受け取ったリテラルをそのまま埋め込むだけにする。
/// </para>
/// </remarks>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>SQL Server の識別子クォート（開始・終了）</summary>
    private const string SqlServerQuoteOpen = "[";

    /// <summary>SQL Server の識別子クォート（終了）</summary>
    private const string SqlServerQuoteClose = "]";

    /// <summary>
    /// 同期対象テーブル（Repository 契約を持つ単一主キーのテーブル）の生成モデルを FK トポロジカル順で組み立てる
    /// </summary>
    /// <remarks>
    /// rowversion 列の有無は対象かどうかを決めず、モード素材（版あり＝増分＋競合検出／版なし＝後勝ち・全量）に
    /// なる。対象が 0 件になる構成（オプション OFF・Repository 対象テーブルなし）では空リストを返す。オプション
    /// ON で 0 件になる場合の診断は生成サービス側が担う（ここは素材の組み立てに徹する）。
    /// </remarks>
    private IReadOnlyList<CSharpSyncTableModel> BuildSyncTables(
        ErDiagram diagram,
        CodeGenerationOptions options,
        IReadOnlyList<CSharpRepositoryModel> repositoryClasses
    )
    {
        if (!options.GenerateSyncSupport || !options.GeneratesRepositoryContract)
        {
            return [];
        }

        var repositoriesByEntityClass = repositoryClasses.ToDictionary(
            repository => repository.EntityClassName,
            StringComparer.Ordinal
        );

        var targets = new List<Entity>();

        foreach (var entity in OrderEntitiesByForeignKey(diagram))
        {
            var className = _nameConverter.ToEntityClassName(entity.TableName);

            if (!repositoriesByEntityClass.ContainsKey(className))
            {
                continue;
            }

            targets.Add(entity);
        }

        var targetIds = targets.Select(entity => entity.Id).ToHashSet();

        return targets
            .Select(entity =>
                BuildSyncTable(
                    entity,
                    repositoriesByEntityClass[_nameConverter.ToEntityClassName(entity.TableName)],
                    options,
                    diagram,
                    targetIds
                )
            )
            .ToList();
    }

    /// <summary>そのエンティティの行バージョン列（型解決が <c>IsRowVersion</c> の列）を返す（無ければ null）</summary>
    /// <remarks>
    /// 判定は <c>[StoreGeneratedColumn]</c> の付与条件と同一の型マッパーの解決結果。1 エンティティに 2 本以上あると
    /// 生成サービス側の検証がエラーにするため、ここは先頭 1 本を採ってよい。
    /// </remarks>
    private Column? FindRowVersionColumn(Entity entity) =>
        entity.Columns.FirstOrDefault(column =>
            _columnTypes.TryGetValue(column.Id, out var typeInfo) && typeInfo.IsRowVersion
        );

    /// <summary>
    /// エンティティを FK トポロジカル順（親が先）へ並べる。
    /// </summary>
    /// <remarks>
    /// ダウンロードの適用は親→子、削除は子→親でなければ FK 制約に触れる。自己参照は辺として数えない。循環参照がある図では
    /// 完全な順序が存在しないため、行き詰まったら循環に属するテーブルを宣言順で 1 つだけ先に出して続ける
    /// （循環の外のテーブルは親の後ろに保たれ、循環の中の順序だけが宣言順で決まる。生成は完走させる）。
    /// </remarks>
    private static IReadOnlyList<Entity> OrderEntitiesByForeignKey(ErDiagram diagram)
    {
        var columnOwners = new Dictionary<Guid, Guid>();

        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                columnOwners[column.Id] = entity.Id;
            }
        }

        // 子 → 親（依存先）の集合。自己参照は順序を決められないので辺として数えない
        var parents = diagram.Entities.ToDictionary(entity => entity.Id, _ => new HashSet<Guid>());

        foreach (var relationship in diagram.Relationships)
        {
            foreach (var pair in relationship.ColumnPairs)
            {
                if (
                    !columnOwners.TryGetValue(pair.SourceColumnId, out var principal)
                    || !columnOwners.TryGetValue(pair.TargetColumnId, out var dependent)
                    || principal == dependent
                    || !parents.TryGetValue(dependent, out var set)
                )
                {
                    continue;
                }

                set.Add(principal);
            }
        }

        var ordered = new List<Entity>();
        var emitted = new HashSet<Guid>();
        var remaining = diagram.Entities.ToList();

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(entity => parents[entity.Id].All(parent => emitted.Contains(parent)))
                .ToList();

            if (ready.Count == 0)
            {
                // 循環（相互参照）は解けない。循環に属するテーブルを宣言順で 1 つだけ先に出して続ける
                // （残りを全部出すと、循環の外で循環の誰かを参照する子まで親より前へ出てしまう）
                ready = [remaining.First(entity => IsOnCycle(entity.Id, remaining, parents))];
            }

            foreach (var entity in ready)
            {
                ordered.Add(entity);
                emitted.Add(entity.Id);
                remaining.Remove(entity);
            }
        }

        return ordered;
    }

    /// <summary>
    /// エンティティが、まだ出していないエンティティの間の循環に属するか（親を辿って自分へ戻れるか）
    /// </summary>
    /// <remarks>
    /// 出していないエンティティが 1 つも出せない（全員にまだ出していない親がいる）とき、親を辿ると必ずどこかで循環に入るので、
    /// 循環に属するエンティティは必ず 1 つ以上ある。
    /// </remarks>
    private static bool IsOnCycle(
        Guid start,
        IReadOnlyCollection<Entity> remaining,
        IReadOnlyDictionary<Guid, HashSet<Guid>> parents
    )
    {
        var remainingIds = remaining.Select(entity => entity.Id).ToHashSet();
        var visited = new HashSet<Guid>();
        var pending = new Stack<Guid>(parents[start].Where(remainingIds.Contains));

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (current == start)
            {
                return true;
            }

            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var parent in parents[current].Where(remainingIds.Contains))
            {
                pending.Push(parent);
            }
        }

        return false;
    }

    /// <summary>1 テーブル分の同期生成モデルを組み立てる</summary>
    private CSharpSyncTableModel BuildSyncTable(
        Entity entity,
        CSharpRepositoryModel repository,
        CodeGenerationOptions options,
        ErDiagram diagram,
        IReadOnlySet<Guid> syncTargetIds
    )
    {
        var keyColumn = entity.Columns.First(column => column.IsPrimaryKey);

        // rowversion 列の有無がモードを決める（版あり＝増分＋版ガード／版なし＝後勝ち・キー順全量）
        var rowVersionColumn = FindRowVersionColumn(entity);
        var isVersionless = rowVersionColumn is null;
        var repositoryName = ToRepositoryName(repository.EntityClassName);

        var keyPropertyName = _nameConverter.ToPropertyName(keyColumn.Name);
        var keyValueObject = ResolveValueObject(keyColumn);
        var rowVersionValueObject = rowVersionColumn is null
            ? null
            : ResolveValueObject(rowVersionColumn);

        // 除外列（無制限バイナリ列）は行の転送に載らないため、blob を運ぶには列単位のコピーが要る。
        // オプション OFF・該当列なしなら空＝関連ブロックを 1 つも出さない（生成物はこの機能の追加前と同一）。
        var binaryColumns =
            options.ExcludeUnboundedBinaryColumns
            && entity.Columns.Any(column => _columnTypes[column.Id].IsUnboundedBinary)
                ? entity
                    .Columns.Where(column => _columnTypes[column.Id].IsUnboundedBinary)
                    .Select(column => _nameConverter.ToPropertyName(column.Name))
                    .ToList()
                : [];

        // 差分走査の SELECT 列。除外列があるときだけ明示列挙へ切り替え、除外列を落とす。
        // 生 SQL のエンティティ取得は SELECT に含まれた列を opportunistic にマップするため、"*" のままだと
        // 除外列まで降りてきてしまう（そして UPDATE 経路の「除外列に値が入っている」ガードに引っかかる）。
        // 「除外列は行の転送に載らない」という意味論は、ここで列を落とすことで初めて成立する。
        var serverSelectList =
            binaryColumns.Count == 0
                ? "*"
                : string.Join(
                    ", ",
                    entity
                        .Columns.Where(column => !_columnTypes[column.Id].IsUnboundedBinary)
                        .Select(column =>
                            QuoteIdentifier(column.Name, SqlServerQuoteOpen, SqlServerQuoteClose)
                        )
                );

        var serverTable = QuoteQualified(entity.TableName, SqlServerQuoteOpen, SqlServerQuoteClose);
        var localTable = QuoteQualified(entity.TableName, "\"", "\"");
        var serverKey = QuoteIdentifier(keyColumn.Name, SqlServerQuoteOpen, SqlServerQuoteClose);
        var localKey = QuoteIdentifier(keyColumn.Name, "\"", "\"");
        var rowVersionPropertyName = rowVersionColumn is null
            ? string.Empty
            : _nameConverter.ToPropertyName(rowVersionColumn.Name);
        var serverRowVersion = rowVersionColumn is null
            ? string.Empty
            : QuoteIdentifier(rowVersionColumn.Name, SqlServerQuoteOpen, SqlServerQuoteClose);
        var localRowVersion = rowVersionColumn is null
            ? string.Empty
            : QuoteIdentifier(rowVersionColumn.Name, "\"", "\"");

        return new CSharpSyncTableModel
        {
            EntityClassName = repository.EntityClassName,
            InterfaceName = repository.InterfaceName,
            KeyTypeName = repository.KeyTypeName,
            // SyncTableDescriptor.TableName へ C# リテラルとして埋め込むためエスケープする（[Table] と同じ規則）
            TableName = EscapeNameForCSharpString(entity.TableName),
            // 記述子プロパティの XmlDoc へ載せるため、XML としてエスケープした形も持つ
            TableNameXmlDoc = EscapeForXmlDocSummary(entity.TableName),
            IsVersionless = isVersionless,
            RemoteRouteName = repositoryName,
            DescriptorPropertyName = repositoryName,
            DecoratorClassName = $"Journaling{repositoryName}Repository",
            // デコレータの汎用基底だけは版の有無でサブクラス階層を選ぶ（削除記録の形が違う＝版ありは
            // 行が消える前にミラー版を読む）。記述子・差分ソースの選択は登録時に IsVersionless が決める
            DecoratorBaseClassName = isVersionless
                ? "VersionlessJournalingRepository"
                : "JournalingRepository",
            KeyPropertyName = keyPropertyName,
            // 昇順・上限つきの差分走査（版ありのみ）。2 点が効いている:
            //  (1) @anchor / @ceiling は NULL を取り得るため、素の比較では 3 値論理で全行が UNKNOWN になる
            //      （初回の全量取得が 0 件になる）。NULL を「制約なし」として明示的に外す。
            //  (2) 生 SQL のパラメータは列の文脈を持たず型なしでバインドされるため、NULL は nvarchar と推論される。
            //      rowversion と nvarchar は暗黙変換できず実行時エラーになるので、比較の相手を binary(8) へ明示的に
            //      キャストする（非 NULL のときは byte[] がそのまま binary(8) として読まれる）。
            ServerChangesSql = isVersionless
                ? string.Empty
                : CSharpLiteral(
                    $"SELECT TOP (@batchSize) {serverSelectList} FROM {serverTable} "
                        + $"WHERE (@anchor IS NULL OR {serverRowVersion} > CAST(@anchor AS binary(8))) "
                        + $"AND (@ceiling IS NULL OR {serverRowVersion} < CAST(@ceiling AS binary(8))) "
                        + $"ORDER BY {serverRowVersion}"
                ),
            // 版なしテーブルの全量走査はキー昇順のページング。@afterKey は型なしでバインドされる（文字列は nvarchar）ので、
            // 文字列の主キーは列と同じ型へ明示的にキャストする（BuildServerAfterKey）
            ServerPageFirstSql = isVersionless
                ? CSharpLiteral(
                    $"SELECT TOP (@batchSize) {serverSelectList} FROM {serverTable} ORDER BY {serverKey}"
                )
                : string.Empty,
            ServerPageAfterSql = isVersionless
                ? CSharpLiteral(
                    $"SELECT TOP (@batchSize) {serverSelectList} FROM {serverTable} "
                        + $"WHERE {serverKey} > {BuildServerAfterKey(keyColumn)} ORDER BY {serverKey}"
                )
                : string.Empty,
            ServerKeysSql = CSharpLiteral($"SELECT {serverKey} FROM {serverTable}"),
            LocalAnchorSql = isVersionless
                ? string.Empty
                : CSharpLiteral($"SELECT MAX({localRowVersion}) FROM {localTable}"),
            // グラフ保存の削除の記録が、ローカルの行のミラー版を読む（スタブや古い実体は版を持たない＝#27）
            LocalRowVersionSql = isVersionless
                ? string.Empty
                : CSharpLiteral(
                    $"SELECT {localRowVersion} FROM {localTable} WHERE {localKey} = @key"
                ),
            LocalKeysSql = CSharpLiteral($"SELECT {localKey} FROM {localTable}"),
            LocalExistingKeysSql = CSharpLiteral(
                $"SELECT {localKey} FROM {localTable} WHERE {localKey} IN (@keys)"
            ),
            LocalDeleteAllSql = CSharpLiteral($"DELETE FROM {localTable}"),
            // 版なしテーブルのミラー版は存在しない＝記述子は版アクセサを載せず、既定（常に null）が働く
            RowVersionReadExpression = rowVersionColumn is null
                ? "null"
                : BuildRowVersionRead("entity", rowVersionPropertyName, rowVersionValueObject),
            RowVersionWriteExpression = rowVersionColumn is null
                ? string.Empty
                : BuildRowVersionWrite(
                    rowVersionPropertyName,
                    rowVersionColumn,
                    rowVersionValueObject
                ),
            FormatKeyExpression = BuildFormatKey("key", keyColumn, keyValueObject),
            ParseKeyExpression = BuildParseKey(keyColumn, keyValueObject),
            NormalizeKeyTextExpression = BuildNormalizeKeyText(keyColumn),
            DecoratorDelegationBlock = BuildSyncDelegationBlock(repository),
            BinaryColumnPropertyNames = binaryColumns,
            BinaryAccessorsBlock = BuildSyncBinaryAccessorsBlock(
                binaryColumns,
                repository.InterfaceName,
                repository.KeyTypeName
            ),
            DecoratorBinaryBlock = BuildSyncDecoratorBinaryBlock(
                binaryColumns,
                repository.KeyTypeName
            ),
            FillUnsetUnboundedBinariesBlock = BuildSyncFillUnsetUnboundedBinariesBlock(
                entity,
                options
            ),
            ParentReferencesBlock = BuildSyncParentReferencesBlock(
                entity,
                repository.EntityClassName,
                diagram,
                syncTargetIds
            ),
            ReferencingTableNamesBlock = BuildSyncReferencingTableNamesBlock(entity, diagram),
            LocalReferencesBlock = BuildSyncLocalReferencesBlock(entity, diagram),
            LocalUniqueLookupsBlock = BuildSyncLocalUniqueLookupsBlock(
                entity,
                repository.EntityClassName
            ),
        };
    }

    /// <summary>
    /// 記述子の <c>FillUnsetUnboundedBinaries</c>（NOT NULL の除外列が null なら空の値を入れる処理）を組み立てる。
    /// </summary>
    /// <remarks>
    /// 除外列が無い・NOT NULL の除外列が無いときは空文字（記述子の既定＝何もしない）。値オブジェクトは空配列から
    /// 作った値（<c>Create([])</c>＝無制限なので長さの検証に掛からない）、非 VO は空配列。
    /// </remarks>
    private string BuildSyncFillUnsetUnboundedBinariesBlock(
        Entity entity,
        CodeGenerationOptions options
    )
    {
        if (!options.ExcludeUnboundedBinaryColumns)
        {
            return string.Empty;
        }

        var statements = entity
            .Columns.Where(column =>
                !column.IsNullable && _columnTypes[column.Id].IsUnboundedBinary
            )
            .Select(column =>
            {
                var propertyName = _nameConverter.ToPropertyName(column.Name);
                var empty = ResolveValueObject(column) is { } valueObject
                    ? $"{valueObject.ClassName}.Create([])"
                    : "[]";

                return $"                entity.{propertyName} ??= {empty};";
            })
            .ToList();

        return statements.Count == 0
            ? string.Empty
            : "            FillUnsetUnboundedBinaries = static entity =>\n            {\n"
                + string.Join("\n", statements)
                + "\n            },";
    }

    /// <summary>
    /// 記述子の親参照（子の行から親のキーのテキストを読む関数の表）を組み立てる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 対象は列ペア 1 組で、親の主キー（同期対象は単一主キー）を参照し、親も同期対象のリレーションだけ（自己参照を
    /// 含む）。一意制約の列を参照する外部キーと複数列の外部キーは、親のキーを子の行から組み立てられないため出さない
    /// （docs の既知の制限）。
    /// </para>
    /// <para>
    /// キーのテキストは子の外部キー列へ主キーと同じ整形（<see cref="BuildFormatKey"/>）を当てて作る。ジャーナルの
    /// キーは「同じ値が同じ文字列になる」ことだけを要求するので、int と long、親と同じ値オブジェクト（FK 列の VO 型
    /// 統一）のどちらでも親の記述子が作る文字列と一致する。NULL の外部キーは「参照なし」＝null を返す。
    /// </para>
    /// </remarks>
    private string BuildSyncParentReferencesBlock(
        Entity entity,
        string entityClassName,
        ErDiagram diagram,
        IReadOnlySet<Guid> syncTargetIds
    )
    {
        var entitiesById = diagram.Entities.ToDictionary(candidate => candidate.Id);
        var columnOwners = diagram
            .Entities.SelectMany(owner => owner.Columns.Select(column => (column.Id, owner)))
            .ToDictionary(pair => pair.Id, pair => pair.owner);
        var references = new List<string>();

        foreach (var relationship in diagram.Relationships)
        {
            if (relationship.ColumnPairs.Count != 1)
            {
                continue;
            }

            var pair = relationship.ColumnPairs[0];

            if (
                !columnOwners.TryGetValue(pair.SourceColumnId, out var parent)
                || !columnOwners.TryGetValue(pair.TargetColumnId, out var child)
                || child.Id != entity.Id
                || !syncTargetIds.Contains(parent.Id)
                || !entitiesById.ContainsKey(parent.Id)
            )
            {
                continue;
            }

            var parentKeys = parent.Columns.Where(column => column.IsPrimaryKey).ToList();

            if (parentKeys.Count != 1 || parentKeys[0].Id != pair.SourceColumnId)
            {
                continue;
            }

            var foreignKey = entity.Columns.First(column => column.Id == pair.TargetColumnId);
            var propertyName = _nameConverter.ToPropertyName(foreignKey.Name);
            var format = BuildFormatKey("value", foreignKey, ResolveValueObject(foreignKey));

            references.Add(
                $"                new SyncParentReference<{entityClassName}>(\n"
                    + $"                    \"{EscapeNameForCSharpString(parent.TableName)}\",\n"
                    + $"                    entity => entity.{propertyName} is {{ }} value ? {format} : null\n"
                    + "                ),"
            );
        }

        return references.Count == 0
            ? string.Empty
            : "            ParentReferences =\n            [\n"
                + string.Join("\n", references)
                + "\n            ],";
    }

    /// <summary>記述子の被参照テーブル名（このテーブルを親として参照する子テーブル・図の全リレーションから）を組み立てる。</summary>
    /// <remarks>
    /// 同期対象かどうかを問わない。サーバーの削除を断るのはサーバーの外部キーで、同期対象外の子テーブルにも
    /// 外部キーはあるため。自己参照も含める（自分の子の行が削除を断り得る）。
    /// </remarks>
    private static string BuildSyncReferencingTableNamesBlock(Entity entity, ErDiagram diagram)
    {
        var columnOwners = diagram
            .Entities.SelectMany(owner => owner.Columns.Select(column => (column.Id, owner)))
            .ToDictionary(pair => pair.Id, pair => pair.owner);
        var names = new List<string>();

        foreach (var relationship in diagram.Relationships)
        {
            foreach (var pair in relationship.ColumnPairs)
            {
                if (
                    columnOwners.TryGetValue(pair.SourceColumnId, out var parent)
                    && parent.Id == entity.Id
                    && columnOwners.TryGetValue(pair.TargetColumnId, out var child)
                    && !names.Contains(child.TableName, SchemaNames.Comparer)
                )
                {
                    names.Add(child.TableName);
                }
            }
        }

        return names.Count == 0
            ? string.Empty
            : "            ReferencingTableNames = ["
                + string.Join(", ", names.Select(name => $"\"{EscapeNameForCSharpString(name)}\""))
                + "],";
    }

    /// <summary>記述子の、一意制約ごとの引き当ての並び（<c>LocalUniqueLookups</c>）を組み立てる。</summary>
    /// <remarks>
    /// 一意制約ごとに、行から制約の値を読む関数（値オブジェクトは内側の値）と、その値を持つローカルの行のキーを返す
    /// <c>SELECT "キー" FROM "表" WHERE "列0" = @values0 AND …</c>（ローカル＝SQLite の引用）を 1 組作る。ダウンロードで
    /// サーバーの行がローカルの行と一意な値でぶつかったとき、値を持つローカルの行を引き当ててサーバーから読み直す。
    /// 主キーが 1 列でない表・制約の列を解決できない制約は載せない。
    /// </remarks>
    private string BuildSyncLocalUniqueLookupsBlock(Entity entity, string entityClassName)
    {
        var keys = entity.Columns.Where(column => column.IsPrimaryKey).ToList();

        if (keys.Count != 1 || entity.UniqueConstraints.Count == 0)
        {
            return string.Empty;
        }

        var table = QuoteQualified(entity.TableName, "\"", "\"");
        var key = QuoteIdentifier(keys[0].Name, "\"", "\"");
        var lookups = new List<string>();

        foreach (var constraint in entity.UniqueConstraints)
        {
            var columns = constraint
                .ColumnIds.Select(id => entity.Columns.FirstOrDefault(column => column.Id == id))
                .ToList();

            if (columns.Count == 0 || columns.Any(column => column is null))
            {
                continue;
            }

            var reads = columns.Select(
                (column, index) =>
                {
                    var propertyName = _nameConverter.ToPropertyName(column!.Name);

                    return ResolveValueObject(column) is null
                        ? $"entity.{propertyName}"
                        : $"entity.{propertyName} is {{ }} value{index} ? (object?)value{index}.Value : null";
                }
            );
            var conditions = columns.Select(
                (column, index) => $"{QuoteIdentifier(column!.Name, "\"", "\"")} = @values{index}"
            );
            var sql = CSharpLiteral(
                $"SELECT {key} FROM {table} WHERE {string.Join(" AND ", conditions)}"
            );

            lookups.Add(
                $"                new SyncUniqueLookup<{entityClassName}>(\n"
                    + $"                    entity => [{string.Join(", ", reads)}],\n"
                    + $"                    {sql}\n"
                    + "                ),"
            );
        }

        return lookups.Count == 0
            ? string.Empty
            : "            LocalUniqueLookups =\n            [\n"
                + string.Join("\n", lookups)
                + "\n            ],";
    }

    /// <summary>
    /// 記述子の、この表を参照する外部キーの並び（<c>LocalReferences</c>）を組み立てる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 図の全リレーションのうち、このテーブルの主キーを列ペア 1 組で参照するものごとに
    /// <c>SELECT DISTINCT "子の列" FROM "子の表" WHERE "子の列" IN (@keys)</c> を 1 本作る（ローカル＝SQLite の引用）。
    /// 子の表が同期対象かどうかは問わない＝実行の範囲外の表・構築時に除外した表・自分自身の行も、ローカルで参照して
    /// いれば削除伝搬はその行を消さない。子の表の名前も載せ、実行時にローカル DB に無い表を照会から外す（同期対象の表
    /// だけでローカル DB を組む構成で、無い表への照会が同期全体を止めないため）。
    /// </para>
    /// <para>
    /// 一意制約を参照する外部キーと複数列の外部キーは載せない（親のキーを子の行の 1 列から読めない＝
    /// <c>ParentReferences</c> と同じ線引き）。
    /// </para>
    /// </remarks>
    private static string BuildSyncLocalReferencesBlock(Entity entity, ErDiagram diagram)
    {
        var columnOwners = diagram
            .Entities.SelectMany(owner =>
                owner.Columns.Select(column => (column.Id, Owner: owner, Column: column))
            )
            .ToDictionary(item => item.Id);
        var keys = entity.Columns.Where(column => column.IsPrimaryKey).ToList();
        var sqls = new List<string>();

        if (keys.Count != 1)
        {
            return string.Empty;
        }

        foreach (var relationship in diagram.Relationships)
        {
            if (relationship.ColumnPairs.Count != 1)
            {
                continue;
            }

            var pair = relationship.ColumnPairs[0];

            if (
                pair.SourceColumnId != keys[0].Id
                || !columnOwners.TryGetValue(pair.TargetColumnId, out var child)
            )
            {
                continue;
            }

            var childTable = QuoteQualified(child.Owner.TableName, "\"", "\"");
            var childColumn = QuoteIdentifier(child.Column.Name, "\"", "\"");
            var sql =
                $"new SyncLocalReference({CSharpLiteral(child.Owner.TableName)}, "
                + CSharpLiteral(
                    $"SELECT DISTINCT {childColumn} FROM {childTable} WHERE {childColumn} IN (@keys)"
                )
                + ")";

            if (!sqls.Contains(sql, StringComparer.Ordinal))
            {
                sqls.Add(sql);
            }
        }

        return sqls.Count == 0
            ? string.Empty
            : "            LocalReferences =\n            [\n"
                + string.Join("\n", sqls.Select(sql => $"                {sql},"))
                + "\n            ],";
    }

    /// <summary>
    /// 記述子へ載せる除外列（無制限バイナリ列）のアクセサ表（列名 → 読み書きのラムダ）を組み立てる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同期エンジンは特定のエンティティ型を知らないため、列ごとに名前の違う生成アクセサ
    /// （<c>Read{列}Async</c>）を直接は呼べない。列名で引ける薄い面（<c>ISyncBinaryColumns</c>）を
    /// 固定側の <c>SyncRepositoryBinaryColumns</c> が実装し、エンジンはその面だけを見てコピーする
    /// （＝転送経路が直結でも HTTP でも同じ 1 つの意味論になる）。
    /// </para>
    /// <para>
    /// ラムダの第 1 引数が <c>object</c> なのは、アクセサが具象契約（<c>I{Entity}Repository</c>）の
    /// メンバーで固定側から名指しできないため。具象型を知っているのはこの生成ラムダだけで、
    /// キャストもここに閉じる。
    /// </para>
    /// </remarks>
    private static string BuildSyncBinaryAccessorsBlock(
        IReadOnlyList<string> columns,
        string interfaceName,
        string keyTypeName
    )
    {
        if (columns.Count == 0)
        {
            return string.Empty;
        }

        var names = string.Join(", ", columns.Select(column => $"\"{column}\""));
        var entries = string.Join(
            "\n",
            columns.Select(column =>
                $"                [\"{column}\"] = new SyncBinaryColumnAccessor<{keyTypeName}>(\n"
                + "                    (repository, id, destination, cancellationToken) =>\n"
                + $"                        (({interfaceName})repository).Read{column}Async(id, destination, cancellationToken),\n"
                + "                    (repository, id, source, length, cancellationToken) =>\n"
                + $"                        (({interfaceName})repository).Write{column}Async(id, source, length, cancellationToken)\n"
                + "                ),"
            )
        );

        return $"            UnboundedBinaryColumnNames = [{names}],\n"
            + "            UnboundedBinaryAccessors = new Dictionary<\n"
            + "                string,\n"
            + $"                SyncBinaryColumnAccessor<{keyTypeName}>\n"
            + "            >(StringComparer.Ordinal)\n"
            + "            {\n"
            + entries
            + "\n            },";
    }

    /// <summary>
    /// ジャーナル記録デコレータの除外列アクセサ（読みは素通し・書きは journal-first で記録）を組み立てる。
    /// </summary>
    /// <remarks>
    /// blob だけを差し替える編集（行の通常列は変わらない）は Repository の他の入口を一切通らないため、
    /// ここで記録しないとオフライン編集として永久に検出されない。記録は
    /// <c>SyncOptions.IncludeUnboundedBinary</c> に依らず常時行う（送るかどうかは送信側の判断で、
    /// 生成時点ではどちらで呼ばれるか分からない）。記録と書き込みは基底の
    /// <c>RecordUnboundedBinaryWriteAsync</c> がローカルの書き込みロックの内側で 1 組として行い、
    /// <c>SyncSession.IsSuppressed</c> のとき（エンジン自身のコピー）は記録もロックもしない。
    /// </remarks>
    private static string BuildSyncDecoratorBinaryBlock(
        IReadOnlyList<string> columns,
        string keyTypeName
    )
    {
        if (columns.Count == 0)
        {
            return string.Empty;
        }

        var members = columns.Select(column =>
            "    /// <inheritdoc />\n"
            + $"    public Task<bool> Read{column}Async(\n"
            + $"        {keyTypeName} id,\n"
            + "        Stream destination,\n"
            + "        CancellationToken cancellationToken = default\n"
            + $"    ) => _inner.Read{column}Async(id, destination, cancellationToken);\n\n"
            + "    /// <inheritdoc />\n"
            + "    /// <remarks>\n"
            + "    /// A blob written on its own changes no ordinary column, so no other entry point records it. The\n"
            + "    /// intent is journaled first, exactly as every other write is, and the upload sends the row's\n"
            + "    /// current content (blob included) when it is asked to carry the excluded columns.\n"
            + "    /// </remarks>\n"
            + $"    public Task<bool> Write{column}Async(\n"
            + $"        {keyTypeName} id,\n"
            + "        Stream? source,\n"
            + "        long? length = null,\n"
            + "        CancellationToken cancellationToken = default\n"
            + "    ) =>\n"
            + "        RecordUnboundedBinaryWriteAsync(\n"
            + "            id,\n"
            + $"            \"{column}\",\n"
            + $"            () => _inner.Write{column}Async(id, source, length, cancellationToken),\n"
            + "            cancellationToken\n"
            + "        );"
        );

        return string.Join("\n\n", members);
    }

    /// <summary>ジャーナル記録デコレータへ追加する委譲メンバー（名前付きクエリ）を組み立てる</summary>
    /// <remarks>
    /// デコレータは <c>I{Entity}Repository</c> の全機能面を実装するため、契約へ載る追加メンバーもすべて素通しで
    /// 実装しなければならない。名前付きクエリは Repository 契約の構築時に組んだ委譲ブロック
    /// （<see cref="CSharpRepositoryModel.QueryDelegationBlock"/>）をそのまま使う（契約とのずれが構造的に
    /// 起きない。同じエンティティで <c>BuildQueryBlocks</c> を呼び直すと、ビルド全体で一意管理している
    /// 射影 DTO 名の重複判定に 1 回目が引っかかり、射影クエリの委譲だけが黙って抜けて CS0535 になる）。
    /// 重複事前チェックはランタイム共通面へ上がったため、素通しは基底
    /// <c>JournalingRepositoryBase</c> が 1 回だけ持つ＝ここでは出さない。
    /// </remarks>
    private static string BuildSyncDelegationBlock(CSharpRepositoryModel repository)
    {
        var members = new List<string>();

        if (!string.IsNullOrEmpty(repository.QueryDelegationBlock))
        {
            members.Add(repository.QueryDelegationBlock);
        }

        return string.Join("\n\n", members);
    }

    /// <summary>
    /// ドット付きテーブル名を最初の <c>.</c> で 2 分割してクォートする（DDL・生成 CRUD の
    /// <c>EntitySaveMetadata.QuoteTableName</c> と同じ規則）。
    /// </summary>
    /// <remarks>
    /// 全ドット分割にすると <c>a.b.c</c> のような名前で DDL・CRUD と同期 SQL のクォート結果が食い違い、
    /// 同じテーブルを指せなくなる（2 つ目以降のドットは名前の一部として扱う）。
    /// </remarks>
    private static string QuoteQualified(string name, string open, string close) =>
        SchemaQualifiedName.TrySplit(name, out var schema, out var table)
            ? $"{QuoteIdentifier(schema, open, close)}.{QuoteIdentifier(table, open, close)}"
            : QuoteIdentifier(name, open, close);

    /// <summary>SQL 識別子を方言のクォートで包む（終端クォート文字を二重化してクォートの脱出を防ぐ）</summary>
    /// <remarks>
    /// 図の名前は自由入力で <c>]</c>（SQL Server）や <c>"</c>（SQLite）を含み得る。二重化しないとクォートが
    /// そこで閉じ、以降が SQL のコードとして解釈される。DDL 側の <c>SqlIdentifier.Escape</c> /
    /// <c>SqliteIdentifier.Escape</c> と同じ規則（方言の終端文字を 2 つ重ねる）。
    /// </remarks>
    private static string QuoteIdentifier(string name, string open, string close) =>
        open + name.Replace(close, close + close) + close;

    /// <summary>文字列を C# の通常文字列リテラル（前後の <c>"</c> 込み）へ変換する</summary>
    /// <remarks>改行も空白 1 つへ畳む（畳まないと 1 行前提の通常リテラルが行をまたいで壊れる）</remarks>
    private static string CSharpLiteral(string value) =>
        "\"" + EscapeNameForCSharpString(value) + "\"";

    /// <summary>エンティティ変数からミラー版（byte[]）を読む式を組み立てる</summary>
    /// <remarks>
    /// VO 経路は列の NULL 許容宣言に依らず <c>?.Value</c>（＝実行時 null を素通しする）。「NOT NULL 宣言なら
    /// 実体も非 null」は同期の文脈では成り立たない: マルチターゲットの方言変換はローカル側の rowversion 列を
    /// 意図的に NULL 許容へ落とす（未同期行の版は空）し、削除の記録はキーだけのスタブから読むため列宣言と
    /// 無関係に版プロパティが未設定になる。<c>.Value</c> を出すとその両方が NullReferenceException になる。
    /// 実体が非 null のときの結果は変わらない。
    /// </remarks>
    private static string BuildRowVersionRead(
        string variable,
        string propertyName,
        CSharpValueObjectModel? valueObject
    ) => valueObject is null ? $"{variable}.{propertyName}" : $"{variable}.{propertyName}?.Value";

    /// <summary>エンティティへミラー版（byte[]）を書く式を組み立てる</summary>
    /// <remarks>
    /// 非 NULL 許容の列へ null を書くことはできないため空配列へ倒す（「まだ一度も同期していない」の表現は
    /// エンジン側が「null または長さ 0」で受けており、どちらの列定義でも同じ意味になる）。
    /// </remarks>
    private static string BuildRowVersionWrite(
        string propertyName,
        Column column,
        CSharpValueObjectModel? valueObject
    )
    {
        if (valueObject is null)
        {
            return column.IsNullable
                ? $"entity.{propertyName} = rowVersion"
                : $"entity.{propertyName} = rowVersion ?? []";
        }

        return column.IsNullable
            ? $"entity.{propertyName} = rowVersion is null ? null : {valueObject.ClassName}.Create(rowVersion)"
            : $"entity.{propertyName} = {valueObject.ClassName}.Create(rowVersion ?? [])";
    }

    /// <summary>キー順ページングの SQL（サーバー＝SQL Server）で、前のページの最後のキーと比べる式を組み立てる</summary>
    /// <remarks>
    /// 生 SQL のパラメータは型なしでバインドされ、文字列は nvarchar になる。varchar / char の主キーを nvarchar と
    /// 比べると列の側が nvarchar へ変換され、SQL_* 照合順序では比較の規則が ORDER BY の規則（列の型のまま）と
    /// 食い違う（例: <c>"a-c"</c> と <c>"ab"</c> の順が逆になる）。ページの境目で行を読み飛ばすので、文字列の主キーは
    /// 列と同じ型へ明示的にキャストする。型の名前と宣言長は SQL Server の型マッパーが渡すもの（<c>SqlDbTypeName</c>・
    /// <c>SqlDeclaredLength</c>）を使い、それ以外の型はそのまま比べる。
    /// </remarks>
    private string BuildServerAfterKey(Column keyColumn)
    {
        var typeInfo = _columnTypes[keyColumn.Id];
        var length = typeInfo.SqlDeclaredLength switch
        {
            -1 => "max",
            > 0 => typeInfo.SqlDeclaredLength.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            _ => null,
        };
        var sqlType = (typeInfo.SqlDbTypeName, length) switch
        {
            ("VarChar", { } n) => $"varchar({n})",
            ("NVarChar", { } n) => $"nvarchar({n})",
            ("Char", { } n) => $"char({n})",
            ("NChar", { } n) => $"nchar({n})",
            _ => null,
        };

        return sqlType is null ? "@afterKey" : $"CAST(@afterKey AS {sqlType})";
    }

    /// <summary>主キーをジャーナルのテキスト形へ変換する式を組み立てる</summary>
    /// <remarks>
    /// テキスト形は「往復できること」と「同じ値が同じ文字列になること」だけを要求する（照合は序数比較）。
    /// 文化圏に依存する既定の書式は使わず、数値・日時は不変文化・往復書式で書く。
    /// </remarks>
    private string BuildFormatKey(
        string variable,
        Column column,
        CSharpValueObjectModel? valueObject
    )
    {
        var access = valueObject is null ? variable : $"{variable}.Value";
        var underlying = _columnTypes[column.Id].TypeName.TrimEnd('?');

        return underlying switch
        {
            "string" => access,
            "byte[]" => $"Convert.ToHexString({access})",
            "Guid" => $"{access}.ToString()",
            // DateTime は Kind を落とす（どちらの DB も Kind を持たず、読み戻した行は Unspecified になる＝Kind つきの
            // テキストは同じ行のテキストと一致しない）。DateTimeOffset はオフセットが値の一部なのでそのまま
            "DateTime" =>
                $"DateTime.SpecifyKind({access}, DateTimeKind.Unspecified).ToString(\"o\", CultureInfo.InvariantCulture)",
            "DateTimeOffset" => $"{access}.ToString(\"o\", CultureInfo.InvariantCulture)",
            // decimal はスケールを正規化する（G29＝末尾ゼロなし）。SQL Server は宣言スケールへ揃えて返し
            // （decimal(10,2) の 1.5 は 1.50）、SQLite（Microsoft.Data.Sqlite）は書き込んだ綴りを保存する
            // （1.5 のまま＝実測）ため、素の ToString だと同じ行のキー文字列がサーバーとローカルで食い違い、
            // 削除伝搬・ack・pending の序数照合がすべて外れる
            "decimal" => $"{access}.ToString(\"G29\", CultureInfo.InvariantCulture)",
            // TimeSpan は書式を取る ToString(IFormatProvider) を持たない（既定の形ではコンパイルできない）。TimeOnly の既定の形は
            // 秒を落として往復しない（"HH:mm"）。どちらも不変の往復書式で書く。DateOnly は既定の形（"MM/dd/yyyy"）で往復する
            "TimeSpan" => $"{access}.ToString(\"c\", CultureInfo.InvariantCulture)",
            "TimeOnly" => $"{access}.ToString(\"O\", CultureInfo.InvariantCulture)",
            _ => $"{access}.ToString(CultureInfo.InvariantCulture)",
        };
    }

    /// <summary>以前の版が書いたキーのテキストをいまの形へ書き直す関数の式を組み立てる（形が変わっていない型は空文字）</summary>
    /// <remarks>
    /// DateTime の主キーは、以前は Kind つきの往復書式（<c>Z</c> やローカルのオフセット）で書いていた。ローカル DB に
    /// 残ったジャーナル・受理記録・コピー待ちのテキストを、ランの前に Kind を落とした形へ書き直す。
    /// </remarks>
    private string BuildNormalizeKeyText(Column column) =>
        _columnTypes[column.Id].TypeName.TrimEnd('?') == "DateTime"
            ? "SyncKeyText.WithoutDateTimeKind"
            : string.Empty;

    /// <summary>ジャーナルのテキスト形から主キーを復元する式を組み立てる</summary>
    private string BuildParseKey(Column column, CSharpValueObjectModel? valueObject)
    {
        var underlying = _columnTypes[column.Id].TypeName.TrimEnd('?');
        var parsed = underlying switch
        {
            "string" => "keyText",
            "byte[]" => "Convert.FromHexString(keyText)",
            "Guid" => "Guid.Parse(keyText)",
            "DateTime" =>
                "DateTime.Parse(SyncKeyText.WithoutDateTimeKind(keyText), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)",
            "DateTimeOffset" =>
                "DateTimeOffset.Parse(keyText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)",
            "TimeSpan" => "TimeSpan.ParseExact(keyText, \"c\", CultureInfo.InvariantCulture)",
            "TimeOnly" => "TimeOnly.ParseExact(keyText, \"O\", CultureInfo.InvariantCulture)",
            _ => $"{underlying}.Parse(keyText, CultureInfo.InvariantCulture)",
        };

        return valueObject is null ? parsed : $"{valueObject.ClassName}.Create({parsed})";
    }
}
