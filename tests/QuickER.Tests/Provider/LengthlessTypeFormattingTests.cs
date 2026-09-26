using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// 「長さを要する型の長さ無しは書き出せない」（<see cref="ITypeCatalog.TryFormat"/> が <c>false</c>）ことの
/// 波及先を、呼び出し元ごとに固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ITypeCatalog.TryFormat"/> の呼び出し元は 4 つで、長さ無しの正規型に対する振る舞いは次のとおり:
/// </para>
/// <list type="bullet">
///   <item><see cref="DiagramTypeConverter"/>: 対象 DBMS 切替の<b>変換不能一覧</b>へ載る（当クラス）</item>
///   <item><see cref="CanonicalTypeTokenAttacher"/>: 書き戻せないので<b>verbatim 併記</b>が付く（当クラス）</item>
///   <item><c>CSharpReverseParser</c>: 警告つきで<b>トークン文字列をそのまま列の型に採る</b>（CodeReverse 側のテスト）</item>
///   <item><c>QueryParameterTypeResolver</c>: パラメータは列を作らないので<b>「無制限」として解決し直す</b>（CodeGen 側のテスト）</item>
/// </list>
/// <para>
/// 種別ごとの可否そのものは <c>CanonicalTypeTokenTests</c> の 5 方言表が正本。
/// </para>
/// </remarks>
public class LengthlessTypeFormattingTests
{
    /// <summary>
    /// 固定長へ長さ <c>-1</c>（max）を渡すと、方言の整形は「実在しない表記」または「別の種別の表記」を
    /// <c>true</c> で返す——<c>QueryParameterTypeResolver</c> のフォールバックが可変長 3 種別に限られる理由。
    /// </summary>
    /// <remarks>
    /// <c>FormatLength</c> 系は長さ <c>-1</c> に対して機械的に <c>(max)</c> を付ける（または LOB 型へ倒す）ため、
    /// 「長さが無いなら無制限として読み直す」を固定長まで広げると、<c>nchar(max)</c> のような DDL として通らない
    /// 表記や、固定長が可変長 LOB に化けた表記が生まれる。ここはその実測を固定して、フォールバックの対象種別を
    /// 安易に広げられないようにするためのもの。
    /// </remarks>
    [Theory(
        DisplayName = "固定長へ max（-1）を渡すと実在しない／別種別の表記が返る（対象外の根拠）"
    )]
    [InlineData("SqlServer", CanonicalTypeKind.FixedString, "nchar(max)")]
    [InlineData("SqlServer", CanonicalTypeKind.AnsiFixedString, "char(max)")]
    [InlineData("SqlServer", CanonicalTypeKind.FixedBinary, "binary(max)")]
    [InlineData("Oracle", CanonicalTypeKind.FixedString, "NCHAR")]
    [InlineData("Oracle", CanonicalTypeKind.AnsiFixedString, "CHAR")]
    [InlineData("MySql", CanonicalTypeKind.FixedString, "longtext")]
    [InlineData("MySql", CanonicalTypeKind.FixedBinary, "longblob")]
    public void TryFormat_FixedLengthWithMax_ProducesUnusableText(
        string dialect,
        CanonicalTypeKind kind,
        string expected
    )
    {
        ITypeCatalog catalog = dialect switch
        {
            "SqlServer" => new SqlServerTypeCatalog(),
            "MySql" => new MySqlTypeCatalog(),
            "Oracle" => new OracleTypeCatalog(),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
        };

        catalog
            .TryFormat(new CanonicalType(kind, Length: -1), out var nativeType)
            .Should()
            .BeTrue();
        nativeType.Should().Be(expected);
    }

    /// <summary>PostgreSQL の長さ無し <c>varchar</c> 1 列だけを持つ図を作る</summary>
    private static (ErDiagram Diagram, Guid ColumnId) BuildLengthlessVarcharDiagram()
    {
        var column = new Column
        {
            Name = "memo",
            DataType = "varchar",
            IsNullable = true,
        };
        var entity = new Entity { TableName = "notes", Columns = { column } };

        return (new ErDiagram { Entities = { entity } }, column.Id);
    }

    /// <summary>
    /// PostgreSQL の長さ無し <c>varchar</c> は、長さを要する 3 方言への切替で変換不能一覧に載る。
    /// </summary>
    /// <remarks>
    /// PostgreSQL では長さ無し <c>varchar</c> が正当な宣言（上限なしの可変長）で、実 DB の取込がそのまま
    /// 持ち帰る。これを黙って <c>varchar</c> / <c>VARCHAR2</c> として書き出すと、SQL Server は長さ 1 の列を作り
    /// MySQL / Oracle は構文エラーになるため、切替前の一覧で名指しするのが正しい。
    /// </remarks>
    [Theory(
        DisplayName = "長さ無し varchar の図は SQL Server / MySQL / Oracle への切替で変換不能一覧に載る"
    )]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("Oracle")]
    public void CreatePlan_LengthlessVarchar_IsUnconverted(string targetDialect)
    {
        var (diagram, columnId) = BuildLengthlessVarcharDiagram();

        ITypeCatalog target = targetDialect switch
        {
            "SqlServer" => new SqlServerTypeCatalog(),
            "MySql" => new MySqlTypeCatalog(),
            "Oracle" => new OracleTypeCatalog(),
            _ => throw new ArgumentOutOfRangeException(nameof(targetDialect)),
        };

        var plan = DiagramTypeConverter.CreatePlan(diagram, new PostgreSqlTypeCatalog(), target);

        plan.Converted.Should().BeEmpty();
        var unconverted = plan.Unconverted.Should().ContainSingle().Subject;
        unconverted.ColumnId.Should().Be(columnId);
        unconverted.TableName.Should().Be("notes");
        unconverted.ColumnName.Should().Be("memo");
        unconverted.OldType.Should().Be("varchar");
        unconverted.NewType.Should().BeNull();
    }

    /// <summary>長さを与えれば同じ図が 3 方言とも変換できる（長さ無しだけを弾いていることの対照）</summary>
    [Fact(DisplayName = "長さ付き varchar(30) は SQL Server への切替で従来どおり変換される")]
    public void CreatePlan_VarcharWithLength_IsConverted()
    {
        var (diagram, _) = BuildLengthlessVarcharDiagram();
        diagram.Entities[0].Columns[0].DataType = "varchar(30)";

        var plan = DiagramTypeConverter.CreatePlan(
            diagram,
            new PostgreSqlTypeCatalog(),
            new SqlServerTypeCatalog()
        );

        plan.Unconverted.Should().BeEmpty();
        // PostgreSQL の varchar は Unicode 可変長＝SQL Server では nvarchar
        plan.Converted.Should().ContainSingle().Which.NewType.Should().Be("nvarchar(30)");
    }

    /// <summary>
    /// 長さ無しの文字列列には <c>[DbColumnMeta]</c> の verbatim 併記（<c>NativeType</c>）が付く。
    /// </summary>
    /// <remarks>
    /// <para>
    /// トークンから同じ方言へ書き戻せない（<c>TryFormat</c> が <c>false</c>）列は、リバース側がトークン文字列を
    /// そのまま型として採るため綴りが必ず変わる。<see cref="CanonicalTypeTokenAttacher"/> はこの場合も元の表記を
    /// 刻み、C# リバースが綴りごと復元できるようにする。
    /// </para>
    /// <para>
    /// 長さ無しの列は QuickER が作れないが、他ツールが作ったスキーマの取込（SQL Server の
    /// <c>CREATE TABLE ... varchar</c> は長さ 1 の列になる）や MCP / 手編集で図に入りうる。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "長さ無しの文字列列は [DbColumnMeta] へ verbatim 併記が付く")]
    public void Attach_LengthlessStringColumn_RecordsVerbatimType()
    {
        var unicode = new Column
        {
            Name = "title",
            DataType = "nvarchar",
            IsNullable = true,
        };
        var ansi = new Column
        {
            Name = "code",
            DataType = "varchar",
            IsNullable = true,
        };
        var bounded = new Column
        {
            Name = "memo",
            DataType = "nvarchar(50)",
            IsNullable = true,
        };
        var diagram = new ErDiagram
        {
            Entities =
            {
                new Entity { TableName = "docs", Columns = { unicode, ansi, bounded } },
            },
        };

        var columnTypes = CanonicalTypeTokenAttacher.Attach(
            SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            diagram,
            new SqlServerTypeCatalog()
        );

        columnTypes[unicode.Id].CanonicalTypeToken.Should().Be("string");
        columnTypes[unicode.Id].VerbatimDbType.Should().Be("nvarchar");
        columnTypes[ansi.Id].CanonicalTypeToken.Should().Be("ansistring");
        columnTypes[ansi.Id].VerbatimDbType.Should().Be("varchar");

        // 長さ付きは従来どおり書き戻せるので併記は付かない
        columnTypes[bounded.Id].CanonicalTypeToken.Should().Be("string(50)");
        columnTypes[bounded.Id].VerbatimDbType.Should().BeNull();

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            columnTypes,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                GenerateEfCoreRepositories = false,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files.Should().ContainSingle().Subject.Content;
        content.Should().Contain("[DbColumnMeta(\"string\", NativeType = \"nvarchar\")]");
        content.Should().Contain("[DbColumnMeta(\"ansistring\", NativeType = \"varchar\")]");
        content.Should().Contain("[DbColumnMeta(\"string(50)\")]");
    }
}
