using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// DBML の引用識別子（<c>"列 名"</c>）の書き出しと取込を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 囲まないと、空白を含むテーブル名は取込側の <c>Table</c> 行の書式に一致せず<b>テーブルごと落ち</b>、
/// 空白を含む列名は先頭トークンで切れて残りが型名へ混ざる。SQL Server 由来の
/// <c>[Order Details]</c> のような名前で現実に起きる。
/// </para>
/// <para>
/// 型は名前と別規則で、空白または角括弧を含むときだけ囲む（DBML の規約どおり
/// <c>"double precision"</c>・<c>varchar(255)</c> は素のまま）。
/// </para>
/// </remarks>
public class DbmlQuotedIdentifierTests
{
    /// <summary>1 テーブル 2 列の最小の図を組み立てる</summary>
    private static ErDiagram BuildDiagram(
        string tableName,
        string columnName,
        string dataType,
        string keyColumnName = "id"
    ) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = tableName,
                    Columns =
                    [
                        new Column
                        {
                            Name = keyColumnName,
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = columnName,
                            DataType = dataType,
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

    [Theory(DisplayName = "DBML 往復: 空白や記号を含むテーブル名・列名が保たれる")]
    [InlineData("Order Details", "Order Date")]
    [InlineData("order-details", "order-date")]
    [InlineData("Order.Details", "unit price")]
    public void RoundTrip_NamesWithSpacesOrSymbols_ArePreserved(string tableName, string columnName)
    {
        var dbml = DbmlExporter.Build(BuildDiagram(tableName, columnName, "date"));

        var restored = DbmlImporter.Parse(dbml);

        var entity = restored.Entities.Should().ContainSingle().Subject;
        entity.TableName.Should().Be(tableName);
        entity.Columns.Select(column => column.Name).Should().Contain(columnName);
        entity.Columns.Single(column => column.Name == columnName).DataType.Should().Be("date");
    }

    [Fact(DisplayName = "DBML 往復: 名前に含まれる二重引用符も保たれる")]
    public void RoundTrip_NameWithDoubleQuote_IsPreserved()
    {
        const string TableName = @"we""ird";
        const string ColumnName = @"a""b\c";

        var restored = DbmlImporter.Parse(
            DbmlExporter.Build(BuildDiagram(TableName, ColumnName, "int"))
        );

        var entity = restored.Entities.Should().ContainSingle().Subject;
        entity.TableName.Should().Be(TableName);
        entity.Columns.Select(column => column.Name).Should().Contain(ColumnName);
    }

    [Fact(DisplayName = "DBML 出力: 英数字とアンダースコアだけの名前は囲まない")]
    public void Export_PlainNames_AreNotQuoted()
    {
        var dbml = DbmlExporter.Build(BuildDiagram("order_details", "order_date", "date"));

        dbml.Should().Contain("Table order_details {");
        dbml.Should().Contain("order_date date [");
        dbml.Should().NotContain("\"");
    }

    [Fact(DisplayName = "DBML 出力: 日本語の名前は従来どおり囲まない")]
    public void Export_JapaneseNames_AreNotQuoted()
    {
        var dbml = DbmlExporter.Build(BuildDiagram("受注明細", "受注日", "date"));

        dbml.Should().Contain("Table 受注明細 {");
        dbml.Should().Contain("受注日 date [");
        dbml.Should().NotContain("\"");
    }

    /// <summary>
    /// 空白を含む名前でも、リレーションの端点（<c>Ref:</c> 行）が往復する。
    /// </summary>
    [Fact(DisplayName = "DBML 往復: 空白を含む名前のリレーションも列対応ごと保たれる")]
    public void RoundTrip_RelationshipWithQuotedNames_KeepsColumnPairs()
    {
        var parentKey = new Column
        {
            Name = "Order Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parent = new Entity { TableName = "Order Header", Columns = [parentKey] };
        var childKey = new Column
        {
            Name = "Line Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var childRef = new Column
        {
            Name = "Order Id",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var child = new Entity { TableName = "Order Details", Columns = [childKey, childRef] };

        var diagram = new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(parentKey.Id, childRef.Id)],
                    ConstraintName = "FK_OrderDetails_OrderHeader",
                },
            ],
        };

        var restored = DbmlImporter.Parse(DbmlExporter.Build(diagram));

        var restoredParent = restored.Entities.Single(e => e.TableName == "Order Header");
        var restoredChild = restored.Entities.Single(e => e.TableName == "Order Details");
        var relationship = restored.Relationships.Should().ContainSingle().Subject;

        relationship.ConstraintName.Should().Be("FK_OrderDetails_OrderHeader");

        var pair = relationship.ColumnPairs.Should().ContainSingle().Subject;
        pair.SourceColumnId.Should()
            .Be(restoredParent.Columns.Single(c => c.Name == "Order Id").Id);
        pair.TargetColumnId.Should().Be(restoredChild.Columns.Single(c => c.Name == "Order Id").Id);
    }

    /// <summary>
    /// 空白を含む名前の複合一意制約が <c>Indexes</c> ブロックを通って往復する。
    /// </summary>
    [Fact(DisplayName = "DBML 往復: 空白を含む名前の複合一意制約も保たれる")]
    public void RoundTrip_CompositeUniqueConstraintWithQuotedNames_IsPreserved()
    {
        var first = new Column
        {
            Name = "Order Date",
            DataType = "date",
            IsNullable = false,
        };
        var second = new Column
        {
            Name = "Customer Code",
            DataType = "nvarchar(10)",
            IsNullable = false,
        };
        var entity = new Entity
        {
            TableName = "Order Details",
            Columns = [first, second],
            UniqueConstraints =
            {
                new UniqueConstraint { Name = "UQ Order", ColumnIds = [first.Id, second.Id] },
            },
        };

        var restored = DbmlImporter.Parse(
            DbmlExporter.Build(new ErDiagram { Entities = [entity] })
        );

        var restoredEntity = restored.Entities.Should().ContainSingle().Subject;
        var constraint = restoredEntity.UniqueConstraints.Should().ContainSingle().Subject;
        constraint.Name.Should().Be("UQ Order");
        constraint
            .ColumnIds.Select(id => restoredEntity.Columns.Single(c => c.Id == id).Name)
            .Should()
            .Equal("Order Date", "Customer Code");
    }

    /// <summary>
    /// 空白を含む型（<c>double precision</c>）は DBML の規約どおり囲んで書き出し、取込で復元する。
    /// </summary>
    [Theory(DisplayName = "DBML 往復: 空白を含む型は囲まれて保たれる")]
    [InlineData("double precision")]
    [InlineData("timestamp without time zone")]
    public void RoundTrip_TypeWithSpaces_IsQuotedAndPreserved(string dataType)
    {
        var dbml = DbmlExporter.Build(BuildDiagram("t", "c", dataType));

        dbml.Should().Contain($"c \"{dataType}\" [");
        DbmlImporter
            .Parse(dbml)
            .Entities.Single()
            .Columns.Single(column => column.Name == "c")
            .DataType.Should()
            .Be(dataType);
    }

    /// <summary>
    /// 配列型（<c>integer[]</c>）の角括弧が設定ブロックの開始と取り違えられない。
    /// </summary>
    /// <remarks>
    /// 囲まずに書くと取込側は最初の <c>[</c> を設定の開始と読み、型が <c>integer</c> へ化ける。
    /// 書き出しは囲み、取込は「行頭または空白の直後の <c>[</c>」だけを設定とみなす（旧版の出力も読める）。
    /// </remarks>
    [Fact(DisplayName = "DBML 往復: 配列型の角括弧が設定ブロックと取り違えられない")]
    public void RoundTrip_ArrayType_IsPreserved()
    {
        var dbml = DbmlExporter.Build(BuildDiagram("t", "tags", "integer[]"));

        dbml.Should().Contain("tags \"integer[]\" [");
        DbmlImporter
            .Parse(dbml)
            .Entities.Single()
            .Columns.Single(column => column.Name == "tags")
            .DataType.Should()
            .Be("integer[]");
    }

    /// <summary>囲まれていない配列型（旧版の出力・他ツール製）も型を落とさずに読む。</summary>
    [Fact(DisplayName = "DBML 取込: 囲まれていない配列型も型を落とさない")]
    public void Import_UnquotedArrayType_KeepsBrackets()
    {
        var text = string.Join(
            "\n",
            ["Table t {", "  id int [pk, not null]", "  tags integer[] [null]", "}"]
        );

        DbmlImporter
            .Parse(text)
            .Entities.Single()
            .Columns.Single(column => column.Name == "tags")
            .DataType.Should()
            .Be("integer[]");
    }

    /// <summary>括弧つきの型は DBML の規約どおり素のまま書く。</summary>
    [Theory(DisplayName = "DBML 出力: 括弧つきの型は囲まない")]
    [InlineData("varchar(255)")]
    [InlineData("decimal(10,2)")]
    public void Export_TypeWithParentheses_IsNotQuoted(string dataType)
    {
        DbmlExporter.Build(BuildDiagram("t", "c", dataType)).Should().Contain($"c {dataType} [");
    }
}
