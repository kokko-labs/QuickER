using System.Text.Json;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Model;

namespace QuickER.Mcp.Tools;

/// <summary>
/// 名前付きクエリツール（set_query / list_queries / remove_query）の実行コア。
/// </summary>
/// <remarks>
/// <para>
/// 面（MCP ファイルホスト / 内蔵チャット）に依らない共有部品。入力は意味モデル（<see cref="ErDiagram"/>）と
/// 引数 JSON、出力は表示文字列を含まない構造化結果（<see cref="QueryToolOutcome"/>）。文字列化・ファイル IO は
/// 面側の責務で、本コアは <see cref="ErDiagram.Queries"/> の更新までを担う（成功時のみ変更・失敗時は無変更）。
/// </para>
/// <para>
/// 検証の意味論は生成側（<c>QuickER.CodeGen.CSharp</c> の <c>CSharpGenerationModelBuilder.Queries</c>）・
/// GUI のクエリ定義ダイアログと揃える：DSL は <see cref="QueryConditionParser"/>、生 SQL は
/// <see cref="RawSqlAnalyzer"/> を用い、未宣言パラメータは保存拒否・未使用パラメータ／複文は警告のみ。
/// 型トークンの内容検証（方言依存）は行わず、生成時検証に委ねる。
/// </para>
/// </remarks>
public static class QueryToolCore
{
    /// <summary>クエリ定義を upsert するツールの名前（全面共通の正本）</summary>
    public const string SetQueryToolName = "set_query";

    /// <summary>クエリ定義を一覧するツール（読み取り系）の名前（全面共通の正本）</summary>
    public const string ListQueriesToolName = "list_queries";

    /// <summary>クエリ定義を 1 件削除するツールの名前（全面共通の正本）</summary>
    public const string RemoveQueryToolName = "remove_query";

    /// <summary>SQL 方言辞書のキーに指定できるプロバイダ識別名（クエリツール共通の正本）</summary>
    public static readonly string[] SupportedDbms =
    [
        "sqlserver",
        "postgresql",
        "mysql",
        "oracle",
        "sqlite",
    ];

    // ---------------- set_query ----------------

    /// <summary>
    /// 名前付きクエリ定義を 1 件 upsert する。(<c>table_name</c>, <c>query_name</c>) で照合し、既存があれば
    /// 丸ごと置換（<see cref="QueryDefinition.Id"/> は温存）、なければ追加する。検証エラー時は図を変更しない。
    /// </summary>
    public static QueryToolOutcome SetQuery(ErDiagram diagram, JsonElement args)
    {
        var tableName = GetString(args, "table_name");
        var queryName = GetString(args, "query_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(queryName))
        {
            return new QueryToolOutcome { Status = QueryToolStatus.MissingArgument };
        }

        var entity = FindEntity(diagram, tableName);

        if (entity is null)
        {
            return new QueryToolOutcome
            {
                Status = QueryToolStatus.TableNotFound,
                TableName = tableName,
                QueryName = queryName,
            };
        }

        // 戻り形（必須）・実装方式（既定 dsl）を先に確定する（以降の構造検証がこれらに依存するため）
        var returnsInput = GetString(args, "returns");

        if (!TryParseReturnShape(returnsInput, out var returns))
        {
            return new QueryToolOutcome
            {
                Status = QueryToolStatus.InvalidReturns,
                TableName = tableName,
                QueryName = queryName,
            };
        }

        var implementationInput = GetString(args, "implementation");

        if (!TryParseImplementation(implementationInput, out var implementation))
        {
            return new QueryToolOutcome
            {
                Status = QueryToolStatus.InvalidImplementation,
                TableName = tableName,
                QueryName = queryName,
            };
        }

        var description = GetString(args, "description") ?? string.Empty;
        var scalarType = GetString(args, "scalar_type");
        var condition = GetString(args, "condition");
        var resultTypeName = GetString(args, "result_type_name");
        var paging = GetBool(args, "paging") ?? false;

        var errors = new List<QueryToolDiagnostic>();
        var warnings = new List<QueryToolDiagnostic>();

        var parameters = BuildParameters(args, entity, tableName, errors);
        var orderBy = BuildOrderBy(args, entity, tableName, returns, returnsInput!, errors);
        var fields = BuildFields(args, entity, tableName, errors);
        var sql = BuildSql(args, errors);

        // ---- 構造検証 ----
        if (returns == QueryReturnShape.Scalar && string.IsNullOrWhiteSpace(scalarType))
        {
            errors.Add(new QueryToolDiagnostic(QueryToolDiagnosticCode.ScalarRequiresScalarType));
        }

        if (returns == QueryReturnShape.Projection)
        {
            if (string.IsNullOrWhiteSpace(resultTypeName))
            {
                errors.Add(
                    new QueryToolDiagnostic(
                        QueryToolDiagnosticCode.ProjectionRequiresResultTypeName
                    )
                );
            }

            if (fields.Count == 0)
            {
                errors.Add(
                    new QueryToolDiagnostic(QueryToolDiagnosticCode.ProjectionRequiresFields)
                );
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
                    errors.Add(
                        new QueryToolDiagnostic(
                            QueryToolDiagnosticCode.ConditionDiagnostic,
                            DetailText: diagnostic.Text
                        )
                    );
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
                            new QueryToolDiagnostic(
                                QueryToolDiagnosticCode.ParameterUnusedInCondition,
                                Name: parameter.Name
                            )
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
                    var diagnostic = new QueryToolDiagnostic(
                        QueryToolDiagnosticCode.RawSqlDiagnostic,
                        Dialect: dialect,
                        DetailText: RawSqlAnalyzer.DescribeText(finding)
                    );

                    if (finding.Kind == RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter)
                    {
                        errors.Add(diagnostic);
                    }
                    else
                    {
                        warnings.Add(diagnostic);
                    }
                }
            }
        }

        if (errors.Count > 0)
        {
            return new QueryToolOutcome
            {
                Status = QueryToolStatus.ValidationFailed,
                TableName = tableName,
                QueryName = queryName,
                Errors = errors,
            };
        }

        // ---- upsert（既存は Id 温存で丸ごと置換・なければ追加） ----
        var existing = diagram.Queries.FirstOrDefault(query =>
            query.EntityId == entity.Id
            && string.Equals(query.Name, queryName, StringComparison.OrdinalIgnoreCase)
        );

        var definition = new QueryDefinition
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            EntityId = entity.Id,
            Name = queryName,
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

        bool wasUpdate;

        if (existing is not null)
        {
            diagram.Queries[diagram.Queries.IndexOf(existing)] = definition;
            wasUpdate = true;
        }
        else
        {
            diagram.Queries.Add(definition);
            wasUpdate = false;
        }

        return new QueryToolOutcome
        {
            Status = QueryToolStatus.Success,
            TableName = tableName,
            QueryName = queryName,
            WasUpdate = wasUpdate,
            Returns = returns,
            Implementation = implementation,
            Warnings = warnings,
        };
    }

    /// <summary>パラメータ配列を読み、モデル化する（type / source_column は排他・source_column は所属エンティティの列に限る）</summary>
    private static List<QueryParameter> BuildParameters(
        JsonElement args,
        Entity entity,
        string tableName,
        List<QueryToolDiagnostic> errors
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
                errors.Add(new QueryToolDiagnostic(QueryToolDiagnosticCode.ParameterMissingName));
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
                    new QueryToolDiagnostic(
                        QueryToolDiagnosticCode.ParameterTypeSourceExclusive,
                        Name: name
                    )
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
                        new QueryToolDiagnostic(
                            QueryToolDiagnosticCode.ParameterSourceColumnNotFound,
                            Name: name,
                            Column: sourceColumn,
                            Table: tableName
                        )
                    );
                    continue;
                }

                sourceColumnId = column.Id;
            }

            parameters.Add(
                new QueryParameter
                {
                    Name = name,
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
        List<QueryToolDiagnostic> errors
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
                new QueryToolDiagnostic(
                    QueryToolDiagnosticCode.OrderByInvalidForReturnShape,
                    Detail: returnsInput
                )
            );
        }

        foreach (var item in array.EnumerateArray())
        {
            var columnName = GetString(item, "column");

            if (string.IsNullOrWhiteSpace(columnName))
            {
                errors.Add(new QueryToolDiagnostic(QueryToolDiagnosticCode.OrderByMissingColumn));
                continue;
            }

            var column = FindColumn(entity, columnName);

            if (column is null)
            {
                errors.Add(
                    new QueryToolDiagnostic(
                        QueryToolDiagnosticCode.OrderByColumnNotFound,
                        Column: columnName,
                        Table: tableName
                    )
                );
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
        List<QueryToolDiagnostic> errors
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
                errors.Add(
                    new QueryToolDiagnostic(QueryToolDiagnosticCode.ProjectionFieldMissingName)
                );
                continue;
            }

            var type = GetString(item, "type");
            var sourceColumn = GetString(item, "source_column");
            var hasType = !string.IsNullOrWhiteSpace(type);
            var hasSource = !string.IsNullOrWhiteSpace(sourceColumn);

            if (hasType == hasSource)
            {
                errors.Add(
                    new QueryToolDiagnostic(
                        QueryToolDiagnosticCode.ProjectionFieldTypeSourceExclusive,
                        Name: name
                    )
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
                        new QueryToolDiagnostic(
                            QueryToolDiagnosticCode.ProjectionFieldSourceColumnNotFound,
                            Name: name,
                            Column: sourceColumn,
                            Table: tableName
                        )
                    );
                    continue;
                }

                sourceColumnId = column.Id;
            }

            fields.Add(
                new ProjectionField
                {
                    Name = name,
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
    private static Dictionary<string, string> BuildSql(
        JsonElement args,
        List<QueryToolDiagnostic> errors
    )
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
                    new QueryToolDiagnostic(
                        QueryToolDiagnosticCode.UnknownSqlDialect,
                        Dialect: property.Name
                    )
                );
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                errors.Add(
                    new QueryToolDiagnostic(
                        QueryToolDiagnosticCode.SqlDialectNotString,
                        Dialect: property.Name
                    )
                );
                continue;
            }

            sql[canonical] = property.Value.GetString()!;
        }

        return sql;
    }

    // ---------------- list_queries ----------------

    /// <summary>図の名前付きクエリをエンティティ別にまとめた構造化一覧を返す（列参照は名前へ解決済み）</summary>
    public static QueryToolOutcome ListQueries(ErDiagram diagram)
    {
        var groups = new List<QueryListingGroup>();

        // エンティティ順に、そのエンティティ配下のクエリを列挙する
        foreach (var entity in diagram.Entities)
        {
            var entityQueries = diagram
                .Queries.Where(query => query.EntityId == entity.Id)
                .ToList();

            if (entityQueries.Count == 0)
            {
                continue;
            }

            groups.Add(
                new QueryListingGroup(
                    entity.TableName,
                    entityQueries.Select(query => BuildListingItem(entity, query)).ToList()
                )
            );
        }

        // 参照先エンティティが消えたダングリングクエリも見えるようにする
        var knownEntityIds = diagram.Entities.Select(entity => entity.Id).ToHashSet();
        var orphans = diagram
            .Queries.Where(query => !knownEntityIds.Contains(query.EntityId))
            .ToList();

        if (orphans.Count > 0)
        {
            groups.Add(
                new QueryListingGroup(
                    null,
                    orphans.Select(query => BuildListingItem(null, query)).ToList()
                )
            );
        }

        return new QueryToolOutcome
        {
            Status = QueryToolStatus.Success,
            Listing = new QueryListing(diagram.Queries.Count, groups),
        };
    }

    /// <summary>クエリ 1 件を要約データへ変換する（列参照は所属エンティティで名前解決する）</summary>
    private static QueryListingItem BuildListingItem(Entity? entity, QueryDefinition query)
    {
        var parameters = query
            .Parameters.Select(parameter => new QueryListingParameter(
                parameter.Name,
                parameter.Type,
                parameter.SourceColumnId is not null,
                parameter.SourceColumnId is { } columnId
                    ? ResolveColumnName(entity, columnId)
                    : null,
                parameter.IsList
            ))
            .ToList();

        var orderBy = query
            .OrderBy.Select(ordering => new QueryListingOrder(
                ResolveColumnName(entity, ordering.ColumnId),
                ordering.Descending
            ))
            .ToList();

        var fields = query
            .Fields.Select(field => new QueryListingField(
                field.Name,
                field.Type,
                field.SourceColumnId is not null,
                field.SourceColumnId is { } columnId ? ResolveColumnName(entity, columnId) : null
            ))
            .ToList();

        return new QueryListingItem(
            query.Name,
            query.Returns,
            query.Implementation,
            string.IsNullOrWhiteSpace(query.Description) ? null : query.Description,
            string.IsNullOrWhiteSpace(query.ScalarType) ? null : query.ScalarType,
            string.IsNullOrWhiteSpace(query.Condition) ? null : query.Condition,
            query.Sql.Keys.OrderBy(key => key).ToList(),
            parameters,
            orderBy,
            query.HasPaging,
            query.ResultTypeName,
            fields
        );
    }

    // ---------------- remove_query ----------------

    /// <summary>テーブル名＋クエリ名で 1 件削除する（不在はエラー）。成功時のみ図を変更する</summary>
    public static QueryToolOutcome RemoveQuery(ErDiagram diagram, JsonElement args)
    {
        var tableName = GetString(args, "table_name");
        var queryName = GetString(args, "query_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(queryName))
        {
            return new QueryToolOutcome { Status = QueryToolStatus.MissingArgument };
        }

        var entity = FindEntity(diagram, tableName);

        if (entity is null)
        {
            return new QueryToolOutcome
            {
                Status = QueryToolStatus.TableNotFound,
                TableName = tableName,
                QueryName = queryName,
            };
        }

        var query = diagram.Queries.FirstOrDefault(candidate =>
            candidate.EntityId == entity.Id
            && string.Equals(candidate.Name, queryName, StringComparison.OrdinalIgnoreCase)
        );

        if (query is null)
        {
            return new QueryToolOutcome
            {
                Status = QueryToolStatus.QueryNotFound,
                TableName = tableName,
                QueryName = queryName,
            };
        }

        diagram.Queries.Remove(query);

        return new QueryToolOutcome
        {
            Status = QueryToolStatus.Success,
            TableName = tableName,
            QueryName = queryName,
        };
    }

    // ---------------- helpers ----------------

    /// <summary>テーブル名でエンティティを検索する（大文字小文字を区別しない・最初の一致）</summary>
    private static Entity? FindEntity(ErDiagram diagram, string tableName) =>
        diagram.Entities.FirstOrDefault(entity =>
            string.Equals(entity.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>列名でエンティティ内の列を検索する（大文字小文字を区別しない・最初の一致）</summary>
    private static Column? FindColumn(Entity entity, string columnName) =>
        entity.Columns.FirstOrDefault(column =>
            string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>列 ID からエンティティ内の列名を引く（未解決は null＝面側で表示トークンを決める）</summary>
    private static string? ResolveColumnName(Entity? entity, Guid columnId) =>
        entity?.Columns.FirstOrDefault(column => column.Id == columnId)?.Name;

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

    /// <summary>JSON 引数から文字列プロパティを取得する（無い・型不一致なら null）</summary>
    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>JSON 引数から真偽プロパティを取得する（無い・型不一致なら null）</summary>
    private static bool? GetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
