using System.Reflection;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成コードの実行時テストの「機能 × 実装先」の行列を、テストのクラスの宣言から組み、照合し、生成物として固定するテストクラス
/// </summary>
/// <remarks>
/// <para>
/// 「機能 × 実装先」の空欄はバグの保管庫である（名前付きクエリ × インメモリの空欄を 1 つ埋めた初回実行で
/// NULL 許容列の <c>NullReferenceException</c> が出た）。ここは<b>宣言しない限り赤くなる</b>網で、次を同時に固定する。
/// </para>
/// <list type="number">
///   <item>どの機能 × どの実装先も、担当（クラスの <see cref="RuntimeCellAttribute"/> かエンジンの登録）か、行の表の宣言（空き・割り切り・非該当）のちょうど一方。</item>
///   <item>走査の規約（<see cref="RuntimeMatrix"/>）に合うクラスは、どれもセルを名乗る。</item>
///   <item>実装先の正本（<see cref="RuntimeTarget"/>）は製品の方言の正本と一対一に対応する。</item>
///   <item>行列の生成物（<see cref="RuntimeMatrix.DocumentPath"/>）は宣言から作った内容と同じ。</item>
///   <item>新しいクラスの名前は実装先の正本に従う（既存のクラスは既知の一覧 <see cref="RuntimeTestNamingKnown"/>）。</item>
/// </list>
/// </remarks>
public sealed class RuntimeTestMatrixTests
{
    [Fact(DisplayName = "実装先の正本は、製品の QuickER 版の方言・方言の全体と一対一に対応する")]
    public void Targets_MatchProductDialects() =>
        RuntimeTargets
            .ProductProblems(CodeGenerationOptions.SupportedRepositoryDialects, DialectNames.All)
            .Should()
            .BeEmpty();

    [Fact(
        DisplayName = "実行時テストクラスがすべて機能 × 実装先のセルを名乗り、担当の形（リモートのサーバー）が整っている"
    )]
    public void EveryRuntimeTestClass_ShouldAppearInMatrix()
    {
        var (cells, engineProblems) = RuntimeMatrix.Collect();
        var classes = RuntimeMatrix.RuntimeTestClasses().Select(type => type.Name).ToList();
        var problems = engineProblems
            .Concat(RuntimeMatrix.ClassProblems(classes, cells, RuntimeMatrixEngines.ClassNames))
            .Concat(RuntimeMatrix.CoverageProblems(cells))
            .ToList();

        problems.Should().BeEmpty(string.Join(Environment.NewLine, problems));

        // 走査の規約が壊れて 0 件になると、上の表明は空振りで緑になる
        classes
            .Should()
            .HaveCountGreaterThan(
                40,
                "走査が実行時テストクラスを見つけられていない（走査規約が名前空間・属性の変更で空振りしている）"
            );
    }

    [Fact(
        DisplayName = "機能 × 実装先の各セルが担当か理由つきの宣言（空き・割り切り・非該当）のちょうど一方で埋まっている"
    )]
    public void EveryMatrixCell_ShouldBeCoveredOrDeclaredAsGap()
    {
        var (cells, _) = RuntimeMatrix.Collect();
        var problems = RuntimeMatrix
            .CellProblems(RuntimeMatrixRows.All, cells)
            .Concat(RuntimeMatrix.BasisProblems(RuntimeMatrixRows.All, EngineRepository.Root()))
            .ToList();

        problems
            .Should()
            .BeEmpty(
                "機能 × 実装先の行列に未解決のセルがある:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems)
            );
    }

    /// <summary>
    /// 行列が「空欄の一覧」として意味を保っていること（成り立つセルが空き・割り切りの宣言で埋まる退化を防ぐ）を検証する。
    /// </summary>
    /// <remarks>
    /// 分母は成り立つセル（担当・空き・割り切り）で、非該当は数えない。EF Core を方言ごとに分けると非該当の列が増えるため、
    /// 全セルを分母にすると網の質と関係なく過半を割る。
    /// </remarks>
    [Fact(DisplayName = "成り立つセルのうち担当が過半を占める")]
    public void Matrix_ShouldBeMostlyCovered()
    {
        var declared = RuntimeMatrixRows.All.SelectMany(row => row.Declared.Values).ToList();
        var notApplicable = declared.Count(cell => cell.Kind == RuntimeCellKind.NotApplicable);
        var total = RuntimeMatrixRows.All.Count * Enum.GetValues<RuntimeTarget>().Length;
        var applicable = total - notApplicable;
        var covered = total - declared.Count;

        covered
            .Should()
            .BeGreaterThan(
                applicable / 2,
                $"成り立つセル {applicable} のうち担当が {covered}。半数以下なら、行列は網ではなく空きの宣言の一覧に退化している"
            );
    }

    [Fact(DisplayName = "[台帳] 機能 × 実装先の行列の生成物は、宣言から作り直した内容と同じ")]
    public void RuntimeMatrixDrift_MatchesRecord()
    {
        var (cells, _) = RuntimeMatrix.Collect();

        GeneratedTextFile.Verify(
            Path.Combine(EngineRepository.Root(), RuntimeMatrix.DocumentPath),
            RuntimeMatrix.Render(RuntimeMatrixRows.All, cells),
            "機能 × 実装先の行列",
            RuntimeMatrix.RegenerateSteps
        );
    }

    [Fact(DisplayName = "新しい実行時テストクラスの名前は、名乗る機能と実装先の正本の名前に従う")]
    public void NewRuntimeTestClasses_FollowNamingRule()
    {
        var (cells, _) = RuntimeMatrix.Collect();
        var engines = RuntimeMatrixEngines.ClassNames.ToHashSet(StringComparer.Ordinal);
        var violations = RuntimeMatrix
            .RuntimeTestClasses()
            .Select(type => type.Name)
            .Where(name =>
                !RuntimeTestNaming.Follows(
                    name,
                    cells.Where(cell => cell.ClassName == name).ToList(),
                    engines.Contains(name)
                )
            )
            .ToDictionary(name => name, _ => 1, StringComparer.Ordinal);

        GuardKnownList.AssertMatches(
            violations,
            RuntimeTestNamingKnown.All.Select(entry => (entry.ClassName, 1)),
            (name, _) => name,
            "名前が規則に合わない実行時テストクラスがある（実装先が 1 つなら {機能}{実装先}RuntimeTests、リモートは {機能}Remote{サーバー}RuntimeTests、"
                + "実装先が 2 つ以上なら {機能}…RuntimeTests。RuntimeCell 属性の機能・実装先の名前で付ける）",
            "既知の一覧のクラスが無くなったか規則に合うようになった（RuntimeTestNamingKnown から消す）"
        );
        RuntimeTestNamingKnown
            .All.Where(entry => FactorInventoryGuard.IsVagueReason(entry.Reason))
            .Select(entry => entry.ClassName)
            .Should()
            .BeEmpty("既知の一覧の理由は具体的に書く");
    }

    /// <summary>照合の関数が問題の形を 1 つずつ名指しすることを、合成の宣言で確かめる（DB を使わない自己検査）</summary>
    [Fact(
        DisplayName = "行列の照合は、担当でも宣言でもないセル・埋まった宣言・宣言の形・リモートのサーバー・名前の規則の違反を名指しする"
    )]
    public void Checks_NameEveryProblemShape()
    {
        var gap = new RuntimeCellDeclaration(
            RuntimeCellKind.Gap,
            "まだ流していない理由の文",
            "きっかけ待ち"
        );
        var allButSqlite = Enum.GetValues<RuntimeTarget>()
            .Where(target => target != RuntimeTarget.AdoSqlite)
            .ToDictionary(target => target, _ => gap);
        RuntimeFeatureRow Row(
            IReadOnlyDictionary<RuntimeTarget, RuntimeCellDeclaration> declared,
            params RuntimeMissingServer[] missing
        ) => new("Feature", "合成の機能の説明の文", declared, missing);
        RuntimeCoverage Cover(
            RuntimeTarget target,
            RuntimeTarget? server = null,
            bool none = false,
            string feature = "Feature"
        ) => new(feature, target, server, none, "FeatureAdoSqliteRuntimeTests", null);

        // 整った宣言は問題なし
        RuntimeMatrix
            .CellProblems([Row(allButSqlite)], [Cover(RuntimeTarget.AdoSqlite)])
            .Should()
            .BeEmpty();

        // 担当も宣言も無いセル（属性を外す・実装先を足すのと同じ形）
        RuntimeMatrix
            .CellProblems([Row(allButSqlite)], [])
            .Should()
            .ContainSingle(problem => problem.Contains("[Feature × AdoSqlite] は担当も宣言も無い"));

        // 埋まった宣言
        RuntimeMatrix
            .CellProblems(
                [Row(allButSqlite)],
                [Cover(RuntimeTarget.AdoSqlite), Cover(RuntimeTarget.InMemory)]
            )
            .Should()
            .ContainSingle(problem =>
                problem.Contains("[Feature × InMemory]") && problem.Contains("埋まった")
            );

        // 宣言の形（区切りの無い空き・根拠の無い割り切り・曖昧な理由）
        var malformed = new Dictionary<RuntimeTarget, RuntimeCellDeclaration>(allButSqlite)
        {
            [RuntimeTarget.InMemory] = new(RuntimeCellKind.Gap, "まだ流していない理由の文"),
            [RuntimeTarget.Remote] = new(RuntimeCellKind.Accepted, "代表させている理由の文"),
            [RuntimeTarget.EfCoreOracle] = new(RuntimeCellKind.NotApplicable, "TBD"),
        };
        RuntimeMatrix
            .CellProblems([Row(malformed)], [Cover(RuntimeTarget.AdoSqlite)])
            .Should()
            .HaveCount(3);

        // 知らない機能・リモートの足りないサーバーが埋まった
        var remoteCovered = new Dictionary<RuntimeTarget, RuntimeCellDeclaration>(allButSqlite);
        remoteCovered.Remove(RuntimeTarget.Remote);
        RuntimeMatrix
            .CellProblems(
                [
                    Row(
                        remoteCovered,
                        new RuntimeMissingServer(
                            RuntimeTarget.AdoSqlServer,
                            "きっかけ待ち",
                            "流していないサーバーの理由"
                        )
                    ),
                ],
                [
                    Cover(RuntimeTarget.AdoSqlite),
                    Cover(RuntimeTarget.Remote, RuntimeTarget.AdoSqlServer),
                    Cover(RuntimeTarget.AdoSqlite, feature: "Unknown"),
                ]
            )
            .Should()
            .HaveCount(2);

        // リモートのサーバー：名乗らない・両方・リモート以外が名乗る・リモートをサーバーに書く
        RuntimeMatrix
            .CoverageProblems([
                Cover(RuntimeTarget.Remote),
                Cover(RuntimeTarget.Remote, RuntimeTarget.AdoSqlite, none: true),
                Cover(RuntimeTarget.InMemory, RuntimeTarget.AdoSqlite),
                Cover(RuntimeTarget.Remote, RuntimeTarget.Remote),
            ])
            .Should()
            .HaveCount(4);
        RuntimeMatrix
            .CoverageProblems([
                Cover(RuntimeTarget.Remote, none: true),
                Cover(RuntimeTarget.Remote, RuntimeTarget.InMemory),
            ])
            .Should()
            .BeEmpty();

        // 属性の無いクラス・実在しないエンジンのクラス
        RuntimeMatrix
            .ClassProblems(
                ["FeatureAdoSqliteRuntimeTests", "Bare"],
                [Cover(RuntimeTarget.AdoSqlite)],
                ["Ghost"]
            )
            .Should()
            .HaveCount(2);

        // 名前の規則
        RuntimeTestNaming
            .Follows("FeatureAdoSqliteRuntimeTests", [Cover(RuntimeTarget.AdoSqlite)], false)
            .Should()
            .BeTrue();
        RuntimeTestNaming
            .Follows("FeatureSqliteRuntimeTests", [Cover(RuntimeTarget.AdoSqlite)], false)
            .Should()
            .BeFalse();
        RuntimeTestNaming
            .Follows(
                "FeatureRemoteInMemoryRuntimeTests",
                [Cover(RuntimeTarget.Remote, RuntimeTarget.InMemory)],
                false
            )
            .Should()
            .BeTrue();
        RuntimeTestNaming
            .Follows(
                "FeatureRemoteRuntimeTests",
                [Cover(RuntimeTarget.Remote, RuntimeTarget.InMemory)],
                false
            )
            .Should()
            .BeFalse();
        RuntimeTestNaming
            .Follows("FeatureRemoteRuntimeTests", [Cover(RuntimeTarget.Remote, none: true)], false)
            .Should()
            .BeTrue();
        RuntimeTestNaming
            .Follows(
                "FeatureAnythingRuntimeTests",
                [Cover(RuntimeTarget.AdoSqlite), Cover(RuntimeTarget.InMemory)],
                false
            )
            .Should()
            .BeTrue();
        RuntimeTestNaming
            .Follows(
                "OtherRuntimeTests",
                [Cover(RuntimeTarget.AdoSqlite), Cover(RuntimeTarget.InMemory)],
                false
            )
            .Should()
            .BeFalse();
        RuntimeTestNaming
            .Follows("FeatureEngineRuntimeTests", [Cover(RuntimeTarget.AdoSqlite)], true)
            .Should()
            .BeTrue();
    }
}

/// <summary>実行時テストクラスの名前の規則</summary>
public static class RuntimeTestNaming
{
    /// <summary>
    /// 名前が規則に合うか：実装先（とリモートのサーバー）が 1 つなら <c>{機能}{実装先}RuntimeTests</c>
    /// （リモートは <c>{機能}Remote{サーバー}RuntimeTests</c>・リポジトリなしは <c>{機能}RemoteRuntimeTests</c>）、
    /// 2 つ以上かエンジンなら <c>{機能}…RuntimeTests</c>（機能はクラスが名乗るもののどれか）
    /// </summary>
    /// <param name="className">クラスの名前</param>
    /// <param name="cells">そのクラスの担当</param>
    /// <param name="isEngine">実装先を比べるエンジンか</param>
    public static bool Follows(
        string className,
        IReadOnlyList<RuntimeCoverage> cells,
        bool isEngine
    )
    {
        const string Suffix = "RuntimeTests";
        var features = cells.Select(cell => cell.Feature).Distinct(StringComparer.Ordinal).ToList();
        var places = cells
            .Select(cell => (cell.Target, cell.Server, cell.ServerNone))
            .Distinct()
            .ToList();

        if (features.Count == 0)
        {
            return false;
        }

        if (isEngine || places.Count != 1)
        {
            return className.EndsWith(Suffix, StringComparison.Ordinal)
                && features.Any(feature =>
                    className.StartsWith(feature, StringComparison.Ordinal)
                    && className.Length > feature.Length + Suffix.Length - 1
                );
        }

        var (target, server, _) = places[0];
        var token = target.ToString() + (server?.ToString() ?? "");

        return features.Any(feature =>
            string.Equals(className, feature + token + Suffix, StringComparison.Ordinal)
        );
    }
}
