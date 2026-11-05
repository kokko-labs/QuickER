using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 分割生成の固定 infra ファイル（<c>Runtime*.g.cs</c>）とスキーマ依存物のファイルが分離していることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 残している検証対象は、固定 infra とスキーマ依存物の型集合が排他であること・同期の固定エンジンと per-entity の分離（本文の文字列）。
/// </para>
/// <para>
/// 「分割ファイル ⇔ パッケージ」の 1:1 対応（<c>Runtime*.g.cs</c> のトップレベル型宣言の集合が、対応するパッケージ用ソース
/// （<see cref="RuntimePackageSourceRenderer"/> の出力）の型の集合と一致）は、<c>CodegenCaseTests</c> の行
/// <c>relation-runtime-type-set-*</c> と網羅の配列の分割・インラインの行の関係 RuntimeTypeSet へ、スキーマ依存物が
/// 「パッケージ参照モードの on/off で本体不変」であることは、<c>CodegenCaseTests</c> の行
/// <c>relation-runtime-packages-split-multi-dialect</c> と網羅の配列の分割・パッケージ参照の行の関係 RuntimePackages
/// （<c>Judges/Relations.cs</c>）へ移した。
/// </para>
/// </remarks>
public class SplitRuntimeSymmetryTests
{
    /// <summary>
    /// 同期支援の分割生成でも、固定 infra（<c>Runtime.Sync.g.cs</c>）とスキーマ依存物
    /// （<c>Repositories.Sync.g.cs</c>）の分離が保たれることを検証する。
    /// </summary>
    [Fact]
    public void SplitSyncFiles_ShouldSeparateFixedEngineFromPerEntityCode()
    {
        var files = GenerateSyncSplit();

        // 2 つのファイルがどちらも出ることは、決まった行 files-split-sync-fixed-engine-and-per-entity-files が見る

        // 固定エンジン側に per-entity 生成物（記述子・デコレータ・DI 登録）が混じらない
        // （汎用基底 JournalingRepositoryBase 系は固定エンジンの持ち物＝具象デコレータだけを弾く）
        files["Runtime.Sync.g.cs"].Should().NotContain("sealed class Journaling");
        files["Runtime.Sync.g.cs"]
            .Should()
            .Contain("public abstract class JournalingRepositoryBase<TEntity, TKey>");
        files["Runtime.Sync.g.cs"].Should().NotContain("AddGeneratedSyncSupport");
        files["Runtime.Sync.g.cs"]
            .Should()
            .NotContain(
                "Microsoft.Extensions.DependencyInjection",
                "同期の固定エンジンは DI へ触れず、パッケージの依存ゼロを保つ"
            );

        // per-entity 側に固定エンジンの型定義が混じらない
        files["Repositories.Sync.g.cs"].Should().NotContain("class SyncEngine");
        files["Repositories.Sync.g.cs"].Should().NotContain("class SyncJournal");
    }

    /// <summary>
    /// 固定 infra ファイルにスキーマ依存物（エンティティ別の型）が 1 つも混じらないことを検証する
    /// （型集合の一致は「パッケージ側にも同じ漏れがある」場合に素通りしうるため、独立した観点で押さえる）。
    /// </summary>
    [Fact]
    public void SplitRuntimeFiles_ShouldNotContainSchemaDependentTypes()
    {
        var files = GenerateSplit(MultiDialectOptions());

        var fixedTypes = files
            .Where(file => IsFixedRuntimeFile(file.Key))
            .SelectMany(file => DeclaredTypes(file.Value))
            .ToHashSet(StringComparer.Ordinal);
        var schemaTypes = files
            .Where(file => !IsFixedRuntimeFile(file.Key))
            .SelectMany(file => DeclaredTypes(file.Value))
            .ToHashSet(StringComparer.Ordinal);

        fixedTypes.Should().NotBeEmpty();
        schemaTypes.Should().NotBeEmpty();
        fixedTypes
            .Should()
            .NotIntersectWith(
                schemaTypes,
                "固定 infra ファイルとスキーマ依存ファイルの型集合は排他でなければならない"
            );

        foreach (var (fileName, content) in files.Where(file => IsFixedRuntimeFile(file.Key)))
        {
            // DI 登録拡張（AddGenerated*Repositories / AddSaveHook）はスキーマ依存物のため固定 infra 側へ出ない
            // （EF Core 固定 infra だけは DbContextOptions 拡張の ApplyServices のために DI 抽象を使うが、
            //   本構成はマルチ方言＝EF Core と排他のため対象外）
            content.Should().NotContain("ServiceCollectionExtensions", $"{fileName}");

            // サーバー固定部（Runtime.AspNetCore.g.cs）は EF Core 固定 infra と同じ例外＝エンジン自身が
            // リポジトリ・ILoggerFactory を DI から解決するため DI 抽象が必須で、ASP.NET Core の
            // FrameworkReference が推移的に連れてくるので依存も増えない
            if (!fileName.Contains("AspNetCore", StringComparison.Ordinal))
            {
                content
                    .Should()
                    .NotContain("Microsoft.Extensions.DependencyInjection", $"{fileName}");
            }
        }
    }

    // ---- ヘルパー ----

    /// <summary>固定 infra ファイル（Runtime 系）かどうか</summary>
    private static bool IsFixedRuntimeFile(string fileName) =>
        fileName.StartsWith("Runtime.", StringComparison.Ordinal);

    /// <summary>
    /// ソースからトップレベル（列 0）の型宣言名を抽出する。
    /// </summary>
    /// <remarks>
    /// 生成コードはファイルスコープ名前空間のため、トップレベル型はすべて列 0 から始まる（入れ子型は字下げされ対象外）。
    /// <c>record struct</c> / <c>record class</c> は素の <c>struct</c> / <c>class</c> へ正規化してから走査する。
    /// </remarks>
    private static IReadOnlyList<string> DeclaredTypes(string content)
    {
        var normalized = content
            .Replace("record struct ", "struct ", StringComparison.Ordinal)
            .Replace("record class ", "class ", StringComparison.Ordinal);

        return TypeDeclarationRegex
            .Matches(normalized)
            .Select(FormatTypeName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>型宣言 1 件を「名前＋型引数の個数」（例 <c>IValueObject`1</c>）へ正規化する</summary>
    /// <remarks>
    /// アリティまで含めるのは、同名でアリティだけが違う型が別々のファイルへ宣言されるため
    /// （拡張シムの <c>IValueObject</c> は per-型ファイル・固定 infra の <c>IValueObject&lt;TSelf&gt;</c> /
    /// <c>IValueObject&lt;TSelf, TValue&gt;</c> は Runtime ファイル）。名前だけで突き合わせると別物どうしが
    /// 同一視され、「固定 infra とスキーマ依存の型集合は排他」の判定が誤検知する。
    /// </remarks>
    private static string FormatTypeName(Match match)
    {
        var generics = match.Groups["generics"].Value;

        if (string.IsNullOrEmpty(generics))
        {
            return match.Groups["name"].Value;
        }

        var arity = generics.Count(character => character == ',') + 1;
        return $"{match.Groups["name"].Value}`{arity}";
    }

    /// <summary>トップレベル型宣言（列 0・public / internal）を拾う正規表現</summary>
    private static readonly Regex TypeDeclarationRegex = new(
        @"^(?:public|internal)(?:\s+(?:sealed|abstract|static|partial|readonly|ref))*\s+(?:class|interface|struct|enum|record)\s+(?<name>\w+)(?<generics><[^>]*>)?",
        RegexOptions.Multiline | RegexOptions.Compiled
    );

    /// <summary>分割生成を実行し、ファイル名→内容の辞書を返す</summary>
    private static IReadOnlyDictionary<string, string> GenerateSplit(CodeGenerationOptions options)
    {
        var diagram = Diagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = primary,
            ["sqlite"] = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram),
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result.HasErrors.Should().BeFalse();

        return result.Files.ToDictionary(file => file.FileName, file => file.Content);
    }

    /// <summary>
    /// 同期支援つきの分割生成を実行し、ファイル名→内容の辞書を返す（図は同期フィクスチャの単一ソースを共有する）。
    /// </summary>
    private static IReadOnlyDictionary<string, string> GenerateSyncSplit()
    {
        var diagram = GeneratedSyncFixture.SyncFixtureDefinition.Build();
        var (primary, byDialect) = GeneratedSyncFixture.SyncFixtureDefinition.ResolveColumnTypes(
            diagram
        );
        var options = GeneratedSyncFixture.SyncFixtureDefinition.Options with
        {
            SplitFilesByCategory = true,
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result.HasErrors.Should().BeFalse();

        return result.Files.ToDictionary(file => file.FileName, file => file.Content);
    }

    /// <summary>全機能 ON（パッケージ書き出しと同条件）のマルチ方言分割オプション</summary>
    private static CodeGenerationOptions MultiDialectOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            SplitFilesByCategory = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            RepositoryDialects = ["sqlserver", "sqlite"],
            GenerateRemoteServices = true,
            GenerateValueObjects = true,
            IncludeDataAnnotations = true,
            IncludeJsonIgnoreOnParentNavigation = true,
            ExcludeUnboundedBinaryColumns = true,
        };

    /// <summary>
    /// 説明・UNIQUE 制約・無制限バイナリ列を含む図（属性型の出力条件をすべて満たしてパッケージ書き出しと同じ
    /// 固定 infra 集合になるようにする）。
    /// </summary>
    private static ErDiagram Diagram()
    {
        var customer = Guid.NewGuid();
        var customerPk = Guid.NewGuid();
        var customerCode = Guid.NewGuid();
        var order = Guid.NewGuid();
        var orderPk = Guid.NewGuid();
        var orderFk = Guid.NewGuid();

        return new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = customer,
                    TableName = "customers",
                    // [DbTableMeta] / [DbColumnMeta] の出力条件（説明が 1 つでもある）を満たす
                    Description = "Customer master",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerPk,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        // 文字列列は Unicode で統一（Ansi/Unicode 差で canonical トークンが割れるため）
                        new Column
                        {
                            Id = customerCode,
                            Name = "code",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                            Description = "Business code",
                        },
                        // 無制限バイナリ列（除外オプションの経路と [UnboundedBinaryColumn] を有効化する）
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "photo",
                            DataType = "varbinary(max)",
                            IsNullable = true,
                        },
                    ],
                    // [UniqueConstraint] 属性型の出力条件を満たす
                    UniqueConstraints =
                    [
                        new UniqueConstraint
                        {
                            Id = Guid.NewGuid(),
                            Name = "UQ_customers_code",
                            ColumnIds = [customerCode],
                        },
                    ],
                },
                new Entity
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = orderPk,
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = orderFk,
                            Name = "customer_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    ColumnPairs = [new(customerPk, orderFk)],
                },
            ],
        };
    }
}
