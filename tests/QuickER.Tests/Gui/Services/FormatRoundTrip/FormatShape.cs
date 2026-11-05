using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>形の行の、ある形式についての期待（データ。形式による分岐ではなく「この形式ではこう出る」の表）</summary>
internal sealed class FormatExpectation
{
    /// <summary>告知の種類の並び（完全一致。null なら見ない）</summary>
    public IReadOnlyList<ExportOmissionKind>? OmissionsExact { get; set; }

    /// <summary>告知に含まれる種類</summary>
    public List<ExportOmissionKind> OmissionsInclude { get; } = [];

    /// <summary>告知に含まれない種類</summary>
    public List<ExportOmissionKind> OmissionsExclude { get; } = [];

    /// <summary>出力のテキスト全体（1 要素）への期待</summary>
    public FormatLineCheck Output { get; } = new();

    /// <summary>出力のテキストの行（改行で分け、行末の CR を除く）への期待</summary>
    public FormatLineCheck OutputLines { get; } = new();

    /// <summary>取り込んだ図の写し（取り込んだ図の自分の名前を鍵にした <see cref="FacetLine"/> の文字列）への期待</summary>
    public FormatLineCheck Imported { get; } = new();

    /// <summary>取込の警告への期待</summary>
    public FormatLineCheck Warnings { get; } = new();

    /// <summary>取込の結果（付帯情報を含む。C# リバースの結果など）を直接見る検査（書式で書けない表明だけ）</summary>
    public List<(string Description, Func<FormatImport, bool> Predicate)> ImportedResult { get; } =
    [];

    /// <summary>取り込んだ図を直接見る検査（書式で書けない表明だけ）</summary>
    public List<(string Description, Func<ErDiagram, bool> Predicate)> ImportedDiagram { get; } =
    [];
}

/// <summary>形の行の入力（図・設定・追加の期待）</summary>
/// <param name="Build">図を組む（ケースごとに呼んで新しい図を作る＝取込・マージが図を書き換えても他のケースへ漏れない）</param>
/// <param name="Settings">形式ごとの設定</param>
/// <param name="Expectations">形式の値 → 期待</param>
/// <param name="DistinctDdlConstraintNames">取り込んだ図の SQL Server の DDL の制約名がすべて違うことも確かめる</param>
/// <param name="CultureProbe">カルチャの代表の形（カルチャ不変の表明をこの形で流す）</param>
internal sealed record FormatShape(
    Func<ErDiagram> Build,
    FormatSettings Settings,
    IReadOnlyDictionary<string, FormatExpectation> Expectations,
    bool DistinctDdlConstraintNames,
    bool CultureProbe
);

/// <summary>形の行を組む</summary>
internal sealed class FormatShapeBuilder
{
    private readonly string _id;
    private readonly string _description;
    private Func<ErDiagram>? _build;
    private FormatSettings _settings = FormatSettings.Default;
    private readonly Dictionary<string, FormatExpectation> _expectations = new(
        StringComparer.Ordinal
    );
    private bool _distinctDdl;
    private bool _cultureProbe;

    /// <summary>行を始める</summary>
    public FormatShapeBuilder(string id, string description)
    {
        _id = id;
        _description = description;
    }

    /// <summary>図を組む</summary>
    public FormatShapeBuilder Build(Func<ErDiagram> build)
    {
        _build = build;
        return this;
    }

    /// <summary>形式ごとの設定</summary>
    public FormatShapeBuilder Settings(FormatSettings settings)
    {
        _settings = settings;
        return this;
    }

    /// <summary>取り込んだ図の SQL Server の DDL の制約名がすべて違う（全形式で確かめる）</summary>
    public FormatShapeBuilder DistinctDdlConstraintNames()
    {
        _distinctDdl = true;
        return this;
    }

    /// <summary>カルチャの代表の形にする（数値・日時・大文字小文字の書き方を持つ図。カルチャ不変の表明をこの形で流す）</summary>
    public FormatShapeBuilder CultureProbe()
    {
        _cultureProbe = true;
        return this;
    }

    /// <summary>形式ごとの期待（形式の値を 1 つ以上）</summary>
    public FormatShapeBuilder Expect(Action<FormatExpectation> expect, params string[] formats)
    {
        if (formats.Length == 0)
        {
            throw new ArgumentException("期待には形式を 1 つ以上指定する", nameof(formats));
        }

        foreach (var format in formats)
        {
            FormatAxis.Axis.Get(format);

            if (!_expectations.TryGetValue(format, out var expectation))
            {
                expectation = new FormatExpectation();
                _expectations[format] = expectation;
            }

            expect(expectation);
        }

        return this;
    }

    /// <summary>行にする</summary>
    public ScenarioRow<FormatShape> Row() =>
        new(
            _id,
            null,
            _description,
            new FormatShape(
                _build ?? throw new InvalidOperationException($"{_id}: 図が無い"),
                _settings,
                _expectations,
                _distinctDdl,
                _cultureProbe
            )
        );

    /// <summary>行への暗黙の変換（表の初期化子で <c>Row()</c> を書かない）</summary>
    public static implicit operator ScenarioRow<FormatShape>(FormatShapeBuilder builder) =>
        builder.Row();
}
