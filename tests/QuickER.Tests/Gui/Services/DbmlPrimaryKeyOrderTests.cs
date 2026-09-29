using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 複合主キー自身の列順序を、DBML 標準の <c>Indexes { (b, a) [pk] }</c> で往復させることを検証するテストクラス
/// </summary>
/// <remarks>
/// <para>
/// 列ごとの <c>pk</c> は列の並びでしか順序を表せないため、主キーの順序が列の並びと食い違うテーブルだけ、
/// 主キーを <c>Indexes</c> ブロックの <c>[pk]</c> 索引で書く（判定の正本は
/// <see cref="Entity.GetReorderedPrimaryKeyColumnNames"/>）。食い違わないテーブルの出力は従来どおり。
/// </para>
/// <para>
/// 取込は <c>[pk]</c> 索引を主キーの構成列と順序として読み、一意制約は作らない。列ごとの <c>pk</c> と
/// 集合が食い違う・索引が 2 つ・索引の中で同じ列が 2 回、のどれも推測で合わせず行を名指しして断る。
/// </para>
/// </remarks>
public class DbmlPrimaryKeyOrderTests
{
    /// <summary>主キー 2 列（tenant_id → order_no の宣言順）＋通常列 1 つのテーブルを作る</summary>
    private static ErDiagram Diagram(string[]? order)
    {
        var entity = new Entity
        {
            TableName = "order_lines",
            Columns =
            {
                new Column
                {
                    Name = "tenant_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "order_no",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "memo",
                    DataType = "varchar(50)",
                    IsNullable = true,
                },
            },
        };

        if (order is not null)
        {
            entity.PrimaryKeyColumnIds = order
                .Select(name => entity.Columns.Single(column => column.Name == name).Id)
                .ToList();
        }

        return new ErDiagram { Entities = { entity } };
    }

    /// <summary>主キーを実効順の列名で返す</summary>
    private static string[] KeyOrder(Entity entity) =>
        entity.GetPrimaryKeyColumnsInOrder().Select(column => column.Name).ToArray();

    private static string Lines(params string[] lines) =>
        string.Join(Environment.NewLine, lines) + Environment.NewLine;

    /// <summary>列の並びと食い違う主キーの順序は、書き出し → 取り込みで戻る</summary>
    [Fact(DisplayName = "DBML: 列の並びと食い違う主キーの順序は、書き出し → 取り込みで戻る")]
    public void RoundTrip_ReorderedKey_IsRestored()
    {
        var text = DbmlExporter.Build(Diagram(["order_no", "tenant_id"]));

        // 主キー列に列ごとの pk は付けず、索引の [pk] で順序ごと書く（NOT NULL は列の行に残る）
        text.Should().Contain("  tenant_id int [not null]");
        text.Should().Contain("  order_no int [not null]");
        text.Should().Contain("    (order_no, tenant_id) [pk]");

        var restored = DbmlImporter.Parse(text).Entities.Single();

        KeyOrder(restored).Should().Equal("order_no", "tenant_id");
        restored
            .Columns.Where(column => column.IsPrimaryKey)
            .Should()
            .OnlyContain(column => !column.IsNullable);
        restored.UniqueConstraints.Should().BeEmpty("主キーの索引から一意制約は作らない");
    }

    /// <summary>
    /// 列の並びどおりの主キーは列ごとの <c>pk</c> で書き、出力は順序の明示の有無で変わらない
    /// </summary>
    [Fact(DisplayName = "DBML: 列の並びどおりの主キーは従来どおり列ごとの pk で書く")]
    public void Build_KeyInColumnOrder_IsUnchanged()
    {
        var unstated = DbmlExporter.Build(Diagram(order: null));
        var pinned = DbmlExporter.Build(Diagram(["tenant_id", "order_no"]));

        unstated.Should().Contain("  tenant_id int [pk, not null]");
        unstated.Should().NotContain("[pk]");
        unstated.Should().NotContain("Indexes");
        pinned.Should().Be(unstated);
    }

    /// <summary>他ツール製の DBML（列ごとの pk なしで [pk] 索引だけ）を読める</summary>
    [Fact(DisplayName = "DBML: 列ごとの pk の無い [pk] 索引だけの主キーを読める")]
    public void Parse_PrimaryKeyIndexOnly_SetsKeyAndNotNull()
    {
        var diagram = DbmlImporter.Parse(
            Lines(
                "Table order_lines {",
                "  tenant_id int",
                "  order_no int",
                "  Indexes {",
                "    (order_no, tenant_id) [pk, name: 'pk_order_lines']",
                "  }",
                "}"
            )
        );
        var entity = diagram.Entities.Single();

        KeyOrder(entity).Should().Equal("order_no", "tenant_id");
        entity.Columns.Should().OnlyContain(column => column.IsPrimaryKey && !column.IsNullable);
    }

    /// <summary>列ごとの pk と [pk] 索引が同じ集合なら受け、順序は索引を採る</summary>
    [Fact(DisplayName = "DBML: 列ごとの pk と [pk] 索引が同じ集合なら、順序は索引を採る")]
    public void Parse_SameSetInBothPlaces_UsesIndexOrder()
    {
        var diagram = DbmlImporter.Parse(
            Lines(
                "Table order_lines {",
                "  tenant_id int [pk]",
                "  order_no int [pk]",
                "  Indexes {",
                "    (order_no, tenant_id) [pk]",
                "  }",
                "}"
            )
        );

        KeyOrder(diagram.Entities.Single()).Should().Equal("order_no", "tenant_id");
    }

    /// <summary>列ごとの pk と [pk] 索引の集合が食い違えば、推測で合わせず索引の行を名指しして断る</summary>
    [Fact(DisplayName = "DBML: 列ごとの pk と [pk] 索引の集合が食い違えば取込を断る")]
    public void Parse_ConflictingKeySets_IsRejected()
    {
        var text = Lines(
            "Table order_lines {",
            "  tenant_id int [pk]",
            "  order_no int",
            "  memo varchar(50)",
            "  Indexes {",
            "    (order_no, tenant_id) [pk]",
            "  }",
            "}"
        );

        var act = () => DbmlImporter.Parse(text);

        act.Should()
            .Throw<InvalidDataException>()
            .WithMessage(
                string.Format(
                    Strings.Import_LineDiagnostic,
                    6,
                    string.Format(
                        Strings.Dbml_PrimaryKeyConflict,
                        "order_lines",
                        "tenant_id",
                        "order_no, tenant_id"
                    )
                )
            );
    }

    /// <summary>[pk] 索引が 2 つあれば、2 つ目の行を名指しして断る</summary>
    [Fact(DisplayName = "DBML: [pk] 索引が 2 つあれば取込を断る")]
    public void Parse_TwoPrimaryKeyIndexes_IsRejected()
    {
        var text = Lines(
            "Table order_lines {",
            "  tenant_id int",
            "  order_no int",
            "  Indexes {",
            "    (order_no, tenant_id) [pk]",
            "    (tenant_id, order_no) [pk]",
            "  }",
            "}"
        );

        var act = () => DbmlImporter.Parse(text);

        act.Should()
            .Throw<InvalidDataException>()
            .WithMessage(
                string.Format(
                    Strings.Import_LineDiagnostic,
                    6,
                    string.Format(Strings.Dbml_PrimaryKeyIndexDuplicate, "order_lines")
                )
            );
    }

    /// <summary>[pk] 索引の中で同じ列が 2 回出れば断る</summary>
    [Fact(DisplayName = "DBML: [pk] 索引の中で同じ列が 2 回出れば取込を断る")]
    public void Parse_DuplicateColumnInPrimaryKeyIndex_IsRejected()
    {
        var text = Lines(
            "Table order_lines {",
            "  tenant_id int",
            "  Indexes {",
            "    (tenant_id, tenant_id) [pk]",
            "  }",
            "}"
        );

        var act = () => DbmlImporter.Parse(text);

        act.Should().Throw<InvalidDataException>().WithMessage("*4*tenant_id, tenant_id*");
    }

    /// <summary>[pk] 索引が未定義の列を指せば、その列名を名指しして断る</summary>
    [Fact(DisplayName = "DBML: [pk] 索引が未定義の列を指せば取込を断る")]
    public void Parse_PrimaryKeyIndexUnknownColumn_IsRejected()
    {
        var text = Lines(
            "Table order_lines {",
            "  tenant_id int",
            "  Indexes {",
            "    (tenant_id, no_such_column) [pk]",
            "  }",
            "}"
        );

        var act = () => DbmlImporter.Parse(text);

        act.Should().Throw<InvalidDataException>().WithMessage("*no_such_column*");
    }
}
