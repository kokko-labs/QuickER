using System.Text.Json;
using QuickER.AI.Chat;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.TestSupport;
using Xunit.Sdk;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>名前付きクエリのツールの結果の整形器（と UI の言語）</summary>
public enum QueryToolFormatterKind
{
    /// <summary>MCP 面の英語の整形器（<see cref="QueryToolEnglishFormatter"/>・UI の言語に依らない）</summary>
    English,

    /// <summary>内蔵チャット面の整形器（<see cref="QueryToolLocalizedFormatter"/>）を UI の言語 en で</summary>
    LocalizedEn,

    /// <summary>内蔵チャット面の整形器（<see cref="QueryToolLocalizedFormatter"/>）を UI の言語 ja で</summary>
    LocalizedJa,
}

/// <summary>整形器の 3 つの入口（set_query・remove_query・list_queries）</summary>
/// <param name="SetQuery">set_query の結果の整形</param>
/// <param name="RemoveQuery">remove_query の結果の整形</param>
/// <param name="Listing">list_queries の一覧の整形</param>
internal sealed record QueryToolFormatter(
    Func<QueryToolOutcome, string> SetQuery,
    Func<QueryToolOutcome, string> RemoveQuery,
    Func<QueryListing, string> Listing
)
{
    /// <summary>英語の整形器</summary>
    public static QueryToolFormatter English { get; } =
        new(
            QueryToolEnglishFormatter.FormatSetQuery,
            QueryToolEnglishFormatter.FormatRemoveQuery,
            QueryToolEnglishFormatter.FormatListing
        );

    /// <summary>UI の言語に追従する整形器</summary>
    public static QueryToolFormatter Localized { get; } =
        new(
            QueryToolLocalizedFormatter.FormatSetQuery,
            QueryToolLocalizedFormatter.FormatRemoveQuery,
            QueryToolLocalizedFormatter.FormatListing
        );
}

/// <summary>整形の行の観測（応答の文と、同じ入力でもう一度整形する処理）</summary>
/// <param name="Text">応答の文</param>
/// <param name="Render">同じ整形器・同じ UI の言語でもう一度整形する（書式のカルチャを替えて比べる期待が使う）</param>
internal sealed record QueryFormatterObservation(string Text, Func<string> Render);

/// <summary>整形の行の期待 1 つ</summary>
/// <param name="Description">何を見るか（失敗の文に出す）</param>
/// <param name="Verify">合っていれば null・違えば理由</param>
internal sealed record QueryFormatterCheck(
    string Description,
    Func<QueryFormatterObservation, string?> Verify
);

/// <summary>整形の行 1 つ（入力の結果を 1 つの整形器で整形し、応答の文を照合する）</summary>
/// <param name="Id">行の Id</param>
/// <param name="Description">何を確かめるか（元のテストの DisplayName）</param>
/// <param name="Formatter">整形器（と UI の言語）</param>
/// <param name="Status">入力の結果の状態（網羅の計数が読む）</param>
/// <param name="Render">入力の結果を作って整形する</param>
/// <param name="Checks">期待（全行に共通の「改行は LF だけ」はランナーが足す）</param>
internal sealed record QueryFormatterRow(
    string Id,
    string Description,
    QueryToolFormatterKind Formatter,
    QueryToolStatus Status,
    Func<QueryToolFormatter, string> Render,
    IReadOnlyList<QueryFormatterCheck> Checks
)
{
    public override string ToString() => Id;
}

/// <summary>
/// 名前付きクエリのツールの結果の整形（MCP の英語・内蔵チャットの UI の言語）の表。流すのは <see cref="QueryToolFormatterTests"/>
/// </summary>
/// <remarks>
/// <para>
/// 行は 1 つの整形器で 1 つの結果を整形し、全文一致・前方一致・含む・含まないを書く。全行に共通で、応答に CR を含まない
/// （改行は LF だけ）ことを見る。入力の結果は元のテストのとおり組み立てるか、共有コアを実際に呼んで作る。
/// </para>
/// <para>
/// 結果の状態（<see cref="QueryToolStatus"/>）× 整形器の全組に行があることを網羅の Fact が確かめる。行の要らない組は
/// <see cref="Excluded"/> に理由つきで載せる。
/// </para>
/// </remarks>
internal static class QueryToolFormatterRows
{
    /// <summary>行の要らない組（状態・整形器・理由）</summary>
    public static IReadOnlyList<(
        QueryToolStatus Status,
        QueryToolFormatterKind Formatter,
        string Reason
    )> Excluded => [];

    /// <summary>全行</summary>
    public static IReadOnlyList<QueryFormatterRow> All { get; } = [.. Rows()];

    private static IEnumerable<QueryFormatterRow> Rows() =>
        [
            .. SetQueryRows(),
            .. RemoveQueryRows(),
            .. ListingRows(),
            .. CommonFailureRows(),
            .. LineEndingRows(),
        ];

    // ---------------- set_query ----------------

    private static IEnumerable<QueryFormatterRow> SetQueryRows()
    {
        var added = new QueryToolOutcome
        {
            Status = QueryToolStatus.Success,
            WasUpdate = false,
            TableName = "Order",
            QueryName = "GetById",
            Returns = QueryReturnShape.Single,
            Implementation = QueryImplementationKind.Dsl,
        };

        yield return Row(
                "formatter-set-query-added-localized-en",
                "set_query 成功（追加）を日英で整形する",
                QueryToolFormatterKind.LocalizedEn,
                f => f.SetQuery(added)
            )
            .Exact("Added query 'GetById' on table 'Order' (returns single, dsl).");
        yield return Row(
                "formatter-set-query-added-localized-ja",
                "set_query 成功（追加）を日英で整形する",
                QueryToolFormatterKind.LocalizedJa,
                f => f.SetQuery(added)
            )
            .Exact("テーブル 'Order' にクエリ 'GetById' を追加しました（戻り形 single、dsl）。");

        var updated = new QueryToolOutcome
        {
            Status = QueryToolStatus.Success,
            WasUpdate = true,
            TableName = "Order",
            QueryName = "Search",
            Returns = QueryReturnShape.List,
            Implementation = QueryImplementationKind.Dsl,
            Warnings =
            [
                new QueryToolDiagnostic(
                    QueryToolDiagnosticCode.ParameterUnusedInCondition,
                    Name: "keyword"
                ),
            ],
        };

        yield return Row(
                "formatter-set-query-updated-with-warning-localized-en",
                "set_query 成功（更新＋警告）を日英で整形する",
                QueryToolFormatterKind.LocalizedEn,
                f => f.SetQuery(updated)
            )
            .StartsWith("Updated query 'Search' on table 'Order' (returns list, dsl).")
            .Contains("Warning: Parameter 'keyword' is declared but not used in the condition.");
        yield return Row(
                "formatter-set-query-updated-with-warning-localized-ja",
                "set_query 成功（更新＋警告）を日英で整形する",
                QueryToolFormatterKind.LocalizedJa,
                f => f.SetQuery(updated)
            )
            .StartsWith("テーブル 'Order' のクエリ 'Search' を更新しました")
            .Contains("警告: パラメータ 'keyword' は宣言されていますが条件で使用されていません。");

        var validationFailed = new QueryToolOutcome
        {
            Status = QueryToolStatus.ValidationFailed,
            TableName = "Order",
            QueryName = "Bad",
            Errors = [new QueryToolDiagnostic(QueryToolDiagnosticCode.ScalarRequiresScalarType)],
        };

        yield return Row(
                "formatter-set-query-validation-failed-localized-en",
                "set_query 検証失敗を日英で整形する",
                QueryToolFormatterKind.LocalizedEn,
                f => f.SetQuery(validationFailed)
            )
            .Contains(
                "Cannot set query 'Bad': validation failed. The diagram was not modified.",
                "returns=scalar requires 'scalar_type'."
            );
        yield return Row(
                "formatter-set-query-validation-failed-localized-ja",
                "set_query 検証失敗を日英で整形する",
                QueryToolFormatterKind.LocalizedJa,
                f => f.SetQuery(validationFailed)
            )
            .Contains(
                "クエリ 'Bad' を設定できません: 検証に失敗しました。図は変更していません。",
                "returns=scalar には 'scalar_type' が必要です。"
            );

        var tableNotFound = new QueryToolOutcome
        {
            Status = QueryToolStatus.TableNotFound,
            TableName = "Ghost",
        };

        yield return Row(
                "formatter-set-query-table-not-found-localized-en",
                "テーブル不在の失敗を日英で整形する",
                QueryToolFormatterKind.LocalizedEn,
                f => f.SetQuery(tableNotFound)
            )
            .Exact("Table 'Ghost' not found.");
        yield return Row(
                "formatter-set-query-table-not-found-localized-ja",
                "テーブル不在の失敗を日英で整形する",
                QueryToolFormatterKind.LocalizedJa,
                f => f.SetQuery(tableNotFound)
            )
            .Exact("テーブル 'Ghost' が見つかりません。");

        // 表に無かった組（英語の整形器のテーブル不在）を足す
        yield return Row(
                "formatter-set-query-table-not-found-english",
                "テーブル不在の失敗を英語で整形する",
                QueryToolFormatterKind.English,
                f => f.SetQuery(tableNotFound)
            )
            .Exact("Table 'Ghost' not found.");

        yield return Row(
                "formatter-set-query-condition-diagnostic-follows-ui-language-localized-en",
                "DSL 条件の診断は UI 言語に追従して整形される",
                QueryToolFormatterKind.LocalizedEn,
                f => f.SetQuery(BrokenConditionOutcome())
            )
            .Contains("The condition references column 'no_such_column', which does not exist");
        yield return Row(
                "formatter-set-query-condition-diagnostic-follows-ui-language-localized-ja",
                "DSL 条件の診断は UI 言語に追従して整形される",
                QueryToolFormatterKind.LocalizedJa,
                f => f.SetQuery(BrokenConditionOutcome())
            )
            .Contains(
                "条件式が参照する列 'no_such_column' はエンティティ 'orders' に存在しません。"
            );
    }

    /// <summary>orders（customer_id PK）だけの図で、無い列を参照する条件の set_query を共有コアへ当てた結果</summary>
    private static QueryToolOutcome BrokenConditionOutcome()
    {
        var diagram = new ErDiagram();
        var entity = new Entity { TableName = "orders" };
        entity.Columns.Add(
            new Column
            {
                Name = "customer_id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        diagram.Entities.Add(entity);

        var args = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(
                new
                {
                    table_name = "orders",
                    query_name = "Broken",
                    returns = "list",
                    condition = "no_such_column = 1",
                }
            )
        );

        return QueryToolCore.SetQuery(diagram, args);
    }

    // ---------------- remove_query ----------------

    private static IEnumerable<QueryFormatterRow> RemoveQueryRows()
    {
        var removed = new QueryToolOutcome
        {
            Status = QueryToolStatus.Success,
            TableName = "Order",
            QueryName = "GetById",
        };
        var notFound = new QueryToolOutcome
        {
            Status = QueryToolStatus.QueryNotFound,
            TableName = "Order",
            QueryName = "Missing",
        };

        yield return Row(
                "formatter-remove-query-success-localized-en",
                "remove_query の成功・不在を日英で整形する",
                QueryToolFormatterKind.LocalizedEn,
                f => f.RemoveQuery(removed)
            )
            .Exact("Removed query 'GetById' from table 'Order'.");
        yield return Row(
                "formatter-remove-query-success-localized-ja",
                "remove_query の成功・不在を日英で整形する",
                QueryToolFormatterKind.LocalizedJa,
                f => f.RemoveQuery(removed)
            )
            .Exact("テーブル 'Order' からクエリ 'GetById' を削除しました。");
        yield return Row(
                "formatter-remove-query-not-found-localized-en",
                "remove_query の成功・不在を日英で整形する",
                QueryToolFormatterKind.LocalizedEn,
                f => f.RemoveQuery(notFound)
            )
            .Exact("Query 'Missing' not found on table 'Order'.");
        yield return Row(
                "formatter-remove-query-not-found-localized-ja",
                "remove_query の成功・不在を日英で整形する",
                QueryToolFormatterKind.LocalizedJa,
                f => f.RemoveQuery(notFound)
            )
            .Exact("テーブル 'Order' にクエリ 'Missing' が見つかりません。");

        // 表に無かった組（英語の整形器のクエリ不在）を足す
        yield return Row(
                "formatter-remove-query-not-found-english",
                "remove_query の不在を英語で整形する",
                QueryToolFormatterKind.English,
                f => f.RemoveQuery(notFound)
            )
            .Exact("Query 'Missing' not found on table 'Order'.");
    }

    // ---------------- list_queries ----------------

    private static IEnumerable<QueryFormatterRow> ListingRows()
    {
        var listing = new QueryListing(
            1,
            [
                new QueryListingGroup(
                    "Order",
                    [
                        new QueryListingItem(
                            "GetById",
                            QueryReturnShape.Single,
                            QueryImplementationKind.Dsl,
                            Description: "Find one order",
                            ScalarType: null,
                            Condition: "OrderId == @id",
                            SqlDialects: [],
                            Parameters:
                            [
                                new QueryListingParameter("id", "int32", false, null, false),
                            ],
                            OrderBy: [],
                            HasPaging: false,
                            ResultTypeName: null,
                            Fields: []
                        ),
                    ]
                ),
            ]
        );

        yield return Row(
                "formatter-listing-localized-en",
                "list_queries の一覧を日英で整形する",
                QueryToolFormatterKind.LocalizedEn,
                f => f.Listing(listing)
            )
            .Contains(
                "Queries: 1",
                "[Order]",
                "GetById",
                "condition: OrderId == @id",
                "parameters: id: int32"
            );
        yield return Row(
                "formatter-listing-localized-ja",
                "list_queries の一覧を日英で整形する",
                QueryToolFormatterKind.LocalizedJa,
                f => f.Listing(listing)
            )
            .Contains(
                "クエリ数: 1",
                "[Order]",
                "GetById",
                "条件: OrderId == @id",
                "パラメータ: id: int32"
            );

        yield return Row(
                "formatter-listing-is-culture-invariant-english",
                "名前付きクエリの一覧の応答は、書式のカルチャに依らずインバリアントと同じ",
                QueryToolFormatterKind.English,
                f =>
                {
                    var outcome = QueryToolCore.ListQueries(QueryFixtureDefinition.Build());

                    return f.Listing(
                        outcome.Listing
                            ?? throw new XunitException(
                                "一覧が null（ListQueries が一覧を返さない）"
                            )
                    );
                }
            )
            .CultureInvariant();
    }

    // ---------------- 共通の失敗（表に無かった組を足す） ----------------

    /// <summary>
    /// 引数の欠落・戻りの形・方式の不正（set_query の共通の失敗）を 3 つの整形器で整形する（元の表に無かった組）
    /// </summary>
    private static IEnumerable<QueryFormatterRow> CommonFailureRows()
    {
        var missingArgument = new QueryToolOutcome { Status = QueryToolStatus.MissingArgument };
        var invalidReturns = new QueryToolOutcome
        {
            Status = QueryToolStatus.InvalidReturns,
            TableName = "Order",
            QueryName = "X",
        };
        var invalidImplementation = new QueryToolOutcome
        {
            Status = QueryToolStatus.InvalidImplementation,
            TableName = "Order",
            QueryName = "X",
        };

        var cases = new (string Shape, QueryToolOutcome Outcome, string English, string Japanese)[]
        {
            (
                "missing-argument",
                missingArgument,
                "table_name and query_name are required.",
                "table_name と query_name は必須です。"
            ),
            (
                "invalid-returns",
                invalidReturns,
                "Invalid or missing 'returns' (must be one of: list, single, count, scalar, projection).",
                "'returns' の指定が無いか不正です（list / single / count / scalar / projection のいずれか）。"
            ),
            (
                "invalid-implementation",
                invalidImplementation,
                "Invalid 'implementation' (must be one of: dsl, sql, manual).",
                "'implementation' の指定が不正です（dsl / sql / manual のいずれか）。"
            ),
        };

        foreach (var (shape, outcome, english, japanese) in cases)
        {
            var description = $"set_query の共通の失敗（{outcome.Status}）を整形する";

            yield return Row(
                    $"formatter-set-query-{shape}-english",
                    description,
                    QueryToolFormatterKind.English,
                    f => f.SetQuery(outcome)
                )
                .Exact(english);
            yield return Row(
                    $"formatter-set-query-{shape}-localized-en",
                    description,
                    QueryToolFormatterKind.LocalizedEn,
                    f => f.SetQuery(outcome)
                )
                .Exact(english);
            yield return Row(
                    $"formatter-set-query-{shape}-localized-ja",
                    description,
                    QueryToolFormatterKind.LocalizedJa,
                    f => f.SetQuery(outcome)
                )
                .Exact(japanese);
        }
    }

    // ---------------- 改行 ----------------

    /// <summary>
    /// 警告つきの成功・失敗・一覧の応答（共有コアを実際に呼んで作る）。改行が LF だけなことは全行に共通の表明が見る
    /// </summary>
    private static IEnumerable<QueryFormatterRow> LineEndingRows()
    {
        const string englishDescription =
            "set_query・list_queries の英語の応答は、成否や警告の有無に依らず改行を LF にそろえる";
        const string localizedDescription =
            "set_query・list_queries のチャットの応答は、成否や警告の有無に依らず改行を LF にそろえる";

        foreach (
            var (formatter, suffix, description, withAmount) in new[]
            {
                (QueryToolFormatterKind.English, "english", englishDescription, true),
                (QueryToolFormatterKind.LocalizedEn, "localized-en", localizedDescription, false),
                (QueryToolFormatterKind.LocalizedJa, "localized-ja", localizedDescription, false),
            }
        )
        {
            yield return Row(
                $"formatter-line-ending-set-query-with-warning-{suffix}",
                description,
                formatter,
                f => f.SetQuery(LineEndingOutcomes(withAmount).WithWarning),
                QueryToolStatus.Success
            );
            yield return Row(
                $"formatter-line-ending-set-query-failed-{suffix}",
                description,
                formatter,
                f => f.SetQuery(LineEndingOutcomes(withAmount).Failed),
                QueryToolStatus.ValidationFailed
            );
            yield return Row(
                $"formatter-line-ending-listing-{suffix}",
                description,
                formatter,
                f => f.Listing(LineEndingOutcomes(withAmount).Listing),
                QueryToolStatus.Success
            );
        }
    }

    /// <summary>
    /// orders（order_id PK・customer_id。<paramref name="withAmount"/> なら amount も）の図へ、警告つきで成功する
    /// set_query と失敗する set_query を当て、その後の一覧を取る（英語の元のテストは amount あり・チャットの元のテストは無し）
    /// </summary>
    private static (
        QueryToolOutcome WithWarning,
        QueryToolOutcome Failed,
        QueryListing Listing
    ) LineEndingOutcomes(bool withAmount)
    {
        var orders = new Entity { TableName = "orders" };
        orders.Columns.Add(
            new Column
            {
                Name = "order_id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        orders.Columns.Add(new Column { Name = "customer_id", DataType = "int" });

        if (withAmount)
        {
            orders.Columns.Add(new Column { Name = "amount", DataType = "decimal(10,2)" });
        }

        var diagram = new ErDiagram { TargetDbms = "sqlite" };
        diagram.Entities.Add(orders);

        var withWarning = QueryToolCore.SetQuery(
            diagram,
            JsonSerializer.SerializeToElement(
                new
                {
                    table_name = "orders",
                    query_name = "ByCustomer",
                    returns = "list",
                    parameters = new[]
                    {
                        new { name = "customerId", source_column = "customer_id" },
                    },
                    condition = "customer_id = 1",
                }
            )
        );
        var failed = QueryToolCore.SetQuery(
            diagram,
            JsonSerializer.SerializeToElement(
                new
                {
                    table_name = "orders",
                    query_name = "Broken",
                    returns = "list",
                    condition = "no_such_column = 1 AND",
                }
            )
        );

        if (withWarning.Warnings.Count == 0)
        {
            throw new XunitException("警告つきの成功を作れていない");
        }

        if (failed.Success)
        {
            throw new XunitException("失敗を作れていない");
        }

        return (withWarning, failed, QueryToolCore.ListQueries(diagram).Listing!);
    }

    // ---------------- 組み立て ----------------

    private static RowBuilder Row(
        string id,
        string description,
        QueryToolFormatterKind formatter,
        Func<QueryToolFormatter, string> render,
        QueryToolStatus? status = null
    ) => new(id, description, formatter, render, status);

    /// <summary>行の組み立て（期待を足し、暗黙の変換で行にする）</summary>
    private sealed class RowBuilder(
        string id,
        string description,
        QueryToolFormatterKind formatter,
        Func<QueryToolFormatter, string> render,
        QueryToolStatus? status
    )
    {
        private readonly List<QueryFormatterCheck> _checks = [];

        public RowBuilder Exact(string expected) =>
            Add(
                $"text == \"{expected}\"",
                observation =>
                    string.Equals(observation.Text, expected, StringComparison.Ordinal)
                        ? null
                        : "全文が違う"
            );

        public RowBuilder StartsWith(string prefix) =>
            Add(
                $"text starts with \"{prefix}\"",
                observation =>
                    observation.Text.StartsWith(prefix, StringComparison.Ordinal)
                        ? null
                        : "始まりが違う"
            );

        public RowBuilder Contains(params string[] fragments)
        {
            foreach (var fragment in fragments)
            {
                Add(
                    $"text contains \"{fragment}\"",
                    observation =>
                        observation.Text.Contains(fragment, StringComparison.Ordinal)
                            ? null
                            : "含まない"
                );
            }

            return this;
        }

        /// <summary>書式のカルチャ（<see cref="CultureScope.Cultures"/>）を替えて整形しても、インバリアントと同じ</summary>
        public RowBuilder CultureInvariant() =>
            Add(
                "text is culture invariant",
                observation =>
                    CultureScope.FindDifferences(observation.Render) is { Count: > 0 } differences
                        ? string.Join("; ", differences)
                        : null
            );

        private RowBuilder Add(string description, Func<QueryFormatterObservation, string?> verify)
        {
            _checks.Add(new QueryFormatterCheck(description, verify));
            return this;
        }

        public static implicit operator QueryFormatterRow(RowBuilder builder) =>
            new(
                builder.Id,
                builder.Description,
                builder.Formatter,
                builder.Status ?? builder.StatusFromRender(),
                builder.RenderWith,
                [.. builder._checks]
            );

        private string Id => id;

        private string Description => description;

        private QueryToolFormatterKind Formatter => formatter;

        private QueryToolStatus? Status => status;

        private Func<QueryToolFormatter, string> RenderWith => render;

        /// <summary>状態を宣言していない行は、整形器へ渡した結果の状態を読む（呼び出しを記録する整形器で 1 回だけ流す）</summary>
        private QueryToolStatus StatusFromRender()
        {
            QueryToolStatus? seen = null;
            var recorder = new QueryToolFormatter(
                outcome =>
                {
                    seen = outcome.Status;
                    return string.Empty;
                },
                outcome =>
                {
                    seen = outcome.Status;
                    return string.Empty;
                },
                _ =>
                {
                    seen = QueryToolStatus.Success;
                    return string.Empty;
                }
            );
            render(recorder);

            return seen
                ?? throw new InvalidOperationException($"行 {id} の整形が結果を渡していない");
        }
    }
}
