using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>DDL の表の経路（行がどの出力を作るか）</summary>
internal static class DialectDdlPaths
{
    /// <summary>DDL 生成（<see cref="IDdlGenerator.Build"/>）</summary>
    public const string Ddl = "ddl";

    /// <summary>同期スクリプトの表の追加</summary>
    public const string SyncAddTable = "sync-add-table";

    /// <summary>同期スクリプトの列の追加</summary>
    public const string SyncAddColumn = "sync-add-column";

    /// <summary>同期スクリプトのテーブルの再構築（SQLite）</summary>
    public const string SyncRebuild = "sync-rebuild";

    /// <summary>同期スクリプトのそのほかの項目（主キー・外部キー・列の変更・説明・表の削除・列順）</summary>
    public const string SyncOther = "sync-other";

    /// <summary>すべての経路（次元表の水準の並び）</summary>
    public static IReadOnlyList<string> All { get; } =
    [Ddl, SyncAddTable, SyncAddColumn, SyncRebuild, SyncOther];
}

/// <summary>DDL の表の要素（行が主に何の出力を見るか）</summary>
internal static class DialectDdlElements
{
    /// <summary>表（CREATE TABLE・表のオプション・ヘッダー）</summary>
    public const string Table = "table";

    /// <summary>列の定義</summary>
    public const string Column = "column";

    /// <summary>主キー</summary>
    public const string PrimaryKey = "pk";

    /// <summary>外部キー</summary>
    public const string ForeignKey = "fk";

    /// <summary>一意制約</summary>
    public const string Unique = "unique";

    /// <summary>計算列</summary>
    public const string Computed = "computed";

    /// <summary>DB が採番する列</summary>
    public const string Identity = "identity";

    /// <summary>説明</summary>
    public const string Description = "description";

    /// <summary>スキーマ修飾名（ドットを含むテーブル名）</summary>
    public const string SchemaQualified = "schema-qualified";

    /// <summary>多対多のリレーション</summary>
    public const string ManyToMany = "many-to-many";

    /// <summary>列の型の表記</summary>
    public const string Type = "type";

    /// <summary>すべての要素（次元表の水準の並び）</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Table,
        Column,
        PrimaryKey,
        ForeignKey,
        Unique,
        Computed,
        Identity,
        Description,
        SchemaQualified,
        ManyToMany,
        Type,
    ];
}

/// <summary>DDL の表の観点（行が出力の何を確かめるか）</summary>
internal static class DialectDdlFacets
{
    /// <summary>文の形（文の種類・句の有無・並び・区切り）</summary>
    public const string StatementShape = "statement-shape";

    /// <summary>識別子のクォート・エスケープ・分割</summary>
    public const string Quote = "quote";

    /// <summary>制約名（既定名・合成名・安全化）</summary>
    public const string ConstraintName = "constraint-name";

    /// <summary>注記（告げる）</summary>
    public const string Note = "note";

    /// <summary>図の要素を出さずに外す</summary>
    public const string Omit = "omit";

    /// <summary>並べ替えて出す</summary>
    public const string Order = "order";

    /// <summary>図全体を断る（入口の検証）</summary>
    public const string Reject = "reject";

    /// <summary>固定文が英語</summary>
    public const string English = "english";

    /// <summary>書式のカルチャに依らない</summary>
    public const string Culture = "culture";

    /// <summary>コメントへ埋める名前・説明の改行を畳む</summary>
    public const string CommentSanitize = "comment-sanitize";

    /// <summary>すべての観点（次元表の水準の並び）</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        StatementShape,
        Quote,
        ConstraintName,
        Note,
        Omit,
        Order,
        Reject,
        English,
        Culture,
        CommentSanitize,
    ];
}

/// <summary>期待を照合する面</summary>
internal static class DialectDdlOutputs
{
    /// <summary>出力（DDL・同期スクリプト）を構造の行へ写したもの（<see cref="DialectDdlReader"/>）</summary>
    public const string Lines = "lines";

    /// <summary>出力の素の行（行末の空白を除く・空行を除く）</summary>
    public const string Text = "text";

    /// <summary>同期の計画の警告（<c>{種類} {表} {詳細}</c>）</summary>
    public const string Warnings = "warnings";

    /// <summary>出力を作る処理が投げた例外（<c>{型名}: {メッセージ}</c> の 1 行。投げなければ空）</summary>
    public const string Exception = "exception";

    /// <summary>すべての面</summary>
    public static IReadOnlyList<string> All { get; } = [Lines, Text, Warnings, Exception];
}

/// <summary>期待の強さ</summary>
internal enum DdlMatch
{
    /// <summary>どのパターンにも合う行が 1 つ以上ある（包含）</summary>
    Contains,

    /// <summary>どのパターンにも合う行が無い</summary>
    Absent,

    /// <summary>パターンに合う行がちょうど <see cref="DdlExpectation.Count"/> 個</summary>
    Count,

    /// <summary>行の数が同じで、i 番目の行が i 番目のパターンに合う</summary>
    Exact,

    /// <summary>パターンが続いた行に並ぶ（どこかに <c>p1, p2, …</c> に合う連続した行がある）</summary>
    Sequence,

    /// <summary>パターンがこの順に現れる（間にほかの行があってよい）</summary>
    Order,
}

/// <summary>期待 1 つ</summary>
/// <param name="Output">照合する面（<see cref="DialectDdlOutputs"/>）</param>
/// <param name="Match">強さ</param>
/// <param name="Patterns">行のパターン（<c>*</c> は任意の文字列。ほかの文字はそのまま）</param>
/// <param name="Count">強さが <see cref="DdlMatch.Count"/> のときの個数</param>
internal sealed record DdlExpectation(
    string Output,
    DdlMatch Match,
    IReadOnlyList<string> Patterns,
    int Count = 0
)
{
    /// <summary>1 行で書く（失敗の文に使う）</summary>
    public override string ToString() =>
        $"{Output} {Match}{(Match == DdlMatch.Count ? $"={Count}" : "")} [{string.Join(", ", Patterns.Select(DialectIdentifierRow.Show))}]";
}

/// <summary>同期の計画を作る入力（手組みの項目）</summary>
/// <param name="Items">差分の項目（選んだもの）</param>
/// <param name="Capabilities">能力（null なら方言の宣言。方言差を見ない行は <c>Neutral</c>）</param>
/// <param name="Context">live の文脈（null なら無し）</param>
internal sealed record DdlSyncItems(
    IReadOnlyList<SchemaDiffItem> Items,
    SyncDialectCapabilities? Capabilities = null,
    SyncPlanContext? Context = null
);

/// <summary>同期の計画を差分から作る入力</summary>
/// <param name="LiveEntities">live の表</param>
/// <param name="LiveRelationships">live のリレーション</param>
/// <param name="TargetEntities">目標の表</param>
/// <param name="TargetRelationships">目標のリレーション</param>
/// <param name="Select">差分の項目の選び方（null なら選べる項目をすべて選ぶ）</param>
internal sealed record DdlSyncDiff(
    IReadOnlyList<Entity> LiveEntities,
    IReadOnlyList<Relationship> LiveRelationships,
    IReadOnlyList<Entity> TargetEntities,
    IReadOnlyList<Relationship> TargetRelationships,
    Action<IReadOnlyList<SchemaDiffItem>>? Select = null
);

/// <summary>DDL の表の 1 行の中身</summary>
/// <param name="Path">経路（<see cref="DialectDdlPaths"/>）</param>
/// <param name="Element">要素（<see cref="DialectDdlElements"/>）</param>
/// <param name="Facet">観点（<see cref="DialectDdlFacets"/>）</param>
/// <param name="Build">
/// 方言を受けて入力を作る（<see cref="ErDiagram"/>・<see cref="DdlSyncItems"/>・<see cref="DdlSyncDiff"/>・<see cref="SyncPlan"/> のどれか。
/// 方言は図の型の表記を表の中の対応から引くためだけに使う）
/// </param>
/// <param name="Expected">方言ごとの期待</param>
/// <param name="Treatment">扱いの表のマス（種類・機能・方言ごとの扱い。扱いの行だけ）</param>
internal sealed record DialectDdlCase(
    string Path,
    string Element,
    string Facet,
    Func<string, object> Build,
    IReadOnlyDictionary<string, IReadOnlyList<DdlExpectation>> Expected,
    DdlTreatmentCell? Treatment
);

/// <summary>扱いの行が言う扱いの表のマス（方言ごとの扱いは行が手で書き、宣言と照合する）</summary>
/// <param name="Kind">図で表せない定義の種類</param>
/// <param name="Feature">機能</param>
/// <param name="Treatments">方言ごとの扱い</param>
internal sealed record DdlTreatmentCell(
    UnsupportedDefinitionKind Kind,
    UnsupportedFeature Feature,
    IReadOnlyDictionary<string, UnsupportedTreatment> Treatments
);

/// <summary>1 方言の期待の組み立て（同じ方言へ何度でも足せる）</summary>
internal sealed class DdlExpect
{
    private readonly List<DdlExpectation> _items = [];

    /// <summary>組み立てた期待</summary>
    public IReadOnlyList<DdlExpectation> Items => _items;

    /// <summary>どのパターンにも合う行が構造の行に 1 つ以上ある</summary>
    public DdlExpect Lines(params string[] patterns) =>
        Add(DialectDdlOutputs.Lines, DdlMatch.Contains, patterns);

    /// <summary>どのパターンにも合う行が構造の行に無い</summary>
    public DdlExpect NoLines(params string[] patterns) =>
        Add(DialectDdlOutputs.Lines, DdlMatch.Absent, patterns);

    /// <summary>どのパターンにも合う行が素の行に 1 つ以上ある</summary>
    public DdlExpect Text(params string[] patterns) =>
        Add(DialectDdlOutputs.Text, DdlMatch.Contains, patterns);

    /// <summary>どのパターンにも合う行が素の行に無い</summary>
    public DdlExpect NoText(params string[] patterns) =>
        Add(DialectDdlOutputs.Text, DdlMatch.Absent, patterns);

    /// <summary>面のパターンに合う行がちょうど <paramref name="count"/> 個</summary>
    public DdlExpect Count(string output, int count, string pattern)
    {
        _items.Add(new DdlExpectation(output, DdlMatch.Count, [pattern], count));
        return this;
    }

    /// <summary>面の行が続いてパターンに並ぶ</summary>
    public DdlExpect Sequence(string output, params string[] patterns) =>
        Add(output, DdlMatch.Sequence, patterns);

    /// <summary>面の行にパターンがこの順に現れる</summary>
    public DdlExpect Order(string output, params string[] patterns) =>
        Add(output, DdlMatch.Order, patterns);

    /// <summary>面の行がちょうどパターンの並び</summary>
    public DdlExpect Exact(string output, params string[] patterns) =>
        Add(output, DdlMatch.Exact, patterns);

    /// <summary>面の行がどのパターンにも合わない（面を指定する）</summary>
    public DdlExpect Absent(string output, params string[] patterns) =>
        Add(output, DdlMatch.Absent, patterns);

    /// <summary>面の行にどのパターンにも合う行が 1 つ以上ある（面を指定する）</summary>
    public DdlExpect Contains(string output, params string[] patterns) =>
        Add(output, DdlMatch.Contains, patterns);

    /// <summary>例外を投げない（出力を作れる）</summary>
    public DdlExpect NoException() => Exact(DialectDdlOutputs.Exception);

    /// <summary>例外を投げ、その 1 行がパターンに合う</summary>
    public DdlExpect Throws(string pattern) => Exact(DialectDdlOutputs.Exception, pattern);

    private DdlExpect Add(string output, DdlMatch match, string[] patterns)
    {
        if (match != DdlMatch.Exact && patterns.Length == 0)
        {
            throw new ArgumentException($"{output} の {match} にパターンが無い", nameof(patterns));
        }

        _items.Add(new DdlExpectation(output, match, patterns));
        return this;
    }
}

/// <summary>DDL の表の行の組み立て（主題ごとの行のファイルが使う）</summary>
internal sealed class DialectDdlRow
{
    private readonly Dictionary<string, List<DdlExpectation>> _expected = new(
        StringComparer.Ordinal
    );
    private DdlTreatmentCell? _treatment;

    private DialectDdlRow(
        string id,
        string description,
        string path,
        string element,
        string facet,
        Func<string, object> build
    )
    {
        Id = id;
        Description = description;
        Path = path;
        Element = element;
        Facet = facet;
        Build = build;
    }

    /// <summary>行の Id</summary>
    public string Id { get; }

    /// <summary>説明（テストの表示名）</summary>
    public string Description { get; }

    /// <summary>経路</summary>
    public string Path { get; }

    /// <summary>要素</summary>
    public string Element { get; }

    /// <summary>観点</summary>
    public string Facet { get; }

    /// <summary>入力を作る</summary>
    public Func<string, object> Build { get; }

    /// <summary>図から DDL を作る行</summary>
    public static DialectDdlRow Ddl(
        string id,
        string description,
        string element,
        string facet,
        Func<string, ErDiagram> diagram
    ) => new(id, description, DialectDdlPaths.Ddl, element, facet, d => diagram(d));

    /// <summary>手組みの項目から同期スクリプトを作る行</summary>
    public static DialectDdlRow SyncItems(
        string id,
        string description,
        string path,
        string element,
        string facet,
        Func<string, DdlSyncItems> items
    ) => new(id, description, path, element, facet, d => items(d));

    /// <summary>差分から同期スクリプトを作る行</summary>
    public static DialectDdlRow SyncDiff(
        string id,
        string description,
        string path,
        string element,
        string facet,
        Func<string, DdlSyncDiff> diff
    ) => new(id, description, path, element, facet, d => diff(d));

    /// <summary>手組みの計画から同期スクリプトを作る行（SQLite の再構築）</summary>
    public static DialectDdlRow SyncPlan(
        string id,
        string description,
        string element,
        string facet,
        Func<string, SyncPlan> plan
    ) => new(id, description, DialectDdlPaths.SyncRebuild, element, facet, d => plan(d));

    /// <summary>方言の期待を足す</summary>
    /// <param name="dialect">方言（<see cref="DialectNames"/> の定数）</param>
    /// <param name="expect">期待の組み立て</param>
    public DialectDdlRow Expect(string dialect, Action<DdlExpect> expect)
    {
        var builder = new DdlExpect();
        expect(builder);

        if (!_expected.TryGetValue(dialect, out var list))
        {
            list = [];
            _expected.Add(dialect, list);
        }

        list.AddRange(builder.Items);
        return this;
    }

    /// <summary>いくつかの方言へ同じ期待を足す</summary>
    /// <param name="dialects">方言</param>
    /// <param name="expect">期待の組み立て</param>
    public DialectDdlRow Expect(IEnumerable<string> dialects, Action<DdlExpect> expect)
    {
        foreach (var dialect in dialects)
        {
            Expect(dialect, expect);
        }

        return this;
    }

    /// <summary>5 方言すべてへ同じ期待を足す</summary>
    /// <param name="expect">期待の組み立て</param>
    public DialectDdlRow ExpectAll(Action<DdlExpect> expect) => Expect(DialectNames.All, expect);

    /// <summary>扱いの表のマスを言う（方言ごとの扱いを手で書く。自動の照合が宣言と比べる）</summary>
    /// <param name="kind">図で表せない定義の種類</param>
    /// <param name="feature">機能</param>
    /// <param name="treatments">方言ごとの扱い（書いた方言だけを照合する）</param>
    public DialectDdlRow Treats(
        UnsupportedDefinitionKind kind,
        UnsupportedFeature feature,
        params (string Dialect, UnsupportedTreatment Treatment)[] treatments
    )
    {
        _treatment = new DdlTreatmentCell(
            kind,
            feature,
            treatments.ToDictionary(t => t.Dialect, t => t.Treatment, StringComparer.Ordinal)
        );
        return this;
    }

    /// <summary>表の行にする</summary>
    public ScenarioRow<DialectDdlCase> ToScenario() =>
        new(
            Id,
            null,
            Description,
            new DialectDdlCase(
                Path,
                Element,
                Facet,
                Build,
                _expected.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<DdlExpectation>)pair.Value.ToList(),
                    StringComparer.Ordinal
                ),
                _treatment
            )
        );
}
