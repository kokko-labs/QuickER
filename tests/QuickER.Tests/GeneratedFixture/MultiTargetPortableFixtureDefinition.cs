using System;
using System.Collections.Generic;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Sqlite;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedFixture;

namespace QuickER.Tests.GeneratedMultiTargetFixture;

/// <summary>
/// QuickER 版 Repository のマルチターゲット構成（SQL Server / SQLite の 2 方言同時生成）を含む
/// 「第4の固定フィクスチャ」を生成する単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 入力の ER 図は既存の方言可搬フィクスチャ（<see cref="Tests.GeneratedPortableFixture.PortableFixtureDefinition"/>）と
/// <b>同一</b>（rowversion なし・2 エンティティ・1対多カスケード・int/string/decimal のみ）。相違はオプションのみで、
/// <see cref="CodeGenerationOptions.RepositoryDialects"/> = <c>["sqlserver", "sqlite"]</c>・
/// <c>GenerateRepositories=true</c>・<c>GenerateEfCoreRepositories=false</c>（EF Core はマルチターゲットと排他）・VO 有効で生成する。
/// これにより「契約 1 回（<c>QuickER.Tests.GeneratedMultiTargetFixture</c>）＋方言別 namespace 実装
/// （<c>.SqlServer</c> / <c>.Sqlite</c>）＋方言別 DI 拡張（AddGeneratedSqlServerRepositories /
/// AddGeneratedSqliteRepositories・keyed 版）」を 1 つのアセンブリへ載せ、同一プロセスで両方言を keyed DI 解決する
/// 統合テストの入力にできる。
/// </para>
/// <para>
/// 主辞書＝図の方言（SQL Server 型表記基準）で解決し、方言辞書に sqlserver / sqlite の両方を積んで
/// マルチ辞書オーバーロード（<see cref="CSharpCodeGenerationService.Generate(ErDiagram,
/// IReadOnlyDictionary{Guid, CSharpTypeInfo},
/// IReadOnlyDictionary{string, IReadOnlyDictionary{Guid, CSharpTypeInfo}}, CodeGenerationOptions)"/>）へ渡す。
/// namespace は既存フィクスチャ 3 つと衝突しない専用のもの。
/// </para>
/// </remarks>
public static class MultiTargetPortableFixtureDefinition
{
    /// <summary>生成フィクスチャの契約 namespace（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedMultiTargetFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "MultiTargetPortableFixture.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQL Server / SQLite のQuickER 版 Repository を同時生成する（EF Core は併用不可）。
    /// </summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateValueObjects = true,
            GenerateEfCoreRepositories = false,
            RepositoryDialects = ["sqlserver", "sqlite"],
            SplitFilesByCategory = false,
        };

    /// <summary>
    /// フィクスチャの ER 図を返す。図の中身は方言可搬フィクスチャと同一（SQL Server 型表記基準）で、
    /// SQL Server / SQLite の型カタログはこの表記を同じ C# 型へ解決する（可搬型のみで構成）。
    /// これにグラフ取得糖衣の edge-skip 検証用の自己参照テーブルを加える
    /// （<see cref="SelfReferenceTableDefinition"/>＝EF Core を生成しない本フィクスチャだけが置ける）。
    /// </summary>
    public static ErDiagram Build()
    {
        var diagram = Tests.GeneratedPortableFixture.PortableFixtureDefinition.Build(
            Tests.GeneratedPortableFixture.PortableDialect.SqlServer
        );
        SelfReferenceTableDefinition.AddTo(diagram);

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
