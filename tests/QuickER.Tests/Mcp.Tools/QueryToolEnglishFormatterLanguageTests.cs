using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using QuickER.Mcp.Tools;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// MCP 面のフォーマッタ（<see cref="QueryToolEnglishFormatter"/>）が、DSL パーサ・生 SQL アナライザ由来の
/// 診断まで含めて UI 言語に依らず英語で出力することを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 外部 AI エージェント向け stdio MCP サーバはヘッドレス実行＝英語固定が正本。診断が resx 経由で
/// ローカライズされる都合上、日本語 OS では英語応答に日本語診断が混ざる回帰が起きやすいためガードする。
/// </para>
/// <para>
/// カルチャ切替はプロセス共有の静的 <c>Strings.Culture</c> を変更せず、スレッドローカルの
/// <see cref="CultureInfo.CurrentUICulture"/> を try/finally で一時変更・復元して行う。
/// </para>
/// </remarks>
public class QueryToolEnglishFormatterLanguageTests
{
    /// <summary>CJK 文字の検出パターン（ErDiagramToolCatalogEnglishGuardTests と同じ範囲）</summary>
    private static readonly Regex CjkPattern = new("[　-鿿＀-￯]", RegexOptions.Compiled);

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

    /// <summary>単一テーブル（orders）だけの図を作る</summary>
    private static ErDiagram CreateDiagram()
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

        return diagram;
    }

    /// <summary>匿名オブジェクトを引数 JSON へ変換する</summary>
    private static JsonElement Args(object value) =>
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value));

    /// <summary>DSL 条件の検証エラーが、日本語 UI でも英語で出力されることを検証する</summary>
    [Fact(DisplayName = "DSL 条件の診断は日本語 UI でも英語で出力される")]
    public void FormatSetQuery_ConditionDiagnostic_IsEnglishUnderJapaneseUi()
    {
        var text = WithCulture(
            "ja",
            () =>
                QueryToolEnglishFormatter.FormatSetQuery(
                    QueryToolCore.SetQuery(
                        CreateDiagram(),
                        Args(
                            new
                            {
                                table_name = "orders",
                                query_name = "Broken",
                                returns = "list",
                                condition = "no_such_column = 1",
                            }
                        )
                    )
                )
        );

        text.Should().Contain("Condition: ");
        text.Should()
            .Contain("The condition references column 'no_such_column', which does not exist");
        CjkPattern.IsMatch(text).Should().BeFalse("MCP 面の応答は英語固定です: " + text);
    }

    /// <summary>生 SQL の警告が、日本語 UI でも英語で出力されることを検証する</summary>
    [Fact(DisplayName = "生 SQL の診断は日本語 UI でも英語で出力される")]
    public void FormatSetQuery_RawSqlDiagnostic_IsEnglishUnderJapaneseUi()
    {
        var text = WithCulture(
            "ja",
            () =>
                QueryToolEnglishFormatter.FormatSetQuery(
                    QueryToolCore.SetQuery(
                        CreateDiagram(),
                        Args(
                            new
                            {
                                table_name = "orders",
                                query_name = "ListSql",
                                returns = "list",
                                implementation = "sql",
                                sql = new Dictionary<string, string>
                                {
                                    ["sqlite"] = "SELECT * FROM orders",
                                },
                                parameters = new[] { new { name = "unused", type = "int32" } },
                            }
                        )
                    )
                )
        );

        text.Should().Contain("Warning: sqlite: ");
        text.Should().Contain("The declared parameter '@unused' is not used in the raw SQL.");
        CjkPattern.IsMatch(text).Should().BeFalse("MCP 面の応答は英語固定です: " + text);
    }
}
