using System;
using System.Collections.Generic;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.GeneratedMultiTargetRowVersionFixture;

/// <summary>
/// rowversion 列を持つ図を「SQL Server ＋ SQLite のマルチターゲット」で生成するフィクスチャの単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// サーバー＝SQL Server・ローカル＝SQLite のハイブリッド構成（同期）の最小形を固定する。同じ <c>rowversion</c> 列を
/// SQL Server の型マッパは <c>byte[]</c>（<see cref="CSharpTypeInfo.IsRowVersion"/>）へ、SQLite の型マッパは
/// <c>DateTime</c>（<c>timestamp</c>＝日時の別名）へ解決するため、統一規則が無いと共有 Entity の型不一致で
/// 生成自体が診断エラーになる。本フィクスチャはその統一（<c>byte[]</c> 側へ寄せる）と、
/// 方言ごとの書き込み意味論の違いを 1 アセンブリへ焼き付ける。
/// </para>
/// <para>
/// 図は 1 テーブルの最小構成:
/// </para>
/// <list type="bullet">
///   <item><c>sync_items</c>: <c>item_id</c>（int・PK）／<c>name</c>（nvarchar(50)・NOT NULL）／<c>row_ver</c>（rowversion・NOT NULL）</item>
/// </list>
/// <para>
/// 生成物では、SQL Server 実装だけが <c>row_ver</c> を INSERT / UPDATE から外して版ガード SQL を組み立て、
/// SQLite 実装は通常のバイナリ列として書き込む（サーバー版のミラー置き場）。VO・EditModel・Mapper・EF Core は
/// 交差の焦点でないため生成しない（EF Core はマルチターゲットと排他でもある）。
/// </para>
/// </remarks>
public static class MultiTargetRowVersionFixtureDefinition
{
    /// <summary>生成フィクスチャの契約 namespace（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedMultiTargetRowVersionFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "MultiTargetRowVersionFixture.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQL Server / SQLite のQuickER 版 Repository を同時生成する（EF Core は併用不可）。
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
            GenerateEfCore = false,
            RepositoryDialects = ["sqlserver", "sqlite"],
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid ItemEntityId = new("b1000000-0000-0000-0000-000000000001");
    private static readonly Guid ItemPkColId = new("b1000000-0000-0000-0000-000000000002");
    private static readonly Guid ItemNameColId = new("b1000000-0000-0000-0000-000000000003");
    private static readonly Guid ItemRowVerColId = new("b1000000-0000-0000-0000-000000000004");

    /// <summary>マルチターゲット × rowversion の検証用 ER 図を決定的に構築する（型は SQL Server 表記）</summary>
    public static ErDiagram Build()
    {
        var item = new Entity
        {
            Id = ItemEntityId,
            TableName = "sync_items",
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
                new Column
                {
                    Id = ItemNameColId,
                    Name = "name",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
                // 実 SQL Server の rowversion に合わせ NOT NULL（DB が必ず採番する）。
                // SQLite へ方言変換すると BLOB かつ NULL 許容になる（未同期の行は空になるため）
                new Column
                {
                    Id = ItemRowVerColId,
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram { TargetDbms = "sqlserver", Entities = { item } };
    }

    /// <summary>
    /// 図を SQLite 方言へ変換した複製を返す（ローカル DB 側のスキーマ＝<c>rowversion</c> は BLOB・NULL 許容）。
    /// </summary>
    /// <remarks>
    /// 実運用の「SQL Server の図をローカル用に方言切替する」手順と同じ <see cref="DiagramTypeConverter"/> を通す。
    /// SQLite 実 DB テストのスキーマ作成にそのまま使う（変換規則の回帰も同時に押さえられる）。
    /// </remarks>
    public static ErDiagram BuildSqliteMirror()
    {
        var diagram = Build();
        var plan = DiagramTypeConverter.CreatePlan(
            diagram,
            new SqlServerTypeCatalog(),
            new SqliteTypeCatalog()
        );
        DiagramTypeConverter.Apply(diagram, plan);
        diagram.TargetDbms = "sqlite";

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
}
