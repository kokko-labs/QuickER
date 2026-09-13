using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AwesomeAssertions;
using QuickER.Runtime;
using Xunit;
using SqliteMetadata = QuickER.Runtime.Sqlite.EntitySaveMetadata;
using SqlServerMetadata = QuickER.Runtime.SqlServer.EntitySaveMetadata;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成ランタイム（<c>EntitySaveMetadata</c>）が組み立てる SQL の識別子クォートを、実際に実行して検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// クォートは生成時ではなく<b>生成コードの実行時</b>に <c>[Table]</c> / <c>[Column]</c> 属性から組み立てられるため、
/// 生成テキストの文字列比較では確かめられない。ここではランタイムパッケージ（生成物と同一のテンプレート由来）の
/// <c>EntitySaveMetadata.For</c> を実際に呼び、組み上がった SQL を見る。
/// </para>
/// <para>
/// 固定するのは 2 点。(1) スキーマ修飾名は最初の <c>.</c> で分割して各部をクォートする
/// （まとめてクォートすると「ドットを含む 1 つのテーブル名」を指してしまい、dbo 以外のスキーマの図で全 CRUD が
/// 実行時エラーになる。DDL 生成側は元から分割クォートなので、食い違っていた）。
/// (2) 名前に含まれる終端クォート文字は二重化する（しないとクォートがそこで閉じる）。
/// </para>
/// </remarks>
public class GeneratedIdentifierQuotingTests
{
    /// <summary>スキーマ修飾名のテーブル（SQL Server の生成物が読むのと同じ属性だけを持つ）</summary>
    [Table("sales.orders")]
    private sealed class SchemaQualifiedEntity : EntityBaseCore
    {
        [Key]
        [Column("order_id")]
        public int OrderId { get; set; }

        [Column("memo")]
        public string? Memo { get; set; }
    }

    /// <summary>終端クォート文字を名前に含むテーブル（SQL Server の <c>]</c> と SQLite の <c>"</c> を同時に含める）</summary>
    [Table("we]ird\"table")]
    private sealed class QuoteInNameEntity : EntityBaseCore
    {
        [Key]
        [Column("i]d\"x")]
        public int Id { get; set; }
    }

    [Fact(
        DisplayName = "SQL Server の生成ランタイムはスキーマ修飾名を [schema].[table] へ分割クォートする"
    )]
    public void SqlServer_SchemaQualifiedTableName_IsSplitQuoted()
    {
        var metadata = SqlServerMetadata.For(typeof(SchemaQualifiedEntity));

        metadata.TableName.Should().Be("[sales].[orders]");
        metadata.SelectByIdSql.Should().Contain("FROM [sales].[orders]");
        metadata.DeleteSql.Should().Contain("DELETE FROM [sales].[orders]");
        // まとめてクォートした旧形は「ドットを含む 1 つの名前」を指すため実行時に落ちる
        metadata.SelectByIdSql.Should().NotContain("[sales.orders]");
    }

    [Fact(
        DisplayName = "SQLite の生成ランタイムはスキーマ修飾名を \"schema\".\"table\" へ分割クォートする"
    )]
    public void Sqlite_SchemaQualifiedTableName_IsSplitQuoted()
    {
        var metadata = SqliteMetadata.For(typeof(SchemaQualifiedEntity));

        metadata.TableName.Should().Be("\"sales\".\"orders\"");
        metadata.SelectByIdSql.Should().Contain("FROM \"sales\".\"orders\"");
        metadata.SelectByIdSql.Should().NotContain("\"sales.orders\"");
    }

    [Fact(DisplayName = "SQL Server の生成ランタイムは名前に含まれる ] を二重化する")]
    public void SqlServer_ClosingQuoteInName_IsDoubled()
    {
        var metadata = SqlServerMetadata.For(typeof(QuoteInNameEntity));

        // ] は二重化し、" は SQL Server の識別子では素の文字なのでそのまま
        metadata.TableName.Should().Be("[we]]ird\"table]");
        metadata.KeyColumnName.Should().Be("i]d\"x");
        metadata.SelectByIdSql.Should().Contain("[i]]d\"x]");
        metadata.ColumnList.Should().Contain("[i]]d\"x]");
    }

    [Fact(DisplayName = "SQLite の生成ランタイムは名前に含まれる \" を二重化する")]
    public void Sqlite_ClosingQuoteInName_IsDoubled()
    {
        var metadata = SqliteMetadata.For(typeof(QuoteInNameEntity));

        // " は二重化し、] は SQLite の識別子では素の文字なのでそのまま
        metadata.TableName.Should().Be("\"we]ird\"\"table\"");
        metadata.SelectByIdSql.Should().Contain("\"i]d\"\"x\"");
        metadata.ColumnList.Should().Contain("\"i]d\"\"x\"");
    }
}
