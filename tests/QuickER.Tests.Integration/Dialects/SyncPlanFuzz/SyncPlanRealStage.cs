using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>期待の行の実 DB の段の入力（DB の前提の作り方・目標の図の写し方・探針・期待）</summary>
internal sealed class RealStageInput
{
    /// <summary>流す方言（null なら行の能力の鍵のうちの実方言すべて）</summary>
    public IReadOnlyList<string>? Dialects { get; set; }

    /// <summary>網では流さない（DB を生の SQL だけで作る行・網の図と DB の図が食い違う行）</summary>
    public bool RealOnly { get; set; }

    /// <summary>DDL の前に流す文（方言 null は全方言）</summary>
    public List<(string? Dialect, string Sql)> Prepare { get; } = [];

    /// <summary>DDL の代わりに DB を作る文（方言 null は全方言。図の DDL は流さない）</summary>
    public List<(string? Dialect, string Sql)> LiveSql { get; } = [];

    /// <summary>DDL の後に流す文（行・ビュー・トリガー・DEFAULT 制約など。方言 null は全方言）</summary>
    public List<(string? Dialect, string Sql)> Setup { get; } = [];

    /// <summary>
    /// 同期の実行器を通さずに DB へ直接流す文（<see cref="Setup"/> の後。実行器が増加を断る前提＝最初から壊れたビュー・既存の外部キー違反を
    /// 作るときだけ。方言 null は全方言）
    /// </summary>
    public List<(string? Dialect, string Sql)> RawSetup { get; } = [];

    /// <summary>管理者として DDL の前に流す文（別のユーザー・データベースの作成。<see cref="Prepare"/> より前・方言 null は全方言）</summary>
    public List<(string? Dialect, string Sql)> AdminSetup { get; } = [];

    /// <summary>探針の後に必ず管理者として流す後片付けの文（<see cref="Teardown"/> の後・方言 null は全方言）</summary>
    public List<(string? Dialect, string Sql)> AdminTeardown { get; } = [];

    /// <summary>行が持つ、管理者の接続で流す問い合わせ（名前・方言 null は全方言・SQL）。面は <c>probe:{名前}</c></summary>
    public List<(string Name, string? Dialect, string Sql)> AdminQueries { get; } = [];

    /// <summary>探針の後に必ず流す後片付けの文（方言 null は全方言。失敗は問題にする）</summary>
    public List<(string? Dialect, string Sql)> Teardown { get; } = [];

    /// <summary>目標の図を、取り込んだ図と同じ Guid のまま写す（理由。null なら別の Guid＝ランナーと同じ）</summary>
    public string? PreserveIdsReason { get; set; }

    /// <summary>
    /// 計画が警告しない失敗をわざと起こす（理由。null でなければ表明 1＝失敗なら警告がある、を外す）。計画が予言できないデータの都合の失敗と、
    /// 計画が見ないモデルの外の DB の物（ビューなど）による失敗の 2 種類だけ
    /// </summary>
    public string? UnwarnedFailureReason { get; set; }

    /// <summary>流すカタログの探針の名前（<see cref="SyncPlanCatalogProbes"/>）</summary>
    public List<string> Probes { get; } = [];

    /// <summary>行が持つデータの問い合わせ（名前・方言 null は全方言・SQL）。面は <c>probe:{名前}</c></summary>
    public List<(string Name, string? Dialect, string Sql)> Queries { get; } = [];

    /// <summary>実 DB の段の期待</summary>
    public ExpectationBuilder Expect { get; } = new();

    /// <summary>方言に当たる文</summary>
    public static IEnumerable<string> For(
        IEnumerable<(string? Dialect, string Sql)> statements,
        string dialect
    ) => statements.Where(s => s.Dialect is null || s.Dialect == dialect).Select(s => s.Sql);
}

/// <summary>実 DB の段を書く（<see cref="ExpectationRow.OnRealDatabase"/> の中で使う）</summary>
internal sealed class RealStage(RealStageInput input)
{
    /// <summary>行の能力のうち、この方言だけで流す（既定は行の実方言すべて）</summary>
    public RealStage Only(params string[] dialects)
    {
        input.Dialects = dialects;
        return this;
    }

    /// <summary>網では流さない</summary>
    public RealStage RealOnly()
    {
        input.RealOnly = true;
        return this;
    }

    /// <summary>DDL の前に流す文</summary>
    public RealStage Prepare(string sql, string? dialect = null)
    {
        input.Prepare.Add((dialect, sql));
        return this;
    }

    /// <summary>DDL の代わりに DB を作る文（行は網で流さない）</summary>
    public RealStage LiveSql(string sql, string? dialect = null)
    {
        input.LiveSql.Add((dialect, sql));
        input.RealOnly = true;
        return this;
    }

    /// <summary>DDL の後に流す文</summary>
    public RealStage Setup(string sql, string? dialect = null)
    {
        input.Setup.Add((dialect, sql));
        return this;
    }

    /// <summary>
    /// 同期の実行器を通さずに DB へ直接流す文（方言の <see cref="SyncPlanFuzzDialect.CreateQueryConnection"/> へ 1 つのコマンドで流す）。
    /// 用途は、実行器の検査を通さずに置く前提（最初から壊れているビュー・既存の外部キー違反）だけ。普通の前提は <see cref="LiveSql"/> と
    /// <see cref="Setup"/> で作る。SQLite の接続は外部キーを検査するので、違反の行は文の中で <c>PRAGMA foreign_keys</c> を切り替えて入れ、元へ戻す
    /// </summary>
    public RealStage RawSetup(string sql, string? dialect = null)
    {
        input.RawSetup.Add((dialect, sql));
        return this;
    }

    /// <summary>
    /// 管理者として DDL の前に流す文（別のユーザー・データベースの作成など、アプリの接続の権限で作れない前提だけ。Oracle は SYSTEM・
    /// MySQL は root。後片付けは <see cref="AdminTeardown"/>）
    /// </summary>
    public RealStage AdminSetup(string sql, string? dialect = null)
    {
        input.AdminSetup.Add((dialect, sql));
        return this;
    }

    /// <summary>管理者として流す後片付けの文（<see cref="AdminSetup"/> で作った物を消す。行が赤でも必ず流れる）</summary>
    public RealStage AdminTeardown(string sql, string? dialect = null)
    {
        input.AdminTeardown.Add((dialect, sql));
        return this;
    }

    /// <summary>同期の後に管理者の接続で問い合わせる（別のユーザー・スキーマの物を読むとき。面は <c>probe:{名前}</c>）</summary>
    public RealStage AdminQuery(string name, string sql, string? dialect = null)
    {
        input.AdminQueries.Add((name, dialect, sql));
        return this;
    }

    /// <summary>後片付けの文（リセットが消さないビュー・別スキーマのものなど）</summary>
    public RealStage Teardown(string sql, string? dialect = null)
    {
        input.Teardown.Add((dialect, sql));
        return this;
    }

    /// <summary>目標の図を取り込んだ図と同じ Guid のまま写す（同じ Guid であること自体を確かめる行だけ。理由は必須）</summary>
    public RealStage PreserveIds(string reason)
    {
        input.PreserveIdsReason = reason;
        return this;
    }

    /// <summary>
    /// 計画が警告しない失敗をわざと起こす（理由は必須で、種類が読める文にする＝「データの都合：…」か「モデルの外の物：…」）。
    /// 種類は、計画が予言できないデータの都合の失敗（NULL を含む列の NOT NULL 化・親の無い行への外部キー）と、計画が見ないモデルの外の
    /// DB の物による失敗（実行器が後から検出して巻き戻す SQLite のビューの破損）の 2 つだけ。表明 1（失敗なら警告がある）だけを外し、1b は当てる。期待には
    /// 失敗の形（<c>Outcome</c>）と DB が変わらないこと（<c>Unchanged</c>）を書く
    /// </summary>
    public RealStage ExpectsUnwarnedFailure(string reason)
    {
        input.UnwarnedFailureReason = reason;
        return this;
    }

    /// <summary>カタログの探針を流す（同期の前と後。面は <c>probe:{名前}</c> と <c>probe-before:{名前}</c>）</summary>
    public RealStage Probe(params string[] names)
    {
        input.Probes.AddRange(names);
        return this;
    }

    /// <summary>同期の後にデータを問い合わせる（面は <c>probe:{名前}</c>。失敗したら <c>error {型}: {1 行目}</c> の行）</summary>
    public RealStage Query(string name, string sql, string? dialect = null)
    {
        input.Queries.Add((name, dialect, sql));
        return this;
    }

    /// <summary>実 DB の段の期待を書く</summary>
    public RealStage Expect(Action<ExpectationBuilder> expect)
    {
        expect(input.Expect);
        return this;
    }
}

/// <summary>
/// 期待の行 1 つを総当たりのランナー（<see cref="SyncPlanFuzzRunner.RunCaseAsync"/>）へ渡す形にしたもの
/// </summary>
/// <remarks>
/// DB 側の図は行の <c>Live</c>（QuickER の DDL）か <see cref="RealStageInput.LiveSql"/>（生の SQL）で作る。目標の図は、行の
/// <c>Target</c>（独立に組む）か <c>TargetFromLive</c>（ランナーが渡す取り込んだ図の複製へ同じ編集を当てる）で作る。
/// </remarks>
internal sealed class ExpectationRealCase(
    SyncPlanExpectationInput input,
    SyncPlanFuzzDialect dialect
) : ISyncPlanFuzzCase
{
    private readonly RealStageInput _real = input.Real!;
    private SchemaImportResult? _before;

    public List<string> Mutations { get; } = [];

    /// <summary>選び方を当てる前の差分の行（網の差分の面と同じく既定の選択のまま）</summary>
    public IReadOnlyList<string> DiffLines { get; private set; } = [];

    /// <summary>同期の前の探針の行（探針の名前ごと）</summary>
    public Dictionary<string, IReadOnlyList<string>> ProbesBefore { get; } =
        new(StringComparer.Ordinal);

    public FuzzLiveSchema BuildLive()
    {
        var live = new FuzzLiveSchema();
        var world = NewWorld();

        if (input.LiveIsModel)
        {
            input.Live(world);
        }

        live.Diagram.Entities.AddRange(world.Live.Entities);
        live.Diagram.Relationships.AddRange(world.Live.Relationships);
        live.AdminStatements.AddRange(RealStageInput.For(_real.AdminSetup, dialect.Name));
        live.PrepareStatements.AddRange(RealStageInput.For(_real.Prepare, dialect.Name));
        live.InsertStatements.AddRange(RealStageInput.For(_real.LiveSql, dialect.Name));
        live.InsertStatements.AddRange(RealStageInput.For(_real.Setup, dialect.Name));
        live.RawStatements.AddRange(RealStageInput.For(_real.RawSetup, dialect.Name));
        return live;
    }

    public async Task OnImportedAsync(
        SyncPlanFuzzDialect dialect,
        SchemaImportResult before,
        CancellationToken ct
    )
    {
        _before = before;

        foreach (var name in _real.Probes)
        {
            ProbesBefore[name] = await SyncPlanCatalogProbes
                .RunAsync(dialect, name, ct)
                .ConfigureAwait(false);
        }
    }

    public void Mutate(List<Entity> entities, List<Relationship> relationships, FuzzLiveSchema live)
    {
        var world = NewWorld();
        world.TargetPreservesIds = _real.PreserveIdsReason is not null;

        if (world.TargetPreservesIds)
        {
            world.Live.Adopt(_before!.Entities, _before.Relationships);
        }
        else
        {
            world.Live.Adopt(entities, relationships);
        }

        input.Target!(world);
        entities.Clear();
        entities.AddRange(world.Target.Entities);
        relationships.Clear();
        relationships.AddRange(world.Target.Relationships);
    }

    public FuzzSelection Select(IReadOnlyList<SchemaDiffItem> items)
    {
        DiffLines = SyncPlanProjection.Diff(
            items,
            new Dictionary<SchemaDiffItem, int>(ReferenceEqualityComparer.Instance)
        );
        input.Select?.Invoke(items);
        return input.Select is null ? FuzzSelection.Default : FuzzSelection.ByKinds;
    }

    private ExpectationWorld NewWorld() => new(dialect.Name, dialect.Provider.SyncCapabilities);
}

/// <summary>期待の行の実 DB の段を流して照合する</summary>
internal static class SyncPlanRealRunner
{
    /// <summary>行を方言 1 つの実 DB で流し、問題の文を返す（汎用の表明 1〜5 の違反も問題）</summary>
    public static async Task<IReadOnlyList<string>> RunAsync(
        ScenarioRow<SyncPlanExpectationInput> row,
        SyncPlanFuzzDialect dialect,
        CancellationToken ct
    )
    {
        var real = row.Input.Real!;
        var problems = new List<string>();
        var testCase = new ExpectationRealCase(row.Input, dialect);
        var facets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        FuzzSeedOutcome outcome;

        try
        {
            outcome = await SyncPlanFuzzRunner
                .RunGuardedAsync(
                    dialect.Name,
                    0,
                    () =>
                        SyncPlanFuzzRunner.RunCaseAsync(
                            dialect,
                            testCase,
                            new FuzzSeedOutcome
                            {
                                Seed = 0,
                                Dialect = dialect.Name,
                                ScenarioId = row.Id,
                            },
                            ct
                        ),
                    row.Id
                )
                .ConfigureAwait(false);

            foreach (var name in real.Probes)
            {
                facets[ExpectationFacet.ProbePrefix + name] = await SyncPlanCatalogProbes
                    .RunAsync(dialect, name, ct)
                    .ConfigureAwait(false);
                facets[ExpectationFacet.ProbeBeforePrefix + name] =
                    testCase.ProbesBefore.TryGetValue(name, out var before) ? before : [];
            }

            foreach (
                var (name, _, sql) in real.Queries.Where(q =>
                    q.Dialect is null || q.Dialect == dialect.Name
                )
            )
            {
                facets[ExpectationFacet.ProbePrefix + name] = await SyncPlanCatalogProbes
                    .QueryAsync(dialect, sql, ct)
                    .ConfigureAwait(false);
            }

            foreach (
                var (name, _, sql) in real.AdminQueries.Where(q =>
                    q.Dialect is null || q.Dialect == dialect.Name
                )
            )
            {
                facets[ExpectationFacet.ProbePrefix + name] = await SyncPlanCatalogProbes
                    .QueryAsync(dialect, sql, ct, admin: true)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var sql in RealStageInput.For(real.Teardown, dialect.Name))
            {
                await dialect.ExecuteSetupAsync(sql, ct).ConfigureAwait(false);
            }

            foreach (var sql in RealStageInput.For(real.AdminTeardown, dialect.Name))
            {
                await dialect.ExecuteAdminAsync(sql, ct).ConfigureAwait(false);
            }
        }

        // 汎用の表明 6：行の後に、製品の取込・実行器が DB のファイルを掴んでいない（ファイルの DB の方言だけ。
        // 掴んだままだと、取込・同期の直後に利用者がファイルを移動・削除できない）
        if (dialect.HeldFileProblem() is { } held)
        {
            problems.Add($"[{dialect.Name}] generic assertion: {held}");
        }

        // 汎用の表明 1〜5 の違反はすべて行の問題（計画が警告しない失敗をわざと起こす行だけ、表明 1 を外す）
        foreach (var violation in outcome.Violations)
        {
            if (
                real.UnwarnedFailureReason is not null
                && violation.StartsWith(FailedWithoutWarningPrefix, StringComparison.Ordinal)
            )
            {
                continue;
            }

            problems.Add($"[{dialect.Name}] generic assertion: {violation}");
        }

        facets[ExpectationFacet.Outcome] =
        [
            outcome.Committed ? "committed" : "failed: " + FirstLine(outcome.Error),
        ];
        facets[ExpectationFacet.Error] = outcome.Committed
            ? []
            :
            [
                .. (outcome.Error ?? string.Empty)
                    .ReplaceLineEndings("\n")
                    .Split('\n')
                    .Select(l => l.TrimEnd())
                    .Where(l => l.Length > 0),
            ];
        facets[ExpectationFacet.Script] =
        [
            .. outcome
                .Script.ReplaceLineEndings("\n")
                .Split('\n')
                .Select(l => l.TrimEnd())
                .Where(l => l.Length > 0),
        ];
        facets[ExpectationFacet.Diff] = testCase.DiffLines;

        if (outcome.Plan is { } plan)
        {
            var labels = new Dictionary<SchemaDiffItem, int>(ReferenceEqualityComparer.Instance);

            foreach (
                var (facet, lines) in SyncPlanProjection.Plan(
                    plan,
                    labels,
                    outcome.Before?.Entities ?? []
                )
            )
            {
                facets[facet] = lines;
            }
        }

        facets[ExpectationFacet.Before] = outcome.Before is { } b
            ? SyncPlanRealProjection.Schema(b)
            : [];
        facets[ExpectationFacet.After] = outcome.After is { } a
            ? SyncPlanRealProjection.Schema(a)
            : [];
        facets[ExpectationFacet.AfterDiff] = outcome.AfterDiffItems is { } afterDiff
            ? SyncPlanProjection.Diff(
                afterDiff,
                new Dictionary<SchemaDiffItem, int>(ReferenceEqualityComparer.Instance)
            )
            : [];

        foreach (
            var expectation in real.Expect.Expectations.Where(e =>
                e.Capability is null || e.Capability == dialect.Name
            )
        )
        {
            var lines = facets.TryGetValue(expectation.Facet, out var found) ? found : [];
            var problem =
                expectation.Match == ExpectationMatch.SameAs
                    ? expectation.CheckSame(
                        lines,
                        facets.TryGetValue(expectation.Patterns[0], out var other) ? other : []
                    )
                    : expectation.Check(lines);

            if (problem is not null)
            {
                problems.Add($"[{dialect.Name}] {problem}");
            }
        }

        if (problems.Count > 0 && outcome.Error is { Length: > 0 } error)
        {
            problems.Add($"[{dialect.Name}] error: {FirstLine(error)}");
        }

        return [.. problems.Select(p => $"{row.Id}: {p}")];
    }

    /// <summary>表明 1（失敗したのに警告が無い）の違反の文の頭</summary>
    private const string FailedWithoutWarningPrefix = "failed without a warning:";

    private static string FirstLine(string? text) =>
        text?.ReplaceLineEndings("\n").Split('\n', 2)[0].Trim() ?? string.Empty;
}

/// <summary>取り込んだ図を、実 DB の段の面の行へ写す</summary>
internal static class SyncPlanRealProjection
{
    /// <summary>
    /// 取り込んだ図の行（表の名前の序数順に、表ごとに <c>table</c>・<c>col</c>（列の並び）・<c>pk</c>・<c>uq</c>（名前の序数順）・
    /// <c>desc</c> の行、続けて <c>fk</c>・<c>aux</c>・<c>import-warning</c> の行をそれぞれ序数順に）
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>table {表}</c></item>
    /// <item><c>col {表}.{列} {型} null|notnull[ pk][ computed][ identity]</c></item>
    /// <item><c>pk {表} ({列},…)</c>（実効順・主キーがあるときだけ）</item>
    /// <item><c>uq {表} {名前|-} ({列},…)</c></item>
    /// <item><c>desc {表}[.{列}] "{説明}"</c>（説明があるときだけ）</item>
    /// <item><c>fk {子} {名前|-} ({子の列},…) -&gt; {親}({親の列},…) del={OnDelete} upd={OnUpdate}</c></item>
    /// <item><c>aux {表} {種類} {名前}</c></item>
    /// <item><c>import-warning {種類}({表})[: {対象} {補足}]</c></item>
    /// </list>
    /// </remarks>
    public static IReadOnlyList<string> Schema(SchemaImportResult import)
    {
        var lines = new List<string>();
        var byId = import.Entities.ToDictionary(e => e.Id);

        foreach (var entity in import.Entities.OrderBy(e => e.TableName, StringComparer.Ordinal))
        {
            var t = entity.TableName;
            lines.Add("table " + t);

            foreach (var column in entity.Columns)
            {
                lines.Add($"col {t}.{column.Name} {SyncPlanProjection.ColumnText(column)}");
            }

            var pk = entity.GetPrimaryKeyColumnsInOrder();

            if (pk.Count > 0)
            {
                lines.Add($"pk {t} ({string.Join(",", pk.Select(c => c.Name))})");
            }

            foreach (
                var unique in entity
                    .UniqueConstraints.Select(u =>
                        $"uq {t} {u.Name ?? "-"} ({string.Join(",", u.ColumnIds.Select(id => entity.Columns.FirstOrDefault(c => c.Id == id)?.Name ?? "?"))})"
                    )
                    .OrderBy(l => l, StringComparer.Ordinal)
            )
            {
                lines.Add(unique);
            }

            if (DescriptionText.IsPresent(entity.Description))
            {
                lines.Add($"desc {t} \"{entity.Description}\"");
            }

            foreach (
                var column in entity.Columns.Where(c => DescriptionText.IsPresent(c.Description))
            )
            {
                lines.Add($"desc {t}.{column.Name} \"{column.Description}\"");
            }
        }

        lines.AddRange(
            import
                .Relationships.Select(r =>
                {
                    var parent = byId[r.SourceEntityId];
                    var child = byId[r.TargetEntityId];
                    string Name(Entity e, Guid id) =>
                        e.Columns.FirstOrDefault(c => c.Id == id)?.Name ?? "?";
                    return $"fk {child.TableName} {r.ConstraintName ?? "-"} "
                        + $"({string.Join(",", r.ColumnPairs.Select(p => Name(child, p.TargetColumnId)))}) -> "
                        + $"{parent.TableName}({string.Join(",", r.ColumnPairs.Select(p => Name(parent, p.SourceColumnId)))}) "
                        + $"del={r.OnDelete} upd={r.OnUpdate}";
                })
                .OrderBy(l => l, StringComparer.Ordinal)
        );
        lines.AddRange(
            import
                .AuxiliaryObjects.Select(a => $"aux {a.TableName} {a.Kind} {a.Name}")
                .OrderBy(l => l, StringComparer.Ordinal)
        );
        lines.AddRange(
            import
                .Warnings.Select(w =>
                    $"import-warning {w.Kind}({w.TableName})"
                    + (
                        w.Subject.Length > 0 || w.Detail.Length > 0
                            ? $": {w.Subject} {w.Detail}".TrimEnd()
                            : ""
                    )
                )
                .OrderBy(l => l, StringComparer.Ordinal)
        );

        return lines;
    }
}
