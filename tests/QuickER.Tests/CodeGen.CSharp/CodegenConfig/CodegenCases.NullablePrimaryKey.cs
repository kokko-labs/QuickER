using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：主キーの列に NULL 許容の印がある図。C# 生成はその列を NOT NULL とみなし（DDL と同じ規則）、名指しの Info を出す
/// </summary>
/// <remarks>
/// <para>
/// 元のテストを持たない行：修正 L の受け入れ。網羅の配列の図の形（<see cref="ShapeDiagram"/>）に NULL 許容の主キーの水準が無かったため、
/// 同期の記述子が <c>int?</c> のプロパティを <c>int</c> の主キーとして読んでコンパイルできない（CS0266・文字列と値オブジェクトの主キーは
/// CS8603）見逃しと、インメモリのシードが主キーの null の行を入れて起動時に投げる見逃しを拾えなかった。
/// 図の形の水準 <c>int-nullable</c> は組で面を網羅するが、実効の同期（QuickER 版 sqlserver+sqlite）と NULL 許容の主キーの組を
/// 必ず含むとは限らないので、同期の 2 行とシードの行を決まった行で持つ。
/// </para>
/// <para>
/// 図は NULL 許容の主キーの表 <c>items</c> と、それを NULL 許容の外部キーで参照する子 <c>item_tags</c>（主キーは NOT NULL の int）。
/// 振る舞いはコンパイル（エラー 0・警告 0）と探針で確かめ、関係 <see cref="CodegenRelation.PrimaryKeyNotNull"/>（主キーを NOT NULL にした図と
/// 同じ生成物）を並べて掛ける。
/// </para>
/// </remarks>
internal static partial class CodegenCases
{
    /// <summary>主キーの列に NULL 許容の印がある図の行</summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> NullablePrimaryKeyRows() =>
        [
            NullablePrimaryKeyRow(
                "compile-nullable-primary-key-sync",
                "NULL 許容の int の主キー × 同期（QuickER 版 sqlserver+sqlite）：記述子が主キーを読めてコンパイルできる",
                () => NullablePrimaryKeyDiagram("int"),
                NullablePrimaryKeySyncOptions(),
                "items.item_id (int)"
            ),
            NullablePrimaryKeyRow(
                "compile-nullable-primary-key-sync-string-value-objects",
                "NULL 許容の文字列の主キー × 値オブジェクト × 同期：記述子の主キーの読み取りに null 許容の警告が出ずにコンパイルできる",
                () => NullablePrimaryKeyDiagram("nvarchar(50)"),
                NullablePrimaryKeySyncOptions() with
                {
                    GenerateValueObjects = true,
                },
                "items.item_id (nvarchar(50))"
            ),
            NullablePrimaryKeyRow(
                "probe-inmemory-seed-nullable-primary-key",
                "NULL 許容の int の主キー × インメモリ：シードがどの行にも主キーの値を入れ、どの表も 3 行で読み書きできる",
                () => NullablePrimaryKeyDiagram("int"),
                NullablePrimaryKeyOptions() with
                {
                    GenerateInMemoryRepositories = true,
                },
                "items.item_id (int)",
                probes: [CodegenProbe.InMemorySeed],
                probeFacts: new CodegenProbeFacts(
                    SeedRowCounts: new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["ItemEntity"] = 3,
                        ["ItemTagEntity"] = 3,
                    }
                )
            ),
            NullablePrimaryKeyRow(
                "relation-nullable-primary-key-all-surfaces",
                "NULL 許容の int の主キー × QuickER 版＋EF Core 版＋インメモリ＋リモートサービス＋EditModel / Mapper / 値オブジェクト＋API リファレンス："
                    + "主キーを NOT NULL にした図と同じ生成物で、コンパイルでき、EF Core のモデルが組める",
                () => NullablePrimaryKeyDiagram("int"),
                NullablePrimaryKeyOptions() with
                {
                    GenerateRepositories = true,
                    GenerateEfCoreRepositories = true,
                    GenerateInMemoryRepositories = true,
                    GenerateRemoteServices = true,
                    GenerateEditModels = true,
                    GenerateMappers = true,
                    GenerateValueObjects = true,
                    GenerateApiDocs = true,
                },
                "items.item_id (int)",
                probes: [CodegenProbe.EfCoreModel]
            ),
        ];

    /// <summary>行を組む（直接の入口・コンパイル・関係 PrimaryKeyNotNull・NULL 許容の主キーの Info が列を名指しする）</summary>
    private static ScenarioRow<CodegenCase> NullablePrimaryKeyRow(
        string id,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        string namedColumn,
        IReadOnlyList<CodegenProbe>? probes = null,
        CodegenProbeFacts? probeFacts = null
    ) =>
        new(
            id,
            null,
            description,
            new CodegenCase(
                diagram,
                options,
                DialectNames.SqlServer,
                DiagnosticExpectation.Contains(
                    new ExpectedDiagnostic(
                        GenerationDiagnosticSeverity.Info,
                        nameof(Strings.CodeGen_Info_NullablePrimaryKeyColumns),
                        [namedColumn]
                    )
                ),
                FileRules: [CompileGenerated],
                Compile: true,
                Probes: probes,
                Relations: [CodegenRelation.PrimaryKeyNotNull],
                ProbeFacts: probeFacts
            )
        );

    /// <summary>行の共通のオプション</summary>
    private static CodeGenerationOptions NullablePrimaryKeyOptions() =>
        new() { RootNamespace = "Sample.Domain" };

    /// <summary>同期を生成するオプション（QuickER 版 sqlserver+sqlite）</summary>
    private static CodeGenerationOptions NullablePrimaryKeySyncOptions() =>
        NullablePrimaryKeyOptions() with
        {
            GenerateRepositories = true,
            RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
            GenerateSyncSupport = true,
        };

    /// <summary>
    /// NULL 許容の主キーの表 <c>items(item_id, name)</c> と、それを NULL 許容の外部キーで参照する <c>item_tags(tag_id, item_id, label)</c>
    /// </summary>
    /// <param name="keyType">主キーと外部キーの型の表記（SQL Server）</param>
    private static ErDiagram NullablePrimaryKeyDiagram(string keyType)
    {
        var itemId = NullablePrimaryKeyColumn("item_id", keyType, primaryKey: true, nullable: true);
        var tagItemId = NullablePrimaryKeyColumn("item_id", keyType, nullable: true);
        tagItemId.IsForeignKey = true;

        var items = new Entity
        {
            TableName = "items",
            Columns = [itemId, NullablePrimaryKeyColumn("name", "nvarchar(50)")],
        };
        var tags = new Entity
        {
            TableName = "item_tags",
            Columns =
            [
                NullablePrimaryKeyColumn("tag_id", "int", primaryKey: true),
                tagItemId,
                NullablePrimaryKeyColumn("label", "nvarchar(20)", nullable: true),
            ],
        };

        return new ErDiagram
        {
            TargetDbms = DialectNames.SqlServer,
            Entities = [items, tags],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = items.Id,
                    TargetEntityId = tags.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new RelationshipColumnPair(itemId.Id, tagItemId.Id)],
                },
            ],
        };
    }

    /// <summary>列を作る</summary>
    private static Column NullablePrimaryKeyColumn(
        string name,
        string dataType,
        bool primaryKey = false,
        bool nullable = false
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataType = dataType,
            IsPrimaryKey = primaryKey,
            IsNullable = nullable,
        };
}
