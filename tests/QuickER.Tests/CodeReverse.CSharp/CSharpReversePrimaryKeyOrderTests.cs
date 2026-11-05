using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// 複合主キー自身の列順序を、生成コードの <c>[DbTableMeta(PrimaryKeyOrder = …)]</c> に食い違うときだけ載せることを検証する（生成側）。
/// </summary>
/// <remarks>
/// <para>
/// 主キーの順序は DDL の主キー句と DB 同期の主キー比較にそのまま使われるため、「図 → 生成コード → C# リバース」で
/// 落ちると、次の同期で実 DB と食い違って主キーの張り直しが出る。
/// </para>
/// <para>
/// 生成側は <see cref="Entity.GetReorderedPrimaryKeyColumnNames"/> が食い違いを返すテーブルにだけ順序を載せ
/// （<c>[DbColumnMeta].NativeType</c> の verbatim 併記と同じ「食い違うときだけ出す」方式）、
/// リバース側は主キー列とちょうど一致するときだけ採る。GUI のマージ取込では、コードが順序を書いていない
/// （古い生成コード）ときに限り現在図の順序を引き継ぐ。
/// </para>
/// <para>
/// 往復・読み取り・マージ取込は形式の往復のエンジン（<c>FormatRoundTrip/</c> の形の行 <c>csharp-reordered-primary-key</c>・
/// <c>csharp-description-and-primary-key-order</c>、規則の行 <c>csharp-rule-primary-key-order-*</c>、マージの行
/// <c>merge-csharp-*-key-*</c>・<c>merge-csharp-*-order-*</c>）が見る。ここではリバースを通らない生成側の判定だけを固定する。
/// </para>
/// </remarks>
public class CSharpReversePrimaryKeyOrderTests
{
    private const string TableName = "order_lines";

    private static CodeGenerationOptions Options =>
        new()
        {
            RootNamespace = "QuickER.Tests.ReversePrimaryKeyOrder",
            GenerateValueObjects = false,
            SplitFilesByCategory = false,
        };

    /// <summary>
    /// 主キー 2 列（<c>tenant_id</c> → <c>order_no</c> の宣言順）＋通常列 1 つのテーブルを作る
    /// </summary>
    /// <param name="order">主キーの順序として明示する列名（<c>null</c>＝明示しない）</param>
    /// <param name="description">テーブルの説明</param>
    private static ErDiagram Diagram(string[]? order, string description = "")
    {
        var entity = new Entity
        {
            TableName = TableName,
            Description = description,
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
                    DataType = "nvarchar(50)",
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

    private static string Generate(ErDiagram diagram)
    {
        var provider = new SqlServerProvider();
        var generation = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            Options
        );

        generation.HasErrors.Should().BeFalse();

        return generation.Files.Single().Content;
    }

    /// <summary>
    /// 順序を明示していない図・宣言順どおりに明示した図は属性を出さず、生成物がバイト単位で一致する
    /// </summary>
    [Fact(DisplayName = "宣言順どおりの主キーには順序を出さず、生成物は明示の有無で変わらない")]
    public void Generate_KeyInDeclarationOrder_EmitsNothingAndIsByteIdentical()
    {
        var unstated = Generate(Diagram(order: null));
        var pinned = Generate(Diagram(["tenant_id", "order_no"]));

        unstated.Should().NotContain("PrimaryKeyOrder =");
        pinned.Should().Be(unstated, "宣言順どおりの明示は、明示しないのと同じ生成物になる");
    }

    /// <summary>単一列の主キーには順序を出さない（並べ替える相手が無い）</summary>
    [Fact(DisplayName = "単一列の主キーには順序を出さない")]
    public void Generate_SingleColumnKey_EmitsNothing()
    {
        var diagram = Diagram(order: null);
        diagram.Entities[0].Columns[1].IsPrimaryKey = false;

        Generate(diagram).Should().NotContain("PrimaryKeyOrder =");
    }
}
