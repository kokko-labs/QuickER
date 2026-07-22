using System.IO;
using System.Text;
using System.Text.Json;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Documents;
using QuickER.Model;

namespace QuickER.Mcp.Tools;

/// <summary>
/// 名前付きクエリ定義ツール（<c>set_query</c> / <c>list_queries</c> / <c>remove_query</c>）の実行部分。
/// </summary>
/// <remarks>
/// <para>
/// クエリ定義（<see cref="QueryDefinition"/>）を図（<see cref="ErDiagram.Queries"/>）へ upsert・削除・一覧する。
/// エンティティ・列はテーブル名／列名で受け取り、実行時に Guid へ解決する（他の編集系ツールと同じ流儀＝
/// 大文字小文字を区別しない最初の一致）。<c>set_query</c> は保存前に構造検証・DSL 構文検証・生 SQL 静的検証を行い、
/// <b>検証エラー時はファイルを一切変更しない</b>（<see cref="Mutate"/> が成功時のみ保存する仕組みに乗る）。
/// </para>
/// <para>
/// 検証の意味論は生成側（<c>QuickER.CodeGen.CSharp</c> の <c>CSharpGenerationModelBuilder.Queries</c>）・
/// GUI のクエリ定義ダイアログ（<c>QuickER.CodeGen.UI</c>）と揃える：DSL は <see cref="QueryConditionParser"/>、
/// 生 SQL は <see cref="RawSqlAnalyzer"/> を用い、未宣言パラメータは保存拒否・未使用パラメータ／複文は警告のみ。
/// 型トークンの内容検証（方言依存）は行わず、生成時検証に委ねる。
/// </para>
/// </remarks>
public static partial class DocumentErDiagramToolHost
{
    /// <summary>クエリ定義を upsert するツールの名前</summary>
    public const string SetQueryToolName = "set_query";

    /// <summary>クエリ定義を一覧するツール（読み取り系）の名前</summary>
    public const string ListQueriesToolName = "list_queries";

    /// <summary>クエリ定義を 1 件削除するツールの名前</summary>
    public const string RemoveQueryToolName = "remove_query";

    // ---------------- set_query ----------------

    /// <summary>
    /// 名前付きクエリ定義を 1 件 upsert する。(<c>table_name</c>, <c>query_name</c>) で照合し、既存があれば
    /// 丸ごと置換（<see cref="QueryDefinition.Id"/> は温存）、なければ追加する。検証エラー時は保存しない。
    /// </summary>
    private static (string, bool) SetQuery(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");
        var queryName = GetString(args, "query_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(queryName))
        {
            return ("table_name and query_name are required.", false);
        }

        var schema = document.Schema;
        var entity = FindEntity(schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        // 戻り形（必須）・実装方式（既定 dsl）を先に確定する（以降の構造検証がこれらに依存するため）
        var returnsInput = GetString(args, "returns");

        if (!TryParseReturnShape(returnsInput, out var returns))
        {
            return (
                "Invalid or missing 'returns' (must be one of: list, single, count, scalar, projection).",
                false
            );
        }

        var implementationInput = GetString(args, "implementation");

        if (!TryParseImplementation(implementationInput, out var implementation))
        {
            return ("Invalid 'implementation' (must be one of: dsl, sql, manual).", false);
        }

        var description = GetString(args, "description") ?? string.Empty;
        var scalarType = GetString(args, "scalar_type");
        var condition = GetString(args, "condition");
        var resultTypeName = GetString(args, "result_type_name");
        var paging = GetBool(args, "paging") ?? false;

        var errors = new List<string>();
        var warnings = new List<string>();

        var parameters = BuildParameters(args, entity, tableName!, errors);
        var orderBy = BuildOrderBy(args, entity, tableName!, returns, returnsInput!, errors);
        var fields = BuildFields(args, entity, tableName!, errors);
        var sql = BuildSql(args, errors);

        // ---- 構造検証 ----
        if (returns == QueryReturnShape.Scalar && string.IsNullOrWhiteSpace(scalarType))
        {
            errors.Add("returns=scalar requires 'scalar_type'.");
        }

        if (returns == QueryReturnShape.Projection)
        {
            if (string.IsNullOrWhiteSpace(resultTypeName))
            {
                errors.Add("returns=projection requires 'result_type_name'.");
            }

            if (fields.Count == 0)
            {
                errors.Add("returns=projection requires at least one entry in 'fields'.");
            }
        }

        // ---- DSL 構文・参照検証（implementation=dsl かつ condition あり） ----
        if (implementation == QueryImplementationKind.Dsl && !string.IsNullOrWhiteSpace(condition))
        {
            var parsed = QueryConditionParser.ParseAndValidate(condition, entity, parameters);

            if (!parsed.Success)
            {
                foreach (var diagnostic in parsed.Diagnostics)
                {
                    errors.Add($"Condition: {diagnostic.Message}");
                }
            }
            else
            {
                // 宣言済みだが条件で未使用のパラメータは警告（生成メソッドの未使用引数になる）
                var referenced = parsed
                    .ParameterReferences.Select(reference =>
                        reference.ResolvedName ?? reference.Text
                    )
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var parameter in parameters)
                {
                    if (!referenced.Contains(parameter.Name))
                    {
                        warnings.Add(
                            $"Parameter '{parameter.Name}' is declared but not used in the condition."
                        );
                    }
                }
            }
        }

        // ---- 生 SQL 静的検証（implementation=sql）。未宣言＝拒否・未使用／複文＝警告のみ ----
        if (implementation == QueryImplementationKind.Sql && sql.Count > 0)
        {
            var declared = parameters.Select(parameter => parameter.Name).ToList();

            if (paging)
            {
                declared.Add("take");
                declared.Add("skip");
            }

            foreach (var (dialect, sqlText) in sql)
            {
                foreach (var finding in RawSqlAnalyzer.Analyze(sqlText, declared))
                {
                    var message = $"{dialect}: {RawSqlAnalyzer.Describe(finding)}";

                    if (finding.Kind == RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter)
                    {
                        errors.Add(message);
                    }
                    else
                    {
                        warnings.Add(message);
                    }
                }
            }
        }

        if (errors.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                $"Cannot set query '{queryName}': validation failed. The file was not modified."
            );

            foreach (var error in errors)
            {
                sb.AppendLine($"  - {error}");
            }

            return (sb.ToString().TrimEnd(), false);
        }

        // ---- upsert（既存は Id 温存で丸ごと置換・なければ追加） ----
        var existing = schema.Queries.FirstOrDefault(query =>
            query.EntityId == entity.Id
            && string.Equals(query.Name, queryName, StringComparison.OrdinalIgnoreCase)
        );

        var definition = new QueryDefinition
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            EntityId = entity.Id,
            Name = queryName!,
            Description = description,
            Returns = returns,
            ScalarType = string.IsNullOrWhiteSpace(scalarType) ? null : scalarType,
            Parameters = parameters,
            Condition =
                implementation == QueryImplementationKind.Dsl
                && !string.IsNullOrWhiteSpace(condition)
                    ? condition
                    : null,
            OrderBy = orderBy,
            HasPaging = paging,
            Implementation = implementation,
            Sql = sql,
            ResultTypeName = string.IsNullOrWhiteSpace(resultTypeName) ? null : resultTypeName,
            Fields = fields,
        };

        string verb;

        if (existing is not null)
        {
            schema.Queries[schema.Queries.IndexOf(existing)] = definition;
            verb = "Updated";
        }
        else
        {
            schema.Queries.Add(definition);
            verb = "Added";
        }

        var result = new StringBuilder();
        result.Append(
            $"{verb} query '{queryName}' on table '{tableName}' (returns {Lower(returns)}, {Lower(implementation)})."
        );

        foreach (var warning in warnings)
        {
            result.Append($"\n  Warning: {warning}");
        }

        return (result.ToString(), true);
    }

    /// <summary>パラメータ配列を読み、モデル化する（type / source_column は排他・source_column は所属エンティティの列に限る）</summary>
    private static List<QueryParameter> BuildParameters(
        JsonElement args,
        Entity entity,
        string tableName,
        List<string> errors
    )
    {
        var parameters = new List<QueryParameter>();

        if (
            !args.TryGetProperty("parameters", out var array)
            || array.ValueKind != JsonValueKind.Array
        )
        {
            return parameters;
        }

        foreach (var item in array.EnumerateArray())
        {
            var name = GetString(item, "name");

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("A parameter is missing its required 'name'.");
                continue;
            }

            var type = GetString(item, "type");
            var sourceColumn = GetString(item, "source_column");
            var isList = GetBool(item, "is_list") ?? false;
            var hasType = !string.IsNullOrWhiteSpace(type);
            var hasSource = !string.IsNullOrWhiteSpace(sourceColumn);

            // type と source_column はどちらか一方（両方指定・両方欠落はエラー）
            if (hasType == hasSource)
            {
                errors.Add(
                    $"Parameter '{name}' must specify exactly one of 'type' or 'source_column'."
                );
                continue;
            }

            Guid? sourceColumnId = null;

            if (hasSource)
            {
                var column = FindColumn(entity, sourceColumn!);

                if (column is null)
                {
                    errors.Add(
                        $"Parameter '{name}': source_column '{sourceColumn}' not found in table '{tableName}'."
                    );
                    continue;
                }

                sourceColumnId = column.Id;
            }

            parameters.Add(
                new QueryParameter
                {
                    Name = name!,
                    // 列参照は列由来で型が決まるため型トークンは保存しない（モデルの規約に合わせる）
                    Type = hasType ? type : null,
                    IsList = isList,
                    SourceColumnId = sourceColumnId,
                }
            );
        }

        return parameters;
    }

    /// <summary>並び順配列を読み、モデル化する（列名 → 列 ID 解決・戻り形は list / single / projection のみ許可）</summary>
    private static List<QueryOrdering> BuildOrderBy(
        JsonElement args,
        Entity entity,
        string tableName,
        QueryReturnShape returns,
        string returnsInput,
        List<string> errors
    )
    {
        var orderBy = new List<QueryOrdering>();

        if (
            !args.TryGetProperty("order_by", out var array)
            || array.ValueKind != JsonValueKind.Array
            || array.GetArrayLength() == 0
        )
        {
            return orderBy;
        }

        // order_by は一覧・単一（並び替えて先頭 1 件）・射影のときのみ有効
        if (
            returns
            is not (QueryReturnShape.List or QueryReturnShape.Single or QueryReturnShape.Projection)
        )
        {
            errors.Add(
                $"order_by is only valid when returns is 'list', 'single', or 'projection' (got '{returnsInput}')."
            );
        }

        foreach (var item in array.EnumerateArray())
        {
            var columnName = GetString(item, "column");

            if (string.IsNullOrWhiteSpace(columnName))
            {
                errors.Add("An order_by entry is missing its required 'column'.");
                continue;
            }

            var column = FindColumn(entity, columnName!);

            if (column is null)
            {
                errors.Add($"order_by column '{columnName}' not found in table '{tableName}'.");
                continue;
            }

            orderBy.Add(
                new QueryOrdering
                {
                    ColumnId = column.Id,
                    Descending = GetBool(item, "descending") ?? false,
                }
            );
        }

        return orderBy;
    }

    /// <summary>射影フィールド配列を読み、モデル化する（type / source_column は排他・source_column は所属エンティティの列に限る）</summary>
    private static List<ProjectionField> BuildFields(
        JsonElement args,
        Entity entity,
        string tableName,
        List<string> errors
    )
    {
        var fields = new List<ProjectionField>();

        if (!args.TryGetProperty("fields", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return fields;
        }

        foreach (var item in array.EnumerateArray())
        {
            var name = GetString(item, "name");

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("A projection field is missing its required 'name'.");
                continue;
            }

            var type = GetString(item, "type");
            var sourceColumn = GetString(item, "source_column");
            var hasType = !string.IsNullOrWhiteSpace(type);
            var hasSource = !string.IsNullOrWhiteSpace(sourceColumn);

            if (hasType == hasSource)
            {
                errors.Add(
                    $"Projection field '{name}' must specify exactly one of 'type' or 'source_column'."
                );
                continue;
            }

            Guid? sourceColumnId = null;

            if (hasSource)
            {
                var column = FindColumn(entity, sourceColumn!);

                if (column is null)
                {
                    errors.Add(
                        $"Projection field '{name}': source_column '{sourceColumn}' not found in table '{tableName}'."
                    );
                    continue;
                }

                sourceColumnId = column.Id;
            }

            fields.Add(
                new ProjectionField
                {
                    Name = name!,
                    Type = hasType ? type : null,
                    SourceColumnId = sourceColumnId,
                    // 省略時は null（＝自動: 列参照は列の NULL 許容に従い、自由フィールドは常に NULL 許容）
                    IsNullable = GetBool(item, "is_nullable"),
                }
            );
        }

        return fields;
    }

    /// <summary>方言別 SQL 辞書を読む（キーは 5 方言名のみ許可・値は文字列。未知方言・非文字列はエラー）</summary>
    private static Dictionary<string, string> BuildSql(JsonElement args, List<string> errors)
    {
        var sql = new Dictionary<string, string>();

        if (!args.TryGetProperty("sql", out var obj) || obj.ValueKind != JsonValueKind.Object)
        {
            return sql;
        }

        foreach (var property in obj.EnumerateObject())
        {
            var canonical = SupportedDbms.FirstOrDefault(dbms =>
                string.Equals(dbms, property.Name, StringComparison.OrdinalIgnoreCase)
            );

            if (canonical is null)
            {
                errors.Add(
                    $"Unknown SQL dialect '{property.Name}' (must be one of: {string.Join(", ", SupportedDbms)})."
                );
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                errors.Add($"SQL for dialect '{property.Name}' must be a string.");
                continue;
            }

            sql[canonical] = property.Value.GetString()!;
        }

        return sql;
    }

    // ---------------- list_queries ----------------

    /// <summary>図の名前付きクエリをエンティティ別に一覧する（英語テキスト。読み取り系＝新フォーマットは警告付きで続行）</summary>
    private static (string, bool) ListQueries(string file)
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

        if (document!.IsNewerFormat)
        {
            sb.AppendLine(
                $"Warning: this diagram was saved in a newer format (version {document.Version} > supported {DiagramDocument.CurrentVersion}); unknown data may be omitted. Showing a best-effort listing."
            );
            sb.AppendLine();
        }

        var schema = document.Schema;
        sb.AppendLine($"Queries: {schema.Queries.Count}");

        if (schema.Queries.Count == 0)
        {
            return (sb.ToString().TrimEnd(), true);
        }

        // エンティティ順に、そのエンティティ配下のクエリを列挙する
        foreach (var entity in schema.Entities)
        {
            var entityQueries = schema.Queries.Where(query => query.EntityId == entity.Id).ToList();

            if (entityQueries.Count == 0)
            {
                continue;
            }

            sb.AppendLine();
            sb.AppendLine($"[{entity.TableName}]");

            foreach (var query in entityQueries)
            {
                AppendQuerySummary(sb, entity, query);
            }
        }

        // 参照先エンティティが消えたダングリングクエリも見えるようにする
        var knownEntityIds = schema.Entities.Select(entity => entity.Id).ToHashSet();
        var orphans = schema
            .Queries.Where(query => !knownEntityIds.Contains(query.EntityId))
            .ToList();

        if (orphans.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[(unknown entity)]");

            foreach (var query in orphans)
            {
                AppendQuerySummary(sb, null, query);
            }
        }

        return (sb.ToString().TrimEnd(), true);
    }

    /// <summary>クエリ 1 件の要約（戻り形・実装方式・条件／SQL・パラメータ等）をテキスト化する</summary>
    private static void AppendQuerySummary(StringBuilder sb, Entity? entity, QueryDefinition query)
    {
        sb.AppendLine($"  - {query.Name}: {Lower(query.Returns)}, {Lower(query.Implementation)}");

        if (!string.IsNullOrWhiteSpace(query.Description))
        {
            sb.AppendLine($"      description: {query.Description}");
        }

        if (
            query.Returns == QueryReturnShape.Scalar
            && !string.IsNullOrWhiteSpace(query.ScalarType)
        )
        {
            sb.AppendLine($"      scalar type: {query.ScalarType}");
        }

        if (
            query.Implementation == QueryImplementationKind.Dsl
            && !string.IsNullOrWhiteSpace(query.Condition)
        )
        {
            sb.AppendLine($"      condition: {query.Condition}");
        }
        else if (query.Implementation == QueryImplementationKind.Sql && query.Sql.Count > 0)
        {
            sb.AppendLine(
                $"      sql dialects: {string.Join(", ", query.Sql.Keys.OrderBy(k => k))}"
            );
        }

        if (query.Parameters.Count > 0)
        {
            var parts = query.Parameters.Select(parameter => DescribeParameter(entity, parameter));
            sb.AppendLine($"      parameters: {string.Join(", ", parts)}");
        }

        if (query.OrderBy.Count > 0)
        {
            var parts = query.OrderBy.Select(ordering => DescribeOrdering(entity, ordering));
            sb.AppendLine($"      order by: {string.Join(", ", parts)}");
        }

        if (query.HasPaging)
        {
            sb.AppendLine("      paging: yes");
        }

        if (query.Returns == QueryReturnShape.Projection)
        {
            sb.AppendLine($"      result type: {query.ResultTypeName}");

            if (query.Fields.Count > 0)
            {
                var parts = query.Fields.Select(field => DescribeField(entity, field));
                sb.AppendLine($"      fields: {string.Join(", ", parts)}");
            }
        }
    }

    /// <summary>パラメータ 1 件を「名前: 型（または列参照）［(list)］」に整形する</summary>
    private static string DescribeParameter(Entity? entity, QueryParameter parameter)
    {
        var typeText = parameter.SourceColumnId is { } columnId
            ? $"column {ColumnName(entity, columnId)}"
            : parameter.Type ?? "(untyped)";
        var list = parameter.IsList ? " (list)" : string.Empty;

        return $"{parameter.Name}: {typeText}{list}";
    }

    /// <summary>並び順 1 件を「列名 [desc]」に整形する</summary>
    private static string DescribeOrdering(Entity? entity, QueryOrdering ordering)
    {
        var name = ColumnName(entity, ordering.ColumnId);

        return ordering.Descending ? $"{name} desc" : name;
    }

    /// <summary>射影フィールド 1 件を「名前: 型（または列参照）」に整形する</summary>
    private static string DescribeField(Entity? entity, ProjectionField field)
    {
        var typeText = field.SourceColumnId is { } columnId
            ? $"column {ColumnName(entity, columnId)}"
            : field.Type ?? "(untyped)";

        return $"{field.Name}: {typeText}";
    }

    // ---------------- remove_query ----------------

    /// <summary>テーブル名＋クエリ名で 1 件削除する（不在はエラー）</summary>
    private static (string, bool) RemoveQuery(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");
        var queryName = GetString(args, "query_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(queryName))
        {
            return ("table_name and query_name are required.", false);
        }

        var schema = document.Schema;
        var entity = FindEntity(schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        var query = schema.Queries.FirstOrDefault(candidate =>
            candidate.EntityId == entity.Id
            && string.Equals(candidate.Name, queryName, StringComparison.OrdinalIgnoreCase)
        );

        if (query is null)
        {
            return ($"Query '{queryName}' not found on table '{tableName}'.", false);
        }

        schema.Queries.Remove(query);

        return ($"Removed query '{queryName}' from table '{tableName}'.", true);
    }

    // ---------------- helpers ----------------

    /// <summary>列名でエンティティ内の列を検索する（大文字小文字を区別しない・最初の一致）</summary>
    private static Column? FindColumn(Entity entity, string columnName) =>
        entity.Columns.FirstOrDefault(column =>
            string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>列 ID からエンティティ内の列名を引く（未解決は "(unknown)"）</summary>
    private static string ColumnName(Entity? entity, Guid columnId) =>
        entity?.Columns.FirstOrDefault(column => column.Id == columnId)?.Name ?? "(unknown)";

    /// <summary>戻り形の入力文字列（list/single/count/scalar/projection）を列挙値へ解釈する</summary>
    private static bool TryParseReturnShape(string? value, out QueryReturnShape shape)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "list":
                shape = QueryReturnShape.List;
                return true;
            case "single":
                shape = QueryReturnShape.Single;
                return true;
            case "count":
                shape = QueryReturnShape.Count;
                return true;
            case "scalar":
                shape = QueryReturnShape.Scalar;
                return true;
            case "projection":
                shape = QueryReturnShape.Projection;
                return true;
            default:
                shape = QueryReturnShape.List;
                return false;
        }
    }

    /// <summary>実装方式の入力文字列（dsl/sql/manual）を列挙値へ解釈する（未指定・空は dsl）</summary>
    private static bool TryParseImplementation(string? value, out QueryImplementationKind kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            kind = QueryImplementationKind.Dsl;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "dsl":
                kind = QueryImplementationKind.Dsl;
                return true;
            case "sql":
                kind = QueryImplementationKind.Sql;
                return true;
            case "manual":
                kind = QueryImplementationKind.Manual;
                return true;
            default:
                kind = QueryImplementationKind.Dsl;
                return false;
        }
    }

    /// <summary>列挙値を小文字表記へ（戻り形・実装方式の表示・応答用）</summary>
    private static string Lower<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();

    /// <summary>JSON 引数から真偽プロパティを取得する（無い・型不一致なら null）</summary>
    private static bool? GetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
