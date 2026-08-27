using System.IO;
using System.Text;
using System.Text.Json;
using QuickER.Documents;
using QuickER.Model;

namespace QuickER.Mcp.Tools;

/// <summary>
/// ファイルベースの ER 図ツール実行ホスト。各ツール呼び出しを
/// 「<see cref="JsonStorageService.Load"/>（DiagramDocument 読込）→ 意味モデル（<see cref="ErDiagram"/>）変更 →
/// <see cref="JsonStorageService.SaveAtomic"/>（保存）」で完結させるステートレスな実行器。
/// </summary>
/// <remarks>
/// 意味論は GUI 側 <c>QuickER.Services.ErDiagramDynamicTools</c>（ViewModel 操作）を忠実にミラーするが、
/// 対象は ViewModel ではなく <see cref="ErDiagram"/> / <see cref="Entity"/> / <see cref="Column"/> /
/// <see cref="Relationship"/>（意味モデル）。Undo/Redo は概念ごと持たない。結果テキストはすべて英語。
/// レイアウト（<see cref="DiagramDocument.Layout"/>）は原則温存し、削除エンティティの孤児レイアウトのみ除去する。
/// </remarks>
public static partial class DocumentErDiagramToolHost
{
    /// <summary>作成専用ツール（既存ファイルを変更しない）の名前</summary>
    public const string CreateDiagramToolName = "create_diagram";

    /// <summary>読み取り系ツール（新しいフォーマットでも警告付きで続行する）の名前</summary>
    private const string GetSummaryToolName = "get_diagram_summary";

    /// <summary>
    /// <c>target_dbms</c> に指定できるプロバイダ識別名（tool 定義の enum と一致させる）。
    /// 正本は <see cref="QueryToolCore.SupportedDbms"/>（クエリツールの SQL 方言キーと同一集合）。
    /// </summary>
    private static string[] SupportedDbms => QueryToolCore.SupportedDbms;

    /// <summary>ツール名でディスパッチし、対象ファイルに対してツールを実行する</summary>
    /// <param name="toolName">ツール名</param>
    /// <param name="file">対象図の JSON ファイルパス</param>
    /// <param name="arguments">ツール固有の引数（<c>file</c> を含んでいてもよい・未使用）</param>
    /// <returns>実行結果テキスト（英語）と成否のタプル</returns>
    public static (string Result, bool Success) Execute(
        string toolName,
        string file,
        JsonElement arguments
    )
    {
        try
        {
            return toolName switch
            {
                CreateDiagramToolName => CreateDiagram(file, arguments),
                GetSummaryToolName => GetDiagramSummary(file),
                ListQueriesToolName => ListQueries(file),
                SetQueryToolName => Mutate(file, doc => SetQuery(doc, arguments)),
                RemoveQueryToolName => Mutate(file, doc => RemoveQuery(doc, arguments)),
                "add_entity" => Mutate(file, doc => AddEntity(doc, arguments)),
                "remove_entity" => Mutate(file, doc => RemoveEntity(doc, arguments)),
                "add_column" => Mutate(file, doc => AddColumn(doc, arguments)),
                "remove_column" => Mutate(file, doc => RemoveColumn(doc, arguments)),
                "set_entity_property" => Mutate(file, doc => SetEntityProperty(doc, arguments)),
                "set_column_property" => Mutate(file, doc => SetColumnProperty(doc, arguments)),
                "add_relationship" => Mutate(file, doc => AddRelationship(doc, arguments)),
                "remove_relationship" => Mutate(file, doc => RemoveRelationship(doc, arguments)),
                "set_unique_constraint" => Mutate(file, doc => SetUniqueConstraint(doc, arguments)),
                "remove_unique_constraint" => Mutate(
                    file,
                    doc => RemoveUniqueConstraint(doc, arguments)
                ),
                _ => ($"Unsupported tool: {toolName}", false),
            };
        }
        catch (Exception ex)
        {
            // ツール実行中の例外はエラーテキストとして返し、プロセスを落とさない（GUI 側と同方針）
            return ($"Error: {ex.Message}", false);
        }
    }

    // ---------------- load / save orchestration ----------------

    /// <summary>変更系ツールの共通処理。読込・ガードを行い、変更が成功したときのみ保存する</summary>
    /// <param name="file">対象ファイル</param>
    /// <param name="operation">読み込んだ文書を変更し結果を返す処理（成功時のみ保存される）</param>
    private static (string, bool) Mutate(
        string file,
        Func<DiagramDocument, (string Result, bool Success)> operation
    )
    {
        var (document, error) = LoadForMutation(file);

        if (error is not null)
        {
            return (error, false);
        }

        var (result, success) = operation(document!);

        if (success)
        {
            // ユーザーの実ファイルへ書き戻すため原子的に差し替える（書き込み途中の中断・
            // ディスク満杯で既存の図が半端な JSON になるのを防ぐ）
            JsonStorageService.SaveAtomic(file, document!);
        }

        return (result, success);
    }

    /// <summary>変更系ツール向けに文書を読み込む（不在・非 DiagramDocument・新フォーマットは拒否）</summary>
    private static (DiagramDocument? Document, string? Error) LoadForMutation(string file)
    {
        if (!File.Exists(file))
        {
            return (
                null,
                $"Diagram file not found: {file}. To create a new diagram, call {CreateDiagramToolName} first."
            );
        }

        var (document, error) = TryReadDocument(file);

        if (error is not null)
        {
            return (null, error);
        }

        if (document!.IsNewerFormat)
        {
            return (
                null,
                $"Diagram file '{file}' was saved in a newer format (version {document.Version} > supported {DiagramDocument.CurrentVersion}); refusing to modify to avoid discarding unknown data."
            );
        }

        return (document, null);
    }

    /// <summary>ファイルを読み、DiagramDocument として妥当か検証したうえで読み込む</summary>
    /// <remarks>
    /// 検証は <see cref="JsonStorageService.TryLoad"/> に委ね、失敗種別を MCP ツールの応答文言
    /// （英語正本）へ翻訳するだけを担う。
    /// </remarks>
    private static (DiagramDocument? Document, string? Error) TryReadDocument(string file)
    {
        if (JsonStorageService.TryLoad(file, out var document, out var error, out var exception))
        {
            return (document, null);
        }

        return (null, DescribeLoadError(file, error, exception));
    }

    /// <summary>読込失敗の種別を MCP ツールの応答文言へ翻訳する（ユーザー向け文字列は英語正本）</summary>
    private static string DescribeLoadError(
        string file,
        DocumentLoadError error,
        Exception? exception
    ) =>
        error switch
        {
            DocumentLoadError.ReadFailed =>
                $"Failed to read diagram file '{file}': {exception!.Message}",
            DocumentLoadError.InvalidJson =>
                $"Diagram file '{file}' is not valid JSON: {exception!.Message}",
            DocumentLoadError.NotDiagramDocument =>
                $"Diagram file '{file}' is not a DiagramDocument (expected an object with 'Version' and 'Schema'). Refusing to treat unrelated JSON as a diagram.",
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
        };

    // ---------------- create_diagram ----------------

    /// <summary>新規のスキーマのみ文書（レイアウトなし）を作成する。既存ファイルは上書きしない</summary>
    private static (string, bool) CreateDiagram(string file, JsonElement args)
    {
        var dbmsInput = GetString(args, "target_dbms");

        if (string.IsNullOrWhiteSpace(dbmsInput))
        {
            return (
                $"target_dbms is required (one of: {string.Join(", ", SupportedDbms)}).",
                false
            );
        }

        var dbms = SupportedDbms.FirstOrDefault(d =>
            string.Equals(d, dbmsInput, StringComparison.OrdinalIgnoreCase)
        );

        if (dbms is null)
        {
            return (
                $"Invalid target_dbms '{dbmsInput}'. Must be one of: {string.Join(", ", SupportedDbms)}.",
                false
            );
        }

        if (File.Exists(file))
        {
            return (
                $"Diagram file already exists: {file}. {CreateDiagramToolName} only creates new files; use the other tools to modify an existing diagram.",
                false
            );
        }

        // 親ディレクトリを黙って掘らない（存在しなければエラー）
        var directory = Path.GetDirectoryName(file);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            return (
                $"Parent directory does not exist: {directory}. Create the directory first.",
                false
            );
        }

        // Layout=null によりスキーマのみ文書となり、GUI で開くと全体自動整列される
        var document = new DiagramDocument
        {
            Version = DiagramDocument.CurrentVersion,
            Schema = new ErDiagram { TargetDbms = dbms },
            Layout = null,
        };
        JsonStorageService.SaveAtomic(file, document);

        return ($"Created diagram '{file}' (target DBMS: {dbms}).", true);
    }

    // ---------------- get_diagram_summary ----------------

    /// <summary>ER 図の概要（テーブル・カラム・リレーション）を英語テキスト化する</summary>
    private static (string, bool) GetDiagramSummary(string file)
    {
        if (!File.Exists(file))
        {
            return (
                $"Diagram file not found: {file}. To create a new diagram, call {CreateDiagramToolName} first.",
                false
            );
        }

        var (document, error) = TryReadDocument(file);

        if (error is not null)
        {
            return (error, false);
        }

        var sb = new StringBuilder();

        // 読み取り系は新フォーマットでも警告付きで続行する（CLI と同方針）
        if (document!.IsNewerFormat)
        {
            sb.AppendLine(
                $"Warning: this diagram was saved in a newer format (version {document.Version} > supported {DiagramDocument.CurrentVersion}); unknown data may be omitted. Showing a best-effort summary."
            );
            sb.AppendLine();
        }

        var schema = document.Schema;
        sb.AppendLine($"Tables: {schema.Entities.Count}");
        sb.AppendLine($"Relationships: {schema.Relationships.Count}");
        sb.AppendLine();

        foreach (var entity in schema.Entities)
        {
            sb.AppendLine($"[{entity.TableName}]");

            if (!string.IsNullOrWhiteSpace(entity.Description))
            {
                sb.AppendLine($"  Description: {entity.Description}");
            }

            foreach (var col in entity.Columns)
            {
                var flags = new List<string>();

                if (col.IsPrimaryKey)
                {
                    flags.Add("PK");
                }

                if (col.IsForeignKey)
                {
                    flags.Add("FK");
                }

                if (!col.IsNullable)
                {
                    flags.Add("NOT NULL");
                }

                var flagsText = flags.Count > 0 ? $" ({string.Join(", ", flags)})" : string.Empty;
                var colDesc = !string.IsNullOrWhiteSpace(col.Description)
                    ? $" // {col.Description}"
                    : string.Empty;
                sb.AppendLine($"  - {col.Name}: {col.DataType}{flagsText}{colDesc}");
            }

            AppendUniqueConstraints(sb, entity);
        }

        if (schema.Relationships.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Relationships:");

            foreach (var rel in schema.Relationships)
            {
                var source = FindEntityById(schema, rel.SourceEntityId);
                var target = FindEntityById(schema, rel.TargetEntityId);
                var sourceName = source?.TableName ?? "(unknown)";
                var targetName = target?.TableName ?? "(unknown)";
                sb.AppendLine(
                    $"  {sourceName} → {targetName} ({rel.Type}{DescribeColumnPairs(rel, source, target)}){DescribeConstraintName(rel)}"
                );
            }
        }

        return (sb.ToString(), true);
    }

    // ---------------- entity / column / relationship operations ----------------

    /// <summary>エンティティを追加する（列は自動生成しない・レイアウトも作らない）</summary>
    private static (string, bool) AddEntity(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name") ?? "NewTable";
        var desc = GetString(args, "description") ?? string.Empty;

        document.Schema.Entities.Add(new Entity { TableName = tableName, Description = desc });

        return ($"Added table '{tableName}'.", true);
    }

    /// <summary>指定テーブル名のエンティティと、接続する全リレーション・孤児レイアウトを削除する</summary>
    private static (string, bool) RemoveEntity(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return ("table_name is required.", false);
        }

        var schema = document.Schema;
        var entity = FindEntity(schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        // 削除エンティティを端点に持つリレーションも併せて除去する（孤立した線を残さない）
        schema.Relationships.RemoveAll(r =>
            r.SourceEntityId == entity.Id || r.TargetEntityId == entity.Id
        );
        schema.Entities.Remove(entity);

        // 孤児になったレイアウトエントリを削除する（ファイルを清潔に保つ）
        document.Layout?.Remove(entity.Id);

        return ($"Removed table '{tableName}'.", true);
    }

    /// <summary>指定テーブルへカラムを追加する</summary>
    private static (string, bool) AddColumn(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");
        var dataType = GetString(args, "data_type") ?? "nvarchar(100)";

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return ("table_name and column_name are required.", false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        // is_primary_key は明示 true のときのみ真、is_nullable は明示 false のときのみ偽（GUI と同一）
        var isPk =
            args.TryGetProperty("is_primary_key", out var isPkEl)
            && isPkEl.ValueKind == JsonValueKind.True;
        var isNullable =
            !args.TryGetProperty("is_nullable", out var isNullEl)
            || isNullEl.ValueKind != JsonValueKind.False;
        var desc = GetString(args, "description") ?? string.Empty;

        entity.Columns.Add(
            new Column
            {
                Name = columnName,
                DataType = dataType,
                IsPrimaryKey = isPk,
                IsNullable = isNullable,
                Description = desc,
            }
        );

        return ($"Added column '{columnName}' to table '{tableName}'.", true);
    }

    /// <summary>指定テーブルからカラムを削除し、そのカラムを参照するリレーションの参照をクリアする</summary>
    /// <remarks>
    /// 削除カラムを構成列に含む一意制約は制約ごと削除する（構成列を 1 つ失った制約を黙って別の意味の制約へ
    /// 変質させないため。GUI の <c>RemoveColumnCommand</c> と同じ規則）
    /// </remarks>
    private static (string, bool) RemoveColumn(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return ("table_name and column_name are required.", false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        var column = entity.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

        if (column is null)
        {
            return ($"Column '{columnName}' not found.", false);
        }

        entity.Columns.Remove(column);

        // 削除カラムを構成列に含む一意制約は制約ごと取り除く（GUI の削除後挙動をミラー）
        entity.UniqueConstraints.RemoveAll(constraint => constraint.ColumnIds.Contains(column.Id));

        // 削除カラムを構成列に含むリレーションは、列ペアをすべてクリアする（GUI の削除後挙動をミラー）
        // ＝残りのペアだけを保った「意味の違う外部キー」へ縮めない
        foreach (var relationship in document.Schema.Relationships)
        {
            if (
                relationship.ColumnPairs.Any(pair =>
                    pair.SourceColumnId == column.Id || pair.TargetColumnId == column.Id
                )
            )
            {
                relationship.ColumnPairs.Clear();
            }
        }

        return ($"Removed column '{columnName}' from table '{tableName}'.", true);
    }

    /// <summary>エンティティのテーブル名・メモ・説明のうち、指定されたものを更新する</summary>
    private static (string, bool) SetEntityProperty(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return ("table_name is required.", false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        var changed = new List<string>();

        if (
            args.TryGetProperty("new_table_name", out var newNameEl)
            && newNameEl.ValueKind == JsonValueKind.String
        )
        {
            entity.TableName = newNameEl.GetString()!;
            changed.Add("table name");
        }

        if (args.TryGetProperty("memo", out var memoEl) && memoEl.ValueKind == JsonValueKind.String)
        {
            entity.Memo = memoEl.GetString()!;
            changed.Add("memo");
        }

        if (
            args.TryGetProperty("description", out var descEl)
            && descEl.ValueKind == JsonValueKind.String
        )
        {
            entity.Description = descEl.GetString()!;
            changed.Add("description");
        }

        if (changed.Count == 0)
        {
            return ("No properties specified to change.", false);
        }

        return ($"Updated {string.Join(", ", changed)} of table '{tableName}'.", true);
    }

    /// <summary>カラムの説明・データ型・NULL 許容のうち、指定されたものを更新する</summary>
    private static (string, bool) SetColumnProperty(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return ("table_name and column_name are required.", false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        var column = entity.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

        if (column is null)
        {
            return ($"Column '{columnName}' not found.", false);
        }

        var changed = new List<string>();

        if (
            args.TryGetProperty("description", out var descEl)
            && descEl.ValueKind == JsonValueKind.String
        )
        {
            column.Description = descEl.GetString()!;
            changed.Add("description");
        }

        if (
            args.TryGetProperty("data_type", out var dataTypeEl)
            && dataTypeEl.ValueKind == JsonValueKind.String
        )
        {
            column.DataType = dataTypeEl.GetString()!;
            changed.Add("data type");
        }

        if (
            args.TryGetProperty("is_nullable", out var isNullEl)
            && isNullEl.ValueKind is JsonValueKind.True or JsonValueKind.False
        )
        {
            column.IsNullable = isNullEl.GetBoolean();
            changed.Add("nullability");
        }

        if (changed.Count == 0)
        {
            return (
                "No properties specified to change. Specify at least one of description, data_type, or is_nullable.",
                false
            );
        }

        return (
            $"Updated {string.Join(", ", changed)} of column '{columnName}' in table '{tableName}'.",
            true
        );
    }

    /// <summary>2 テーブル間にリレーションを追加する</summary>
    /// <remarks>
    /// source_columns / target_columns で明示された列をそのまま使用し、両方の省略時のみ
    /// 「親 PK 全列の自動ペア化」で既定解決する（GUI の作成フローと同じ意味論）
    /// </remarks>
    private static (string, bool) AddRelationship(DiagramDocument document, JsonElement args)
    {
        var sourceTable = GetString(args, "source_table");
        var targetTable = GetString(args, "target_table");
        var typeStr = GetString(args, "relationship_type") ?? "OneToMany";

        if (string.IsNullOrWhiteSpace(sourceTable) || string.IsNullOrWhiteSpace(targetTable))
        {
            return ("source_table and target_table are required.", false);
        }

        var schema = document.Schema;
        var source = FindEntity(schema, sourceTable);
        var target = FindEntity(schema, targetTable);

        if (source is null)
        {
            return ($"Table '{sourceTable}' not found.", false);
        }

        if (target is null)
        {
            return ($"Table '{targetTable}' not found.", false);
        }

        var relType = typeStr switch
        {
            "OneToOne" => RelationshipType.OneToOne,
            "ManyToMany" => RelationshipType.ManyToMany,
            _ => RelationshipType.OneToMany,
        };

        var (columnPairs, pairError) = ResolveRelationshipColumnPairs(
            source,
            target,
            args,
            schema.Relationships
        );

        if (pairError is not null)
        {
            return (pairError, false);
        }

        // 多対多は中間テーブルを介する概念表現のため列ペアを持たない（GUI の VM 側整合をミラー）
        if (relType == RelationshipType.ManyToMany)
        {
            columnPairs!.Clear();
        }

        schema.Relationships.Add(
            new Relationship
            {
                SourceEntityId = source.Id,
                TargetEntityId = target.Id,
                Type = relType,
                ColumnPairs = columnPairs!,
                ConstraintName = $"FK_{target.TableName}_{source.TableName}",
            }
        );

        // 参照先の外部キー列へ FK フラグを付与する（GUI の LockRelationshipColumns をミラー）
        foreach (var pair in columnPairs!)
        {
            var targetColumn = target.Columns.FirstOrDefault(c => c.Id == pair.TargetColumnId);

            if (targetColumn is not null)
            {
                targetColumn.IsForeignKey = true;
            }
        }

        return ($"Added relationship '{sourceTable}' → '{targetTable}'.", true);
    }

    /// <summary>
    /// <c>source_columns</c> / <c>target_columns</c>（並行配列）から列ペアを解決する。
    /// 両方が省略された場合のみ親 PK 全列の自動ペア化へフォールバックする
    /// </summary>
    private static (
        List<RelationshipColumnPair>? Pairs,
        string? Error
    ) ResolveRelationshipColumnPairs(
        Entity source,
        Entity target,
        JsonElement args,
        IEnumerable<Relationship> existingRelationships
    )
    {
        var (sourceNames, sourceError) = GetColumnNames(args, "source_columns");

        if (sourceError is not null)
        {
            return (null, sourceError);
        }

        var (targetNames, targetError) = GetColumnNames(args, "target_columns");

        if (targetError is not null)
        {
            return (null, targetError);
        }

        if (sourceNames is null && targetNames is null)
        {
            return (
                ForeignKeyColumnResolver.ResolveColumnPairs(source, target, existingRelationships),
                null
            );
        }

        if (sourceNames is null || targetNames is null)
        {
            return (
                null,
                "source_columns and target_columns must be given together (omit both to derive the mapping from the parent's primary key columns)."
            );
        }

        if (sourceNames.Count != targetNames.Count)
        {
            return (
                null,
                $"source_columns and target_columns must have the same length (got {sourceNames.Count} and {targetNames.Count}); they are parallel arrays of column pairs."
            );
        }

        var pairs = new List<RelationshipColumnPair>();
        var usedSourceIds = new HashSet<Guid>();
        var usedTargetIds = new HashSet<Guid>();

        for (var i = 0; i < sourceNames.Count; i++)
        {
            var sourceColumn = FindColumn(source, sourceNames[i]);

            if (sourceColumn is null)
            {
                return (
                    null,
                    $"Column '{sourceNames[i]}' not found in table '{source.TableName}'."
                );
            }

            if (!usedSourceIds.Add(sourceColumn.Id))
            {
                return (
                    null,
                    $"Column '{sourceColumn.Name}' is listed more than once in source_columns."
                );
            }

            var targetColumn = FindColumn(target, targetNames[i]);

            if (targetColumn is null)
            {
                return (
                    null,
                    $"Column '{targetNames[i]}' not found in table '{target.TableName}'."
                );
            }

            if (!usedTargetIds.Add(targetColumn.Id))
            {
                return (
                    null,
                    $"Column '{targetColumn.Name}' is listed more than once in target_columns."
                );
            }

            pairs.Add(new RelationshipColumnPair(sourceColumn.Id, targetColumn.Id));
        }

        return (pairs, null);
    }

    /// <summary>列名配列の引数を取り出す（未指定は <c>null</c>・型不正や空配列はエラー）</summary>
    private static (List<string>? Names, string? Error) GetColumnNames(
        JsonElement args,
        string propertyName
    )
    {
        if (!args.TryGetProperty(propertyName, out var element))
        {
            return (null, null);
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return (null, null);
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return (null, $"{propertyName} must be an array of column names.");
        }

        var names = new List<string>();

        foreach (var item in element.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString())
            )
            {
                return (null, $"{propertyName} must contain non-empty column names.");
            }

            names.Add(item.GetString()!);
        }

        if (names.Count == 0)
        {
            return (null, $"{propertyName} must contain at least one column name.");
        }

        return (names, null);
    }

    /// <summary>指定テーブルの列を名前で検索する（大文字小文字を区別しない）</summary>
    private static Column? FindColumn(Entity entity, string columnName) =>
        entity.Columns.FirstOrDefault(column =>
            string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>指定した参照元・参照先テーブル間のリレーションを削除する</summary>
    /// <remarks>
    /// 同じ向きのテーブル対に複数のリレーションがある場合は <c>constraint_name</c> で特定する。
    /// 無指定で複数一致したときは黙って先頭を消さず、候補の制約名を挙げてエラーにする
    /// </remarks>
    private static (string, bool) RemoveRelationship(DiagramDocument document, JsonElement args)
    {
        var sourceTable = GetString(args, "source_table");
        var targetTable = GetString(args, "target_table");

        if (string.IsNullOrWhiteSpace(sourceTable) || string.IsNullOrWhiteSpace(targetTable))
        {
            return ("source_table and target_table are required.", false);
        }

        var schema = document.Schema;

        var matches = schema
            .Relationships.Where(r =>
            {
                var source = FindEntityById(schema, r.SourceEntityId);
                var target = FindEntityById(schema, r.TargetEntityId);

                return source is not null
                    && target is not null
                    && string.Equals(
                        source.TableName,
                        sourceTable,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && string.Equals(
                        target.TableName,
                        targetTable,
                        StringComparison.OrdinalIgnoreCase
                    );
            })
            .ToList();

        if (matches.Count == 0)
        {
            return ($"Relationship '{sourceTable}' → '{targetTable}' not found.", false);
        }

        var constraintName = GetString(args, "constraint_name");

        if (!string.IsNullOrWhiteSpace(constraintName))
        {
            var byName = matches
                .Where(r =>
                    string.Equals(
                        r.ConstraintName,
                        constraintName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToList();

            if (byName.Count == 0)
            {
                return (
                    $"Relationship '{sourceTable}' → '{targetTable}' with constraint name '{constraintName}' not found. Candidates: {DescribeConstraintNames(matches)}.",
                    false
                );
            }

            matches = byName;
        }

        if (matches.Count > 1)
        {
            return (
                $"Several relationships connect '{sourceTable}' → '{targetTable}'; specify constraint_name to choose one. Candidates: {DescribeConstraintNames(matches)}.",
                false
            );
        }

        schema.Relationships.Remove(matches[0]);

        return ($"Removed relationship '{sourceTable}' → '{targetTable}'.", true);
    }

    /// <summary>候補リレーションの制約名を列挙する（名前なしは <c>(unnamed)</c>）</summary>
    private static string DescribeConstraintNames(IEnumerable<Relationship> relationships) =>
        string.Join(
            ", ",
            relationships.Select(r =>
                string.IsNullOrWhiteSpace(r.ConstraintName) ? "(unnamed)" : r.ConstraintName!
            )
        );

    // ---------------- unique constraint operations ----------------

    /// <summary>一意制約を定義する（同じ列集合の制約があれば名前・列順を差し替え、無ければ追加する）</summary>
    /// <remarks>
    /// 照合キーは (テーブル, 列集合) で、列の順序・大文字小文字は問わない（UNIQUE の意味論が列の並びに
    /// 依存しないため）。既存が見つかった場合は Id を温存したまま丸ごと再定義する（set_query と同じ upsert 流儀）。
    /// </remarks>
    private static (string, bool) SetUniqueConstraint(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return ("table_name is required.", false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        var (columns, error) = ResolveConstraintColumns(entity, args);

        if (error is not null)
        {
            return (error, false);
        }

        var columnIds = columns!.Select(column => column.Id).ToList();
        var name = GetString(args, "name");
        var normalizedName = string.IsNullOrWhiteSpace(name) ? null : name;
        var existing = FindUniqueConstraintByColumnSet(entity, columnIds);
        var columnText = string.Join(", ", columns!.Select(column => column.Name));

        if (existing is not null)
        {
            existing.Name = normalizedName;
            existing.ColumnIds = columnIds;

            return (
                $"Updated unique constraint on table '{entity.TableName}' (columns: {columnText}).",
                true
            );
        }

        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = normalizedName, ColumnIds = columnIds }
        );

        return (
            $"Added unique constraint on table '{entity.TableName}' (columns: {columnText}).",
            true
        );
    }

    /// <summary>列集合で特定した一意制約を削除する</summary>
    private static (string, bool) RemoveUniqueConstraint(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return ("table_name is required.", false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        var (columns, error) = ResolveConstraintColumns(entity, args);

        if (error is not null)
        {
            return (error, false);
        }

        var columnText = string.Join(", ", columns!.Select(column => column.Name));
        var existing = FindUniqueConstraintByColumnSet(
            entity,
            columns!.Select(column => column.Id).ToList()
        );

        if (existing is null)
        {
            return (
                $"Table '{entity.TableName}' has no unique constraint over exactly these columns: {columnText}.",
                false
            );
        }

        entity.UniqueConstraints.Remove(existing);

        return (
            $"Removed unique constraint from table '{entity.TableName}' (columns: {columnText}).",
            true
        );
    }

    /// <summary><c>columns</c> 引数（列名の配列）をエンティティのカラムへ解決する</summary>
    /// <returns>解決したカラム（宣言順）と、失敗時のエラーテキスト</returns>
    private static (List<Column>? Columns, string? Error) ResolveConstraintColumns(
        Entity entity,
        JsonElement args
    )
    {
        if (
            !args.TryGetProperty("columns", out var columnsEl)
            || columnsEl.ValueKind != JsonValueKind.Array
        )
        {
            return (null, "columns is required and must be an array of column names.");
        }

        var resolved = new List<Column>();

        foreach (var item in columnsEl.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString())
            )
            {
                return (null, "columns must contain non-empty column names.");
            }

            var columnName = item.GetString()!;
            var column = entity.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)
            );

            if (column is null)
            {
                return (null, $"Column '{columnName}' not found in table '{entity.TableName}'.");
            }

            // 同じ列を 2 回並べた制約は意味を持たないため拒否する（DB 側でもエラーになる）
            if (resolved.Any(existing => existing.Id == column.Id))
            {
                return (null, $"Column '{column.Name}' is listed more than once in columns.");
            }

            resolved.Add(column);
        }

        if (resolved.Count == 0)
        {
            return (null, "columns must contain at least one column name.");
        }

        return (resolved, null);
    }

    /// <summary>構成列の集合（順序を問わない）が一致する一意制約を探す</summary>
    private static UniqueConstraint? FindUniqueConstraintByColumnSet(
        Entity entity,
        IReadOnlyList<Guid> columnIds
    )
    {
        var target = new HashSet<Guid>(columnIds);

        return entity.UniqueConstraints.FirstOrDefault(constraint =>
            target.SetEquals(constraint.ColumnIds)
        );
    }

    /// <summary>要約テキストへエンティティの一意制約（解決済み名＋構成列）を追記する</summary>
    /// <remarks>構成列を解決できない制約（空・壊れた参照）は DDL 生成と同じ規則で読み飛ばす</remarks>
    private static void AppendUniqueConstraints(StringBuilder sb, Entity entity)
    {
        var lines = new List<string>();

        foreach (var constraint in entity.UniqueConstraints)
        {
            var columnNames = new List<string>();

            foreach (var columnId in constraint.ColumnIds)
            {
                var column = entity.Columns.FirstOrDefault(c => c.Id == columnId);

                if (column is not null)
                {
                    columnNames.Add(column.Name);
                }
            }

            if (columnNames.Count == 0 || columnNames.Count != constraint.ColumnIds.Count)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(constraint.Name)
                ? UniqueConstraint.SynthesizeName(entity.TableName, columnNames)
                : constraint.Name!;
            lines.Add($"    - {name} ({string.Join(", ", columnNames)})");
        }

        if (lines.Count == 0)
        {
            return;
        }

        sb.AppendLine("  Unique constraints:");

        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }
    }

    /// <summary>要約テキスト用に外部キーの列ペアを <c>, FK: (親列 → 子列, …)</c> 形式で表す</summary>
    /// <remarks>列ペアなし（多対多・未割当）や解決できない参照を含む場合は空文字を返す</remarks>
    private static string DescribeColumnPairs(
        Relationship relationship,
        Entity? source,
        Entity? target
    )
    {
        if (relationship.ColumnPairs.Count == 0 || source is null || target is null)
        {
            return string.Empty;
        }

        var texts = new List<string>();

        foreach (var pair in relationship.ColumnPairs)
        {
            var sourceColumn = source.Columns.FirstOrDefault(c => c.Id == pair.SourceColumnId);
            var targetColumn = target.Columns.FirstOrDefault(c => c.Id == pair.TargetColumnId);

            if (sourceColumn is null || targetColumn is null)
            {
                return string.Empty;
            }

            texts.Add($"{sourceColumn.Name} → {targetColumn.Name}");
        }

        return $", FK: ({string.Join(", ", texts)})";
    }

    /// <summary>要約テキスト用に外部キー制約名を <c> [名前]</c> 形式で表す（未設定は空文字）</summary>
    private static string DescribeConstraintName(Relationship relationship) =>
        string.IsNullOrWhiteSpace(relationship.ConstraintName)
            ? string.Empty
            : $" [{relationship.ConstraintName}]";

    // ---------------- helpers ----------------

    /// <summary>テーブル名でエンティティを検索する（大文字小文字を区別しない・最初の一致）</summary>
    private static Entity? FindEntity(ErDiagram schema, string tableName) =>
        schema.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>ID でエンティティを検索する</summary>
    private static Entity? FindEntityById(ErDiagram schema, Guid id) =>
        schema.Entities.FirstOrDefault(e => e.Id == id);

    /// <summary>JSON 引数から文字列プロパティを取得する（無い・型不一致なら null）</summary>
    private static string? GetString(JsonElement element, string propertyName)
    {
        return
            element.TryGetProperty(propertyName, out var val)
            && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;
    }
}
