using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Runtime;
using Xunit;
using EfCoreMetadata = QuickER.Runtime.EntityFrameworkCore.EntitySaveMetadata;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// EF Core のランタイムパッケージの <c>EntitySaveMetadata</c> が、EF Core 単独の構成で要るものだけを用意することを
/// 検証するテストクラス。
/// </summary>
/// <remarks>
/// EF Core は自分でクエリを組み立てて行を組み立てるので、行の組み立て器（式木のコンパイル）は使われるまで作らない。
/// 例外文に出す列の一覧も、特定の方言の引用符で囲まない。ランタイムパッケージを手書きのエンティティで直接使う。
/// </remarks>
public class EfCoreEntitySaveMetadataTests
{
    /// <summary>予約語と閉じ角括弧を名前に持つ列のテーブル</summary>
    [Table("probes")]
    private sealed class ProbeEntity : EntityBaseCore
    {
        [Key]
        [Column("probe_id")]
        public int ProbeId { get; set; }

        [Column("group")]
        public string Group { get; set; } = string.Empty;

        [Column("x]y")]
        public int Odd { get; set; }
    }

    /// <summary>引数なしのコンストラクタを持たないエンティティ（行の組み立て器は作れない）</summary>
    [Table("orphans")]
    private sealed class NoDefaultConstructorEntity(int orphanId) : EntityBaseCore
    {
        [Key]
        [Column("orphan_id")]
        public int OrphanId { get; set; } = orphanId;
    }

    [Fact(DisplayName = "EF Core のメタデータは、行の組み立て器を使われるまでコンパイルしない")]
    public void For_DoesNotCompileMaterializerUntilUsed()
    {
        // 組み立て器は引数なしのコンストラクタを呼ぶので、作ろうとした時点で失敗する型を使う
        var metadata = EfCoreMetadata.For(typeof(NoDefaultConstructorEntity));

        metadata.KeyProperty.Name.Should().Be(nameof(NoDefaultConstructorEntity.OrphanId));

        var map = () => metadata.SelectMaterializer(null!, []);
        map.Should().Throw<ArgumentException>("使った時点で初めてコンパイルされる");
    }

    [Fact(
        DisplayName = "EF Core のメタデータの行の組み立て器は、使われた時点でコンパイルされて行を写す"
    )]
    public void SelectMaterializer_CompilesOnFirstUse_AndMapsRows()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE probes (probe_id INTEGER PRIMARY KEY, \"group\" TEXT, \"x]y\" INTEGER);"
                + "INSERT INTO probes VALUES (1, 'a', 10), (2, 'b', 20);";
            create.ExecuteNonQuery();
        }

        using var query = connection.CreateCommand();
        query.CommandText = "SELECT probe_id, \"group\", \"x]y\" FROM probes ORDER BY probe_id";
        using var reader = query.ExecuteReader();
        var metadata = EfCoreMetadata.For(typeof(ProbeEntity));
        var mapped = new List<ProbeEntity>();

        while (reader.Read())
        {
            mapped.Add(metadata.MapEntity<ProbeEntity>(reader));
        }

        mapped
            .Select(probe => (probe.ProbeId, probe.Group, probe.Odd))
            .Should()
            .Equal((1, "a", 10), (2, "b", 20));
        mapped.Should().OnlyContain(probe => probe.RowState == RowState.Unchanged);
    }

    [Fact(DisplayName = "EF Core のメタデータの列一覧は、方言の引用符で囲まない")]
    public void ColumnList_IsUnquoted()
    {
        EfCoreMetadata.For(typeof(ProbeEntity)).ColumnList.Should().Be("probe_id, group, x]y");
    }
}
