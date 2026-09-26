using System;
using AwesomeAssertions;
using QuickER.CodeReverse.CSharp;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.SqlServer;
using ReverseStrings = QuickER.CodeReverse.CSharp.Resources.Strings;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// 長さの無い文字列・バイナリのトークン（<c>string</c> / <c>binary</c> 等）を C# リバースへ通したときの
/// 振る舞いを固定するテストクラス。
/// </summary>
/// <remarks>
/// 長さを要する型の長さ無しは、SQL Server / MySQL / Oracle の <see cref="ITypeCatalog.TryFormat"/> が
/// <c>false</c> を返す（列として書き出せない＝長さ無しの型名は構文エラーになるか黙って長さ 1 の列を作る）。
/// リバースはこのとき既存の一般則どおり<b>トークン文字列をそのまま列の型として採り</b>、警告で名指しする。
/// 図に残るのは <c>string</c> のような DB 型ではない表記なので、警告文はその列の型を手で直すよう促す。
/// </remarks>
public class CSharpReverseLengthlessTokenTests
{
    /// <summary>長さ無しトークンを 1 列だけ持つ生成コード相当のソース</summary>
    private static string Source(string token) =>
        $$"""
            namespace Sample;

            [Table("docs")]
            public partial class DocEntity
            {
                [Column("memo")]
                [DbColumnMeta("{{token}}")]
                public string? Memo { get; set; }
            }
            """;

    /// <summary>
    /// 長さを要する方言では、長さ無しトークンはそのまま列の型になり警告が出る。
    /// </summary>
    [Theory(
        DisplayName = "長さ無しトークンは SQL Server / MySQL / Oracle ではトークン文字列のまま採用され警告が出る"
    )]
    [InlineData("SqlServer", "string")]
    [InlineData("SqlServer", "binary")]
    [InlineData("MySql", "string")]
    [InlineData("MySql", "binary")]
    [InlineData("Oracle", "string")]
    [InlineData("Oracle", "binary")]
    public void Parse_LengthlessToken_KeepsTokenTextAndWarns(string dialect, string token)
    {
        var result = new CSharpReverseParser().Parse(Source(token), CatalogOf(dialect));

        var column = result
            .Entities.Should()
            .ContainSingle()
            .Subject.Columns.Should()
            .ContainSingle()
            .Subject;
        column.DataType.Should().Be(token);
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(ReverseStrings.Reverse_TypeTokenUnresolved, token, "docs", "memo"));
    }

    /// <summary>
    /// 長さ無しが正当な方言（PostgreSQL）では従来どおりネイティブ型へ展開され、警告も出ない。
    /// </summary>
    [Theory(DisplayName = "PostgreSQL では長さ無しトークンも従来どおりネイティブ型へ展開される")]
    [InlineData("string", "varchar")]
    [InlineData("binary", "bytea")]
    public void Parse_LengthlessToken_PostgreSql_ExpandsWithoutWarning(
        string token,
        string expectedType
    )
    {
        var result = new CSharpReverseParser().Parse(Source(token), new PostgreSqlTypeCatalog());

        result.Entities[0].Columns[0].DataType.Should().Be(expectedType);
        result.Warnings.Should().BeEmpty();
    }

    /// <summary>長さ付きトークンは 3 方言とも従来どおり展開される（長さ無しだけが落ちていることの対照）</summary>
    [Theory(DisplayName = "長さ付きトークンは 3 方言とも従来どおり展開される")]
    [InlineData("SqlServer", "nvarchar(50)")]
    [InlineData("MySql", "varchar(50)")]
    [InlineData("Oracle", "NVARCHAR2(50)")]
    public void Parse_TokenWithLength_ExpandsWithoutWarning(string dialect, string expectedType)
    {
        var result = new CSharpReverseParser().Parse(Source("string(50)"), CatalogOf(dialect));

        result.Entities[0].Columns[0].DataType.Should().Be(expectedType);
        result.Warnings.Should().BeEmpty();
    }

    private static ITypeCatalog CatalogOf(string dialect) =>
        dialect switch
        {
            "SqlServer" => new SqlServerTypeCatalog(),
            "MySql" => new MySqlTypeCatalog(),
            "Oracle" => new OracleTypeCatalog(),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "未知の方言"),
        };
}
