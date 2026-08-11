using AwesomeAssertions;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary><see cref="GeneratedFilePlanner"/> の名前空間解決とファイル構成を検証するテストクラス</summary>
public class GeneratedFilePlannerTests
{
    /// <summary>名前空間未指定のバケットは {root}.{接尾辞} へフォールバックすることを検証する</summary>
    [Fact]
    public void ResolveNamespace_WhenUnset_FallsBackToRootDotSuffix()
    {
        var options = new CodeGenerationOptions { RootNamespace = "Acme.App" };

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
            RootNamespace = "Acme.App",
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
            .ResolveRootNamespace(new CodeGenerationOptions { RootNamespace = "  " })
            .Should()
            .Be("Generated");
    }

    /// <summary>非分割時は全バケットを 1 ファイルへまとめ、クロス using を持たないことを検証する</summary>
    [Fact]
    public void Plan_NonSplit_ProducesSingleFile()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Acme.App",
            OutputFileName = "All.g.cs",
        };

        var plan = GeneratedFilePlanner.Plan(options);

        plan.Should().ContainSingle();
        plan[0].FileName.Should().Be("All.g.cs");
        plan[0].NamespaceName.Should().Be("Acme.App");
        plan[0].CrossNamespaceUsings.Should().BeEmpty();
        plan[0].Buckets.Should().Contain(GenerationBucket.Runtime);
    }

    /// <summary>
    /// 分割時は有効バケットごとにファイルを作り、自分以外の名前空間をクロス using に持つことを検証する。
    /// QuickER 版 Repository は単一方言でも「契約（Repositories.g.cs）＋方言別実装（Repositories.SqlServer.g.cs）」へ分け、
    /// スキーマ非依存の固定 infra は配布パッケージと対称な Runtime 系ファイル（Runtime.g.cs / Runtime.SqlServer.g.cs）へ分ける。
    /// </summary>
    [Fact]
    public void Plan_Split_ProducesOneFilePerBucketWithCrossUsings()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Acme.App",
            SplitFilesByCategory = true,
            GenerateValueObjects = true,
            GenerateRepositories = true,
        };

        var plan = GeneratedFilePlanner.Plan(options);

        // 並び順は UI のカテゴリ別 namespace 欄と一致させる（DB アクセス系は値オブジェクトの後・Runtime 系は末尾の固定 infra）。
        // 単一方言でも契約 1 回＋方言別実装ファイル（既定 sqlserver）へ分割する。
        plan.Select(spec => spec.FileName)
            .Should()
            .Equal(
                "Entities.g.cs",
                "EditModels.g.cs",
                "Mappers.g.cs",
                "ValueObjects.g.cs",
                "Repositories.g.cs",
                "Repositories.SqlServer.g.cs",
                "Runtime.g.cs",
                "Runtime.SqlServer.g.cs"
            );

        var entity = plan.Single(spec => spec.FileName == "Entities.g.cs");
        entity.NamespaceName.Should().Be("Acme.App.Entities");
        entity.CrossNamespaceUsings.Should().Contain("Acme.App.Runtime");
        entity.CrossNamespaceUsings.Should().NotContain("Acme.App.Entities");

        // 契約ファイル: {Repository} namespace・契約のみ（ContractOnly）
        var contract = plan.Single(spec => spec.FileName == "Repositories.g.cs");
        contract.NamespaceName.Should().Be("Acme.App.Repositories");
        contract.ContractOnly.Should().BeTrue();
        contract.MultiDialect.Should().BeTrue();

        // 方言別実装ファイル: {Repository}.SqlServer namespace・実装（!ContractOnly）・契約 namespace と
        // 方言エンジンの固定 infra namespace（{Runtime}.SqlServer）を using する
        var impl = plan.Single(spec => spec.FileName == "Repositories.SqlServer.g.cs");
        impl.NamespaceName.Should().Be("Acme.App.Repositories.SqlServer");
        impl.ContractOnly.Should().BeFalse();
        impl.CrossNamespaceUsings.Should()
            .Contain("Acme.App.Repositories")
            .And.Contain("Acme.App.Runtime.SqlServer");

        // Repositories 系はスキーマ依存物のみ・Runtime 系は固定 infra のみ（相互に排他）
        contract.EmitSharedInfra.Should().BeFalse();
        contract.EmitSchemaDependent.Should().BeTrue();
        impl.EmitSharedInfra.Should().BeFalse();

        // 固定 infra ファイル: 共有基盤＋方言中立契約（Runtime.g.cs）と方言エンジン（Runtime.SqlServer.g.cs）
        var runtime = plan.Single(spec => spec.FileName == "Runtime.g.cs");
        runtime.NamespaceName.Should().Be("Acme.App.Runtime");
        runtime.Buckets.Should().Equal(GenerationBucket.Runtime, GenerationBucket.Repository);
        runtime.ContractOnly.Should().BeTrue();
        runtime.EmitSharedInfra.Should().BeTrue();
        runtime.EmitSchemaDependent.Should().BeFalse();

        var runtimeEngine = plan.Single(spec => spec.FileName == "Runtime.SqlServer.g.cs");
        runtimeEngine.NamespaceName.Should().Be("Acme.App.Runtime.SqlServer");
        runtimeEngine.ContractOnly.Should().BeFalse();
        runtimeEngine.CrossNamespaceUsings.Should().Equal("Acme.App.Runtime");
        runtimeEngine.EmitSharedInfra.Should().BeTrue();
        runtimeEngine.EmitSchemaDependent.Should().BeFalse();
    }

    /// <summary>
    /// 分割時にインメモリ実装が独立ファイル（Repositories.InMemory.g.cs・{Repository}.InMemory 名前空間）へ分離され、
    /// 契約（Repositories.g.cs）とは別スペックになることを検証する（EF Core 実装と同じ流儀）。
    /// </summary>
    [Fact]
    public void Plan_Split_InMemory_ProducesSeparateFile()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Acme.App",
            SplitFilesByCategory = true,
            GenerateRepositories = true,
            GenerateInMemoryRepositories = true,
        };

        var plan = GeneratedFilePlanner.Plan(options);

        plan.Select(spec => spec.FileName)
            .Should()
            .Equal(
                "Entities.g.cs",
                "EditModels.g.cs",
                "Mappers.g.cs",
                "Repositories.g.cs",
                "Repositories.SqlServer.g.cs",
                "Repositories.InMemory.g.cs",
                "Runtime.g.cs",
                "Runtime.SqlServer.g.cs",
                "Runtime.InMemory.g.cs"
            );

        var inMemory = plan.Single(spec => spec.FileName == "Repositories.InMemory.g.cs");
        inMemory.NamespaceName.Should().Be("Acme.App.Repositories.InMemory");
        inMemory.Buckets.Should().Equal(GenerationBucket.InMemory);
        // 契約 namespace（I{Entity}Repository・SqlQuery 等）とインメモリ固定 infra namespace を using する
        inMemory
            .CrossNamespaceUsings.Should()
            .Contain("Acme.App.Repositories")
            .And.Contain("Acme.App.Runtime.InMemory");

        // インメモリの固定 infra（InMemoryDataStore・InMemoryRepository 基底）は Runtime.InMemory.g.cs へ分かれる
        var inMemoryRuntime = plan.Single(spec => spec.FileName == "Runtime.InMemory.g.cs");
        inMemoryRuntime.NamespaceName.Should().Be("Acme.App.Runtime.InMemory");
        inMemoryRuntime.Buckets.Should().Equal(GenerationBucket.InMemory);
        inMemoryRuntime.CrossNamespaceUsings.Should().Equal("Acme.App.Runtime");
        inMemoryRuntime.EmitSchemaDependent.Should().BeFalse();

        // 契約ファイルは InMemory バケットを含まない（同居しない）
        var contract = plan.Single(spec => spec.FileName == "Repositories.g.cs");
        contract.Buckets.Should().NotContain(GenerationBucket.InMemory);
    }

    /// <summary>非分割時はインメモリ実装も 1 ファイルへ同居し、独立ファイルを作らないことを検証する（従来どおり）</summary>
    [Fact]
    public void Plan_NonSplit_InMemory_StaysInSingleFile()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Acme.App",
            OutputFileName = "All.g.cs",
            GenerateRepositories = true,
            GenerateInMemoryRepositories = true,
        };

        var plan = GeneratedFilePlanner.Plan(options);

        plan.Should().ContainSingle();
        plan[0].Buckets.Should().Contain(GenerationBucket.InMemory);
        plan[0].Buckets.Should().Contain(GenerationBucket.Repository);
    }

    /// <summary>
    /// 分割＋リモートサービス生成時、RemoteServer.g.cs が Repositories.g.cs の直後（EfCore / Runtime より前）に
    /// 並ぶことを検証する（リモート面の契約の隣に置く。プレビュー表示・出力順の両方がこの計画順に従う）。
    /// </summary>
    [Fact]
    public void Plan_Split_RemoteServer_ComesRightAfterRepositories()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Acme.App",
            SplitFilesByCategory = true,
            GenerateRepositories = true,
            GenerateRemoteServices = true,
        };

        var plan = GeneratedFilePlanner.Plan(options);

        // RemoteServer は Repository バケットを含む最後のスキーマ依存スペック（方言別実装）の直後に並ぶ
        // （Runtime 系の固定 infra ファイルはさらに後ろ。サーバー固定部の Runtime.AspNetCore.g.cs もその一員）
        plan.Select(spec => spec.FileName)
            .Should()
            .Equal(
                "Entities.g.cs",
                "EditModels.g.cs",
                "Mappers.g.cs",
                "Repositories.g.cs",
                "Repositories.SqlServer.g.cs",
                "RemoteServer.g.cs",
                "Runtime.g.cs",
                "Runtime.SqlServer.g.cs",
                "Runtime.AspNetCore.g.cs"
            );
    }

    /// <summary>
    /// 分割＋リモートサービス生成時、サーバー実装が「固定部（Runtime.AspNetCore.g.cs）＋スキーマ依存
    /// （RemoteServer.g.cs）」の 2 スペックへ分かれ、パッケージ参照モードでは固定部を計画しないことを検証する。
    /// </summary>
    [Fact]
    public void Plan_Split_RemoteServer_SeparatesFixedEngineSpec()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Acme.App",
            SplitFilesByCategory = true,
            GenerateRepositories = true,
            GenerateRemoteServices = true,
        };

        var plan = GeneratedFilePlanner.Plan(options);

        // 固定部（他の Runtime 系と同型＝コア相当だけを using・スキーマ依存物なし）
        var engine = plan.Single(spec => spec.FileName == "Runtime.AspNetCore.g.cs");
        engine.NamespaceName.Should().Be("Acme.App.Runtime.AspNetCore");
        engine.Buckets.Should().Equal(GenerationBucket.RemoteServer);
        engine.CrossNamespaceUsings.Should().Equal("Acme.App.Runtime");
        engine.EmitSchemaDependent.Should().BeFalse();
        engine.EmitSharedInfra.Should().BeTrue();

        // スキーマ依存側（per-entity）は固定部を using し、固定 infra を描かない
        var server = plan.Single(spec => spec.FileName == "RemoteServer.g.cs");
        server.NamespaceName.Should().Be("Acme.App.RemoteServer");
        server.EmitSharedInfra.Should().BeFalse();
        server
            .CrossNamespaceUsings.Should()
            .BeEquivalentTo(
                "Acme.App.Entities",
                "Acme.App.Repositories",
                "Acme.App.Runtime",
                "Acme.App.Runtime.AspNetCore"
            );

        // パッケージ参照モードでは固定部ファイルを計画しない（固定名前空間 using は GeneratedFileUsings が付ける）
        var packagePlan = GeneratedFilePlanner.Plan(options with { UseRuntimePackages = true });
        packagePlan.Select(spec => spec.FileName).Should().NotContain("Runtime.AspNetCore.g.cs");
        packagePlan
            .Single(spec => spec.FileName == "RemoteServer.g.cs")
            .CrossNamespaceUsings.Should()
            .NotContain("Acme.App.Runtime.AspNetCore");
    }

    /// <summary>クロス using が依存グラフに基づき、参照しないバケットの名前空間を using しないことを検証する</summary>
    [Fact]
    public void Plan_Split_CrossUsings_FollowDependencyGraph()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Acme.App",
            SplitFilesByCategory = true,
            GenerateValueObjects = true,
            GenerateEfCore = true,
        };

        var plan = GeneratedFilePlanner.Plan(options);

        // Entity は Runtime / ValueObjects のみ依存し、Repositories / Repositories.EntityFrameworkCore / Mappers は using しない
        var entity = plan.Single(spec => spec.FileName == "Entities.g.cs");
        entity
            .CrossNamespaceUsings.Should()
            .BeEquivalentTo("Acme.App.Runtime", "Acme.App.ValueObjects");

        // Mapper は Entity / EditModel / Runtime に依存する（ValueObjects へは依存しない）
        var mapper = plan.Single(spec => spec.FileName == "Mappers.g.cs");
        mapper
            .CrossNamespaceUsings.Should()
            .BeEquivalentTo("Acme.App.Entities", "Acme.App.EditModels", "Acme.App.Runtime");

        // EF Core 実装は方言別実装と同じ流儀で Repositories.EntityFrameworkCore.g.cs・{Repository}.EntityFrameworkCore へ出し、
        // Entity / Repositories（契約）/ Runtime / ValueObjects と EF Core 固定 infra namespace に依存する
        var efCore = plan.Single(spec => spec.FileName == "Repositories.EntityFrameworkCore.g.cs");
        efCore.NamespaceName.Should().Be("Acme.App.Repositories.EntityFrameworkCore");
        efCore
            .CrossNamespaceUsings.Should()
            .BeEquivalentTo(
                "Acme.App.Entities",
                "Acme.App.Repositories",
                "Acme.App.Runtime",
                "Acme.App.Runtime.EntityFrameworkCore",
                "Acme.App.ValueObjects"
            );

        // Runtime（共有基盤＋契約の固定 infra）は他バケットへ依存しない
        var runtime = plan.Single(spec => spec.FileName == "Runtime.g.cs");
        runtime.CrossNamespaceUsings.Should().BeEmpty();

        // EF Core 固定 infra はコア相当（Runtime）だけを using する（パッケージが using QuickER.Runtime; するのと同型）
        var efCoreRuntime = plan.Single(spec =>
            spec.FileName == "Runtime.EntityFrameworkCore.g.cs"
        );
        efCoreRuntime.NamespaceName.Should().Be("Acme.App.Runtime.EntityFrameworkCore");
        efCoreRuntime.CrossNamespaceUsings.Should().Equal("Acme.App.Runtime");
    }

    /// <summary>分割時に複数カテゴリが同一名前空間でも、ファイルは分かれ、自分自身は using しないことを検証する</summary>
    [Fact]
    public void Plan_Split_SameNamespace_KeepsSeparateFilesWithoutSelfUsing()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Acme.App",
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

    /// <summary>
    /// パッケージ参照モードの分割時は固定 infra ファイル（Runtime 系）を 1 本も計画せず、スキーマ依存の
    /// Repositories 系ファイルの構成は据え置かれることを検証する（「Runtime 系が出ない」だけの差）。
    /// </summary>
    [Fact]
    public void Plan_Split_UseRuntimePackages_OmitsFixedRuntimeFiles()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Acme.App",
            SplitFilesByCategory = true,
            GenerateRepositories = true,
            GenerateEfCore = true,
            UseRuntimePackages = true,
        };

        var plan = GeneratedFilePlanner.Plan(options);

        plan.Select(spec => spec.FileName)
            .Should()
            .Equal(
                "Entities.g.cs",
                "EditModels.g.cs",
                "Mappers.g.cs",
                "Repositories.g.cs",
                "Repositories.SqlServer.g.cs",
                "Repositories.EntityFrameworkCore.g.cs"
            );

        plan.Should()
            .NotContain(spec => spec.FileName.StartsWith("Runtime", StringComparison.Ordinal));
        // 固定 infra が無いためクロス using も {root}.Runtime を指さない（パッケージ名前空間は using 解決側が付ける）
        plan.SelectMany(spec => spec.CrossNamespaceUsings)
            .Should()
            .NotContain(ns => ns.StartsWith("Acme.App.Runtime", StringComparison.Ordinal));
    }

    /// <summary>クラスを 1 つでも生成するなら Runtime バケットが常に有効になることを検証する</summary>
    [Fact]
    public void ActiveBuckets_AlwaysIncludesRuntime()
    {
        var options = new CodeGenerationOptions
        {
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = false,
        };

        GeneratedFilePlanner.ActiveBuckets(options).Should().Contain(GenerationBucket.Runtime);
    }
}
