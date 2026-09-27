using System.IO;
using AwesomeAssertions;
using QuickER.Documents;

namespace QuickER.Tests.Document;

/// <summary>
/// 読込時の必須文字列検査（<see cref="JsonStorageService.TryLoad"/>）と、
/// 任意の文字列の修復（<c>Normalize</c>）を検証するテストクラス。
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
public sealed class JsonStorageServiceRequiredTextTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-requiredtext-" + Guid.NewGuid().ToString("N")
    );

    public JsonStorageServiceRequiredTextTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch
        {
            // 後始末失敗はテスト結果に影響させない
        }
    }

    /// <summary>手編集を模した JSON をそのまま書き出してパスを返す</summary>
    private string WriteJson(string json)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);

        return path;
    }

    /// <summary>1 テーブル・1 列の最小の図を、指定の差し替えを施した JSON として書き出す</summary>
    private string WriteMinimalDiagram(
        string tableName = "\"Orders\"",
        string columnName = "\"OrderId\"",
        string dataType = "\"int\"",
        string extraSchemaMembers = ""
    ) =>
        WriteJson(
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
            """
        );

    [Fact(DisplayName = "TryLoad: テーブル名の明示 null を拒否する")]
    public void TryLoad_NullTableName_IsRejected()
    {
        var path = WriteMinimalDiagram(tableName: "null");

        JsonStorageService
            .TryLoad(path, out var document, out var error, out var exception)
            .Should()
            .BeFalse();

        document.Should().BeNull();
        error.Should().Be(DocumentLoadError.MissingRequiredText);
        exception!.Message.Should().Contain("TableName").And.Contain("#1");
    }

    [Fact(DisplayName = "TryLoad: 列名の明示 null をテーブル名つきで拒否する")]
    public void TryLoad_NullColumnName_IsRejected()
    {
        var path = WriteMinimalDiagram(columnName: "null");

        JsonStorageService
            .TryLoad(path, out var document, out var error, out var exception)
            .Should()
            .BeFalse();

        document.Should().BeNull();
        error.Should().Be(DocumentLoadError.MissingRequiredText);
        exception!.Message.Should().Contain("Name").And.Contain("Orders").And.Contain("#1");
    }

    /// <summary>列の型の明示 null も拒否する（既定値で埋めない）ことを検証する</summary>
    /// <remarks>
    /// 型を <c>int</c> で埋めると、その図から出る DDL・生成コードが手編集した JSON と別物になる。
    /// 名前を勝手に付け直すのと同じ性質の書き換えなので、名前と同じ扱いで断る。
    /// </remarks>
    [Fact(DisplayName = "TryLoad: 列の型の明示 null を列名つきで拒否する")]
    public void TryLoad_NullColumnDataType_IsRejected()
    {
        var path = WriteMinimalDiagram(dataType: "null");

        JsonStorageService
            .TryLoad(path, out var document, out var error, out var exception)
            .Should()
            .BeFalse();

        document.Should().BeNull();
        error.Should().Be(DocumentLoadError.MissingRequiredText);
        exception!
            .Message.Should()
            .Contain("DataType")
            .And.Contain("OrderId")
            .And.Contain("Orders");
    }

    [Fact(
        DisplayName = "TryLoad: 名前付きクエリの名前・パラメータ名・射影名の明示 null を拒否する"
    )]
    public void TryLoad_NullQueryNames_AreRejected()
    {
        var queryName = WriteMinimalDiagram(
            extraSchemaMembers: """
            ,
                "Queries": [ { "Name": null } ]
            """
        );
        var parameterName = WriteMinimalDiagram(
            extraSchemaMembers: """
            ,
                "Queries": [ { "Name": "FindById", "Parameters": [ { "Name": null } ] } ]
            """
        );
        var fieldName = WriteMinimalDiagram(
            extraSchemaMembers: """
            ,
                "Queries": [ { "Name": "FindById", "Fields": [ { "Name": null } ] } ]
            """
        );

        JsonStorageService
            .TryLoad(queryName, out _, out var queryError, out var queryException)
            .Should()
            .BeFalse();
        queryError.Should().Be(DocumentLoadError.MissingRequiredText);
        queryException!.Message.Should().Contain("query #1");

        JsonStorageService
            .TryLoad(parameterName, out _, out var parameterError, out var parameterException)
            .Should()
            .BeFalse();
        parameterError.Should().Be(DocumentLoadError.MissingRequiredText);
        parameterException!.Message.Should().Contain("parameter #1").And.Contain("FindById");

        JsonStorageService
            .TryLoad(fieldName, out _, out var fieldError, out var fieldException)
            .Should()
            .BeFalse();
        fieldError.Should().Be(DocumentLoadError.MissingRequiredText);
        fieldException!.Message.Should().Contain("projection field #1").And.Contain("FindById");
    }

    /// <summary>説明・メモの明示 null は拒否せず空文字へ修復することを検証する</summary>
    [Fact(DisplayName = "TryLoad: 説明・メモの明示 null は空文字へ修復して読み込む")]
    public void TryLoad_NullOptionalText_IsRepaired()
    {
        var path = WriteJson(
            """
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
            """
        );

        JsonStorageService
            .TryLoad(path, out var document, out var error, out _)
            .Should()
            .BeTrue("任意の文字列は埋めても図の意味が変わらない");

        error.Should().Be(DocumentLoadError.None);

        var entity = document!.Schema.Entities[0];
        entity.Description.Should().BeEmpty();
        entity.Memo.Should().BeEmpty();
        entity.Columns[0].Description.Should().BeEmpty();
        document.Schema.Queries[0].Description.Should().BeEmpty();
    }

    /// <summary>必須文字列の検査は Id 重複より先に走ることを検証する</summary>
    /// <remarks>
    /// 名前が <c>null</c> のままでは、Id 重複の報告が「どのテーブルどうしか」を名指しできない。
    /// </remarks>
    [Fact(DisplayName = "TryLoad: 必須文字列の欠落は Id 重複より先に報告する")]
    public void TryLoad_MissingRequiredText_IsReportedBeforeDuplicateId()
    {
        var duplicated = Guid.NewGuid();
        var path = WriteJson(
            $$"""
            {
              "Version": 1,
              "Schema": {
                "TargetDbms": "sqlserver",
                "Entities": [
                  { "Id": "{{duplicated}}", "TableName": null, "Columns": [] },
                  { "Id": "{{duplicated}}", "TableName": "Orders", "Columns": [] }
                ]
              }
            }
            """
        );

        JsonStorageService
            .TryLoad(path, out _, out var error, out var exception)
            .Should()
            .BeFalse();

        error.Should().Be(DocumentLoadError.MissingRequiredText);
        exception!.Message.Should().Contain("TableName");
    }
}
