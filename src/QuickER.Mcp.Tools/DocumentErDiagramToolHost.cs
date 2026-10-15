using System.IO;
using System.Text;
using System.Text.Json;
using QuickER.Dialects;
using QuickER.Documents;
using QuickER.Model;
using static QuickER.Mcp.ToolArguments;
using static QuickER.Mcp.Tools.SchemaLookup;

namespace QuickER.Mcp.Tools;

/// <summary>
/// ファイルベースの ER 図ツール実行ホスト。各ツール呼び出しを
/// 「<see cref="DiagramFileReader.Read"/>（形式を検証して DiagramDocument を読込）→ 意味モデル（<see cref="ErDiagram"/>）変更 →
/// <see cref="JsonStorageService.SaveAtomic"/>（変わったときだけ保存）」で完結させるステートレスな実行器。
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
            ErDiagramToolNames.GetDiagramSummary => GetDiagramSummary(file),
            ListQueriesToolName => ListQueries(file),
            SetQueryToolName => Mutate(file, doc => SetQuery(doc, arguments)),
            RemoveQueryToolName => Mutate(file, doc => RemoveQuery(doc, arguments)),
            ErDiagramToolNames.AddEntity => Mutate(file, doc => AddEntity(doc, arguments)),
            ErDiagramToolNames.RemoveEntity => Mutate(file, doc => RemoveEntity(doc, arguments)),
            ErDiagramToolNames.AddColumn => Mutate(file, doc => AddColumn(doc, arguments)),
            ErDiagramToolNames.RemoveColumn => Mutate(file, doc => RemoveColumn(doc, arguments)),
            ErDiagramToolNames.SetEntityProperty => Mutate(
                file,
                doc => SetEntityProperty(doc, arguments)
            ),
            ErDiagramToolNames.SetColumnProperty => Mutate(
                file,
                doc => SetColumnProperty(doc, arguments)
            ),
            ErDiagramToolNames.AddRelationship => Mutate(
                file,
                doc => AddRelationship(doc, arguments)
            ),
            ErDiagramToolNames.RemoveRelationship => Mutate(
                file,
                doc => RemoveRelationship(doc, arguments)
            ),
            ErDiagramToolNames.SetPrimaryKey => Mutate(file, doc => SetPrimaryKey(doc, arguments)),
            ErDiagramToolNames.SetUniqueConstraint => Mutate(
                file,
                doc => SetUniqueConstraint(doc, arguments)
            ),
            ErDiagramToolNames.RemoveUniqueConstraint => Mutate(
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
    /// <remarks>
    /// 成功しても図が変わっていなければ書かない（主キー列への <c>is_nullable=true</c> のように無視しただけの呼び出し・
    /// 同じ値の設定・同じ定義の upsert）。比べるのは変更の前後を保存する形で直列化したもの
    /// （<see cref="JsonStorageService.ToJson"/>）で、ツールごとに「変えたか」を返させる形より取りこぼしが無い。
    /// 書くとファイルの更新日時が動き、GUI がその図を開いていれば再読込や外部変更の確認が起きる。
    /// </remarks>
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

        var loaded = document!;
        var before = JsonStorageService.ToJson(loaded);
        var managedBefore = CollectRelationshipChildColumnIds(loaded.Schema);
        var namesRefusedBefore = NameLimitNotes.Find(
            loaded.Schema,
            IdentifierLengthLimits.ForDiagramTarget(loaded.Schema.TargetDbms)
        );
        var (result, success) = operation(loaded);

        if (success)
        {
            ReleaseForeignKeyMarks(loaded.Schema, managedBefore);

            // 名前は受け付けて保存する（DDL・同期の入口が断る）。この呼び出しで新しく受け付けられなくなった名前を知らせる
            if (
                NameLimitNotes.Describe(
                    namesRefusedBefore,
                    NameLimitNotes.Find(
                        loaded.Schema,
                        IdentifierLengthLimits.ForDiagramTarget(loaded.Schema.TargetDbms)
                    ),
                    loaded.Schema.TargetDbms
                ) is
                { } nameNote
            )
            {
                result = $"{result} {nameNote}";
            }

            // ユーザーの実ファイルへ書き戻すため原子的に差し替える（書き込み途中の中断・
            // ディスク満杯で既存の図が半端な JSON になるのを防ぐ）。何も変わっていなければ書かない
            if (JsonStorageService.ToJson(loaded) != before)
            {
                JsonStorageService.SaveAtomic(file, loaded);
            }
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
        var (document, error) = DiagramFileReader.Read(file);

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

    // ---------------- create_diagram ----------------

    /// <summary>新規のスキーマのみ文書（レイアウトなし）を作成する。既存ファイルは上書きしない</summary>
    private static (string, bool) CreateDiagram(string file, JsonElement args)
    {
        var dbmsInput = GetString(args, "target_dbms");

        if (string.IsNullOrWhiteSpace(dbmsInput))
        {
            return (
                $"target_dbms is required (one of: {string.Join(", ", DialectNames.All)}).",
                false
            );
        }

        var dbms = DialectNames.Find(dbmsInput);

        if (dbms is null)
        {
            return (
                $"Invalid target_dbms '{dbmsInput}'. Must be one of: {string.Join(", ", DialectNames.All)}.",
                false
            );
        }

        if (File.Exists(file))
        {
            return (DescribeAlreadyExists(file), false);
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

        try
        {
            // 置き換えない書き込みで作る（上の確認から書くまでの間に別のプロセスが作った図を上書きしない）
            JsonStorageService.SaveNew(file, document);
        }
        catch (IOException) when (File.Exists(file))
        {
            return (DescribeAlreadyExists(file), false);
        }

        return ($"Created diagram '{file}' (target DBMS: {dbms}).", true);
    }

    /// <summary>新しい版の図のときだけ、読み取り系の応答の先頭へ警告と空行を付ける（文言は <see cref="DiagramFileReader.NewerFormatWarning"/>）</summary>
    internal static void AppendNewerFormatWarning(StringBuilder sb, DiagramDocument document)
    {
        if (DiagramFileReader.NewerFormatWarning(document) is { } warning)
        {
            sb.Append(warning).Append('\n').Append('\n');
        }
    }

    /// <summary>create_diagram が既存のファイルを断るときの文言</summary>
    private static string DescribeAlreadyExists(string file) =>
        $"Diagram file already exists: {file}. {CreateDiagramToolName} only creates new files; use the other tools to modify an existing diagram.";

    // ---------------- get_diagram_summary ----------------

    /// <summary>ER 図の概要（テーブル・カラム・リレーション）を英語テキスト化する</summary>
    private static (string, bool) GetDiagramSummary(string file)
    {
        var (document, error) = DiagramFileReader.Read(file);

        if (error is not null)
        {
            return (error, false);
        }

        var sb = new StringBuilder();

        // 読み取り系は新フォーマットでも警告付きで続行する（CLI と同方針）
        AppendNewerFormatWarning(sb, document!);

        var schema = document!.Schema;

        // 型名・制約の書き方は対象 DBMS で変わるので、最初に読む要約の先頭で示す
        sb.Append($"Target DBMS: {schema.TargetDbms}").Append('\n');
        sb.Append($"Tables: {schema.Entities.Count}").Append('\n');
        sb.Append($"Relationships: {schema.Relationships.Count}").Append('\n');
        sb.Append('\n');

        foreach (var entity in schema.Entities)
        {
            sb.Append($"[{entity.TableName}]").Append('\n');

            if (!string.IsNullOrWhiteSpace(entity.Description))
            {
                sb.Append($"  Description: {entity.Description}").Append('\n');
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
                sb.Append($"  - {col.Name}: {col.DataType}{flagsText}{colDesc}").Append('\n');
            }

            AppendPrimaryKeyOrder(sb, entity.GetReorderedPrimaryKeyColumnNames());
            AppendUniqueConstraints(
                sb,
                entity,
                IdentifierLengthLimits.ForDiagramTarget(schema.TargetDbms)
            );
        }

        if (schema.Relationships.Count > 0)
        {
            var ddlNames = ConstraintNames.ResolveForeignKeyNamesById(
                schema,
                IdentifierLengthLimits.ForDiagramTarget(schema.TargetDbms)
            );
            sb.Append('\n');
            sb.Append("Relationships:").Append('\n');

            foreach (var rel in schema.Relationships)
            {
                var source = FindEntityById(schema, rel.SourceEntityId);
                var target = FindEntityById(schema, rel.TargetEntityId);
                var sourceName = source?.TableName ?? "(unknown)";
                var targetName = target?.TableName ?? "(unknown)";
                sb.Append(
                        $"  {sourceName} → {targetName} ({rel.Type}{DescribeColumnPairs(rel, source, target)}){DescribeConstraintName(rel, ddlNames)}"
                    )
                    .Append('\n');
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
        var tableName = GetString(args, "table_name");
        var desc = GetString(args, "description") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return (TableNameRequiredMessage, false);
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
            return (TableNameRequiredMessage, false);
        }

        var schema = document.Schema;
        var entity = FindEntity(schema, tableName);

        if (entity is null)
        {
            return (DescribeTableNotFound(tableName), false);
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
        var dataType = GetString(args, "data_type");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return (TableAndColumnNameRequiredMessage, false);
        }

        if (string.IsNullOrWhiteSpace(dataType))
        {
            return (DataTypeEmptyMessage, false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return (DescribeTableNotFound(tableName), false);
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
            return (TableAndColumnNameRequiredMessage, false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return (DescribeTableNotFound(tableName), false);
        }

        var column = FindColumn(entity, columnName);

        if (column is null)
        {
            return (DescribeColumnNotFound(columnName), false);
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
            return (TableNameRequiredMessage, false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return (DescribeTableNotFound(tableName), false);
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
            return (TableAndColumnNameRequiredMessage, false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return (DescribeTableNotFound(tableName), false);
        }

        var column = FindColumn(entity, columnName);

        if (column is null)
        {
            return (DescribeColumnNotFound(columnName), false);
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
            return (SourceAndTargetTableRequiredMessage, false);
        }

        var schema = document.Schema;
        var source = FindEntity(schema, sourceTable);
        var target = FindEntity(schema, targetTable);

        if (source is null)
        {
            return (DescribeTableNotFound(sourceTable), false);
        }

        if (target is null)
        {
            return (DescribeTableNotFound(targetTable), false);
        }

        if (typeStr is null)
        {
            return (
                $"relationship_type is required (one of: {string.Join(", ", Enum.GetNames<RelationshipType>())}).",
                false
            );
        }

        if (!RelationshipTypeNames.TryParse(typeStr, out var relType))
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
                    && RelationshipColumnPair.HaveSamePairs(existing.ColumnPairs, columnPairs)
                );

        if (duplicate is not null)
        {
            return (
                $"A relationship '{source.TableName}' → '{target.TableName}' over the same column pairs already exists (constraint name: {DescribeConstraintNames([duplicate], ConstraintNames.ResolveForeignKeyNamesById(schema, IdentifierLengthLimits.ForDiagramTarget(schema.TargetDbms)))}). The same foreign key can be defined only once.",
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
            ConstraintNames.CollectExisting(
                schema,
                IdentifierLengthLimits.ForDiagramTarget(schema.TargetDbms)
            ),
            IdentifierLengthLimits.ForDiagramTarget(schema.TargetDbms)
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

    /// <summary>テーブル名の重複を拒否するときの文言</summary>
    /// <param name="existingTableName">既に使われているテーブル名（図に書かれているとおり）</param>
    private static string DescribeTableNameTaken(string existingTableName) =>
        $"A table named '{existingTableName}' already exists (table names must be unique, ignoring case).";

    /// <summary>空・空白だけのデータ型を拒否するときの文言</summary>
    private const string DataTypeEmptyMessage = "data_type must not be empty.";

    /// <summary>テーブル名が無いときの文言</summary>
    private const string TableNameRequiredMessage = "table_name is required.";

    /// <summary>テーブル名・列名が無いときの文言</summary>
    private const string TableAndColumnNameRequiredMessage =
        "table_name and column_name are required.";

    /// <summary>リレーションの両端のテーブル名が無いときの文言</summary>
    private const string SourceAndTargetTableRequiredMessage =
        "source_table and target_table are required.";

    /// <summary>テーブルが見つからないときの文言</summary>
    private static string DescribeTableNotFound(string tableName) =>
        $"Table '{tableName}' not found.";

    /// <summary>列が見つからないときの文言</summary>
    private static string DescribeColumnNotFound(string columnName) =>
        $"Column '{columnName}' not found.";

    /// <summary>新しいリレーションの制約名を決める（<c>constraint_name</c> の指定、無ければ衝突しない既定名）</summary>
    /// <param name="args">ツール引数</param>
    /// <param name="childTable">参照する側（子）のテーブル名</param>
    /// <param name="parentTable">参照される側（親）のテーブル名</param>
    /// <param name="childColumnNames">外部キーを構成する子の列名（宣言順）</param>
    /// <param name="existingNames">図の中の既存の制約名</param>
    /// <returns>決めた名前。指定された名前が既存と衝突するときはエラー文</returns>
    /// <remarks>規則は GUI のツールと共有する <see cref="ConstraintNames.ResolveRequestedForeignKey"/>。ここは文言だけを持つ</remarks>
    private static (string Name, string? Error) ResolveNewForeignKeyName(
        JsonElement args,
        string childTable,
        string parentTable,
        IReadOnlyList<string> childColumnNames,
        IReadOnlySet<string> existingNames,
        IdentifierLengthLimit limit
    )
    {
        var (name, taken) = ConstraintNames.ResolveRequestedForeignKey(
            GetString(args, "constraint_name"),
            childTable,
            parentTable,
            childColumnNames,
            existingNames,
            limit
        );

        return taken
            ? (
                name,
                $"The constraint name '{name}' is already used in this diagram (constraint names must be unique across foreign keys, primary keys and unique constraints, ignoring case). Choose another name, or omit constraint_name to get a unique one automatically."
            )
            : (name, null);
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
    /// 無指定で複数一致したときは黙って先頭を消さず、候補の制約名を挙げてエラーにする。
    /// 名前を持たないリレーションは、DDL が付ける名前（<see cref="ConstraintNames.ResolveForeignKeyNamesById"/>）で選べる
    /// （要約と候補の一覧もその名前を示す）。GUI のツールと同じ規則
    /// </remarks>
    private static (string, bool) RemoveRelationship(DiagramDocument document, JsonElement args)
    {
        var sourceTable = GetString(args, "source_table");
        var targetTable = GetString(args, "target_table");

        if (string.IsNullOrWhiteSpace(sourceTable) || string.IsNullOrWhiteSpace(targetTable))
        {
            return (SourceAndTargetTableRequiredMessage, false);
        }

        var schema = document.Schema;
        var ddlNames = ConstraintNames.ResolveForeignKeyNamesById(
            schema,
            IdentifierLengthLimits.ForDiagramTarget(schema.TargetDbms)
        );

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
                .Where(r => SchemaNames.AreSame(ddlNames.GetValueOrDefault(r.Id), constraintName))
                .ToList();

            if (byName.Count == 0)
            {
                return (
                    $"Relationship '{sourceTable}' → '{targetTable}' with constraint name '{constraintName}' not found. Candidates: {DescribeConstraintNames(matches, ddlNames)}.",
                    false
                );
            }

            matches = byName;
        }

        if (matches.Count > 1)
        {
            return (
                $"Several relationships connect '{sourceTable}' → '{targetTable}'; specify constraint_name to choose one. Candidates: {DescribeConstraintNames(matches, ddlNames)}.",
                false
            );
        }

        schema.Relationships.Remove(matches[0]);

        return ($"Removed relationship '{sourceTable}' → '{targetTable}'.", true);
    }

    /// <summary>候補リレーションの制約名を列挙する（書き方は <see cref="DescribeName"/>）</summary>
    /// <param name="relationships">候補</param>
    /// <param name="ddlNames">リレーションの Id → DDL が付ける名前（<see cref="ConstraintNames.ResolveForeignKeyNamesById"/>）</param>
    private static string DescribeConstraintNames(
        IEnumerable<Relationship> relationships,
        IReadOnlyDictionary<Guid, string> ddlNames
    ) => string.Join(", ", relationships.Select(r => DescribeName(r, ddlNames)));

    /// <summary>
    /// リレーションの名前を示す（保存された名前はそのまま・名前が無ければ DDL が付ける名前に <c>(default name)</c> を添える・
    /// 外部キーを作らない名前の無い関係は <c>(unnamed)</c>）
    /// </summary>
    private static string DescribeName(
        Relationship relationship,
        IReadOnlyDictionary<Guid, string> ddlNames
    ) =>
        !string.IsNullOrWhiteSpace(relationship.ConstraintName) ? relationship.ConstraintName!
        : ddlNames.TryGetValue(relationship.Id, out var ddlName) ? $"{ddlName} (default name)"
        : "(unnamed)";

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
            return (TableNameRequiredMessage, false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return (DescribeTableNotFound(tableName), false);
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
            return (TableNameRequiredMessage, false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return (DescribeTableNotFound(tableName), false);
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
            && ConstraintNames.IsTaken(
                document.Schema,
                normalizedName,
                existing?.Id,
                IdentifierLengthLimits.ForDiagramTarget(document.Schema.TargetDbms)
            )
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
            return (TableNameRequiredMessage, false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return (DescribeTableNotFound(tableName), false);
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

        sb.Append($"  Primary key order: {string.Join(", ", orderedNames)}").Append('\n');
    }

    /// <summary>要約テキストへエンティティの一意制約（解決済み名＋構成列）を追記する</summary>
    /// <remarks>構成列を解決できない制約（空・壊れた参照）は DDL 生成と同じ規則で読み飛ばす</remarks>
    /// <param name="limit">合成名に使う識別子の長さの上限（図の対象 DBMS の上限＝DDL と同じ名前を見せる）</param>
    private static void AppendUniqueConstraints(
        StringBuilder sb,
        Entity entity,
        IdentifierLengthLimit limit
    )
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
                ? UniqueConstraint.SynthesizeName(entity.TableName, columnNames, limit)
                : constraint.Name!;
            lines.Add($"    - {name} ({string.Join(", ", columnNames)})");
        }

        if (lines.Count == 0)
        {
            return;
        }

        sb.Append("  Unique constraints:").Append('\n');

        foreach (var line in lines)
        {
            sb.Append(line).Append('\n');
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

    /// <summary>要約テキスト用に外部キー制約名を <c> [名前]</c> 形式で表す（名前も DDL が付ける名前も無ければ空文字）</summary>
    /// <remarks>
    /// 名前を持たない外部キーには DDL が付ける名前を示す（<see cref="DescribeName"/>）。<c>remove_relationship</c> へ渡す
    /// 名前を知る手段は要約しかないため
    /// </remarks>
    private static string DescribeConstraintName(
        Relationship relationship,
        IReadOnlyDictionary<Guid, string> ddlNames
    ) =>
        string.IsNullOrWhiteSpace(relationship.ConstraintName)
        && !ddlNames.ContainsKey(relationship.Id)
            ? string.Empty
            : $" [{DescribeName(relationship, ddlNames)}]";
}
