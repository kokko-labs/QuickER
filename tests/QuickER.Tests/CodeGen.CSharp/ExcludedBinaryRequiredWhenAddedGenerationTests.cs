using System;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// EditModel の列テーブルに出る「新規行のときだけ必須」フラグ（<c>IsRequiredWhenAdded</c>）が、
/// NOT NULL の除外無制限バイナリ列だけに立つことを検証する。
/// </summary>
/// <remarks>
/// 除外列は通常フェッチで未取得のまま届くため通常の必須検証からは外れるが、NOT NULL 宣言の列を未入力のまま
/// INSERT すると DB の NOT NULL 違反で必ず落ちる。行がまだ DB に無い間だけ必須にすることで、その入力を画面で
/// 列名つきに止める。NULL 許容の除外列・行バージョン列（無制限バイナリではない）・有界バイナリ・通常列は
/// 対象外で、除外オプション OFF なら全列が対象外になる。
/// </remarks>
public sealed class ExcludedBinaryRequiredWhenAddedGenerationTests
{
    /// <summary>非 NULL／NULL 許容の無制限バイナリ・有界バイナリ・rowversion・通常列を 1 つずつ持つ図</summary>
    private static ErDiagram CreateDiagram() =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "documents",
                    Columns =
                    {
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "document_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "title",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                        // 非 NULL の無制限バイナリ＝新規行のときだけ必須になる唯一の列
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "thumb",
                            DataType = "varbinary(max)",
                            IsNullable = false,
                        },
                        // NULL 許容の無制限バイナリ＝DB が空の INSERT を受けるため対象外
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "payload",
                            DataType = "varbinary(max)",
                            IsNullable = true,
                        },
                        // 有界バイナリ＝そもそも除外されない（通常の必須検証の対象）
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "checksum",
                            DataType = "varbinary(16)",
                            IsNullable = false,
                        },
                        // 行バージョン＝DB が採番するため、非 NULL でもどちらの必須検証にも入らない
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "row_ver",
                            DataType = "rowversion",
                            IsNullable = false,
                        },
                    },
                },
            },
        };

    /// <summary>実生成経路と同じく型解決＋方言中立トークン付加を通してコードを生成する</summary>
    private static string Generate(bool excludeUnboundedBinaryColumns, bool generateValueObjects)
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
                GenerateRepositories = true,
                GenerateValueObjects = generateValueObjects,
                ExcludeUnboundedBinaryColumns = excludeUnboundedBinaryColumns,
            }
        );

        result.HasErrors.Should().BeFalse();

        return result.Files[0].Content;
    }

    /// <summary>列テーブルの当該列のエントリから、位置引数 3・4（IsRequired / IsRequiredWhenAdded）を読み取る</summary>
    private static (string IsRequired, string IsRequiredWhenAdded) ColumnFlags(
        string content,
        string propertyName
    )
    {
        var marker =
            $"nameof({propertyName}),\r\n                nameof(Binding{propertyName}),\r\n";
        var index = content.IndexOf(marker, StringComparison.Ordinal);
        index.Should().BeGreaterThan(-1, $"列テーブルに {propertyName} のエントリがあるはず");

        var rest = content[(index + marker.Length)..];
        var lines = rest.Split("\r\n");

        return (lines[0].Trim().TrimEnd(','), lines[1].Trim().TrimEnd(','));
    }

    [Theory(DisplayName = "新規行のときだけ必須になるのは NOT NULL の除外無制限バイナリ列だけ")]
    [InlineData(false)]
    [InlineData(true)]
    public void RequiredWhenAdded_OnlyForNonNullableExcludedBinaryColumn(bool generateValueObjects)
    {
        var content = Generate(
            excludeUnboundedBinaryColumns: true,
            generateValueObjects: generateValueObjects
        );

        // NOT NULL の除外列だけが「通常の必須ではないが新規行では必須」になる
        ColumnFlags(content, "Thumb").Should().Be(("false", "true"));

        // NULL 許容の除外列は DB が空の INSERT を受けるため、どちらの必須検証にも入らない
        ColumnFlags(content, "Payload").Should().Be(("false", "false"));

        // 行バージョン列は DB 採番＝無制限バイナリ判定も偽なので、新フラグは立たない
        ColumnFlags(content, "RowVer").Should().Be(("false", "false"));

        // 有界バイナリと通常列は除外されないため、従来どおり通常の必須検証の対象
        ColumnFlags(content, "Checksum").Should().Be(("true", "false"));
        ColumnFlags(content, "Title").Should().Be(("true", "false"));
    }

    [Theory(DisplayName = "除外オプション OFF なら新規行限定の必須は 1 列も立たない")]
    [InlineData(false)]
    [InlineData(true)]
    public void RequiredWhenAdded_NeverSet_WhenExclusionDisabled(bool generateValueObjects)
    {
        var content = Generate(
            excludeUnboundedBinaryColumns: false,
            generateValueObjects: generateValueObjects
        );

        // 除外がなければ無制限バイナリ列も通常列と同じ扱い（NOT NULL は通常の必須検証へ戻る）
        ColumnFlags(content, "Thumb").Should().Be(("true", "false"));
        ColumnFlags(content, "Payload").Should().Be(("false", "false"));
        ColumnFlags(content, "RowVer").Should().Be(("false", "false"));
        ColumnFlags(content, "Checksum").Should().Be(("true", "false"));
        ColumnFlags(content, "Title").Should().Be(("true", "false"));
    }
}
