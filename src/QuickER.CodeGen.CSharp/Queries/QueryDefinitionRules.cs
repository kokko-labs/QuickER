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
/// <c>set_query</c>・GUI のクエリ定義ダイアログ）は <see cref="Validate"/> を呼ぶ。生成器は判定の順序と打ち切り
/// （名前の規則に違反したらそのクエリの残りを見ない等）を自分の流れで持つので、<see cref="Validate"/> も同じ順序・
/// 同じ打ち切りで組み立てる（同じ定義に対して同じ違反を同じ文言で返す）。
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
            "Query",
            "QueryBySqlAsync",
            "ExecuteSqlAsync",
            "ExecuteScalarSqlAsync",
            "QueryProjectionBySqlAsync",
            "CheckUniquenessAsync",
        };

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
    /// パラメータ名に使えない名前か（大文字小文字まで一致で判定する）
    /// </summary>
    /// <remarks>
    /// <c>Query</c>: 生成メソッドの本体は契約メンバー <c>Query()</c> の呼び出しで組み立てるため、同名の引数は
    /// メソッドグループを隠してコンパイル不能になる（小文字の <c>query</c> は合法）。
    /// <c>await</c>: 生 SQL の単一・件数の生成メソッドは async メソッドで、その中では <c>await</c> を識別子に使えない。
    /// ほかの文脈キーワードは、生成されるどのメソッドの形でもパラメータ名として通る
    /// （<c>QueryReservedParameterNameTests</c> が全文脈キーワード × 全戻り形 × 実装方式を実コンパイルで固定する）。
    /// </remarks>
    public static bool IsReservedParameterName(string name) =>
        string.Equals(name, "Query", StringComparison.Ordinal)
        || string.Equals(name, "await", StringComparison.Ordinal);

    /// <summary>生成メソッドが自分で持つ引数の名前（宣言したパラメータと重なると重複になる）</summary>
    /// <param name="hasPaging">ページングを持つか（持てば <c>take</c> / <c>skip</c> が加わる）</param>
    public static IEnumerable<string> ImplicitParameterNames(bool hasPaging) =>
        hasPaging ? ["cancellationToken", "take", "skip"] : ["cancellationToken"];

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

    /// <summary>
    /// クエリ定義が生成で必ずエラーになる構造規則に違反していないかを調べ、違反を生成器と同じ順序・同じ打ち切りで返す
    /// </summary>
    /// <param name="entity">クエリの対象テーブル（重複の文言にテーブル名を載せる）</param>
    /// <param name="query">調べる定義</param>
    /// <param name="otherQueries">
    /// 図のほかのクエリ（<paramref name="query"/> 自身を含めない。置き換える前の同じクエリも含めない）。
    /// 同じテーブルのクエリとはメソッド名の重複を、すべてのクエリとは射影の結果型名の重複を見る
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
        IEnumerable<QueryDefinition> otherQueries
    )
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(otherQueries);

        var others = otherQueries.ToList();
        var violations = new List<QueryRuleViolation>();

        // ---- メソッド名（違反があればこのクエリの残りは見ない＝生成器と同じ打ち切り） ----
        var methodName = ToMethodName(query.Name);

        if (methodName is null)
        {
            return [QueryRuleViolation.InvalidName(query.Name)];
        }

        if (ReservedMethodNames.Contains(methodName))
        {
            return [QueryRuleViolation.ReservedMethodName(query.Name, methodName)];
        }

        var siblingMethodNames = others
            .Where(other => other.EntityId == query.EntityId)
            .Select(other => ToMethodName(other.Name))
            .OfType<string>()
            .Where(name => !ReservedMethodNames.Contains(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (siblingMethodNames.Contains(methodName))
        {
            return
            [
                QueryRuleViolation.DuplicateMethodName(query.Name, methodName, entity.TableName),
            ];
        }

        // ---- 戻り形と実装方式の整合 ----
        if (query.Returns == QueryReturnShape.Scalar && string.IsNullOrWhiteSpace(query.ScalarType))
        {
            violations.Add(QueryRuleViolation.ScalarRequiresType(query.Name));
        }

        if (IsScalarDslConflict(query.Returns, query.Implementation))
        {
            violations.Add(QueryRuleViolation.ScalarDslUnsupported(query.Name));
        }

        if (query.HasPaging && !SupportsPaging(query.Returns))
        {
            violations.Add(QueryRuleViolation.PagingRequiresList(query.Name));
        }

        if (query.OrderBy.Count > 0 && !SupportsOrderBy(query.Returns))
        {
            violations.Add(QueryRuleViolation.OrderByRequiresList(query.Name));
        }

        // ---- パラメータ名 ----
        var seenParameterNames = new HashSet<string>(
            ImplicitParameterNames(query.HasPaging),
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var parameter in query.Parameters)
        {
            if (!IsValidIdentifier(parameter.Name))
            {
                violations.Add(QueryRuleViolation.InvalidParameterName(query.Name, parameter.Name));
            }
            else if (IsReservedParameterName(parameter.Name))
            {
                violations.Add(
                    QueryRuleViolation.ReservedParameterName(query.Name, parameter.Name)
                );
            }
            else if (!seenParameterNames.Add(parameter.Name))
            {
                violations.Add(
                    QueryRuleViolation.DuplicateParameterName(query.Name, parameter.Name)
                );
            }
        }

        if (query.Returns == QueryReturnShape.Projection)
        {
            AddProjectionViolations(query, others, violations);
        }

        return violations;
    }

    /// <summary>射影（結果型名・フィールド名・DSL の列参照）の規則を調べる（生成器と同じ順序・打ち切り）</summary>
    private static void AddProjectionViolations(
        QueryDefinition query,
        IReadOnlyList<QueryDefinition> others,
        List<QueryRuleViolation> violations
    )
    {
        var resultTypeName = query.ResultTypeName?.Trim() ?? string.Empty;

        if (query.Fields.Count == 0 || resultTypeName.Length == 0)
        {
            violations.Add(QueryRuleViolation.ProjectionRequiresFields(query.Name));
            return;
        }

        if (!IsValidIdentifier(resultTypeName))
        {
            violations.Add(QueryRuleViolation.InvalidResultTypeName(query.Name, resultTypeName));
            return;
        }

        var otherResultTypeNames = others
            .Where(other =>
                other.Returns == QueryReturnShape.Projection && ToMethodName(other.Name) is not null
            )
            .Select(other => other.ResultTypeName?.Trim() ?? string.Empty)
            .Where(IsValidIdentifier)
            .ToHashSet(StringComparer.Ordinal);

        if (otherResultTypeNames.Contains(resultTypeName))
        {
            violations.Add(QueryRuleViolation.DuplicateResultTypeName(query.Name, resultTypeName));
            return;
        }

        var seenFieldNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in query.Fields)
        {
            if (!IsValidIdentifier(field.Name) || !seenFieldNames.Add(field.Name))
            {
                violations.Add(QueryRuleViolation.InvalidFieldName(query.Name, field.Name));
            }
            else if (string.Equals(field.Name, resultTypeName, StringComparison.Ordinal))
            {
                violations.Add(
                    QueryRuleViolation.FieldNameEqualsResultType(query.Name, field.Name)
                );
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

    /// <summary>パラメータ名が <c>Query</c>（契約メンバーを隠す）</summary>
    ReservedParameterName,

    /// <summary>パラメータ名が重複する（暗黙の引数名を含む）</summary>
    DuplicateParameterName,

    /// <summary>射影なのに結果型名かフィールドが無い</summary>
    ProjectionRequiresFields,

    /// <summary>結果型名が C# 識別子でない</summary>
    InvalidResultTypeName,

    /// <summary>結果型名が別のクエリと重複する</summary>
    DuplicateResultTypeName,

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
