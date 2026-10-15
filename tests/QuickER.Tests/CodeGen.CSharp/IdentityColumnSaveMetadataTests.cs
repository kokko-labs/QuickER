using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using AwesomeAssertions;
using QuickER.Runtime;
using Xunit;
using SqliteMetadata = QuickER.Runtime.Sqlite.EntitySaveMetadata;
using SqlServerMetadata = QuickER.Runtime.SqlServer.EntitySaveMetadata;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成ランタイム（<c>EntitySaveMetadata</c>）が <c>[IdentityColumn]</c> の列を書き込みから外すことを、
/// ランタイムパッケージを実際に呼んで検証するテストクラス。
/// </summary>
/// <remarks>
/// 除外は生成時でなく<b>生成コードの実行時</b>に属性から組み立てられるため、生成テキストの比較では確かめられない。
/// ランタイムパッケージ（生成物と同一のテンプレート由来）の <c>EntitySaveMetadata.For</c> を呼び、
/// INSERT / UPDATE の対象から外れ、SELECT には残ることを両方言で見る。
/// </remarks>
public class IdentityColumnSaveMetadataTests
{
    /// <summary>主キーとは別に採番列を 1 本持つテーブル（生成物が読むのと同じ属性だけを持つ）</summary>
    [Table("items")]
    private sealed class IdentityEntity : EntityBaseCore
    {
        [Key]
        [Column("item_id")]
        public int ItemId { get; set; }

        [Column("qty")]
        public int Qty { get; set; }

        [Column("seq")]
        [IdentityColumn]
        public long Seq { get; set; }
    }

    [Fact(
        DisplayName = "SQL Server の生成ランタイムは [IdentityColumn] の列を INSERT / UPDATE から外し SELECT には残す"
    )]
    public void SqlServer_IdentityColumn_IsExcludedFromWrites()
    {
        var metadata = SqlServerMetadata.For(typeof(IdentityEntity));

        metadata.InsertProperties.Select(p => p.Name).Should().Equal("ItemId", "Qty");
        metadata.NonKeyProperties.Select(p => p.Name).Should().Equal("Qty");
        metadata.InsertSql.Should().NotContain("[seq]");
        metadata.UpdateSql.Should().NotContain("[seq]");
        metadata.ColumnList.Should().Contain("[seq]");
        metadata.SelectProperties.Select(p => p.Name).Should().Contain("Seq");
    }

    [Fact(
        DisplayName = "SQLite の生成ランタイムは [IdentityColumn] の列を INSERT / UPDATE から外し SELECT には残す"
    )]
    public void Sqlite_IdentityColumn_IsExcludedFromWrites()
    {
        var metadata = SqliteMetadata.For(typeof(IdentityEntity));

        metadata.InsertProperties.Select(p => p.Name).Should().Equal("ItemId", "Qty");
        metadata.NonKeyProperties.Select(p => p.Name).Should().Equal("Qty");
        metadata.InsertSql.Should().NotContain("\"seq\"");
        metadata.UpdateSql.Should().NotContain("\"seq\"");
        metadata.ColumnList.Should().Contain("\"seq\"");
        metadata.SelectProperties.Select(p => p.Name).Should().Contain("Seq");
    }
}
