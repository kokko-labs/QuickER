using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// UNIQUE 制約（<see cref="Entity.UniqueConstraints"/>）に基づく重複事前チェックのテンプレート用ブロックを構築する部分。
/// </summary>
/// <remarks>
/// <para>
/// 生成物は 2 系統ある。(1) Repository 側の一括チェック <c>CheckUniquenessAsync</c>＝各制約について
/// 「同一主キーの行を除外して同じ値の組を持つ行が DB に存在するか」を式木クエリで照合する。実装は名前付きクエリの
/// ミニ DSL と同じく「全実装先で同一テキストの共有本体」で、QuickER 版 Repository 2 方言・インメモリ・EF Core の
/// どれでも <c>Query()</c> パイプラインを通る。(2) EditModel 側＝クラスへ <c>[UniqueConstraint(...)]</c> 属性を刻んで
/// コレクション内重複検証の入力にし、DB 照合糖衣 <c>ValidateUniqueAsync</c> を生成する。
/// </para>
/// <para>
/// 契約はリモート契約生成（<c>GenerateRemoteContracts</c> または <c>GenerateRemoteServices</c>）の有無で挿入先が変わる
/// （Stream アクセサと同じ規則＝リモート面 ON なら <c>I{Entity}RemoteRepository</c>・OFF なら全機能面
/// <c>I{Entity}Repository</c>）。テンプレート側が出し分けるため、ここで組み立てるブロックは 1 本で足りる。
/// </para>
/// <para>
/// 制約が 1 件も無いエンティティでもメソッドとユーザー定義フック（<c>CollectCustomUniquenessChecks</c>）は生成する
/// （フックだけで独自の重複判定を足せるようにするため）。構成列の値に <c>null</c> を含む組は判定対象外
/// （NULL の衝突意味論が方言で割れるため、DB 照合・コレクション内検証の双方で同じ規則にする）。
/// </para>
/// </remarks>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>1 エンティティ分の重複事前チェックブロック（テンプレートへ渡す整形済みテキスト群）</summary>
    private sealed record UniquenessBlocks(
        string ContractBlock,
        string SharedImplBlock,
        string RemoteClientBlock,
        string RemoteServerBlock,
        string RemoteServerRecordsBlock
    );

    /// <summary>解決済みの UNIQUE 制約 1 件（構成列を生成コード上の姿へ解決したもの）</summary>
    /// <param name="ConstraintName">DDL 上の制約名（未設定なら合成名）</param>
    /// <param name="Members">構成列の生成プロパティ（宣言順）</param>
    private sealed record ResolvedUniqueConstraint(
        string ConstraintName,
        IReadOnlyList<CSharpPropertyModel> Members
    );

    /// <summary>
    /// エンティティの UNIQUE 制約を「生成コード上の姿」へ解決する（構成列が 1 つでも解決できない制約は対象外として捨てる）。
    /// </summary>
    /// <remarks>
    /// 制約名は <see cref="UniqueConstraint.Name"/> が正本で、未設定なら <see cref="UniqueConstraint.SynthesizeName"/> の
    /// 合成名を使う（DDL 生成と同じ意味論。方言別の識別子安全化はここでは行わない＝生成コードは名前を文字列として運ぶだけ）。
    /// </remarks>
    private List<ResolvedUniqueConstraint> ResolveUniqueConstraints(Entity entity)
    {
        var columnsById = entity.Columns.ToDictionary(column => column.Id);
        var resolved = new List<ResolvedUniqueConstraint>();

        foreach (var constraint in entity.UniqueConstraints)
        {
            if (constraint.ColumnIds.Count == 0)
            {
                continue;
            }

            var columns = new List<Column>(constraint.ColumnIds.Count);
            var complete = true;

            foreach (var columnId in constraint.ColumnIds)
            {
                // 削除済み列を指す残骸は生成対象外（列構成が欠けた制約は DDL 側でも成立しない）
                if (!columnsById.TryGetValue(columnId, out var column))
                {
                    complete = false;
                    break;
                }

                columns.Add(column);
            }

            if (!complete)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(constraint.Name)
                ? UniqueConstraint.SynthesizeName(
                    entity.TableName,
                    columns.Select(column => column.Name)
                )
                : constraint.Name;

            resolved.Add(
                new ResolvedUniqueConstraint(name, columns.Select(BuildProperty).ToList())
            );
        }

        return resolved;
    }

    /// <summary>単一主キーを持つ（＝ Repository 契約面が生成される）エンティティかどうか</summary>
    /// <remarks>Repository モデルの構築判定と EditModel の <c>ValidateUniqueAsync</c> 生成判定が同じ規則を共有する。</remarks>
    private static bool HasSinglePrimaryKey(Entity entity) =>
        entity.Columns.Count(column => column.IsPrimaryKey) == 1;

    /// <summary>エンティティの UNIQUE 制約から重複事前チェックのテンプレート用ブロックを構築する</summary>
    private UniquenessBlocks BuildUniquenessBlocks(
        Entity entity,
        string entityClassName,
        string repositoryName,
        CodeGenerationOptions options
    )
    {
        var constraints = ResolveUniqueConstraints(entity);
        var keyPropertyName = _nameConverter.ToPropertyName(
            entity.Columns.First(column => column.IsPrimaryKey).Name
        );

        var summary =
            $"Checks the UNIQUE constraints of {EscapeForXmlDocSummary(entity.TableName)} against the database and returns the violations (an empty list when there are none).";

        var shape = new QueryMethodShape(
            "CheckUniquenessAsync",
            $"{entityClassName} entity, CancellationToken cancellationToken = default",
            "Task<IReadOnlyList<UniquenessViolation>>",
            // クライアントの転送メソッドは「実体はサーバー側」であることを summary へ明示する（フックはサーバー側にしか無い）
            summary
                + " The check, including any user-defined hooks, runs in the server-side repository.",
            [new QueryPayloadParameter(entityClassName, "entity")]
        );

        return new UniquenessBlocks(
            BuildUniquenessContractMember(entityClassName, summary),
            BuildUniquenessImplMember(entityClassName, keyPropertyName, constraints),
            options.GenerateRemoteServices ? BuildRemoteClientMember(shape) : string.Empty,
            options.GenerateRemoteServices
                ? BuildRemoteServerMap(shape, repositoryName)
                : string.Empty,
            options.GenerateRemoteServices
                ? BuildRemoteServerRecord(shape, repositoryName) ?? string.Empty
                : string.Empty
        );
    }

    /// <summary>重複事前チェックの契約メンバー（インターフェイス宣言）を構築する</summary>
    private static string BuildUniquenessContractMember(string entityClassName, string summary) =>
        string.Join(
            "\n",
            $"    /// <summary>{summary}</summary>",
            "    /// <remarks>",
            "    /// Rows that share the entity's primary key are excluded, so the same call is correct for both insert and update. Constraint member values that contain",
            "    /// a null are skipped (NULL collision semantics differ per dialect). The result is advisory only: the definitive guarantee is the database's own UNIQUE",
            "    /// constraint, and a concurrent insert between this check and the save can still make the save fail (TOCTOU).",
            "    /// </remarks>",
            "    /// <param name=\"entity\">The entity whose constraint member values are checked.</param>",
            "    /// <param name=\"cancellationToken\">The cancellation token.</param>",
            "    Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(",
            $"        {entityClassName} entity,",
            "        CancellationToken cancellationToken = default",
            "    );"
        );

    /// <summary>
    /// 重複事前チェックの実装メンバー（式木クエリ経由・全実装先共通）と、ユーザー定義フックの partial 宣言を構築する。
    /// </summary>
    private static string BuildUniquenessImplMember(
        string entityClassName,
        string keyPropertyName,
        IReadOnlyList<ResolvedUniqueConstraint> constraints
    )
    {
        var lines = new List<string>
        {
            "    /// <inheritdoc />",
            "    public async Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(",
            $"        {entityClassName} entity,",
            "        CancellationToken cancellationToken = default",
            "    )",
            "    {",
            "        ArgumentNullException.ThrowIfNull(entity);",
            "        var violations = new List<UniquenessViolation>();",
        };

        for (var index = 0; index < constraints.Count; index++)
        {
            lines.Add(string.Empty);
            lines.AddRange(
                BuildUniquenessConstraintCheckLines(
                    entityClassName,
                    keyPropertyName,
                    constraints[index],
                    index + 1
                )
            );
        }

        // ユーザー定義チェック: 収集 → 順に await → null 以外を合流（フック未実装なら呼び出しごと消滅する）
        lines.AddRange([
            string.Empty,
            $"        List<UniquenessCheck<{entityClassName}>>? customChecks = null;",
            "        CollectCustomUniquenessChecks(ref customChecks);",
            string.Empty,
            "        if (customChecks is not null)",
            "        {",
            "            foreach (var customCheck in customChecks)",
            "            {",
            "                if (await customCheck(entity, cancellationToken) is { } violation)",
            "                {",
            "                    violations.Add(violation);",
            "                }",
            "            }",
            "        }",
            string.Empty,
            "        return violations;",
            "    }",
            string.Empty,
            "    /// <summary>Extension point for adding user-defined uniqueness checks (add delegates to the list in a partial implementation; while unimplemented the call is erased at no cost).</summary>",
            "    partial void CollectCustomUniquenessChecks(",
            $"        ref List<UniquenessCheck<{entityClassName}>>? checks",
            "    );",
        ]);

        return string.Join("\n", lines);
    }

    /// <summary>1 制約分の照合コード（NULL 組のスキップ → 存在確認 → 違反の追加）の行を組み立てる</summary>
    /// <remarks>
    /// 条件は制約構成列ごとに 1 つずつ <c>Where</c> を重ねる（<c>SqlQuery.Where</c> は AND 結合）。1 行 1 比較になるため
    /// 列数が増えても行が伸びず、方言翻訳・EF Core いずれでも同じ式木の形になる。
    /// 判定結果のローカル名に連番を付けるのは、NULL 検査の有無で宣言スコープ（メソッド直下 / <c>if</c> ブロック内）が
    /// 変わり、素の同名だと制約の組み合わせ次第で CS0136 になるため。
    /// </remarks>
    /// <param name="ordinal">制約の 1 始まり通し番号（判定結果のローカル名に使う）</param>
    private static IEnumerable<string> BuildUniquenessConstraintCheckLines(
        string entityClassName,
        string keyPropertyName,
        ResolvedUniqueConstraint constraint,
        int ordinal
    )
    {
        // 構成列の値に null を含む組は判定対象外。null を取り得るプロパティ（NULL 許容列・参照型・値オブジェクト）だけを検査する
        var nullChecks = constraint
            .Members.Where(member => member.IsNullable || member.IsReferenceType)
            .Select(member => $"entity.{member.PropertyName} is not null")
            .ToList();

        var indent = nullChecks.Count > 0 ? "            " : "        ";
        var lines = new List<string>
        {
            $"        // {constraint.ConstraintName}: {string.Join(", ", constraint.Members.Select(member => member.PropertyName))}",
        };

        if (nullChecks.Count > 0)
        {
            lines.Add($"        if ({string.Join(" && ", nullChecks)})");
            lines.Add("        {");
        }

        var duplicatedLocal = $"duplicated{ordinal}";
        lines.Add($"{indent}var {duplicatedLocal} = await Query()");
        lines.AddRange(
            constraint.Members.Select(member =>
                $"{indent}    .Where(candidate => candidate.{member.PropertyName} == entity.{member.PropertyName})"
            )
        );
        lines.Add(
            $"{indent}    .Where(candidate => candidate.{keyPropertyName} != entity.{keyPropertyName})"
        );
        lines.Add($"{indent}    .AnyAsync(cancellationToken);");
        lines.Add(string.Empty);
        lines.Add($"{indent}if ({duplicatedLocal})");
        lines.Add($"{indent}{{");
        lines.Add($"{indent}    violations.Add(");
        lines.Add($"{indent}        new UniquenessViolation(");
        lines.Add($"{indent}            \"{EscapeForCSharpString(constraint.ConstraintName)}\",");
        lines.Add($"{indent}            new[]");
        lines.Add($"{indent}            {{");
        lines.AddRange(
            constraint.Members.Select(member =>
                $"{indent}                nameof({entityClassName}.{member.PropertyName}),"
            )
        );
        lines.Add($"{indent}            }}");
        lines.Add($"{indent}        )");
        lines.Add($"{indent}    );");
        lines.Add($"{indent}}}");

        if (nullChecks.Count > 0)
        {
            lines.Add("        }");
        }

        return lines;
    }

    // ---- EditModel 側（コレクション内重複検証の属性・DB 照合糖衣） ----

    /// <summary>1 EditModel 分の重複検証ブロック（属性行・DB 照合糖衣メソッド・契約面の有無）</summary>
    private sealed record EditModelUniquenessBlocks(
        string AttributesBlock,
        string ValidationBlock,
        bool HasRepositoryFace
    );

    /// <summary>EditModel の重複検証ブロック（<c>[UniqueConstraint]</c> 属性と <c>ValidateUniqueAsync</c>）を構築する</summary>
    private EditModelUniquenessBlocks BuildEditModelUniquenessBlocks(
        Entity entity,
        CodeGenerationOptions options
    )
    {
        var constraints = ResolveUniqueConstraints(entity);
        var attributes = string.Join(
            "\n",
            constraints.Select(constraint =>
                "[UniqueConstraint("
                + string.Join(
                    ", ",
                    constraint.Members.Select(member => $"\"{member.PropertyName}\"")
                )
                + $", Name = \"{EscapeForCSharpString(constraint.ConstraintName)}\")]"
            )
        );

        // ValidateUniqueAsync は Repository 契約面（単一主キーが前提）が生成されるエンティティにだけ出せる
        var hasRepositoryFace = options.GeneratesRepositoryContract && HasSinglePrimaryKey(entity);

        return new EditModelUniquenessBlocks(
            attributes,
            hasRepositoryFace
                ? BuildEditModelValidateUniqueMethod(entity, constraints, options)
                : string.Empty,
            hasRepositoryFace
        );
    }

    /// <summary>DB 照合糖衣 <c>ValidateUniqueAsync</c>（EditModel の現在値から Entity を組んで Repository へ問い合わせる）を構築する</summary>
    private string BuildEditModelValidateUniqueMethod(
        Entity entity,
        IReadOnlyList<ResolvedUniqueConstraint> constraints,
        CodeGenerationOptions options
    )
    {
        var entityClassName = _nameConverter.ToEntityClassName(entity.TableName);
        var repositoryName = entityClassName.EndsWith("Entity", StringComparison.Ordinal)
            ? entityClassName[..^"Entity".Length]
            : entityClassName;

        // 契約面の切り替えは Stream アクセサのファイル糖衣と同じ規則（リモート面 ON なら契約はリモート面へ移設される）
        var faceName =
            options.GenerateRemoteContracts || options.GenerateRemoteServices
                ? $"I{repositoryName}RemoteRepository"
                : $"I{repositoryName}Repository";

        // 照合に要るのは主キー（自分自身の除外）と制約構成列だけなので、その列だけを写す
        var required = constraints
            .SelectMany(constraint => constraint.Members)
            .Select(member => member.PropertyName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var column in entity.Columns.Where(column => column.IsPrimaryKey))
        {
            required.Add(_nameConverter.ToPropertyName(column.Name));
        }

        var lines = new List<string>
        {
            "    /// <summary>",
            "    /// Checks this edit model's confirmed values against the database through the repository and registers duplicate-value errors (returns true when there are no violations).",
            "    /// </summary>",
            "    /// <remarks>",
            "    /// The duplicate-value errors registered by the previous call are cleared first, so re-checking never leaves stale errors. Rows that share the primary key are excluded,",
            "    /// so the same call is correct for both insert and update. The result is advisory only: the definitive guarantee is the database's own UNIQUE constraint (TOCTOU).",
            "    /// </remarks>",
            "    /// <param name=\"repository\">The repository used for the check.</param>",
            "    /// <param name=\"cancellationToken\">The cancellation token.</param>",
            "    public async Task<bool> ValidateUniqueAsync(",
            $"        {faceName} repository,",
            "        CancellationToken cancellationToken = default",
            "    )",
            "    {",
            "        ArgumentNullException.ThrowIfNull(repository);",
            "        ClearDuplicateErrors();",
            string.Empty,
            $"        var entity = new {entityClassName}();",
        };

        // 未入力（null）の列は写さない＝Entity の初期値のまま。構成列に null を含む組は照合対象外なので判定へ影響しない
        foreach (var column in entity.Columns)
        {
            var propertyName = _nameConverter.ToPropertyName(column.Name);

            if (!required.Contains(propertyName))
            {
                continue;
            }

            lines.AddRange([
                string.Empty,
                $"        if ({propertyName} is {{ }} resolved{propertyName})",
                "        {",
                $"            entity.{propertyName} = resolved{propertyName};",
                "        }",
            ]);
        }

        lines.AddRange([
            string.Empty,
            "        var violations = await repository.CheckUniquenessAsync(entity, cancellationToken);",
            string.Empty,
            "        foreach (var violation in violations)",
            "        {",
            "            RegisterDuplicateError(violation.PropertyNames, violation.Message);",
            "        }",
            string.Empty,
            "        return violations.Count == 0;",
            "    }",
        ]);

        return string.Join("\n", lines);
    }
}
