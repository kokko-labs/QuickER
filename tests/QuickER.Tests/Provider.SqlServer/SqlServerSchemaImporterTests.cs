using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Services;

namespace QuickER.Tests.Provider.SqlServer;

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

    /// <summary>
    /// 小数秒の桁数（<c>DATETIME_PRECISION</c>）を型表記へ反映し、既定の 7 桁だけは括弧を省くことを検証する。
    /// 桁数を読まないと <c>datetime2(3)</c> が <c>datetime2</c>（7 桁）として図へ入り、その図からの同期が
    /// 精度を黙って 3 から 7 へ変える。
    /// </summary>
    [Theory(DisplayName = "FormatDataType: 小数秒の桁数を反映し既定の 7 桁は省く")]
    [InlineData("datetime2", 3, "datetime2(3)")]
    [InlineData("datetime2", 0, "datetime2(0)")]
    [InlineData("datetime2", 7, "datetime2")]
    [InlineData("time", 3, "time(3)")]
    [InlineData("time", 7, "time")]
    [InlineData("datetimeoffset", 5, "datetimeoffset(5)")]
    [InlineData("datetimeoffset", 7, "datetimeoffset")]
    [InlineData("datetime2", null, "datetime2")]
    [InlineData("datetime", 3, "datetime")]
    [InlineData("date", 0, "date")]
    public void FormatDataType_FractionalSeconds(
        string type,
        int? datetimePrecision,
        string expected
    )
    {
        SqlServerSchemaImporter
            .FormatDataType(type, null, null, null, datetimePrecision)
            .Should()
            .Be(expected);
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
            ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
        };
        var relB = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parent.Columns[0].Id, child.Columns[2].Id)],
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
            ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
            OnDelete = ForeignKeyReferentialAction.NoAction,
            OnUpdate = ForeignKeyReferentialAction.NoAction,
        };
        var relB = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.SetNull,
        };

        var sigA = SchemaSignature.Compute(new[] { parent, child }, new[] { relA });
        var sigB = SchemaSignature.Compute(new[] { parent, child }, new[] { relB });

        sigA.Should().NotBe(sigB);
    }

    /// <summary>外部テーブルは取り込まず（<c>is_external = 0</c>）、名指しのクエリで告げる（<c>is_external = 1</c>）</summary>
    /// <remarks>
    /// <para>
    /// 実 DB では確かめられない。テストに使う SQL Server のコンテナ（<c>mcr.microsoft.com/mssql/server:2022-latest</c>）は
    /// PolyBase を持たず、<c>sp_configure 'polybase enabled'</c> は「PolyBase feature is not installed」、
    /// <c>CREATE EXTERNAL DATA SOURCE</c> は「External data sources are not supported with type GENERIC」で断られ、
    /// 外部テーブルを作れない（2026-10-15 に確認）。そのため SQL の形だけを固定する。
    /// </para>
    /// <para>
    /// <c>sys.tables.is_external</c> は SQL Server 2016 で追加された列で、docs の対応 DBMS の表が宣言する下限
    /// （SQL Server 2016 以降）と一致する（<c>temporal_type</c> と同じく、下限の版で必ず在る列だけを使う）。
    /// </para>
    /// </remarks>
    [Fact(
        DisplayName = "外部テーブルは取込の対象から外し（is_external = 0）、名指しのクエリで告げる（is_external = 1）"
    )]
    public void ExternalTables_AreFilteredAndReportedInSql()
    {
        static string Sql(string name) =>
            System.Text.RegularExpressions.Regex.Replace(
                (string)
                    typeof(SqlServerSchemaImporter)
                        .GetField(
                            name,
                            System.Reflection.BindingFlags.NonPublic
                                | System.Reflection.BindingFlags.Static
                        )!
                        .GetValue(null)!,
                @"\s+",
                " "
            );

        Sql("TablesSql").Should().Contain("AND t.is_external = 0");
        Sql("ExcludedObjectsSql")
            .Should()
            .Contain("WHERE t.is_ms_shipped = 0 AND t.is_external = 1")
            .And.Contain("'external'");
    }
}
