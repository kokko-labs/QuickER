using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// 「長さを要する型の長さ無しは書き出せない」（<see cref="ITypeCatalog.TryFormat"/> が <c>false</c>）ことの
/// 波及先のうち、コード生成の出力（verbatim 併記）を固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ITypeCatalog.TryFormat"/> の呼び出し元は 4 つで、長さ無しの正規型に対する振る舞いは次のとおり:
/// </para>
/// <list type="bullet">
///   <item><see cref="DiagramTypeConverter"/>: 対象 DBMS 切替の<b>変換不能一覧</b>へ載る（方言の型の表の <c>lengthless-convert-*</c>）</item>
///   <item><see cref="CanonicalTypeTokenAttacher"/>: 書き戻せないので<b>verbatim 併記</b>が付く（当クラス）</item>
///   <item><c>CSharpReverseParser</c>: 警告つきで<b>トークン文字列をそのまま列の型に採る</b>（CodeReverse 側のテスト）</item>
///   <item><c>QueryParameterTypeResolver</c>: パラメータは列を作らないので<b>「無制限」として解決し直す</b>（CodeGen 側のテスト）</item>
/// </list>
/// <para>
/// 種別ごとの可否そのものは方言の型の表（<c>DialectTypeRows.Token.cs</c> の <c>format-token-*</c>）が正本。
/// </para>
/// </remarks>
public class LengthlessTypeFormattingTests
{
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
