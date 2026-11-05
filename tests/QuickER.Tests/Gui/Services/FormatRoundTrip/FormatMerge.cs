using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>マージの経路（次元表の水準）</summary>
internal enum FormatMergePath
{
    /// <summary>Id の引き継ぎだけ（<see cref="DiagramMergeReconciler.Reconcile"/>。形式に依らない共通の段）</summary>
    Reconcile,

    /// <summary>Excel 定義書のマージ取込（<see cref="ExcelAdapter"/> の <see cref="FormatAdapter.Merge"/>）</summary>
    Excel,

    /// <summary>C# リバースのマージ取込（<see cref="CSharpReverseAdapter"/> の <see cref="FormatAdapter.Merge"/>）</summary>
    CSharpReverse,
}

/// <summary>取込の図の出どころ（次元表の水準）</summary>
internal enum FormatMergeSource
{
    /// <summary>図を直接組む（Reconcile の経路）／図を形式へ出力して取り込む（形式の経路）</summary>
    Diagram,

    /// <summary>形式の固有の入力（C# のソース・Excel のブック）を取り込む</summary>
    Input,
}

/// <summary>マージの結果（マージ後の図と、Id の引き継ぎの結果）</summary>
/// <param name="Merged">マージ後の図（エンティティ・リレーション・生き残ったクエリ）</param>
/// <param name="Reconciled">Id の引き継ぎの結果（生き残ったクエリ・壊れたクエリ・説明の上書き件数）</param>
internal sealed record FormatMergeOutcome(ErDiagram Merged, DiagramMergeResult Reconciled);

/// <summary>マージの行の期待</summary>
internal sealed class FormatMergeExpectation
{
    /// <summary>
    /// マージ後の図の写し（<see cref="FormatRoundTripRunner.Describe"/>）に、Id の出どころの行
    /// （<c>Id table {表} = current:{現在の図の表}|imported|new</c>・<c>Id column {表}.{列} = current:{表}.{列}|imported|new</c>）と、
    /// クエリの行（<c>Surviving {名前}</c>・<c>Broken {名前}</c>）と、<c>DescriptionOverwriteCount = n</c> を足したものへの期待
    /// </summary>
    public FormatLineCheck Merged { get; } = new();

    /// <summary>書式で書けない表明（現在の図とマージの結果を受ける）</summary>
    public List<(
        string Description,
        Func<ErDiagram, FormatMergeOutcome, bool> Predicate
    )> That { get; } = [];
}

/// <summary>マージの行の入力</summary>
/// <param name="Path">経路</param>
/// <param name="Current">現在の図を組む</param>
/// <param name="ImportedDiagram">取込の図を組む（Reconcile では取込の図そのもの、形式の経路では形式へ出力して取り込む図。null なら現在の図）</param>
/// <param name="ImportedInput">形式の固有の入力（C# のソース・Excel のブック。形式の経路だけ）</param>
/// <param name="PreserveMemo">Reconcile の経路で一致したエンティティのメモに現在の図の値を残すか</param>
/// <param name="TargetDbms">取込に渡す対象 DBMS</param>
/// <param name="Settings">設定</param>
/// <param name="Expectation">期待</param>
internal sealed record FormatMerge(
    FormatMergePath Path,
    Func<ErDiagram> Current,
    Func<ErDiagram>? ImportedDiagram,
    Func<FormatAdapter, FormatArtifact>? ImportedInput,
    bool PreserveMemo,
    string TargetDbms,
    FormatSettings Settings,
    FormatMergeExpectation Expectation
)
{
    /// <summary>取込の図の出どころ</summary>
    public FormatMergeSource Source =>
        ImportedInput is not null ? FormatMergeSource.Input : FormatMergeSource.Diagram;
}

/// <summary>マージの行を組む</summary>
internal sealed class FormatMergeBuilder
{
    private readonly FormatMergePath _path;
    private readonly string _id;
    private readonly string _description;
    private Func<ErDiagram>? _current;
    private Func<ErDiagram>? _importedDiagram;
    private Func<FormatAdapter, FormatArtifact>? _importedInput;
    private bool _preserveMemo;
    private string _targetDbms = DialectNames.SqlServer;
    private FormatSettings _settings = FormatSettings.Default;
    private readonly FormatMergeExpectation _expectation = new();

    /// <summary>行を始める</summary>
    public FormatMergeBuilder(FormatMergePath path, string id, string description)
    {
        _path = path;
        _id = id;
        _description = description;
    }

    /// <summary>現在の図</summary>
    public FormatMergeBuilder Current(Func<ErDiagram> build)
    {
        _current = build;
        return this;
    }

    /// <summary>取込の図（Reconcile では取込の図そのもの、形式の経路では形式へ出力して取り込む図）</summary>
    public FormatMergeBuilder Imported(Func<ErDiagram> build)
    {
        _importedDiagram = build;
        return this;
    }

    /// <summary>形式の固有の文字列の入力（C# のソース）</summary>
    public FormatMergeBuilder ImportedText(string text)
    {
        _importedInput = adapter => adapter.FromText(text);
        return this;
    }

    /// <summary>形式の固有の入力（Excel のブックなど。アダプタを受けて成果物を作る）</summary>
    public FormatMergeBuilder ImportedInput(Func<FormatAdapter, FormatArtifact> input)
    {
        _importedInput = input;
        return this;
    }

    /// <summary>Reconcile の経路で一致したエンティティのメモに現在の図の値を残す（既定は残さない）</summary>
    public FormatMergeBuilder PreserveMemo(bool preserve = true)
    {
        _preserveMemo = preserve;
        return this;
    }

    /// <summary>取込に渡す対象 DBMS（既定 sqlserver）</summary>
    public FormatMergeBuilder TargetDbms(string dbms)
    {
        _targetDbms = dbms;
        return this;
    }

    /// <summary>設定</summary>
    public FormatMergeBuilder Settings(FormatSettings settings)
    {
        _settings = settings;
        return this;
    }

    /// <summary>期待</summary>
    public FormatMergeBuilder Expect(Action<FormatMergeExpectation> expect)
    {
        expect(_expectation);
        return this;
    }

    /// <summary>行にする</summary>
    public ScenarioRow<FormatMerge> Row() =>
        new(
            _id,
            null,
            _description,
            new FormatMerge(
                _path,
                _current ?? throw new InvalidOperationException($"{_id}: 現在の図が無い"),
                _importedDiagram,
                _importedInput,
                _preserveMemo,
                _targetDbms,
                _settings,
                _expectation
            )
        );

    /// <summary>行への暗黙の変換</summary>
    public static implicit operator ScenarioRow<FormatMerge>(FormatMergeBuilder builder) =>
        builder.Row();
}

/// <summary>マージの行を流す</summary>
internal static class FormatMergeRunner
{
    /// <summary>形式の経路のアダプタ</summary>
    public static FormatAdapter? AdapterOf(FormatMergePath path) =>
        path switch
        {
            FormatMergePath.Reconcile => null,
            FormatMergePath.Excel => FormatAxis.Axis.Get(
                nameof(QuickER.Services.DiagramExportFormat.Excel)
            ),
            FormatMergePath.CSharpReverse => FormatAxis.Axis.Get(CSharpReverseAdapter.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        };

    /// <summary>マージの行を流し、問題を返す（空なら合格）</summary>
    public static List<string> Check(FormatMerge merge)
    {
        var problems = new List<string>();
        var current = merge.Current();
        var currentKeys = FormatPairing.Identity(current);
        ErDiagram importedDiagram;
        FormatMergeOutcome outcome;
        var adapter = AdapterOf(merge.Path);

        if (adapter is null)
        {
            if (merge.ImportedInput is not null || merge.ImportedDiagram is null)
            {
                problems.Add("Reconcile の経路は取込の図を直接組む");
                return problems;
            }

            importedDiagram = merge.ImportedDiagram();
            var reconciled = DiagramMergeReconciler.Reconcile(
                current,
                importedDiagram.Entities,
                importedDiagram.Relationships,
                merge.PreserveMemo
            );
            outcome = new FormatMergeOutcome(
                new ErDiagram
                {
                    TargetDbms = current.TargetDbms,
                    Entities = reconciled.Entities.ToList(),
                    Relationships = reconciled.Relationships.ToList(),
                    Queries = reconciled.SurvivingQueries.ToList(),
                },
                reconciled
            );
        }
        else
        {
            using var artifact =
                merge.ImportedInput?.Invoke(adapter)
                ?? adapter.Export(
                    merge.ImportedDiagram?.Invoke() ?? merge.Current(),
                    merge.Settings
                );
            var imported = adapter.Import(artifact, merge.TargetDbms, merge.Settings);
            importedDiagram = imported.Diagram;
            outcome = adapter.Merge!(current, imported);
        }

        problems.AddRange(
            merge.Expectation.Merged.Problems(Describe(current, importedDiagram, outcome), "マージ")
        );

        foreach (var (description, predicate) in merge.Expectation.That)
        {
            if (!predicate(current, outcome))
            {
                problems.Add($"マージ: {description} が成り立たない");
            }
        }

        return problems;
    }

    /// <summary>マージの結果の写し（<see cref="FormatMergeExpectation.Merged"/> の書式）</summary>
    /// <param name="current">現在の図</param>
    /// <param name="imported">取込の図（Id はマージがその場で書き換えた後でもよい。Id の出どころは現在の図にあるかで決める）</param>
    /// <param name="outcome">マージの結果</param>
    public static List<string> Describe(
        ErDiagram current,
        ErDiagram imported,
        FormatMergeOutcome outcome
    )
    {
        var lines = FormatRoundTripRunner.Describe(outcome.Merged);
        var currentKeys = FormatPairing.Identity(current);
        var currentTables = current.Entities.Select(e => e.Id).ToHashSet();
        var currentColumns = current
            .Entities.SelectMany(e => e.Columns)
            .Select(c => c.Id)
            .ToHashSet();
        var importedTables = imported.Entities.Select(e => e.Id).ToHashSet();
        var importedColumns = imported
            .Entities.SelectMany(e => e.Columns)
            .Select(c => c.Id)
            .ToHashSet();

        foreach (var entity in outcome.Merged.Entities)
        {
            lines.Add(
                $"Id table {entity.TableName} = "
                    + (
                        currentTables.Contains(entity.Id)
                            ? "current:" + currentKeys.Table(entity.Id)
                        : importedTables.Contains(entity.Id) ? "imported"
                        : "new"
                    )
            );

            foreach (var column in entity.Columns)
            {
                lines.Add(
                    $"Id column {entity.TableName}.{column.Name} = "
                        + (
                            currentColumns.Contains(column.Id)
                                ? "current:" + currentKeys.Column(column.Id)
                            : importedColumns.Contains(column.Id) ? "imported"
                            : "new"
                        )
                );
            }
        }

        lines.AddRange(outcome.Reconciled.SurvivingQueries.Select(q => $"Surviving {q.Name}"));
        lines.AddRange(outcome.Reconciled.BrokenQueries.Select(q => $"Broken {q.Name}"));
        lines.Add($"DescriptionOverwriteCount = {outcome.Reconciled.DescriptionOverwriteCount}");
        return lines;
    }
}
