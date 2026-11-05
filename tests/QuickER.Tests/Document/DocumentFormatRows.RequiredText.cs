using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Document;

internal static partial class DocumentFormatRows
{
    /// <summary>
    /// 読込時の必須文字列検査と、任意の文字列の修復
    /// </summary>
    /// <remarks>
    /// <para>
    /// テーブル名・列名・列の型のような「推測で埋めると図の意味が変わるもの」は修復せず、
    /// 場所を名指しして読込を断る。既定値（空文字・<c>int</c>）で埋めると、名前を勝手に付け直すのと
    /// 同じ書き換えになるうえ、そのまま上書き保存すれば手編集した JSON の元の内容が失われる。
    /// </para>
    /// <para>
    /// 説明・メモのような任意の文字列は空文字へ修復する（埋めても図の意味が変わらない）。
    /// 図ファイルは手編集でしか明示 <c>null</c> を持ち得ないので、どちらの側も入口は JSON の直書き。
    /// </para>
    /// </remarks>
    private static IEnumerable<ScenarioRow<FormatCase>> RequiredTextRows() =>
        [
            Row(
                "tryload-null-table-name-rejected",
                "TryLoad: テーブル名の明示 null を拒否する",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.MissingRequiredText,
                    Json = MinimalDiagramJson(tableName: "null"),
                    Expect = o =>
                    {
                        o.Loaded.Should().BeNull();
                        o.Error.Should().Be(DocumentLoadError.MissingRequiredText);
                        o.Exception!.Message.Should().Contain("TableName").And.Contain("#1");
                    },
                }
            ),
            Row(
                "tryload-null-column-name-rejected",
                "TryLoad: 列名の明示 null をテーブル名つきで拒否する",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.MissingRequiredText,
                    Json = MinimalDiagramJson(columnName: "null"),
                    Expect = o =>
                    {
                        o.Loaded.Should().BeNull();
                        o.Error.Should().Be(DocumentLoadError.MissingRequiredText);
                        o.Exception!.Message.Should()
                            .Contain("Name")
                            .And.Contain("Orders")
                            .And.Contain("#1");
                    },
                }
            ),
            // 型を int で埋めると、その図から出る DDL・生成コードが手編集した JSON と別物になる。
            // 名前を勝手に付け直すのと同じ性質の書き換えなので、名前と同じ扱いで断る。
            Row(
                "tryload-null-column-data-type-rejected",
                "TryLoad: 列の型の明示 null を列名つきで拒否する",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.MissingRequiredText,
                    Json = MinimalDiagramJson(dataType: "null"),
                    Expect = o =>
                    {
                        o.Loaded.Should().BeNull();
                        o.Error.Should().Be(DocumentLoadError.MissingRequiredText);
                        o.Exception!.Message.Should()
                            .Contain("DataType")
                            .And.Contain("OrderId")
                            .And.Contain("Orders");
                    },
                }
            ),
            Row(
                "tryload-null-query-name-rejected",
                "TryLoad: 名前付きクエリの名前・パラメータ名・射影名の明示 null を拒否する（クエリ名）",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.MissingRequiredText,
                    Json = MinimalDiagramJson(
                        extraSchemaMembers: """
                        ,
                            "Queries": [ { "Name": null } ]
                        """
                    ),
                    Expect = o =>
                    {
                        o.Error.Should().Be(DocumentLoadError.MissingRequiredText);
                        o.Exception!.Message.Should().Contain("query #1");
                    },
                }
            ),
            Row(
                "tryload-null-query-parameter-name-rejected",
                "TryLoad: 名前付きクエリの名前・パラメータ名・射影名の明示 null を拒否する（パラメータ名）",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.MissingRequiredText,
                    Json = MinimalDiagramJson(
                        extraSchemaMembers: """
                        ,
                            "Queries": [ { "Name": "FindById", "Parameters": [ { "Name": null } ] } ]
                        """
                    ),
                    Expect = o =>
                    {
                        o.Error.Should().Be(DocumentLoadError.MissingRequiredText);
                        o.Exception!.Message.Should()
                            .Contain("parameter #1")
                            .And.Contain("FindById");
                    },
                }
            ),
            Row(
                "tryload-null-query-projection-field-name-rejected",
                "TryLoad: 名前付きクエリの名前・パラメータ名・射影名の明示 null を拒否する（射影名）",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.MissingRequiredText,
                    Json = MinimalDiagramJson(
                        extraSchemaMembers: """
                        ,
                            "Queries": [ { "Name": "FindById", "Fields": [ { "Name": null } ] } ]
                        """
                    ),
                    Expect = o =>
                    {
                        o.Error.Should().Be(DocumentLoadError.MissingRequiredText);
                        o.Exception!.Message.Should()
                            .Contain("projection field #1")
                            .And.Contain("FindById");
                    },
                }
            ),
            Row(
                "tryload-null-optional-text-repaired-to-empty",
                "TryLoad: 説明・メモの明示 null は空文字へ修復して読み込む",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.Loaded,
                    Json = """
                    {
                      "Version": 1,
                      "Schema": {
                        "TargetDbms": "sqlserver",
                        "Entities": [
                          {
                            "TableName": "Orders",
                            "Description": null,
                            "Memo": null,
                            "Columns": [
                              { "Name": "OrderId", "DataType": "int", "Description": null }
                            ]
                          }
                        ],
                        "Queries": [ { "Name": "FindById", "Description": null } ]
                      }
                    }
                    """,
                    Expect = o =>
                    {
                        // 任意の文字列は埋めても図の意味が変わらない（読めたこと自体は Result が照合する）
                        o.Error.Should().Be(DocumentLoadError.None);

                        var entity = o.Loaded!.Schema.Entities[0];
                        entity.Description.Should().BeEmpty();
                        entity.Memo.Should().BeEmpty();
                        entity.Columns[0].Description.Should().BeEmpty();
                        o.Loaded.Schema.Queries[0].Description.Should().BeEmpty();
                    },
                }
            ),
            // 名前が null のままでは、Id 重複の報告が「どのテーブルどうしか」を名指しできない
            Row(
                "tryload-missing-required-text-reported-before-duplicate-id",
                "TryLoad: 必須文字列の欠落は Id 重複より先に報告する",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.MissingRequiredText,
                    Json = $$"""
                    {
                      "Version": 1,
                      "Schema": {
                        "TargetDbms": "sqlserver",
                        "Entities": [
                          { "Id": "{{IdOf(1)}}", "TableName": null, "Columns": [] },
                          { "Id": "{{IdOf(1)}}", "TableName": "Orders", "Columns": [] }
                        ]
                      }
                    }
                    """,
                    Expect = o =>
                    {
                        o.Error.Should().Be(DocumentLoadError.MissingRequiredText);
                        o.Exception!.Message.Should().Contain("TableName");
                    },
                }
            ),
        ];

    /// <summary>1 テーブル・1 列の最小の図を、指定の差し替えを施した JSON として組み立てる</summary>
    private static string MinimalDiagramJson(
        string tableName = "\"Orders\"",
        string columnName = "\"OrderId\"",
        string dataType = "\"int\"",
        string extraSchemaMembers = ""
    ) =>
        $$"""
            {
              "Version": 1,
              "Schema": {
                "TargetDbms": "sqlserver",
                "Entities": [
                  {
                    "TableName": {{tableName}},
                    "Columns": [
                      { "Name": {{columnName}}, "DataType": {{dataType}} }
                    ]
                  }
                ]{{extraSchemaMembers}}
              }
            }
            """;
}
