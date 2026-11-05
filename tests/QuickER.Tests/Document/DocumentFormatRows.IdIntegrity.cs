using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Document;

internal static partial class DocumentFormatRows
{
    /// <summary>
    /// 読込時の Id 重複検査
    /// </summary>
    /// <remarks>
    /// 重複 Id を持つ図を読み込むと、上書き保存（<c>ToDocument</c> の <c>ToDictionary</c>）が毎回失敗し、
    /// 自動保存は例外を握り潰すため一度も成功しない＝作業内容を保存する手段が両方とも消える。
    /// 図ファイルは手編集でしか重複 Id を持ち得ないため、修復せず読込を拒否する
    /// （拒否メッセージにはテーブル名と重複した Id を含める＝手で直すための情報）。
    /// </remarks>
    private static IEnumerable<ScenarioRow<FormatCase>> IdIntegrityRows() =>
        [
            Row(
                "tryload-duplicate-entity-id-rejected",
                "TryLoad: エンティティ Id の重複を拒否する",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.DuplicateId,
                    Prior = () =>
                        SchemaOnly(
                            new ErDiagram
                            {
                                Entities =
                                {
                                    IdEntity(IdOf(1), "Orders", IdOf(2), "OrderId"),
                                    IdEntity(IdOf(1), "OrderLines", IdOf(3), "LineId"),
                                },
                            }
                        ),
                    Expect = o =>
                    {
                        o.Loaded.Should().BeNull();
                        o.Error.Should().Be(DocumentLoadError.DuplicateId);
                        o.Exception!.Message.Should().Contain(IdOf(1).ToString());
                        o.Exception.Message.Should().Contain("Orders").And.Contain("OrderLines");
                    },
                }
            ),
            Row(
                "tryload-duplicate-column-id-within-entity-rejected",
                "TryLoad: 同一テーブル内の列 Id 重複を拒否する",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.DuplicateId,
                    Prior = () =>
                    {
                        var entity = IdEntity(IdOf(1), "Orders", IdOf(2), "OrderId");
                        entity.Columns.Add(new Column { Id = IdOf(2), Name = "CustomerId" });

                        return SchemaOnly(new ErDiagram { Entities = { entity } });
                    },
                    Expect = o =>
                    {
                        o.Loaded.Should().BeNull();
                        o.Error.Should().Be(DocumentLoadError.DuplicateId);
                        o.Exception!.Message.Should().Contain(IdOf(2).ToString());
                        // テーブル間の重複とは別の検査（別の文言）で報告する＝どちらが欠けても残りでは代替できない
                        o.Exception.Message.Should().Contain("in table 'Orders'");
                        o.Exception.Message.Should().Contain("OrderId").And.Contain("CustomerId");
                    },
                }
            ),
            Row(
                "tryload-duplicate-column-id-across-entities-rejected",
                "TryLoad: エンティティをまたぐ列 Id 重複を拒否する",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.DuplicateId,
                    Prior = () =>
                        SchemaOnly(
                            new ErDiagram
                            {
                                Entities =
                                {
                                    IdEntity(IdOf(1), "Orders", IdOf(3), "OrderId"),
                                    IdEntity(IdOf(2), "OrderLines", IdOf(3), "LineId"),
                                },
                            }
                        ),
                    Expect = o =>
                    {
                        o.Loaded.Should().BeNull();
                        o.Error.Should().Be(DocumentLoadError.DuplicateId);
                        o.Exception!.Message.Should().Contain(IdOf(3).ToString());
                        o.Exception.Message.Should().Contain("Orders").And.Contain("OrderLines");
                    },
                }
            ),
            Row(
                "tryload-no-duplicates-loads",
                "TryLoad: Id が重複しない図は読み込める",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.Loaded,
                    Prior = () =>
                        SchemaOnly(
                            new ErDiagram
                            {
                                Entities =
                                {
                                    IdEntity(IdOf(1), "Orders", IdOf(2), "OrderId"),
                                    IdEntity(IdOf(3), "OrderLines", IdOf(4), "LineId"),
                                },
                            }
                        ),
                    Expect = o =>
                    {
                        o.Error.Should().Be(DocumentLoadError.None);
                        o.Loaded!.Schema.Entities.Should().HaveCount(2);
                    },
                }
            ),
            // 参照も辞書化も無く実害が無いため、検査対象を広げない判断の固定
            Row(
                "tryload-duplicate-non-key-ids-accepted",
                "TryLoad: 一意制約・クエリの Id 重複は拒否しない",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.Loaded,
                    Prior = () =>
                    {
                        var source = IdEntity(IdOf(1), "Orders", IdOf(2), "OrderId");
                        var target = IdEntity(IdOf(3), "OrderLines", IdOf(4), "LineId");
                        var duplicated = IdOf(5);

                        source.UniqueConstraints.Add(
                            new UniqueConstraint
                            {
                                Id = duplicated,
                                ColumnIds = { source.Columns[0].Id },
                            }
                        );
                        source.UniqueConstraints.Add(
                            new UniqueConstraint
                            {
                                Id = duplicated,
                                ColumnIds = { source.Columns[0].Id },
                            }
                        );

                        return SchemaOnly(
                            new ErDiagram
                            {
                                Entities = { source, target },
                                Relationships =
                                {
                                    new Relationship
                                    {
                                        Id = duplicated,
                                        SourceEntityId = source.Id,
                                        TargetEntityId = target.Id,
                                    },
                                },
                                Queries =
                                {
                                    new QueryDefinition { Id = duplicated, Name = "ByOrderId" },
                                    new QueryDefinition { Id = duplicated, Name = "ByLineId" },
                                },
                            }
                        );
                    },
                    Expect = o =>
                    {
                        o.Error.Should().Be(DocumentLoadError.None);
                        o.Loaded!.Schema.Entities[0].UniqueConstraints.Should().HaveCount(2);
                        o.Loaded.Schema.Queries.Should().HaveCount(2);
                    },
                }
            ),
            // リレーションの Id は辞書のキーに使われ、重複すると MCP・内蔵チャットのツールが例外になる
            Row(
                "tryload-duplicate-relationship-id-with-constraint-names-rejected",
                "TryLoad: 制約名のあるリレーションの Id 重複を、制約名を名指しして拒否する",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.DuplicateId,
                    Prior = () =>
                    {
                        var source = IdEntity(IdOf(1), "Orders", IdOf(2), "OrderId");
                        var target = IdEntity(IdOf(3), "OrderLines", IdOf(4), "LineId");

                        return SchemaOnly(
                            new ErDiagram
                            {
                                Entities = { source, target },
                                Relationships =
                                {
                                    new Relationship
                                    {
                                        Id = IdOf(5),
                                        SourceEntityId = source.Id,
                                        TargetEntityId = target.Id,
                                        ConstraintName = "FK_OrderLines_Orders",
                                    },
                                    new Relationship
                                    {
                                        Id = IdOf(5),
                                        SourceEntityId = source.Id,
                                        TargetEntityId = target.Id,
                                        ConstraintName = "FK_OrderLines_Orders_Backup",
                                    },
                                },
                            }
                        );
                    },
                    Expect = o =>
                    {
                        o.Loaded.Should().BeNull();
                        o.Error.Should().Be(DocumentLoadError.DuplicateId);
                        o.Exception!.Message.Should().Contain(IdOf(5).ToString());
                        o.Exception.Message.Should()
                            .Contain("relationship 'FK_OrderLines_Orders'")
                            .And.Contain("relationship 'FK_OrderLines_Orders_Backup'");
                    },
                }
            ),
            // 図に無いエンティティ Id はテーブル名の代わりに Id のまま書く
            Row(
                "tryload-duplicate-relationship-id-without-constraint-names-rejected",
                "TryLoad: 制約名の無いリレーションの Id 重複を、親と子のテーブル名で名指しして拒否する",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.DuplicateId,
                    Prior = () =>
                    {
                        var source = IdEntity(IdOf(1), "Orders", IdOf(2), "OrderId");
                        var target = IdEntity(IdOf(3), "OrderLines", IdOf(4), "LineId");

                        return SchemaOnly(
                            new ErDiagram
                            {
                                Entities = { source, target },
                                Relationships =
                                {
                                    new Relationship
                                    {
                                        Id = IdOf(5),
                                        SourceEntityId = source.Id,
                                        TargetEntityId = target.Id,
                                    },
                                    new Relationship
                                    {
                                        Id = IdOf(5),
                                        SourceEntityId = source.Id,
                                        TargetEntityId = IdOf(6),
                                    },
                                },
                            }
                        );
                    },
                    Expect = o =>
                    {
                        o.Error.Should().Be(DocumentLoadError.DuplicateId);
                        o.Exception!.Message.Should().Contain(IdOf(5).ToString());
                        o.Exception.Message.Should()
                            .Contain("relationship from table 'Orders' to table 'OrderLines'")
                            .And.Contain($"relationship from table 'Orders' to table '{IdOf(6)}'");
                    },
                }
            ),
        ];

    /// <summary>Id の検査の行が使う固定の Id（番号ごとに別の値）</summary>
    private static Guid IdOf(int number) => new($"44444444-0000-0000-0000-{number:D12}");

    /// <summary>指定スキーマの図（レイアウトなし）</summary>
    private static DiagramDocument SchemaOnly(ErDiagram schema) =>
        new() { Schema = schema, Layout = null };

    /// <summary>指定 Id・名前の列を 1 本だけ持つエンティティを作る</summary>
    private static Entity IdEntity(
        Guid entityId,
        string tableName,
        Guid columnId,
        string columnName
    ) =>
        new()
        {
            Id = entityId,
            TableName = tableName,
            Columns =
            {
                new Column { Id = columnId, Name = columnName },
            },
        };
}
