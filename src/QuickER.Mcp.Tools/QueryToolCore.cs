using System.Text.Json;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Model;
using static QuickER.Mcp.Tools.SchemaLookup;
using static QuickER.Mcp.Tools.ToolArguments;

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
/// 生成で必ずエラーになる構造規則（メソッド名・予約名・重複・識別子・戻り形と実装方式・ページングの整合）は
/// 生成器と同じ <see cref="QueryDefinitionRules.Validate"/> を通し、違反があれば保存しない。
/// 型トークンの内容検証（方言依存）は行わず、生成時検証に委ねる。
/// </para>
/// </remarks>
public static class QueryToolCore
{
    /// <summary>クエリ定義を upsert するツールの名前（正本は <see cref="ErDiagramToolNames"/>）</summary>
    public const string SetQueryToolName = ErDiagramToolNames.SetQuery;

    /// <summary>クエリ定義を一覧するツール（読み取り系）の名前（正本は <see cref="ErDiagramToolNames"/>）</summary>
    public const string ListQueriesToolName = ErDiagramToolNames.ListQueries;

    /// <summary>クエリ定義を 1 件削除するツールの名前（正本は <see cref="ErDiagramToolNames"/>）</summary>
    public const string RemoveQueryToolName = ErDiagramToolNames.RemoveQuery;

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

        // クエリ名は保存する形（前後の空白を除いた形）で扱う（上書きの照合・応答・保存が同じ名前になる）
        queryName = SchemaNames.ToStoredForm(queryName);
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
        // 型トークンの前後の空白は除いて扱う（GUI のクエリ定義ダイアログと同じ）
        var scalarType = GetString(args, "scalar_type")?.Trim();
        var condition = GetString(args, "condition");
        var resultTypeName = GetString(args, "result_type_name");
        var paging = GetBool(args, "paging") ?? false;

        var errors = new List<QueryToolDiagnostic>();
        var warnings = new List<QueryToolDiagnostic>();

        var parameters = BuildParameters(args, entity, tableName, errors);
        var orderBy = BuildOrderBy(args, entity, tableName, errors);
        var fields = BuildFields(args, entity, tableName, errors);
        var sql = BuildSql(args, implementation, errors, warnings);

        // ---- DSL 構文・参照検証（implementation=dsl） ----
        if (implementation == QueryImplementationKind.Dsl)
        {
            AddDslDiagnostics(condition, entity, parameters, errors, warnings);
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

        // ---- 実装方式に合わない入力（拒否はせず、使わないことを警告で知らせる） ----
        // GUI のクエリ定義ダイアログと同じく、sql 方式以外では SQL を保存しない（condition は従来から dsl のときだけ保存）
        var implementationText = implementation.ToString().ToLowerInvariant();

        if (implementation != QueryImplementationKind.Sql && sql.Count > 0)
        {
            warnings.Add(
                new QueryToolDiagnostic(
                    QueryToolDiagnosticCode.SqlIgnoredForImplementation,
                    Detail: implementationText
                )
            );
        }

        // 並び順は GUI のクエリ定義ダイアログと同じく保存する（実装方式を dsl へ戻したときに効く）が、今は使われない
        if (implementation != QueryImplementationKind.Dsl && orderBy.Count > 0)
        {
            warnings.Add(
                new QueryToolDiagnostic(
                    QueryToolDiagnosticCode.OrderByIgnoredForImplementation,
                    Detail: implementationText
                )
            );
        }

        if (implementation != QueryImplementationKind.Dsl && !string.IsNullOrWhiteSpace(condition))
        {
            warnings.Add(
                new QueryToolDiagnostic(
                    QueryToolDiagnosticCode.ConditionIgnoredForImplementation,
                    Detail: implementationText
                )
            );
        }

        // 中身のある SQL が 1 方言も無ければ、生成は全方言で手動実装（partial）になる
        if (implementation == QueryImplementationKind.Sql && sql.Count == 0)
        {
            warnings.Add(
                new QueryToolDiagnostic(QueryToolDiagnosticCode.SqlImplementationWithoutSql)
            );
        }

        // ---- upsert の相手（既存は Id 温存で丸ごと置換・なければ追加） ----
        var existing = diagram.Queries.FirstOrDefault(query =>
            query.EntityId == entity.Id && SchemaNames.AreSame(query.Name, queryName)
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
            Sql = implementation == QueryImplementationKind.Sql ? sql : [],
            ResultTypeName = string.IsNullOrWhiteSpace(resultTypeName) ? null : resultTypeName,
            Fields = fields,
        };

        // ---- 生成器と同じ構造規則（生成で必ずエラーになる定義を保存しない）。置き換える既存の定義は比較から外す ----
        var otherQueries = diagram.Queries.Where(query => !ReferenceEquals(query, existing));

        foreach (
            var violation in QueryDefinitionRules.Validate(
                entity,
                definition,
                otherQueries,
                diagram.Entities
            )
        )
        {
            errors.AddRange(ToToolDiagnostics(violation, definition, returnsInput!));
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

    /// <summary>簡易 DSL の条件の診断を積む（構文・参照のエラーと、条件で使わないパラメータの警告）</summary>
    /// <remarks>
    /// 条件を書かないクエリで宣言したパラメータも、条件で使わないパラメータと同じく警告する（生成メソッドの
    /// 使われない引数になり、全行が返る）。
    /// </remarks>
    private static void AddDslDiagnostics(
        string? condition,
        Entity entity,
        List<QueryParameter> parameters,
        List<QueryToolDiagnostic> errors,
        List<QueryToolDiagnostic> warnings
    )
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(condition))
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

                return;
            }

            referenced.UnionWith(
                parsed.ParameterReferences.Select(reference =>
                    reference.ResolvedName ?? reference.Text
                )
            );
        }

        // 宣言済みだが条件で使わないパラメータは警告（生成メソッドの使われない引数になる）
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

    /// <summary>構造規則の違反を、ツールの引数名で言う診断へ写す（写せない種別は生成時診断と同じ文言のまま）</summary>
    /// <remarks>
    /// 判定は <see cref="QueryDefinitionRules.Validate"/> だけが行い、ここは文言を選ぶだけ（ツールで同じ判定を
    /// 書き直さない）。射影の見出しの違反は、欠けているものを名指しする 2 つの診断へ分ける。
    /// </remarks>
    private static IEnumerable<QueryToolDiagnostic> ToToolDiagnostics(
        QueryRuleViolation violation,
        QueryDefinition definition,
        string returnsInput
    )
    {
        switch (violation.Kind)
        {
            case QueryRuleKind.ScalarRequiresType:
                yield return new QueryToolDiagnostic(
                    QueryToolDiagnosticCode.ScalarRequiresScalarType
                );
                break;
            case QueryRuleKind.ProjectionRequiresFields:
                if (string.IsNullOrWhiteSpace(definition.ResultTypeName))
                {
                    yield return new QueryToolDiagnostic(
                        QueryToolDiagnosticCode.ProjectionRequiresResultTypeName
                    );
                }

                if (definition.Fields.Count == 0)
                {
                    yield return new QueryToolDiagnostic(
                        QueryToolDiagnosticCode.ProjectionRequiresFields
                    );
                }

                break;
            case QueryRuleKind.OrderByRequiresList:
                yield return new QueryToolDiagnostic(
                    QueryToolDiagnosticCode.OrderByInvalidForReturnShape,
                    Detail: returnsInput
                );
                break;
            default:
                yield return new QueryToolDiagnostic(
                    QueryToolDiagnosticCode.GenerationRule,
                    Detail: violation.Kind.ToString(),
                    DetailText: violation.Text
                );
                break;
        }
    }

    /// <summary>パラメータ・射影フィールドの要素 1 つの読み取り結果（名前・型トークン・列参照）</summary>
    private readonly record struct TypedItem(string Name, string? Type, Guid? SourceColumnId);

    /// <summary>パラメータ・射影フィールドの診断の種別（要素の種類ごとに違う）</summary>
    private readonly record struct TypedItemCodes(
        QueryToolDiagnosticCode MissingName,
        QueryToolDiagnosticCode TypeSourceExclusive,
        QueryToolDiagnosticCode SourceColumnNotFound
    );

    /// <summary>
    /// name と、type / source_column のどちらか一方を持つ要素を読む（パラメータと射影フィールドが共有する規則）
    /// </summary>
    /// <returns>読めた要素。規則に合わなければ診断を積んで null</returns>
    private static TypedItem? ReadTypedItem(
        JsonElement item,
        Entity entity,
        string tableName,
        TypedItemCodes codes,
        List<QueryToolDiagnostic> errors
    )
    {
        var name = GetString(item, "name");

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(new QueryToolDiagnostic(codes.MissingName));
            return null;
        }

        var type = GetString(item, "type");
        var sourceColumn = GetString(item, "source_column");
        var hasType = !string.IsNullOrWhiteSpace(type);
        var hasSource = !string.IsNullOrWhiteSpace(sourceColumn);

        // type と source_column はどちらか一方（両方指定・両方欠落はエラー）
        if (hasType == hasSource)
        {
            errors.Add(new QueryToolDiagnostic(codes.TypeSourceExclusive, Name: name));
            return null;
        }

        if (!hasSource)
        {
            // 型トークンの前後の空白は除いて保存する（GUI のクエリ定義ダイアログと同じ）
            return new TypedItem(name, type!.Trim(), null);
        }

        if (FindColumn(entity, sourceColumn!) is not { } column)
        {
            errors.Add(
                new QueryToolDiagnostic(
                    codes.SourceColumnNotFound,
                    Name: name,
                    Column: sourceColumn,
                    Table: tableName
                )
            );
            return null;
        }

        // 列参照は列由来で型が決まるため型トークンは保存しない（モデルの規約に合わせる）
        return new TypedItem(name, null, column.Id);
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

        var codes = new TypedItemCodes(
            QueryToolDiagnosticCode.ParameterMissingName,
            QueryToolDiagnosticCode.ParameterTypeSourceExclusive,
            QueryToolDiagnosticCode.ParameterSourceColumnNotFound
        );

        foreach (var item in array.EnumerateArray())
        {
            if (ReadTypedItem(item, entity, tableName, codes, errors) is { } read)
            {
                parameters.Add(
                    new QueryParameter
                    {
                        Name = read.Name,
                        Type = read.Type,
                        IsList = GetBool(item, "is_list") ?? false,
                        SourceColumnId = read.SourceColumnId,
                    }
                );
            }
        }

        return parameters;
    }

    /// <summary>並び順配列を読み、モデル化する（列名 → 列 ID 解決。戻り形との整合は構造規則が見る）</summary>
    private static List<QueryOrdering> BuildOrderBy(
        JsonElement args,
        Entity entity,
        string tableName,
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

        var codes = new TypedItemCodes(
            QueryToolDiagnosticCode.ProjectionFieldMissingName,
            QueryToolDiagnosticCode.ProjectionFieldTypeSourceExclusive,
            QueryToolDiagnosticCode.ProjectionFieldSourceColumnNotFound
        );

        foreach (var item in array.EnumerateArray())
        {
            if (ReadTypedItem(item, entity, tableName, codes, errors) is { } read)
            {
                fields.Add(
                    new ProjectionField
                    {
                        Name = read.Name,
                        Type = read.Type,
                        SourceColumnId = read.SourceColumnId,
                        // 省略時は null（＝自動: 列参照は列の NULL 許容に従い、自由フィールドは常に NULL 許容）
                        IsNullable = GetBool(item, "is_nullable"),
                    }
                );
            }
        }

        return fields;
    }

    /// <summary>方言別 SQL 辞書を読む（キーは対象 DBMS の名前・値は文字列）</summary>
    /// <remarks>
    /// <para>
    /// 未知の方言・文字列でない値・大文字小文字だけ違う同じ方言のキーは、SQL を使う実装（sql）ではエラー、
    /// 使わない実装では警告にする（使われない入力の誤りで、正しい定義の保存を止めない）。
    /// </para>
    /// <para>
    /// 生成器が生 SQL の実装を作らない方言（<see cref="CodeGenerationOptions.SupportedRepositoryDialects"/> の外）の SQL は、
    /// 保存したうえで、その方言ではメソッドが手動実装の契約になることを警告する（生成のたびに分かるのでは遅い）。
    /// 空・空白だけの SQL は「その方言の SQL は無い」と同じ扱いで保存しない（GUI のクエリ定義ダイアログと同じ。
    /// 保存すると生成器が空の SQL を実行する実装を作り、手動実装（partial）にならない）。
    /// </para>
    /// </remarks>
    private static Dictionary<string, string> BuildSql(
        JsonElement args,
        QueryImplementationKind implementation,
        List<QueryToolDiagnostic> errors,
        List<QueryToolDiagnostic> warnings
    )
    {
        var sql = new Dictionary<string, string>();

        if (!args.TryGetProperty("sql", out var obj) || obj.ValueKind != JsonValueKind.Object)
        {
            return sql;
        }

        var usesSql = implementation == QueryImplementationKind.Sql;
        var problems = usesSql ? errors : warnings;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in obj.EnumerateObject())
        {
            if (TargetDbmsNames.Find(property.Name) is not { } canonical)
            {
                problems.Add(
                    new QueryToolDiagnostic(
                        QueryToolDiagnosticCode.UnknownSqlDialect,
                        Dialect: property.Name
                    )
                );
                continue;
            }

            if (!seen.Add(canonical))
            {
                problems.Add(
                    new QueryToolDiagnostic(
                        QueryToolDiagnosticCode.SqlDialectDuplicate,
                        Dialect: property.Name,
                        Detail: canonical
                    )
                );
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                problems.Add(
                    new QueryToolDiagnostic(
                        QueryToolDiagnosticCode.SqlDialectNotString,
                        Dialect: property.Name
                    )
                );
                continue;
            }

            if (string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                continue;
            }

            sql[canonical] = property.Value.GetString()!;

            if (usesSql && !CodeGenerationOptions.SupportedRepositoryDialects.Contains(canonical))
            {
                warnings.Add(
                    new QueryToolDiagnostic(
                        QueryToolDiagnosticCode.SqlDialectNotImplemented,
                        Dialect: canonical
                    )
                );
            }
        }

        return sql;
    }

    // ---------------- list_queries ----------------

    /// <summary>図の名前付きクエリをエンティティ別にまとめた構造化一覧を返す（列参照は名前へ解決済み）</summary>
    public static QueryToolOutcome ListQueries(ErDiagram diagram)
    {
        var groups = new List<QueryListingGroup>();
        var queriesByEntity = diagram.Queries.ToLookup(query => query.EntityId);

        // エンティティ順に、そのエンティティ配下のクエリを列挙する（表ごとに全クエリを走査しない）
        foreach (var entity in diagram.Entities)
        {
            var entityQueries = queriesByEntity[entity.Id].ToList();

            if (entityQueries.Count == 0)
            {
                continue;
            }

            var columnNames = entity.Columns.ToDictionary(
                column => column.Id,
                column => column.Name
            );
            groups.Add(
                new QueryListingGroup(
                    entity.TableName,
                    entityQueries.Select(query => BuildListingItem(columnNames, query)).ToList()
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

    /// <summary>クエリ 1 件を要約データへ変換する（列参照は所属エンティティの列名で解決する）</summary>
    /// <param name="columnNames">所属エンティティの列 ID → 列名（所属エンティティが無ければ null）</param>
    /// <param name="query">クエリ</param>
    private static QueryListingItem BuildListingItem(
        IReadOnlyDictionary<Guid, string>? columnNames,
        QueryDefinition query
    )
    {
        var parameters = query
            .Parameters.Select(parameter => new QueryListingParameter(
                parameter.Name,
                parameter.Type,
                parameter.SourceColumnId is not null,
                parameter.SourceColumnId is { } columnId
                    ? ResolveColumnName(columnNames, columnId)
                    : null,
                parameter.IsList
            ))
            .ToList();

        var orderBy = query
            .OrderBy.Select(ordering => new QueryListingOrder(
                ResolveColumnName(columnNames, ordering.ColumnId),
                ordering.Descending
            ))
            .ToList();

        var fields = query
            .Fields.Select(field => new QueryListingField(
                field.Name,
                field.Type,
                field.SourceColumnId is not null,
                field.SourceColumnId is { } columnId
                    ? ResolveColumnName(columnNames, columnId)
                    : null
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

        queryName = SchemaNames.ToStoredForm(queryName);
        var entity = FindEntity(diagram, tableName);

        if (entity is null)
        {
            return RemoveOrphanQueries(diagram, tableName, queryName);
        }

        var query = diagram.Queries.FirstOrDefault(candidate =>
            candidate.EntityId == entity.Id && SchemaNames.AreSame(candidate.Name, queryName)
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

    /// <summary>
    /// どのテーブルにも属さないクエリ（属していたテーブルが図に無い）を、クエリ名で探して消す。
    /// 見つからなければテーブルが無いことを返す
    /// </summary>
    /// <remarks>
    /// そうしたクエリは list_queries で見えるのに、テーブル名で照合する remove_query から消せなかった
    /// （古いファイルや手編集で残る。今の remove_entity はテーブルのクエリも消す）。属していたテーブルの名前は図に
    /// 残っていないので、テーブル名ではなくクエリ名で照合する。同じ名前が複数あれば全部消す
    /// （どれも引数で選び分けられず、生成器も読み飛ばしている）。
    /// </remarks>
    private static QueryToolOutcome RemoveOrphanQueries(
        ErDiagram diagram,
        string tableName,
        string queryName
    )
    {
        var entityIds = diagram.Entities.Select(entity => entity.Id).ToHashSet();
        var orphans = diagram
            .Queries.Where(query =>
                !entityIds.Contains(query.EntityId) && SchemaNames.AreSame(query.Name, queryName)
            )
            .ToList();

        if (orphans.Count == 0)
        {
            return new QueryToolOutcome
            {
                Status = QueryToolStatus.TableNotFound,
                TableName = tableName,
                QueryName = queryName,
            };
        }

        diagram.Queries.RemoveAll(orphans.Contains);

        return new QueryToolOutcome
        {
            Status = QueryToolStatus.Success,
            TableName = tableName,
            QueryName = queryName,
            RemovedOrphans = orphans.Count,
        };
    }

    // ---------------- helpers ----------------

    /// <summary>列 ID から列名を引く（未解決は null＝面側で表示トークンを決める）</summary>
    private static string? ResolveColumnName(
        IReadOnlyDictionary<Guid, string>? columnNames,
        Guid columnId
    ) => columnNames is not null && columnNames.TryGetValue(columnId, out var name) ? name : null;

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
}
