using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// グラフ取得糖衣（<c>IncludeGraphExtensions</c>＝<c>SqlQuery&lt;T&gt;.IncludeGraph()</c>）の生成挙動を、
/// 再生成に依存しない生成テキストと診断で固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 閉包の形（兄弟分岐で同一ナビのノードが 1 本だけ・パス上の再訪辺を展開しない）は、壊れてもビルド・型検査には
/// 一切出ず、実行時に「孫が黙って落ちる」「無限再帰で生成が止まる」という形でしか現れない。ここで名指しして表明する。
/// </para>
/// <para>
/// ドリフト検知（フィクスチャ再生成）は「意図しない変化に気づく」ための仕掛けで、変化が正しいことは保証しない
/// （<c>QUICKER_REGEN_FIXTURES=1</c> で誤った変更も緑になる）ため、ゲート・診断・ツリー形状は本クラスの担当とする。
/// </para>
/// </remarks>
public class IncludeGraphGenerationTests
{
    /// <summary>実経路（SqlServer プロバイダで型解決）で生成する</summary>
    private static CodeGenerationResult Generate(ErDiagram diagram, CodeGenerationOptions options)
    {
        var provider = new SqlServerProvider();
        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            options
        );
    }

    /// <summary>QuickER 版 Repository を生成する標準オプション（＝<c>Query()</c> が出る構成）</summary>
    private static CodeGenerationOptions RepositoryOptions() =>
        new() { RootNamespace = "Test.Ns", GenerateRepositories = true };

    /// <summary>全ファイルの内容を連結して返す</summary>
    private static string AllContent(CodeGenerationResult result) =>
        string.Join("\n", result.Files.Select(file => file.Content));

    /// <summary>生成物から <c>IncludeGraphExtensions</c> クラスの本文だけを切り出す</summary>
    private static string ExtensionsClass(CodeGenerationResult result)
    {
        var content = AllContent(result);
        var start = content.IndexOf(
            "public static class IncludeGraphExtensions",
            StringComparison.Ordinal
        );
        start.Should().BeGreaterThanOrEqualTo(0, "IncludeGraphExtensions が生成されていない");

        // クラス宣言から、行頭 "}" で閉じる最初の位置までを本文とみなす（生成物は 1 クラス 1 ブロック）
        var end = content.IndexOf("\n}", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return content[start..(end + 2)];
    }

    /// <summary>主キー列（int）を作る</summary>
    private static Column Key(string name) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };

    /// <summary>外部キー列（int・NULL 許容）を作る</summary>
    private static Column Fk(string name) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsNullable = true,
        };

    /// <summary>1 対多のリレーション（親 principal → 子 dependent）を作る</summary>
    private static Relationship OneToMany(
        Entity principal,
        Column principalColumn,
        Entity dependent,
        Column dependentColumn
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = RelationshipType.OneToMany,
            SourceEntityId = principal.Id,
            TargetEntityId = dependent.Id,
            ColumnPairs = { new(principalColumn.Id, dependentColumn.Id) },
        };

    /// <summary>root → mid → leaf の 3 階層チェーンの図を作る</summary>
    private static ErDiagram ChainDiagram()
    {
        var rootId = Key("root_id");
        var root = new Entity { TableName = "root", Columns = { rootId } };

        var midId = Key("mid_id");
        var midRootId = Fk("root_id");
        var mid = new Entity { TableName = "mid", Columns = { midId, midRootId } };

        var leafId = Key("leaf_id");
        var leafMidId = Fk("mid_id");
        var leaf = new Entity { TableName = "leaf", Columns = { leafId, leafMidId } };

        return new ErDiagram
        {
            Entities = { root, mid, leaf },
            Relationships =
            {
                OneToMany(root, rootId, mid, midRootId),
                OneToMany(mid, midId, leaf, leafMidId),
            },
        };
    }

    /// <summary>
    /// 3 階層チェーンは「親ノード 1 本＋その子 1 本」のツリーになり、ルート配列には親だけが載る。
    /// </summary>
    [Fact(DisplayName = "3 階層チェーンのツリー形状（親ノード＋子ノード＋ルートは親のみ）")]
    public void Chain_BuildsNestedTree()
    {
        var result = Generate(ChainDiagram(), RepositoryOptions());

        result.HasErrors.Should().BeFalse();
        var extensions = ExtensionsClass(result);

        extensions
            .Should()
            .Contain(
                "var n0 = new IncludeNode(typeof(RootEntity).GetProperty(nameof(RootEntity.Mids))!);"
            )
            .And.Contain(
                "var n1 = new IncludeNode(typeof(MidEntity).GetProperty(nameof(MidEntity.Leafs))!);"
            )
            .And.Contain("n0.Children.Add(n1);")
            .And.Contain("return new IncludeNode[] { n0 };");

        // 中間テーブル自身の閉包は子 1 本だけ（ルートを変えても親方向へは戻らない）
        extensions
            .Should()
            .Contain("private static readonly Lazy<IReadOnlyList<IncludeNode>> _midEntityGraph");
        extensions.Should().NotContain("_leafEntityGraph", "葉エンティティは静的ツリーを持たない");

        // 実際に拡張として呼べる形になっている（受け入れ口は AddIncludeNodes）
        extensions
            .Should()
            .Contain(
                "public static SqlQuery<RootEntity> IncludeGraph(this SqlQuery<RootEntity> query)"
            )
            .And.Contain("query.AddIncludeNodes(_rootEntityGraph.Value);");
    }

    /// <summary>
    /// 兄弟分岐（1 つのナビの下に子テーブル 2 つ）でも、同一ナビの <c>IncludeNode</c> は 1 本しか作らない。
    /// </summary>
    /// <remarks>
    /// fluent の Include/ThenInclude 連鎖では枝ごとに同じナビのノードが積まれ、SQLite / インメモリの置換バインドと
    /// SQL Server の同名 JSON 後勝ちで先行の枝の孫が消える。ツリーを直接組み立てる意義はここにあるので、
    /// 「同一ナビのノードが 1 本」を名指しで表明する。
    /// </remarks>
    [Fact(DisplayName = "兄弟分岐でも同一ナビの IncludeNode は 1 本だけ（子を 2 つぶら下げる）")]
    public void SiblingBranches_ShareOneNodePerNavigation()
    {
        var rootId = Key("root_id");
        var root = new Entity { TableName = "root", Columns = { rootId } };

        var midId = Key("mid_id");
        var midRootId = Fk("root_id");
        var mid = new Entity { TableName = "mid", Columns = { midId, midRootId } };

        var leafId = Key("leaf_id");
        var leafMidId = Fk("mid_id");
        var leaf = new Entity { TableName = "leaf", Columns = { leafId, leafMidId } };

        var twigId = Key("twig_id");
        var twigMidId = Fk("mid_id");
        var twig = new Entity { TableName = "twig", Columns = { twigId, twigMidId } };

        var diagram = new ErDiagram
        {
            Entities = { root, mid, leaf, twig },
            Relationships =
            {
                OneToMany(root, rootId, mid, midRootId),
                OneToMany(mid, midId, leaf, leafMidId),
                OneToMany(mid, midId, twig, twigMidId),
            },
        };

        var extensions = ExtensionsClass(Generate(diagram, RepositoryOptions()));

        // root の閉包で RootEntity.Mids のノードは 1 本だけ（2 本出るのが連鎖版の不具合）
        var rootGraph = extensions[
            extensions.IndexOf("_rootEntityGraph", StringComparison.Ordinal)..
        ];
        rootGraph = rootGraph[..rootGraph.IndexOf("});", StringComparison.Ordinal)];

        CountOf(rootGraph, "nameof(RootEntity.Mids)").Should().Be(1);
        rootGraph
            .Should()
            .Contain("nameof(MidEntity.Leafs)")
            .And.Contain("nameof(MidEntity.Twigs)");

        // 2 つの子はどちらも同じ親ノードへぶら下がる
        rootGraph.Should().Contain("n0.Children.Add(n1);").And.Contain("n0.Children.Add(n2);");
        rootGraph.Should().Contain("return new IncludeNode[] { n0 };");
    }

    /// <summary>自己参照ナビはツリーに入らず、Info 診断で名指しされる</summary>
    [Fact(DisplayName = "自己参照ナビは展開せず Info 診断で列挙する")]
    public void SelfReference_IsSkippedAndReported()
    {
        var nodeId = Key("node_id");
        var parentId = Fk("parent_id");
        var node = new Entity { TableName = "node", Columns = { nodeId, parentId } };

        var diagram = new ErDiagram
        {
            Entities = { node },
            Relationships = { OneToMany(node, nodeId, node, parentId) },
        };

        var result = Generate(diagram, RepositoryOptions());

        result.HasErrors.Should().BeFalse();

        // ツリーは空＝no-op 形（辿ると無限に深くなるため展開しない）
        var extensions = ExtensionsClass(result);
        extensions.Should().NotContain("_nodeEntityGraph");
        extensions
            .Should()
            .Contain(
                "public static SqlQuery<NodeEntity> IncludeGraph(this SqlQuery<NodeEntity> query) => query;"
            );

        // 落としたことは生成物に痕跡が残らないため、Info 診断で名指しする
        result
            .Diagnostics.Should()
            .ContainSingle(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Info
                && diagnostic.Message.Contains("NodeEntity.Nodes")
            );
    }

    /// <summary>スキップが 1 件も無い図では IncludeGraph の Info 診断を出さない</summary>
    [Fact(DisplayName = "スキップが無い図では IncludeGraph の Info 診断を出さない")]
    public void NoSkippedEdges_EmitsNoInfo()
    {
        var result = Generate(ChainDiagram(), RepositoryOptions());

        result
            .Diagnostics.Should()
            .NotContain(diagnostic => diagnostic.Message.Contains("IncludeGraph"));
    }

    /// <summary>相互参照（A → B → A）では、パス上のテーブルへ戻る辺だけが落ちる</summary>
    [Fact(DisplayName = "相互参照は再訪する辺だけを落とす（往路は残る）")]
    public void MutualReference_SkipsOnlyTheRevisitingEdge()
    {
        var alphaId = Key("alpha_id");
        var alphaBetaId = Fk("beta_id");
        var alpha = new Entity { TableName = "alpha", Columns = { alphaId, alphaBetaId } };

        var betaId = Key("beta_id");
        var betaAlphaId = Fk("alpha_id");
        var beta = new Entity { TableName = "beta", Columns = { betaId, betaAlphaId } };

        var diagram = new ErDiagram
        {
            Entities = { alpha, beta },
            Relationships =
            {
                OneToMany(alpha, alphaId, beta, betaAlphaId),
                OneToMany(beta, betaId, alpha, alphaBetaId),
            },
        };

        var result = Generate(diagram, RepositoryOptions());

        result.HasErrors.Should().BeFalse();
        var extensions = ExtensionsClass(result);

        // 往路（1 段目）は両ルートとも残る
        extensions
            .Should()
            .Contain(
                "var n0 = new IncludeNode(typeof(AlphaEntity).GetProperty(nameof(AlphaEntity.Betas))!);"
            )
            .And.Contain(
                "var n0 = new IncludeNode(typeof(BetaEntity).GetProperty(nameof(BetaEntity.Alphas))!);"
            );

        // 2 段目（ルートのテーブルへ戻る辺）はどちらのツリーにも現れない＝各ツリーはノード 1 本
        CountOf(extensions, "var n1 = ").Should().Be(0);
        CountOf(extensions, "Children.Add(").Should().Be(0);

        // 落とした辺は両方向ぶん Info 診断へ載る
        var info = result
            .Diagnostics.Should()
            .ContainSingle(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Info)
            .Which;
        info.Message.Should().Contain("BetaEntity.Alphas").And.Contain("AlphaEntity.Betas");
    }

    /// <summary>Repository 契約が出ない構成では拡張クラスごと出力されない</summary>
    [Fact(DisplayName = "契約が出ない構成（Entity / EditModel のみ）では IncludeGraph が出ない")]
    public void WithoutRepositoryContract_EmitsNothing()
    {
        var result = Generate(
            ChainDiagram(),
            new CodeGenerationOptions { RootNamespace = "Test.Ns" }
        );

        result.HasErrors.Should().BeFalse();
        AllContent(result).Should().NotContain("IncludeGraphExtensions");
    }

    /// <summary>EF Core 単独・インメモリ単独でも Query() は出るため IncludeGraph も出る</summary>
    [Theory(DisplayName = "EF Core 単独・インメモリ単独でも IncludeGraph は生成される")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ContractOnlyBackends_EmitIncludeGraph(bool efCore, bool inMemory)
    {
        var result = Generate(
            ChainDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateEfCore = efCore,
                GenerateInMemoryRepositories = inMemory,
            }
        );

        result.HasErrors.Should().BeFalse();
        ExtensionsClass(result)
            .Should()
            .Contain(
                "public static SqlQuery<RootEntity> IncludeGraph(this SqlQuery<RootEntity> query)"
            );
    }

    /// <summary>カスケード子を持たない葉エンティティは、フィールドなしの no-op 形で生成される</summary>
    [Fact(DisplayName = "葉エンティティは query をそのまま返す no-op 形になる")]
    public void LeafEntity_IsGeneratedAsNoOp()
    {
        var extensions = ExtensionsClass(Generate(ChainDiagram(), RepositoryOptions()));

        extensions
            .Should()
            .Contain(
                "public static SqlQuery<LeafEntity> IncludeGraph(this SqlQuery<LeafEntity> query) => query;"
            );
    }

    /// <summary>部分文字列の出現回数を数える</summary>
    private static int CountOf(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
