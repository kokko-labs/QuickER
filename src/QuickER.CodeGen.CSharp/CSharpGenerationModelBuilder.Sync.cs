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

        return targets
            .Select(entity =>
                BuildSyncTable(
                    entity,
                    repositoriesByEntityClass[_nameConverter.ToEntityClassName(entity.TableName)],
                    options
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
    /// ダウンロードの適用は親→子、削除は子→親でなければ FK 制約に触れる。循環参照や自己参照がある図では
    /// 完全な順序が存在しないため、解けた分だけ先に出し、残りは図の宣言順で後ろへ付ける（生成は完走させる）。
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
                // 循環（相互参照）は解けない。残りを宣言順でそのまま出す
                ordered.AddRange(remaining);
                break;
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

    /// <summary>1 テーブル分の同期生成モデルを組み立てる</summary>
    private CSharpSyncTableModel BuildSyncTable(
        Entity entity,
        CSharpRepositoryModel repository,
        CodeGenerationOptions options
    )
    {
        var keyColumn = entity.Columns.First(column => column.IsPrimaryKey);

        // rowversion 列の有無がモードを決める（版あり＝増分＋版ガード／版なし＝後勝ち・キー順全量）
        var rowVersionColumn = FindRowVersionColumn(entity);
        var isVersionless = rowVersionColumn is null;
        var repositoryName = repository.InterfaceName[1..^"Repository".Length];

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
                        .Select(column => SqlServerQuoteOpen + column.Name + SqlServerQuoteClose)
                );

        var serverTable = QuoteQualified(entity.TableName, SqlServerQuoteOpen, SqlServerQuoteClose);
        var localTable = QuoteQualified(entity.TableName, "\"", "\"");
        var serverKey = SqlServerQuoteOpen + keyColumn.Name + SqlServerQuoteClose;
        var localKey = "\"" + keyColumn.Name + "\"";
        var rowVersionPropertyName = rowVersionColumn is null
            ? string.Empty
            : _nameConverter.ToPropertyName(rowVersionColumn.Name);
        var serverRowVersion = rowVersionColumn is null
            ? string.Empty
            : SqlServerQuoteOpen + rowVersionColumn.Name + SqlServerQuoteClose;
        var localRowVersion = rowVersionColumn is null
            ? string.Empty
            : "\"" + rowVersionColumn.Name + "\"";

        return new CSharpSyncTableModel
        {
            EntityClassName = repository.EntityClassName,
            InterfaceName = repository.InterfaceName,
            KeyTypeName = repository.KeyTypeName,
            TableName = entity.TableName,
            IsVersionless = isVersionless,
            TableBaseClassName = isVersionless ? "VersionlessSyncTable" : "SyncTable",
            SourceClassName = $"{repositoryName}DirectSyncSource",
            HttpSourceClassName = $"Http{repositoryName}SyncSource",
            RemoteRouteName = repositoryName,
            TableClassName = $"{repositoryName}SyncTable",
            DecoratorClassName = $"Journaling{repositoryName}Repository",
            // デコレータの汎用基底も SyncTable と同じく版の有無でサブクラス階層を選ぶ（削除記録の形だけが違う）
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
            // 版なしテーブルの全量走査はキー昇順のページング（@afterKey はキー型の値がそのままバインドされる）
            ServerPageFirstSql = isVersionless
                ? CSharpLiteral(
                    $"SELECT TOP (@batchSize) {serverSelectList} FROM {serverTable} ORDER BY {serverKey}"
                )
                : string.Empty,
            ServerPageAfterSql = isVersionless
                ? CSharpLiteral(
                    $"SELECT TOP (@batchSize) {serverSelectList} FROM {serverTable} "
                        + $"WHERE {serverKey} > @afterKey ORDER BY {serverKey}"
                )
                : string.Empty,
            ServerKeysSql = CSharpLiteral($"SELECT {serverKey} FROM {serverTable}"),
            LocalAnchorSql = isVersionless
                ? string.Empty
                : CSharpLiteral($"SELECT MAX({localRowVersion}) FROM {localTable}"),
            LocalKeysSql = CSharpLiteral($"SELECT {localKey} FROM {localTable}"),
            LocalExistingKeysSql = CSharpLiteral(
                $"SELECT {localKey} FROM {localTable} WHERE {localKey} IN (@keys)"
            ),
            LocalDeleteAllSql = CSharpLiteral($"DELETE FROM {localTable}"),
            // 版なしテーブルのミラー版は存在しない＝記録（SyncGraphRecorder の Delete）は常に null を添える
            RowVersionReadExpression = rowVersionColumn is null
                ? "null"
                : BuildRowVersionRead(
                    "entity",
                    rowVersionPropertyName,
                    rowVersionColumn,
                    rowVersionValueObject
                ),
            RowVersionWriteExpression = rowVersionColumn is null
                ? string.Empty
                : BuildRowVersionWrite(
                    rowVersionPropertyName,
                    rowVersionColumn,
                    rowVersionValueObject
                ),
            FormatKeyExpression = BuildFormatKey("key", keyColumn, keyValueObject),
            FormatKeyEntityExpression = BuildFormatKey(
                $"entity.{keyPropertyName}",
                keyColumn,
                keyValueObject
            ),
            FormatKeyIdExpression = BuildFormatKey("id", keyColumn, keyValueObject),
            ParseKeyExpression = BuildParseKey(keyColumn, keyValueObject),
            DecoratorDelegationBlock = BuildSyncDelegationBlock(entity, repository, options),
            BinaryColumnPropertyNames = binaryColumns,
            BinaryInterfaceDeclaration =
                binaryColumns.Count == 0
                    ? string.Empty
                    : $", ISyncBinaryColumns<{repository.KeyTypeName}>",
            DirectSourceBinaryBlock = BuildSyncBinaryAccessorBlock(
                binaryColumns,
                repository.KeyTypeName,
                column => $"serverRepository.Read{column}Async(id, destination, cancellationToken)",
                column =>
                    $"serverRepository.Write{column}Async(id, source, length, cancellationToken)",
                $"public ISyncBinaryColumns<{repository.KeyTypeName}>? BinaryColumns => this;"
            ),
            HttpSourceBinaryBlock = BuildSyncBinaryAccessorBlock(
                binaryColumns,
                repository.KeyTypeName,
                column =>
                    $"DownloadUnboundedBinaryColumnAsync(\"{column}\", id, destination, cancellationToken)",
                column =>
                    $"UploadUnboundedBinaryColumnAsync(\"{column}\", id, source, length, cancellationToken)",
                $"public override ISyncBinaryColumns<{repository.KeyTypeName}>? BinaryColumns => this;"
            ),
            TableBinaryBlock = BuildSyncBinaryAccessorBlock(
                binaryColumns,
                repository.KeyTypeName,
                column => $"localRepository.Read{column}Async(id, destination, cancellationToken)",
                column =>
                    $"localRepository.Write{column}Async(id, source, length, cancellationToken)",
                $"protected override ISyncBinaryColumns<{repository.KeyTypeName}>? LocalBinaryColumns => this;",
                "public override"
            ),
            DecoratorBinaryBlock = BuildSyncDecoratorBinaryBlock(
                binaryColumns,
                entity.TableName,
                repository.KeyTypeName,
                BuildFormatKey("id", keyColumn, keyValueObject)
            ),
        };
    }

    /// <summary>
    /// 除外列（無制限バイナリ列）を列名で引くアクセサ実装（<c>ISyncBinaryColumns</c>）を組み立てる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同期エンジンは特定のエンティティ型を知らないため、列ごとに名前の違う生成アクセサ
    /// （<c>Read{列}Async</c>）を直接は呼べない。列名で引ける薄い面をサーバー側・ローカル側の双方が実装し、
    /// エンジンはその面だけを見てコピーする（＝転送経路が直結でも HTTP でも同じ 1 つの意味論になる）。
    /// </para>
    /// <para>
    /// 未知の列名は既定へ落とさず例外にする（呼び出し側が <c>UnboundedBinaryColumnNames</c> 以外を渡すのは
    /// 実装の誤りで、黙って何もしないと「コピーしたつもりの空」がそのまま残る）。
    /// </para>
    /// </remarks>
    private static string BuildSyncBinaryAccessorBlock(
        IReadOnlyList<string> columns,
        string keyTypeName,
        Func<string, string> readCall,
        Func<string, string> writeCall,
        string surfaceDeclaration,
        // 記述子は基底（ISyncTable 側の同名プロパティ）を override する＝1 つのプロパティが 2 つの面を満たす
        string columnNamesModifier = "public"
    )
    {
        if (columns.Count == 0)
        {
            return string.Empty;
        }

        const string unknownColumn =
            "            _ => throw new ArgumentOutOfRangeException(\n"
            + "                nameof(columnName),\n"
            + "                columnName,\n"
            + "                \"The column is not an unbounded binary column of this table.\"\n"
            + "            ),";

        var names = string.Join(", ", columns.Select(column => $"\"{column}\""));
        var readArms = string.Join(
            "\n",
            columns.Select(column => $"            \"{column}\" => {readCall(column)},")
        );
        var writeArms = string.Join(
            "\n",
            columns.Select(column => $"            \"{column}\" => {writeCall(column)},")
        );

        return "    /// <inheritdoc />\n    "
            + surfaceDeclaration
            + "\n\n    /// <inheritdoc />\n"
            + $"    {columnNamesModifier} IReadOnlyList<string> UnboundedBinaryColumnNames => [{names}];\n\n"
            + "    /// <inheritdoc />\n"
            + "    public Task<bool> ReadUnboundedBinaryAsync(\n"
            + "        string columnName,\n"
            + $"        {keyTypeName} id,\n"
            + "        Stream destination,\n"
            + "        CancellationToken cancellationToken = default\n"
            + "    ) =>\n"
            + "        columnName switch\n        {\n"
            + readArms
            + "\n"
            + unknownColumn
            + "\n        };\n\n"
            + "    /// <inheritdoc />\n"
            + "    public Task<bool> WriteUnboundedBinaryAsync(\n"
            + "        string columnName,\n"
            + $"        {keyTypeName} id,\n"
            + "        Stream? source,\n"
            + "        long? length,\n"
            + "        CancellationToken cancellationToken = default\n"
            + "    ) =>\n"
            + "        columnName switch\n        {\n"
            + writeArms
            + "\n"
            + unknownColumn
            + "\n        };";
    }

    /// <summary>
    /// ジャーナル記録デコレータの除外列アクセサ（読みは素通し・書きは journal-first で記録）を組み立てる。
    /// </summary>
    /// <remarks>
    /// blob だけを差し替える編集（行の通常列は変わらない）は Repository の他の入口を一切通らないため、
    /// ここで記録しないとオフライン編集として永久に検出されない。記録は
    /// <c>SyncOptions.IncludeUnboundedBinary</c> に依らず常時行う（送るかどうかは送信側の判断で、
    /// 生成時点ではどちらで呼ばれるか分からない）。<c>SyncJournal.RecordAsync</c> 自身が
    /// <c>SyncSession.IsSuppressed</c> のとき何もしないので、エンジン自身のコピーは記録されない。
    /// </remarks>
    private static string BuildSyncDecoratorBinaryBlock(
        IReadOnlyList<string> columns,
        string tableName,
        string keyTypeName,
        string formatKeyIdExpression
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
            + $"    public async Task<bool> Write{column}Async(\n"
            + $"        {keyTypeName} id,\n"
            + "        Stream? source,\n"
            + "        long? length = null,\n"
            + "        CancellationToken cancellationToken = default\n"
            + "    )\n    {\n"
            + $"        await Journal.RecordAsync(\n            \"{tableName}\",\n            {formatKeyIdExpression},\n"
            + "            SyncJournalOperation.Upsert,\n            null,\n            cancellationToken\n        )\n"
            + "            .ConfigureAwait(false);\n\n"
            + $"        return await _inner.Write{column}Async(id, source, length, cancellationToken)\n"
            + "            .ConfigureAwait(false);\n    }"
        );

        return string.Join("\n\n", members);
    }

    /// <summary>ジャーナル記録デコレータへ追加する委譲メンバー（重複事前チェック・名前付きクエリ）を組み立てる</summary>
    /// <remarks>
    /// デコレータは <c>I{Entity}Repository</c> の全機能面を実装するため、契約へ載る追加メンバーもすべて素通しで
    /// 実装しなければならない。重複事前チェックは契約が存在する限り必ず 1 本出るので無条件、名前付きクエリは
    /// クエリブロック側が同じシグネチャで組み立てたものを使う（契約とのずれが構造的に起きない）。
    /// </remarks>
    private string BuildSyncDelegationBlock(
        Entity entity,
        CSharpRepositoryModel repository,
        CodeGenerationOptions options
    )
    {
        var members = new List<string>
        {
            "    /// <inheritdoc />\n"
                + "    public Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(\n"
                + $"        {repository.EntityClassName} entity,\n"
                + "        CancellationToken cancellationToken = default\n"
                + "    ) => _inner.CheckUniquenessAsync(entity, cancellationToken);",
        };

        var queryBlocks = BuildQueryBlocks(
            entity,
            repository.InterfaceName[1..^"Repository".Length],
            options,
            []
        );

        if (!string.IsNullOrEmpty(queryBlocks.DelegationBlock))
        {
            members.Add(queryBlocks.DelegationBlock);
        }

        return string.Join("\n\n", members);
    }

    /// <summary>ドット区切りのテーブル名を方言のクォートで分割クォートする（5 方言共通の規則に合わせる）</summary>
    private static string QuoteQualified(string name, string open, string close) =>
        string.Join(".", name.Split('.').Select(part => open + part + close));

    /// <summary>文字列を C# の通常文字列リテラル（前後の <c>"</c> 込み）へ変換する</summary>
    private static string CSharpLiteral(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>エンティティ変数からミラー版（byte[]）を読む式を組み立てる</summary>
    private static string BuildRowVersionRead(
        string variable,
        string propertyName,
        Column column,
        CSharpValueObjectModel? valueObject
    )
    {
        if (valueObject is null)
        {
            return $"{variable}.{propertyName}";
        }

        return column.IsNullable
            ? $"{variable}.{propertyName}?.Value"
            : $"{variable}.{propertyName}.Value";
    }

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
            "DateTime" or "DateTimeOffset" =>
                $"{access}.ToString(\"o\", CultureInfo.InvariantCulture)",
            _ => $"{access}.ToString(CultureInfo.InvariantCulture)",
        };
    }

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
                "DateTime.Parse(keyText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)",
            "DateTimeOffset" =>
                "DateTimeOffset.Parse(keyText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)",
            _ => $"{underlying}.Parse(keyText, CultureInfo.InvariantCulture)",
        };

        return valueObject is null ? parsed : $"{valueObject.ClassName}.Create({parsed})";
    }

    /// <summary>
    /// グラフ保存のジャーナル記録クラス（<c>SyncGraphRecorder</c>）の全文を組み立てる（同期支援が無効なら空文字）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// デコレータの SaveAsync はルートの RowState しか見えないが、保存側（EntityGraphSaver）はカスケード
    /// ナビゲーションを辿って子孫も書く。記録が保存の決定手順をミラーしないと、カスケード子の変更が
    /// ジャーナルに載らず「アップロードされない・削除伝搬にローカル行が消される・削除再生がサーバー FK に
    /// 阻まれて同期が恒久失敗する」というサイレント損失になる。
    /// </para>
    /// <para>
    /// 生成対象の型集合は「同期対象をルートとするカスケード閉包のうち、自身または子孫に同期対象を含む型」。
    /// 対象を含まない部分木は保存側が書いても記録すべきものが無いため、走査ごと刈る。閉包内の非対象型は
    /// 自身を記録せず子へ降りるだけのメソッドになる（対象→非対象→対象と挟まる混在トポロジ対応）。
    /// 走査はコード生成が図から静的に組み立てる（リフレクションなし・保存側と同じ図が正本）。
    /// </para>
    /// </remarks>
    private string BuildSyncGraphRecorder(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, List<NavigationInfo>> navigationsByEntity,
        IReadOnlyList<CSharpSyncTableModel> syncTables
    )
    {
        if (syncTables.Count == 0)
        {
            return string.Empty;
        }

        var targetsByTable = syncTables.ToDictionary(
            table => table.TableName,
            StringComparer.Ordinal
        );
        var entitiesByTable = new Dictionary<string, Entity>(StringComparer.Ordinal);

        foreach (var entity in diagram.Entities)
        {
            entitiesByTable.TryAdd(entity.TableName, entity);
        }

        // 子方向（カスケード）ナビゲーションの辺。保存側の EnumerateCascadeChildren と同じ向き＝親参照は辿らない
        var edges = new Dictionary<Guid, List<(NavigationInfo Nav, Entity Child)>>();

        foreach (var entity in diagram.Entities)
        {
            var list = new List<(NavigationInfo, Entity)>();

            if (navigationsByEntity.TryGetValue(entity.Id, out var navigations))
            {
                foreach (var nav in navigations)
                {
                    if (
                        !nav.IsParentReference
                        && entitiesByTable.TryGetValue(nav.TargetTableName, out var child)
                    )
                    {
                        list.Add((nav, child));
                    }
                }
            }

            edges[entity.Id] = list;
        }

        // contributes: 自身または子孫のいずれかが同期対象（循環カスケードがあり得るため不動点で解く）
        var contributes = diagram.Entities.ToDictionary(
            entity => entity.Id,
            entity => targetsByTable.ContainsKey(entity.TableName)
        );
        var changed = true;

        while (changed)
        {
            changed = false;

            foreach (var entity in diagram.Entities)
            {
                if (
                    !contributes[entity.Id]
                    && edges[entity.Id].Any(edge => contributes[edge.Child.Id])
                )
                {
                    contributes[entity.Id] = true;
                    changed = true;
                }
            }
        }

        // included: デコレータが記録の起点にする同期対象ルートから、contributes な辺だけを辿って到達できる型
        var included = new HashSet<Guid>();
        var queue = new Queue<Entity>(
            diagram.Entities.Where(entity => targetsByTable.ContainsKey(entity.TableName))
        );

        while (queue.Count > 0)
        {
            var entity = queue.Dequeue();

            if (!included.Add(entity.Id))
            {
                continue;
            }

            foreach (var (_, child) in edges[entity.Id])
            {
                if (contributes[child.Id] && !included.Contains(child.Id))
                {
                    queue.Enqueue(child);
                }
            }
        }

        var members = OrderEntitiesByForeignKey(diagram)
            .Where(entity => included.Contains(entity.Id))
            .Select(entity =>
                BuildSyncGraphRecorderMembers(
                    _nameConverter.ToEntityClassName(entity.TableName),
                    targetsByTable.GetValueOrDefault(entity.TableName),
                    edges[entity.Id]
                        .Where(edge => contributes[edge.Child.Id])
                        .Select(edge => edge.Nav)
                        .ToList()
                )
            )
            .ToList();

        return "/// <summary>\n"
            + "/// Records the journal entries a graph save is about to need, before the save itself runs (\"journal-first\").\n"
            + "/// </summary>\n"
            + "/// <remarks>\n"
            + "/// <para>\n"
            + "/// The traversal mirrors the decision procedure of the graph saver: a removed node has its cascade subtree\n"
            + "/// recorded as deletes (children first, each regardless of its own state) followed by the node itself, an added\n"
            + "/// or updated node is recorded as an upsert, and an unchanged node contributes nothing of its own - but its\n"
            + "/// children are still visited while the save cascades, because a changed child under an unchanged root is\n"
            + "/// written all the same. Only synchronised tables are recorded; a table on the path that is not synchronised\n"
            + "/// is walked through without an entry of its own.\n"
            + "/// </para>\n"
            + "/// <para>\n"
            + "/// Recording the whole graph first keeps the journal-first safety order for every row the save is going to\n"
            + "/// touch: an entry whose write then fails or is rolled back describes a row the upload re-reads and settles\n"
            + "/// without a round trip.\n"
            + "/// </para>\n"
            + "/// </remarks>\n"
            + "public static class SyncGraphRecorder\n"
            + "{\n"
            + string.Join("\n\n", members)
            + "\n}";
    }

    /// <summary>1 つの参加型ぶんの SyncGraphRecorder メンバー群（RecordSaveAsync ほか）を組み立てる</summary>
    /// <param name="entityClassName">エンティティクラス名</param>
    /// <param name="target">同期対象ならその生成モデル（テーブル名・キー整形・ミラー版式の供給元）・非対象なら null</param>
    /// <param name="children">カスケード子ナビゲーション（対象を含む部分木のものだけに刈られている）</param>
    private static string BuildSyncGraphRecorderMembers(
        string entityClassName,
        CSharpSyncTableModel? target,
        IReadOnlyList<NavigationInfo> children
    )
    {
        var members = new List<string>
        {
            BuildRecorderSaveMethod(entityClassName, target, children),
            BuildRecorderDeleteGraphMethod(entityClassName, target, children),
        };

        // 子を持つ対象型だけ、自身の削除記録を別メソッドへ切り出す（cascadeDelete=false の枝と
        // RecordDeleteGraphAsync の両方が使う）。子なしの対象型は RecordDeleteGraphAsync 自体が自身の記録になる
        if (target is not null && children.Count > 0)
        {
            members.Add(BuildRecorderDeleteMethod(entityClassName, target));
        }

        return string.Join("\n\n", members);
    }

    /// <summary>RecordSaveAsync（保存側 SaveAsync の RowState 分岐をミラーする記録メソッド）を組み立てる</summary>
    private static string BuildRecorderSaveMethod(
        string entityClassName,
        CSharpSyncTableModel? target,
        IReadOnlyList<NavigationInfo> children
    )
    {
        var body = new List<string>();

        // Removed: cascadeDelete なら部分木ごと・さもなくば自身のみ（保存側と同じく子への保存再帰はしない）
        string removedBranch;

        if (children.Count == 0)
        {
            // 子なし＝cascadeDelete の別なく自身の削除だけ（RecordDeleteGraphAsync が自身の記録そのもの）
            removedBranch =
                "        if (entity.RowState == RowState.Removed)\n"
                + "        {\n"
                + "            await RecordDeleteGraphAsync(journal, entity, cancellationToken).ConfigureAwait(false);\n"
                + "\n"
                + "            return;\n"
                + "        }";
        }
        else if (target is not null)
        {
            removedBranch =
                "        if (entity.RowState == RowState.Removed)\n"
                + "        {\n"
                + "            if (cascadeDelete)\n"
                + "            {\n"
                + "                await RecordDeleteGraphAsync(journal, entity, cancellationToken)\n"
                + "                    .ConfigureAwait(false);\n"
                + "            }\n"
                + "            else\n"
                + "            {\n"
                + "                await RecordDeleteAsync(journal, entity, cancellationToken).ConfigureAwait(false);\n"
                + "            }\n"
                + "\n"
                + "            return;\n"
                + "        }";
        }
        else
        {
            // 非対象型は自身の記録が無い＝cascadeDelete のときだけ部分木の対象を記録する
            removedBranch =
                "        if (entity.RowState == RowState.Removed)\n"
                + "        {\n"
                + "            if (cascadeDelete)\n"
                + "            {\n"
                + "                await RecordDeleteGraphAsync(journal, entity, cancellationToken)\n"
                + "                    .ConfigureAwait(false);\n"
                + "            }\n"
                + "\n"
                + "            return;\n"
                + "        }";
        }

        body.Add(removedBranch);

        if (target is not null)
        {
            body.Add(
                "        if (entity.RowState != RowState.Unchanged)\n"
                    + "        {\n"
                    + "            await journal.RecordAsync(\n"
                    + $"                \"{target.TableName}\",\n"
                    + $"                {target.FormatKeyEntityExpression},\n"
                    + "                SyncJournalOperation.Upsert,\n"
                    + "                null,\n"
                    + "                cancellationToken\n"
                    + "            )\n"
                    + "                .ConfigureAwait(false);\n"
                    + "        }"
            );
        }

        if (children.Count > 0)
        {
            var loops = string.Join(
                "\n\n",
                children.Select(child =>
                    child.IsCollection
                        ? $"            foreach (var child in entity.{child.PropertyName})\n"
                            + "            {\n"
                            + "                if (child is not null)\n"
                            + "                {\n"
                            + "                    await RecordSaveAsync(journal, child, cascadeSave, cascadeDelete, cancellationToken)\n"
                            + "                        .ConfigureAwait(false);\n"
                            + "                }\n"
                            + "            }"
                        : $"            if (entity.{child.PropertyName} is not null)\n"
                            + "            {\n"
                            + $"                await RecordSaveAsync(journal, entity.{child.PropertyName}, cascadeSave, cascadeDelete, cancellationToken)\n"
                            + "                    .ConfigureAwait(false);\n"
                            + "            }"
                )
            );
            body.Add("        if (cascadeSave)\n        {\n" + loops + "\n        }");
        }

        return $"    /// <summary>Records the entries for a graph save rooted at this {entityClassName} (the cascade children included).</summary>\n"
            + "    public static async Task RecordSaveAsync(\n"
            + "        SyncJournal journal,\n"
            + $"        {entityClassName} entity,\n"
            + "        bool cascadeSave,\n"
            + "        bool cascadeDelete,\n"
            + "        CancellationToken cancellationToken = default\n"
            + "    )\n"
            + "    {\n"
            + "        ArgumentNullException.ThrowIfNull(journal);\n"
            + "        ArgumentNullException.ThrowIfNull(entity);\n"
            + "\n"
            + string.Join("\n\n", body)
            + "\n    }";
    }

    /// <summary>RecordDeleteGraphAsync（保存側 DeleteGraphAsync＝子から順の部分木削除をミラーする記録メソッド）を組み立てる</summary>
    private static string BuildRecorderDeleteGraphMethod(
        string entityClassName,
        CSharpSyncTableModel? target,
        IReadOnlyList<NavigationInfo> children
    )
    {
        if (children.Count == 0)
        {
            // 子なしの対象型: 部分木の記録＝自身の削除記録（非対象の子なし型は included に入らないためここへ来ない）
            return $"    /// <summary>Records the delete of this {entityClassName}, carrying the mirrored version the row holds.</summary>\n"
                + "    private static Task RecordDeleteGraphAsync(\n"
                + "        SyncJournal journal,\n"
                + $"        {entityClassName} entity,\n"
                + "        CancellationToken cancellationToken\n"
                + "    ) =>\n"
                + "        journal.RecordAsync(\n"
                + $"            \"{target!.TableName}\",\n"
                + $"            {target.FormatKeyEntityExpression},\n"
                + "            SyncJournalOperation.Delete,\n"
                + $"            {target.RowVersionReadExpression},\n"
                + "            cancellationToken\n"
                + "        );";
        }

        var loops = string.Join(
            "\n\n",
            children.Select(child =>
                child.IsCollection
                    ? $"        foreach (var child in entity.{child.PropertyName})\n"
                        + "        {\n"
                        + "            if (child is not null)\n"
                        + "            {\n"
                        + "                await RecordDeleteGraphAsync(journal, child, cancellationToken).ConfigureAwait(false);\n"
                        + "            }\n"
                        + "        }"
                    : $"        if (entity.{child.PropertyName} is not null)\n"
                        + "        {\n"
                        + $"            await RecordDeleteGraphAsync(journal, entity.{child.PropertyName}, cancellationToken)\n"
                        + "                .ConfigureAwait(false);\n"
                        + "        }"
            )
        );

        var summary = target is null
            ? $"Walks the subtree of this {entityClassName} and records the synchronised descendants as deletes (this table itself is not synchronised)."
            : $"Records the whole subtree of this {entityClassName} as deletes, children first (the saver removes them regardless of their own state).";
        var selfRecord = target is null
            ? string.Empty
            : "\n\n        await RecordDeleteAsync(journal, entity, cancellationToken).ConfigureAwait(false);";

        return $"    /// <summary>{summary}</summary>\n"
            + "    private static async Task RecordDeleteGraphAsync(\n"
            + "        SyncJournal journal,\n"
            + $"        {entityClassName} entity,\n"
            + "        CancellationToken cancellationToken\n"
            + "    )\n"
            + "    {\n"
            + loops
            + selfRecord
            + "\n    }";
    }

    /// <summary>RecordDeleteAsync（自身 1 行の削除記録＝グラフに載っている実体のミラー版を添える）を組み立てる</summary>
    private static string BuildRecorderDeleteMethod(
        string entityClassName,
        CSharpSyncTableModel target
    ) =>
        $"    /// <summary>Records the delete of this {entityClassName}, carrying the mirrored version the row holds.</summary>\n"
        + "    private static Task RecordDeleteAsync(\n"
        + "        SyncJournal journal,\n"
        + $"        {entityClassName} entity,\n"
        + "        CancellationToken cancellationToken\n"
        + "    ) =>\n"
        + "        journal.RecordAsync(\n"
        + $"            \"{target.TableName}\",\n"
        + $"            {target.FormatKeyEntityExpression},\n"
        + "            SyncJournalOperation.Delete,\n"
        + $"            {target.RowVersionReadExpression},\n"
        + "            cancellationToken\n"
        + "        );";
}
