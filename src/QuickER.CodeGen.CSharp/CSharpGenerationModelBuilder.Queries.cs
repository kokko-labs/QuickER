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
///     QuickER 版 Repository（全方言）・EF Core・インメモリのすべてへ出力</item>
///   <item>自由 SQL（<see cref="QueryImplementationKind.Sql"/>）→ SQL 辞書にある方言の
///     QuickER 版 Repository のみへ出力（EF Core・インメモリ・辞書にない方言は manual 扱い）</item>
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
        "CheckUniquenessAsync",
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

    /// <summary>
    /// 図の名前付きクエリ定義の Guid 参照整合性（エンティティ・列参照のダングリング）を検証し、
    /// 有効な定義だけをエンティティ別（定義順）に分類して返す
    /// </summary>
    /// <remarks>
    /// エンティティ・列は Guid 参照のためリネームには追従するが、参照先が図から削除されると
    /// 定義が残骸（ダングリング）として残る。生成前のセーフティネットとしてここで一括検出し、
    /// 該当クエリはローカライズ済みの警告診断を出してスキップする（他のクエリと生成全体は継続する）。
    /// メソッド構築時の列不在エラーは多重防御としてそのまま残す。
    /// </remarks>
    private static Dictionary<Guid, List<QueryDefinition>> CollectValidQueries(
        ErDiagram diagram,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var entitiesById = diagram.Entities.ToDictionary(entity => entity.Id);
        var result = new Dictionary<Guid, List<QueryDefinition>>();

        foreach (var query in diagram.Queries)
        {
            // 参照先エンティティが存在しないクエリ定義（削除済みエンティティの残骸等）
            if (!entitiesById.TryGetValue(query.EntityId, out var entity))
            {
                diagnostics.Add(
                    GenerationDiagnostic.Warning(
                        string.Format(Strings.CodeGen_Query_UnknownEntity, query.Name)
                    )
                );
                continue;
            }

            var columnIds = entity.Columns.Select(column => column.Id).ToHashSet();
            var valid = true;

            // 列参照型付けパラメータの参照先列（クエリが属するエンティティの列に限る）
            foreach (var parameter in query.Parameters)
            {
                if (
                    parameter.SourceColumnId is { } parameterColumnId
                    && !columnIds.Contains(parameterColumnId)
                )
                {
                    diagnostics.Add(
                        GenerationDiagnostic.Warning(
                            string.Format(
                                Strings.CodeGen_Query_DanglingParameterColumn,
                                query.Name,
                                parameter.Name
                            )
                        )
                    );
                    valid = false;
                }
            }

            // 射影フィールドの参照元列
            foreach (var field in query.Fields)
            {
                if (field.SourceColumnId is { } fieldColumnId && !columnIds.Contains(fieldColumnId))
                {
                    diagnostics.Add(
                        GenerationDiagnostic.Warning(
                            string.Format(
                                Strings.CodeGen_Query_DanglingFieldColumn,
                                query.Name,
                                field.Name
                            )
                        )
                    );
                    valid = false;
                }
            }

            // 並び順の参照列
            if (query.OrderBy.Any(ordering => !columnIds.Contains(ordering.ColumnId)))
            {
                diagnostics.Add(
                    GenerationDiagnostic.Warning(
                        string.Format(Strings.CodeGen_Query_DanglingOrderByColumn, query.Name)
                    )
                );
                valid = false;
            }

            if (!valid)
            {
                continue;
            }

            if (!result.TryGetValue(query.EntityId, out var list))
            {
                result[query.EntityId] = list = new List<QueryDefinition>();
            }

            list.Add(query);
        }

        return result;
    }

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
                // ミニ DSL 系の共有本体は全方言のQuickER 版 Repository にも同一テキストで出力する
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

    /// <summary>
    /// 1 クエリ分の検証済み生成計画（検証フェーズの成果物）。
    /// テキスト組み立て（エミットフェーズ）に必要な素材だけを運ぶ
    /// </summary>
    private sealed record QueryMethodPlan(
        string MethodName,
        string EntityClassName,
        string ResultTypeName,
        string ReturnTypeName,
        string ParameterList,
        string Summary,
        IReadOnlyList<string> ArgumentNames,
        IReadOnlyList<QueryPayloadParameter> PayloadParameters,
        QueryConditionCSharpEmitter.EmitResult? Condition,
        IReadOnlyList<string> OrderCalls,
        string? DtoClass,
        string? ProjectionSelector
    );

    /// <summary>1 クエリ定義から契約・実装・DTO のメンバーテキストを構築する（検証エラー時は null）</summary>
    /// <remarks>
    /// 「検証フェーズ（診断収集 → <see cref="QueryMethodPlan"/>）」と「エミットフェーズ（テキスト組み立て）」の
    /// 2 相に分かれており、検証エラーは計画 null（＝このクエリをスキップ）として表す。
    /// </remarks>
    private QueryMethodMembers? BuildQueryMethod(
        Entity entity,
        QueryDefinition query,
        HashSet<string> usedMethodNames,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var plan = PlanQueryMethod(entity, query, usedMethodNames, diagnostics);

        return plan is null ? null : EmitQueryMethod(query, plan, diagnostics);
    }

    /// <summary>検証フェーズ: クエリ定義を検証し、エミットに必要な素材（生成計画）を組み立てる（エラー時は null）</summary>
    private QueryMethodPlan? PlanQueryMethod(
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
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(Strings.CodeGen_Query_InvalidName, query.Name)
                )
            );
            return null;
        }

        var methodName = baseName.EndsWith("Async", StringComparison.Ordinal)
            ? baseName
            : baseName + "Async";

        if (ReservedQueryMethodNames.Contains(methodName))
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(Strings.CodeGen_Query_ReservedMethodName, query.Name, methodName)
                )
            );
            return null;
        }

        if (!usedMethodNames.Add(methodName))
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
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
                    GenerationDiagnostic.Error(
                        string.Format(Strings.CodeGen_Query_ScalarRequiresType, query.Name)
                    )
                );
                hasError = true;
            }

            if (query.Implementation == QueryImplementationKind.Dsl)
            {
                diagnostics.Add(
                    GenerationDiagnostic.Error(
                        string.Format(Strings.CodeGen_Query_ScalarDslUnsupported, query.Name)
                    )
                );
                hasError = true;
            }
        }

        var supportsOrderAndPaging =
            query.Returns is QueryReturnShape.List or QueryReturnShape.Projection;

        if (query.HasPaging && !supportsOrderAndPaging)
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(Strings.CodeGen_Query_PagingRequiresList, query.Name)
                )
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
                GenerationDiagnostic.Error(
                    string.Format(Strings.CodeGen_Query_OrderByRequiresList, query.Name)
                )
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
                    GenerationDiagnostic.Error(
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
                    GenerationDiagnostic.Error(
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
                        GenerationDiagnostic.Error(
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

        // ---- 自由 SQL の静的検証（レベル 1・方言非依存。未宣言＝スキップ・未使用/複文＝警告のみ） ----
        if (!ValidateRawSql(query, argumentNames, diagnostics))
        {
            hasError = true;
        }

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
                        GenerationDiagnostic.Error(
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
                    GenerationDiagnostic.Error(
                        string.Format(Strings.CodeGen_Query_OrderByColumnNotFound, query.Name)
                    )
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
                    GenerationDiagnostic.Error(
                        string.Format(Strings.CodeGen_Query_ProjectionRequiresFields, query.Name)
                    )
                );
                return null;
            }

            if (!IsValidIdentifier(resultTypeName))
            {
                diagnostics.Add(
                    GenerationDiagnostic.Error(
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
                    GenerationDiagnostic.Error(
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
                diagnostics
            );

            if (dtoClass is null)
            {
                hasError = true;
            }

            if (query.Implementation == QueryImplementationKind.Dsl)
            {
                projectionSelector = BuildProjectionSelector(
                    query,
                    resultTypeName,
                    columnBindings,
                    lambdaVar,
                    diagnostics
                );

                if (projectionSelector is null)
                {
                    hasError = true;
                }
            }
        }

        if (hasError)
        {
            return null;
        }

        // ---- 戻り値型（スカラーのみ型トークン解決を伴うため表に載せず個別に解決する） ----
        var entityClassName = _nameConverter.ToEntityClassName(entity.TableName);
        var returnTypeName = query.Returns switch
        {
            QueryReturnShape.Scalar => BuildScalarReturnType(query, diagnostics),
            var shape => string.Format(
                GetReturnShapeInfo(shape).ReturnTypeFormat,
                shape == QueryReturnShape.Projection ? resultTypeName : entityClassName
            ),
        };

        if (returnTypeName is null)
        {
            return null;
        }

        var parameterList = string.Join(", ", parameterDecls);
        var summary = string.IsNullOrWhiteSpace(query.Description)
            ? $"Named query {methodName}."
            : EscapeForXmlDocSummary(query.Description);

        return new QueryMethodPlan(
            methodName,
            entityClassName,
            resultTypeName,
            returnTypeName,
            parameterList,
            summary,
            argumentNames,
            payloadParameters,
            condition,
            orderCalls,
            dtoClass,
            projectionSelector
        );
    }

    /// <summary>
    /// 自由 SQL（<see cref="QueryImplementationKind.Sql"/>）の静的検証（レベル 1・方言非依存）を行い、
    /// 方言別 SQL 辞書の各エントリを走査して未宣言パラメータ・未使用パラメータ・複文を診断する
    /// </summary>
    /// <remarks>
    /// 未宣言パラメータは実行時に必ず失敗するため「警告＋該当クエリのみ生成スキップ」（Guid 参照検証と同じ方針）、
    /// 未使用パラメータ・複文は「警告のみ・生成継続」。診断は方言名を含む（GUI / CLI 双方に効く）。
    /// </remarks>
    /// <param name="argumentNames">メソッド引数になるパラメータ名（ページング時は take / skip も SQL から参照できる）</param>
    /// <returns>未宣言パラメータが 1 件でもあれば false（このクエリはスキップする）</returns>
    private static bool ValidateRawSql(
        QueryDefinition query,
        IReadOnlyList<string> argumentNames,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (query.Implementation != QueryImplementationKind.Sql || query.Sql.Count == 0)
        {
            return true;
        }

        // 生 SQL が参照できるパラメータ＝メソッド引数（＋ページング時の take / skip）
        var declared = query.HasPaging
            ? argumentNames.Concat(["take", "skip"]).ToList()
            : argumentNames.ToList();

        var ok = true;

        foreach (var (dialect, sql) in query.Sql)
        {
            foreach (var finding in RawSqlAnalyzer.Analyze(sql, declared))
            {
                diagnostics.Add(
                    GenerationDiagnostic.Warning(
                        string.Format(
                            Strings.CodeGen_Query_RawSqlIssue,
                            query.Name,
                            dialect,
                            RawSqlAnalyzer.Describe(finding)
                        )
                    )
                );

                if (finding.Kind == RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter)
                {
                    ok = false;
                }
            }
        }

        return ok;
    }

    /// <summary>エミットフェーズ: 検証済みの生成計画から契約・実装・DTO のメンバーテキストを組み立てる</summary>
    /// <remarks>SQL 辞書の未知方言はここで警告してスキップする（検証エラーではなく縮退＝該当方言のみ manual 扱い）。</remarks>
    private static QueryMethodMembers EmitQueryMethod(
        QueryDefinition query,
        QueryMethodPlan plan,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var interfaceMember = BuildInterfaceMember(query, plan);

        string? sharedImplMember = null;
        var dialectImplMembers = new Dictionary<string, string>(StringComparer.Ordinal);

        if (query.Implementation == QueryImplementationKind.Dsl)
        {
            sharedImplMember = BuildDslImplMember(query, plan);
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
                        GenerationDiagnostic.Warning(
                            string.Format(
                                Strings.CodeGen_Query_UnknownSqlDialect,
                                query.Name,
                                dialect
                            )
                        )
                    );
                    continue;
                }

                dialectImplMembers[dialect] = BuildSqlImplMember(query, plan, sql);
            }
        }

        return new QueryMethodMembers(
            interfaceMember,
            sharedImplMember,
            dialectImplMembers,
            plan.DtoClass,
            new QueryMethodShape(
                plan.MethodName,
                plan.ParameterList,
                plan.ReturnTypeName,
                plan.Summary,
                plan.PayloadParameters
            )
        );
    }

    /// <summary>スカラー戻り形の戻り値型（Task&lt;T?&gt;）を構築する（トークン解決不能は null）</summary>
    private string? BuildScalarReturnType(
        QueryDefinition query,
        ICollection<GenerationDiagnostic> diagnostics
    ) =>
        TryResolveTokenType(query, query.ScalarType, diagnostics, out var type)
            ? $"Task<{type}?>"
            : null;

    /// <summary>型トークンを C# 型名へ解決する（解決不能・トークン欠落は診断エラー）</summary>
    private bool TryResolveTokenType(
        QueryDefinition query,
        string? token,
        ICollection<GenerationDiagnostic> diagnostics,
        out string typeName
    )
    {
        if (TryResolveTokenInfo(query, token, diagnostics, out var info))
        {
            typeName = info.TypeName;
            return true;
        }

        typeName = string.Empty;
        return false;
    }

    /// <summary>型トークンを解決済み C# 型情報へ解決する（解決不能・トークン欠落は診断エラー）</summary>
    /// <remarks>列参照でない（トークン型付けの）パラメータ・フィールドはトークンが必須で、null / 空白は解決不能として扱う。</remarks>
    private bool TryResolveTokenInfo(
        QueryDefinition query,
        string? token,
        ICollection<GenerationDiagnostic> diagnostics,
        out CSharpTypeInfo typeInfo
    )
    {
        if (!string.IsNullOrWhiteSpace(token) && _queryTokenTypes.TryGetValue(token, out var info))
        {
            typeInfo = info;
            return true;
        }

        diagnostics.Add(
            GenerationDiagnostic.Error(
                string.Format(
                    Strings.CodeGen_Query_UnresolvedTypeToken,
                    query.Name,
                    token ?? string.Empty
                )
            )
        );
        typeInfo = null!;
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
                column.IsNullable,
                typeInfo.IsReferenceType
            );
        }

        return bindings;
    }

    /// <summary>
    /// 戻り形ごとの表駆動部分（スカラー以外）。戻り値型は {0}＝要素型（エンティティまたは射影 DTO）、
    /// DSL 終端呼び出しは {0}＝射影セレクタで埋める。スカラーは型トークン解決（診断あり）を伴うため
    /// 表に載せない（<see cref="BuildScalarReturnType"/> が担当し、DSL とは組み合わせ不可）
    /// </summary>
    private static readonly IReadOnlyDictionary<
        QueryReturnShape,
        (string ReturnTypeFormat, string DslTerminalFormat)
    > ReturnShapeTable = new Dictionary<
        QueryReturnShape,
        (string ReturnTypeFormat, string DslTerminalFormat)
    >
    {
        [QueryReturnShape.List] = ("Task<IReadOnlyList<{0}>>", ".ToListAsync(cancellationToken)"),
        [QueryReturnShape.Single] = ("Task<{0}?>", ".FirstOrDefaultAsync(cancellationToken)"),
        [QueryReturnShape.Count] = ("Task<int>", ".CountAsync(cancellationToken)"),
        [QueryReturnShape.Projection] = (
            "Task<IReadOnlyList<{0}>>",
            ".ToProjectionListAsync({0}, cancellationToken)"
        ),
    };

    /// <summary>戻り形の表駆動部分を引く（表にない戻り形は未知として例外）</summary>
    private static (string ReturnTypeFormat, string DslTerminalFormat) GetReturnShapeInfo(
        QueryReturnShape returns
    ) =>
        ReturnShapeTable.TryGetValue(returns, out var info)
            ? info
            : throw new InvalidOperationException($"未知の戻り形です: {returns}");

    /// <summary>XML doc の summary 行（インデント込み・改行付き）を追記する</summary>
    private static StringBuilder AppendDocSummary(StringBuilder builder, string summary) =>
        builder.Append("    /// <summary>").Append(summary).Append("</summary>\n");

    /// <summary>メソッドヘッダ（インデント＋修飾子＋戻り値型＋メソッド名＋引数リスト＋閉じ括弧）を追記する</summary>
    /// <param name="modifiers">アクセス修飾子等（例: <c>"public "</c> / <c>"public async "</c>。契約宣言は空文字列）</param>
    private static StringBuilder AppendMethodHeader(
        StringBuilder builder,
        string modifiers,
        string returnTypeName,
        string methodName,
        string parameterList
    ) =>
        builder
            .Append("    ")
            .Append(modifiers)
            .Append(returnTypeName)
            .Append(' ')
            .Append(methodName)
            .Append('(')
            .Append(parameterList)
            .Append(')');

    /// <summary>生成計画のシグネチャでメソッドヘッダを追記する（<see cref="AppendMethodHeader(StringBuilder, string, string, string, string)"/> の糖衣）</summary>
    private static StringBuilder AppendMethodHeader(
        StringBuilder builder,
        string modifiers,
        QueryMethodPlan plan
    ) =>
        AppendMethodHeader(
            builder,
            modifiers,
            plan.ReturnTypeName,
            plan.MethodName,
            plan.ParameterList
        );

    /// <summary>契約（インターフェイス）メンバーのテキストを構築する</summary>
    private static string BuildInterfaceMember(QueryDefinition query, QueryMethodPlan plan)
    {
        var builder = AppendDocSummary(new StringBuilder(), plan.Summary);

        if (query.Implementation != QueryImplementationKind.Dsl)
        {
            // manual／自由 SQL は「実装が生成されない実装先がある」ことを契約側に明示する
            builder.Append(
                "    /// <remarks>Implementation targets that do not get a generated body (EF Core, dialects without a SQL definition, and in-memory) require an implementation in a partial class.</remarks>\n"
            );
        }

        return AppendMethodHeader(builder, string.Empty, plan).Append(';').ToString();
    }

    /// <summary>ミニ DSL の共有実装メンバー（Query() パイプライン経由・全実装先共通）を構築する</summary>
    private static string BuildDslImplMember(QueryDefinition query, QueryMethodPlan plan)
    {
        var chain = new StringBuilder("Query()");

        if (plan.Condition is not null)
        {
            chain.Append(".Where(").Append(plan.Condition.Lambda).Append(')');
        }

        foreach (var orderCall in plan.OrderCalls)
        {
            chain.Append(orderCall);
        }

        if (query.HasPaging)
        {
            chain.Append(".Skip(skip).Take(take)");
        }

        chain.Append(
            string.Format(
                GetReturnShapeInfo(query.Returns).DslTerminalFormat,
                plan.ProjectionSelector
            )
        );

        var builder = AppendDocSummary(new StringBuilder(), plan.Summary);

        if (plan.Condition is { PreludeLines.Count: > 0 })
        {
            AppendMethodHeader(builder, "public ", plan).Append("\n    {\n");

            foreach (var line in plan.Condition.PreludeLines)
            {
                builder.Append("        ").Append(line).Append('\n');
            }

            builder.Append("        return ").Append(chain).Append(";\n    }");
        }
        else
        {
            AppendMethodHeader(builder, "public ", plan)
                .Append(" =>\n        ")
                .Append(chain)
                .Append(';');
        }

        return builder.ToString();
    }

    /// <summary>自由 SQL の方言別実装メンバー（生 SQL API へ委譲）を構築する</summary>
    private static string BuildSqlImplMember(
        QueryDefinition query,
        QueryMethodPlan plan,
        string sql
    )
    {
        // SQL は逐語的文字列リテラルで埋め込む（改行を保持し、" は "" へエスケープ）
        var sqlLiteral = "@\"" + sql.Replace("\"", "\"\"") + "\"";

        // 匿名オブジェクトの束縛引数（ページング有効時は take / skip も SQL から @take / @skip で参照できる）
        var boundNames = query.HasPaging
            ? plan.ArgumentNames.Concat(["take", "skip"]).ToList()
            : plan.ArgumentNames.ToList();
        var args = boundNames.Count == 0 ? "null" : $"new {{ {string.Join(", ", boundNames)} }}";

        var builder = AppendDocSummary(new StringBuilder(), plan.Summary);

        switch (query.Returns)
        {
            case QueryReturnShape.List:
                AppendMethodHeader(builder, "public ", plan)
                    .Append(" =>\n        QueryBySqlAsync(\n            ")
                    .Append(sqlLiteral)
                    .Append(",\n            ")
                    .Append(args)
                    .Append(",\n            cancellationToken\n        );");
                break;

            case QueryReturnShape.Single:
                AppendMethodHeader(builder, "public async ", plan)
                    .Append("\n    {\n        var items = await QueryBySqlAsync(\n            ")
                    .Append(sqlLiteral)
                    .Append(",\n            ")
                    .Append(args)
                    .Append(
                        ",\n            cancellationToken\n        ).ConfigureAwait(false);\n        return items.Count > 0 ? items[0] : null;\n    }"
                    );
                break;

            case QueryReturnShape.Count:
                AppendMethodHeader(builder, "public async ", plan)
                    .Append(" =>\n        await ExecuteScalarSqlAsync<int?>(\n            ")
                    .Append(sqlLiteral)
                    .Append(",\n            ")
                    .Append(args)
                    .Append(
                        ",\n            cancellationToken\n        ).ConfigureAwait(false) ?? 0;"
                    );
                break;

            case QueryReturnShape.Scalar:
                // 非制約ジェネリックの TResult? は値型に効かないため、Nullable 型を明示して Task<T?> に合わせる
                AppendMethodHeader(builder, "public ", plan)
                    .Append(" =>\n        ExecuteScalarSqlAsync<")
                    .Append(ScalarElementType(plan.ReturnTypeName))
                    .Append("?>(\n            ")
                    .Append(sqlLiteral)
                    .Append(",\n            ")
                    .Append(args)
                    .Append(",\n            cancellationToken\n        );");
                break;

            case QueryReturnShape.Projection:
                AppendMethodHeader(builder, "public ", plan)
                    .Append(" =>\n        QueryProjectionBySqlAsync<")
                    .Append(plan.ResultTypeName)
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

    /// <summary>Task&lt;T?&gt; 形式の戻り値型からスカラー要素型 T（末尾の NULL 許容 ? を除いた素の型）を取り出す</summary>
    private static string ScalarElementType(string returnTypeName) =>
        StripTaskType(returnTypeName).TrimEnd('?');

    /// <summary>
    /// 射影 DTO クラスのテキストを構築する（寛容マッパー互換: 引数なしコンストラクタ＋settable プロパティ）。
    /// フィールドに検証エラーがあれば全件を診断へ収集したうえで null を返す
    /// </summary>
    private string? BuildProjectionDto(
        Entity entity,
        QueryDefinition query,
        string resultTypeName,
        IReadOnlyDictionary<Guid, QueryColumnBinding> columnBindings,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var hasError = false;
        var properties = new List<string>();
        var seenFieldNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in query.Fields)
        {
            if (!IsValidIdentifier(field.Name) || !seenFieldNames.Add(field.Name))
            {
                diagnostics.Add(
                    GenerationDiagnostic.Error(
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
            bool isNullable;
            bool isReferenceType;

            if (field.SourceColumnId is { } columnId)
            {
                if (!columnBindings.TryGetValue(columnId, out var binding))
                {
                    diagnostics.Add(
                        GenerationDiagnostic.Error(
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

                // 列由来フィールドは列の生成型（VO 含む）を使い、NULL 許容も列から引き当てる（明示指定があれば優先）
                baseType = binding.ValueObjectClassName ?? binding.UnderlyingTypeName;
                isNullable = field.IsNullable ?? binding.IsNullable;
                isReferenceType =
                    binding.ValueObjectClassName is not null || binding.IsUnderlyingReferenceType;
            }
            else
            {
                if (!TryResolveTokenInfo(query, field.Type, diagnostics, out var tokenInfo))
                {
                    hasError = true;
                    continue;
                }

                // 自由フィールド（自由 SQL 由来）は列の裏付けがないため、既定で NULL 許容にする
                // （寛容マッパーの列欠落・集計 NULL を安全に受ける。明示指定があれば優先）
                baseType = tokenInfo.TypeName;
                isNullable = field.IsNullable ?? true;
                isReferenceType = tokenInfo.IsReferenceType;
            }

            // 非 NULL の参照型（VO 含む）は生成エンティティと同じく null! で初期化して警告を抑止する
            var typeText = isNullable ? baseType + "?" : baseType;
            var initializer = !isNullable && isReferenceType ? " = null!;" : string.Empty;
            properties.Add(
                $"    /// <summary>{EscapeForXmlDocSummary(field.Name)}</summary>\n"
                    + $"    public {typeText} {field.Name} {{ get; set; }}{initializer}"
            );
        }

        if (hasError)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder
            .Append("/// <summary>Projection DTO for the named query ")
            .Append(EscapeForXmlDocSummary(query.Name))
            .Append(" (")
            .Append(EscapeForXmlDocSummary(entity.TableName))
            .Append(").</summary>\n")
            .Append("public sealed partial class ")
            .Append(resultTypeName)
            .Append("\n{\n")
            .Append(string.Join("\n\n", properties))
            .Append("\n}");
        return builder.ToString();
    }

    /// <summary>ミニ DSL 射影の選択式（{v} =&gt; new Dto { F = {v}.Prop, ... }）を構築する（検証エラーは null）</summary>
    private static string? BuildProjectionSelector(
        QueryDefinition query,
        string resultTypeName,
        IReadOnlyDictionary<Guid, QueryColumnBinding> columnBindings,
        string lambdaVar,
        ICollection<GenerationDiagnostic> diagnostics
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
                    GenerationDiagnostic.Error(
                        string.Format(
                            Strings.CodeGen_Query_ProjectionDslRequiresColumns,
                            query.Name
                        )
                    )
                );
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
}
