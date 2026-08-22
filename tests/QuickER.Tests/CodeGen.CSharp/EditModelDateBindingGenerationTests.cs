using System;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 日付のみ（SQL の <c>date</c>）の列で、EditModel の表示文字列が日付だけになる生成分岐を検証する。
/// </summary>
/// <remarks>
/// 既定の <c>ToString()</c> は時刻部（"0:00:00"）を必ず伴うため、日付のみの列はカルチャ依存の短い日付書式
/// （<c>ToString("d")</c>）で導出する。判定材料は方言中立トークン（<c>date</c>）で、時刻を持つ列
/// （<c>datetime2</c>）は既定書式のまま。値オブジェクト有効時は内包値を同じ書式で表示する
/// （VO の <c>ToString()</c> は時刻部まで出るため）。
/// </remarks>
public sealed class EditModelDateBindingGenerationTests
{
    /// <summary>date 列（delivery_date）と datetime2 列（ordered_at）を持つ図を組み立てる</summary>
    private static ErDiagram CreateDiagram() =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "orders",
                    Columns =
                    {
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "delivery_date",
                            DataType = "date",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "ordered_at",
                            DataType = "datetime2",
                            IsNullable = true,
                        },
                    },
                },
            },
        };

    /// <summary>実生成経路と同じく型解決＋方言中立トークン付加を通してコードを生成する</summary>
    private static string Generate(bool generateValueObjects)
    {
        var diagram = CreateDiagram();
        var columnTypes = CanonicalTypeTokenAttacher.Attach(
            SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            diagram,
            new SqlServerTypeCatalog()
        );

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            columnTypes,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEditModels = true,
                GenerateValueObjects = generateValueObjects,
            }
        );

        result.HasErrors.Should().BeFalse();

        return result.Files[0].Content;
    }

    [Fact(DisplayName = "date 列の表示は日付のみの書式で導出される（時刻を持つ列は既定書式）")]
    public void DateColumn_UsesShortDateFormat()
    {
        var content = Generate(generateValueObjects: false);

        content
            .Should()
            .Contain("BindingDeliveryDate = DeliveryDate?.ToString(\"d\") ?? string.Empty;");
        content.Should().Contain("BindingOrderedAt = OrderedAt?.ToString() ?? string.Empty;");
    }

    [Fact(DisplayName = "値オブジェクト有効時も date 列は内包値を日付のみの書式で表示する")]
    public void DateColumn_WithValueObjects_UsesShortDateFormatOfUnderlyingValue()
    {
        var content = Generate(generateValueObjects: true);

        content
            .Should()
            .Contain("BindingDeliveryDate = DeliveryDate?.Value.ToString(\"d\") ?? string.Empty;");
        content.Should().Contain("BindingOrderedAt = OrderedAt?.ToString() ?? string.Empty;");
    }
}
