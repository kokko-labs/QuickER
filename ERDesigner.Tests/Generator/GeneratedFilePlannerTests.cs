using ERDesigner.Generator;
using FluentAssertions;

namespace ERDesigner.Tests.Generator;

/// <summary><see cref="GeneratedFilePlanner"/> の名前空間解決とファイル構成を検証するテストクラス</summary>
public class GeneratedFilePlannerTests
{
    /// <summary>名前空間未指定のバケットは {root}.{接尾辞} へフォールバックすることを検証する</summary>
    [Fact]
    public void ResolveNamespace_WhenUnset_FallsBackToRootDotSuffix()
    {
        var options = new CodeGenerationOptions { NamespaceName = "Acme.App" };

        GeneratedFilePlanner
            .ResolveNamespace(options, GenerationBucket.Entity)
            .Should()
            .Be("Acme.App.Entities");
        GeneratedFilePlanner
            .ResolveNamespace(options, GenerationBucket.Runtime)
            .Should()
            .Be("Acme.App.Runtime");
    }

    /// <summary>名前空間を明示指定した場合はそれを優先することを検証する</summary>
    [Fact]
    public void ResolveNamespace_WhenExplicit_UsesExplicitValue()
    {
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Acme.App",
            EntityNamespace = "Acme.App.Domain.Models",
        };

        GeneratedFilePlanner
            .ResolveNamespace(options, GenerationBucket.Entity)
            .Should()
            .Be("Acme.App.Domain.Models");
    }

    /// <summary>空のベース名前空間は既定 "Generated" へフォールバックすることを検証する</summary>
    [Fact]
    public void ResolveRootNamespace_WhenEmpty_FallsBackToGenerated()
    {
        GeneratedFilePlanner
            .ResolveRootNamespace(new CodeGenerationOptions { NamespaceName = "  " })
            .Should()
            .Be("Generated");
    }

    /// <summary>非分割時は全バケットを 1 ファイルへまとめ、クロス using を持たないことを検証する</summary>
    [Fact]
    public void Plan_NonSplit_ProducesSingleFile()
    {
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Acme.App",
            OutputFileName = "All.g.cs",
        };

        var plan = GeneratedFilePlanner.Plan(options);

        plan.Should().ContainSingle();
        plan[0].FileName.Should().Be("All.g.cs");
        plan[0].NamespaceName.Should().Be("Acme.App");
        plan[0].CrossNamespaceUsings.Should().BeEmpty();
        plan[0].Buckets.Should().Contain(GenerationBucket.Runtime);
    }

    /// <summary>分割時は有効バケットごとにファイルを作り、自分以外の名前空間をクロス using に持つことを検証する</summary>
    [Fact]
    public void Plan_Split_ProducesOneFilePerBucketWithCrossUsings()
    {
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Acme.App",
            SplitFilesByCategory = true,
            GenerateValueObjects = true,
        };

        var plan = GeneratedFilePlanner.Plan(options);

        // 並び順は UI のカテゴリ別 namespace 欄と一致させる（Runtime は末尾の共有基盤）
        plan.Select(spec => spec.FileName)
            .Should()
            .Equal(
                "Entities.g.cs",
                "EditModels.g.cs",
                "Mappers.g.cs",
                "Repositories.g.cs",
                "ValueObjects.g.cs",
                "Runtime.g.cs"
            );

        var entity = plan.Single(spec => spec.FileName == "Entities.g.cs");
        entity.NamespaceName.Should().Be("Acme.App.Entities");
        entity.CrossNamespaceUsings.Should().Contain("Acme.App.Runtime");
        entity.CrossNamespaceUsings.Should().NotContain("Acme.App.Entities");
    }

    /// <summary>分割時に複数カテゴリが同一名前空間でも、ファイルは分かれ、自分自身は using しないことを検証する</summary>
    [Fact]
    public void Plan_Split_SameNamespace_KeepsSeparateFilesWithoutSelfUsing()
    {
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Acme.App",
            SplitFilesByCategory = true,
            GenerateMappers = false,
            GenerateRepositories = false,
            EntityNamespace = "Shared.Models",
            EditModelNamespace = "Shared.Models",
        };

        var plan = GeneratedFilePlanner.Plan(options);

        var entity = plan.Single(spec => spec.FileName == "Entities.g.cs");
        var editModel = plan.Single(spec => spec.FileName == "EditModels.g.cs");
        entity.NamespaceName.Should().Be("Shared.Models");
        editModel.NamespaceName.Should().Be("Shared.Models");
        entity.CrossNamespaceUsings.Should().NotContain("Shared.Models");
    }

    /// <summary>クラスを 1 つでも生成するなら Runtime バケットが常に有効になることを検証する</summary>
    [Fact]
    public void ActiveBuckets_AlwaysIncludesRuntime()
    {
        var options = new CodeGenerationOptions
        {
            GenerateEntityClasses = true,
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = false,
        };

        GeneratedFilePlanner.ActiveBuckets(options).Should().Contain(GenerationBucket.Runtime);
    }
}
