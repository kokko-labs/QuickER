using System.IO;
using System.Text;
using System.Text.Json;
using QuickER.Documents;
using QuickER.Model;
using static QuickER.Mcp.Tools.SchemaLookup;
using static QuickER.Mcp.Tools.ToolArguments;

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
/// 同じプロセス内の呼び出しは <see cref="DiagramFileGate"/> で 1 本ずつに並べる（読込から保存までの途中へ
/// 別の呼び出しを割り込ませない）。
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
            // 同じプロセス内の並行呼び出しを 1 本ずつに並べる（読込 → 変更 → 保存の途中へ別の呼び出しが
            // 割り込むと、後から保存したほうが先の更新を消す。読み取り系も保存と重なると失敗するので同じロックに入れる）
            return DiagramFileGate.Run(() => Dispatch(toolName, file, arguments));
        }
        catch (Exception ex)
        {
            // ツール実行中の例外はエラーテキストとして返し、プロセスを落とさない（GUI 側と同方針）
            return ($"Error: {ex.Message}", false);
        }
    }

    /// <summary>ツール名で振り分けて実行する（呼び出し側が <see cref="DiagramFileGate"/> のロックを取っている）</summary>
    private static (string Result, bool Success) Dispatch(
        string toolName,
        string file,
        JsonElement arguments
    ) =>
        toolName switch
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
            "set_primary_key" => Mutate(file, doc => SetPrimaryKey(doc, arguments)),
            "set_unique_constraint" => Mutate(file, doc => SetUniqueConstraint(doc, arguments)),
            "remove_unique_constraint" => Mutate(
                file,
                doc => RemoveUniqueConstraint(doc, arguments)
            ),
            _ => ($"Unsupported tool: {toolName}", false),
        };

    // ---------------- load / save orchestration ----------------

    /// <summary>
    /// 変更系ツールの共通処理。読込・ガードを行い、変更が成功したときのみ外部キー印を当て直して
    /// （<see cref="ReleaseForeignKeyMarks"/>）保存する
    /// </summary>
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

        var managedBefore = CollectRelationshipChildColumnIds(document!.Schema);
        var (result, success) = operation(document);

        if (success)
        {
            ReleaseForeignKeyMarks(document.Schema, managedBefore);

            // ユーザーの実ファイルへ書き戻すため原子的に差し替える（書き込み途中の中断・
            // ディスク満杯で既存の図が半端な JSON になるのを防ぐ）
            JsonStorageService.SaveAtomic(file, document!);
        }

        return (result, success);
    }

    /// <summary>全リレーションの列ペアに子列（外部キー側）として現れる列の Id を集める</summary>
    private static HashSet<Guid> CollectRelationshipChildColumnIds(ErDiagram schema) =>
        schema
            .Relationships.SelectMany(relationship => relationship.ColumnPairs)
            .Select(pair => pair.TargetColumnId)
            .ToHashSet();

    /// <summary>
    /// 変更の前にリレーションの子列だった列のうち、変更の後にどの列ペアにも現れなくなった列の外部キー印を落とす
    /// </summary>
    /// <remarks>
    /// GUI の規則（<c>MainViewModel.ApplyRelationshipColumnRules</c>）と同じ意味論。GUI は列ペアの子列を
    /// 「リレーションが管理する列」として印を立て、リレーションから外れたら印を落とす。どの列ペアにも現れない列に
    /// 手で付けた印（DB 取込・GUI の手動チェック由来）は残す。図を開いた時点で列ペアにいる子列だけが管理対象になるので、
    /// 毎回読み込み直すこのホストでは「変更の前に列ペアにいた子列」が管理対象にあたる。
    /// 関係の削除・端点テーブルの削除・列の削除（列ペアのクリア）のどれで外れても、ここで 1 回だけ当て直す。
    /// </remarks>
    private static void ReleaseForeignKeyMarks(ErDiagram schema, HashSet<Guid> managedBefore)
    {
        var managedAfter = CollectRelationshipChildColumnIds(schema);

        foreach (var column in schema.Entities.SelectMany(entity => entity.Columns))
        {
            if (managedBefore.Contains(column.Id) && !managedAfter.Contains(column.Id))
            {
                column.IsForeignKey = false;
            }
        }
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

        return (null, DiagramLoadErrors.Describe(file, error, exception));
    }

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

        // 型名・制約の書き方は対象 DBMS で変わるので、最初に読む要約の先頭で示す
        sb.AppendLine($"Target DBMS: {schema.TargetDbms}");
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

            AppendPrimaryKeyOrder(sb, entity.GetReorderedPrimaryKeyColumnNames());
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
    /// <remarks>
    /// 空・空白だけの名前と、既存テーブルと大文字小文字を無視して同じ名前は拒否する（どちらも、テーブル名で
    /// 照合する他のツールから触れないテーブルになるため）。GUI 側ツールと同じ規則
    /// </remarks>
    private static (string, bool) AddEntity(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name") ?? "NewTable";
        var desc = GetString(args, "description") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return ("table_name is required.", false);
        }

        if (FindEntity(document.Schema, tableName) is { } existing)
        {
            return (DescribeTableNameTaken(existing.TableName), false);
        }

        document.Schema.Entities.Add(new Entity { TableName = tableName, Description = desc });

        return ($"Added table '{tableName}'.", true);
    }

    /// <summary>指定テーブル名のエンティティと、接続する全リレーション・そのテーブルの名前付きクエリ・孤児レイアウトを削除する</summary>
    /// <remarks>
    /// クエリはテーブル単位で定義されるので、テーブルを消したあとに残すと <c>remove_query</c>（テーブル名で照合）でも
    /// 消せない孤児になる。GUI の削除コマンド（<c>RemoveEntityCommand</c>）と同じ規則。このホストは Undo を持たないので
    /// 消したクエリは戻らない（ツールの説明に明記してある）
    /// </remarks>
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

        // そのテーブルの名前付きクエリも取り除く（残すとどのツールからも触れない孤児になる）
        var removedQueryNames = schema
            .Queries.Where(query => query.EntityId == entity.Id)
            .Select(query => query.Name)
            .ToList();
        schema.Queries.RemoveAll(query => query.EntityId == entity.Id);

        // 孤児になったレイアウトエントリを削除する（ファイルを清潔に保つ）
        document.Layout?.Remove(entity.Id);

        return removedQueryNames.Count == 0
            ? ($"Removed table '{tableName}'.", true)
            : (
                $"Removed table '{tableName}' along with its named queries: {string.Join(", ", removedQueryNames)}.",
                true
            );
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

        if (string.IsNullOrWhiteSpace(dataType))
        {
            return (DataTypeEmptyMessage, false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        // 同じテーブルに大文字小文字だけ違う同名の列を作らない（列名で照合する他のツールから 2 本目に触れなくなる）
        if (FindColumn(entity, columnName) is { } existingColumn)
        {
            return (
                $"Table '{entity.TableName}' already has a column named '{existingColumn.Name}' (column names must be unique within a table, ignoring case).",
                false
            );
        }

        // is_primary_key は明示 true のときのみ真、is_nullable は明示 false のときのみ偽（GUI と同一）
        var isPk =
            args.TryGetProperty("is_primary_key", out var isPkEl)
            && isPkEl.ValueKind == JsonValueKind.True;
        var isNullable =
            !args.TryGetProperty("is_nullable", out var isNullEl)
            || isNullEl.ValueKind != JsonValueKind.False;

        // 主キー列は NULL 不可（GUI 側の ColumnViewModel も同じ強制を行う）
        isNullable = isPk ? false : isNullable;

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

        var column = FindColumn(entity, columnName);

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
            var newTableName = newNameEl.GetString()!;

            if (string.IsNullOrWhiteSpace(newTableName))
            {
                return ("new_table_name must not be empty.", false);
            }

            // 自分自身の大文字小文字だけを変える改名は許す
            var other = document.Schema.Entities.FirstOrDefault(candidate =>
                !ReferenceEquals(candidate, entity)
                && SchemaNames.AreSame(candidate.TableName, newTableName)
            );

            if (other is not null)
            {
                return (DescribeTableNameTaken(other.TableName), false);
            }

            entity.TableName = newTableName;
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

        var column = FindColumn(entity, columnName);

        if (column is null)
        {
            return ($"Column '{columnName}' not found.", false);
        }

        // 検証はどの変更よりも先に行う（GUI 側ツールは変更をその場で反映するので、途中で失敗すると一部だけ反映される）
        if (
            GetString(args, "data_type") is { } requestedDataType
            && string.IsNullOrWhiteSpace(requestedDataType)
        )
        {
            return (DataTypeEmptyMessage, false);
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

        // 主キー列は NULL 不可（GUI 側の ColumnViewModel も同じ強制を行う）。NULL 許容への変更は無視し、応答でそう告げる
        var nullableIgnored = false;

        if (
            args.TryGetProperty("is_nullable", out var isNullEl)
            && isNullEl.ValueKind is JsonValueKind.True or JsonValueKind.False
        )
        {
            if (column.IsPrimaryKey && isNullEl.GetBoolean())
            {
                nullableIgnored = true;
            }
            else
            {
                column.IsNullable = isNullEl.GetBoolean();
                changed.Add("nullability");
            }
        }

        var ignoredNote =
            $"Column '{column.Name}' is part of the primary key, so it stays NOT NULL; is_nullable=true was ignored (to allow NULL, first take the column out of the key with set_primary_key).";

        if (changed.Count == 0)
        {
            return nullableIgnored
                ? (ignoredNote, true)
                : (
                    "No properties specified to change. Specify at least one of description, data_type, or is_nullable.",
                    false
                );
        }

        var updated =
            $"Updated {string.Join(", ", changed)} of column '{columnName}' in table '{tableName}'.";

        return (nullableIgnored ? $"{updated} {ignoredNote}" : updated, true);
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
        var typeStr = GetString(args, "relationship_type");

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

        if (ParseRelationshipType(typeStr) is not { } relType)
        {
            return (
                $"Invalid relationship_type '{typeStr}'. Must be one of: {string.Join(", ", Enum.GetNames<RelationshipType>())}.",
                false
            );
        }

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

        // 同じ親子・同じ列ペアの外部キーを 2 本作らない（DDL に同じ外部キーが 2 本出る）。
        // 列ペアの無い関係は外部キーとして DDL に出ないので対象にしない
        var duplicate =
            columnPairs!.Count == 0
                ? null
                : schema.Relationships.FirstOrDefault(existing =>
                    existing.SourceEntityId == source.Id
                    && existing.TargetEntityId == target.Id
                    && HasSameColumnPairs(existing.ColumnPairs, columnPairs)
                );

        if (duplicate is not null)
        {
            return (
                $"A relationship '{source.TableName}' → '{target.TableName}' over the same column pairs already exists (constraint name: {DescribeConstraintNames([duplicate])}). The same foreign key can be defined only once.",
                false
            );
        }

        var childColumnNames = columnPairs!
            .Select(pair => target.Columns.First(c => c.Id == pair.TargetColumnId).Name)
            .ToList();
        var (constraintName, nameError) = ResolveNewForeignKeyName(
            args,
            target.TableName,
            source.TableName,
            childColumnNames,
            ConstraintNames.CollectExisting(schema)
        );

        if (nameError is not null)
        {
            return (nameError, false);
        }

        schema.Relationships.Add(
            new Relationship
            {
                SourceEntityId = source.Id,
                TargetEntityId = target.Id,
                Type = relType,
                ColumnPairs = columnPairs!,
                ConstraintName = constraintName,
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

        var added =
            $"Added relationship '{sourceTable}' → '{targetTable}' (constraint name: {constraintName}).";
        var note =
            DescribeIncompleteInferredPairs(args, relType, source, target, columnPairs.Count)
            ?? DescribeNonCandidateKeyColumns(relType, source, columnPairs);

        return (note is null ? added : $"{added} {note}", true);
    }

    /// <summary>明示した親側の列が親の主キーとも一意制約とも一致しないときの注意文</summary>
    /// <param name="relationshipType">リレーションの種別（多対多は列ペアを持たないので対象外）</param>
    /// <param name="source">親テーブル</param>
    /// <param name="columnPairs">作成したリレーションの列ペア</param>
    /// <returns>注意文。候補キーと一致する・列ペアが無い・多対多のときは null</returns>
    /// <remarks>
    /// 拒否はしない（MySQL は一意でない索引列を参照する外部キーを受け付け、DB 取込の図にもその形が入り得る）。
    /// 列を省略した推論は主キー全列から作るため、ここに来るのは列を明示したときだけ。
    /// GUI 側 <c>ErDiagramDynamicTools.DescribeNonCandidateKeyColumns</c> と同じ判定（<see cref="Entity.IsCandidateKey"/>）
    /// </remarks>
    private static string? DescribeNonCandidateKeyColumns(
        RelationshipType relationshipType,
        Entity source,
        IReadOnlyList<RelationshipColumnPair> columnPairs
    )
    {
        if (relationshipType == RelationshipType.ManyToMany || columnPairs.Count == 0)
        {
            return null;
        }

        var parentColumnIds = columnPairs.Select(pair => pair.SourceColumnId).ToList();

        if (source.IsCandidateKey(parentColumnIds))
        {
            return null;
        }

        var names = parentColumnIds.Select(id => source.Columns.First(c => c.Id == id).Name);

        return $"Note: the columns ({string.Join(", ", names)}) of '{source.TableName}' are neither its primary key nor one of its unique constraints, so most databases reject this FOREIGN KEY when the generated DDL is applied (MySQL accepts it if the columns are indexed). To fix it, remove the relationship and add it again with source_columns naming the primary key or a unique constraint's columns, or add a unique constraint over these columns.";
    }

    /// <summary>
    /// 列を省略して親の主キーから列ペアを推論したのに、主キー列の一部（または全部）に子列を引き当てられなかったときの注意文
    /// </summary>
    /// <param name="args">ツール引数（列を明示したかの判定に使う）</param>
    /// <param name="relationshipType">リレーションの種別（多対多は列ペアを持たないので対象外）</param>
    /// <param name="source">親テーブル</param>
    /// <param name="target">子テーブル</param>
    /// <param name="pairCount">推論できた列ペアの数</param>
    /// <returns>注意文。推論が親の主キー全列に届いた・列を明示した・多対多のときは null</returns>
    /// <remarks>
    /// 列ペアが無い外部キーは DDL に出ず、主キーの一部だけの外部キーは DB が受け付けない。どちらも成功のまま返すので、
    /// 気付けるよう応答に添える。GUI 側 <c>ErDiagramDynamicTools</c> と同じ判定
    /// </remarks>
    private static string? DescribeIncompleteInferredPairs(
        JsonElement args,
        RelationshipType relationshipType,
        Entity source,
        Entity target,
        int pairCount
    )
    {
        var columnsGiven =
            GetColumnNames(args, "source_columns").Names is not null
            || GetColumnNames(args, "target_columns").Names is not null;
        var keyCount = source.Columns.Count(column => column.IsPrimaryKey);

        if (columnsGiven || relationshipType == RelationshipType.ManyToMany)
        {
            return null;
        }

        if (keyCount == 0)
        {
            return $"Note: table '{source.TableName}' has no primary key, so no column pairs were inferred and the generated DDL has no FOREIGN KEY for this relationship. To fix it, remove the relationship and add it again with source_columns and target_columns.";
        }

        return pairCount < keyCount
            ? $"Note: only {pairCount} of the {keyCount} primary key columns of '{source.TableName}' could be paired with a column of '{target.TableName}', so the generated DDL has no FOREIGN KEY or an incomplete one for this relationship. To fix it, remove the relationship and add it again with source_columns and target_columns."
            : null;
    }

    /// <summary><c>relationship_type</c> を列挙値へ解釈する（列挙子の名前と大文字小文字を無視して一致。未指定は一対多）</summary>
    /// <returns>解釈した種別。どの列挙子とも一致しなければ null</returns>
    /// <remarks>GUI 側 <c>ErDiagramDynamicTools.ParseRelationshipType</c> と同じ規則</remarks>
    private static RelationshipType? ParseRelationshipType(string? value) =>
        value is null
            ? RelationshipType.OneToMany
            : Enum.GetValues<RelationshipType>()
                .Cast<RelationshipType?>()
                .FirstOrDefault(type =>
                    string.Equals(type.ToString(), value, StringComparison.OrdinalIgnoreCase)
                );

    /// <summary>2 つの列ペア一覧が、並びを問わず同じ (親列, 子列) の組を持つかを判定する</summary>
    private static bool HasSameColumnPairs(
        IReadOnlyCollection<RelationshipColumnPair> left,
        IReadOnlyCollection<RelationshipColumnPair> right
    ) =>
        left.Select(pair => (pair.SourceColumnId, pair.TargetColumnId))
            .ToHashSet()
            .SetEquals(right.Select(pair => (pair.SourceColumnId, pair.TargetColumnId)));

    /// <summary>テーブル名の重複を拒否するときの文言</summary>
    /// <param name="existingTableName">既に使われているテーブル名（図に書かれているとおり）</param>
    private static string DescribeTableNameTaken(string existingTableName) =>
        $"A table named '{existingTableName}' already exists (table names must be unique, ignoring case).";

    /// <summary>空・空白だけのデータ型を拒否するときの文言</summary>
    private const string DataTypeEmptyMessage = "data_type must not be empty.";

    /// <summary>新しいリレーションの制約名を決める（<c>constraint_name</c> の指定、無ければ衝突しない既定名）</summary>
    /// <param name="args">ツール引数</param>
    /// <param name="childTable">参照する側（子）のテーブル名</param>
    /// <param name="parentTable">参照される側（親）のテーブル名</param>
    /// <param name="childColumnNames">外部キーを構成する子の列名（宣言順）</param>
    /// <param name="existingNames">図の中の既存の制約名</param>
    /// <returns>決めた名前。指定された名前が既存と衝突するときはエラー文</returns>
    /// <remarks>
    /// GUI 側 <c>ErDiagramDynamicTools.ResolveNewForeignKeyName</c> のミラー。空・空白だけの指定は省略として扱い、
    /// 前後の空白は除く。指定された名前が既存と衝突するときは、黙って付け替えず拒否する
    /// （付け替えるとその名前での remove_relationship が外れ、受け入れると DDL が失敗する）
    /// </remarks>
    private static (string Name, string? Error) ResolveNewForeignKeyName(
        JsonElement args,
        string childTable,
        string parentTable,
        IReadOnlyList<string> childColumnNames,
        IReadOnlySet<string> existingNames
    )
    {
        var requested = GetString(args, "constraint_name")?.Trim();

        if (string.IsNullOrEmpty(requested))
        {
            return (
                ConstraintNames.UniqueForeignKey(
                    childTable,
                    parentTable,
                    childColumnNames,
                    existingNames
                ),
                null
            );
        }

        return existingNames.Contains(requested)
            ? (
                requested,
                $"The constraint name '{requested}' is already used in this diagram (constraint names must be unique across foreign keys, primary keys and unique constraints, ignoring case). Choose another name, or omit constraint_name to get a unique one automatically."
            )
            : (requested, null);
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
                    && SchemaNames.AreSame(source.TableName, sourceTable)
                    && SchemaNames.AreSame(target.TableName, targetTable);
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
                .Where(r => SchemaNames.AreSame(r.ConstraintName, constraintName))
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

    // ---------------- primary key operations ----------------

    /// <summary>テーブルの主キーを指定列（指定順）へ置き換える</summary>
    /// <remarks>
    /// 照合キーはテーブルのみ（主キーはテーブルに高々 1 つのため常に全体置換）。膜（どの列が主キーか）は
    /// <see cref="Column.IsPrimaryKey"/>、順序は <see cref="Entity.PrimaryKeyColumnIds"/> へ書く。
    /// 列挙された列は NULL 不可へ正規化し、主キーから外れた列の NULL 許容は据え置く
    /// （外したことが NULL 許容の変更まで意味しないため）。
    /// </remarks>
    private static (string, bool) SetPrimaryKey(DiagramDocument document, JsonElement args)
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

        var keyColumnIds = new HashSet<Guid>(columns!.Select(column => column.Id));

        foreach (var column in entity.Columns)
        {
            column.IsPrimaryKey = keyColumnIds.Contains(column.Id);

            // 主キー列は NULL 不可（DB 側の制約）。外れた列の NULL 許容は触らない
            if (column.IsPrimaryKey)
            {
                column.IsNullable = false;
            }
        }

        entity.PrimaryKeyColumnIds = columns!.Select(column => column.Id).ToList();

        var columnText = string.Join(", ", columns!.Select(column => column.Name));

        return ($"Set primary key on table '{entity.TableName}' (columns: {columnText}).", true);
    }

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
        var normalizedName = string.IsNullOrWhiteSpace(name)
            ? null
            : SchemaNames.ToStoredForm(name);
        var existing = FindUniqueConstraintByColumnSet(entity, columnIds);
        var columnText = string.Join(", ", columns!.Select(column => column.Name));

        // 外部キーの名前（add_relationship）と同じ規則で、図の中の既存の制約名との衝突を断る
        if (
            normalizedName is not null
            && ConstraintNames.IsTaken(document.Schema, normalizedName, existing?.Id)
        )
        {
            return (
                $"The constraint name '{normalizedName}' is already used in this diagram (constraint names must be unique across foreign keys, primary keys and unique constraints, ignoring case). Choose another name, or omit name to get a unique one automatically.",
                false
            );
        }

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
    /// <remarks>
    /// UNIQUE 制約ツールと主キーツールで共用する（空配列・列不在・列の重複という検証内容は制約の種別に
    /// 依存しないため、エラー文言も種別を名乗らない）。
    /// </remarks>
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
            var column = FindColumn(entity, columnName);

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

    /// <summary>要約テキストへ主キーの実効順を追記する（列宣言順と食い違うときだけ）</summary>
    /// <param name="orderedNames">
    /// <see cref="Entity.GetReorderedPrimaryKeyColumnNames"/> の結果（<c>null</c>＝食い違いなしで追記しない）
    /// </param>
    private static void AppendPrimaryKeyOrder(StringBuilder sb, List<string>? orderedNames)
    {
        if (orderedNames is null)
        {
            return;
        }

        sb.AppendLine($"  Primary key order: {string.Join(", ", orderedNames)}");
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
}
