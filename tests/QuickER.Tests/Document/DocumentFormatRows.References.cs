using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Document;

internal static partial class DocumentFormatRows
{
    /// <summary>
    /// 読込時の参照の検査（図に無い表・列を指す参照を断る）と、断らない参照
    /// </summary>
    /// <remarks>
    /// <para>
    /// リレーションの両端・列ペアの列・一意制約の構成列は、普通の操作では切れない（表の削除はリレーションを、
    /// 列の削除は列ペアと一意制約を一緒に消す）。切れていれば手編集で、そのまま開くと GUI がリレーション・列ペアを
    /// 黙って読み飛ばし、上書き保存で消える。図に無い Id（行 <c>tryload-dangling-*</c>）とキーの欠落＝空の Guid
    /// （行 <c>tryload-missing-*</c>）を、場所と Id を名指しして断る。
    /// </para>
    /// <para>
    /// 主キーの順序の古い Id・クエリの参照先と列参照・レイアウトの余分なキー・リレーション／一意制約／クエリの Id の欠落は
    /// 断らない（普通の操作か公開版の図で生じる・誰も引かない）。行 <c>tryload-*-is-kept</c> と
    /// <c>tryload-missing-unreferenced-ids-*</c> が、読めて値が保たれることを固定する。
    /// </para>
    /// </remarks>
    private static IEnumerable<ScenarioRow<FormatCase>> ReferenceRows() =>
        [
            Row(
                "tryload-reference-diagram-loads",
                "TryLoad: 参照がすべて解ける図（リレーション・列ペア・一意制約）は読める",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.Loaded,
                    Json = ReferenceDiagramJson(),
                    Expect = o =>
                    {
                        var relationship = o.Loaded!.Schema.Relationships.Should()
                            .ContainSingle()
                            .Which;
                        relationship.ColumnPairs.Should().ContainSingle();
                        o.Loaded.Schema.Entities[1].UniqueConstraints.Should().ContainSingle();
                    },
                }
            ),
            BrokenReferenceRow(
                "tryload-missing-relationship-source-rejected",
                "TryLoad: リレーションの親の表の Id のキーの欠落を拒否する",
                ReferenceDiagramJson(source: null),
                message =>
                    message
                        .Should()
                        .Contain("\"SourceEntityId\" is missing")
                        .And.Contain("relationship 'FK_Orders_Customers'")
            ),
            BrokenReferenceRow(
                "tryload-dangling-relationship-source-rejected",
                "TryLoad: リレーションの親の表が図に無い Id を指すのを拒否する",
                ReferenceDiagramJson(source: Quoted(IdOf(9))),
                message =>
                    message
                        .Should()
                        .Contain("\"SourceEntityId\"")
                        .And.Contain("relationship 'FK_Orders_Customers'")
                        .And.Contain(IdOf(9).ToString())
                        .And.Contain("not in the diagram")
            ),
            BrokenReferenceRow(
                "tryload-missing-relationship-target-rejected",
                "TryLoad: リレーションの子の表の Id のキーの欠落を拒否する",
                ReferenceDiagramJson(target: null),
                message =>
                    message
                        .Should()
                        .Contain("\"TargetEntityId\" is missing")
                        .And.Contain("relationship 'FK_Orders_Customers'")
            ),
            // 名前の無いリレーションは、図の中の番号で名指しする
            BrokenReferenceRow(
                "tryload-dangling-relationship-target-rejected",
                "TryLoad: リレーションの子の表が図に無い Id を指すのを拒否する（名前の無いリレーションは番号で名指し）",
                ReferenceDiagramJson(target: Quoted(IdOf(9)), constraintName: null),
                message =>
                    message
                        .Should()
                        .Contain("\"TargetEntityId\"")
                        .And.Contain("relationship #1")
                        .And.Contain(IdOf(9).ToString())
            ),
            BrokenReferenceRow(
                "tryload-dangling-column-pair-source-rejected",
                "TryLoad: 列ペアの親の列が図に無い Id を指すのを拒否する",
                ReferenceDiagramJson(pairSource: Quoted(IdOf(9))),
                message =>
                    message
                        .Should()
                        .Contain("\"SourceColumnId\"")
                        .And.Contain("column pair #1 of relationship 'FK_Orders_Customers'")
                        .And.Contain(IdOf(9).ToString())
                        .And.Contain("table 'Customers'")
            ),
            BrokenReferenceRow(
                "tryload-dangling-column-pair-target-rejected",
                "TryLoad: 列ペアの子の列が図に無い Id を指すのを拒否する",
                ReferenceDiagramJson(pairTarget: Quoted(IdOf(9))),
                message =>
                    message
                        .Should()
                        .Contain("\"TargetColumnId\"")
                        .And.Contain(IdOf(9).ToString())
                        .And.Contain("table 'Orders'")
            ),
            BrokenReferenceRow(
                "tryload-missing-column-pair-target-rejected",
                "TryLoad: 列ペアの子の列の Id のキーの欠落を拒否する",
                ReferenceDiagramJson(pairTarget: null),
                message =>
                    message
                        .Should()
                        .Contain("\"TargetColumnId\" is missing")
                        .And.Contain("column pair #1")
            ),
            // 親の列は親の表（Source）の列でなければならない。子の表の列を指すペアは、GUI が黙って捨てる
            BrokenReferenceRow(
                "tryload-column-pair-column-of-other-table-rejected",
                "TryLoad: 列ペアの親の列が、図にはあるが親の表の列でないのを拒否する",
                ReferenceDiagramJson(pairSource: Quoted(IdOf(4))),
                message =>
                    message
                        .Should()
                        .Contain("\"SourceColumnId\"")
                        .And.Contain(IdOf(4).ToString())
                        .And.Contain("table 'Customers'")
            ),
            BrokenReferenceRow(
                "tryload-dangling-unique-constraint-column-rejected",
                "TryLoad: 一意制約の構成列がその表に無い列を指すのを拒否する",
                ReferenceDiagramJson(uniqueColumn: Quoted(IdOf(2))),
                message =>
                    message
                        .Should()
                        .Contain("\"ColumnIds\"")
                        .And.Contain("unique constraint 'UQ_Orders_OrderNo' of table 'Orders'")
                        .And.Contain(IdOf(2).ToString())
            ),
            // 列の削除・主キーの切り替えは主キーの順序の古い Id を掃除しない仕様（読み手が無視する）。
            // 断ると普通に保存した図が開けなくなる
            Row(
                "tryload-stale-primary-key-column-id-is-kept",
                "TryLoad: 主キーの順序の、どの列も指さない Id は断らずにそのまま読む",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.Loaded,
                    Json = ReferenceDiagramJson(
                        ordersExtraMembers: $$"""
                        "PrimaryKeyColumnIds": [ "{{IdOf(9)}}", "{{IdOf(4)}}" ],
                        """
                    ),
                    Expect = o =>
                        o.Loaded!.Schema.Entities[1]
                            .PrimaryKeyColumnIds.Should()
                            .Equal(IdOf(9), IdOf(4)),
                }
            ),
            // クエリの参照先の表は v0.2.0 までの版で保存した図に残りうる。列参照は列の削除で切れる。
            // どちらも見えて直せる（定義ダイアログ・生成の警告・MCP の list_queries / remove_query）ので断らない
            Row(
                "tryload-dangling-query-references-are-kept",
                "TryLoad: クエリの参照先の表と列参照（パラメータ・並び順・射影）が切れていても断らずにそのまま読む",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.Loaded,
                    Json = ReferenceDiagramJson(
                        extraSchemaMembers: $$"""
                        ,
                            "Queries": [
                              {
                                "EntityId": "{{IdOf(9)}}",
                                "Name": "FindById",
                                "Parameters": [ { "Name": "id", "SourceColumnId": "{{IdOf(8)}}" } ],
                                "OrderBy": [ { "ColumnId": "{{IdOf(7)}}" } ],
                                "Fields": [ { "Name": "F", "SourceColumnId": "{{IdOf(6)}}" } ]
                              }
                            ]
                        """
                    ),
                    Expect = o =>
                    {
                        var query = o.Loaded!.Schema.Queries.Should().ContainSingle().Which;
                        query.EntityId.Should().Be(IdOf(9));
                        query.Parameters[0].SourceColumnId.Should().Be(IdOf(8));
                        query.OrderBy[0].ColumnId.Should().Be(IdOf(7));
                        query.Fields[0].SourceColumnId.Should().Be(IdOf(6));
                    },
                }
            ),
            // レイアウトの、表を指さないキーは図の意味を持たない（GUI の保存で消えるだけ）
            Row(
                "tryload-orphan-layout-key-is-kept",
                "TryLoad: レイアウトの、どの表も指さないキーは断らずにそのまま読む",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.Loaded,
                    Json = ReferenceDiagramJson(
                        layout: $$"""
                        ,
                          "Layout": { "{{IdOf(9)}}": { "X": 10 } }
                        """
                    ),
                    Expect = o => o.Loaded!.Layout.Should().ContainKey(IdOf(9)),
                }
            ),
            // リレーション・一意制約・クエリの Id は図の中で誰も引かないので、キーが無ければ新しい Guid で読む
            Row(
                "tryload-missing-unreferenced-ids-get-new-ids",
                "TryLoad: リレーション・一意制約・クエリの Id のキーの欠落は断らず、新しい Id で読む",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.Loaded,
                    Json = ReferenceDiagramJson(
                        withUnreferencedIds: false,
                        extraSchemaMembers: $$"""
                        ,
                            "Queries": [ { "EntityId": "{{IdOf(3)}}", "Name": "FindById" } ]
                        """
                    ),
                    Expect = o =>
                    {
                        var schema = o.Loaded!.Schema;
                        schema.Relationships[0].Id.Should().NotBe(Guid.Empty);
                        schema.Entities[1].UniqueConstraints[0].Id.Should().NotBe(Guid.Empty);
                        schema.Queries[0].Id.Should().NotBe(Guid.Empty);
                    },
                }
            ),
        ];

    /// <summary>図に無い表・列を指す参照で断る行（結果・種類・名指しの文言を表明する）</summary>
    private static ScenarioRow<FormatCase> BrokenReferenceRow(
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
                Result = FormatResult.BrokenReference,
                Json = json,
                Expect = o =>
                {
                    o.Loaded.Should().BeNull();
                    o.Error.Should().Be(DocumentLoadError.BrokenReference);
                    expectMessage(o.Exception!.Message);
                },
            }
        );

    /// <summary>Guid を JSON の文字列の値にする</summary>
    private static string Quoted(Guid id) => $"\"{id}\"";

    /// <summary>
    /// 親 Customers（Id は <c>IdOf(1)</c>・列 CustomerId は <c>IdOf(2)</c>）と子 Orders（<c>IdOf(3)</c>・列 OrderNo は
    /// <c>IdOf(4)</c>・列 CustomerId は <c>IdOf(5)</c>）、その間のリレーション 1 本（列ペア 1 つ）と、
    /// Orders の一意制約 1 つ（構成列 OrderNo）の図を、指定の差し替えを施した JSON として組み立てる
    /// </summary>
    /// <remarks>参照の値は JSON の値の文字列で渡し、<c>null</c> を渡すとキーごと書かない</remarks>
    private static string ReferenceDiagramJson(
        string? source = "\"44444444-0000-0000-0000-000000000001\"",
        string? target = "\"44444444-0000-0000-0000-000000000003\"",
        string? pairSource = "\"44444444-0000-0000-0000-000000000002\"",
        string? pairTarget = "\"44444444-0000-0000-0000-000000000005\"",
        string? uniqueColumn = "\"44444444-0000-0000-0000-000000000004\"",
        string? constraintName = "\"FK_Orders_Customers\"",
        string ordersExtraMembers = "",
        string extraSchemaMembers = "",
        string layout = "",
        bool withUnreferencedIds = true
    ) =>
        $$"""
            {
              "Version": 1,
              "Schema": {
                "TargetDbms": "sqlserver",
                "Entities": [
                  {
                    "Id": "{{IdOf(1)}}",
                    "TableName": "Customers",
                    "Columns": [ { "Id": "{{IdOf(2)}}", "Name": "CustomerId", "DataType": "int" } ]
                  },
                  {
                    "Id": "{{IdOf(3)}}",
                    "TableName": "Orders",
                    {{ordersExtraMembers}}
                    "Columns": [
                      { "Id": "{{IdOf(4)}}", "Name": "OrderNo", "DataType": "int" },
                      { "Id": "{{IdOf(5)}}", "Name": "CustomerId", "DataType": "int" }
                    ],
                    "UniqueConstraints": [
                      { {{Members(
                withUnreferencedIds ? Member("Id", Quoted(IdOf(11))) : string.Empty,
                Member("Name", "\"UQ_Orders_OrderNo\""),
                $"\"ColumnIds\": [ {uniqueColumn} ]"
            )}} }
                    ]
                  }
                ],
                "Relationships": [
                  { {{Members(
                withUnreferencedIds ? Member("Id", Quoted(IdOf(12))) : string.Empty,
                Member("ConstraintName", constraintName),
                Member("SourceEntityId", source),
                Member("TargetEntityId", target),
                "\"ColumnPairs\": [ { "
                    + Members(
                        Member("SourceColumnId", pairSource),
                        Member("TargetColumnId", pairTarget)
                    )
                    + " } ]"
            )}} }
                ]{{extraSchemaMembers}}
              }{{layout}}
            }
            """;
}
