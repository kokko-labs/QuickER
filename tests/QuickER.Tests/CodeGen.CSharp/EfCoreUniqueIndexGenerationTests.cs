using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// EF Core の Fluent が、図の一意制約ごとに <c>HasIndex(列…).IsUnique()</c> を出すことを検証するテストクラス。
/// </summary>
/// <remarks>
/// EF Core は 1 回の SaveChanges の中で文の順を自分で決め、モデルの一意インデックスを見て、一意な値が削除する行から別の行へ
/// 移るときに削除を先に並べる。インデックスが無いと、更新が削除より先に走って一意制約に断られることがある（実行時は
/// GraphSaveRuntimeTestsBase の「削除する注文の一意な値を別の注文の更新で取る」が固定する）。DB には何も作らない。
/// </remarks>
public class EfCoreUniqueIndexGenerationTests
{
    /// <summary>一意制約を 2 つ（単一列・複合）持つ表と、一意制約の無い表の図</summary>
    private static ErDiagram BuildDiagram()
    {
        var idColumn = new Column
        {
            Name = "item_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var codeColumn = new Column
        {
            Name = "code",
            DataType = "nvarchar(20)",
            IsNullable = false,
        };
        var groupColumn = new Column
        {
            Name = "group_no",
            DataType = "int",
            IsNullable = false,
        };
        var labelColumn = new Column
        {
            Name = "label",
            DataType = "nvarchar(50)",
            IsNullable = true,
        };
        var items = new Entity
        {
            TableName = "items",
            Columns = { idColumn, codeColumn, groupColumn, labelColumn },
            UniqueConstraints =
            {
                new UniqueConstraint { ColumnIds = [codeColumn.Id] },
                // 列の宣言順と違う並び（label, group_no）を宣言する
                new UniqueConstraint
                {
                    Name = "UQ_items_label_group",
                    ColumnIds = [labelColumn.Id, groupColumn.Id],
                },
            },
        };
        var notes = new Entity
        {
            TableName = "notes",
            Columns =
            {
                new Column
                {
                    Name = "note_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "body",
                    DataType = "nvarchar(100)",
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram { TargetDbms = "sqlserver", Entities = { items, notes } };
    }

    /// <summary>EF Core の Fluent の、指定のエンティティの構成の行を返す</summary>
    private static IReadOnlyList<string> ConfigLines(bool valueObjects, string entityClassName)
    {
        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            BuildDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Test.UniqueIndex",
                GenerateEfCoreRepositories = true,
                GenerateValueObjects = valueObjects,
            }
        );
        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        var lines = result
            .Files.SelectMany(file => file.Content.Split('\n'))
            .Select(line => line.Trim())
            .ToList();
        var start = lines.IndexOf($"modelBuilder.Entity<{entityClassName}>(entity =>");
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = lines.IndexOf("});", start);

        return lines.Skip(start).Take(end - start).ToList();
    }

    [Theory(DisplayName = "EF Core の Fluent: 一意制約ごとに HasIndex(...).IsUnique() を出す")]
    [InlineData(false)]
    [InlineData(true)]
    public void UniqueConstraints_EmitOneUniqueIndexEach(bool valueObjects)
    {
        var indexes = ConfigLines(valueObjects, "ItemEntity")
            .Where(line => line.StartsWith("entity.HasIndex(", StringComparison.Ordinal))
            .ToList();

        indexes
            .Should()
            .Equal(
                "entity.HasIndex(e => e.Code).IsUnique();",
                "entity.HasIndex(e => new { e.Label, e.GroupNo }).IsUnique();"
            );
    }

    [Fact(DisplayName = "EF Core の Fluent: 複合の一意制約は、制約の列の並びどおりに出す")]
    public void CompositeUniqueConstraint_FollowsDeclaredColumnOrder()
    {
        ConfigLines(valueObjects: false, "ItemEntity")
            .Should()
            .Contain("entity.HasIndex(e => new { e.Label, e.GroupNo }).IsUnique();");
    }

    [Fact(DisplayName = "EF Core の Fluent: 一意制約の無い表には HasIndex を出さない")]
    public void TableWithoutUniqueConstraints_EmitsNoIndex()
    {
        ConfigLines(valueObjects: false, "NoteEntity")
            .Should()
            .NotContain(line => line.Contains("HasIndex", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "EF Core の Fluent: 値オブジェクトの列の一意制約にも HasIndex を出す")]
    public void ValueObjectColumns_EmitUniqueIndex()
    {
        var lines = ConfigLines(valueObjects: true, "ItemEntity");

        lines
            .Should()
            .Contain(line =>
                line.StartsWith("entity.Property(e => e.Code)", StringComparison.Ordinal)
                && line.Contains("HasConversion", StringComparison.Ordinal)
            );
        lines.Should().Contain("entity.HasIndex(e => e.Code).IsUnique();");
    }
}
