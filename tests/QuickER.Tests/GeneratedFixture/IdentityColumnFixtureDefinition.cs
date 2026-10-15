using System;
using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedIdentityColumnFixture;

/// <summary>
/// 主キーでない自動採番列（<see cref="Column.IsIdentity"/>）を持つ図を、SQL Server 方言の
/// QuickER 版 Repository ＋ EF Core で生成するフィクスチャの単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 固定する対象は「自動採番列がマーカー属性 <c>[IdentityColumn]</c> を持ち、<c>EntitySaveMetadata</c> が
/// INSERT / BulkInsert / UPDATE の対象から外す（SELECT では読む）」という規則と、
/// EF Core の Fluent が <c>ValueGeneratedOnAddOrUpdate()</c> を出して明示値を送らせない規則。
/// 自動採番列は DB が値を決めて明示の値を拒むため、普通の列として扱うとそのテーブルへの全書き込みが失敗する。
/// </para>
/// <para>
/// 図は 1 テーブルの最小構成:
/// </para>
/// <list type="bullet">
///   <item><c>identity_items</c>: <c>item_id</c>（int・PK・アプリ側採番）／
///   <c>seq</c>（int・NOT NULL・<b>非主キーの自動採番列</b>）／<c>name</c>（nvarchar(50)・NOT NULL）</item>
/// </list>
/// <para>
/// EditModel / Mapper / VO / インメモリ / リモートは交差の焦点でないため生成しない。
/// 実スキーマはテスト側の DDL が作る（SQL Server は <c>IDENTITY(1,1)</c>、PostgreSQL は <c>GENERATED ALWAYS AS IDENTITY</c>）。
/// </para>
/// </remarks>
public static class IdentityColumnFixtureDefinition
{
    /// <summary>生成フィクスチャの契約 namespace（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedIdentityColumnFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "IdentityColumnFixture.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQL Server 方言のQuickER 版 Repository と EF Core を同時に生成する（方言が単一なら併用できる）。
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
            GenerateEfCoreRepositories = true,
            RepositoryDialects = ["sqlserver"],
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid ItemEntityId = new("c1000000-0000-0000-0000-000000000001");
    private static readonly Guid ItemPkColId = new("c1000000-0000-0000-0000-000000000002");
    private static readonly Guid ItemSeqColId = new("c1000000-0000-0000-0000-000000000003");
    private static readonly Guid ItemNameColId = new("c1000000-0000-0000-0000-000000000004");

    /// <summary>自動採番列の検証用 ER 図を決定的に構築する（型は SQL Server 表記）</summary>
    public static ErDiagram Build()
    {
        var item = new Entity
        {
            Id = ItemEntityId,
            TableName = "identity_items",
            Columns =
            {
                new Column
                {
                    Id = ItemPkColId,
                    Name = "item_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                // 非主キーの自動採番列。採番の開始値・増分は意味モデルに載らないため、
                // 運ぶのは「DB が値を決め、明示の値を拒む」という事実だけ。実スキーマはテスト側の DDL が作る
                new Column
                {
                    Id = ItemSeqColId,
                    Name = "seq",
                    DataType = "int",
                    IsNullable = false,
                    IsIdentity = true,
                },
                new Column
                {
                    Id = ItemNameColId,
                    Name = "name",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram { TargetDbms = "sqlserver", Entities = { item } };
    }
}
