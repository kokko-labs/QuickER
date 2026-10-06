using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Runtime;
using Xunit;
using EfCoreMetadata = QuickER.Runtime.EntityFrameworkCore.EntitySaveMetadata;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// EF Core 版の生 SQL のエンティティ取得（<c>QueryBySqlAsync</c>）が、ドライバが返す格納型を列のプロパティ型へ
/// 変換して写すことを検証するテストクラス。
/// </summary>
/// <remarks>
/// EF Core は SQLite（int 列を long で返す）や Oracle（NUMBER を decimal で返す）にも接続するため、
/// 値オブジェクトでない列も <c>RawValueConverter.ConvertRaw</c> を通す。ここではランタイムパッケージの
/// <c>EntitySaveMetadata</c> を実ファイルでない SQLite のリーダーへ実際に当て、行の写しを見る。
/// あわせてインラインで出す EF Core 単独の生成物（値オブジェクトなし）が同じ変換を出すことを生成テキストで固定する。
/// </remarks>
public class EfCoreRawSqlValueConversionTests
{
    /// <summary>格納型とプロパティ型が食い違う列を持つテーブル</summary>
    [Table("probes")]
    private sealed class ProbeEntity : EntityBaseCore
    {
        [Key]
        [Column("probe_id")]
        public int ProbeId { get; set; }

        [Column("small")]
        public short Small { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("token")]
        public Guid Token { get; set; }

        [Column("flag")]
        public bool Flag { get; set; }

        [Column("count")]
        public int? Count { get; set; }
    }

    [Fact(
        DisplayName = "EF Core の生 SQL のエンティティ取得は SQLite の格納型をプロパティ型へ変換して写す"
    )]
    public void MapEntityFromRawSql_ConvertsSqliteStorageTypes()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE probes (probe_id INTEGER PRIMARY KEY, small INTEGER, amount TEXT, token TEXT, flag INTEGER, count INTEGER);"
                + "INSERT INTO probes VALUES (7, 3, '123.45', '00000000-0000-0000-0000-000000000042', 1, NULL);";
            create.ExecuteNonQuery();
        }

        using var query = connection.CreateCommand();
        query.CommandText = "SELECT * FROM probes";
        using var reader = query.ExecuteReader();
        reader.Read().Should().BeTrue();

        // SQLite のドライバは INTEGER を long・TEXT を string で返す（プロパティ型とは食い違う）
        reader.GetValue(0).Should().BeOfType<long>();

        var metadata = EfCoreMetadata.For(typeof(ProbeEntity));
        var plan = metadata.CreateRawSqlRowPlan(reader);
        var entity = metadata.MapEntityFromRawSql<ProbeEntity>(reader, plan);

        entity.ProbeId.Should().Be(7);
        entity.Small.Should().Be(3);
        entity.Amount.Should().Be(123.45m);
        entity.Token.Should().Be(new Guid("00000000-0000-0000-0000-000000000042"));
        entity.Flag.Should().BeTrue();
        entity.Count.Should().BeNull();
        entity.RowState.Should().Be(RowState.Unchanged);
    }

    [Fact(
        DisplayName = "EF Core 単独・値オブジェクトなしの生成物は生 SQL の列を RawValueConverter で変換する"
    )]
    public void Generate_EfCoreOnlyWithoutValueObjects_ConvertsRawColumnValues()
    {
        var diagram = new ErDiagram();
        var table = new Entity { TableName = "probes" };
        table.Columns.Add(
            new Column
            {
                Name = "probe_id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        diagram.Entities.Add(table);

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEfCoreRepositories = true,
                GenerateValueObjects = false,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        var start = content.IndexOf(
            "private static void SetColumnValue(",
            StringComparison.Ordinal
        );
        start.Should().BeGreaterThan(0);
        var body = content[
            start..content.IndexOf(
                "public TEntity MapEntityFromRawSql",
                start,
                StringComparison.Ordinal
            )
        ];

        body.Should().Contain("RawValueConverter.ConvertRaw(value, propertyType)");
        body.Should().NotContain("property.SetValue(entity, value);");
    }
}
