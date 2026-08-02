using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Services;
using QuickER.SqlServer;

namespace QuickER.Tests.SqlServer;

/// <summary><see cref="SqlServerSchemaImporter"/> の DB 非依存ロジック（型整形・署名計算）を検証するテストクラス</summary>
public class SqlServerSchemaImporterTests
{
    /// <summary>FormatDataType が長さ・精度・スケールを反映した型表記を返すことを検証する</summary>
    [Theory(DisplayName = "FormatDataType: 文字列/数値型を正しく整形する")]
    [InlineData("nvarchar", 50, null, null, "nvarchar(50)")]
    [InlineData("nvarchar", -1, null, null, "nvarchar(max)")]
    [InlineData("varchar", 100, null, null, "varchar(100)")]
    [InlineData("decimal", null, 10, 2, "decimal(10,2)")]
    [InlineData("decimal", null, 18, 0, "decimal(18)")]
    [InlineData("int", null, null, null, "int")]
    [InlineData("datetime2", null, null, null, "datetime2")]
    public void FormatDataType_Cases(
        string type,
        int? maxLen,
        int? prec,
        int? scale,
        string expected
    )
    {
        SqlServerSchemaImporter.FormatDataType(type, maxLen, prec, scale).Should().Be(expected);
    }

    /// <summary>構造が同一なら同じ署名が返ることを検証する</summary>
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
                    IsNullable = false,
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
                    IsNullable = false,
                },
            },
        };

        var sigA = SchemaSignature.Compute(new[] { a }, Array.Empty<Relationship>());
        var sigB = SchemaSignature.Compute(new[] { b }, Array.Empty<Relationship>());

        sigA.Should().Be(sigB);
    }

    /// <summary>NULL 許容の違いが署名へ反映されることを検証する</summary>
    [Fact(DisplayName = "ComputeSignature: NULL 許容が違えば署名が変わる")]
    public void ComputeSignature_DifferentNullability_DifferentSignature()
    {
        var a = new Entity
        {
            TableName = "T",
            Columns =
            {
                new Column
                {
                    Name = "Name",
                    DataType = "nvarchar(50)",
                    IsNullable = true,
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
                    Name = "Name",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
            },
        };

        var sigA = SchemaSignature.Compute(new[] { a }, Array.Empty<Relationship>());
        var sigB = SchemaSignature.Compute(new[] { b }, Array.Empty<Relationship>());

        sigA.Should().NotBe(sigB);
    }

    /// <summary>列の型の違いが署名へ反映されることを検証する</summary>
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

        var sigA = SchemaSignature.Compute(new[] { a }, Array.Empty<Relationship>());
        var sigB = SchemaSignature.Compute(new[] { b }, Array.Empty<Relationship>());

        sigA.Should().NotBe(sigB);
    }

    /// <summary>外部キーの参照列の違いが署名へ反映されることを検証する</summary>
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

        var sigA = SchemaSignature.Compute(new[] { parent, child }, new[] { relA });
        var sigB = SchemaSignature.Compute(new[] { parent, child }, new[] { relB });

        sigA.Should().NotBe(sigB);
    }

    /// <summary>参照アクション（ON DELETE/UPDATE）の違いが署名へ反映されることを検証する</summary>
    [Fact(DisplayName = "ComputeSignature: 参照アクションが違えば署名が変わる")]
    public void ComputeSignature_DifferentReferentialActions_DifferentSignature()
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
                new Column { Name = "ParentId", DataType = "int" },
            },
        };

        var relA = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parent.Columns[0].Id,
            TargetColumnId = child.Columns[1].Id,
            OnDelete = ForeignKeyReferentialAction.NoAction,
            OnUpdate = ForeignKeyReferentialAction.NoAction,
        };
        var relB = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parent.Columns[0].Id,
            TargetColumnId = child.Columns[1].Id,
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.SetNull,
        };

        var sigA = SchemaSignature.Compute(new[] { parent, child }, new[] { relA });
        var sigB = SchemaSignature.Compute(new[] { parent, child }, new[] { relB });

        sigA.Should().NotBe(sigB);
    }
}
