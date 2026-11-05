using System.Collections.Concurrent;
using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>文書の保存・読込・外部変更・自動保存の状態機械のエンジン（決まった形のシナリオ）</summary>
/// <remarks>
/// 各行の手の列を VM と参照モデル（<see cref="DocumentFileModel"/>）で流して毎手で突き合わせ、不変条件
/// （<see cref="DocumentFileInvariants"/>）と元のテストの表明を当てる。詳細は <see cref="DocumentFileRunner"/>。
/// <para>
/// ドラッグの手が DragBehavior の進行中の状態（スレッドごとの静的な状態）を触るので、ほかのドラッグのテストと同じ
/// <c>CanvasInteractionState</c> コレクションで直列化する。
/// </para>
/// </remarks>
[Collection("CanvasInteractionState")]
public sealed class DocumentFileTests
{
    /// <summary>決まった形のシナリオの Id</summary>
    public static TheoryData<string> ScenarioIds => DocumentFileScenarios.Table.Ids();

    /// <summary>決まった形のシナリオを流す</summary>
    [Theory(DisplayName = "文書の状態機械: 決まった形のシナリオ")]
    [MemberData(nameof(ScenarioIds))]
    public void Scenario_Holds(string id)
    {
        var row = DocumentFileScenarios.Table[id];

        var problems = DocumentFileRunner.Run(row.Input.Steps);

        problems.Should().BeEmpty($"{id}（{row.Description}）");
    }

    /// <summary>乱数の手の列を種ごとに流す（普段 16 種・夜間 400 種。種は並列に流す）</summary>
    [Fact(DisplayName = "文書の状態機械: 乱数の手の列でもモデル・不変条件と一致する")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public void Random_Holds()
    {
        var plan = SeedSource.Resolve(DocumentFileGenerator.Seeds);
        plan.WriteWarnings();
        var failures = new ConcurrentBag<EngineFailure>();

        Parallel.ForEach(
            plan.Seeds,
            seed =>
            {
                var problems = DocumentFileRunner.Run(DocumentFileGenerator.Generate(seed));

                if (problems.Count > 0)
                {
                    failures.Add(
                        new EngineFailure(
                            DocumentFileDimensions.RandomScope,
                            "random",
                            string.Join(Environment.NewLine, problems),
                            seed
                        )
                    );
                }
            }
        );

        DocumentFileDimensions
            .KnownFailures.Classify(failures.OrderBy(f => f.Seed), plan)
            .AssertClean("文書の状態機械の乱数の手の列");
    }

    /// <summary>決まった形のシナリオと乱数の手の列の網羅を数え、網羅しない組の一覧と照合する（VM で流さない）</summary>
    [Fact(DisplayName = "文書の状態機械: シナリオと乱数の手の列の網羅は一覧と一致する")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public void Coverage_MatchesGaps()
    {
        var plan = SeedSource.Resolve(DocumentFileGenerator.Seeds);
        var counter = new CoverageCounter(DocumentFileDimensions.Table);

        foreach (var row in DocumentFileScenarios.Table)
        {
            DocumentFileDimensions.Observe(counter, row.Input.Steps);
        }

        foreach (var seed in plan.Seeds)
        {
            DocumentFileDimensions.Observe(counter, DocumentFileGenerator.Generate(seed));
        }

        var report = counter.Report();
        report.WriteSummary();
        report.WriteGapUsage(DocumentFileDimensions.Gaps, DocumentFileDimensions.RandomScope);
        report.AssertGaps(DocumentFileDimensions.Gaps, DocumentFileDimensions.RandomScope, plan);
    }

    /// <summary>
    /// 反射の読み方が、製品が記録した最終既知ハッシュと「続行」で無視したハッシュを指していることを検証する
    /// </summary>
    /// <remarks>
    /// エンジンの観測の口（<see cref="DocumentFileProbe"/>）の自己検査。読む先のフィールドを取り違えると、不変条件
    /// 「最終既知ハッシュは書いた／読んだ内容そのもの」が別の値と比べて黙って緑になるため、単独で固定する。
    /// </remarks>
    [Fact(DisplayName = "文書の状態機械: 観測の口は最終既知ハッシュと無視したハッシュを読む")]
    public void Probe_ReadsTheRecordedHashes()
    {
        using var temp = new TestTempFolder();
        var folder = temp.Path;
        var path = Path.Combine(folder, "Doc.json");
        JsonStorageService.SaveAtomic(path, Document("Original"));
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new MainViewModel(
            dialogs,
            files: new RecordingFileDialogService { OpenResult = new(path, 1) }
        );
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(folder),
            Path.Combine(folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();

        vm.OpenCommand.Execute(null);

        DocumentFileProbe.LastKnownHash(vm).Should().Be(DocumentContentHash.TryCompute(path));
        DocumentFileProbe.IgnoredHash(vm).Should().BeNull();

        vm.AddEntityCommand.Execute(null);
        JsonStorageService.SaveAtomic(path, Document("External"));
        var external = DocumentContentHash.TryCompute(path);
        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Modified, external);

        DocumentFileProbe.IgnoredHash(vm).Should().Be(external, "続行を選んだ外部の版");
        DocumentFileProbe.LastKnownHash(vm).Should().NotBe(external);
    }

    private static DiagramDocument Document(string table) =>
        new()
        {
            Schema = new ErDiagram
            {
                Entities = { new Entity { TableName = table } },
                TargetDbms = "sqlserver",
            },
            Layout = null,
        };
}
