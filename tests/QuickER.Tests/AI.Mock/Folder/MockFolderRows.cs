using System.IO;
using System.Text;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Platform;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Mock.Folder.MockFolderHarness;

namespace QuickER.Tests.AI.Mock.Folder;

/// <summary>モックフォルダの保存単位の表：ストア（<see cref="MockFolderStore"/>）とセッション（<see cref="MockFolderDesignSession"/>）の操作の列</summary>
/// <remarks>
/// 行は元のテストの入力と期待を持ち、手は組（<see cref="MockFolderHarness"/>）のメソッドで打つ。どの手の後にも組が不変条件を見張る
/// （失敗した手の後のディスクとメモリ・一時ファイル・宣言と実体の整合・CSS・SourceSchema・ターンの完了・保存の通知）。
/// 失敗で残る一時の食い違い 3 つ（宣言の無い HTML・HTML の無い宣言・新しい CSS）は、それを作る行の説明に書く。
/// </remarks>
internal static partial class MockFolderRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<MockFolderScenario> Table =>
        new(
            EngineIds.Ai,
            [
                .. Create(),
                .. Screens(),
                .. Entities(),
                .. Failures(),
                .. Reads(),
                .. Session(),
                .. SessionEntities(),
                .. InitialTurn(),
            ]
        );

    private static IReadOnlyList<MockTransition> None => Array.Empty<MockTransition>();

    private static MockScreenEntity Entity(string name, string operations) =>
        new() { Name = name, Operations = operations };

    /// <summary>新規作成と mock.json の書式</summary>
    private static IEnumerable<ScenarioRow<MockFolderScenario>> Create() =>
        [
            Row(
                "create-new-then-open-round-trips",
                "CreateNew の後に開き直すと、版・表題・スキーマ・空の画面一覧がそのまま読める",
                MockFolderEntry.CreateNew,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew(title: "受注管理", schema: "# データベーススキーマ");

                    var reopened = h.Reopen();
                    reopened.Version.Should().Be(MockManifest.CurrentVersion);
                    reopened.Title.Should().Be("受注管理");
                    reopened.SourceSchema.Should().Be("# データベーススキーマ");
                    reopened.Screens.Should().BeEmpty();
                }
            ),
            Row(
                "create-new-manifest-is-camel-case-unescaped-without-bom",
                "mock.json は camelCase・日本語を Unicode エスケープせず・BOM なしの UTF-8 で書く（BOM は組がどの手の後にも見る）",
                MockFolderEntry.CreateNew,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew(title: "受注管理", schema: "スキーマ");

                    var bytes = File.ReadAllBytes(h.ManifestPath);
                    (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                        .Should()
                        .BeFalse();
                    var json = File.ReadAllText(h.ManifestPath);
                    json.Should().Contain("\"version\"");
                    json.Should().Contain("\"sourceSchema\"");
                    json.Should().Contain("受注管理");
                    json.Should().NotContain("\\u53d7");
                }
            ),
            Row(
                "create-new-on-existing-folder-is-refused",
                "mock.json が既にあるフォルダの CreateNew は InvalidOperationException で断り、mock.json を変えない",
                MockFolderEntry.CreateNew,
                MockFolderOutcome.Refused,
                h =>
                {
                    h.CreateNew();

                    var act = () => h.CreateNew();

                    act.Should().Throw<InvalidOperationException>();
                }
            ),
            Row(
                "create-new-keeps-foreign-manifest-written-after-check",
                "存在の確認の後・書き込みの前に別のプロセスが作った mock.json を上書きせず、作りかけ・一時ファイルを残さず、事前の確認と同じ文言で断る",
                MockFolderEntry.CreateNew,
                MockFolderOutcome.IoFailure,
                h =>
                {
                    const string ForeignManifest = "{ \"title\": \"別のプロセス\" }";

                    var act = () =>
                        h.CreateNew(beforeManifestWrite: () =>
                            File.WriteAllText(h.ManifestPath, ForeignManifest)
                        );

                    var thrown = act.Should().Throw<InvalidOperationException>().Which;
                    File.ReadAllText(h.ManifestPath)
                        .Should()
                        .Be(ForeignManifest, "別のプロセスの mock.json を上書きしない");
                    var names = Directory.GetFiles(h.Folder).Select(Path.GetFileName).ToList();
                    names.Should().Equal(["mock.json"], "作りかけのファイルを残さない");
                    names
                        .Where(name => AtomicFile.IsTemporaryFileName(name!))
                        .Should()
                        .BeEmpty("一時ファイルを残さない");

                    var precheck = () => h.CreateNew();
                    precheck
                        .Should()
                        .Throw<InvalidOperationException>()
                        .Which.Message.Should()
                        .Be(thrown.Message);
                }
            ),
        ];

    /// <summary>画面の保存・削除・CSS・スキーマの成功と、入力の拒否</summary>
    private static IEnumerable<ScenarioRow<MockFolderScenario>> Screens() =>
        [
            Row(
                "save-screen-long-file-name-succeeds",
                "受け付ける長さの画面のファイル名（235 文字）へ保存できる（原子的な書き込みの一時ファイルの名前が上限を超えない）",
                MockFolderEntry.SaveScreen,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    var file = new string('a', 230) + ".html";
                    h.CreateNew();

                    h.SaveScreen(file, "n", "d", "<html><body></body></html>", None, "r");

                    File.Exists(Path.Combine(h.Folder, file)).Should().BeTrue();
                }
            ),
            Row(
                "save-screen-adds-screen-and-writes-html",
                "SaveScreen は画面を足して HTML を BOM なしの UTF-8 で書き、改訂に注記と固定時刻を積む",
                MockFolderEntry.SaveScreen,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew();

                    h.SaveScreen(
                        "OrderList.html",
                        "注文一覧",
                        "注文の一覧画面",
                        ScreenHtml("<h1>注文一覧</h1>"),
                        None,
                        "初版"
                    );

                    var path = Path.Combine(h.Folder, "OrderList.html");
                    File.ReadAllText(path, Encoding.UTF8).Should().Contain("注文一覧");
                    var manifest = h.Store.Manifest;
                    manifest.Screens.Should().ContainSingle();
                    manifest.Screens[0].File.Should().Be("OrderList.html");
                    manifest.Screens[0].Name.Should().Be("注文一覧");
                    manifest.Revisions.Should().ContainSingle(r => r.Note == "初版");
                    manifest.Revisions[0].Timestamp.Should().Be(FixedNow);
                }
            ),
            Row(
                "save-screen-upserts-case-insensitively",
                "同じ名前（大文字小文字の違いだけ）の SaveScreen は 1 つの画面を上書きし、改訂は 2 つ積む",
                MockFolderEntry.SaveScreen,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew();

                    h.SaveScreen(
                        "OrderList.html",
                        "旧名",
                        "旧説明",
                        ScreenHtml("<h1>v1</h1>"),
                        None,
                        "v1"
                    );
                    h.SaveScreen(
                        "orderlist.html",
                        "新名",
                        "新説明",
                        ScreenHtml("<h1>v2</h1>"),
                        None,
                        "v2"
                    );

                    var manifest = h.Store.Manifest;
                    manifest.Screens.Should().ContainSingle();
                    manifest.Screens[0].Name.Should().Be("新名");
                    manifest.Screens[0].Description.Should().Be("新説明");
                    manifest.Revisions.Should().HaveCount(2);
                }
            ),
            Row(
                "save-screen-replaces-transitions-from-same-screen",
                "SaveScreen は同じ画面を起点とする遷移を新しい一式で差し替える",
                MockFolderEntry.SaveScreen,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew();

                    h.SaveScreen(
                        "OrderList.html",
                        "一覧",
                        "",
                        ScreenHtml("<a href=\"OrderDetail.html\">詳細</a>"),
                        [
                            new MockTransition
                            {
                                From = "OrderList.html",
                                To = "OrderDetail.html",
                                Trigger = "行クリック",
                            },
                        ],
                        "v1"
                    );
                    h.SaveScreen(
                        "OrderList.html",
                        "一覧",
                        "",
                        ScreenHtml("<a href=\"OrderNew.html\">新規</a>"),
                        [
                            new MockTransition
                            {
                                From = "OrderList.html",
                                To = "OrderNew.html",
                                Trigger = "ボタン",
                            },
                        ],
                        "v2"
                    );

                    h.Store.Manifest.Transitions.Should().ContainSingle();
                    h.Store.Manifest.Transitions[0].To.Should().Be("OrderNew.html");
                }
            ),
            .. new[]
            {
                ("sub-folder", "sub/Order.html"),
                ("parent", "..\\Order.html"),
                ("not-html", "Order.txt"),
                ("empty", ""),
            }.Select(c =>
                Row(
                    $"save-screen-invalid-file-name-is-refused-{c.Item1}",
                    $"不正なファイル名（{c.Item2}）の SaveScreen は ArgumentException で断り、何も書かない",
                    MockFolderEntry.SaveScreen,
                    MockFolderOutcome.Refused,
                    h =>
                    {
                        h.CreateNew();

                        var act = () =>
                            h.SaveScreen(c.Item2, "n", "d", ScreenHtml("<h1>x</h1>"), None, "note");

                        act.Should().Throw<ArgumentException>();
                    }
                )
            ),
            .. new[] { ("blank", "   "), ("fragment", "<div>断片のみ</div>") }.Select(c =>
                Row(
                    $"save-screen-{c.Item1}-html-is-refused",
                    $"空白だけ・<html を含まない HTML（{c.Item1}）の SaveScreen は ArgumentException で断り、何も書かない",
                    MockFolderEntry.SaveScreen,
                    MockFolderOutcome.Refused,
                    h =>
                    {
                        h.CreateNew();

                        var act = () => h.SaveScreen("A.html", "n", "d", c.Item2, None, "note");

                        act.Should().Throw<ArgumentException>();
                    }
                )
            ),
            Row(
                "save-screen-warns-without-stylesheet-link",
                "共有 CSS を参照しない画面の SaveScreen は style.css の警告を返す（保存は断らない）",
                MockFolderEntry.SaveScreen,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew();

                    var warnings = h.SaveScreen(
                        "A.html",
                        "n",
                        "d",
                        ScreenHtml("<h1>x</h1>", withStylesheet: false),
                        None,
                        "note"
                    );

                    warnings.Should().Contain(w => w.Contains("style.css"));
                }
            ),
            Row(
                "save-screen-link-to-target-declared-by-another-screen-is-not-warned",
                "他の画面が遷移を宣言した未作成の画面へのリンクは警告せず、どこにも宣言の無い画面へのリンクと、リンクしていない遷移の宣言は警告する",
                MockFolderEntry.SaveScreen,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew();
                    h.SaveScreen(
                        "A.html",
                        "A",
                        "a",
                        ScreenHtml("<a href=\"B.html\">B</a>"),
                        [new MockTransition { To = "B.html", Trigger = "nav" }],
                        "a"
                    );

                    var warnings = h.SaveScreen(
                        "C.html",
                        "C",
                        "c",
                        ScreenHtml("<a href=\"B.html\">B</a><a href=\"D.html\">D</a>"),
                        [new MockTransition { To = "E.html", Trigger = "next" }],
                        "c"
                    );

                    warnings.Should().NotContain(w => w.Contains("'B.html'"));
                    warnings.Should().Contain(w => w.Contains("Link target 'D.html'"));
                    warnings.Should().Contain(w => w.Contains("Transition target 'E.html'"));
                }
            ),
            Row(
                "remove-screen-removes-html-and-both-direction-transitions",
                "RemoveScreen は HTML を消し、その画面を起点・終点とする遷移を両方とも外す",
                MockFolderEntry.RemoveScreen,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew();
                    h.SaveScreen(
                        "OrderList.html",
                        "一覧",
                        "",
                        ScreenHtml("<a href=\"OrderDetail.html\">詳細</a>"),
                        [new MockTransition { From = "OrderList.html", To = "OrderDetail.html" }],
                        "v1"
                    );
                    h.SaveScreen(
                        "OrderDetail.html",
                        "詳細",
                        "",
                        ScreenHtml("<a href=\"OrderList.html\">戻る</a>"),
                        [new MockTransition { From = "OrderDetail.html", To = "OrderList.html" }],
                        "v2"
                    );

                    h.RemoveScreen("OrderDetail.html");

                    File.Exists(Path.Combine(h.Folder, "OrderDetail.html")).Should().BeFalse();
                    h.Store.Manifest.Screens.Should()
                        .ContainSingle(s => s.File == "OrderList.html");
                    h.Store.Manifest.Transitions.Should().BeEmpty();
                }
            ),
            Row(
                "remove-screen-missing-is-refused",
                "宣言の無い画面の RemoveScreen は InvalidOperationException で断り、何も変えない",
                MockFolderEntry.RemoveScreen,
                MockFolderOutcome.Refused,
                h =>
                {
                    h.CreateNew();

                    var act = () => h.RemoveScreen("Nope.html");

                    act.Should().Throw<InvalidOperationException>();
                }
            ),
            Row(
                "save-stylesheet-writes-css-and-revision",
                "SaveStylesheet は style.css を書いて改訂を積み、外部参照の無い CSS には警告を返さない",
                MockFolderEntry.SaveStylesheet,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew();
                    h.Store.HasStylesheet.Should().BeFalse();

                    var warnings = h.SaveStylesheet("body { color: #333; }", "スタイル初版");

                    h.Store.HasStylesheet.Should().BeTrue();
                    h.Store.GetStylesheet().Should().Be("body { color: #333; }");
                    h.Store.Manifest.Revisions.Should()
                        .ContainSingle(r => r.Note == "スタイル初版");
                    warnings.Should().BeEmpty();
                }
            ),
            Row(
                "update-source-schema-persists",
                "UpdateSourceSchema はスキーマのスナップショットを書き、開き直しにも残る",
                MockFolderEntry.UpdateSourceSchema,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew(schema: "旧スキーマ");

                    h.UpdateSourceSchema("新スキーマ");

                    h.Reopen().SourceSchema.Should().Be("新スキーマ");
                }
            ),
        ];

    /// <summary>エンティティの宣言（画面 × エンティティの CRUD）と正規化</summary>
    private static IEnumerable<ScenarioRow<MockFolderScenario>> Entities() =>
        [
            Row(
                "normalize-operations-uppercases-filters-dedups-orders",
                "NormalizeOperations は大文字にし、C/R/U/D 以外と重複を除き、C→R→U→D に並べる（null・空は空）",
                MockFolderEntry.Normalize,
                MockFolderOutcome.Succeeds,
                _ =>
                {
                    MockFolderStore.NormalizeOperations("urc").Should().Be("CRU");
                    MockFolderStore.NormalizeOperations("dcru").Should().Be("CRUD");
                    MockFolderStore.NormalizeOperations("CCRr").Should().Be("CR");
                    MockFolderStore.NormalizeOperations("xyz").Should().BeEmpty();
                    MockFolderStore.NormalizeOperations("").Should().BeEmpty();
                    MockFolderStore.NormalizeOperations(null).Should().BeEmpty();
                }
            ),
            Row(
                "normalize-entities-drops-empty-operations-and-trims-names",
                "NormalizeEntities は名前の前後の空白を除き、操作が空になった宣言は捨てて名前を控え、名前が空の宣言は黙って除く",
                MockFolderEntry.Normalize,
                MockFolderOutcome.Succeeds,
                _ =>
                {
                    var result = MockFolderStore.NormalizeEntities([
                        Entity("  Product ", "ur"),
                        Entity("Junk", "zzz"),
                        Entity("   ", "cru"),
                    ]);

                    result.Entities.Should().ContainSingle();
                    result.Entities[0].Name.Should().Be("Product");
                    result.Entities[0].Operations.Should().Be("RU");
                    result.DiscardedNames.Should().BeEquivalentTo(["Junk"]);
                }
            ),
            Row(
                "save-screen-null-entities-keep-declarations",
                "entities を省いた（null の）SaveScreen は既存の宣言を残す",
                MockFolderEntry.SaveScreen,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    SaveWithEntities(h, [Entity("Order", "CRUD")]);

                    h.SaveScreen(
                        "OrderList.html",
                        "注文一覧",
                        "説明変更",
                        ScreenHtml("<h1>v2</h1>"),
                        None,
                        "v2"
                    );

                    var entities = EntitiesOf(h);
                    entities.Should().ContainSingle();
                    entities[0].Name.Should().Be("Order");
                    entities[0].Operations.Should().Be("CRUD");
                }
            ),
            Row(
                "save-screen-empty-entities-clear-declarations",
                "entities が空の SaveScreen は宣言を消す",
                MockFolderEntry.SaveScreen,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    SaveWithEntities(h, [Entity("Order", "CRUD")]);

                    SaveWithEntities(h, []);

                    EntitiesOf(h).Should().BeEmpty();
                }
            ),
            Row(
                "save-screen-non-empty-entities-replace-and-normalize",
                "entities が空でない SaveScreen は宣言を正規化して全部置き換え、開き直しても同じ宣言が読める",
                MockFolderEntry.SaveScreen,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    SaveWithEntities(h, [Entity("Order", "CRUD")]);

                    SaveWithEntities(h, [Entity("Customer", "urc"), Entity("Product", "r")]);

                    EntitiesOf(h)
                        .Select(e => (e.Name, e.Operations))
                        .Should()
                        .BeEquivalentTo([("Customer", "CRU"), ("Product", "R")]);
                    (h.Reopen().Screens.Single(s => s.File == "OrderList.html").Entities ?? [])
                        .Select(e => (e.Name, e.Operations))
                        .Should()
                        .BeEquivalentTo([("Customer", "CRU"), ("Product", "R")]);
                }
            ),
            Row(
                "save-screen-entities-round-trip-through-manifest",
                "保存した宣言は開き直しで正規化した形のまま読める",
                MockFolderEntry.SaveScreen,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    SaveWithEntities(h, [Entity("Order", "cr"), Entity("Customer", "r")]);

                    (h.Reopen().Screens.Single(s => s.File == "OrderList.html").Entities ?? [])
                        .Select(e => (e.Name, e.Operations))
                        .Should()
                        .BeEquivalentTo([("Order", "CR"), ("Customer", "R")]);
                }
            ),
            Row(
                "save-screen-without-declarations-omits-entities-key",
                "宣言の無い画面は mock.json に entities のキーを書かない",
                MockFolderEntry.SaveScreen,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew();

                    h.SaveScreen(
                        "OrderList.html",
                        "注文一覧",
                        "注文の一覧",
                        ScreenHtml("<h1>一覧</h1>"),
                        None,
                        "note"
                    );

                    File.ReadAllText(h.ManifestPath).Should().NotContain("entities");
                }
            ),
        ];

    /// <summary>書き込みの失敗とメモリの整合（失敗の注入は mock.json の読み取り専用の属性・同名のフォルダ）</summary>
    private static IEnumerable<ScenarioRow<MockFolderScenario>> Failures() =>
        [
            Row(
                "save-screen-manifest-write-failure-does-not-leak-into-later-save",
                "mock.json の書き込みに失敗した SaveScreen は、メモリにも mock.json にも次の保存にも残らない（失敗で残る一時の食い違い：宣言の無い HTML の B.html）",
                MockFolderEntry.SaveScreen,
                MockFolderOutcome.IoFailure,
                h =>
                {
                    h.CreateNew();
                    h.SaveScreen("A.html", "A", "a", ScreenHtml("<h1>A</h1>"), None, "first");
                    var revisionsBefore = h.Store.Manifest.Revisions.Count;
                    var jsonBefore = File.ReadAllText(h.ManifestPath);

                    using (h.ManifestReadOnly())
                    {
                        var act = () =>
                            h.SaveScreen(
                                "B.html",
                                "B",
                                "b",
                                ScreenHtml("<h1>B</h1>"),
                                [new MockTransition { To = "A.html", Trigger = "back" }],
                                "failed"
                            );

                        act.Should()
                            .Throw<Exception>()
                            .Which.Should()
                            .Match<Exception>(ex =>
                                ex is IOException || ex is UnauthorizedAccessException
                            );
                        h.Store.Manifest.Screens.Select(s => s.File).Should().Equal("A.html");
                        h.Store.Manifest.Transitions.Should().BeEmpty();
                        h.Store.Manifest.Revisions.Should().HaveCount(revisionsBefore);
                        File.ReadAllText(h.ManifestPath).Should().Be(jsonBefore);
                        File.Exists(Path.Combine(h.Folder, "B.html"))
                            .Should()
                            .BeTrue("HTML は mock.json より先に書く");
                    }

                    h.SaveStylesheet("body{}", "css");

                    var reopened = h.Reopen();
                    reopened.Screens.Select(s => s.File).Should().Equal("A.html");
                    reopened.Transitions.Should().BeEmpty();
                    reopened.Revisions.Should().NotContain(r => r.Note == "failed");
                }
            ),
            Row(
                "save-screen-html-write-failure-leaves-manifest",
                "画面の HTML を書けない（同名のフォルダがある）SaveScreen は、マニフェストも mock.json も変えない",
                MockFolderEntry.SaveScreen,
                MockFolderOutcome.IoFailure,
                h =>
                {
                    h.CreateNew();
                    Directory.CreateDirectory(Path.Combine(h.Folder, "B.html"));
                    var jsonBefore = File.ReadAllText(h.ManifestPath);

                    var act = () =>
                        h.SaveScreen("B.html", "B", "b", ScreenHtml("<h1>B</h1>"), None, "failed");

                    act.Should().Throw<Exception>();
                    h.Store.Manifest.Screens.Should().BeEmpty();
                    h.Store.Manifest.Revisions.Should().BeEmpty();
                    File.ReadAllText(h.ManifestPath).Should().Be(jsonBefore);
                }
            ),
            Row(
                "remove-screen-manifest-write-failure-keeps-declaration-and-retry-recovers",
                "mock.json の書き込みに失敗した RemoveScreen は HTML だけ先に消えて宣言を残し（失敗で残る一時の食い違い：HTML の無い宣言の A.html）、やり直しで宣言も外れる",
                MockFolderEntry.RemoveScreen,
                MockFolderOutcome.IoFailure,
                h =>
                {
                    h.CreateNew();
                    h.SaveScreen("A.html", "A", "a", ScreenHtml("<h1>A</h1>"), None, "first");

                    using (h.ManifestReadOnly())
                    {
                        var act = () => h.RemoveScreen("A.html");

                        act.Should().Throw<Exception>();
                        File.Exists(Path.Combine(h.Folder, "A.html")).Should().BeFalse();
                        h.Store.Manifest.Screens.Select(s => s.File).Should().Equal("A.html");
                    }

                    h.RemoveScreen("A.html");

                    h.Store.Manifest.Screens.Should().BeEmpty();
                    h.Reopen().Screens.Should().BeEmpty();
                }
            ),
            Row(
                "update-source-schema-manifest-write-failure-keeps-schema",
                "mock.json の書き込みに失敗した UpdateSourceSchema は、メモリにも mock.json にも次の保存にもスキーマを残さない",
                MockFolderEntry.UpdateSourceSchema,
                MockFolderOutcome.IoFailure,
                h =>
                {
                    h.CreateNew(schema: "# old");

                    using (h.ManifestReadOnly())
                    {
                        var act = () => h.UpdateSourceSchema("# new");

                        act.Should().Throw<Exception>();
                    }

                    h.Store.Manifest.SourceSchema.Should().Be("# old");
                    h.Reopen().SourceSchema.Should().Be("# old");

                    // 次の保存が、失敗した変更を持ち込まない（読み取り用の複製は次の確定まで使い回されるので、確定の後に見る）
                    h.SaveStylesheet("body{}", "css");

                    h.Store.Manifest.SourceSchema.Should().Be("# old");
                    h.Reopen().SourceSchema.Should().Be("# old");
                }
            ),
            Row(
                "save-stylesheet-manifest-write-failure-leaves-new-css-only",
                "mock.json の書き込みに失敗した SaveStylesheet は改訂を積まない。CSS は先に書くので新しい内容のまま残る（失敗で残る一時の食い違い：新しい CSS。マニフェストは差し替えない）。次の保存で CSS も確定する",
                MockFolderEntry.SaveStylesheet,
                MockFolderOutcome.IoFailure,
                h =>
                {
                    h.CreateNew();
                    h.SaveStylesheet("body{}", "css1");
                    var revisionsBefore = h.Store.Manifest.Revisions.Count;

                    using (h.ManifestReadOnly())
                    {
                        var act = () => h.SaveStylesheet("body{color:red}", "css2");

                        act.Should()
                            .Throw<Exception>()
                            .Which.Should()
                            .Match<Exception>(ex =>
                                ex is IOException || ex is UnauthorizedAccessException
                            );
                        h.Store.Manifest.Revisions.Should().HaveCount(revisionsBefore);
                        h.Store.GetStylesheet().Should().Be("body{color:red}");
                    }

                    h.SaveStylesheet("body{color:blue}", "css3");

                    h.Reopen().Revisions.Select(r => r.Note).Should().Equal("css1", "css3");
                    h.Store.GetStylesheet().Should().Be("body{color:blue}");
                }
            ),
            Row(
                "open-hand-edited-nulls-are-normalized-for-later-steps",
                "手で書いた mock.json の null を開くときに補い、後段（単一 HTML・設計書・再開のプロンプト・保存）が NRE にならない（手で書いた mock.json なので宣言と実体の整合・CSS は見張らない）",
                MockFolderEntry.Open,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew();
                    h.SaveScreen(
                        "A.html",
                        "A",
                        "a",
                        ScreenHtml("<a href=\"A.html\">self</a>"),
                        None,
                        "first"
                    );
                    h.WriteManifestByHand(
                        """
                        {
                          "version": 1,
                          "title": null,
                          "sourceSchema": null,
                          "screens": [
                            null,
                            { "file": "A.html", "name": null, "description": null,
                              "entities": [ null, { "name": null, "operations": null }, { "name": "Order", "operations": "R" } ] }
                          ],
                          "transitions": [ null, { "from": "A.html", "to": null, "trigger": null } ],
                          "revisions": [ null, { "timestamp": "2026-07-23T10:00:00+00:00", "note": null } ]
                        }
                        """
                    );

                    var reopened = h.OpenStore();
                    var manifest = reopened.Manifest;

                    manifest.Title.Should().BeEmpty();
                    manifest.SourceSchema.Should().BeEmpty();
                    manifest.Screens.Should().ContainSingle();
                    manifest.Screens[0].Name.Should().BeEmpty();
                    manifest.Screens[0].Description.Should().BeEmpty();
                    // 宣言は保存の経路と同じ正規化に通す：名前が null の宣言は同定できないので捨てる（出力の表の行 load-hand-written-entities-are-normalized）
                    manifest
                        .Screens[0]
                        .Entities!.Select(e => (e.Name, e.Operations))
                        .Should()
                        .Equal(("Order", "R"));
                    manifest.Transitions.Should().ContainSingle();
                    manifest.Transitions[0].To.Should().BeEmpty();
                    manifest.Revisions.Should().ContainSingle();
                    manifest.Revisions[0].Note.Should().BeEmpty();

                    MockBundleExporter.Export(reopened).Should().Contain("data-screen");
                    MockDesignDocExporter.Export(reopened).Should().Contain("A.html");
                    MockResumePrompt
                        .Build("# schema", manifest, schemaChanged: true)
                        .Should()
                        .NotBeEmpty();
                    h.SaveScreen("A.html", "A", "a", ScreenHtml("<h1>A</h1>"), None, "again");
                    h.Reopen().Screens.Should().ContainSingle();
                }
            ),
            Row(
                "open-refused-newer-format-keeps-manifest-and-current-store",
                "開いているストアがあるところで新しい版の mock.json を開き直すと断られ、mock.json のバイト列も今のストアのマニフェストも変わらず、今のストアで保存を続けられる",
                MockFolderEntry.Open,
                MockFolderOutcome.Refused,
                h =>
                {
                    // 根拠：MockFolderStore.Open の XmlDoc（新しい版は明確なメッセージの例外）と、
                    // 組の不変条件 1（失敗した手は mock.json もメモリも変えない）。中身の文言は出力の表（load-newer の行）が見る
                    h.CreateNew();
                    h.SaveScreen("A.html", "A", "a", ScreenHtml("<h1>A</h1>"), None, "first");
                    var current = h.Store;
                    h.WriteManifestByHand("{ \"version\": 99, \"title\": \"newer\" }");

                    var act = () => h.OpenStore();

                    act.Should().Throw<InvalidOperationException>();
                    h.Store.Should().BeSameAs(current);
                    h.Store.Manifest.Screens.Select(s => s.File).Should().Equal("A.html");
                    h.SaveScreen("B.html", "B", "b", ScreenHtml("<h1>B</h1>"), None, "second");
                    h.Reopen().Version.Should().Be(MockManifest.CurrentVersion);
                    h.Reopen().Screens.Select(s => s.File).Should().Equal("A.html", "B.html");
                }
            ),
            Row(
                "open-manifest-held-by-another-process-throws-io-and-keeps-manifest",
                "特性の固定（文書に定めが無い今の振る舞い）：mock.json を別のプロセスが共有なしで開いている間の Open は IOException のまま投げる（mock.json のバイト列は変わらず、放した後は開ける）",
                MockFolderEntry.Open,
                MockFolderOutcome.IoFailure,
                h =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockFolderStore.cs:170-187（読み込みの JsonException だけを包み、I/O の例外は包まない）。
                    // Open の XmlDoc は I/O の失敗を挙げない。バイト列が変わらないことは組の不変条件 1 が見る
                    h.CreateNew();
                    h.SaveScreen("A.html", "A", "a", ScreenHtml("<h1>A</h1>"), None, "first");

                    var act = () => h.OpenStore(holdExclusively: h.ManifestPath);

                    act.Should().Throw<IOException>();
                    h.OpenStore().Manifest.Screens.Select(s => s.File).Should().Equal("A.html");
                }
            ),
        ];

    /// <summary>読み取り</summary>
    private static IEnumerable<ScenarioRow<MockFolderScenario>> Reads() =>
        [
            Row(
                "read-is-mock-folder-follows-manifest",
                "IsMockFolder は mock.json の有無を返す（CreateNew の前は false・後は true）",
                MockFolderEntry.Read,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    MockFolderStore.IsMockFolder(h.Folder).Should().BeFalse();

                    h.CreateNew();

                    MockFolderStore.IsMockFolder(h.Folder).Should().BeTrue();
                }
            ),
            Row(
                "read-missing-screen-html-is-null",
                "GetScreenHtml は無い画面に null を返す",
                MockFolderEntry.Read,
                MockFolderOutcome.Succeeds,
                h =>
                {
                    h.CreateNew();

                    h.Store.GetScreenHtml("Nope.html").Should().BeNull();
                }
            ),
            Row(
                "read-invalid-screen-file-name-is-refused-without-reading",
                "GetScreenHtml はフォルダ直下の .html でない名前（親フォルダ・区切り・拡張子違い）を ArgumentException で断り、フォルダの外の同名のファイルを読まない",
                MockFolderEntry.Read,
                MockFolderOutcome.Refused,
                h =>
                {
                    // 根拠：MockFolderStore.ValidateScreenFileName の XmlDoc（CheckScreenFileName が断る名前は ArgumentException）と、
                    // CheckScreenFileName の remarks（保存・削除・読み取り・読み込みが同じ判定を使う）。src/QuickER.AI.Mock/MockFolderStore.cs:690-712
                    h.CreateNew();
                    File.WriteAllText(Path.Combine(h.Folder, "..", "outside.html"), "secret");

                    foreach (var name in new[] { "../outside.html", "sub/A.html", "style.css" })
                    {
                        var act = () => h.ReadScreen(name);

                        act.Should().Throw<ArgumentException>(name);
                    }
                }
            ),
            Row(
                "read-screen-held-by-another-process-throws-io-and-keeps-manifest",
                "特性の固定（文書に定めが無い今の振る舞い）：画面の HTML を別のプロセスが共有なしで開いている間の GetScreenHtml は IOException のまま投げる（mock.json は変わらず、放した後は読める）",
                MockFolderEntry.Read,
                MockFolderOutcome.IoFailure,
                h =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockFolderStore.cs:441-448（存在すれば File.ReadAllText で読む。XmlDoc は「未存在なら null」だけ）
                    h.CreateNew();
                    h.SaveScreen("A.html", "A", "a", ScreenHtml("<h1>A</h1>"), None, "first");

                    var act = () =>
                        h.ReadScreen("A.html", holdExclusively: Path.Combine(h.Folder, "A.html"));

                    act.Should().Throw<IOException>();
                    h.ReadScreen("A.html").Should().Contain("<h1>A</h1>");
                }
            ),
        ];

    /// <summary>OrderList.html を宣言つきで保存する（ストアが無ければ作る）</summary>
    private static void SaveWithEntities(
        MockFolderHarness h,
        IReadOnlyList<MockScreenEntity> entities
    )
    {
        if (!File.Exists(h.ManifestPath))
        {
            h.CreateNew();
        }

        h.SaveScreen(
            "OrderList.html",
            "注文一覧",
            "注文の一覧",
            ScreenHtml("<h1>一覧</h1>"),
            None,
            "note",
            entities
        );
    }

    private static IReadOnlyList<MockScreenEntity> EntitiesOf(MockFolderHarness h) =>
        h.Store.Manifest.Screens.Single(s => s.File == "OrderList.html").Entities ?? [];

    /// <summary>行を作る（同期の本体）</summary>
    private static ScenarioRow<MockFolderScenario> Row(
        string id,
        string description,
        MockFolderEntry entry,
        MockFolderOutcome outcome,
        Action<MockFolderHarness> body
    ) =>
        new(
            id,
            null,
            description,
            new MockFolderScenario(
                entry,
                outcome,
                h =>
                {
                    body(h);
                    return Task.CompletedTask;
                }
            )
        );

    /// <summary>行を作る（非同期の本体）</summary>
    private static ScenarioRow<MockFolderScenario> RowAsync(
        string id,
        string description,
        MockFolderEntry entry,
        MockFolderOutcome outcome,
        Func<MockFolderHarness, Task> body
    ) => new(id, null, description, new MockFolderScenario(entry, outcome, body));
}
