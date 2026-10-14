using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// SQLite の <c>REAL</c> 列が倍精度の <c>double</c> で生成され、型トークンでは <c>FLOAT</c> へ畳まれる元の綴りを
/// <c>NativeType</c> として併記し、C# リバースで <c>REAL</c> へ戻ることを検証するテストクラス。
/// </summary>
/// <remarks>
/// SQLite の REAL アフィニティは 8 バイトの倍精度で、以前は単精度の <c>float</c> で生成していた（読み書きのたびに精度を落とす）。
/// 型カタログは REAL を Float64 と読み、Float64 は従来どおり <c>FLOAT</c> と書き戻すため、REAL の列は綴りが変わる列として
/// verbatim 併記と生成時 Info の対象になる（FLOAT の列は綴りが揃うので併記しない）。
/// </remarks>
public class SqliteRealColumnGenerationTests
{
    private static CodeGenerationResult Generate(string dataType)
    {
        var provider = new SqliteProvider();
        var diagram = new ErDiagram
        {
            TargetDbms = "sqlite",
            Entities =
            {
                new Entity
                {
                    TableName = "measures",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "INTEGER",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "value",
                            DataType = dataType,
                            IsNullable = false,
                        },
                    },
                },
            },
        };

        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "QuickER.Tests.SqliteReal",
                GenerateValueObjects = false,
                SplitFilesByCategory = false,
            }
        );
    }

    /// <summary>REAL は double で生成され、元の綴りを併記し、C# リバースで REAL へ警告なしに戻る</summary>
    [Fact(DisplayName = "SQLite の REAL は double で生成され、綴りを併記して REAL へ戻る")]
    public void Real_GeneratesDoubleWithVerbatimSpelling()
    {
        var result = Generate("REAL");
        result.HasErrors.Should().BeFalse();

        var source = result.Files.Single().Content;
        source.Should().Contain("public double Value { get; set; }");
        source.Should().Contain("[DbColumnMeta(\"float64\", NativeType = \"REAL\"");

        var reversed = new CSharpReverseParser().Parse(
            source,
            new SqliteTypeCatalog(),
            IdentifierLengthLimit.Unlimited
        );
        reversed
            .Entities.Single()
            .Columns.Single(column => column.Name == "value")
            .DataType.Should()
            .Be("REAL");
        reversed.Warnings.Should().BeEmpty();
    }

    /// <summary>FLOAT は double で生成され、綴りが揃うので併記しない（既存の生成物と同じ）</summary>
    [Fact(DisplayName = "SQLite の FLOAT は double で生成され、綴りの併記は付かない")]
    public void Float_GeneratesDoubleWithoutVerbatim()
    {
        var source = Generate("FLOAT").Files.Single().Content;

        source.Should().Contain("public double Value { get; set; }");
        source.Should().Contain("[DbColumnMeta(\"float64\")]");
        source.Should().NotContain("NativeType = \"FLOAT\"");
    }
}
