using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Runtime;
using Xunit;
using EfCoreMetadata = QuickER.Runtime.EntityFrameworkCore.EntitySaveMetadata;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// EF Core 単独のスコープ（EF Core のランタイムパッケージを含む）の <c>EntitySaveMetadata</c> が、そのスコープで
/// 使うものだけを持つことを検証するテストクラス。
/// </summary>
/// <remarks>
/// EF Core は自分でクエリを組み立てて行を組み立て、生 SQL のエンティティ取得は列ごとの写し（<c>SetColumnValue</c>）で行うので、
/// QuickER 版 Repository の行の組み立て器（式木のコンパイル・型ごとの読み取り・値オブジェクトの速い経路・<c>MapEntity</c>）は
/// 出さない。例外文に出す列の一覧も、特定の方言の引用符で囲まない。ランタイムパッケージを手書きのエンティティで直接使う。
/// </remarks>
public class EfCoreEntitySaveMetadataTests
{
    /// <summary>QuickER 版 Repository のスコープにだけある、行の組み立て器のメンバーの名前</summary>
    private static readonly string[] MaterializerMemberNames =
    [
        "SelectMaterializer",
        "MapEntity",
        "BuildSelectMaterializer",
        "BuildColumnAssign",
        "BuildTypedReaders",
        "ResolveValueObjectReader",
        "ResolveValueObjectReaderCore",
        "CompileOnFirstUse",
        "_typedReaders",
        "_valueObjectReaderCache",
        "_isDbNullMethod",
        "_getValueMethod",
        "_setColumnValueMethod",
        "_columnMappingFailureMethod",
        "_rowStateProperty",
    ];

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

    /// <summary>引数なしのコンストラクタを持たないエンティティ（行の組み立て器を作ろうとすると失敗する型）</summary>
    [Table("orphans")]
    private sealed class NoDefaultConstructorEntity(int orphanId) : EntityBaseCore
    {
        [Key]
        [Column("orphan_id")]
        public int OrphanId { get; set; } = orphanId;
    }

    [Fact(
        DisplayName = "EF Core のランタイムパッケージの EntitySaveMetadata は、行の組み立て器のメンバーを持たない"
    )]
    public void Package_HasNoRowMaterializerMembers()
    {
        var present = typeof(EfCoreMetadata)
            .GetMembers(
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly
            )
            .Select(member => member.Name)
            .Intersect(MaterializerMemberNames)
            .ToList();

        present
            .Should()
            .BeEmpty("EF Core のスコープは行を自分で組み立てないので、組み立て器を出さない");

        // 生 SQL の列の解決は EF Core のスコープでも使うので残る
        typeof(EfCoreMetadata)
            .GetMethod(nameof(EfCoreMetadata.SelectOrdinals))
            .Should()
            .NotBeNull();
    }

    [Fact(DisplayName = "EF Core のメタデータは、引数なしのコンストラクタを持たない型でも作れる")]
    public void For_TypeWithoutParameterlessConstructor_Builds()
    {
        var metadata = EfCoreMetadata.For(typeof(NoDefaultConstructorEntity));

        metadata.KeyProperty.Name.Should().Be(nameof(NoDefaultConstructorEntity.OrphanId));
    }

    [Theory(
        DisplayName = "インラインの生成物は、EF Core 単独なら行の組み立て器を出さず、QuickER 版 Repository があれば出す"
    )]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void InlineGeneration_EmitsMaterializerOnlyWithQuickErRepositories(
        bool quickErRepositories,
        bool efCore,
        bool expected
    )
    {
        var provider = new SqliteProvider();
        var diagram = new ErDiagram
        {
            TargetDbms = "sqlite",
            Entities =
            {
                new Entity
                {
                    TableName = "items",
                    Columns =
                    {
                        new Column
                        {
                            Name = "item_id",
                            DataType = "INTEGER",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    },
                },
            },
        };

        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Materializer",
                GenerateRepositories = quickErRepositories,
                GenerateEfCoreRepositories = efCore,
            }
        );
        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));
        var code = string.Concat(result.Files.Select(file => file.Content));

        code.Contains("BuildSelectMaterializer").Should().Be(expected);
        code.Contains("public TEntity MapEntity<TEntity>").Should().Be(expected);
        code.Should().NotContain("CompileOnFirstUse");
    }

    [Fact(DisplayName = "EF Core のメタデータの列一覧は、方言の引用符で囲まない")]
    public void ColumnList_IsUnquoted()
    {
        EfCoreMetadata.For(typeof(ProbeEntity)).ColumnList.Should().Be("probe_id, group, x]y");
    }
}
