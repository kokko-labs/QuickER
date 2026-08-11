using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// UNIQUE 制約（<see cref="Entity.UniqueConstraints"/>）に基づく重複事前チェックのテンプレート用ブロックを構築する部分。
/// </summary>
/// <remarks>
/// <para>
/// 生成物は 3 系統ある。(1) Repository 側の一括チェック <c>CheckUniquenessAsync</c>＝各制約について
/// 「同一主キーの行を除外して同じ値の組を持つ行が DB に存在するか」を式木クエリで照合する。実装は名前付きクエリの
/// ミニ DSL と同じく「全実装先で同一テキストの共有本体」で、QuickER 版 Repository 2 方言・インメモリ・EF Core の
/// どれでも <c>Query()</c> パイプラインを通る。(2) Entity 側＝クラスへ <c>[UniqueConstraint(...)]</c> 属性を刻む
/// （<c>[DbTableMeta]</c> / <c>[DbColumnMeta]</c> と同じ「DB 定義の自己記述」で、実行時の振る舞いは持たない）。
/// (3) EditModel 側＝制約テーブル（<c>UniquenessConstraints</c> の override）でコレクション内重複検証の入力を宣言し、
/// DB 照合糖衣 <c>ValidateUniqueAsync</c> を生成する。
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
        var keyProperty = BuildProperty(entity.Columns.First(column => column.IsPrimaryKey));

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
            BuildUniquenessImplMember(entityClassName, keyProperty, constraints),
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
            "    /// Rows that share the entity's primary key are excluded, so the same call is correct for both insert and update (an entity whose key is not set yet excludes nothing). Constraint member values that contain",
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
        CSharpPropertyModel keyProperty,
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
                    keyProperty,
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
            "                if (",
            "                    await customCheck(entity, cancellationToken)",
            "                        .ConfigureAwait(false) is { } violation",
            "                )",
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
    /// <para>
    /// 条件は制約構成列ごとに 1 つずつ <c>Where</c> を重ねる（<c>SqlQuery.Where</c> は AND 結合）。1 行 1 比較になるため
    /// 列数が増えても行が伸びず、方言翻訳・EF Core いずれでも同じ式木の形になる。
    /// 判定結果のローカル名に連番を付けるのは、NULL 検査の有無で宣言スコープ（メソッド直下 / <c>if</c> ブロック内）が
    /// 変わり、素の同名だと制約の組み合わせ次第で CS0136 になるため。
    /// </para>
    /// <para>
    /// 自分自身の除外（主キー不一致の <c>Where</c>）は<b>主キーが null を取り得る型（値オブジェクト・string 等）のとき
    /// 条件付きで足す</b>。挿入前のエンティティは主キー未設定＝除外すべき行が存在しないため、除外条件そのものを
    /// 付けないのが正しい（付けたままだと「NULL との比較」に依存することになる）。非 NULL の値型（int 等）は
    /// <c>is not null</c> が常に真で警告になるため、従来どおり無条件に連ねる＝そうした図の生成物はバイト不変。
    /// </para>
    /// </remarks>
    /// <param name="ordinal">制約の 1 始まり通し番号（判定結果のローカル名に使う）</param>
    private static IEnumerable<string> BuildUniquenessConstraintCheckLines(
        string entityClassName,
        CSharpPropertyModel keyProperty,
        ResolvedUniqueConstraint constraint,
        int ordinal
    )
    {
        var keyPropertyName = keyProperty.PropertyName;

        // 主キーが null を取り得るか（構成列の NULL 検査と同じ判定規則）
        var keyCanBeNull = keyProperty.IsNullable || keyProperty.IsReferenceType;
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
        var memberFilters = constraint
            .Members.Select(member =>
                $"{indent}    .Where(candidate => candidate.{member.PropertyName} == entity.{member.PropertyName})"
            )
            .ToList();

        if (keyCanBeNull)
        {
            // 主キーを持ち得ない（＝未設定の）新規行では除外条件そのものを足さない
            var queryLocal = $"query{ordinal}";

            lines.Add($"{indent}var {queryLocal} = Query()");
            lines.AddRange(memberFilters);
            lines[^1] += ";";
            lines.Add(string.Empty);
            lines.Add(
                $"{indent}// A row that has no primary key yet (a new row) has no row of its own to exclude"
            );
            lines.Add($"{indent}if (entity.{keyPropertyName} is not null)");
            lines.Add($"{indent}{{");
            lines.Add(
                $"{indent}    {queryLocal} = {queryLocal}.Where(candidate => candidate.{keyPropertyName} != entity.{keyPropertyName});"
            );
            lines.Add($"{indent}}}");
            lines.Add(string.Empty);
            lines.Add($"{indent}var {duplicatedLocal} = await {queryLocal}");
            lines.Add($"{indent}    .AnyAsync(cancellationToken)");
            lines.Add($"{indent}    .ConfigureAwait(false);");
        }
        else
        {
            lines.Add($"{indent}var {duplicatedLocal} = await Query()");
            lines.AddRange(memberFilters);
            lines.Add(
                $"{indent}    .Where(candidate => candidate.{keyPropertyName} != entity.{keyPropertyName})"
            );
            lines.Add($"{indent}    .AnyAsync(cancellationToken)");
            lines.Add($"{indent}    .ConfigureAwait(false);");
        }

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

    // ---- Entity 側（DB 定義の自己記述属性） ----

    /// <summary>
    /// Entity クラスへ刻む <c>[UniqueConstraint(...)]</c> 属性行（制約なしは空文字）を構築する。
    /// </summary>
    /// <remarks>
    /// 役割は <c>[DbTableMeta]</c> / <c>[DbColumnMeta]</c> と同じ「DB 定義の自己記述」で、実行時に読む機構は無い
    /// （コレクション内重複検証は EditModel 側の生成コードが持つ制約テーブルを使う）。将来の C# → ErDiagram
    /// リバースが列型・説明と同じ経路で UNIQUE 制約を復元できるようにするための布石でもある。
    /// </remarks>
    private string BuildEntityUniqueConstraintAttributes(Entity entity) =>
        string.Join(
            "\n",
            ResolveUniqueConstraints(entity)
                .Select(constraint =>
                    "[UniqueConstraint("
                    + string.Join(
                        ", ",
                        constraint.Members.Select(member => $"\"{member.PropertyName}\"")
                    )
                    + $", Name = \"{EscapeForCSharpString(constraint.ConstraintName)}\")]"
                )
        );

    // ---- EditModel 側（コレクション内重複検証の制約テーブル・DB 照合糖衣） ----

    /// <summary>1 EditModel 分の重複検証ブロック（制約テーブル・DB 照合糖衣メソッド・条件付き固定メンバーの発火有無）</summary>
    private sealed record EditModelUniquenessBlocks(
        string ConstraintsBlock,
        string ValidationBlock,
        bool HasRepositoryFace,
        bool HasUniqueConstraints
    );

    /// <summary>EditModel の重複検証ブロック（制約テーブルの override と <c>ValidateUniqueAsync</c>）を構築する</summary>
    private EditModelUniquenessBlocks BuildEditModelUniquenessBlocks(
        Entity entity,
        CodeGenerationOptions options
    )
    {
        var constraints = ResolveUniqueConstraints(entity);

        // ValidateUniqueAsync は Repository 契約面（単一主キーが前提）が生成されるエンティティにだけ出せる
        var hasRepositoryFace = options.GeneratesRepositoryContract && HasSinglePrimaryKey(entity);

        return new EditModelUniquenessBlocks(
            constraints.Count > 0
                ? BuildEditModelConstraintsTable(entity, constraints)
                : string.Empty,
            hasRepositoryFace
                ? BuildEditModelValidateUniqueMethod(entity, constraints, options)
                : string.Empty,
            hasRepositoryFace,
            constraints.Count > 0
        );
    }

    /// <summary>
    /// コレクション内重複検証の入力となる制約テーブル（<c>_uniquenessConstraints</c> と
    /// <c>UniquenessConstraints</c> の override）を構築する。
    /// </summary>
    /// <remarks>
    /// 値アクセサはコンパイル済みのラムダ（1 呼び出しで構成列の値を配列で返す）で、検証時のリフレクションは無い。
    /// 読むのは確定値プロパティ（バインディング文字列ではない）で、DB 照合・DDL と同じ値の組を比較する。
    /// テーブルは <c>static readonly</c> の 1 回構築で、インスタンスごとの再構築は起きない。
    /// </remarks>
    private string BuildEditModelConstraintsTable(
        Entity entity,
        IReadOnlyList<ResolvedUniqueConstraint> constraints
    )
    {
        var editModelClassName = _nameConverter.ToEditModelClassName(entity.TableName);

        var lines = new List<string>
        {
            $"    /// <summary>UNIQUE constraints of the {EscapeForXmlDocSummary(entity.TableName)} table, with compiled accessors for their member values (input of the duplicate check inside a collection).</summary>",
            "    private static readonly IReadOnlyList<EditModelUniquenessConstraint> _uniquenessConstraints =",
            "        new EditModelUniquenessConstraint[]",
            "        {",
        };

        foreach (var constraint in constraints)
        {
            var names = string.Join(
                ", ",
                constraint.Members.Select(member => $"nameof({member.PropertyName})")
            );

            lines.Add("            new(");
            lines.Add($"                \"{EscapeForCSharpString(constraint.ConstraintName)}\",");
            lines.Add($"                new[] {{ {names} }},");

            // 構成列 1 つなら 1 行に収まる。複数列は 1 行 1 値へ展開して行の伸びを抑える
            if (constraint.Members.Count == 1)
            {
                lines.Add(
                    $"                static model => new object?[] {{ (({editModelClassName})model).{constraint.Members[0].PropertyName} }}"
                );
            }
            else
            {
                lines.Add("                static model =>");
                lines.Add("                    new object?[]");
                lines.Add("                    {");
                lines.AddRange(
                    constraint.Members.Select(member =>
                        $"                        (({editModelClassName})model).{member.PropertyName},"
                    )
                );
                lines.Add("                    }");
            }

            lines.Add("            ),");
        }

        lines.AddRange([
            "        };",
            string.Empty,
            "    /// <inheritdoc />",
            "    public override IReadOnlyList<EditModelUniquenessConstraint> UniquenessConstraints =>",
            "        _uniquenessConstraints;",
        ]);

        return string.Join("\n", lines);
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
            "    /// so the same call is correct for both insert and update (a model whose key is not set yet excludes nothing). The result is advisory only: the definitive guarantee is the database's own UNIQUE constraint (TOCTOU).",
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
            "        var violations = await repository",
            "            .CheckUniquenessAsync(entity, cancellationToken)",
            "            .ConfigureAwait(false);",
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
