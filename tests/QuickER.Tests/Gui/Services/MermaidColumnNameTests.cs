using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// Mermaid の属性名（列名）の書き出しを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// Mermaid の属性行は「型 名前 標識」を空白で区切るため、空白や記号を含む列名は後半が黙って消える。
/// 型が既に <c>decimal(10,2)</c> → <c>decimal_10_2</c> と畳まれているのと同じ扱いにし、名前も畳んだうえで
/// <see cref="ExportOmissionKind.ColumnNameNormalized"/> で告知する（黙って変えない）。
/// </para>
/// <para>
/// テーブル名は <c>名前 {</c> の行全体を名前に採るため空白を含んでも往復する（畳まない）。
/// </para>
/// </remarks>
public class MermaidColumnNameTests
{
    /// <summary>指定した列名を持つ 1 テーブルの図を作る</summary>
    private static ErDiagram BuildDiagram(params string[] columnNames) =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    TableName = "Orders",
                    Columns = columnNames
                        .Select(name => new Column { Name = name, DataType = "int" })
                        .ToList(),
                },
            },
        };

    /// <summary>Mermaid 出力から Orders テーブルの属性名だけを取り出す</summary>
    private static List<string> AttributeNames(string mermaid) =>
        mermaid
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("int ", StringComparison.Ordinal))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1])
            .ToList();

    [Theory(DisplayName = "属性名に書けない文字は _ へ畳まれる")]
    [InlineData("Order Date", "Order_Date")]
    [InlineData("unit price", "unit_price")]
    [InlineData("order-date", "order_date")]
    [InlineData("受注 日", "受注_日")]
    public void Export_ColumnNameWithUnwritableCharacters_IsFolded(
        string columnName,
        string expected
    )
    {
        AttributeNames(MermaidExporter.Build(BuildDiagram(columnName))).Should().Equal(expected);
    }

    [Theory(DisplayName = "そのまま書ける列名は変えない")]
    [InlineData("order_date")]
    [InlineData("OrderDate")]
    [InlineData("受注日")]
    public void Export_WritableColumnName_IsUnchanged(string columnName)
    {
        AttributeNames(MermaidExporter.Build(BuildDiagram(columnName))).Should().Equal(columnName);
    }

    /// <summary>
    /// 畳んだ結果が同じテーブルの別の列とぶつかるときは連番を付ける（黙って重複させない）。
    /// </summary>
    /// <remarks>
    /// 同名の属性が 2 つ並ぶと、読み込み直したときに黙って 1 つへ潰れる。
    /// 畳む必要のない名前を先に予約するので、既にある <c>Order_Date</c> は動かない。
    /// </remarks>
    [Fact(DisplayName = "畳んだ名前が衝突したら連番を付ける")]
    public void Export_FoldedNameCollision_GetsSuffix()
    {
        var mermaid = MermaidExporter.Build(BuildDiagram("Order Date", "Order_Date"));

        // 畳む必要のない Order_Date が元の名前を保ち、畳んだほうに連番が付く
        AttributeNames(mermaid).Should().Equal("Order_Date_2", "Order_Date");
    }

    /// <summary>連番は列の宣言順で決まり、3 つ以上でも重複しないことを検証する</summary>
    [Fact(DisplayName = "連番は宣言順で決まり 3 つ以上でも重複しない")]
    public void Export_MultipleCollisions_AreNumberedInDeclarationOrder()
    {
        var mermaid = MermaidExporter.Build(BuildDiagram("Order Date", "Order-Date", "Order.Date"));

        var names = AttributeNames(mermaid);
        names.Should().Equal("Order_Date", "Order_Date_2", "Order_Date_3");
        names.Should().OnlyHaveUniqueItems();
    }

    /// <summary>同じ図を 2 回書き出しても同じ結果になる（決定的）ことを検証する</summary>
    [Fact(DisplayName = "衝突を含む出力は決定的")]
    public void Export_WithCollisions_IsDeterministic()
    {
        var first = MermaidExporter.Build(BuildDiagram("a b", "a_b", "a-b"));
        var second = MermaidExporter.Build(BuildDiagram("a b", "a_b", "a-b"));

        first.Should().Be(second);
    }

    [Fact(DisplayName = "書き換えた列名があると欠落として告知する")]
    public void DetectOmissions_ReportsNormalizedColumnName()
    {
        MermaidExporter
            .DetectOmissions(BuildDiagram("Order Date"))
            .Should()
            .Contain(ExportOmissionKind.ColumnNameNormalized);
    }

    [Fact(DisplayName = "書き換える列名が無ければ告知しない")]
    public void DetectOmissions_IgnoresWritableColumnNames()
    {
        MermaidExporter
            .DetectOmissions(BuildDiagram("order_date"))
            .Should()
            .NotContain(ExportOmissionKind.ColumnNameNormalized);
    }

    /// <summary>テーブル名は畳まず、そのまま往復することを検証する（IF3 の対象は列名だけ）</summary>
    [Fact(DisplayName = "空白を含むテーブル名は畳まず往復する")]
    public void RoundTrip_TableNameWithSpace_IsPreserved()
    {
        var diagram = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "Order Details",
                    Columns =
                    {
                        new Column { Name = "id", DataType = "int" },
                    },
                },
            },
        };

        MermaidImporter
            .Parse(MermaidExporter.Build(diagram))
            .Entities.Should()
            .ContainSingle()
            .Which.TableName.Should()
            .Be("Order Details");
    }

    /// <summary>畳んだ列名でも Mermaid として読み戻せる（行が壊れない）ことを検証する</summary>
    [Fact(DisplayName = "畳んだ列名は読み戻せる（行が壊れない）")]
    public void RoundTrip_FoldedColumnName_IsReadable()
    {
        var restored = MermaidImporter.Parse(
            MermaidExporter.Build(BuildDiagram("Order Date", "Order_Date"))
        );

        restored
            .Entities.Should()
            .ContainSingle()
            .Which.Columns.Select(column => column.Name)
            .Should()
            .Equal("Order_Date_2", "Order_Date");
    }
}
