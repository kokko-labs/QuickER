using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 分割生成の固定 infra ファイル（<c>Runtime*.g.cs</c>）が、配布 NuGet パッケージ
/// （<see cref="RuntimePackageSourceRenderer"/> の出力）と同じ守備範囲になっていることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 「分割ファイル ⇔ パッケージ」の対応は 1:1（<c>Runtime.g.cs</c>⇔Core・<c>Runtime.SqlServer.g.cs</c>⇔SqlServer・
/// <c>Runtime.Sqlite.g.cs</c>⇔Sqlite・<c>Runtime.EntityFrameworkCore.g.cs</c>⇔EF Core）。バイト一致は原理的に成り立たない
/// （名前空間・可視性 internal/public・パッケージ側のヘッダコメントが異なる）ため、<b>トップレベル型宣言の集合</b>で
/// 構造一致を固定する。境界がずれれば「片方にしかない型」として必ず落ちる。
/// </para>
/// <para>
/// あわせて、スキーマ依存物の <c>Repositories*.g.cs</c> が「パッケージ参照モードの on/off で本体不変」
/// （差は using だけ）であることも固定する。これが崩れると「固定 infra は Runtime 系・スキーマ依存物は
/// Repositories 系」という分離が壊れている。
/// </para>
/// </remarks>
public class SplitRuntimeSymmetryTests
{
    private static readonly RuntimePackageSourceRenderer PackageRenderer = new();

    /// <summary>
    /// 分割の <c>Runtime.g.cs</c> / <c>Runtime.SqlServer.g.cs</c> / <c>Runtime.Sqlite.g.cs</c> が、
    /// コア／方言エンジンパッケージと同じトップレベル型集合になることを検証する。
    /// </summary>
    [Fact]
    public void SplitRuntimeFiles_ShouldDeclareSameTypesAsRuntimePackages()
    {
        var files = GenerateSplit(MultiDialectOptions());

        DeclaredTypes(files["Runtime.g.cs"])
            .Should()
            .BeEquivalentTo(
                DeclaredTypes(PackageRenderer.RenderCore()),
                "Runtime.g.cs はコアパッケージ QuickER.Runtime と同じ守備範囲でなければならない"
            );

        DeclaredTypes(files["Runtime.SqlServer.g.cs"])
            .Should()
            .BeEquivalentTo(
                DeclaredTypes(PackageRenderer.RenderSqlServer()),
                "Runtime.SqlServer.g.cs は QuickER.Runtime.SqlServer と同じ守備範囲でなければならない"
            );

        DeclaredTypes(files["Runtime.Sqlite.g.cs"])
            .Should()
            .BeEquivalentTo(
                DeclaredTypes(PackageRenderer.RenderSqlite()),
                "Runtime.Sqlite.g.cs は QuickER.Runtime.Sqlite と同じ守備範囲でなければならない"
            );
    }

    /// <summary>
    /// 分割の <c>Runtime.EntityFrameworkCore.g.cs</c> が EF Core パッケージと同じトップレベル型集合になることを検証する
    /// （マルチ方言と EF Core は排他のため単独構成で確認する）。
    /// </summary>
    [Fact]
    public void SplitEfCoreRuntimeFile_ShouldDeclareSameTypesAsEfCorePackage()
    {
        var files = GenerateSplit(EfCoreOnlyOptions());

        DeclaredTypes(files["Runtime.EntityFrameworkCore.g.cs"])
            .Should()
            .BeEquivalentTo(
                DeclaredTypes(PackageRenderer.RenderEfCore()),
                "Runtime.EntityFrameworkCore.g.cs は QuickER.Runtime.EntityFrameworkCore と同じ守備範囲でなければならない"
            );
    }

    /// <summary>
    /// 分割の <c>Runtime.InMemory.g.cs</c> がインメモリ基盤パッケージと同じトップレベル型集合になることを検証する。
    /// </summary>
    [Fact]
    public void SplitInMemoryRuntimeFile_ShouldDeclareSameTypesAsInMemoryPackage()
    {
        var files = GenerateSplit(InMemoryOptions());

        DeclaredTypes(files["Runtime.InMemory.g.cs"])
            .Should()
            .BeEquivalentTo(
                DeclaredTypes(PackageRenderer.RenderInMemory()),
                "Runtime.InMemory.g.cs は QuickER.Runtime.InMemory と同じ守備範囲でなければならない"
            );
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
            content.Should().NotContain("Microsoft.Extensions.DependencyInjection", $"{fileName}");
        }
    }

    /// <summary>
    /// パッケージ参照モードの分割生成では固定 infra ファイルが 1 本も出力されないことを検証する
    /// （「Runtime 系が出ない」だけがモードの差になる）。
    /// </summary>
    [Fact]
    public void SplitUseRuntimePackages_ShouldEmitNoFixedRuntimeFile()
    {
        var options = MultiDialectOptions();
        options = CloneWithRuntimePackages(options);

        var files = GenerateSplit(options);

        files.Keys.Should().NotContain(fileName => IsFixedRuntimeFile(fileName));
        files.Keys.Should().Contain("Repositories.g.cs");
        files.Keys.Should().Contain("Repositories.SqlServer.g.cs");
    }

    /// <summary>
    /// スキーマ依存物のファイル（<c>Repositories*.g.cs</c> ほか）が、パッケージ参照モードの on/off で
    /// 本体（namespace 宣言以降）が 1 バイトも変わらないことを検証する。
    /// </summary>
    /// <remarks>差が出てよいのはヘッダの using と案内コメントだけ（固定 infra の所在が変わるため）。</remarks>
    [Fact]
    public void SchemaDependentFiles_ShouldBeIdenticalAcrossRuntimePackageModes()
    {
        var inline = GenerateSplit(MultiDialectOptions());
        var packages = GenerateSplit(CloneWithRuntimePackages(MultiDialectOptions()));

        foreach (var (fileName, content) in packages)
        {
            inline.Should().ContainKey(fileName);
            Body(content)
                .Should()
                .Be(
                    Body(inline[fileName]),
                    $"{fileName} はスキーマ依存物のみのファイルでランタイム配布方式に依らず不変でなければならない"
                );
        }
    }

    // ---- ヘルパー ----

    /// <summary>固定 infra ファイル（Runtime 系）かどうか</summary>
    private static bool IsFixedRuntimeFile(string fileName) =>
        fileName.StartsWith("Runtime.", StringComparison.Ordinal);

    /// <summary>ヘッダ（auto-generated・using・案内コメント）を落とし、namespace 宣言以降の本体を返す</summary>
    private static string Body(string content)
    {
        var index = content.IndexOf("namespace ", StringComparison.Ordinal);
        return index < 0 ? content : content[index..];
    }

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
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>トップレベル型宣言（列 0・public / internal）を拾う正規表現</summary>
    private static readonly Regex TypeDeclarationRegex = new(
        @"^(?:public|internal)(?:\s+(?:sealed|abstract|static|partial|readonly|ref))*\s+(?:class|interface|struct|enum|record)\s+(?<name>\w+)",
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

    /// <summary>EF Core 単独（マルチ方言と排他）の分割オプション</summary>
    private static CodeGenerationOptions EfCoreOnlyOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            SplitFilesByCategory = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = false,
            GenerateEfCore = true,
            GenerateRemoteServices = true,
            GenerateValueObjects = true,
            IncludeDataAnnotations = true,
            IncludeJsonIgnoreOnParentNavigation = true,
            ExcludeUnboundedBinaryColumns = true,
        };

    /// <summary>インメモリ Repository を含む分割オプション（単一方言＋インメモリ）</summary>
    private static CodeGenerationOptions InMemoryOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            SplitFilesByCategory = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateInMemoryRepositories = true,
            GenerateRemoteServices = true,
            GenerateValueObjects = true,
            IncludeDataAnnotations = true,
            IncludeJsonIgnoreOnParentNavigation = true,
            ExcludeUnboundedBinaryColumns = true,
        };

    /// <summary>同じ構成のままパッケージ参照モードへ切り替えたオプションを返す</summary>
    /// <remarks>
    /// 全プロパティを手書きで複製するとオプション追加時に写し漏れてその構成が黙って未検証になるため、
    /// <c>with</c> 式で 1 項目だけ差し替える（<see cref="RuntimePackageModeCompilationTests"/> と同じ流儀）。
    /// </remarks>
    private static CodeGenerationOptions CloneWithRuntimePackages(CodeGenerationOptions options) =>
        options with
        {
            UseRuntimePackages = true,
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
                        // 文字列列は Unicode で統一（Ansi/Unicode 差で canonical トークンが割れるため。lessons.md 参照）
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
