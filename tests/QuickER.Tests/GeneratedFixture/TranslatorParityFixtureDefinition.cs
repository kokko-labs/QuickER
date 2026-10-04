using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.GeneratedTranslatorParityFixture;

/// <summary>
/// 生成ランタイムの式の翻訳・識別子の引用・Include の照合・UPDATE 文の組み立てを、
/// 「SQL Server ＋ SQLite のマルチターゲット ＋ インメモリ」で 1 つの名前空間へ生成するフィクスチャの単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 3 つの実装先（SQL Server の ADO・SQLite の ADO・インメモリ）が同じエンティティ型を共有するので、
/// 共通の基底 1 つ（<c>TranslatorParityRuntimeTestsBase</c>）へ同じシナリオを書き、派生で実装先だけを差し替えられる。
/// EF Core はマルチターゲットと生成時エラーで排他になるため含めない（C# の意味論の参照はインメモリと
/// LINQ to Objects の期待値が担う）。値オブジェクト・EditModel は焦点でないため生成しない
/// （値オブジェクトを外すのは <c>Nullable&lt;T&gt;.Value</c> の経路を素の列で通すため）。
/// </para>
/// <list type="bullet">
///   <item>
///     <c>probes</c>: 翻訳器の検証用。NULL 許容の可変長文字列・固定長文字列（<c>nchar(10)</c>）・日時・
///     オフセットつき日時・bool と、予約語の列名 <c>group</c>・閉じ角括弧を含む列名 <c>x]y</c> を持つ
///   </item>
///   <item>
///     <c>order</c> / <c>order_line</c>: 予約語の表名とナビゲーション名（<c>Order</c>）で、
///     FOR JSON の別名と相関の引用を確かめる
///   </item>
///   <item>
///     <c>big_parent</c> / <c>small_child</c>: 主キーが <c>bigint</c>（long）・外部キーが <c>int</c>（int）で、
///     CLR の型が違うキーの Include の照合を確かめる。SQL Server はこの組の外部キー制約を作れないので、
///     SQL Server の DDL には <see cref="BuildWithoutCrossTypeForeignKey"/> を使う
///   </item>
///   <item>
///     <c>tags</c>（キー列だけ）/ <c>versioned_tags</c>（キー列と rowversion）: 更新できる列が 1 本も無い表の UPDATE
///   </item>
/// </list>
/// </remarks>
public static class TranslatorParityFixtureDefinition
{
    /// <summary>生成フィクスチャの契約 namespace（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedTranslatorParityFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "TranslatorParityFixture.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQL Server / SQLite のQuickER 版 Repository とインメモリ Repository を同時に生成する。
    /// </summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = true,
            GenerateValueObjects = false,
            GenerateEfCoreRepositories = false,
            GenerateInMemoryRepositories = true,
            RepositoryDialects = ["sqlserver", "sqlite"],
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid ProbeId = new("7a000000-0000-0000-0000-000000000001");
    private static readonly Guid ProbePkColId = new("7a000000-0000-0000-0000-000000000002");
    private static readonly Guid ProbeNameColId = new("7a000000-0000-0000-0000-000000000003");
    private static readonly Guid ProbeCodeColId = new("7a000000-0000-0000-0000-000000000004");
    private static readonly Guid ProbeAtColId = new("7a000000-0000-0000-0000-000000000005");
    private static readonly Guid ProbeAtOffsetColId = new("7a000000-0000-0000-0000-000000000006");
    private static readonly Guid ProbeIsActiveColId = new("7a000000-0000-0000-0000-000000000007");
    private static readonly Guid ProbeGroupColId = new("7a000000-0000-0000-0000-000000000008");
    private static readonly Guid ProbeBracketColId = new("7a000000-0000-0000-0000-000000000009");

    private static readonly Guid OrderId = new("7a000000-0000-0000-0000-000000000011");
    private static readonly Guid OrderPkColId = new("7a000000-0000-0000-0000-000000000012");
    private static readonly Guid OrderGroupColId = new("7a000000-0000-0000-0000-000000000013");
    private static readonly Guid LineId = new("7a000000-0000-0000-0000-000000000021");
    private static readonly Guid LinePkColId = new("7a000000-0000-0000-0000-000000000022");
    private static readonly Guid LineOrderFkColId = new("7a000000-0000-0000-0000-000000000023");
    private static readonly Guid RelOrderLines = new("7a000000-0000-0000-0000-000000000024");

    private static readonly Guid BigParentId = new("7a000000-0000-0000-0000-000000000031");
    private static readonly Guid BigParentPkColId = new("7a000000-0000-0000-0000-000000000032");
    private static readonly Guid BigParentLabelColId = new("7a000000-0000-0000-0000-000000000033");
    private static readonly Guid SmallChildId = new("7a000000-0000-0000-0000-000000000041");
    private static readonly Guid SmallChildPkColId = new("7a000000-0000-0000-0000-000000000042");
    private static readonly Guid SmallChildFkColId = new("7a000000-0000-0000-0000-000000000043");
    private static readonly Guid RelBigParentChildren = new("7a000000-0000-0000-0000-000000000044");

    private static readonly Guid TagId = new("7a000000-0000-0000-0000-000000000051");
    private static readonly Guid TagPkColId = new("7a000000-0000-0000-0000-000000000052");
    private static readonly Guid VersionedTagId = new("7a000000-0000-0000-0000-000000000061");
    private static readonly Guid VersionedTagPkColId = new("7a000000-0000-0000-0000-000000000062");
    private static readonly Guid VersionedTagRowVerColId = new(
        "7a000000-0000-0000-0000-000000000063"
    );

    /// <summary>翻訳器のパリティ検証用 ER 図を決定的に構築する（型は SQL Server 表記）</summary>
    public static ErDiagram Build()
    {
        var probe = new Entity
        {
            Id = ProbeId,
            TableName = "probes",
            Columns =
            {
                Key(ProbePkColId, "probe_id", "int"),
                Nullable(ProbeNameColId, "name", "nvarchar(50)"),
                Nullable(ProbeCodeColId, "code", "nchar(10)"),
                Nullable(ProbeAtColId, "at", "datetime2"),
                Nullable(ProbeAtOffsetColId, "at_offset", "datetimeoffset"),
                new Column
                {
                    Id = ProbeIsActiveColId,
                    Name = "is_active",
                    DataType = "bit",
                    IsNullable = false,
                },
                Nullable(ProbeGroupColId, "group", "nvarchar(20)"),
                Nullable(ProbeBracketColId, "x]y", "nvarchar(20)"),
            },
        };

        var order = new Entity
        {
            Id = OrderId,
            TableName = "order",
            Columns =
            {
                Key(OrderPkColId, "order_id", "int"),
                Nullable(OrderGroupColId, "group", "nvarchar(20)"),
            },
        };

        var line = new Entity
        {
            Id = LineId,
            TableName = "order_line",
            Columns =
            {
                Key(LinePkColId, "line_id", "int"),
                new Column
                {
                    Id = LineOrderFkColId,
                    Name = "order_id",
                    DataType = "int",
                    IsNullable = false,
                },
            },
        };

        var bigParent = new Entity
        {
            Id = BigParentId,
            TableName = "big_parent",
            Columns =
            {
                Key(BigParentPkColId, "id", "bigint"),
                Nullable(BigParentLabelColId, "label", "nvarchar(20)"),
            },
        };

        var smallChild = new Entity
        {
            Id = SmallChildId,
            TableName = "small_child",
            Columns =
            {
                Key(SmallChildPkColId, "id", "int"),
                Nullable(SmallChildFkColId, "big_parent_id", "int"),
            },
        };

        var tag = new Entity
        {
            Id = TagId,
            TableName = "tags",
            Columns = { Key(TagPkColId, "name", "nvarchar(20)") },
        };

        var versionedTag = new Entity
        {
            Id = VersionedTagId,
            TableName = "versioned_tags",
            Columns =
            {
                Key(VersionedTagPkColId, "name", "nvarchar(20)"),
                new Column
                {
                    Id = VersionedTagRowVerColId,
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = false,
                },
            },
        };

        var diagram = new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities = { probe, order, line, bigParent, smallChild, tag, versionedTag },
        };

        diagram.Relationships.Add(
            new Relationship
            {
                Id = RelOrderLines,
                Type = RelationshipType.OneToMany,
                SourceEntityId = OrderId,
                TargetEntityId = LineId,
                ColumnPairs = [new(OrderPkColId, LineOrderFkColId)],
                ConstraintName = "FK_order_line_order",
                OnDelete = ForeignKeyReferentialAction.NoAction,
                OnUpdate = ForeignKeyReferentialAction.NoAction,
            }
        );

        diagram.Relationships.Add(
            new Relationship
            {
                Id = RelBigParentChildren,
                Type = RelationshipType.OneToMany,
                SourceEntityId = BigParentId,
                TargetEntityId = SmallChildId,
                ColumnPairs = [new(BigParentPkColId, SmallChildFkColId)],
                ConstraintName = "FK_small_child_big_parent",
                OnDelete = ForeignKeyReferentialAction.NoAction,
                OnUpdate = ForeignKeyReferentialAction.NoAction,
            }
        );

        return diagram;
    }

    /// <summary>
    /// <see cref="Build"/> から、主キー（bigint）と外部キー（int）の型が違うリレーションだけを外した図を返す。
    /// </summary>
    /// <remarks>
    /// SQL Server は型の違う列の間に外部キー制約を作れない（Msg 1778）。SQL Server の DDL はこの図から作り、
    /// 生成コードのナビゲーションはそのまま使う（FOR JSON の相関は制約が無くても動く）。
    /// </remarks>
    public static ErDiagram BuildWithoutCrossTypeForeignKey()
    {
        var diagram = Build();
        var crossType = diagram.Relationships.Single(r => r.Id == RelBigParentChildren);
        diagram.Relationships.Remove(crossType);

        return diagram;
    }

    /// <summary>
    /// 主辞書（図の方言＝SQL Server）と、実効方言（sqlserver / sqlite）ごとに解決した方言辞書を返す。
    /// マルチ辞書オーバーロードの入力に使う。
    /// </summary>
    public static (
        IReadOnlyDictionary<Guid, CSharpTypeInfo> Primary,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> ByDialect
    ) ResolveColumnTypes(ErDiagram diagram)
    {
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = primary,
            ["sqlite"] = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram),
        };

        return (primary, byDialect);
    }

    private static Column Key(Guid id, string name, string dataType) =>
        new()
        {
            Id = id,
            Name = name,
            DataType = dataType,
            IsPrimaryKey = true,
            IsNullable = false,
        };

    private static Column Nullable(Guid id, string name, string dataType) =>
        new()
        {
            Id = id,
            Name = name,
            DataType = dataType,
            IsNullable = true,
        };
}
