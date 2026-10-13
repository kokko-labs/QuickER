using System.Globalization;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;

namespace QuickER.CodeGen.CSharp.Queries;

/// <summary>
/// 名前付きクエリ定義の構造規則（生成すると必ずエラーになる定義を見分ける規則）の正本。
/// </summary>
/// <remarks>
/// <para>
/// 生成器（<c>CSharpGenerationModelBuilder.Queries</c>）は各判定をここの部品で行い、診断の文言も
/// <see cref="QueryRuleViolation.Text"/> から作る。保存前に同じ判定を通したい面（MCP / 内蔵チャットの
/// <c>set_query</c>・GUI のクエリ定義ダイアログ）は <see cref="Validate"/> を呼ぶ。規則ごとの順序と打ち切りは
/// 部品（<c>CheckMethodName</c> / <c>CheckShape</c> / <c>CheckParameterName</c> / <c>CheckProjectionHeader</c> /
/// <c>CheckFieldName</c>）が 1 か所で持ち、生成器と <see cref="Validate"/> は同じ部品を同じ順に呼ぶ
/// （同じ定義に対して同じ違反を同じ文言で返す）。生成器はその間に、型の解決に依る検査を差し込む。
/// </para>
/// <para>
/// 対象は生成オプション・方言・型解決に依らない規則だけ。型トークンの解決、条件式の型整合、射影フィールドの
/// NULL 許容の上書き、列参照の切れ（ダングリング）は列の C# 型（方言の型マッパー）や図の状態に依存するため、
/// 生成時の診断に委ねる。
/// </para>
/// </remarks>
public static class QueryDefinitionRules
{
    /// <summary>クエリ名からメソッド名を作る変換器（生成器と同じ規則）</summary>
    private static readonly CSharpNameConverter NameConverter = new();

    /// <summary>
    /// 標準 Repository メンバー・リモートエンドポイントの操作名と衝突するため名前付きクエリに使えないメソッド名。
    /// </summary>
    /// <remarks>
    /// C# メンバー名の衝突（コンパイルエラー）だけでなく、リモートサービスのルート
    /// （<c>POST {prefix}/{エンティティ}/{操作}</c>）の衝突も対象にする。ルートが重なると同じパスへ
    /// ハンドラが 2 本張られ、実行時に AmbiguousMatch の 500 になる（<c>SaveMany</c>＝保存の複数形・
    /// <c>Ping</c>＝死活確認・<c>SyncCeiling</c> / <c>SyncChanges</c> / <c>SyncKeys</c> / <c>SyncPage</c>＝
    /// 同期専用エンドポイント）。リモートを生成しない構成でも予約する（後からリモートを ON にした
    /// 瞬間に既存クエリ名が壊れる非対称を作らない）。照合はルートの照合規則に合わせて大小無視
    /// （ASP.NET Core のルートリテラルは大小を区別しないため、<c>Savemany</c> のような大小違いも
    /// 同じパスへの 2 本目のハンドラになる）。
    /// </remarks>
    public static IReadOnlySet<string> ReservedMethodNames { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GetByIdAsync",
            "GetAllAsync",
            "InsertAsync",
            "BulkInsertAsync",
            "UpdateAsync",
            "DeleteAsync",
            "SaveAsync",
            "SaveManyAsync",
            "PingAsync",
            "SyncCeilingAsync",
            "SyncChangesAsync",
            "SyncKeysAsync",
            "SyncPageAsync",
            "QueryBySqlAsync",
            "ExecuteSqlAsync",
            "ExecuteScalarSqlAsync",
            "QueryProjectionBySqlAsync",
            "CheckUniquenessAsync",
        };

    /// <summary>
    /// 識別子としては合法だが、C# が型の名前として受け付けない文脈キーワード（射影の結果型名に使えない）
    /// </summary>
    /// <remarks>
    /// <c>extension</c> / <c>file</c> / <c>record</c> / <c>required</c> / <c>scoped</c> は、型の宣言に使うと
    /// 構文エラーかコンパイルエラーになる。パラメータ名・フィールド名には使えるので <see cref="IsValidIdentifier"/> には
    /// 含めない。表が Roslyn の判定と合っていることは、文脈キーワードの全数を型の名前にしてコンパイルするテストが固定する
    /// </remarks>
    private static readonly IReadOnlySet<string> ContextualKeywordsInvalidAsTypeNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "extension",
            "file",
            "record",
            "required",
            "scoped",
        };

    /// <summary>射影の結果型名（DTO のクラス名）として妥当か（識別子であり、型の名前に使えない文脈キーワードでない）</summary>
    public static bool IsValidResultTypeName(string name) =>
        IsValidIdentifier(name) && !ContextualKeywordsInvalidAsTypeNames.Contains(name);

    /// <summary>
    /// 射影 DTO の型名が、生成コードの型名と衝突するか（衝突すると重複宣言か、単純名で使う型を隠してコンパイルできない）
    /// </summary>
    /// <param name="name">DTO の型名</param>
    /// <param name="generatedTypeNames">図から決まる生成型名（<see cref="GeneratedTypeNames.Collect"/>）</param>
    internal static bool IsReservedResultTypeName(
        string name,
        IReadOnlySet<string> generatedTypeNames
    ) => GeneratedFixedTypeNames.IsReserved(name) || generatedTypeNames.Contains(name);

    /// <summary>C# 識別子として妥当か（先頭は文字か _、以降は文字・数字・_。予約語は不可）</summary>
    /// <remarks>
    /// 生成器は識別子を <c>@</c> エスケープせずそのまま出力するため、予約語（<c>class</c> / <c>int</c> 等）を
    /// 通すと引数宣言・DTO プロパティ宣言がコンパイル不能になる（判定表は名前空間検証と共有。
    /// 文脈キーワードは識別子として合法なので対象にしない）
    /// </remarks>
    public static bool IsValidIdentifier(string name) =>
        !string.IsNullOrEmpty(name)
        && (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(c => char.IsLetterOrDigit(c) || c == '_')
        && !CSharpNamespaceValidator.IsReservedKeyword(name);

    /// <summary>クエリ名から生成メソッド名（PascalCase ＋ <c>Async</c>）を作る。識別子にならなければ <c>null</c></summary>
    public static string? ToMethodName(string queryName)
    {
        var baseName = NameConverter.ToPropertyName(queryName);

        if (string.IsNullOrEmpty(baseName) || !IsValidIdentifier(baseName))
        {
            return null;
        }

        return baseName.EndsWith("Async", StringComparison.Ordinal) ? baseName : baseName + "Async";
    }

    /// <summary>
    /// パラメータ名に使えない名前か
    /// </summary>
    /// <remarks>
    /// <para>
    /// 予約するのは、生成したコードがコンパイルできなくなる名前だけ（<c>ReservedName_IsActuallyUncompilable</c> が、
    /// 予約から外すとコンパイルできないことを名前ごとに確かめる）。生成本体が呼ぶメンバー（<c>Query()</c>・
    /// <c>QueryBySqlAsync</c>・同期のデコレータの <c>_inner</c>・リモートのクライアントの <c>InvokeAsync</c> など）は
    /// <c>this.</c> で修飾して呼ぶので、同名のパラメータがあっても隠されない＝予約しない。
    /// </para>
    /// <para>
    /// <c>await</c>（大文字小文字まで一致）: 生 SQL の単一・件数の生成メソッドは async メソッドで、その中では
    /// <c>await</c> を識別子に使えない。ほかの文脈キーワードは、生成されるどのメソッドの形でもパラメータ名として通る
    /// （<c>QueryReservedParameterNameTests</c> が全文脈キーワード × 全戻り形 × 実装方式を実コンパイルで固定する）。
    /// </para>
    /// <para>
    /// record のメンバーとぶつかる名前（<c>ToString</c> / <c>Equals</c> / <c>GetHashCode</c> / <c>GetType</c> /
    /// <c>MemberwiseClone</c> / <c>ReferenceEquals</c> / <c>EqualityContract</c> / <c>PrintMembers</c> / <c>Deconstruct</c>）:
    /// リモートサービスのリクエストは、パラメータ名を PascalCase にしたメンバーを持つ positional record なので、
    /// object のメンバーや record が合成するメンバーと同じ名前になるとコンパイルできない（CS8866 / CS0102）。
    /// 照合は PascalCase にした名前で行い（<c>toString</c> も同じ）、リモートを生成しない構成でも予約する
    /// （後からリモートを ON にした瞬間に既存の定義が壊れる非対称を作らない＝<see cref="ReservedMethodNames"/> と同じ方針）。
    /// </para>
    /// <para>
    /// 値オブジェクトのクラス名と同名の引数（<c>CustomerIdValue</c> など）も、条件式の <c>Create</c> 呼び出しを隠して
    /// コンパイルできないが、クラス名は図と生成オプションから決まるため、ここの予約には載せない（docs の既知の割り切り）。
    /// </para>
    /// </remarks>
    public static bool IsReservedParameterName(string name) =>
        string.Equals(name, "await", StringComparison.Ordinal)
        || (
            name.Length > 0
            && RecordMemberNames.Contains(char.ToUpperInvariant(name[0]) + name[1..])
        );

    /// <summary>
    /// リモートのリクエストの record（パラメータ名を PascalCase にしたメンバーを持つ）で、メンバー名にできない名前
    /// （object のメンバーと、record が合成するメンバー）
    /// </summary>
    private static readonly IReadOnlySet<string> RecordMemberNames = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "ToString",
        "Equals",
        "GetHashCode",
        "GetType",
        "MemberwiseClone",
        "ReferenceEquals",
        "EqualityContract",
        "PrintMembers",
        "Deconstruct",
    };

    /// <summary>ページングを持つクエリの生成メソッドが、宣言したパラメータの後ろに足す引数の名前（宣言順）</summary>
    /// <remarks>生 SQL からは <c>@take</c> / <c>@skip</c> で参照できる（宣言したパラメータと同じく束縛する）</remarks>
    public static IReadOnlyList<string> PagingParameterNames { get; } = ["take", "skip"];

    /// <summary>生成メソッドが自分で持つ引数の名前（宣言したパラメータと重なると重複になる）</summary>
    /// <param name="hasPaging">ページングを持つか（持てば <see cref="PagingParameterNames"/> が加わる）</param>
    public static IEnumerable<string> ImplicitParameterNames(bool hasPaging) =>
        hasPaging ? ["cancellationToken", .. PagingParameterNames] : ["cancellationToken"];

    /// <summary>スカラー戻り形を簡易 DSL で組もうとしているか（スカラーは生 SQL / 手動実装専用）</summary>
    public static bool IsScalarDslConflict(
        QueryReturnShape returns,
        QueryImplementationKind implementation
    ) => returns == QueryReturnShape.Scalar && implementation == QueryImplementationKind.Dsl;

    /// <summary>ページングを持てる戻り形か（一覧・射影）</summary>
    public static bool SupportsPaging(QueryReturnShape returns) =>
        returns is QueryReturnShape.List or QueryReturnShape.Projection;

    /// <summary>並び順を持てる戻り形か（一覧・射影・単一＝並び替えて先頭 1 件）</summary>
    public static bool SupportsOrderBy(QueryReturnShape returns) =>
        SupportsPaging(returns) || returns == QueryReturnShape.Single;

    // ---- 規則の部品 ----
    // 生成器と Validate が同じ部品を呼ぶ（規則の中の順序と打ち切りを 2 か所に写さない）。部品どうしの順序
    // （メソッド名 → 戻り形 → パラメータ名 → 射影）と、型の解決に依る検査を差し込む位置は、それぞれの呼び出し側が持つ

    /// <summary>メソッド名の規則（名前の形 → 予約 → 重複の順。最初の違反で打ち切る）</summary>
    /// <param name="query">調べる定義</param>
    /// <param name="tableName">重複の文言に載せるテーブル名</param>
    /// <param name="tryClaim">メソッド名を使ってよいか（使えるなら確保して true。生成器は確保もする）</param>
    /// <param name="methodName">クエリ名から作ったメソッド名（名前の形の違反なら null）</param>
    /// <returns>違反（無ければ null）</returns>
    internal static QueryRuleViolation? CheckMethodName(
        QueryDefinition query,
        string tableName,
        Func<string, bool> tryClaim,
        out string? methodName
    )
    {
        methodName = ToMethodName(query.Name);

        if (methodName is null)
        {
            return QueryRuleViolation.InvalidName(query.Name);
        }

        if (ReservedMethodNames.Contains(methodName))
        {
            return QueryRuleViolation.ReservedMethodName(query.Name, methodName);
        }

        return tryClaim(methodName)
            ? null
            : QueryRuleViolation.DuplicateMethodName(query.Name, methodName, tableName);
    }

    /// <summary>戻り形と実装方式の整合の規則（打ち切らずに全部返す）</summary>
    internal static IEnumerable<QueryRuleViolation> CheckShape(QueryDefinition query)
    {
        if (query.Returns == QueryReturnShape.Scalar && string.IsNullOrWhiteSpace(query.ScalarType))
        {
            yield return QueryRuleViolation.ScalarRequiresType(query.Name);
        }

        if (IsScalarDslConflict(query.Returns, query.Implementation))
        {
            yield return QueryRuleViolation.ScalarDslUnsupported(query.Name);
        }

        if (query.HasPaging && !SupportsPaging(query.Returns))
        {
            yield return QueryRuleViolation.PagingRequiresList(query.Name);
        }

        if (query.OrderBy.Count > 0 && !SupportsOrderBy(query.Returns))
        {
            yield return QueryRuleViolation.OrderByRequiresList(query.Name);
        }
    }

    /// <summary>パラメータ名の重複を数える集合（生成メソッドが自分で持つ引数の名前を最初から含む）</summary>
    internal static HashSet<string> NewParameterNameSet(bool hasPaging) =>
        new(ImplicitParameterNames(hasPaging), StringComparer.OrdinalIgnoreCase);

    /// <summary>パラメータ名の規則（識別子 → 予約 → 重複の順）。通れば <paramref name="seen"/> へ登録する</summary>
    /// <param name="queryName">クエリ名（文言用）</param>
    /// <param name="parameterName">調べるパラメータ名</param>
    /// <param name="seen">ここまでのパラメータ名（暗黙の引数を含む）</param>
    /// <param name="selectorFieldName">
    /// そのクエリの射影の選択式を置く静的フィールドの名前（<see cref="SelectorFieldNameFor"/>。無ければ null）。
    /// 同名のパラメータはフィールドを隠してコンパイルできない。静的フィールドは <c>this.</c> で修飾できないので予約で断る
    /// </param>
    internal static QueryRuleViolation? CheckParameterName(
        string queryName,
        string parameterName,
        HashSet<string> seen,
        string? selectorFieldName
    )
    {
        if (!IsValidIdentifier(parameterName))
        {
            return QueryRuleViolation.InvalidParameterName(queryName, parameterName);
        }

        if (
            IsReservedParameterName(parameterName)
            || string.Equals(parameterName, selectorFieldName, StringComparison.Ordinal)
        )
        {
            return QueryRuleViolation.ReservedParameterName(queryName, parameterName);
        }

        return seen.Add(parameterName)
            ? null
            : QueryRuleViolation.DuplicateParameterName(queryName, parameterName);
    }

    /// <summary>
    /// 簡易 DSL の射影の選択式を置く静的フィールドの名前（<c>_</c> ＋小文字始まりのメソッド名＋ <c>Selector</c>）。
    /// 生成器がフィールドを宣言するときも、パラメータ名の規則も、この関数で名前を作る
    /// </summary>
    internal static string ProjectionSelectorFieldName(string methodName) =>
        "_" + char.ToLowerInvariant(methodName[0]) + methodName[1..] + "Selector";

    /// <summary>そのクエリが選択式のフィールドを持つなら、その名前（簡易 DSL の射影だけ。ほかは null）</summary>
    internal static string? SelectorFieldNameFor(QueryDefinition query, string methodName) =>
        query.Returns == QueryReturnShape.Projection
        && query.Implementation == QueryImplementationKind.Dsl
            ? ProjectionSelectorFieldName(methodName)
            : null;

    /// <summary>
    /// 射影の結果型名の規則（フィールドと型名の有無 → 型名の形 → 生成コードの型名との衝突 → 重複の順。最初の違反で打ち切る）
    /// </summary>
    /// <param name="query">調べる射影の定義</param>
    /// <param name="generatedTypeNames">図から決まる生成型名</param>
    /// <param name="tryClaim">結果型名を使ってよいか（使えるなら確保して true。生成器は確保もする）</param>
    internal static QueryRuleViolation? CheckProjectionHeader(
        QueryDefinition query,
        IReadOnlySet<string> generatedTypeNames,
        Func<string, bool> tryClaim
    )
    {
        var resultTypeName = query.ResultTypeName ?? string.Empty;

        if (query.Fields.Count == 0 || resultTypeName.Length == 0)
        {
            return QueryRuleViolation.ProjectionRequiresFields(query.Name);
        }

        if (!IsValidResultTypeName(resultTypeName))
        {
            return QueryRuleViolation.InvalidResultTypeName(query.Name, resultTypeName);
        }

        if (IsReservedResultTypeName(resultTypeName, generatedTypeNames))
        {
            return QueryRuleViolation.ReservedResultTypeName(query.Name, resultTypeName);
        }

        return tryClaim(resultTypeName)
            ? null
            : QueryRuleViolation.DuplicateResultTypeName(query.Name, resultTypeName);
    }

    /// <summary>
    /// 射影のフィールド名の重複を数える集合（SchemaNames の規則＝前後の空白を除き大文字小文字を区別しない。
    /// 大小だけ違う名前は C# では別のプロパティだが、生 SQL の列別名の対応づけ・リモートの JSON で衝突する）
    /// </summary>
    internal static HashSet<string> NewFieldNameSet() => new(SchemaNames.Comparer);

    /// <summary>射影のフィールド名の規則（識別子・重複 → 結果型名との一致の順）。通れば <paramref name="seen"/> へ登録する</summary>
    internal static QueryRuleViolation? CheckFieldName(
        string queryName,
        string fieldName,
        string resultTypeName,
        HashSet<string> seen
    )
    {
        if (!IsValidIdentifier(fieldName) || !seen.Add(fieldName))
        {
            return QueryRuleViolation.InvalidFieldName(queryName, fieldName);
        }

        // DTO 型名と同名のプロパティは C# が禁止する（CS0542＝メンバー名は外側の型名と同じにできない）
        return string.Equals(fieldName, resultTypeName, StringComparison.Ordinal)
            ? QueryRuleViolation.FieldNameEqualsResultType(queryName, fieldName)
            : null;
    }

    /// <summary>
    /// クエリ定義が生成で必ずエラーになる構造規則に違反していないかを調べ、違反を生成器と同じ順序・同じ打ち切りで返す
    /// </summary>
    /// <param name="entity">クエリの対象テーブル（重複の文言にテーブル名を載せる）</param>
    /// <param name="query">調べる定義</param>
    /// <param name="otherQueries">
    /// 図のほかのクエリ（<paramref name="query"/> 自身を含めない。置き換える前の同じクエリも含めない）。
    /// 同じテーブルのクエリとはメソッド名の重複を、すべてのクエリとは射影の結果型名の重複を見る
    /// </param>
    /// <param name="diagramEntities">
    /// 図のすべてのテーブル。射影の結果型名が、図から決まる生成型名（エンティティ・Repository・値オブジェクトなど）と
    /// 衝突しないかを見るのに使う。<b>省略すると固定の名簿（ランタイム・外部の型）だけで判定し、図由来の衝突を見逃す</b>ので、
    /// 図を持つ呼び出し側（GUI のダイアログ・MCP の <c>set_query</c>）は必ず渡す。渡したときは、どのテーブルにも属さない
    /// クエリ（属していたテーブルが図に無い）を重複の相手から外す（生成器はそうしたクエリを読み飛ばすので衝突しない。
    /// 数えると、どのツールからも消せない残骸が新しい定義の名前を塞ぐ）
    /// </param>
    /// <returns>違反（無ければ空）</returns>
    /// <remarks>
    /// メソッド名の重複の相手に数えるのは、それ自身が名前の規則を満たすクエリだけ（生成器は名前の規則に違反した
    /// クエリの名前を登録しない）。射影の結果型名の重複は、クエリ名がメソッド名になる射影すべてを相手に数える。
    /// 生成器は警告で飛ばしたクエリ（条件式の型の不整合・列参照の切れなど）の結果型名を登録しないので、
    /// その相手との重複は生成では衝突しないが、相手を直した時点で衝突するので保存の前に拒否する（安全側）。
    /// 生成器は図の並びで後に来たほうへ重複を報告するが、ここは並びに依らず調べる定義の側へ報告する。
    /// </remarks>
    public static IReadOnlyList<QueryRuleViolation> Validate(
        Entity entity,
        QueryDefinition query,
        IEnumerable<QueryDefinition> otherQueries,
        IEnumerable<Entity>? diagramEntities = null
    )
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(otherQueries);

        var entities = diagramEntities?.ToList();
        var entityIds = entities?.Select(candidate => candidate.Id).ToHashSet();
        var others = otherQueries
            .Where(other => entityIds is null || entityIds.Contains(other.EntityId))
            .ToList();
        var violations = new List<QueryRuleViolation>();

        // ---- メソッド名（違反があればこのクエリの残りは見ない＝生成器と同じ打ち切り） ----
        var siblingMethodNames = others
            .Where(other => other.EntityId == query.EntityId)
            .Select(other => ToMethodName(other.Name))
            .OfType<string>()
            .Where(name => !ReservedMethodNames.Contains(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (
            CheckMethodName(
                query,
                entity.TableName,
                name => !siblingMethodNames.Contains(name),
                out var methodName
            ) is
            { } methodNameViolation
        )
        {
            return [methodNameViolation];
        }

        // ---- 戻り形と実装方式の整合 ----
        violations.AddRange(CheckShape(query));

        // ---- パラメータ名 ----
        var seenParameterNames = NewParameterNameSet(query.HasPaging);

        foreach (var parameter in query.Parameters)
        {
            if (
                CheckParameterName(
                    query.Name,
                    parameter.Name,
                    seenParameterNames,
                    SelectorFieldNameFor(query, methodName!)
                ) is
                { } violation
            )
            {
                violations.Add(violation);
            }
        }

        if (query.Returns == QueryReturnShape.Projection)
        {
            AddProjectionViolations(
                query,
                others,
                entities is null ? [] : GeneratedTypeNames.Collect(entities),
                violations
            );
        }

        return violations;
    }

    /// <summary>射影（結果型名・フィールド名・DSL の列参照）の規則を調べる（生成器と同じ順序・打ち切り）</summary>
    private static void AddProjectionViolations(
        QueryDefinition query,
        IReadOnlyList<QueryDefinition> others,
        IReadOnlySet<string> generatedTypeNames,
        List<QueryRuleViolation> violations
    )
    {
        var otherResultTypeNames = others
            .Where(other =>
                other.Returns == QueryReturnShape.Projection && ToMethodName(other.Name) is not null
            )
            .Select(other => other.ResultTypeName ?? string.Empty)
            .Where(IsValidResultTypeName)
            .ToHashSet(StringComparer.Ordinal);

        if (
            CheckProjectionHeader(
                query,
                generatedTypeNames,
                name => !otherResultTypeNames.Contains(name)
            ) is
            { } headerViolation
        )
        {
            violations.Add(headerViolation);
            return;
        }

        var resultTypeName = query.ResultTypeName!;
        var seenFieldNames = NewFieldNameSet();

        foreach (var field in query.Fields)
        {
            if (
                CheckFieldName(query.Name, field.Name, resultTypeName, seenFieldNames) is
                { } fieldViolation
            )
            {
                violations.Add(fieldViolation);
            }
        }

        // 簡易 DSL の射影は選択式を列から組むので、列を参照しないフィールドは置けない（最初の 1 件で打ち切る）
        if (
            query.Implementation == QueryImplementationKind.Dsl
            && query.Fields.Any(field => field.SourceColumnId is null)
        )
        {
            violations.Add(QueryRuleViolation.ProjectionDslRequiresColumns(query.Name));
        }
    }
}

/// <summary>名前付きクエリの構造規則の種別（<see cref="QueryDefinitionRules"/>）</summary>
public enum QueryRuleKind
{
    /// <summary>クエリ名がメソッド名にならない</summary>
    InvalidName,

    /// <summary>メソッド名が標準 Repository メンバー・リモートの操作名と衝突する</summary>
    ReservedMethodName,

    /// <summary>メソッド名が同じテーブルの別のクエリと重複する</summary>
    DuplicateMethodName,

    /// <summary>スカラー戻り形なのに型トークンが無い</summary>
    ScalarRequiresType,

    /// <summary>スカラー戻り形を簡易 DSL で組もうとしている</summary>
    ScalarDslUnsupported,

    /// <summary>一覧・射影以外の戻り形でページングを指定している</summary>
    PagingRequiresList,

    /// <summary>一覧・射影・単一以外の戻り形で並び順を指定している</summary>
    OrderByRequiresList,

    /// <summary>パラメータ名が C# 識別子でない</summary>
    InvalidParameterName,

    /// <summary>パラメータ名が、生成するメソッドの使う名前と衝突する（<see cref="QueryDefinitionRules.IsReservedParameterName"/>）</summary>
    ReservedParameterName,

    /// <summary>パラメータ名が重複する（暗黙の引数名を含む）</summary>
    DuplicateParameterName,

    /// <summary>射影なのに結果型名かフィールドが無い</summary>
    ProjectionRequiresFields,

    /// <summary>結果型名が C# 識別子でない</summary>
    InvalidResultTypeName,

    /// <summary>結果型名が別のクエリと重複する</summary>
    DuplicateResultTypeName,

    /// <summary>結果型名が生成コードの型名（生成される型・ランタイム・単純名で使う外部の型）と衝突する</summary>
    ReservedResultTypeName,

    /// <summary>フィールド名が C# 識別子でない、または重複する</summary>
    InvalidFieldName,

    /// <summary>フィールド名が結果型名と同じ</summary>
    FieldNameEqualsResultType,

    /// <summary>簡易 DSL の射影に列を参照しないフィールドがある</summary>
    ProjectionDslRequiresColumns,
}

/// <summary>名前付きクエリの構造規則の違反 1 件</summary>
/// <param name="Kind">違反した規則</param>
/// <param name="Text">
/// 生成時診断と同じ文言（資源キー＋書式引数）。面ごとに言語を選んで描画する
/// （<see cref="QueryDiagnosticText.Format(CultureInfo?)"/>）
/// </param>
public sealed record QueryRuleViolation(QueryRuleKind Kind, QueryDiagnosticText Text)
{
    internal static QueryRuleViolation InvalidName(string queryName) =>
        new(QueryRuleKind.InvalidName, Of(nameof(Strings.CodeGen_Query_InvalidName), queryName));

    internal static QueryRuleViolation ReservedMethodName(string queryName, string methodName) =>
        new(
            QueryRuleKind.ReservedMethodName,
            Of(nameof(Strings.CodeGen_Query_ReservedMethodName), queryName, methodName)
        );

    internal static QueryRuleViolation DuplicateMethodName(
        string queryName,
        string methodName,
        string tableName
    ) =>
        new(
            QueryRuleKind.DuplicateMethodName,
            Of(nameof(Strings.CodeGen_Query_DuplicateMethodName), queryName, methodName, tableName)
        );

    internal static QueryRuleViolation ScalarRequiresType(string queryName) =>
        new(
            QueryRuleKind.ScalarRequiresType,
            Of(nameof(Strings.CodeGen_Query_ScalarRequiresType), queryName)
        );

    internal static QueryRuleViolation ScalarDslUnsupported(string queryName) =>
        new(
            QueryRuleKind.ScalarDslUnsupported,
            Of(nameof(Strings.CodeGen_Query_ScalarDslUnsupported), queryName)
        );

    internal static QueryRuleViolation PagingRequiresList(string queryName) =>
        new(
            QueryRuleKind.PagingRequiresList,
            Of(nameof(Strings.CodeGen_Query_PagingRequiresList), queryName)
        );

    internal static QueryRuleViolation OrderByRequiresList(string queryName) =>
        new(
            QueryRuleKind.OrderByRequiresList,
            Of(nameof(Strings.CodeGen_Query_OrderByRequiresList), queryName)
        );

    internal static QueryRuleViolation InvalidParameterName(
        string queryName,
        string parameterName
    ) =>
        new(
            QueryRuleKind.InvalidParameterName,
            Of(nameof(Strings.CodeGen_Query_InvalidParameterName), queryName, parameterName)
        );

    internal static QueryRuleViolation ReservedParameterName(
        string queryName,
        string parameterName
    ) =>
        new(
            QueryRuleKind.ReservedParameterName,
            Of(nameof(Strings.CodeGen_Query_ReservedParameterName), queryName, parameterName)
        );

    internal static QueryRuleViolation DuplicateParameterName(
        string queryName,
        string parameterName
    ) =>
        new(
            QueryRuleKind.DuplicateParameterName,
            Of(nameof(Strings.CodeGen_Query_DuplicateParameterName), queryName, parameterName)
        );

    internal static QueryRuleViolation ProjectionRequiresFields(string queryName) =>
        new(
            QueryRuleKind.ProjectionRequiresFields,
            Of(nameof(Strings.CodeGen_Query_ProjectionRequiresFields), queryName)
        );

    internal static QueryRuleViolation InvalidResultTypeName(
        string queryName,
        string resultTypeName
    ) =>
        new(
            QueryRuleKind.InvalidResultTypeName,
            Of(nameof(Strings.CodeGen_Query_InvalidResultTypeName), queryName, resultTypeName)
        );

    internal static QueryRuleViolation ReservedResultTypeName(
        string queryName,
        string resultTypeName
    ) =>
        new(
            QueryRuleKind.ReservedResultTypeName,
            Of(nameof(Strings.CodeGen_Query_ReservedResultTypeName), queryName, resultTypeName)
        );

    internal static QueryRuleViolation DuplicateResultTypeName(
        string queryName,
        string resultTypeName
    ) =>
        new(
            QueryRuleKind.DuplicateResultTypeName,
            Of(nameof(Strings.CodeGen_Query_DuplicateResultTypeName), queryName, resultTypeName)
        );

    internal static QueryRuleViolation InvalidFieldName(string queryName, string fieldName) =>
        new(
            QueryRuleKind.InvalidFieldName,
            Of(nameof(Strings.CodeGen_Query_InvalidFieldName), queryName, fieldName)
        );

    internal static QueryRuleViolation FieldNameEqualsResultType(
        string queryName,
        string fieldName
    ) =>
        new(
            QueryRuleKind.FieldNameEqualsResultType,
            Of(nameof(Strings.CodeGen_Query_FieldNameEqualsResultType), queryName, fieldName)
        );

    internal static QueryRuleViolation ProjectionDslRequiresColumns(string queryName) =>
        new(
            QueryRuleKind.ProjectionDslRequiresColumns,
            Of(nameof(Strings.CodeGen_Query_ProjectionDslRequiresColumns), queryName)
        );

    /// <summary>資源キーと書式引数から文言を作る</summary>
    private static QueryDiagnosticText Of(string resourceKey, params object?[] arguments) =>
        new(resourceKey, arguments);
}
