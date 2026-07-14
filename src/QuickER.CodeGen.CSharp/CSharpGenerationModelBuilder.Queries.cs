using System.Text;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>名前付きクエリ（<see cref="ErDiagram.Queries"/>）から Repository メソッドの生成モデルを構築する部分</summary>
/// <remarks>
/// <para>
/// 出力は「整形済みのメンバーテキスト」（契約メンバー・実装メンバー・射影 DTO）で、テンプレート側は
/// それを差し込むだけにする（Scriban 側のロジックを増やさない）。実装の出し分けは統一規則
/// 「<b>SQL（または DSL 由来の共有本体）が与えられていない実装先は manual</b>＝契約宣言のみ生成し、
/// 実装はユーザーの partial クラスが担う（漏れはコンパイルエラーで検出）」に従う：
/// </para>
/// <list type="bullet">
///   <item>ミニ DSL（<see cref="QueryImplementationKind.Dsl"/>）→ 単一の共有本体を
///     Repository (QuickER)（全方言）・EF Core・インメモリのすべてへ出力</item>
///   <item>自由 SQL（<see cref="QueryImplementationKind.Sql"/>）→ SQL 辞書にある方言の
///     Repository (QuickER) のみへ出力（EF Core・インメモリ・辞書にない方言は manual 扱い）</item>
///   <item>manual（<see cref="QueryImplementationKind.Manual"/>）→ 契約宣言のみ</item>
/// </list>
/// </remarks>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>型トークン → 解決済み C# 型情報（クエリパラメータ・スカラー・射影フィールドの型解決に使う）。
    /// 列型辞書と同じく Build 呼び出し時に外部（プロバイダ側）から受け取る</summary>
    private IReadOnlyDictionary<string, CSharpTypeInfo> _queryTokenTypes = new Dictionary<
        string,
        CSharpTypeInfo
    >(StringComparer.OrdinalIgnoreCase);

    /// <summary>エンティティ ID → そのエンティティの名前付きクエリ定義（定義順）</summary>
    private Dictionary<Guid, List<QueryDefinition>> _queriesByEntity = new();

    /// <summary>ビルド全体で射影 DTO 名の重複を検出するための集合（namespace 単位で一意が必要）</summary>
    private readonly HashSet<string> _queryDtoNames = new(StringComparer.Ordinal);

    /// <summary>標準 Repository メンバーと衝突するため名前付きクエリに使えないメソッド名</summary>
    private static readonly HashSet<string> ReservedQueryMethodNames = new(StringComparer.Ordinal)
    {
        "GetByIdAsync",
        "GetAllAsync",
        "InsertAsync",
        "BulkInsertAsync",
        "UpdateAsync",
        "DeleteAsync",
        "SaveAsync",
        "Query",
        "QueryBySqlAsync",
        "ExecuteSqlAsync",
        "ExecuteScalarSqlAsync",
        "QueryProjectionBySqlAsync",
    };

    /// <summary>1 エンティティ分のクエリブロック（テンプレートへ渡す整形済みテキスト群）</summary>
    private sealed record QueryBlocks(
        string InterfaceBlock,
        string SharedImplBlock,
        IReadOnlyDictionary<string, string> ImplBlocksByDialect,
        string DtoBlock,
        string RemoteClientBlock,
        string RemoteServerBlock,
        string RemoteServerRecordsBlock
    );

    /// <summary>クエリメソッドのペイロードパラメータ（HTTP 転送のエンベロープに載る引数。CancellationToken は含まない）</summary>
    private sealed record QueryPayloadParameter(string TypeName, string Name);

    /// <summary>1 クエリ分のメソッド形状（リモート転送メソッド・サーバーハンドラの生成素材）</summary>
    private sealed record QueryMethodShape(
        string MethodName,
        string ParameterList,
        string ReturnTypeName,
        string Summary,
        IReadOnlyList<QueryPayloadParameter> PayloadParameters
    );

    /// <summary>1 クエリ分の生成済みメンバー（出し分け前の素材）</summary>
    private sealed record QueryMethodMembers(
        string InterfaceMember,
        string? SharedImplMember,
        IReadOnlyDictionary<string, string> DialectImplMembers,
        string? DtoClass,
        QueryMethodShape Shape
    );

    /// <summary>ブロックが空のときの既定値</summary>
    private static readonly QueryBlocks EmptyQueryBlocks = new(
        string.Empty,
        string.Empty,
        CSharpRepositoryModel.EmptyQueryImplBlocks,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty
    );

    /// <summary>エンティティの名前付きクエリからテンプレート用ブロックを構築する</summary>
    private QueryBlocks BuildQueryBlocks(
        Entity entity,
        string repositoryName,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (!_queriesByEntity.TryGetValue(entity.Id, out var queries) || queries.Count == 0)
        {
            return EmptyQueryBlocks;
        }

        var interfaceMembers = new List<string>();
        var sharedMembers = new List<string>();
        var dialectMembers = CodeGenerationOptions.SupportedRepositoryDialects.ToDictionary(
            dialect => dialect,
            _ => new List<string>(),
            StringComparer.Ordinal
        );
        var dtoClasses = new List<string>();
        var remoteClientMembers = new List<string>();
        var remoteServerMaps = new List<string>();
        var remoteServerRecords = new List<string>();
        var usedMethodNames = new HashSet<string>(ReservedQueryMethodNames, StringComparer.Ordinal);

        foreach (var query in queries)
        {
            var members = BuildQueryMethod(entity, query, usedMethodNames, diagnostics);

            if (members is null)
            {
                continue;
            }

            interfaceMembers.Add(members.InterfaceMember);

            if (members.SharedImplMember is { } shared)
            {
                // ミニ DSL 系の共有本体は全方言のRepository (QuickER) にも同一テキストで出力する
                foreach (var list in dialectMembers.Values)
                {
                    list.Add(shared);
                }

                sharedMembers.Add(shared);
            }

            foreach (var (dialect, member) in members.DialectImplMembers)
            {
                if (dialectMembers.TryGetValue(dialect, out var list))
                {
                    list.Add(member);
                }
            }

            if (members.DtoClass is { } dto)
            {
                dtoClasses.Add(dto);
            }

            // リモートサービス生成時: クエリの実装方式に依らず、クライアントは同一シグネチャの転送メソッド・
            // サーバーはリクエスト復元→リモート面呼び出しのハンドラを生成する（実装の実体はサーバー側リポジトリ）
            if (options.GenerateRemoteServices)
            {
                remoteClientMembers.Add(BuildRemoteClientMember(members.Shape));
                remoteServerMaps.Add(BuildRemoteServerMap(members.Shape, repositoryName));

                if (BuildRemoteServerRecord(members.Shape, repositoryName) is { } record)
                {
                    remoteServerRecords.Add(record);
                }
            }
        }

        return new QueryBlocks(
            string.Join("\n\n", interfaceMembers),
            string.Join("\n\n", sharedMembers),
            dialectMembers.ToDictionary(
                pair => pair.Key,
                pair => string.Join("\n\n", pair.Value),
                StringComparer.Ordinal
            ),
            string.Join("\n\n", dtoClasses),
            string.Join("\n\n", remoteClientMembers),
            string.Join("\n\n", remoteServerMaps),
            string.Join("\n\n", remoteServerRecords)
        );
    }

    /// <summary>1 クエリ定義から契約・実装・DTO のメンバーテキストを構築する（検証エラー時は null）</summary>
    private QueryMethodMembers? BuildQueryMethod(
        Entity entity,
        QueryDefinition query,
        HashSet<string> usedMethodNames,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var hasError = false;

        // ---- メソッド名（PascalCase ＋ Async。既存メンバー・他クエリとの衝突を弾く） ----
        var baseName = _nameConverter.ToPropertyName(query.Name);

        if (string.IsNullOrEmpty(baseName) || !IsValidIdentifier(baseName))
        {
            diagnostics.Add(Error(string.Format(Strings.CodeGen_Query_InvalidName, query.Name)));
            return null;
        }

        var methodName = baseName.EndsWith("Async", StringComparison.Ordinal)
            ? baseName
            : baseName + "Async";

        if (ReservedQueryMethodNames.Contains(methodName))
        {
            diagnostics.Add(
                Error(
                    string.Format(Strings.CodeGen_Query_ReservedMethodName, query.Name, methodName)
                )
            );
            return null;
        }

        if (!usedMethodNames.Add(methodName))
        {
            diagnostics.Add(
                Error(
                    string.Format(
                        Strings.CodeGen_Query_DuplicateMethodName,
                        query.Name,
                        methodName,
                        entity.TableName
                    )
                )
            );
            return null;
        }

        // ---- 戻り形と実装方式の整合 ----
        if (query.Returns == QueryReturnShape.Scalar)
        {
            if (string.IsNullOrWhiteSpace(query.ScalarType))
            {
                diagnostics.Add(
                    Error(string.Format(Strings.CodeGen_Query_ScalarRequiresType, query.Name))
                );
                hasError = true;
            }

            if (query.Implementation == QueryImplementationKind.Dsl)
            {
                diagnostics.Add(
                    Error(string.Format(Strings.CodeGen_Query_ScalarDslUnsupported, query.Name))
                );
                hasError = true;
            }
        }

        var supportsOrderAndPaging =
            query.Returns is QueryReturnShape.List or QueryReturnShape.Projection;

        if (query.HasPaging && !supportsOrderAndPaging)
        {
            diagnostics.Add(
                Error(string.Format(Strings.CodeGen_Query_PagingRequiresList, query.Name))
            );
            hasError = true;
        }

        if (
            query.OrderBy.Count > 0
            && !supportsOrderAndPaging
            && query.Returns != QueryReturnShape.Single
        )
        {
            diagnostics.Add(
                Error(string.Format(Strings.CodeGen_Query_OrderByRequiresList, query.Name))
            );
            hasError = true;
        }

        // ---- パラメータ（識別子・重複・型トークン解決） ----
        var parameterDecls = new List<string>();
        var argumentNames = new List<string>();
        var payloadParameters = new List<QueryPayloadParameter>();
        var seenParameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cancellationToken",
        };

        if (query.HasPaging)
        {
            seenParameterNames.Add("take");
            seenParameterNames.Add("skip");
        }

        // ---- 列バインディング（条件・並び順・射影・列参照パラメータが参照する列 → 生成コード上の姿） ----
        var columnBindings = BuildQueryColumnBindings(entity);

        // VO 型で型付けされたパラメータ（列参照）の「名前 → VO クラス名」対応（エミッタの直接比較判定用）
        var parameterValueObjects = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var parameter in query.Parameters)
        {
            if (!IsValidIdentifier(parameter.Name))
            {
                diagnostics.Add(
                    Error(
                        string.Format(
                            Strings.CodeGen_Query_InvalidParameterName,
                            query.Name,
                            parameter.Name
                        )
                    )
                );
                hasError = true;
                continue;
            }

            if (!seenParameterNames.Add(parameter.Name))
            {
                diagnostics.Add(
                    Error(
                        string.Format(
                            Strings.CodeGen_Query_DuplicateParameterName,
                            query.Name,
                            parameter.Name
                        )
                    )
                );
                hasError = true;
                continue;
            }

            string typeName;

            if (parameter.SourceColumnId is { } sourceColumnId)
            {
                // 列参照型付け: その列の生成型（VO 有効なら VO クラス・無効ならプリミティブ）を使う
                if (!columnBindings.TryGetValue(sourceColumnId, out var sourceBinding))
                {
                    diagnostics.Add(
                        Error(
                            string.Format(
                                Strings.CodeGen_Query_ParameterColumnNotFound,
                                query.Name,
                                parameter.Name
                            )
                        )
                    );
                    hasError = true;
                    continue;
                }

                typeName = sourceBinding.ValueObjectClassName ?? sourceBinding.UnderlyingTypeName;

                if (sourceBinding.ValueObjectClassName is { } vo)
                {
                    parameterValueObjects[parameter.Name] = vo;
                }
            }
            else if (!TryResolveTokenType(query, parameter.Type, diagnostics, out typeName))
            {
                hasError = true;
                continue;
            }

            var declaredType = parameter.IsList ? $"IReadOnlyList<{typeName}>" : typeName;
            parameterDecls.Add($"{declaredType} {parameter.Name}");
            argumentNames.Add(parameter.Name);
            payloadParameters.Add(new QueryPayloadParameter(declaredType, parameter.Name));
        }

        if (query.HasPaging)
        {
            parameterDecls.Add("int take");
            parameterDecls.Add("int skip = 0");
            payloadParameters.Add(new QueryPayloadParameter("int", "take"));
            payloadParameters.Add(new QueryPayloadParameter("int", "skip"));
        }

        parameterDecls.Add("CancellationToken cancellationToken = default");

        var lambdaNames = argumentNames.Concat(["take", "skip", "cancellationToken"]).ToList();
        var lambdaVar = QueryConditionCSharpEmitter.PickLambdaVariable(lambdaNames);

        // ---- 条件（ミニ DSL のみ） ----
        QueryConditionCSharpEmitter.EmitResult? condition = null;

        if (
            query.Implementation == QueryImplementationKind.Dsl
            && !string.IsNullOrWhiteSpace(query.Condition)
        )
        {
            var parsed = QueryConditionParser.ParseAndValidate(
                query.Condition,
                entity,
                query.Parameters
            );

            if (!parsed.Success)
            {
                foreach (var diagnostic in parsed.Diagnostics)
                {
                    diagnostics.Add(
                        Error(
                            string.Format(
                                Strings.CodeGen_Query_ConditionInvalid,
                                query.Name,
                                diagnostic.Message
                            )
                        )
                    );
                }

                hasError = true;
            }
            else
            {
                condition = QueryConditionCSharpEmitter.Emit(
                    parsed.Root!,
                    columnBindings,
                    lambdaNames,
                    parameterValueObjects
                );
            }
        }

        // ---- 並び順（列 ID 参照） ----
        var orderCalls = new List<string>();

        foreach (var ordering in query.OrderBy)
        {
            if (!columnBindings.TryGetValue(ordering.ColumnId, out var binding))
            {
                diagnostics.Add(
                    Error(string.Format(Strings.CodeGen_Query_OrderByColumnNotFound, query.Name))
                );
                hasError = true;
                continue;
            }

            var method = ordering.Descending ? "OrderByDescending" : "OrderBy";
            orderCalls.Add($".{method}({lambdaVar} => {lambdaVar}.{binding.PropertyName})");
        }

        // ---- 射影 DTO と選択式 ----
        string? dtoClass = null;
        string? projectionSelector = null;
        var resultTypeName = query.ResultTypeName?.Trim() ?? string.Empty;

        if (query.Returns == QueryReturnShape.Projection)
        {
            if (query.Fields.Count == 0 || string.IsNullOrEmpty(resultTypeName))
            {
                diagnostics.Add(
                    Error(string.Format(Strings.CodeGen_Query_ProjectionRequiresFields, query.Name))
                );
                return null;
            }

            if (!IsValidIdentifier(resultTypeName))
            {
                diagnostics.Add(
                    Error(
                        string.Format(
                            Strings.CodeGen_Query_InvalidResultTypeName,
                            query.Name,
                            resultTypeName
                        )
                    )
                );
                return null;
            }

            if (!_queryDtoNames.Add(resultTypeName))
            {
                diagnostics.Add(
                    Error(
                        string.Format(
                            Strings.CodeGen_Query_DuplicateResultTypeName,
                            query.Name,
                            resultTypeName
                        )
                    )
                );
                return null;
            }

            dtoClass = BuildProjectionDto(
                entity,
                query,
                resultTypeName,
                columnBindings,
                diagnostics,
                ref hasError
            );

            if (query.Implementation == QueryImplementationKind.Dsl)
            {
                projectionSelector = BuildProjectionSelector(
                    query,
                    resultTypeName,
                    columnBindings,
                    lambdaVar,
                    diagnostics,
                    ref hasError
                );
            }
        }

        if (hasError)
        {
            return null;
        }

        // ---- テキスト組み立て ----
        var entityClassName = _nameConverter.ToEntityClassName(entity.TableName);
        var returnTypeName = query.Returns switch
        {
            QueryReturnShape.List => $"Task<IReadOnlyList<{entityClassName}>>",
            QueryReturnShape.Single => $"Task<{entityClassName}?>",
            QueryReturnShape.Count => "Task<int>",
            QueryReturnShape.Scalar => BuildScalarReturnType(query, diagnostics, ref hasError),
            QueryReturnShape.Projection => $"Task<IReadOnlyList<{resultTypeName}>>",
            _ => throw new InvalidOperationException($"未知の戻り形です: {query.Returns}"),
        };

        if (hasError)
        {
            return null;
        }

        var parameterList = string.Join(", ", parameterDecls);
        var summary = string.IsNullOrWhiteSpace(query.Description)
            ? $"名前付きクエリ {methodName}"
            : EscapeForXmlDocSummary(query.Description);

        var interfaceMember = BuildInterfaceMember(
            query,
            summary,
            returnTypeName,
            methodName,
            parameterList
        );

        string? sharedImplMember = null;
        var dialectImplMembers = new Dictionary<string, string>(StringComparer.Ordinal);

        if (query.Implementation == QueryImplementationKind.Dsl)
        {
            sharedImplMember = BuildDslImplMember(
                query,
                summary,
                returnTypeName,
                methodName,
                parameterList,
                condition,
                orderCalls,
                projectionSelector
            );
        }
        else if (query.Implementation == QueryImplementationKind.Sql)
        {
            foreach (var (dialect, sql) in query.Sql)
            {
                if (
                    !CodeGenerationOptions.SupportedRepositoryDialects.Contains(
                        dialect,
                        StringComparer.Ordinal
                    )
                )
                {
                    diagnostics.Add(
                        Warning(
                            string.Format(
                                Strings.CodeGen_Query_UnknownSqlDialect,
                                query.Name,
                                dialect
                            )
                        )
                    );
                    continue;
                }

                dialectImplMembers[dialect] = BuildSqlImplMember(
                    query,
                    summary,
                    returnTypeName,
                    methodName,
                    parameterList,
                    entityClassName,
                    resultTypeName,
                    sql,
                    argumentNames
                );
            }
        }

        return new QueryMethodMembers(
            interfaceMember,
            sharedImplMember,
            dialectImplMembers,
            dtoClass,
            new QueryMethodShape(
                methodName,
                parameterList,
                returnTypeName,
                summary,
                payloadParameters
            )
        );
    }

    /// <summary>スカラー戻り形の戻り値型（Task&lt;T?&gt;）を構築する</summary>
    private string BuildScalarReturnType(
        QueryDefinition query,
        ICollection<GenerationDiagnostic> diagnostics,
        ref bool hasError
    )
    {
        if (
            !TryResolveTokenType(query, query.ScalarType ?? string.Empty, diagnostics, out var type)
        )
        {
            hasError = true;
            return "Task<object?>";
        }

        return $"Task<{type}?>";
    }

    /// <summary>型トークンを C# 型名へ解決する（解決不能は診断エラー）</summary>
    private bool TryResolveTokenType(
        QueryDefinition query,
        string token,
        ICollection<GenerationDiagnostic> diagnostics,
        out string typeName
    )
    {
        if (_queryTokenTypes.TryGetValue(token, out var info))
        {
            typeName = info.TypeName;
            return true;
        }

        diagnostics.Add(
            Error(string.Format(Strings.CodeGen_Query_UnresolvedTypeToken, query.Name, token))
        );
        typeName = string.Empty;
        return false;
    }

    /// <summary>エンティティ全列の「生成コード上の姿」（プロパティ名・素の型・VO・NULL 許容）を引けるようにする</summary>
    private Dictionary<Guid, QueryColumnBinding> BuildQueryColumnBindings(Entity entity)
    {
        var bindings = new Dictionary<Guid, QueryColumnBinding>();

        foreach (var column in entity.Columns)
        {
            if (!_columnTypes.TryGetValue(column.Id, out var typeInfo))
            {
                continue;
            }

            bindings[column.Id] = new QueryColumnBinding(
                _nameConverter.ToPropertyName(column.Name),
                typeInfo.TypeName,
                ResolveValueObject(column)?.ClassName,
                column.IsNullable
            );
        }

        return bindings;
    }

    /// <summary>契約（インターフェイス）メンバーのテキストを構築する</summary>
    private static string BuildInterfaceMember(
        QueryDefinition query,
        string summary,
        string returnTypeName,
        string methodName,
        string parameterList
    )
    {
        var builder = new StringBuilder();
        builder.Append("    /// <summary>").Append(summary).Append("</summary>\n");

        if (query.Implementation != QueryImplementationKind.Dsl)
        {
            // manual／自由 SQL は「実装が生成されない実装先がある」ことを契約側に明示する
            builder.Append(
                "    /// <remarks>実装が生成されない実装先（EF Core・SQL 未定義の方言・インメモリ）では partial クラスでの実装が必要。</remarks>\n"
            );
        }

        builder
            .Append("    ")
            .Append(returnTypeName)
            .Append(' ')
            .Append(methodName)
            .Append('(')
            .Append(parameterList)
            .Append(");");
        return builder.ToString();
    }

    /// <summary>ミニ DSL の共有実装メンバー（Query() パイプライン経由・全実装先共通）を構築する</summary>
    private static string BuildDslImplMember(
        QueryDefinition query,
        string summary,
        string returnTypeName,
        string methodName,
        string parameterList,
        QueryConditionCSharpEmitter.EmitResult? condition,
        IReadOnlyList<string> orderCalls,
        string? projectionSelector
    )
    {
        var chain = new StringBuilder("Query()");

        if (condition is not null)
        {
            chain.Append(".Where(").Append(condition.Lambda).Append(')');
        }

        foreach (var orderCall in orderCalls)
        {
            chain.Append(orderCall);
        }

        if (query.HasPaging)
        {
            chain.Append(".Skip(skip).Take(take)");
        }

        chain.Append(
            query.Returns switch
            {
                QueryReturnShape.List => ".ToListAsync(cancellationToken)",
                QueryReturnShape.Single => ".FirstOrDefaultAsync(cancellationToken)",
                QueryReturnShape.Count => ".CountAsync(cancellationToken)",
                QueryReturnShape.Projection =>
                    $".ToProjectionListAsync({projectionSelector}, cancellationToken)",
                _ => throw new InvalidOperationException($"未知の戻り形です: {query.Returns}"),
            }
        );

        var builder = new StringBuilder();
        builder.Append("    /// <summary>").Append(summary).Append("</summary>\n");

        if (condition is { PreludeLines.Count: > 0 })
        {
            builder
                .Append("    public ")
                .Append(returnTypeName)
                .Append(' ')
                .Append(methodName)
                .Append('(')
                .Append(parameterList)
                .Append(")\n    {\n");

            foreach (var line in condition.PreludeLines)
            {
                builder.Append("        ").Append(line).Append('\n');
            }

            builder.Append("        return ").Append(chain).Append(";\n    }");
        }
        else
        {
            builder
                .Append("    public ")
                .Append(returnTypeName)
                .Append(' ')
                .Append(methodName)
                .Append('(')
                .Append(parameterList)
                .Append(") =>\n        ")
                .Append(chain)
                .Append(';');
        }

        return builder.ToString();
    }

    /// <summary>自由 SQL の方言別実装メンバー（生 SQL API へ委譲）を構築する</summary>
    private static string BuildSqlImplMember(
        QueryDefinition query,
        string summary,
        string returnTypeName,
        string methodName,
        string parameterList,
        string entityClassName,
        string resultTypeName,
        string sql,
        IReadOnlyList<string> argumentNames
    )
    {
        // SQL は逐語的文字列リテラルで埋め込む（改行を保持し、" は "" へエスケープ）
        var sqlLiteral = "@\"" + sql.Replace("\"", "\"\"") + "\"";

        // 匿名オブジェクトの束縛引数（ページング有効時は take / skip も SQL から @take / @skip で参照できる）
        var boundNames = query.HasPaging
            ? argumentNames.Concat(["take", "skip"]).ToList()
            : argumentNames.ToList();
        var args = boundNames.Count == 0 ? "null" : $"new {{ {string.Join(", ", boundNames)} }}";

        var builder = new StringBuilder();
        builder.Append("    /// <summary>").Append(summary).Append("</summary>\n");

        switch (query.Returns)
        {
            case QueryReturnShape.List:
                builder
                    .Append("    public ")
                    .Append(returnTypeName)
                    .Append(' ')
                    .Append(methodName)
                    .Append('(')
                    .Append(parameterList)
                    .Append(") =>\n        QueryBySqlAsync(\n            ")
                    .Append(sqlLiteral)
                    .Append(",\n            ")
                    .Append(args)
                    .Append(",\n            cancellationToken\n        );");
                break;

            case QueryReturnShape.Single:
                builder
                    .Append("    public async ")
                    .Append(returnTypeName)
                    .Append(' ')
                    .Append(methodName)
                    .Append('(')
                    .Append(parameterList)
                    .Append(")\n    {\n        var items = await QueryBySqlAsync(\n            ")
                    .Append(sqlLiteral)
                    .Append(",\n            ")
                    .Append(args)
                    .Append(
                        ",\n            cancellationToken\n        );\n        return items.Count > 0 ? items[0] : null;\n    }"
                    );
                break;

            case QueryReturnShape.Count:
                builder
                    .Append("    public async ")
                    .Append(returnTypeName)
                    .Append(' ')
                    .Append(methodName)
                    .Append('(')
                    .Append(parameterList)
                    .Append(") =>\n        await ExecuteScalarSqlAsync<int?>(\n            ")
                    .Append(sqlLiteral)
                    .Append(",\n            ")
                    .Append(args)
                    .Append(",\n            cancellationToken\n        ) ?? 0;");
                break;

            case QueryReturnShape.Scalar:
                builder
                    .Append("    public ")
                    .Append(returnTypeName)
                    .Append(' ')
                    .Append(methodName)
                    .Append('(')
                    .Append(parameterList)
                    // 非制約ジェネリックの TResult? は値型に効かないため、Nullable 型を明示して Task<T?> に合わせる
                    .Append(") =>\n        ExecuteScalarSqlAsync<")
                    .Append(ScalarElementType(returnTypeName))
                    .Append("?>(\n            ")
                    .Append(sqlLiteral)
                    .Append(",\n            ")
                    .Append(args)
                    .Append(",\n            cancellationToken\n        );");
                break;

            case QueryReturnShape.Projection:
                builder
                    .Append("    public ")
                    .Append(returnTypeName)
                    .Append(' ')
                    .Append(methodName)
                    .Append('(')
                    .Append(parameterList)
                    .Append(") =>\n        QueryProjectionBySqlAsync<")
                    .Append(resultTypeName)
                    .Append(">(\n            ")
                    .Append(sqlLiteral)
                    .Append(",\n            ")
                    .Append(args)
                    .Append(",\n            cancellationToken\n        );");
                break;

            default:
                throw new InvalidOperationException($"未知の戻り形です: {query.Returns}");
        }

        return builder.ToString();
    }

    /// <summary>Task&lt;T?&gt; 形式の戻り値型からスカラー要素型 T を取り出す</summary>
    private static string ScalarElementType(string returnTypeName) =>
        returnTypeName["Task<".Length..^">".Length].TrimEnd('?');

    /// <summary>射影 DTO クラスのテキストを構築する（寛容マッパー互換: 引数なしコンストラクタ＋settable プロパティ）</summary>
    private string BuildProjectionDto(
        Entity entity,
        QueryDefinition query,
        string resultTypeName,
        IReadOnlyDictionary<Guid, QueryColumnBinding> columnBindings,
        ICollection<GenerationDiagnostic> diagnostics,
        ref bool hasError
    )
    {
        var properties = new List<string>();
        var seenFieldNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in query.Fields)
        {
            if (!IsValidIdentifier(field.Name) || !seenFieldNames.Add(field.Name))
            {
                diagnostics.Add(
                    Error(
                        string.Format(
                            Strings.CodeGen_Query_InvalidFieldName,
                            query.Name,
                            field.Name
                        )
                    )
                );
                hasError = true;
                continue;
            }

            string baseType;

            if (field.SourceColumnId is { } columnId)
            {
                if (!columnBindings.TryGetValue(columnId, out var binding))
                {
                    diagnostics.Add(
                        Error(
                            string.Format(
                                Strings.CodeGen_Query_FieldColumnNotFound,
                                query.Name,
                                field.Name
                            )
                        )
                    );
                    hasError = true;
                    continue;
                }

                // 列由来フィールドは列の生成型（VO 含む）を使う
                baseType = binding.ValueObjectClassName ?? binding.UnderlyingTypeName;
            }
            else
            {
                if (!TryResolveTokenType(query, field.Type, diagnostics, out baseType))
                {
                    hasError = true;
                    continue;
                }
            }

            // DTO プロパティは常に NULL 許容にする（寛容マッパーの列欠落・集計 NULL を安全に受ける）
            properties.Add(
                $"    /// <summary>{EscapeForXmlDocSummary(field.Name)}</summary>\n"
                    + $"    public {baseType}? {field.Name} {{ get; set; }}"
            );
        }

        var builder = new StringBuilder();
        builder
            .Append("/// <summary>名前付きクエリ ")
            .Append(EscapeForXmlDocSummary(query.Name))
            .Append(" の射影 DTO（")
            .Append(EscapeForXmlDocSummary(entity.TableName))
            .Append("）</summary>\n")
            .Append("public sealed partial class ")
            .Append(resultTypeName)
            .Append("\n{\n")
            .Append(string.Join("\n\n", properties))
            .Append("\n}");
        return builder.ToString();
    }

    /// <summary>ミニ DSL 射影の選択式（{v} =&gt; new Dto { F = {v}.Prop, ... }）を構築する</summary>
    private static string? BuildProjectionSelector(
        QueryDefinition query,
        string resultTypeName,
        IReadOnlyDictionary<Guid, QueryColumnBinding> columnBindings,
        string lambdaVar,
        ICollection<GenerationDiagnostic> diagnostics,
        ref bool hasError
    )
    {
        var assignments = new List<string>();

        foreach (var field in query.Fields)
        {
            if (
                field.SourceColumnId is not { } columnId
                || !columnBindings.TryGetValue(columnId, out var binding)
            )
            {
                diagnostics.Add(
                    Error(
                        string.Format(
                            Strings.CodeGen_Query_ProjectionDslRequiresColumns,
                            query.Name
                        )
                    )
                );
                hasError = true;
                return null;
            }

            assignments.Add($"{field.Name} = {lambdaVar}.{binding.PropertyName}");
        }

        return $"{lambdaVar} => new {resultTypeName} {{ {string.Join(", ", assignments)} }}";
    }

    /// <summary>C# 識別子として妥当か（先頭は文字か _、以降は文字・数字・_）</summary>
    private static bool IsValidIdentifier(string name) =>
        !string.IsNullOrEmpty(name)
        && (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>エラーレベルの診断情報を生成する</summary>
    private static GenerationDiagnostic Error(string message) =>
        new() { Severity = GenerationDiagnosticSeverity.Error, Message = message };
}
