using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// MySQL の符号つき・符号なし整数列が、生成 → C# リバースの往復で元の綴りのまま戻ることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 正規型は符号なし整数を持たないため、型カタログは値の範囲を収められる型として読む
/// （<c>int unsigned</c> → Int64・<c>bigint unsigned</c> → decimal(20,0)・符号つき <c>tinyint</c> → SmallInt）。
/// その結果、型トークン（<c>[DbColumnMeta]</c> の第 1 引数）は範囲を表す中立の型になり、元の綴りは
/// verbatim 併記（<c>NativeType</c>）が運ぶ。
/// </para>
/// <para>
/// プロパティの C# 型は型トークンからではなく型マッパーから決まり、ドライバ（MySqlConnector）が返す型
/// （<c>uint</c> / <c>ulong</c> / <c>sbyte</c> 等）のまま。トークンは図の型を他方言へ運ぶための範囲の記述で、
/// C# 型と一致させる必要はない（正規型に符号なしの種別が無い以上、そろえる手段も無い）。
/// </para>
/// </remarks>
public class CSharpReverseMySqlUnsignedTests
{
    /// <summary>MySQL の整数型・型メタ属性・プロパティの C# 型の組</summary>
    /// <remarks><c>tinyint unsigned</c> はトークンから同じ綴りへ書き戻せる（TinyInt の書き出しが <c>tinyint unsigned</c>）ので verbatim を持たない</remarks>
    public static TheoryData<string, string, string> IntegerTypes =>
        new()
        {
            { "tinyint unsigned", "[DbColumnMeta(\"tinyint\")]", "byte" },
            { "tinyint", "[DbColumnMeta(\"smallint\", NativeType = \"tinyint\")]", "sbyte" },
            {
                "smallint unsigned",
                "[DbColumnMeta(\"int32\", NativeType = \"smallint unsigned\")]",
                "ushort"
            },
            { "int unsigned", "[DbColumnMeta(\"int64\", NativeType = \"int unsigned\")]", "uint" },
            {
                "bigint unsigned",
                "[DbColumnMeta(\"decimal(20,0)\", NativeType = \"bigint unsigned\")]",
                "ulong"
            },
        };

    /// <summary>整数型の列は、トークンが範囲を表し、元の綴りが往復で戻り、プロパティはドライバの型のまま</summary>
    [Theory(DisplayName = "MySQL の符号つき・符号なし整数は往復で元の綴りのまま戻る")]
    [MemberData(nameof(IntegerTypes))]
    public void RoundTrip_IntegerSpelling_IsPreserved(
        string dataType,
        string metaAttribute,
        string clrType
    )
    {
        var provider = new MySqlProvider();
        var generation = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            Diagram(dataType),
            new CodeGenerationOptions
            {
                RootNamespace = "QuickER.Tests.ReverseMySqlUnsigned",
                GenerateValueObjects = false,
                SplitFilesByCategory = false,
            }
        );
        generation.HasErrors.Should().BeFalse();

        var source = generation.Files.Single().Content;
        source.Should().Contain(metaAttribute);
        source.Should().Contain($"public {clrType}? Probe {{");

        var reversed = new CSharpReverseParser().Parse(source, new MySqlTypeCatalog());
        reversed
            .Entities.Single()
            .Columns.Single(column => column.Name == "probe")
            .DataType.Should()
            .Be(dataType);
        reversed.Warnings.Should().BeEmpty();
    }

    /// <summary>型表記 1 つを持つ 1 テーブルの図を作る</summary>
    private static ErDiagram Diagram(string dataType) =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    TableName = "samples",
                    Columns =
                    {
                        new Column
                        {
                            Name = "sample_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "probe",
                            DataType = dataType,
                            IsNullable = true,
                        },
                    },
                },
            },
        };
}
