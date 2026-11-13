using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using Xunit;
using GF = QuickER.Tests.GeneratedFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 実装先どうしの一致テストの SQL Server の系統：行バージョンを持つ表（<c>ConcurrencyFixture</c>）で、同じ操作を
/// QuickER 版（SQL Server）と EF Core（SQL Server）へ流し、戻り値と保存先がそろうことを表明する（Docker 依存）。
/// </summary>
/// <remarks>
/// <para>
/// SQLite の系統（<see cref="BackendParityRuntimeTests"/>）では、行バージョンを採番しないため並行性トークンを使う経路
/// （ForceOverwrite のやり直し）に届かず、自動セーブポイントを切る MARS も無い。ここはその 2 つが要るケースだけを持つ。
/// </para>
/// <para>
/// 今わかっている違いは <see cref="Known"/> が名指しし、厳格に扱う（載っているケースが一致したら失敗＝直したら外す）。
/// </para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
[RuntimeCell(RuntimeFeatures.BackendParity, RuntimeTarget.AdoSqlServer)]
[RuntimeCell(RuntimeFeatures.BackendParity, RuntimeTarget.EfCoreSqlServer)]
[RuntimeCell(
    RuntimeFeatures.BackendParity,
    RuntimeTarget.Remote,
    Server = RuntimeTarget.EfCoreSqlServer
)]
[RuntimeCell(RuntimeFeatures.SaveHook, RuntimeTarget.AdoSqlServer)]
[RuntimeCell(RuntimeFeatures.SaveHook, RuntimeTarget.EfCoreSqlServer)]
[RuntimeCell(RuntimeFeatures.GraphSave, RuntimeTarget.EfCoreSqlServer)]
[RuntimeCell(RuntimeFeatures.GraphSave, RuntimeTarget.AdoSqlServer)]
public sealed partial class BackendParitySqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>今わかっている違い（範囲＝ケースの名前・<see cref="KnownMatch.KeyOnly"/>＝一致しないことだけを見る）</summary>
    /// <remarks>
    /// #33・#34・F7 は区切り 25 で直した（やり直しの上限は行ごと・分類の後、フックありの経路は試行ごとに
    /// トランザクションを張り直す）。
    /// </remarks>
    private static readonly IReadOnlyList<KnownEntry> Known =
    [
        .. new[]
        {
            "alternate-key-new-takes-released-taker-first",
            "alternate-key-gone-insert-takes-released-taker-first",
        }.SelectMany(scope =>
            new[] { "differs", "remote-efcore-differs" }.Select(signature => new KnownEntry(
                "G3-B",
                KnownKind.Accepted,
                null,
                "挿入する行が、同じ保存で後ろに渡した更新する行の手放す代替キーを取る保存は、QuickER 版がグラフの順に書いて挿入が断られ、EF Core 版が代替キーを先に書いて通す（docs のグラフ保存の書く順と EF Core の代替キーの節。値は ExpectedLines と ExpectedEfCoreLines が固定する）",
                scope,
                KnownMatch.ExactSignature,
                signature
            ))
        ),
        .. new[] { "differs", "remote-efcore-differs" }.Select(signature => new KnownEntry(
            "H2-D1",
            KnownKind.Accepted,
            null,
            "code を手放す行を参照する行を同じ保存で別の行へ付け替える保存は、QuickER 版が付け替え先を先に渡されると付け替えを先に書いて通り、EF Core 版が代替キーを保存のほかの文より先に書いて外部キーに断られる（docs のグラフ保存の書く順と EF Core の代替キーの節。値は ExpectedLines と ExpectedEfCoreLines が固定する）",
            "alternate-key-part-moved-off-released-taker-first",
            KnownMatch.ExactSignature,
            signature
        )),
        .. new[] { "differs", "remote-efcore-differs" }.Select(signature => new KnownEntry(
            "H2-D2",
            KnownKind.Accepted,
            null,
            "どの実装先でも断られる順を含む保存に他者が消した行があると、QuickER 版はグラフの順で前にあるその行の競合を、EF Core 版は先に書く代替キーの断りを報告する（docs のグラフ保存の書く順と EF Core の代替キーの節。値は ExpectedLines と ExpectedEfCoreLines が固定する）",
            "alternate-key-gone-part-then-taker-first",
            KnownMatch.ExactSignature,
            signature
        )),
        new(
            "KF-binary-padding",
            KnownKind.Accepted,
            null,
            "SQL Server は binary(n) の列へ短い値を 0x00 で詰めて保存し、詰めた値で読み戻す（短いキーでも詰めたキーでも同じ行が見つかる）。インメモリは書いた値のまま持つ（docs のインメモリの節「実 DB との既知の乖離」。値は ExpectedLines が固定する）",
            "key-shape-binary-short-key-full",
            KnownMatch.ExactSignature,
            "inmemory-differs"
        ),
    ];

    /// <summary>
    /// EF Core とリモート（EF Core のサーバー）の行の値を固定するケース（基準と違うことを受け入れた差のケースだけ。基準の値は
    /// <see cref="ExpectedLines"/>）
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedEfCoreLines =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // 新しい行の挿入より先に、後ろの行が手放す代替キーを書くので通る
            ["alternate-key-new-takes-released-taker-first"] =
            [
                "rows=2",
                "kits/1 code=K9 name=k1",
                "kits/2 code=K2 name=k2",
                "kits/100 code=K1 name=new100",
            ],
            // 消えた行の挿入より先に、後ろの行が手放す代替キーを書くので通る
            ["alternate-key-gone-insert-takes-released-taker-first"] =
            [
                "rows=2",
                "kits/1 code=K9 name=k1",
                "kits/2 code=K1 name=k2",
            ],
            // 部品の付け替えより先に K1 を手放す代替キーを書くので、まだ K1 を参照する部品の外部キーに断られる
            ["alternate-key-part-moved-off-released-taker-first"] =
            [
                "throws store-rejected",
                "kits/1 code=K1 name=k1",
                "kits/2 code=K2 name=k2",
                "kit_parts/10 kit_code=K1",
            ],
            // 部品の更新より先に、K3 を取る代替キーを書くので、一意制約の断りを報告する
            ["alternate-key-gone-part-then-taker-first"] =
            [
                "throws store-rejected",
                "kits/1 code=K1 name=k1",
                "kits/2 code=K2 name=k2",
                "kits/3 code=K3 name=k3",
            ],
        };

    /// <summary>今わかっている違いの一覧（照合の規則は <see cref="KnownFailureSet"/> が持つ）</summary>
    private static readonly KnownFailureSet KnownSet = new("BackendParity.sqlserver", Known);

    private readonly SqlServerContainerFixture _fixture = fixture;

    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>ケースの名前（xunit の MemberData）。代替キーの行と同じ保存のほかの行の組は軸から組み立てる</summary>
    public static TheoryData<string> Cases()
    {
        var cases = FixedCases();

        foreach (var companionCase in AlternateKeyCompanionCase.All)
        {
            cases.Add(companionCase.Name);
        }

        foreach (var ownWriteCase in OwnWriteCase.All)
        {
            cases.Add(ownWriteCase.Name);
        }

        foreach (var handoffCase in HandoffCase.All)
        {
            cases.Add(handoffCase.Name);
        }

        foreach (var heldBackFactoryCase in HeldBackFactoryCaseNames)
        {
            cases.Add(heldBackFactoryCase);
        }

        foreach (var keyShapeCase in SqlServerKeyShapeCase.All)
        {
            cases.Add(keyShapeCase.Name);
        }

        return cases;
    }

    /// <summary>決まった形のケースの名前</summary>
    private static TheoryData<string> FixedCases() =>
        new(
            "force-overwrite-9-rows",
            "force-overwrite-10-rows",
            "force-overwrite-10-rows-no-hooks",
            "mars-added-then-missing",
            "mars-deleted-then-missing",
            "no-mars-added-then-missing",
            "attempt-limit-then-row-gone-update",
            "attempt-limit-then-row-gone-graph-save",
            "move-stale-note-and-delete-old-gadget",
            "move-stale-note-and-delete-old-gadget-no-hooks",
            "alternate-key-referenced-update",
            "alternate-key-referenced-graph-save",
            "alternate-key-unreferenced-update",
            "alternate-key-unreferenced-graph-save",
            "alternate-key-stale-optimistic-update",
            "alternate-key-stale-optimistic-graph-save",
            "alternate-key-stale-force-overwrite-update",
            "alternate-key-stale-force-overwrite-graph-save",
            "alternate-key-taken-from-deleted-kit-with-parts",
            "alternate-key-taken-from-deleted-kit-stale-optimistic",
            "alternate-key-taken-from-deleted-kit-stale-force-overwrite",
            "include-child-delete-keeps-parent-and-siblings",
            "move-note-and-delete-old-gadget-kept-first",
            "hook-sql-stamps-same-row",
            "hook-sql-stamps-saved-parent",
            "hook-without-writes-no-reread",
            "hook-audit-row-no-reread",
            "hook-skip-parent-child-fk",
            "value-object-in-null-semantics",
            "value-object-in-all-null-elements",
            "any-without-condition"
        );

    /// <summary>
    /// 行の文字列の値そのものを固定するケース（ケースの名前 → QuickER 版の行。EF Core の行は QuickER 版と比べるので、一致すれば
    /// 両方がこの値になる）。基準どうしを比べるだけでは、両方が同じ向きに壊れても緑になるため、移したテストの絶対値をここに書く
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedLines = new Dictionary<
        string,
        string[]
    >(StringComparer.Ordinal)
    {
        // After が SQL で同じ行を書くと版を読み直さず、次の保存は競合（Modified）になり、フックが書いた値を上書きしない
        ["hook-sql-stamps-same-row"] =
        [
            "rows=1",
            "version-matches-store=False",
            "gadgets/1 name=stamped",
            "again throws SaveConflictException reason=Modified",
            "gadgets/1 name=stamped",
            "gadget_notes/10 gadget=1 note=first",
        ],
        // 子の After が同じ保存の親の行を SQL で書くと、親の版を読み直さず、親の次の保存は競合になる
        ["hook-sql-stamps-saved-parent"] =
        [
            "rows=2",
            "version-matches-store=False",
            "gadgets/1 name=stamped",
            "again throws SaveConflictException reason=Modified",
            "gadgets/1 name=stamped",
            "gadget_notes/10 gadget=1 note=second",
        ],
        // context で何も書かないフックの保存は、版を読み直す SELECT を出さず、手元の版は保存先と同じ
        ["hook-without-writes-no-reread"] =
        [
            "rows=1",
            "selects=0",
            "version-matches-store=True",
            "gadgets/1 name=first",
            "gadget_notes/10 gadget=1 note=first",
        ],
        // SQL で別の表へ監査の行を足すフックの保存も、SELECT を出さない（監査の行は残る）
        ["hook-audit-row-no-reread"] =
        [
            "rows=1",
            "selects=0",
            "version-matches-store=True",
            "gadgets/1 name=first",
            "gadget_notes/10 gadget=1 note=first",
            "gadget_notes/99 gadget=1 note=audit",
        ],
        // 部品ごと消す kit 1 の代替キーを kit 2 の更新が取る保存は、削除の後に代替キーを書いて成立し、手元の版は保存先と同じ
        ["alternate-key-taken-from-deleted-kit-with-parts"] =
        [
            "rows=4",
            "version-matches-store=True",
            "kits/2 code=K1 name=k2",
            "again rows=1",
            "kits/2 code=K1 name=again",
        ],
        // 値を取る kit 2 の版が古いと、2 つめの文脈の SaveChanges が 0 行で終わり、Optimistic は競合で全体が巻き戻る
        ["alternate-key-taken-from-deleted-kit-stale-optimistic"] =
        [
            "throws SaveConflictException reason=Modified",
            "version-matches-store=False",
            "kits/1 code=K1 name=k1",
            "kits/2 code=K2 name=other",
            "kit_parts/10 kit_code=K1",
            "kit_parts/11 kit_code=K1",
        ],
        // ForceOverwrite は保存先の値を採ってやり直し（削除も組み直す）、代替キーと名前を書く
        ["alternate-key-taken-from-deleted-kit-stale-force-overwrite"] =
        [
            "rows=4",
            "version-matches-store=True",
            "kits/2 code=K1 name=k2",
            "again rows=1",
            "kits/2 code=K1 name=again",
        ],
        // 代替キーを書いた後に、同じ保存の消えた行を insertWhenUpdateMissing で挿入へ切り替えてやり直しても、代替キーと
        // 名前が入り、消えた行が挿入され、手元の版は保存先と同じ（もう一度 Optimistic で保存しても競合にならない）
        ["alternate-key-plain-with-gone-insert-when-missing-optimistic"] =
            AlternateKeyGoneCompanionPlain,
        ["alternate-key-plain-with-gone-insert-when-missing-hooks-optimistic"] =
            AlternateKeyGoneCompanionPlain,
        // 同じく、後回しの 2 本目（消えた kit 3）が 2 つめの文脈の SaveChanges で当たらずにやり直す形
        ["alternate-key-taken-from-deleted-with-gone-insert-when-missing-optimistic"] =
            AlternateKeyGoneCompanionTakenFromDeleted,
        ["alternate-key-taken-from-deleted-with-gone-insert-when-missing-hooks-optimistic"] =
            AlternateKeyGoneCompanionTakenFromDeleted,
        // 2 行の代替キーの入れ替えは、DB が最初の更新で断って全体が巻き戻る（docs のグラフ保存の書く順）
        ["alternate-key-swap-optimistic"] = AlternateKeySwapRejected,
        ["alternate-key-swap-force-overwrite"] = AlternateKeySwapRejected,
        // 更新する行が手放す code を、その後に渡した新しい行が取る保存は通る
        ["alternate-key-new-takes-released-releaser-first"] =
        [
            "rows=2",
            "kits/1 code=K9 name=k1",
            "kits/2 code=K2 name=k2",
            "kits/100 code=K1 name=new100",
        ],
        // 新しい行を、code を手放す更新する行より前に渡すと、QuickER 版はグラフの順に書くので挿入が一意制約に断られる
        // （EF Core の値は ExpectedEfCoreLines。受け入れた差）
        ["alternate-key-new-takes-released-taker-first"] =
        [
            "throws store-rejected",
            "kits/1 code=K1 name=k1",
            "kits/2 code=K2 name=k2",
        ],
        // 消えた行の挿入（insertWhenUpdateMissing）を前に渡しても同じ
        ["alternate-key-gone-insert-takes-released-taker-first"] =
        [
            "throws store-rejected",
            "kits/1 code=K1 name=k1",
        ],
        // 同じく、その後に渡した消えた行の挿入（insertWhenUpdateMissing）が取る保存も通る
        ["alternate-key-gone-insert-takes-released-releaser-first"] =
        [
            "rows=2",
            "kits/1 code=K9 name=k1",
            "kits/2 code=K1 name=k2",
        ],
        // K1 を手放す kit 1 を先に渡すと、まだ K1 を参照する部品 10 の外部キーにどの実装先でも断られる
        ["alternate-key-part-moved-off-released-releaser-first"] =
        [
            "throws store-rejected",
            "kits/1 code=K1 name=k1",
            "kits/2 code=K2 name=k2",
            "kit_parts/10 kit_code=K1",
        ],
        // 部品 10 を持たせる kit 2 を先に渡すと、QuickER 版はグラフの順に付け替えを先に書いて通る（EF Core の値は
        // ExpectedEfCoreLines。受け入れた差）
        ["alternate-key-part-moved-off-released-taker-first"] =
        [
            "rows=3",
            "kits/1 code=K9 name=k1",
            "kits/2 code=K2 name=k2",
            "kit_parts/10 kit_code=K2",
        ],
        // 前にある部品 10 の更新が、他者が消したので当たらず競合になる（後ろの kit 1 は K3 をまだ持つ kit 3 より先なので、
        // 競合を解いても一意制約に断られる。EF Core の値は ExpectedEfCoreLines。受け入れた差）
        ["alternate-key-gone-part-then-taker-first"] =
        [
            "throws SaveConflictException reason=NotFound type=KitPartEntity key=10",
            "kits/1 code=K1 name=k1",
            "kits/2 code=K2 name=k2",
            "kits/3 code=K3 name=k3",
        ],
        // 書く列を持たない表で、消えた 11 行を insertWhenUpdateMissing で保存すると、EF Core 版も 1 試行 1 行ずつ挿入へ切り替えて
        // やり直しの上限（行ごとに数える）に掛からず、全行が挿入される
        ["own-write-alternate-key-and-version-gone-graph-save-insert-when-missing-11-rows"] =
            ManyGoneRowsSaved("True", id => $"tags/{id} code={(id == 1 ? "T9" : $"T9-{id}")}"),
        ["own-write-key-and-version-gone-graph-save-insert-when-missing-11-rows"] =
            ManyGoneRowsSaved("True", id => $"stamps/{id}"),
        ["own-write-key-only-gone-graph-save-insert-when-missing-11-rows"] = ManyGoneRowsSaved(
            "n/a",
            id => $"markers/{id}"
        ),
        // 新しい親の挿入を Before が飛ばし、新しい子を保存すると、外部キーに断られて全体が巻き戻る
        ["hook-skip-parent-child-fk"] =
        [
            "throws store-rejected",
            "gadgets/1 name=alpha",
            "gadget_notes/10 gadget=1 note=first",
        ],
        // 値オブジェクトの列（NULL 許容の kit_code）の IN / NOT IN は C# の意味論にそろう：null の要素は IS NULL へ畳み、
        // NOT IN は列が NULL の行を残し（null の要素が無いとき）、null の要素があれば落とす
        ["value-object-in-null-semantics"] =
        [
            "in [K1,K2] ids=[10,11]",
            "not-in [K1,K2] ids=[12]",
            "in [K1,null] ids=[10,12]",
            "not-in [K1,null] ids=[11]",
        ],
        // 全要素が null のリストの IN / NOT IN は、NULL の行とそれ以外を二分する
        ["value-object-in-all-null-elements"] = ["in [null] ids=[12]", "not-in [null] ids=[10]"],
        // 条件なしの Any は、空の表で false・行を入れた後で true（SQL Server の EXISTS の組み立て）
        ["any-without-condition"] = ["empty any=False", "seeded any=True"],
    };

    [Theory(
        DisplayName = "[Parity/SqlServer] ケースの結果は、既知の違いを除いて QuickER 版と EF Core でそろう"
    )]
    [MemberData(nameof(Cases))]
    public async Task Case_MatchesBaselineExceptKnownDifferences(string name)
    {
        var baseline = await RunAsync(name, efCore: false);
        var efCore = await RunAsync(name, efCore: true);

        // 代替キーの行と同じ保存のほかの行の組は、サーバーが EF Core 版 Repository のリモートでも流す
        var remoteEfCore =
            AlternateKeyCompanionCase.Find(name) is { } companionCase
                ? await RunOnRemoteEfCoreAsync(companionCase)
            : OwnWriteCase.Find(name) is { } ownWriteCase
                ? await OwnWriteAsync(efCore: true, ownWriteCase, remoteEfCore: true)
            : HandoffCase.Find(name) is { } handoffCase
                ? await HandoffAsync(efCore: true, handoffCase, remoteEfCore: true)
            : SqlServerKeyShapeCase.Find(name)
                is { Surface: not SqlServerKeyShapeSurface.Full } keyShapeCase
                ? await KeyShapeAsync(keyShapeCase, KeyShapeHost.RemoteEfCore)
            : null;

        // キーと表名の形のケースは、インメモリ（同じ図の生成物）でも流す
        var inMemory = SqlServerKeyShapeCase.Find(name) is { } inMemoryCase
            ? await KeyShapeAsync(inMemoryCase, KeyShapeHost.InMemory)
            : null;
        var report =
            $"QuickER:\n  {string.Join("\n  ", baseline)}\nEF Core:\n  {string.Join("\n  ", efCore)}"
            + (
                remoteEfCore is null
                    ? ""
                    : $"\nRemote (EF Core server):\n  {string.Join("\n  ", remoteEfCore)}"
            )
            + (inMemory is null ? "" : $"\nInMemory:\n  {string.Join("\n  ", inMemory)}");

        // 値を固定したケースは、基準の行がその値になる（EF Core は下で基準と比べる）
        if (ExpectedLines.TryGetValue(name, out var expected))
        {
            baseline.Should().Equal(expected, report);
        }

        // キーと表名の形のケースも、基準の行の値を固定する（形ごとのキーの書き方から組み立てる）
        if (SqlServerKeyShapeCase.Find(name) is { } expectedKeyShape)
        {
            baseline.Should().Equal(KeyShapeExpectedLines(expectedKeyShape), report);
        }

        // 受け入れた差のケースは、EF Core とリモート（EF Core のサーバー）の値も固定する
        if (ExpectedEfCoreLines.TryGetValue(name, out var expectedEfCore))
        {
            efCore.Should().Equal(expectedEfCore, report);
            remoteEfCore?.Should().Equal(expectedEfCore, report);
        }

        // 一致しなければ実装先ごとに失敗 1 件（載っていれば既知・載っているのに一致すれば「起きなかった」で赤）
        List<EngineFailure> observed = [];

        if (!efCore.SequenceEqual(baseline, StringComparer.Ordinal))
        {
            observed.Add(new EngineFailure(name, "differs", report));
        }

        if (
            remoteEfCore is not null
            && !remoteEfCore.SequenceEqual(baseline, StringComparer.Ordinal)
        )
        {
            observed.Add(new EngineFailure(name, "remote-efcore-differs", report));
        }

        if (inMemory is not null && !inMemory.SequenceEqual(baseline, StringComparer.Ordinal))
        {
            observed.Add(new EngineFailure(name, "inmemory-differs", report));
        }

        KnownSet
            .Classify(observed, scope => scope == name)
            .AssertClean($"[Parity/SqlServer] ケース {name}");
    }

    [Fact(
        DisplayName = "[Parity/SqlServer] 既知の違いと値を固定する行は、存在するケースを名指しする"
    )]
    public void Known_NamesExistingCases()
    {
        var cases = Cases().Select(row => (string)row.Data).ToHashSet();
        KnownSet.Entries.Select(entry => entry.Scope).Should().BeSubsetOf(cases);
        ExpectedLines.Keys.Should().BeSubsetOf(cases);
        ExpectedEfCoreLines.Keys.Should().BeSubsetOf(ExpectedLines.Keys);

        // 代替キーの行の形 × 同じ保存のほかの行 × 並行性の扱いの組は、どれもケースとして流れる
        CoverageGuard.ContainsAll(
            "[Parity/SqlServer] 代替キーの行と同じ保存のほかの行の組",
            cases,
            AlternateKeyCompanionCase.All.Select(companionCase => companionCase.Name)
        );

        // 書く列を持たない表の形 × 他者の変更 × 保存の経路の組も、どれもケースとして流れる
        CoverageGuard.ContainsAll(
            "[Parity/SqlServer] 書く列を持たない表の更新の組",
            cases,
            OwnWriteCase.All.Select(ownWriteCase => ownWriteCase.Name)
        );

        // 代替キーの手放しと取りの形 × 渡す順の組も、どれもケースとして流れる
        CoverageGuard.ContainsAll(
            "[Parity/SqlServer] 代替キーの手放しと取りの組",
            cases,
            HandoffCase.All.Select(handoffCase => handoffCase.Name)
        );

        // DbContext のファクトリの作り方の各値も、どれもケースとして流れる
        CoverageGuard.ContainsAll(
            "[Parity/SqlServer] 2 つめの文脈とファクトリの作り方",
            cases,
            HeldBackFactoryCaseNames
        );

        // キーと表名の形 × 面の組も、どれもケースとして流れる
        CoverageGuard.ContainsAll(
            "[Parity/SqlServer] キーと表名の形と面の組",
            cases,
            SqlServerKeyShapeCase.All.Select(keyShapeCase => keyShapeCase.Name)
        );
    }

    /// <summary>
    /// EF Core のカスケードの一括削除は、ナビゲーションの条件に合う根のキーを読んでから消す。根が SQL Server の 1 文あたりの
    /// パラメータの上限（2,100）より多くても、DB の互換性レベルが 130 未満（OPENJSON が使えない）でも動く。
    /// </summary>
    /// <remarks>
    /// キーの集合を 1 つの条件に入れると、EF Core 10 は既定でキーを 1 つずつのパラメータへ展開する（上限を超えると失敗する）。
    /// 版や設定によっては 1 つのコレクションのパラメータ（OPENJSON）で送り、互換性レベル 130 未満の DB で失敗する。どちらにも
    /// 頼らないよう、キーは 500 件ずつのかたまりに分ける。互換性レベルを変えるため、専用の DB を作って流し、終わったら消す。
    /// </remarks>
    [Fact(
        DisplayName = "[Parity/SqlServer] EF Core のナビゲーションの条件のカスケード一括削除は、根がパラメータの上限より多くても、互換性レベル 120 の DB でも動く"
    )]
    public async Task EfCoreNavigationPredicateCascadeDelete_WorksOnCompatibilityLevel120()
    {
        const string database = "quicker_parity_compat120";
        await _fixture.ExecuteAsync(
            $"IF DB_ID(N'{database}') IS NOT NULL DROP DATABASE [{database}]; CREATE DATABASE [{database}]; ALTER DATABASE [{database}] SET COMPATIBILITY_LEVEL = 120;",
            Ct
        );

        try
        {
            var connectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
                _fixture.ConnectionString
            )
            {
                InitialCatalog = database,
            }.ConnectionString;
            await using (
                var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString)
            )
            {
                await connection.OpenAsync(Ct);
                await using var command = connection.CreateCommand();
                command.CommandText = new QuickER.Provider.SqlServer.SqlServerDdlGenerator().Build(
                    ConcurrencyFixtureDefinition.Build()
                );
                await command.ExecuteNonQueryAsync(Ct);
            }

            // ConcurrencyFixture（gadgets → gadget_notes）の EF Core を使う。ほかのテストと同じオプションの組み合わせなので、
            // EF Core の内部サービスプロバイダを新しく作らない（フィクスチャごとに値オブジェクトの翻訳の拡張の型が違い、
            // 新しいフィクスチャ × プロバイダの組み合わせは内部プロバイダを 1 つずつ足す）
            await using var provider = new ServiceCollection()
                .AddGeneratedEfCoreRepositories(builder =>
                    builder.UseSqlServer(connectionString).IgnoreServiceProviderCacheLimit()
                )
                .BuildServiceProvider();
            var gadgets = provider.GetRequiredService<IGadgetRepository>();
            var notes = provider.GetRequiredService<IGadgetNoteRepository>();
            var ids = Enumerable.Range(1, 5000).ToList();
            await gadgets.BulkInsertAsync(ids.Select(id => NewGadget(id, $"g{id}")).ToList(), Ct);
            await notes.BulkInsertAsync(
                ids.Where(id => id % 2 == 0)
                    .Select(id => new GadgetNoteEntity
                    {
                        NoteId = NoteIdValue.Create(id),
                        GadgetId = GadgetIdValue.Create(id),
                        Note = NoteValue.Create("x"),
                    })
                    .ToList(),
                Ct
            );

            var rows = await gadgets
                .Query()
                .Where(g => g.GadgetNotes.Any())
                .ExecuteDeleteAsync(cascadeDelete: true, Ct);

            rows.Should().Be(5000, "根 2,500 件と子 2,500 件");
            (await gadgets.GetAllAsync(Ct)).Should().HaveCount(2500);
            (await notes.GetAllAsync(Ct)).Should().BeEmpty();
        }
        finally
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            await _fixture.ExecuteAsync(
                $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END",
                CancellationToken.None
            );
        }
    }

    /// <summary>
    /// グラフ保存が削除した単一のカスケードの子（1 対 1）は、保存の後もナビゲーションに残り、RowState は Removed のまま。
    /// QuickER 版と EF Core で同じ（決定 2026-10-14＝コレクションの子は外すが、単一の子は外すコレクションが無いので残す）。
    /// </summary>
    /// <remarks>
    /// EF Core は削除したエントリを外すときの fix-up で、親のナビゲーションを null にする。生成コードはそれを戻す。
    /// 1 対 1 のカスケードを持ち EF Core もある図は GeneratedFixture（SQL Server）だけなので、ここで流す。
    /// </remarks>
    [Theory(
        DisplayName = "[Parity/SqlServer] 1 対 1 の削除した単一の子は、保存の後もナビゲーションに残る（QuickER 版と EF Core）"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletedSingleChild_StaysOnItsNavigation(bool efCore)
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(GF.GeneratedFixtureDefinition.Build(), Ct);
        var services = new ServiceCollection();

        if (efCore)
        {
            GF.GeneratedEfCoreRepositoryServiceCollectionExtensions.AddGeneratedEfCoreRepositories(
                services,
                builder =>
                    builder
                        .UseSqlServer(_fixture.ConnectionString)
                        .IgnoreServiceProviderCacheLimit()
            );
        }
        else
        {
            GF.GeneratedSqlServerRepositoryServiceCollectionExtensions.AddGeneratedSqlServerRepositories(
                services,
                _fixture.ConnectionString
            );
        }

        await using var provider = services.BuildServiceProvider();
        var customers = provider.GetRequiredService<GF.ICustomerRepository>();
        await customers.InsertAsync(
            new GF.CustomerEntity
            {
                CustomerId = GF.CustomerIdValue.Create(1),
                Name = GF.NameValue.Create("a"),
                IsActive = GF.IsActiveValue.Create(true),
            },
            Ct
        );
        await provider
            .GetRequiredService<GF.ICustomerProfileRepository>()
            .InsertAsync(
                new GF.CustomerProfileEntity
                {
                    ProfileId = GF.ProfileIdValue.Create(1),
                    CustomerId = GF.CustomerIdValue.Create(1),
                },
                Ct
            );
        var customerId = GF.CustomerIdValue.Create(1);
        var customer = (
            await customers
                .Query()
                .Where(c => c.CustomerId == customerId)
                .Include(c => c.CustomerProfile)
                .FirstOrDefaultAsync(Ct)
        )!;
        var profile = customer.CustomerProfile!;
        profile.MarkRemoved();

        var rows = await customers.SaveAsync(customer, cancellationToken: Ct);

        rows.Should().Be(1);
        customer.CustomerProfile.Should().BeSameAs(profile);
        profile.RowState.Should().Be(GF.RowState.Removed);
        (
            await provider
                .GetRequiredService<GF.ICustomerProfileRepository>()
                .GetByIdAsync(GF.ProfileIdValue.Create(1), Ct)
        )
            .Should()
            .BeNull();
    }

    /// <summary>
    /// 区切り 43（外1）：SQL Server でも、Count・Any は Take・Skip を取得と同じ意味で当て（OFFSET / FETCH の副問い合わせ）、
    /// Take・Skip つきの ExecuteDelete は NotSupportedException で断って何も消さない（QuickER 版と EF Core）
    /// </summary>
    [Theory(
        DisplayName = "[Parity/SqlServer] Take・Skip つきの Count・Any は取得の件数とそろい、ExecuteDelete は断る（QuickER 版と EF Core）"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PagedCountAnyAndDelete_FollowFetch(bool efCore)
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);
        await using var provider = Build(efCore, withHooks: false);
        var gadgets = provider.GetRequiredService<IGadgetRepository>();

        foreach (var id in new[] { 1, 2, 3 })
        {
            await gadgets.InsertAsync(NewGadget(id, $"g{id}"), Ct);
        }

        SqlQuery<GadgetEntity> Paged(int? skip, int? take)
        {
            var query = gadgets.Query().OrderBy(g => g.GadgetId);

            if (skip is { } s)
            {
                query = query.Skip(s);
            }

            if (take is { } t)
            {
                query = query.Take(t);
            }

            return query;
        }

        (await Paged(null, 1).CountAsync(Ct)).Should().Be(1);
        (await Paged(1, null).CountAsync(Ct)).Should().Be(2);
        (await Paged(1, 1).CountAsync(Ct)).Should().Be(1);
        (await Paged(3, null).CountAsync(Ct)).Should().Be(0);
        (await Paged(2, null).AnyAsync(Ct)).Should().BeTrue();
        (await Paged(3, null).AnyAsync(Ct)).Should().BeFalse();

        var delete = () => Paged(null, 1).ExecuteDeleteAsync(cancellationToken: Ct);
        await delete.Should().ThrowAsync<NotSupportedException>();
        (await StateAsync()).Should().HaveCount(3);
    }

    /// <summary>1 つのケースを空のスキーマから流し、戻り値と保存先を並べて返す</summary>
    private async Task<IReadOnlyList<string>> RunAsync(string name, bool efCore)
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);

        if (SqlServerKeyShapeCase.Find(name) is { } keyShapeCase)
        {
            return await KeyShapeAsync(
                keyShapeCase,
                efCore ? KeyShapeHost.EfCore : KeyShapeHost.Ado
            );
        }

        return name switch
        {
            "force-overwrite-9-rows" => await ForceOverwriteManyAsync(efCore, 9, withHooks: true),
            "force-overwrite-10-rows" => await ForceOverwriteManyAsync(efCore, 10, withHooks: true),
            "force-overwrite-10-rows-no-hooks" => await ForceOverwriteManyAsync(
                efCore,
                10,
                withHooks: false
            ),
            "mars-added-then-missing" => await FirstThenMissingAsync(
                efCore,
                mars: true,
                removeFirst: false
            ),
            "mars-deleted-then-missing" => await FirstThenMissingAsync(
                efCore,
                mars: true,
                removeFirst: true
            ),
            "no-mars-added-then-missing" => await FirstThenMissingAsync(
                efCore,
                mars: false,
                removeFirst: false
            ),
            "attempt-limit-then-row-gone-update" => await AttemptLimitThenGoneAsync(
                efCore,
                graphSave: false
            ),
            "attempt-limit-then-row-gone-graph-save" => await AttemptLimitThenGoneAsync(
                efCore,
                graphSave: true
            ),
            "move-stale-note-and-delete-old-gadget" => await MoveStaleNoteAsync(
                efCore,
                withHooks: true
            ),
            "move-stale-note-and-delete-old-gadget-no-hooks" => await MoveStaleNoteAsync(
                efCore,
                withHooks: false
            ),
            "alternate-key-referenced-update" => await AlternateKeyAsync(
                efCore,
                graphSave: false,
                referenced: true,
                stale: false,
                ConcurrencyMode.Optimistic
            ),
            "alternate-key-referenced-graph-save" => await AlternateKeyAsync(
                efCore,
                graphSave: true,
                referenced: true,
                stale: false,
                ConcurrencyMode.Optimistic
            ),
            "alternate-key-unreferenced-update" => await AlternateKeyAsync(
                efCore,
                graphSave: false,
                referenced: false,
                stale: false,
                ConcurrencyMode.Optimistic
            ),
            "alternate-key-unreferenced-graph-save" => await AlternateKeyAsync(
                efCore,
                graphSave: true,
                referenced: false,
                stale: false,
                ConcurrencyMode.Optimistic
            ),
            "alternate-key-stale-optimistic-update" => await AlternateKeyAsync(
                efCore,
                graphSave: false,
                referenced: false,
                stale: true,
                ConcurrencyMode.Optimistic
            ),
            "alternate-key-stale-optimistic-graph-save" => await AlternateKeyAsync(
                efCore,
                graphSave: true,
                referenced: false,
                stale: true,
                ConcurrencyMode.Optimistic
            ),
            "alternate-key-stale-force-overwrite-update" => await AlternateKeyAsync(
                efCore,
                graphSave: false,
                referenced: false,
                stale: true,
                ConcurrencyMode.ForceOverwrite
            ),
            "alternate-key-stale-force-overwrite-graph-save" => await AlternateKeyAsync(
                efCore,
                graphSave: true,
                referenced: false,
                stale: true,
                ConcurrencyMode.ForceOverwrite
            ),
            "alternate-key-taken-from-deleted-kit-with-parts" =>
                await AlternateKeyTakenFromDeletedKitAsync(
                    efCore,
                    stale: false,
                    ConcurrencyMode.Optimistic
                ),
            "alternate-key-taken-from-deleted-kit-stale-optimistic" =>
                await AlternateKeyTakenFromDeletedKitAsync(
                    efCore,
                    stale: true,
                    ConcurrencyMode.Optimistic
                ),
            "alternate-key-taken-from-deleted-kit-stale-force-overwrite" =>
                await AlternateKeyTakenFromDeletedKitAsync(
                    efCore,
                    stale: true,
                    ConcurrencyMode.ForceOverwrite
                ),
            "include-child-delete-keeps-parent-and-siblings" => await IncludedNoteDeleteAsync(
                efCore
            ),
            "move-note-and-delete-old-gadget-kept-first" => await MoveNoteAsync(efCore),
            "hook-sql-stamps-same-row" => await HookStampsSameRowAsync(efCore),
            "hook-sql-stamps-saved-parent" => await HookStampsSavedParentAsync(efCore),
            "hook-without-writes-no-reread" => await HookNoRereadAsync(
                efCore,
                new NoWriteOnAfter()
            ),
            "hook-audit-row-no-reread" => await HookNoRereadAsync(efCore, new AuditNoteOnAfter()),
            "hook-skip-parent-child-fk" => await HookSkipParentAsync(efCore),
            "value-object-in-null-semantics" => await ValueObjectInAsync(
                efCore,
                [10, 11, 12],
                [
                    ["K1", "K2"],
                    ["K1", null],
                ]
            ),
            "value-object-in-all-null-elements" => await ValueObjectInAsync(
                efCore,
                [10, 12],
                [
                    [null],
                ]
            ),
            "any-without-condition" => await AnyWithoutConditionAsync(efCore),
            _ when AlternateKeyCompanionCase.Find(name) is { } companionCase =>
                await AlternateKeyWithCompanionAsync(efCore, companionCase),
            _ when FindHeldBackFactory(name) is { } heldBackFactory => await HeldBackTwiceAsync(
                efCore,
                heldBackFactory
            ),
            _ when OwnWriteCase.Find(name) is { } ownWriteCase => await OwnWriteAsync(
                efCore,
                ownWriteCase
            ),
            _ when HandoffCase.Find(name) is { } handoffCase => await HandoffAsync(
                efCore,
                handoffCase
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    /// <summary>
    /// 条件なしの Any（SQL Server の実 DB）：空の gadgets で <c>Query().AnyAsync()</c> を読み、1 行入れてからもう一度読む
    /// </summary>
    private async Task<IReadOnlyList<string>> AnyWithoutConditionAsync(bool efCore)
    {
        await using var provider = Build(efCore, withHooks: false);
        var gadgets = provider.GetRequiredService<IGadgetRepository>();
        var empty = await gadgets.Query().AnyAsync(Ct);
        await gadgets.InsertAsync(NewGadget(1, "g1"), Ct);
        var seeded = await gadgets.Query().AnyAsync(Ct);

        return [$"empty any={empty}", $"seeded any={seeded}"];
    }

    /// <summary>
    /// 値オブジェクトの列の IN / NOT IN（SQL Server の値オブジェクトの束縛と null の要素の畳み込み）：kits K1・K2 と、kit_code が
    /// 部品 10＝K1・11＝K2・12＝NULL のうち <paramref name="parts"/> の行を入れ、<paramref name="lists"/> の各リスト
    /// （<c>List&lt;CodeValue?&gt;</c>。null の要素はそのまま）で <c>codes.Contains(p.KitCode)</c> とその否定を読む
    /// </summary>
    private async Task<IReadOnlyList<string>> ValueObjectInAsync(
        bool efCore,
        IReadOnlyList<int> parts,
        IReadOnlyList<IReadOnlyList<string?>> lists
    )
    {
        await using var provider = Build(efCore, withHooks: false);
        var kits = provider.GetRequiredService<IKitRepository>();
        var kitParts = provider.GetRequiredService<IKitPartRepository>();
        await kits.InsertAsync(
            new KitEntity
            {
                KitId = KitIdValue.Create(1),
                Code = CodeValue.Create("K1"),
                Name = NameValue.Create("k1"),
            },
            Ct
        );
        await kits.InsertAsync(
            new KitEntity
            {
                KitId = KitIdValue.Create(2),
                Code = CodeValue.Create("K2"),
                Name = NameValue.Create("k2"),
            },
            Ct
        );
        var codeOf = new Dictionary<int, string?>
        {
            [10] = "K1",
            [11] = "K2",
            [12] = null,
        };

        foreach (var id in parts)
        {
            await kitParts.InsertAsync(
                new KitPartEntity
                {
                    PartId = PartIdValue.Create(id),
                    KitCode = codeOf[id] is { } code ? CodeValue.Create(code) : null,
                },
                Ct
            );
        }

        var lines = new List<string>();

        foreach (var list in lists)
        {
            var codes = list.Select(c => c is null ? null : CodeValue.Create(c)).ToList();
            var label = string.Join(",", list.Select(c => c ?? "null"));
            var inIds = await kitParts
                .Query()
                .Where(p => codes.Contains(p.KitCode))
                .ToListAsync(Ct);
            var notInIds = await kitParts
                .Query()
                .Where(p => !codes.Contains(p.KitCode))
                .ToListAsync(Ct);
            lines.Add($"in [{label}] ids=[{Ids(inIds)}]");
            lines.Add($"not-in [{label}] ids=[{Ids(notInIds)}]");
        }

        return lines;

        static string Ids(IEnumerable<KitPartEntity> rows) =>
            string.Join(",", rows.Select(p => p.PartId.Value).Order());
    }

    /// <summary>
    /// #33（ForceOverwrite の形）：<paramref name="count"/> 行を読み、他者がすべての版を進めた後で、名前を変えた行の列を
    /// ForceOverwrite のグラフ保存 1 回で保存する（QuickER 版は全行を後勝ちで書く）
    /// </summary>
    private async Task<IReadOnlyList<string>> ForceOverwriteManyAsync(
        bool efCore,
        int count,
        bool withHooks
    )
    {
        await using var provider = Build(efCore, withHooks: withHooks);
        var gadgets = provider.GetRequiredService<IGadgetRepository>();
        var ids = Enumerable.Range(1, count).ToList();

        foreach (var id in ids)
        {
            await gadgets.InsertAsync(NewGadget(id, $"g{id}"), Ct);
        }

        var loaded = new List<GadgetEntity>();

        foreach (var id in ids)
        {
            loaded.Add((await gadgets.GetByIdAsync(GadgetIdValue.Create(id), Ct))!);
        }

        await _fixture.ExecuteAsync("UPDATE gadgets SET name = name + N'!'", Ct);

        foreach (var gadget in loaded)
        {
            gadget.Name = NameValue.Create($"e{gadget.GadgetId.Value}");
            gadget.MarkUpdated();
        }

        var outcome = await OutcomeAsync(() =>
            gadgets.SaveAsync(loaded, mode: ConcurrencyMode.ForceOverwrite, cancellationToken: Ct)
        );

        return [outcome, .. await StateAsync()];
    }

    /// <summary>
    /// #34（MARS の形）：先に流れる行（Added の注文 20、または読み込んで削除の印を付けた 20）と、他者が消した行 21
    /// （読み込み後に消され、Updated）を、Save フックありで insertWhenUpdateMissing のグラフ保存 1 回で保存する。
    /// MARS のときは EF Core が自動セーブポイントを切る
    /// </summary>
    private async Task<IReadOnlyList<string>> FirstThenMissingAsync(
        bool efCore,
        bool mars,
        bool removeFirst
    )
    {
        await using var provider = Build(efCore, withHooks: true, mars: mars);
        var gadgets = provider.GetRequiredService<IGadgetRepository>();

        if (removeFirst)
        {
            await gadgets.InsertAsync(NewGadget(20, "first"), Ct);
        }

        await gadgets.InsertAsync(NewGadget(21, "second"), Ct);
        GadgetEntity first;

        if (removeFirst)
        {
            first = (await gadgets.GetByIdAsync(GadgetIdValue.Create(20), Ct))!;
            first.MarkRemoved();
        }
        else
        {
            first = NewGadget(20, "added");
            first.MarkAdded();
        }

        var second = (await gadgets.GetByIdAsync(GadgetIdValue.Create(21), Ct))!;
        await _fixture.ExecuteAsync("DELETE FROM gadgets WHERE gadget_id = 21", Ct);
        second.Name = NameValue.Create("back");
        second.MarkUpdated();

        var outcome = await OutcomeAsync(() =>
            gadgets.SaveAsync([first, second], insertWhenUpdateMissing: true, cancellationToken: Ct)
        );

        return [outcome, .. await StateAsync()];
    }

    /// <summary>
    /// F7：ForceOverwrite の更新が 9 回続けて当たらず、10 回目の前に行が消える。契約は「行なし」（UpdateAsync は false・
    /// グラフ保存は NotFound）。QuickER 版はやり直さないので、最初から行が消えている同じ呼び出しを基準にする
    /// </summary>
    private async Task<IReadOnlyList<string>> AttemptLimitThenGoneAsync(bool efCore, bool graphSave)
    {
        var interceptor = MissThenDelete.Instance;
        interceptor.Arm(_fixture, misses: 9);
        await using var provider = Build(
            efCore,
            withHooks: false,
            interceptor: efCore ? interceptor : null
        );
        var gadgets = provider.GetRequiredService<IGadgetRepository>();
        await gadgets.InsertAsync(NewGadget(1, "g1"), Ct);
        var gadget = (await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!;
        gadget.Name = NameValue.Create("edited");
        gadget.MarkUpdated();

        if (!efCore)
        {
            await _fixture.ExecuteAsync("DELETE FROM gadgets WHERE gadget_id = 1", Ct);
        }

        var outcome = graphSave
            ? await OutcomeAsync(() =>
                gadgets.SaveAsync(
                    gadget,
                    mode: ConcurrencyMode.ForceOverwrite,
                    cancellationToken: Ct
                )
            )
            : await OutcomeAsync(async () =>
                await gadgets.UpdateAsync(gadget, ConcurrencyMode.ForceOverwrite, Ct) ? 1 : 0
            );

        return [outcome, .. await StateAsync()];
    }

    /// <summary>
    /// 区切り 43（判断 D・E）：メモ 10 を gadget 1 から 2 へ付け替え、gadget 1 を消す（gadget → メモは CASCADE）。メモは読んだ後に
    /// 他者が更新したので版が古く、ForceOverwrite の保存は EF Core では 1 回当たらずにやり直す。やり直しの後も付け替えを
    /// 削除より先に書く（QuickER 版は版を照合せず後勝ちで書く）
    /// </summary>
    private async Task<IReadOnlyList<string>> MoveStaleNoteAsync(bool efCore, bool withHooks)
    {
        await using var provider = Build(efCore, withHooks: withHooks);
        var gadgets = provider.GetRequiredService<IGadgetRepository>();
        var notes = provider.GetRequiredService<IGadgetNoteRepository>();
        await gadgets.InsertAsync(NewGadget(1, "g1"), Ct);
        await gadgets.InsertAsync(NewGadget(2, "g2"), Ct);
        await notes.InsertAsync(
            new GadgetNoteEntity
            {
                NoteId = NoteIdValue.Create(10),
                GadgetId = GadgetIdValue.Create(1),
                Note = NoteValue.Create("n"),
            },
            Ct
        );

        var oldParent = (await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!;
        var newParent = (await gadgets.GetByIdAsync(GadgetIdValue.Create(2), Ct))!;
        var note = (await notes.GetByIdAsync(NoteIdValue.Create(10), Ct))!;
        await _fixture.ExecuteAsync(
            "UPDATE gadget_notes SET note = N'other' WHERE note_id = 10",
            Ct
        );

        note.GadgetId = GadgetIdValue.Create(2);
        note.Note = NoteValue.Create("moved");
        note.MarkUpdated();
        newParent.GadgetNotes.Add(note);
        oldParent.MarkRemoved();

        var outcome = await OutcomeAsync(() =>
            gadgets.SaveAsync(
                [oldParent, newParent],
                mode: ConcurrencyMode.ForceOverwrite,
                cancellationToken: Ct
            )
        );

        return [outcome, .. await StateAsync(), .. await NotesStateAsync()];
    }

    /// <summary>
    /// H1（代替キーの書き込み）：行バージョンを持つ kits の一意なコード（kit_parts.kit_code が参照する）を変えて、UpdateAsync か
    /// グラフ保存 1 回で保存する。<paramref name="referenced"/> なら kit_parts の行がそのコードを参照しており、DB が断る。
    /// <paramref name="stale"/> なら読んだ後に他者が名前を変えて版を進めている（Optimistic は競合・ForceOverwrite は書く）。
    /// 書いたら、手元の版が保存先の版と同じになり、同じインスタンスをもう一度 Optimistic で保存しても競合にならないことを見る
    /// </summary>
    private async Task<IReadOnlyList<string>> AlternateKeyAsync(
        bool efCore,
        bool graphSave,
        bool referenced,
        bool stale,
        ConcurrencyMode mode
    )
    {
        await using var provider = Build(efCore, withHooks: false);
        var kits = provider.GetRequiredService<IKitRepository>();
        await kits.InsertAsync(
            new KitEntity
            {
                KitId = KitIdValue.Create(1),
                Code = CodeValue.Create("K1"),
                Name = NameValue.Create("k1"),
            },
            Ct
        );

        if (referenced)
        {
            await provider
                .GetRequiredService<IKitPartRepository>()
                .InsertAsync(
                    new KitPartEntity
                    {
                        PartId = PartIdValue.Create(10),
                        KitCode = CodeValue.Create("K1"),
                    },
                    Ct
                );
        }

        var kit = (await kits.GetByIdAsync(KitIdValue.Create(1), Ct))!;

        if (stale)
        {
            await _fixture.ExecuteAsync("UPDATE kits SET name = N'other' WHERE kit_id = 1", Ct);
        }

        kit.Code = CodeValue.Create("K2");
        kit.MarkUpdated();

        Task<int> Save() =>
            graphSave
                ? kits.SaveAsync(kit, mode: mode, cancellationToken: Ct)
                : UpdateAsInt(kits.UpdateAsync(kit, mode, Ct));

        var outcome = await OutcomeAsync(Save);
        List<string> lines =
        [
            outcome,
            $"version-matches-store={await VersionMatchesStoreAsync(kit)}",
            .. await KitsStateAsync(),
        ];

        if (outcome == "rows=1")
        {
            kit.Name = NameValue.Create("again");
            kit.MarkUpdated();
            mode = ConcurrencyMode.Optimistic;
            lines.Add($"again {await OutcomeAsync(Save)}");
            lines.AddRange(await KitsStateAsync());
        }

        return lines;
    }

    /// <summary>
    /// 代替キーの書く順：kit_parts 2 行に参照される kit 1（K1）を部品ごと消し、同じグラフ保存 1 回で kit 2 の code を K1 にする
    /// （値を取る kit 2 を先に渡す）。どちらの実装先も削除の後に代替キーを書いて成立する（EF Core は後回しの kit 2 を 2 つめの
    /// 文脈で保存する）。<paramref name="stale"/> なら読んだ後に他者が kit 2 の名前を変えて版を進めている（2 つめの文脈の
    /// SaveChanges が 0 行で終わり、Optimistic は競合・ForceOverwrite は保存先の値を採ってやり直す）。書いたら、手元の版が
    /// 保存先と同じで、同じインスタンスをもう一度 Optimistic で保存しても競合にならないことを見る
    /// </summary>
    private async Task<IReadOnlyList<string>> AlternateKeyTakenFromDeletedKitAsync(
        bool efCore,
        bool stale,
        ConcurrencyMode mode
    )
    {
        await using var provider = Build(efCore, withHooks: false);
        var kits = provider.GetRequiredService<IKitRepository>();
        var parts = provider.GetRequiredService<IKitPartRepository>();
        await kits.InsertAsync(NewKit(1, "K1"), Ct);
        await kits.InsertAsync(NewKit(2, "K2"), Ct);
        await parts.InsertAsync(NewPart(10, "K1"), Ct);
        await parts.InsertAsync(NewPart(11, "K1"), Ct);

        var deleted = (await kits.GetByIdAsync(KitIdValue.Create(1), Ct))!;

        foreach (var id in new[] { 10, 11 })
        {
            deleted.KitParts.Add((await parts.GetByIdAsync(PartIdValue.Create(id), Ct))!);
        }

        deleted.MarkRemoved();
        var taker = (await kits.GetByIdAsync(KitIdValue.Create(2), Ct))!;

        if (stale)
        {
            await _fixture.ExecuteAsync("UPDATE kits SET name = N'other' WHERE kit_id = 2", Ct);
        }

        taker.Code = CodeValue.Create("K1");
        taker.MarkUpdated();

        var outcome = await OutcomeAsync(() =>
            kits.SaveAsync([taker, deleted], mode: mode, cancellationToken: Ct)
        );
        List<string> lines =
        [
            outcome,
            $"version-matches-store={await VersionMatchesStoreAsync(taker)}",
            .. await KitsStateAsync(),
        ];

        if (outcome.StartsWith("rows=", StringComparison.Ordinal))
        {
            taker.Name = NameValue.Create("again");
            taker.MarkUpdated();
            lines.Add(
                $"again {await OutcomeAsync(() => kits.SaveAsync(taker, mode: ConcurrencyMode.Optimistic, cancellationToken: Ct))}"
            );
            lines.AddRange(await KitsStateAsync());
        }

        return lines;

        static KitEntity NewKit(int id, string code) =>
            new()
            {
                KitId = KitIdValue.Create(id),
                Code = CodeValue.Create(code),
                Name = NameValue.Create($"k{id}"),
            };

        static KitPartEntity NewPart(int id, string code) =>
            new() { PartId = PartIdValue.Create(id), KitCode = CodeValue.Create(code) };
    }

    /// <summary>
    /// グラフ保存の追跡の形（GraphSave の EF Core on SQL Server から移した行）：gadget 1 をメモ 10・11 ごと Include で読み
    /// （EF Core はメモの親への参照を埋める）、メモ 10 だけを削除の印にして、メモのリポジトリから直接保存する。保存はメモ 10 の
    /// 1 行だけを消し、親の gadget と兄弟のメモ 11 を消さず、親の RowState も触らない（親への参照を辿って追跡すると、親と兄弟
    /// まで削除する）
    /// </summary>
    private async Task<IReadOnlyList<string>> IncludedNoteDeleteAsync(bool efCore)
    {
        await using var provider = Build(efCore, withHooks: false);
        var gadgets = provider.GetRequiredService<IGadgetRepository>();
        var notes = provider.GetRequiredService<IGadgetNoteRepository>();
        await gadgets.InsertAsync(NewGadget(1, "g1"), Ct);
        await notes.InsertAsync(NewNote(10, 1, "a"), Ct);
        await notes.InsertAsync(NewNote(11, 1, "b"), Ct);

        var gadget = (
            await gadgets
                .Query()
                .Include(g => g.GadgetNotes)
                .GetByIdAsync(GadgetIdValue.Create(1), Ct)
        )!;
        var note = gadget.GadgetNotes.Single(n => n.NoteId.Value == 10);
        note.MarkRemoved();

        var outcome = await OutcomeAsync(() => notes.SaveAsync(note, cancellationToken: Ct));

        return
        [
            outcome,
            $"parent={gadget.RowState} note={note.RowState}",
            .. await StateAsync(),
            .. await NotesStateAsync(),
        ];
    }

    /// <summary>
    /// 付け替えの形（GraphSave の EF Core on SQL Server から移した行）：メモ 10 を gadget 1 から 2 へ付け替え、gadget 1 を消す
    /// （gadget → メモは CASCADE）。版は新しく、Optimistic・フックなし。根の並びは付け替え先が先（版の古い ForceOverwrite の形は
    /// <c>move-stale-note-and-delete-old-gadget</c> が消す側を先に並べて持つ）。削除が先に走ると付け替えたメモまで消え、続く更新が
    /// 「行なし」になる
    /// </summary>
    private async Task<IReadOnlyList<string>> MoveNoteAsync(bool efCore)
    {
        await using var provider = Build(efCore, withHooks: false);
        var gadgets = provider.GetRequiredService<IGadgetRepository>();
        var notes = provider.GetRequiredService<IGadgetNoteRepository>();
        await gadgets.InsertAsync(NewGadget(1, "g1"), Ct);
        await gadgets.InsertAsync(NewGadget(2, "g2"), Ct);
        await notes.InsertAsync(NewNote(10, 1, "n"), Ct);

        var oldParent = (await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!;
        var newParent = (await gadgets.GetByIdAsync(GadgetIdValue.Create(2), Ct))!;
        var note = (await notes.GetByIdAsync(NoteIdValue.Create(10), Ct))!;
        note.GadgetId = GadgetIdValue.Create(2);
        note.MarkUpdated();
        newParent.GadgetNotes.Add(note);
        oldParent.MarkRemoved();

        var outcome = await OutcomeAsync(() =>
            gadgets.SaveAsync([newParent, oldParent], cancellationToken: Ct)
        );

        return [outcome, .. await StateAsync(), .. await NotesStateAsync()];
    }

    /// <summary>Save フックのケースの種（SaveHook の版の系統から移した形＝gadget 1 "alpha" とメモ 10 "first"）</summary>
    private Task SeedHookCaseAsync() =>
        _fixture.ExecuteAsync(
            "INSERT INTO gadgets (gadget_id, name) VALUES (1, N'alpha');"
                + "INSERT INTO gadget_notes (note_id, gadget_id, note) VALUES (10, 1, N'first');",
            Ct
        );

    /// <summary>
    /// Save フックの版（同じ行）：gadget の After が SQL で同じ行の名前を書き換える保存の後、手元の版は保存先と違い（SQL の書き込みでは
    /// 版を読み直さない）、同じインスタンスの次の保存は競合（Modified）になり、フックの書いた値を上書きしない
    /// </summary>
    private async Task<IReadOnlyList<string>> HookStampsSameRowAsync(bool efCore)
    {
        await SeedHookCaseAsync();
        await using var provider = Build(
            efCore,
            withHooks: false,
            hooks: [new StampGadgetOnAfter()]
        );
        var gadgets = provider.GetRequiredService<IGadgetRepository>();
        var gadget = (await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!;

        gadget.Name = NameValue.Create("first");
        gadget.MarkUpdated();
        var outcome = await OutcomeAsync(() => gadgets.SaveAsync(gadget, cancellationToken: Ct));
        List<string> lines =
        [
            outcome,
            $"version-matches-store={await GadgetVersionMatchesStoreAsync(gadget)}",
            .. await StateAsync(),
        ];

        gadget.Name = NameValue.Create("second");
        gadget.MarkUpdated();
        lines.Add(
            $"again {await OutcomeAsync(() => gadgets.SaveAsync(gadget, cancellationToken: Ct))}"
        );

        return [.. lines, .. await StateAsync(), .. await NotesStateAsync()];
    }

    /// <summary>
    /// Save フックの版（同じ保存の親）：gadget をメモごと Include で読み、両方を編集して保存する。メモの After が SQL で同じ保存の親の
    /// 名前を書き換えるので、親の手元の版は保存先と違い、親の次の保存（カスケードなし）は競合になる
    /// </summary>
    private async Task<IReadOnlyList<string>> HookStampsSavedParentAsync(bool efCore)
    {
        await SeedHookCaseAsync();
        await using var provider = Build(
            efCore,
            withHooks: false,
            hooks: [new StampParentOnAfter()]
        );
        var gadgets = provider.GetRequiredService<IGadgetRepository>();
        var key = GadgetIdValue.Create(1);
        var gadget = (
            await gadgets
                .Query()
                .Where(g => g.GadgetId == key)
                .Include(g => g.GadgetNotes)
                .ToListAsync(Ct)
        ).Single();
        var note = gadget.GadgetNotes.Single();

        gadget.Name = NameValue.Create("first");
        gadget.MarkUpdated();
        note.Note = NoteValue.Create("second");
        note.MarkUpdated();
        var outcome = await OutcomeAsync(() => gadgets.SaveAsync(gadget, cancellationToken: Ct));
        List<string> lines =
        [
            outcome,
            $"version-matches-store={await GadgetVersionMatchesStoreAsync(gadget)}",
            .. await StateAsync(),
        ];

        gadget.Name = NameValue.Create("third");
        gadget.MarkUpdated();
        lines.Add(
            $"again {await OutcomeAsync(() => gadgets.SaveAsync(gadget, cascadeSave: false, cancellationToken: Ct))}"
        );

        return [.. lines, .. await StateAsync(), .. await NotesStateAsync()];
    }

    /// <summary>
    /// Save フックの版（読み直さない）：After が context で無制限バイナリ列を書かない保存（何も書かない・SQL で別の表へ監査の行を足す）
    /// は、保存の間に版を読み直す SELECT を出さず（SqlClient のコマンドを数える）、手元の版は保存先と同じ
    /// </summary>
    private async Task<IReadOnlyList<string>> HookNoRereadAsync(bool efCore, object hook)
    {
        await SeedHookCaseAsync();
        await using var provider = Build(efCore, withHooks: false, hooks: [hook]);
        var gadgets = provider.GetRequiredService<IGadgetRepository>();
        var gadget = (await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!;
        string outcome;
        int selects;

        using (var capture = new SqlCommandObserver("gadget"))
        {
            gadget.Name = NameValue.Create("first");
            gadget.MarkUpdated();
            outcome = await OutcomeAsync(() => gadgets.SaveAsync(gadget, cancellationToken: Ct));
            selects = capture.Commands.Count(text =>
                text.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            );
        }

        return
        [
            outcome,
            $"selects={selects}",
            $"version-matches-store={await GadgetVersionMatchesStoreAsync(gadget)}",
            .. await StateAsync(),
            .. await NotesStateAsync(),
        ];
    }

    /// <summary>
    /// Save フックのスキップ（SaveHook の SQL Server の系統から移した形）：新しい gadget の挿入を Before が飛ばし、新しいメモを子に
    /// 持たせて保存すると、メモの挿入が外部キーに断られて全体が巻き戻る
    /// </summary>
    private async Task<IReadOnlyList<string>> HookSkipParentAsync(bool efCore)
    {
        await SeedHookCaseAsync();
        await using var provider = Build(efCore, withHooks: false, hooks: [new SkipGadgetInsert()]);
        var gadgets = provider.GetRequiredService<IGadgetRepository>();
        var parent = NewGadget(50, "orphan-parent");
        parent.MarkAdded();
        var child = NewNote(500, 50, "orphan-child");
        child.MarkAdded();
        parent.GadgetNotes.Add(child);

        var outcome = await OutcomeAsync(() => gadgets.SaveAsync(parent, cancellationToken: Ct));

        return [outcome, .. await StateAsync(), .. await NotesStateAsync()];
    }

    /// <summary>手元の gadget の版が保存先の版と同じか（行が無ければ False）</summary>
    private async Task<bool> GadgetVersionMatchesStoreAsync(GadgetEntity gadget)
    {
        await using var provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();
        var stored = await provider
            .GetRequiredService<IGadgetRepository>()
            .GetByIdAsync(gadget.GadgetId, Ct);

        return stored is not null && stored.RowVer.Value.SequenceEqual(gadget.RowVer.Value);
    }

    private static GadgetNoteEntity NewNote(int id, int gadget, string note) =>
        new()
        {
            NoteId = NoteIdValue.Create(id),
            GadgetId = GadgetIdValue.Create(gadget),
            Note = NoteValue.Create(note),
        };

    private static async Task<int> UpdateAsInt(Task<bool> update) => await update ? 1 : 0;

    /// <summary>手元の版が保存先の版と同じか（行が無ければ False）</summary>
    private async Task<bool> VersionMatchesStoreAsync(KitEntity kit)
    {
        await using var provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();
        var stored = await provider
            .GetRequiredService<IKitRepository>()
            .GetByIdAsync(kit.KitId, Ct);

        return stored is not null && stored.RowVer.Value.SequenceEqual(kit.RowVer.Value);
    }

    /// <summary>保存先の kits と kit_parts の全行（キー順・版は比べない）</summary>
    private async Task<IReadOnlyList<string>> KitsStateAsync()
    {
        await using var provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();
        var kits = await provider.GetRequiredService<IKitRepository>().GetAllAsync(Ct);
        var parts = await provider.GetRequiredService<IKitPartRepository>().GetAllAsync(Ct);

        return
        [
            .. kits.OrderBy(k => k.KitId.Value)
                .Select(k => $"kits/{k.KitId.Value} code={k.Code.Value} name={k.Name.Value}"),
            .. parts
                .OrderBy(p => p.PartId.Value)
                .Select(p => $"kit_parts/{p.PartId.Value} kit_code={p.KitCode?.Value}"),
        ];
    }

    /// <summary>保存先のメモの全行（キー順・版は比べない）</summary>
    private async Task<IReadOnlyList<string>> NotesStateAsync()
    {
        await using var provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();
        var all = await provider.GetRequiredService<IGadgetNoteRepository>().GetAllAsync(Ct);

        return
        [
            .. all.OrderBy(n => n.NoteId.Value)
                .Select(n =>
                    $"gadget_notes/{n.NoteId.Value} gadget={n.GadgetId.Value} note={n.Note.Value}"
                ),
        ];
    }

    /// <summary>実装先のリポジトリを組み立てる</summary>
    private ServiceProvider Build(
        bool efCore,
        bool withHooks,
        bool mars = false,
        IInterceptor? interceptor = null,
        IReadOnlyList<object>? hooks = null
    )
    {
        var connectionString = mars
            ? _fixture.ConnectionString + ";MultipleActiveResultSets=True"
            : _fixture.ConnectionString;
        var services = efCore
            ? new ServiceCollection().AddGeneratedEfCoreRepositories(builder =>
            {
                if (interceptor is not null)
                {
                    builder.UseSqlServerWith(connectionString, interceptor);
                }
                else
                {
                    builder.UseSqlServer(connectionString).IgnoreServiceProviderCacheLimit();
                }
            })
            : new ServiceCollection().AddGeneratedSqlServerRepositories(connectionString);

        if (withHooks)
        {
            services.AddSaveHook(new NoOpHook());
        }

        foreach (var hook in hooks ?? [])
        {
            services.AddSaveHook(hook);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>呼び出しの戻り値（または例外の型と競合の理由）</summary>
    private static async Task<string> OutcomeAsync(Func<Task<int>> call)
    {
        try
        {
            return $"rows={await call()}";
        }
        catch (SaveConflictException ex)
        {
            return $"throws SaveConflictException reason={ex.Reason}";
        }
        catch (Exception ex)
            when (ex
                    is DbException
                        or DbUpdateException
                        or RemoteRepositoryException { StatusCode: 500 }
            )
        {
            // DB が断った（QuickER 版は SqlException・EF Core は DbUpdateException か SqlException・リモートはサーバーの
            // 断りが 500 で届く＝SQLite の系統の ParityFormat と同じ扱い）
            return "throws store-rejected";
        }
        catch (Exception ex)
        {
            return $"throws {ex.GetType().Name}";
        }
    }

    /// <summary>保存先の全行（キー順・版は比べない）</summary>
    private async Task<IReadOnlyList<string>> StateAsync()
    {
        await using var provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();
        var all = await provider.GetRequiredService<IGadgetRepository>().GetAllAsync(Ct);

        return
        [
            .. all.OrderBy(g => g.GadgetId.Value)
                .Select(g => $"gadgets/{g.GadgetId.Value} name={g.Name.Value}"),
        ];
    }

    private static GadgetEntity NewGadget(int id, string name) =>
        new() { GadgetId = GadgetIdValue.Create(id), Name = NameValue.Create(name) };

    /// <summary>何もしない Save フック（EF Core の保存をフックありの経路へ回す）</summary>
    private sealed class NoOpHook : ISaveHook<GadgetEntity>, ISaveHook<GadgetNoteEntity>;

    /// <summary>gadget の After で、同じ行の名前を SQL で書き換える</summary>
    private sealed class StampGadgetOnAfter : ISaveHook<GadgetEntity>
    {
        public Task AfterSaveAsync(
            GadgetEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) =>
            context.ExecuteSqlAsync(
                "UPDATE gadgets SET name = N'stamped' WHERE gadget_id = @id",
                new { id = entity.GadgetId.Value },
                cancellationToken
            );
    }

    /// <summary>メモの After で、同じ保存の親の gadget の名前を SQL で書き換える</summary>
    private sealed class StampParentOnAfter : ISaveHook<GadgetNoteEntity>
    {
        public Task AfterSaveAsync(
            GadgetNoteEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) =>
            context.ExecuteSqlAsync(
                "UPDATE gadgets SET name = N'stamped' WHERE gadget_id = @id",
                new { id = entity.GadgetId.Value },
                cancellationToken
            );
    }

    /// <summary>gadget の After で、別の表へ監査の行を SQL で足す（保存した行には触れない）</summary>
    private sealed class AuditNoteOnAfter : ISaveHook<GadgetEntity>
    {
        public Task AfterSaveAsync(
            GadgetEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) =>
            context.ExecuteSqlAsync(
                "INSERT INTO gadget_notes (note_id, gadget_id, note) VALUES (99, @id, N'audit')",
                new { id = entity.GadgetId.Value },
                cancellationToken
            );
    }

    /// <summary>After を持つが context で何も書かない</summary>
    private sealed class NoWriteOnAfter : ISaveHook<GadgetEntity>
    {
        public Task AfterSaveAsync(
            GadgetEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }

    /// <summary>新しい gadget の挿入（Insert）だけを Before で飛ばす</summary>
    private sealed class SkipGadgetInsert : ISaveHook<GadgetEntity>
    {
        public Task<bool> BeforeSaveAsync(
            GadgetEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(operation != SaveOperation.Insert);
    }

    /// <summary>gadgets の UPDATE を最初の決めた回数だけ当たらなくし、その次の UPDATE の前に行を消す</summary>
    /// <remarks>
    /// インスタンスは 1 つを使い回し、使う前に <see cref="Arm"/> で状態を入れ直す。EF Core はインターセプターのインスタンスが
    /// 違うと内部のサービスプロバイダを別に作り、プロセス全体で 20 を超えると、たまたま 21 個目を作った DbContext が
    /// ManyServiceProvidersCreatedWarning で失敗する（ほかのテストを巻き込む）。このクラスのケースは SQL Server のコレクションで
    /// 1 つずつ流れるので、状態を共有しても重ならない。
    /// </remarks>
    private sealed class MissThenDelete : DbCommandInterceptor
    {
        public static readonly MissThenDelete Instance = new();

        private SqlServerContainerFixture? _fixture;
        private int _misses;
        private int _updates;

        /// <summary>行を消す先と、当たらなくする回数を入れ、数え直す</summary>
        public void Arm(SqlServerContainerFixture fixture, int misses)
        {
            _fixture = fixture;
            _misses = misses;
            _updates = 0;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (command.CommandText.Contains("UPDATE [gadgets]", StringComparison.Ordinal))
            {
                var count = Interlocked.Increment(ref _updates);

                if (count <= _misses)
                {
                    command.CommandText = command.CommandText.Replace(
                        "WHERE [gadget_id] =",
                        "WHERE 1 = 0 AND [gadget_id] =",
                        StringComparison.Ordinal
                    );
                }
                else if (count == _misses + 1)
                {
                    await _fixture!.ExecuteAsync(
                        "DELETE FROM gadgets WHERE gadget_id = 1",
                        cancellationToken
                    );
                }
            }

            return result;
        }
    }
}
