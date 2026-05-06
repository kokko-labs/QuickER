using ERDesigner.Models;
using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="SqlServerSchemaImporter"/> の純ロジック (DB に依存しない部分) のテスト。
/// </summary>
public class SqlServerSchemaImporterTests
{
    [Theory(DisplayName = "FormatDataType: 文字列/数値型を正しく整形する")]
    [InlineData("nvarchar", 50, null, null, "nvarchar(50)")]
    [InlineData("nvarchar", -1, null, null, "nvarchar(max)")]
    [InlineData("varchar", 100, null, null, "varchar(100)")]
    [InlineData("decimal", null, 10, 2, "decimal(10,2)")]
    [InlineData("decimal", null, 18, 0, "decimal(18)")]
    [InlineData("int", null, null, null, "int")]
    [InlineData("datetime2", null, null, null, "datetime2")]
    public void FormatDataType_Cases(string type, int? maxLen, int? prec, int? scale, string expected)
    {
        SqlServerSchemaImporter.FormatDataType(type, maxLen, prec, scale).Should().Be(expected);
    }

    [Fact(DisplayName = "ComputeSignature: 同一構造なら同じ署名を返す")]
    public void ComputeSignature_SameStructure_SameSignature()
    {
        var a = new Entity
        {
            TableName = "T",
            Columns =
            {
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };

        var b = new Entity
        {
            TableName = "T",
            Columns =
            {
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };

        var sigA = SqlServerSchemaImporter.ComputeSignature(new[] { a }, Array.Empty<Relationship>());
        var sigB = SqlServerSchemaImporter.ComputeSignature(new[] { b }, Array.Empty<Relationship>());

        sigA.Should().Be(sigB);
    }

    [Fact(DisplayName = "ComputeSignature: 列が違えば署名が変わる")]
    public void ComputeSignature_DifferentColumns_DifferentSignature()
    {
        var a = new Entity
        {
            TableName = "T",
            Columns =
            {
                new Column { Name = "Id", DataType = "int" },
            },
        };

        var b = new Entity
        {
            TableName = "T",
            Columns =
            {
                new Column { Name = "Id", DataType = "bigint" },
            },
        };

        var sigA = SqlServerSchemaImporter.ComputeSignature(new[] { a }, Array.Empty<Relationship>());
        var sigB = SqlServerSchemaImporter.ComputeSignature(new[] { b }, Array.Empty<Relationship>());

        sigA.Should().NotBe(sigB);
    }

    [Fact(DisplayName = "ComputeSignature: 外部キー列が違えば署名が変わる")]
    public void ComputeSignature_DifferentForeignKeyColumns_DifferentSignature()
    {
        var parent = new Entity
        {
            TableName = "Parent",
            Columns =
            {
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };

        var child = new Entity
        {
            TableName = "Child",
            Columns =
            {
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
                new Column { Name = "ParentId1", DataType = "int" },
                new Column { Name = "ParentId2", DataType = "int" },
            },
        };

        var relA = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parent.Columns[0].Id,
            TargetColumnId = child.Columns[1].Id,
        };
        var relB = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parent.Columns[0].Id,
            TargetColumnId = child.Columns[2].Id,
        };

        var sigA = SqlServerSchemaImporter.ComputeSignature(new[] { parent, child }, new[] { relA });
        var sigB = SqlServerSchemaImporter.ComputeSignature(new[] { parent, child }, new[] { relB });

        sigA.Should().NotBe(sigB);
    }
}
