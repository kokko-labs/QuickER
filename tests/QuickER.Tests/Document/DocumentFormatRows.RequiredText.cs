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
    /// 場所を名指しして読込を断る。既定値（<c>NewTable</c>・<c>int</c> など）で埋めると、名前を勝手に付け直すのと
    /// 同じ書き換えになるうえ、そのまま上書き保存すれば手編集した JSON の元の内容が失われる。
    /// </para>
    /// <para>
    /// 断る形は 2 つ＝明示の <c>null</c>（行 <c>tryload-null-*</c>）とキーの欠落（行 <c>tryload-missing-*</c>）。
    /// 6 つの必須の文字列ごとに、同じ場所・同じ名指しの期待で 2 行を対にして作る（<see cref="RequiredTextPair"/>）。
    /// 表・列の Id も同じ検査で断る。Id の明示の <c>null</c> は型の不一致（InvalidJson）なので、対は
    /// キーの欠落と空の Guid（行 <c>tryload-empty-*</c>）にする（<see cref="RequiredValuePair"/>）。
    /// </para>
    /// <para>
    /// 説明・メモのような任意の文字列は空文字へ修復する（埋めても図の意味が変わらない）。
    /// 図ファイルは手編集でしか明示 <c>null</c> もキーの欠落も持ち得ないので、どちらの側も入口は JSON の直書き。
    /// </para>
    /// </remarks>
    private static IEnumerable<ScenarioRow<FormatCase>> RequiredTextRows() =>
        [
            .. RequiredTextPair(
                "table-name",
                "テーブル名",
                value => MinimalDiagramJson(tableName: value),
                message => message.Should().Contain("TableName").And.Contain("#1")
            ),
            .. RequiredTextPair(
                "column-name",
                "列名（テーブル名つき）",
                value => MinimalDiagramJson(columnName: value),
                message => message.Should().Contain("Name").And.Contain("Orders").And.Contain("#1")
            ),
            // 型を int で埋めると、その図から出る DDL・生成コードが手編集した JSON と別物になる。
            // 名前を勝手に付け直すのと同じ性質の書き換えなので、名前と同じ扱いで断る。
            .. RequiredTextPair(
                "column-data-type",
                "列の型（列名つき）",
                value => MinimalDiagramJson(dataType: value),
                message =>
                    message
                        .Should()
                        .Contain("DataType")
                        .And.Contain("OrderId")
                        .And.Contain("Orders")
            ),
            // 表・列の Id はレイアウト・リレーション・列ペア・一意制約・主キーの順序・クエリから引かれる。
            // 新しい Guid で埋めると、それらが黙って切れる（位置が既定へ戻り、GUI の保存でリレーションが消える）。
            .. RequiredValuePair(
                "entity-id",
                "表の Id（テーブル名つき）",
                EmptyGuidJson,
                value => MinimalDiagramJson(entityId: value),
                message => message.Should().Contain("\"Id\"").And.Contain("table 'Orders'")
            ),
            .. RequiredValuePair(
                "column-id",
                "列の Id（列名とテーブル名つき）",
                EmptyGuidJson,
                value => MinimalDiagramJson(columnId: value),
                message =>
                    message
                        .Should()
                        .Contain("\"Id\"")
                        .And.Contain("column 'OrderId'")
                        .And.Contain("table 'Orders'")
            ),
            .. RequiredTextPair(
                "query-name",
                "名前付きクエリの名前",
                value =>
                    MinimalDiagramJson(
                        extraSchemaMembers: $$"""
                        ,
                            "Queries": [ { {{Member("Name", value)}} } ]
                        """
                    ),
                message => message.Should().Contain("query #1")
            ),
            .. RequiredTextPair(
                "query-parameter-name",
                "名前付きクエリのパラメータ名",
                value =>
                    MinimalDiagramJson(
                        extraSchemaMembers: $$"""
                        ,
                            "Queries": [ { "Name": "FindById", "Parameters": [ { {{Member(
                            "Name",
                            value
                        )}} } ] } ]
                        """
                    ),
                message => message.Should().Contain("parameter #1").And.Contain("FindById")
            ),
            .. RequiredTextPair(
                "query-projection-field-name",
                "名前付きクエリの射影名",
                value =>
                    MinimalDiagramJson(
                        extraSchemaMembers: $$"""
                        ,
                            "Queries": [ { "Name": "FindById", "Fields": [ { {{Member(
                            "Name",
                            value
                        )}} } ] } ]
                        """
                    ),
                message => message.Should().Contain("projection field #1").And.Contain("FindById")
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
                            "Id": "44444444-0000-0000-0000-000000000001",
                            "TableName": "Orders",
                            "Description": null,
                            "Memo": null,
                            "Columns": [
                              { "Id": "44444444-0000-0000-0000-000000000002", "Name": "OrderId", "DataType": "int", "Description": null }
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

    /// <summary>
    /// 必須の文字列 1 つについて、明示の <c>null</c> の行（<c>tryload-null-{field}-rejected</c>）と
    /// キーの欠落の行（<c>tryload-missing-{field}-rejected</c>）を、同じ名指しの期待で対にして作る
    /// </summary>
    /// <param name="field">行 Id の中ほどに入る名前</param>
    /// <param name="subject">説明に入る対象の呼び名</param>
    /// <param name="json">対象の値（JSON の値の文字列。<c>null</c> ならキーごと書かない）から図の JSON を作る</param>
    /// <param name="expectMessage">断った理由の文言（場所の名指し）の表明</param>
    private static IEnumerable<ScenarioRow<FormatCase>> RequiredTextPair(
        string field,
        string subject,
        Func<string?, string> json,
        Action<string> expectMessage
    ) => RequiredValuePair(field, subject, "null", json, expectMessage);

    /// <summary>
    /// 必須の値 1 つについて、明示の空の値の行（<paramref name="explicitEmpty"/> が <c>null</c> なら
    /// <c>tryload-null-{field}-rejected</c>、ほかは <c>tryload-empty-{field}-rejected</c>）と、
    /// キーの欠落の行（<c>tryload-missing-{field}-rejected</c>）を、同じ名指しの期待で対にして作る
    /// </summary>
    /// <param name="field">行 Id の中ほどに入る名前</param>
    /// <param name="subject">説明に入る対象の呼び名</param>
    /// <param name="explicitEmpty">明示の空の値（JSON の値の文字列。文字列は <c>null</c>・Id は空の Guid）</param>
    /// <param name="json">対象の値（JSON の値の文字列。<c>null</c> ならキーごと書かない）から図の JSON を作る</param>
    /// <param name="expectMessage">断った理由の文言（場所の名指し）の表明</param>
    private static IEnumerable<ScenarioRow<FormatCase>> RequiredValuePair(
        string field,
        string subject,
        string explicitEmpty,
        Func<string?, string> json,
        Action<string> expectMessage
    ) =>
        [
            RequiredTextRow(
                explicitEmpty == "null"
                    ? $"tryload-null-{field}-rejected"
                    : $"tryload-empty-{field}-rejected",
                $"TryLoad: {subject}の明示の空の値（{explicitEmpty}）を拒否する",
                json(explicitEmpty),
                expectMessage
            ),
            RequiredTextRow(
                $"tryload-missing-{field}-rejected",
                $"TryLoad: {subject}のキーの欠落を拒否する（既定値で埋めない）",
                json(null),
                expectMessage
            ),
        ];

    /// <summary>空の Guid（JSON の値の文字列）</summary>
    private const string EmptyGuidJson = "\"00000000-0000-0000-0000-000000000000\"";

    private static ScenarioRow<FormatCase> RequiredTextRow(
        string id,
        string description,
        string json,
        Action<string> expectMessage
    ) =>
        Row(
            id,
            description,
            new FormatCase
            {
                Stage = FormatStage.TryLoad,
                Result = FormatResult.MissingRequiredText,
                Json = json,
                Expect = o =>
                {
                    o.Loaded.Should().BeNull();
                    o.Error.Should().Be(DocumentLoadError.MissingRequiredText);
                    expectMessage(o.Exception!.Message);
                },
            }
        );

    /// <summary>JSON のメンバー 1 つ（値が <c>null</c> ならキーごと書かない＝空文字列）</summary>
    /// <param name="name">キー</param>
    /// <param name="value">JSON の値の文字列（<c>"\"Orders\""</c>・<c>"null"</c> など）。<c>null</c> ならキーを書かない</param>
    private static string Member(string name, string? value) =>
        value is null ? string.Empty : $"\"{name}\": {value}";

    /// <summary>1 テーブル・1 列の最小の図を、指定の差し替えを施した JSON として組み立てる</summary>
    /// <remarks>
    /// 表と列の Id・テーブル名・列名・列の型は JSON の値の文字列で渡し、<c>null</c> を渡すとキーごと書かない。
    /// 表の Id は <c>IdOf(1)</c>、列の Id は <c>IdOf(2)</c>（クエリの参照先・参照の行が指すため固定）
    /// </remarks>
    private static string MinimalDiagramJson(
        string? tableName = "\"Orders\"",
        string? columnName = "\"OrderId\"",
        string? dataType = "\"int\"",
        string extraSchemaMembers = "",
        string? entityId = "\"44444444-0000-0000-0000-000000000001\"",
        string? columnId = "\"44444444-0000-0000-0000-000000000002\""
    ) =>
        $$"""
            {
              "Version": 1,
              "Schema": {
                "TargetDbms": "sqlserver",
                "Entities": [
                  {
                    {{Members(
                Member("Id", entityId),
                Member("TableName", tableName),
                "\"Columns\": [ { "
                    + Members(
                        Member("Id", columnId),
                        Member("Name", columnName),
                        Member("DataType", dataType)
                    )
                    + " } ]"
            )}}
                  }
                ]{{extraSchemaMembers}}
              }
            }
            """;

    /// <summary>空でないメンバーをカンマでつなぐ</summary>
    private static string Members(params string[] members) =>
        string.Join(", ", members.Where(member => member.Length > 0));
}
