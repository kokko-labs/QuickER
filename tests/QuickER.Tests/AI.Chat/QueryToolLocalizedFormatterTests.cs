using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI.Chat;
using QuickER.Mcp.Tools;
using QuickER.Model;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// <see cref="QueryToolLocalizedFormatter"/> が構造化結果（<see cref="QueryToolOutcome"/>）を、
/// アプリの表示言語に追従してテキスト化することを検証するテストクラス。
/// </summary>
/// <remarks>
/// カルチャ切替はプロセス共有の静的 <c>Strings.Culture</c> を変更せず、スレッドローカルの
/// <see cref="CultureInfo.CurrentUICulture"/> を try/finally で一時変更・復元して行う（同期テストのため
/// スレッド外へ漏れない）。<c>Strings.Culture</c> 未設定時に <c>GetString</c> が CurrentUICulture へ
/// フォールバックする挙動を利用する（tasks/lessons.md の「静的 Culture を変更しない」方針に沿う安全な方式）。
/// </remarks>
public class QueryToolLocalizedFormatterTests
{
    /// <summary>指定カルチャを CurrentUICulture に設定して関数を評価し、必ず元へ復元する</summary>
    private static T WithCulture<T>(string culture, Func<T> body)
    {
        var previousUi = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            return body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    /// <summary>set_query 成功（新規追加）が両言語で追加メッセージを返すことを検証する</summary>
    [Fact(DisplayName = "set_query 成功（追加）を日英で整形する")]
    public void FormatSetQuery_Added_LocalizesBothLanguages()
    {
        var outcome = new QueryToolOutcome
        {
            Status = QueryToolStatus.Success,
            WasUpdate = false,
            TableName = "Order",
            QueryName = "GetById",
            Returns = QueryReturnShape.Single,
            Implementation = QueryImplementationKind.Dsl,
        };

        WithCulture("en", () => QueryToolLocalizedFormatter.FormatSetQuery(outcome))
            .Should()
            .Be("Added query 'GetById' on table 'Order' (returns single, dsl).");

        WithCulture("ja", () => QueryToolLocalizedFormatter.FormatSetQuery(outcome))
            .Should()
            .Be("テーブル 'Order' にクエリ 'GetById' を追加しました（戻り形 single、dsl）。");
    }

    /// <summary>set_query 成功（更新）＋警告付きが両言語で更新メッセージと警告行を返すことを検証する</summary>
    [Fact(DisplayName = "set_query 成功（更新＋警告）を日英で整形する")]
    public void FormatSetQuery_UpdatedWithWarning_LocalizesBothLanguages()
    {
        var outcome = new QueryToolOutcome
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

        var en = WithCulture("en", () => QueryToolLocalizedFormatter.FormatSetQuery(outcome));
        en.Should().StartWith("Updated query 'Search' on table 'Order' (returns list, dsl).");
        en.Should()
            .Contain("Warning: Parameter 'keyword' is declared but not used in the condition.");

        var ja = WithCulture("ja", () => QueryToolLocalizedFormatter.FormatSetQuery(outcome));
        ja.Should().StartWith("テーブル 'Order' のクエリ 'Search' を更新しました");
        ja.Should()
            .Contain("警告: パラメータ 'keyword' は宣言されていますが条件で使用されていません。");
    }

    /// <summary>set_query 検証失敗が両言語で「変更していない」旨とエラー行を返すことを検証する</summary>
    [Fact(DisplayName = "set_query 検証失敗を日英で整形する")]
    public void FormatSetQuery_ValidationFailed_LocalizesBothLanguages()
    {
        var outcome = new QueryToolOutcome
        {
            Status = QueryToolStatus.ValidationFailed,
            TableName = "Order",
            QueryName = "Bad",
            Errors = [new QueryToolDiagnostic(QueryToolDiagnosticCode.ScalarRequiresScalarType)],
        };

        var en = WithCulture("en", () => QueryToolLocalizedFormatter.FormatSetQuery(outcome));
        en.Should()
            .Contain("Cannot set query 'Bad': validation failed. The diagram was not modified.");
        en.Should().Contain("returns=scalar requires 'scalar_type'.");

        var ja = WithCulture("ja", () => QueryToolLocalizedFormatter.FormatSetQuery(outcome));
        ja.Should()
            .Contain("クエリ 'Bad' を設定できません: 検証に失敗しました。図は変更していません。");
        ja.Should().Contain("returns=scalar には 'scalar_type' が必要です。");
    }

    /// <summary>共通失敗（テーブル不在）が両言語で整形されることを検証する</summary>
    [Fact(DisplayName = "テーブル不在の失敗を日英で整形する")]
    public void FormatSetQuery_TableNotFound_LocalizesBothLanguages()
    {
        var outcome = new QueryToolOutcome
        {
            Status = QueryToolStatus.TableNotFound,
            TableName = "Ghost",
        };

        WithCulture("en", () => QueryToolLocalizedFormatter.FormatSetQuery(outcome))
            .Should()
            .Be("Table 'Ghost' not found.");
        WithCulture("ja", () => QueryToolLocalizedFormatter.FormatSetQuery(outcome))
            .Should()
            .Be("テーブル 'Ghost' が見つかりません。");
    }

    /// <summary>remove_query の成功・不在が両言語で整形されることを検証する</summary>
    [Fact(DisplayName = "remove_query の成功・不在を日英で整形する")]
    public void FormatRemoveQuery_LocalizesBothLanguages()
    {
        var success = new QueryToolOutcome
        {
            Status = QueryToolStatus.Success,
            TableName = "Order",
            QueryName = "GetById",
        };

        WithCulture("en", () => QueryToolLocalizedFormatter.FormatRemoveQuery(success))
            .Should()
            .Be("Removed query 'GetById' from table 'Order'.");
        WithCulture("ja", () => QueryToolLocalizedFormatter.FormatRemoveQuery(success))
            .Should()
            .Be("テーブル 'Order' からクエリ 'GetById' を削除しました。");

        var notFound = new QueryToolOutcome
        {
            Status = QueryToolStatus.QueryNotFound,
            TableName = "Order",
            QueryName = "Missing",
        };

        WithCulture("en", () => QueryToolLocalizedFormatter.FormatRemoveQuery(notFound))
            .Should()
            .Be("Query 'Missing' not found on table 'Order'.");
        WithCulture("ja", () => QueryToolLocalizedFormatter.FormatRemoveQuery(notFound))
            .Should()
            .Be("テーブル 'Order' にクエリ 'Missing' が見つかりません。");
    }

    /// <summary>list_queries の一覧本体が両言語でヘッダ・グループ・クエリ要約を返すことを検証する</summary>
    [Fact(DisplayName = "list_queries の一覧を日英で整形する")]
    public void FormatListing_LocalizesBothLanguages()
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

        var en = WithCulture("en", () => QueryToolLocalizedFormatter.FormatListing(listing));
        en.Should().Contain("Queries: 1");
        en.Should().Contain("[Order]");
        en.Should().Contain("GetById");
        en.Should().Contain("condition: OrderId == @id");
        en.Should().Contain("parameters: id: int32");

        var ja = WithCulture("ja", () => QueryToolLocalizedFormatter.FormatListing(listing));
        ja.Should().Contain("クエリ数: 1");
        ja.Should().Contain("[Order]");
        ja.Should().Contain("GetById");
        ja.Should().Contain("条件: OrderId == @id");
        ja.Should().Contain("パラメータ: id: int32");
    }

    /// <summary>
    /// DSL パーサ由来の診断（描画前の <c>DetailText</c>）が、内蔵チャット面では UI 言語に追従して
    /// 描画されることを検証する（MCP 面は同じ診断を英語固定で描画する）。
    /// </summary>
    [Fact(DisplayName = "DSL 条件の診断は UI 言語に追従して整形される")]
    public void FormatSetQuery_ConditionDiagnostic_FollowsUiLanguage()
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
        var outcome = QueryToolCore.SetQuery(diagram, args);

        WithCulture("en", () => QueryToolLocalizedFormatter.FormatSetQuery(outcome))
            .Should()
            .Contain("The condition references column 'no_such_column', which does not exist");

        WithCulture("ja", () => QueryToolLocalizedFormatter.FormatSetQuery(outcome))
            .Should()
            .Contain(
                "条件式が参照する列 'no_such_column' はエンティティ 'orders' に存在しません。"
            );
    }
}
