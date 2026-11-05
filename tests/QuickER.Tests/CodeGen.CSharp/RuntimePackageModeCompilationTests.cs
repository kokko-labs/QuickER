using AwesomeAssertions;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// パッケージ参照モード（<see cref="CodeGenerationOptions.UseRuntimePackages"/>）の生成物と、案内ビルダー
/// （<see cref="RuntimePackageReferenceGuidance"/>）が指示するパッケージ集合を検証する。
/// </summary>
/// <remarks>
/// <para>
/// 「案内が指示するパッケージ集合だけを参照して Roslyn でコンパイルできる」こと（案内の十分性と依存最小性）と、
/// サーバー実装ファイルが出ることは、<c>CodegenCaseTests</c> の行 <c>compile-package-*</c>（同じ 28 通りの構成。
/// パッケージ用ソースのアセンブリ化と参照集合の組み立ては <c>CodegenConfig/Judges/CompileJudge.cs</c>）が持つ。
/// </para>
/// <para>
/// 同じ構成の生成物に固定 infra の定義（<c>*Core</c> の型）が出ないことは、同じ図・オプションの行 <c>code-package-mode-no-fixed-infra-*</c> が持つ。ここに残るのは、案内ビルダーの単体の表明。
/// </para>
/// </remarks>
public class RuntimePackageModeCompilationTests
{
    /// <summary>Entity のみ（Repository/EF Core なし）は Core だけで成立し、参照集合に ADO/EF Core が含まれない</summary>
    [Fact]
    public void EntityOnly_GuidanceIsCoreOnly_NoAdoOrEf()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEditModels = true,
                GenerateMappers = true,
                GenerateRepositories = false,
            }
        );

        var packages = RuntimePackageReferenceGuidance.Compute(options);

        packages.Should().Equal(RuntimePackages.Core);
        packages.Should().NotContain(RuntimePackages.SqlServer);
        packages.Should().NotContain(RuntimePackages.Sqlite);
        packages.Should().NotContain(RuntimePackages.EntityFrameworkCore);
    }

    /// <summary>SQLite 単独の案内は Core＋Sqlite のみ（SqlServer / EF Core を含まない）になる</summary>
    [Fact]
    public void SqliteOnly_Guidance_IsCoreAndSqlite_NoSqlServerOrEf()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlite"],
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.Sqlite);
    }

    /// <summary>マルチターゲット（sqlserver+sqlite）の案内は Core＋両方言（EF Core なし）になる</summary>
    [Fact]
    public void MultiTarget_Guidance_IsCoreAndBothDialects()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver", "sqlite"],
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.SqlServer, RuntimePackages.Sqlite);
    }

    /// <summary>EF Core 単独×パッケージの案内は Core＋EF Core のみ（ADO を含まない）になる</summary>
    [Fact]
    public void EfCoreOnly_Guidance_IsCoreAndEfCore_NoAdo()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                GenerateEfCoreRepositories = true,
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.EntityFrameworkCore);
    }

    /// <summary>QuickER sqlserver＋EF Core 併存×パッケージの案内は Core＋SqlServer＋EF Core になる</summary>
    [Fact]
    public void RepositoryPlusEfCore_Guidance_IsCoreSqlServerAndEfCore()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                GenerateEfCoreRepositories = true,
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(
                RuntimePackages.Core,
                RuntimePackages.SqlServer,
                RuntimePackages.EntityFrameworkCore
            );
    }

    /// <summary>インメモリ×パッケージの案内は Core＋InMemory のみ（ADO / EF Core を含まない）になる</summary>
    [Fact]
    public void InMemoryOnly_Guidance_IsCoreAndInMemory_NoAdoOrEf()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                GenerateInMemoryRepositories = true,
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.InMemory);
    }

    /// <summary>リモートサービス生成×パッケージの案内は Core＋方言＋AspNetCore になる（サーバー固定部の所在）</summary>
    [Fact]
    public void RemoteServices_Guidance_IncludesAspNetCore()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                GenerateRemoteServices = true,
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.SqlServer, RuntimePackages.AspNetCore);
    }

    /// <summary>リモート契約のみ（サーバー実装なし）の案内には AspNetCore が入らない</summary>
    /// <remarks>
    /// <c>GenerateRemoteContracts</c> はインターフェイスを足すだけで、サーバー実装ファイル
    /// （<c>RemoteServer.g.cs</c>）を出さない＝ASP.NET Core は要らない。
    /// </remarks>
    [Fact]
    public void RemoteContractsOnly_Guidance_DoesNotIncludeAspNetCore()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                GenerateRemoteContracts = true,
            }
        );

        RuntimePackageReferenceGuidance
            .Compute(options)
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.SqlServer);
    }

    /// <summary>PackageReference 行の案内が指定バージョンで組み立てられる</summary>
    [Fact]
    public void BuildPackageReferenceLines_ProducesVersionedLines()
    {
        var options = WithPackageMode(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        var lines = RuntimePackageReferenceGuidance.BuildPackageReferenceLines(options, "9.9.9");

        lines
            .Should()
            .Contain("<PackageReference Include=\"QuickER.Runtime\" Version=\"9.9.9\" />");
        lines
            .Should()
            .Contain(
                "<PackageReference Include=\"QuickER.Runtime.SqlServer\" Version=\"9.9.9\" />"
            );
    }

    /// <summary>ケース定義のオプションをパッケージ参照モードへ切り替える（他の設定はそのまま引き継ぐ）</summary>
    /// <remarks>
    /// 全プロパティを手書きで複製すると、オプションが増えたときに写し漏れてその構成が黙って未検証になる
    /// （実際に GenerateRemoteServices など 5 つが欠落していた）。<c>with</c> 式で 1 項目だけ差し替える。
    /// </remarks>
    private static CodeGenerationOptions WithPackageMode(CodeGenerationOptions options) =>
        options with
        {
            UseRuntimePackages = true,
        };
}
