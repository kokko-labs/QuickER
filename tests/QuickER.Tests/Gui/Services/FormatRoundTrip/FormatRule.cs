using ClosedXML.Excel;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>規則の種類（次元表の水準）</summary>
internal enum FormatRuleKind
{
    /// <summary>取り込める（取り込んだ図の期待と不動点を見る）</summary>
    Accept,

    /// <summary>取込を断る（例外の期待を見る）</summary>
    Reject,

    /// <summary>取り込めるが警告を出す（警告の期待と不動点を見る）</summary>
    Warn,
}

/// <summary>規則の行の期待</summary>
internal sealed class FormatRuleExpectation
{
    /// <summary>取り込んだ図の写し（<see cref="FormatRoundTripRunner.Describe"/>）への期待</summary>
    public FormatLineCheck Imported { get; } = new();

    /// <summary>取込の警告への期待</summary>
    public FormatLineCheck Warnings { get; } = new();

    /// <summary>取込を断る例外の「型名: メッセージ」のパターン（null なら断らない）</summary>
    public string? Rejects { get; set; }

    /// <summary>取込の結果（付帯情報を含む。C# リバースの結果など）を直接見る検査（書式で書けない表明だけ）</summary>
    public List<(string Description, Func<FormatImport, bool> Predicate)> ImportedResult { get; } =
    [];

    /// <summary>取り込んだ図を直接見る検査（書式で書けない表明だけ）</summary>
    public List<(string Description, Func<ErDiagram, bool> Predicate)> ImportedDiagram { get; } =
    [];
}

/// <summary>規則の行の入力（形式の固有の入力・取込の対象 DBMS・期待）</summary>
/// <param name="Format">形式（入力の性質：DBML の文字列は DBML でしか読めない）</param>
/// <param name="Input">入力を成果物にする（アダプタを受ける）</param>
/// <param name="TargetDbms">取込に渡す対象 DBMS</param>
/// <param name="Settings">設定</param>
/// <param name="Expectation">期待</param>
/// <param name="NoFixedPoint">不動点を当てない理由（null なら当てる）</param>
internal sealed record FormatRule(
    string Format,
    Func<FormatAdapter, FormatArtifact> Input,
    string TargetDbms,
    FormatSettings Settings,
    FormatRuleExpectation Expectation,
    string? NoFixedPoint
)
{
    /// <summary>規則の種類（期待から決まる）</summary>
    public FormatRuleKind Kind =>
        Expectation.Rejects is not null ? FormatRuleKind.Reject
        : Expectation.Warnings.IsEmpty ? FormatRuleKind.Accept
        : FormatRuleKind.Warn;
}

/// <summary>規則の行を組む</summary>
internal sealed class FormatRuleBuilder
{
    private readonly string _format;
    private readonly string _id;
    private readonly string _description;
    private Func<FormatAdapter, FormatArtifact>? _input;
    private string _targetDbms = DialectNames.SqlServer;
    private FormatSettings _settings = FormatSettings.Default;
    private readonly FormatRuleExpectation _expectation = new();
    private string? _noFixedPoint;

    /// <summary>行を始める</summary>
    public FormatRuleBuilder(string format, string id, string description)
    {
        FormatAxis.Axis.Get(format);
        _format = format;
        _id = id;
        _description = description;
    }

    /// <summary>文字列の入力（DBML・Mermaid・C# のソース）</summary>
    public FormatRuleBuilder Text(string text)
    {
        _input = adapter => adapter.FromText(text);
        return this;
    }

    /// <summary>Excel のブックの入力（ケースごとに組む）</summary>
    public FormatRuleBuilder Workbook(Func<XLWorkbook> build)
    {
        _input = _ =>
        {
            var workbook = build();
            return new FormatArtifact(ExcelAdapter.Describe(workbook), workbook);
        };
        return this;
    }

    /// <summary>取込に渡す対象 DBMS（既定 sqlserver）</summary>
    public FormatRuleBuilder TargetDbms(string dbms)
    {
        _targetDbms = dbms;
        return this;
    }

    /// <summary>設定</summary>
    public FormatRuleBuilder Settings(FormatSettings settings)
    {
        _settings = settings;
        return this;
    }

    /// <summary>期待</summary>
    public FormatRuleBuilder Expect(Action<FormatRuleExpectation> expect)
    {
        expect(_expectation);
        return this;
    }

    /// <summary>取込を断る（「型名: メッセージ」のパターン）</summary>
    public FormatRuleBuilder Rejects(string pattern)
    {
        _expectation.Rejects = pattern;
        return this;
    }

    /// <summary>不動点を当てない（理由は必須）</summary>
    public FormatRuleBuilder NoFixedPoint(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _noFixedPoint = reason;
        return this;
    }

    /// <summary>行にする</summary>
    public ScenarioRow<FormatRule> Row() =>
        new(
            _id,
            null,
            _description,
            new FormatRule(
                _format,
                _input ?? throw new InvalidOperationException($"{_id}: 入力が無い"),
                _targetDbms,
                _settings,
                _expectation,
                _noFixedPoint
            )
        );

    /// <summary>行への暗黙の変換</summary>
    public static implicit operator ScenarioRow<FormatRule>(FormatRuleBuilder builder) =>
        builder.Row();
}

/// <summary>規則の行を流す</summary>
internal static class FormatRuleRunner
{
    /// <summary>規則の行を流し、問題を返す（空なら合格）</summary>
    public static List<string> Check(FormatRule rule)
    {
        var problems = new List<string>();
        var adapter = FormatAxis.Axis.Get(rule.Format);
        using var artifact = rule.Input(adapter);
        FormatImport imported;

        try
        {
            imported = adapter.Import(artifact, rule.TargetDbms, rule.Settings);
        }
        catch (Exception ex) when (rule.Expectation.Rejects is not null)
        {
            var actual = $"{ex.GetType().Name}: {ex.Message}";

            if (!FormatLineCheck.Like(actual, rule.Expectation.Rejects))
            {
                problems.Add($"断り方が違う: {actual}（期待 {rule.Expectation.Rejects}）");
            }

            return problems;
        }

        if (rule.Expectation.Rejects is not null)
        {
            problems.Add($"断るはずの入力を取り込んだ（期待 {rule.Expectation.Rejects}）");
            return problems;
        }

        problems.AddRange(
            rule.Expectation.Imported.Problems(
                FormatRoundTripRunner.Describe(imported.Diagram),
                "取込"
            )
        );
        problems.AddRange(rule.Expectation.Warnings.Problems(imported.Warnings, "警告"));

        foreach (var (description, predicate) in rule.Expectation.ImportedResult)
        {
            if (!predicate(imported))
            {
                problems.Add($"取込の結果: {description} が成り立たない");
            }
        }

        foreach (var (description, predicate) in rule.Expectation.ImportedDiagram)
        {
            if (!predicate(imported.Diagram))
            {
                problems.Add($"取込: {description} が成り立たない");
            }
        }

        if (rule.NoFixedPoint is null)
        {
            problems.AddRange(
                FormatRoundTripRunner.FixedPoint(adapter, imported.Diagram, rule.Settings)
            );
        }

        return problems;
    }
}
