using System.Globalization;
using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.AI.Mock.Resources;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Mock.Outputs.MockOutputsContext;

namespace QuickER.Tests.AI.Mock.Outputs;

/// <summary>出力の表の読み込みの検証と設計書の行</summary>
/// <remarks>
/// 設計書の期待の見出し・種別語は <see cref="Strings"/> の <c>MockDoc_*</c> から組む（UI の言語に依らない）。
/// 読み込みの文言を日本語・英語で見る行は、UI のカルチャを行の中で固定する。
/// </remarks>
internal static partial class MockOutputsRows
{
    /// <summary>読み込みの検証（mock.json の中身 → Open の結果と文言）</summary>
    private static IEnumerable<ScenarioRow<MockOutputScenario>> Load() =>
        [
            Row(
                "load-missing-manifest-is-refused",
                "mock.json の無いフォルダの Open は、mock.json を名指しする InvalidOperationException で断る",
                [(MockOutputSut.Load, "load-missing")],
                context =>
                {
                    var act = () => MockFolderStore.Open(context.Folder);

                    act.Should().Throw<InvalidOperationException>().WithMessage("*mock.json*");
                }
            ),
            .. new[] { ("en-US", "*Could not parse*"), ("ja-JP", "*解釈できませんでした*") }.Select(
                c =>
                    Row(
                        $"load-corrupt-manifest-is-refused-{c.Item1}",
                        $"JSON として読めない mock.json の Open は、UI の言語（{c.Item1}）の文言で断る",
                        [(MockOutputSut.Load, "load-corrupt")],
                        context =>
                        {
                            context.WriteManifest("{ broken");

                            var act = () =>
                                WithUiCulture(c.Item1, () => MockFolderStore.Open(context.Folder));

                            act.Should().Throw<InvalidOperationException>().WithMessage(c.Item2);
                        }
                    )
            ),
            Row(
                "load-newer-format-is-refused",
                "版が新しい（Version 99）mock.json の Open は、version を含む文言で断る",
                [(MockOutputSut.Load, "load-newer")],
                context =>
                {
                    context.WriteManifest("{ \"version\": 99, \"title\": \"x\" }");

                    var act = () => MockFolderStore.Open(context.Folder);

                    act.Should().Throw<InvalidOperationException>().WithMessage("*version*");
                }
            ),
            .. new[]
            {
                ("parent", "../evil.html"),
                ("folder", "sub/evil.html"),
                ("empty", ""),
                ("colon", "bad:name.html"),
                ("no-extension", "noext"),
            }.Select(c =>
                Row(
                    $"load-invalid-screen-file-name-is-refused-{c.Item1}",
                    $"手で書き換えた不正な画面のファイル名（{c.Item2}）の mock.json の Open は、破損として断り、内側に ArgumentException を持つ",
                    [(MockOutputSut.Load, "load-invalid-file-name")],
                    context =>
                    {
                        var store = context.CreateStore();
                        store.SaveScreen(
                            "OrderList.html",
                            "注文一覧",
                            "注文の一覧",
                            Screen("<h1>一覧</h1>"),
                            Array.Empty<MockTransition>(),
                            "note"
                        );
                        var json = System.IO.File.ReadAllText(context.ManifestPath);
                        json.Should().Contain("OrderList.html");
                        context.WriteManifest(json.Replace("OrderList.html", c.Item2));

                        var act = () =>
                            WithUiCulture("ja-JP", () => MockFolderStore.Open(context.Folder));

                        act.Should()
                            .Throw<InvalidOperationException>()
                            .WithMessage("*画面ファイル名が不正です*")
                            .Which.InnerException.Should()
                            .BeOfType<ArgumentException>();
                    }
                )
            ),
            .. new[] { ("same", "A.html"), ("case", "a.HTML") }.SelectMany(c =>
                new[] { "en-US", "ja-JP" }.Select(culture =>
                    Row(
                        $"load-duplicate-screen-file-is-refused-{c.Item1}-{culture}",
                        $"手で書いた mock.json に同じ画面のファイル（A.html と {c.Item2}＝大文字小文字を区別しない）が 2 回あると、Open は破損として UI の言語（{culture}）の文言で断り、2 件目のファイル名を名指しする（保存が先頭だけを更新し、削除が両方を消す形にしない）",
                        [(MockOutputSut.Load, "load-duplicate-screen-file")],
                        context =>
                        {
                            context.WriteManifest(
                                $$"""
                                {
                                  "version": 1,
                                  "screens": [
                                    { "file": "A.html", "name": "A1" },
                                    { "file": "B.html", "name": "B" },
                                    { "file": "{{c.Item2}}", "name": "A2" }
                                  ]
                                }
                                """
                            );

                            var act = () =>
                                WithUiCulture(culture, () => MockFolderStore.Open(context.Folder));
                            var expected = WithUiCulture(
                                culture,
                                () =>
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        Strings.Mock_ManifestDuplicateScreenFileFormat,
                                        MockManifest.ManifestFileName,
                                        c.Item2
                                    )
                            );

                            act.Should().Throw<InvalidOperationException>().WithMessage(expected);
                        }
                    )
                )
            ),
            Row(
                "load-legacy-manifest-without-entities-opens",
                "entities を持たない旧い形の mock.json を開け、宣言の無い画面は宣言なしのまま読める",
                [(MockOutputSut.Load, "load-legacy")],
                context =>
                {
                    context.WriteManifest(
                        "{ \"version\": 1, \"title\": \"旧\", \"screens\": [ { \"file\": \"A.html\", \"name\": \"A\" } ] }"
                    );

                    var screen = MockFolderStore.Open(context.Folder).Manifest.Screens.Single();

                    screen.File.Should().Be("A.html");
                    (screen.Entities is null || screen.Entities.Count == 0).Should().BeTrue();
                }
            ),
            Row(
                "load-hand-written-entities-are-normalized",
                "手で書いた entities は、開くときに保存の経路と同じ正規化に通す（名前の前後の空白を除く・名前が空と有効な操作の無い宣言を捨てる・"
                    + "操作を C→R→U→D・同じ名前〔大文字小文字だけの違いを含む〕は操作の和で 1 つにまとめる・空の配列は宣言なし）。"
                    + "開いた後の設計書の CRUD 表と再開のプロンプトにも正規化した形が出る。開くだけでは mock.json を書き換えない",
                [
                    (MockOutputSut.Load, "load-entities-normalized"),
                    (MockOutputSut.DesignDoc, "doc-crud"),
                    (MockOutputSut.ResumePrompt, "resume-entities"),
                ],
                context =>
                {
                    // 根拠：src/QuickER.AI.Mock/CLAUDE.md「保存単位とフォルダ」（entities は保存の経路と同じ正規化に通す）・
                    // MockManifest.cs の MockScreenEntity の XmlDoc（名前は大文字小文字を無視して照合・操作は C→R→U→D）
                    var created = context.CreateStore();
                    created.SaveStylesheet("body{}", "css");
                    created.SaveScreen(
                        "A.html",
                        "A",
                        "",
                        Screen("<h1>a</h1>"),
                        Array.Empty<MockTransition>(),
                        "v1"
                    );
                    created.SaveScreen(
                        "B.html",
                        "B",
                        "",
                        Screen("<h1>b</h1>"),
                        Array.Empty<MockTransition>(),
                        "v2"
                    );
                    var manifest = System.Text.Json.Nodes.JsonNode.Parse(
                        File.ReadAllText(context.ManifestPath)
                    )!;
                    manifest["screens"]![0]!["entities"] = System.Text.Json.Nodes.JsonNode.Parse(
                        """
                        [
                          { "name": "  Order ", "operations": "urc" },
                          { "name": "", "operations": "R" },
                          { "name": "Customer", "operations": "xyz" },
                          { "name": "order", "operations": "D" },
                          { "name": "Ghost", "operations": "CRUDX" }
                        ]
                        """
                    );
                    manifest["screens"]![1]!["entities"] = new System.Text.Json.Nodes.JsonArray();
                    context.WriteManifest(manifest.ToJsonString());
                    var written = File.ReadAllBytes(context.ManifestPath);

                    var store = MockFolderStore.Open(context.Folder);

                    var screens = store.Manifest.Screens;
                    screens[0]
                        .Entities!.Select(e => (e.Name, e.Operations))
                        .Should()
                        .Equal(("Order", "CRUD"), ("Ghost", "CRUD"));
                    screens[1].Entities.Should().BeNull();
                    File.ReadAllBytes(context.ManifestPath).Should().Equal(written);

                    var doc = ExportDoc(store);
                    doc.Should()
                        .Contain($"| {Strings.MockDoc_ColScreen} | Order | Ghost |")
                        .And.Contain("| [A](<A.html>) | CRUD | CRUD |")
                        .And.Contain("| [B](<B.html>) |  |  |");

                    WithUiCulture(
                            "ja",
                            () => MockResumePrompt.Build("# 現在スキーマ", store.Manifest, false)
                        )
                        .Should()
                        .Contain("エンティティ: Order(CRUD), Ghost(CRUD)");
                }
            ),
            .. new[]
            {
                ("en-US", "*not a mock folder*", "*newer format*", "*already exists*"),
                (
                    "ja-JP",
                    "*モックフォルダではありません*",
                    "*より新しいフォーマット*",
                    "*既に存在します*"
                ),
            }.Select(c =>
                Row(
                    $"load-folder-failure-messages-follow-ui-culture-{c.Item1}",
                    $"モックフォルダでない・版が新しい・既にある、の文言は UI の言語（{c.Item1}）に従う",
                    [
                        (MockOutputSut.Load, "load-missing"),
                        (MockOutputSut.Load, "load-newer"),
                        (MockOutputSut.Load, "load-already-exists"),
                    ],
                    context =>
                    {
                        var missing = () =>
                            WithUiCulture(c.Item1, () => MockFolderStore.Open(context.Folder));
                        missing.Should().Throw<InvalidOperationException>().WithMessage(c.Item2);

                        context.WriteManifest("{ \"version\": 99 }");

                        var newer = () =>
                            WithUiCulture(c.Item1, () => MockFolderStore.Open(context.Folder));
                        newer.Should().Throw<InvalidOperationException>().WithMessage(c.Item3);

                        var create = () => WithUiCulture(c.Item1, () => context.CreateStore());
                        create.Should().Throw<InvalidOperationException>().WithMessage(c.Item4);
                    }
                )
            ),
        ];

    /// <summary>2 画面＋遷移の設計書の標準のモック</summary>
    private static MockFolderStore DocTwoScreenMock(MockOutputsContext context)
    {
        var store = context.CreateStore("受注管理", "schema");
        store.SaveStylesheet("body { color: #222; }", "css");
        store.SaveScreen(
            "OrderList.html",
            "注文一覧",
            "注文の一覧を表示する画面",
            Screen("<h1>注文一覧</h1><a href=\"OrderDetail.html\">詳細へ</a>"),
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
        store.SaveScreen(
            "OrderDetail.html",
            "注文詳細",
            "",
            Screen("<h1>注文詳細</h1><a href=\"OrderList.html\">戻る</a>"),
            [new MockTransition { From = "OrderDetail.html", To = "OrderList.html" }],
            "v2"
        );
        return store;
    }

    /// <summary>CSS と画面 1 つのストア（設計書の行の多くの入力）</summary>
    private static MockFolderStore DocOneScreen(
        MockOutputsContext context,
        string file,
        string name,
        string description,
        string body,
        IReadOnlyList<MockScreenEntity>? entities = null,
        string title = "t"
    )
    {
        var store = context.CreateStore(title, "s");
        store.SaveStylesheet("body{}", "css");
        store.SaveScreen(
            file,
            name,
            description,
            Screen(body),
            Array.Empty<MockTransition>(),
            "v1",
            entities
        );
        return store;
    }

    private static string ItemTableHeader =>
        $"| {Strings.MockDoc_ColKind} | {Strings.MockDoc_ColItem} | {Strings.MockDoc_ColNote} |";

    /// <summary>設計書（決定的・LF・CR と U+2028 なしは全行で照合する）</summary>
    private static IEnumerable<ScenarioRow<MockOutputScenario>> Doc() =>
        [
            Row(
                "doc-core-structure",
                "タイトル・画面一覧（リンクつきの表）・mermaid の遷移図（全画面のノードとトリガーつきのエッジ）・画面のセクション（説明・遷移先・遷移元）を出す",
                [(MockOutputSut.DesignDoc, "doc-structure")],
                context =>
                {
                    var doc = ExportDoc(DocTwoScreenMock(context));

                    doc.Should().Contain("# 受注管理");
                    doc.Should().Contain("## " + Strings.MockDoc_ScreenListHeading);
                    doc.Should()
                        .Contain(
                            $"| {Strings.MockDoc_ColScreen} | {Strings.MockDoc_ColDescription} |"
                        );
                    doc.Should()
                        .Contain("| [注文一覧](<OrderList.html>) | 注文の一覧を表示する画面 |");
                    doc.Should().Contain("| [注文詳細](<OrderDetail.html>) |  |");
                    doc.Should().Contain("## " + Strings.MockDoc_TransitionDiagramHeading);
                    doc.Should().Contain("```mermaid");
                    doc.Should().Contain("flowchart LR");
                    doc.Should().Contain("\n  s_OrderList[\"注文一覧\"]\n");
                    doc.Should().Contain("\n  s_OrderDetail[\"注文詳細\"]\n");
                    doc.Should().Contain("\n  s_OrderList -->|行クリック| s_OrderDetail\n");
                    doc.Should().Contain("\n  s_OrderDetail --> s_OrderList\n");
                    doc.Should().Contain("## 注文一覧");
                    doc.Should().Contain("注文の一覧を表示する画面");
                    var trigger = string.Format(Strings.MockDoc_TriggerFormat, "行クリック");
                    doc.Should()
                        .Contain(
                            $"- {Strings.MockDoc_TransitionTo}: [注文詳細](<OrderDetail.html>){trigger}"
                        );
                    doc.Should()
                        .Contain(
                            $"- {Strings.MockDoc_TransitionFrom}: [注文一覧](<OrderList.html>){trigger}"
                        );
                }
            ),
            Row(
                "doc-blank-title-uses-default",
                "表題が空なら既定の表題を使う",
                [(MockOutputSut.DesignDoc, "doc-title")],
                context =>
                {
                    var doc = ExportDoc(
                        DocOneScreen(context, "A.html", "A", "", "<h1>a</h1>", title: "")
                    );

                    doc.Should().Contain("# " + Strings.MockDoc_DefaultTitle);
                }
            ),
            Row(
                "doc-mermaid-label-escaped-and-ids-disambiguated",
                "mermaid のラベルの引用符を &quot; にし、ぶつかるノード ID には連番を足す",
                [(MockOutputSut.DesignDoc, "doc-mermaid")],
                context =>
                {
                    var store = context.CreateStore();
                    store.SaveStylesheet("body{}", "css");
                    store.SaveScreen(
                        "Order-Detail.html",
                        "詳細\"A\"",
                        "",
                        Screen("<h1>x</h1>"),
                        Array.Empty<MockTransition>(),
                        "v1"
                    );
                    store.SaveScreen(
                        "Order Detail.html",
                        "詳細B",
                        "",
                        Screen("<h1>y</h1>"),
                        Array.Empty<MockTransition>(),
                        "v2"
                    );

                    var doc = ExportDoc(store);

                    doc.Should().Contain("\n  s_Order_Detail[\"詳細&quot;A&quot;\"]\n");
                    doc.Should().NotContain("[\"詳細\"A\"\"]");
                    doc.Should().Contain("\n  s_Order_Detail_2[\"詳細B\"]\n");
                }
            ),
            Row(
                "doc-transition-to-unknown-screen-skipped",
                "実在しない画面への遷移はエッジに出さない（ノードは出す）",
                [(MockOutputSut.DesignDoc, "doc-mermaid")],
                context =>
                {
                    var store = context.CreateStore();
                    store.SaveStylesheet("body{}", "css");
                    store.SaveScreen(
                        "A.html",
                        "A",
                        "",
                        Screen("<h1>a</h1>"),
                        [new MockTransition { From = "A.html", To = "Ghost.html" }],
                        "v1"
                    );

                    var doc = ExportDoc(store);

                    doc.Should().NotContain("Ghost");
                    doc.Should().Contain("\n  s_A[\"A\"]\n");
                    doc.Should().NotContain("-->");
                }
            ),
            Row(
                "doc-screen-items-extracted",
                "代表的な HTML から画面の項目（入力欄・選択肢・ボタン・表の列）を抜き出し、hidden・script の中の偽のタグ・素のリンクは拾わない",
                [(MockOutputSut.DesignDoc, "doc-items")],
                context =>
                {
                    var body =
                        "<form>"
                        + "<label for=\"name\">氏名</label><input id=\"name\" type=\"text\" required>"
                        + "<label>メール<input type=\"email\" name=\"email\" placeholder=\"you@example.com\"></label>"
                        + "<input type=\"text\" placeholder=\"キーワード検索\">"
                        + "<input type=\"hidden\" name=\"token\" value=\"secret\">"
                        + "<textarea name=\"memo\" placeholder=\"備考を入力\"></textarea>"
                        + "<select id=\"pref\" name=\"pref\"><option>東京</option><option>大阪</option><option>京都</option><option>福岡</option></select>"
                        + "<label>男<input type=\"radio\" name=\"gender\"></label>"
                        + "<label>女<input type=\"radio\" name=\"gender\"></label>"
                        + "<button>登録</button>"
                        + "<button>&laquo; 前へ</button>"
                        + "<input type=\"submit\" value=\"検索\">"
                        + "<a class=\"btn btn-primary\" href=\"Next.html\">次へ</a>"
                        + "<a href=\"Plain.html\">素のリンク</a>"
                        + "<table><thead><tr><th>ID</th><th>名前</th><th>状態</th></tr></thead>"
                        + "<tbody><tr><td>1</td><td>a</td><td>x</td></tr></tbody></table>"
                        + "<script>console.log('<input type=\"text\" name=\"ghost\">');</script>"
                        + "</form>";

                    var doc = ExportDoc(
                        DocOneScreen(context, "Form.html", "入力フォーム", "", body)
                    );

                    doc.Should().Contain(ItemTableHeader);
                    doc.Should()
                        .Contain(
                            $"| {Strings.MockDoc_KindInput} | 氏名 | text / {Strings.MockDoc_NoteRequired} |"
                        );
                    doc.Should()
                        .Contain(
                            $"| {Strings.MockDoc_KindInput} | メール | email / you@example.com |"
                        );
                    doc.Should()
                        .Contain($"| {Strings.MockDoc_KindInput} | キーワード検索 | text |");
                    doc.Should()
                        .Contain(
                            $"| {Strings.MockDoc_KindInput} | 備考を入力 | {Strings.MockDoc_NoteMultiline} |"
                        );
                    doc.Should()
                        .Contain($"| {Strings.MockDoc_KindChoice} | pref | 東京 / 大阪 / 京都 … |");
                    var radioNote =
                        Strings.MockDoc_NoteRadio
                        + " / "
                        + string.Format(
                            CultureInfo.InvariantCulture,
                            Strings.MockDoc_NoteOptionCountFormat,
                            2
                        );
                    doc.Should()
                        .Contain($"| {Strings.MockDoc_KindChoice} | gender | {radioNote} |");
                    doc.Should().Contain($"| {Strings.MockDoc_KindButton} | 登録 |  |");
                    doc.Should().Contain($"| {Strings.MockDoc_KindButton} | « 前へ |  |");
                    doc.Should().NotContain("&laquo;");
                    doc.Should().Contain($"| {Strings.MockDoc_KindButton} | 検索 |  |");
                    doc.Should().Contain($"| {Strings.MockDoc_KindButton} | 次へ |  |");
                    var tableItem = string.Format(
                        CultureInfo.InvariantCulture,
                        Strings.MockDoc_ItemTableFormat,
                        1
                    );
                    doc.Should()
                        .Contain(
                            $"| {Strings.MockDoc_KindTableColumn} | {tableItem} | ID / 名前 / 状態 |"
                        );
                    doc.Should().NotContain("token");
                    doc.Should().NotContain("ghost");
                    doc.Should().NotContain("console.log");
                    doc.Should().NotContain("素のリンク");
                }
            ),
            Row(
                "doc-required-marker-and-button-dedup",
                "必須のマーカーの span は項目名から外して備考へ移し、同じ文字のボタンは 1 行にまとめる",
                [(MockOutputSut.DesignDoc, "doc-items")],
                context =>
                {
                    var body =
                        "<form>"
                        + "<label for=\"sku\">SKU<span class=\"required\">必須</span></label><input id=\"sku\" type=\"text\">"
                        + "<label for=\"state\">販売状態<span class='required'>必須</span></label>"
                        + "<select id=\"state\" name=\"state\"><option>販売中</option><option>停止中</option></select>"
                        + "</form>"
                        + "<button>編集</button><button>編集</button><button>編集</button><button>削除</button>";

                    var doc = ExportDoc(DocOneScreen(context, "Edit.html", "編集", "", body));

                    doc.Should()
                        .Contain(
                            $"| {Strings.MockDoc_KindInput} | SKU | text / {Strings.MockDoc_NoteRequired} |"
                        );
                    doc.Should().NotContain("SKU必須");
                    doc.Should()
                        .Contain(
                            $"| {Strings.MockDoc_KindChoice} | 販売状態 | 販売中 / 停止中 / {Strings.MockDoc_NoteRequired} |"
                        );
                    var editRow = $"| {Strings.MockDoc_KindButton} | 編集 |  |";
                    doc.Should().Contain(editRow);
                    doc.IndexOf(editRow, StringComparison.Ordinal)
                        .Should()
                        .Be(doc.LastIndexOf(editRow, StringComparison.Ordinal));
                    doc.Should().Contain($"| {Strings.MockDoc_KindButton} | 削除 |  |");
                }
            ),
            Row(
                "doc-no-items-omits-item-table",
                "項目を 1 つも抜き出せない画面には項目表を出さない",
                [(MockOutputSut.DesignDoc, "doc-items")],
                context =>
                {
                    var doc = ExportDoc(
                        DocOneScreen(
                            context,
                            "Plain.html",
                            "案内",
                            "テキストだけの画面",
                            "<h1>ようこそ</h1><p>本文</p>"
                        )
                    );

                    doc.Should().NotContain(ItemTableHeader);
                }
            ),
            Row(
                "doc-crud-table-after-diagram-before-sections",
                "宣言があれば画面 × エンティティの CRUD 表を遷移図の後・最初の画面のセクションの前に出す（列は初出順・セルは正規化した操作・宣言の無い画面は空のセル）",
                [(MockOutputSut.DesignDoc, "doc-crud")],
                context =>
                {
                    var store = context.CreateStore("受注管理", "schema");
                    store.SaveStylesheet("body{}", "css");
                    store.SaveScreen(
                        "OrderList.html",
                        "注文一覧",
                        "",
                        Screen("<h1>一覧</h1>"),
                        Array.Empty<MockTransition>(),
                        "v1",
                        [new MockScreenEntity { Name = "Order", Operations = "R" }]
                    );
                    store.SaveScreen(
                        "OrderDetail.html",
                        "注文詳細",
                        "",
                        Screen("<h1>詳細</h1>"),
                        Array.Empty<MockTransition>(),
                        "v2",
                        [
                            new MockScreenEntity { Name = "Order", Operations = "urc" },
                            new MockScreenEntity { Name = "Customer", Operations = "R" },
                        ]
                    );
                    store.SaveScreen(
                        "Help.html",
                        "ヘルプ",
                        "",
                        Screen("<h1>help</h1>"),
                        Array.Empty<MockTransition>(),
                        "v3"
                    );

                    var doc = ExportDoc(store);

                    doc.Should().Contain("## " + Strings.MockDoc_CrudHeading);
                    doc.Should()
                        .Contain($"| {Strings.MockDoc_ColScreen} | Order | Customer |")
                        .And.Contain("| --- | --- | --- |");
                    doc.Should().Contain("| [注文一覧](<OrderList.html>) | R |  |");
                    doc.Should().Contain("| [注文詳細](<OrderDetail.html>) | CRU | R |");
                    doc.Should().Contain("| [ヘルプ](<Help.html>) |  |  |");
                    var crudIndex = doc.IndexOf(
                        "## " + Strings.MockDoc_CrudHeading,
                        StringComparison.Ordinal
                    );
                    var diagramIndex = doc.IndexOf(
                        "## " + Strings.MockDoc_TransitionDiagramHeading,
                        StringComparison.Ordinal
                    );
                    var firstSectionIndex = doc.IndexOf("## 注文一覧", StringComparison.Ordinal);
                    crudIndex.Should().BeGreaterThan(diagramIndex);
                    crudIndex.Should().BeLessThan(firstSectionIndex);
                }
            ),
            Row(
                "doc-no-entities-omits-crud-table",
                "宣言が 1 つも無ければ CRUD 表の見出しを出さない",
                [(MockOutputSut.DesignDoc, "doc-crud")],
                context =>
                {
                    ExportDoc(DocTwoScreenMock(context))
                        .Should()
                        .NotContain("## " + Strings.MockDoc_CrudHeading);
                }
            ),
            Row(
                "doc-crud-entity-names-merged-case-insensitively",
                "エンティティ名は大文字小文字を無視して 1 列にまとめ、見出しは初出の綴りを使う",
                [(MockOutputSut.DesignDoc, "doc-crud")],
                context =>
                {
                    var store = context.CreateStore();
                    store.SaveStylesheet("body{}", "css");
                    store.SaveScreen(
                        "A.html",
                        "A",
                        "",
                        Screen("<h1>a</h1>"),
                        Array.Empty<MockTransition>(),
                        "v1",
                        [new MockScreenEntity { Name = "Customer", Operations = "R" }]
                    );
                    store.SaveScreen(
                        "B.html",
                        "B",
                        "",
                        Screen("<h1>b</h1>"),
                        Array.Empty<MockTransition>(),
                        "v2",
                        [new MockScreenEntity { Name = "customer", Operations = "CRU" }]
                    );

                    var doc = ExportDoc(store);

                    doc.Should()
                        .Contain($"| {Strings.MockDoc_ColScreen} | Customer |")
                        .And.Contain("| --- | --- |");
                    doc.Should().NotContain("| customer |");
                    doc.Should().Contain("| [A](<A.html>) | R |");
                    doc.Should().Contain("| [B](<B.html>) | CRU |");
                }
            ),
            Row(
                "doc-newline-in-screen-name-folded-in-heading",
                "画面名の改行は空白 1 つに畳み、見出しを 1 行に収める",
                [(MockOutputSut.DesignDoc, "doc-escaping")],
                context =>
                {
                    var doc = ExportDoc(
                        DocOneScreen(context, "A.html", "注文\n一覧", "", "<h1>a</h1>")
                    );

                    doc.Should().NotContain("注文\n一覧");
                    doc.Should().Contain("## 注文 一覧\n");
                }
            ),
            Row(
                "doc-parenthesized-file-name-link-in-angle-brackets",
                "括弧を含むファイル名の画面へのリンクは山括弧で包む",
                [(MockOutputSut.DesignDoc, "doc-escaping")],
                context =>
                {
                    var doc = ExportDoc(
                        DocOneScreen(context, "Order (v2).html", "注文(v2)", "", "<h1>a</h1>")
                    );

                    doc.Should().Contain("[注文(v2)](<Order (v2).html>)");
                    doc.Should().NotContain("](Order (v2).html)");
                }
            ),
            Row(
                "doc-pipe-in-description-escaped",
                "説明のパイプは表のセルでエスケープする",
                [(MockOutputSut.DesignDoc, "doc-escaping")],
                context =>
                {
                    var doc = ExportDoc(
                        DocOneScreen(context, "A.html", "A", "一覧 | 詳細", "<h1>a</h1>")
                    );

                    doc.Should().Contain("| [A](<A.html>) | 一覧 \\| 詳細 |");
                }
            ),
            Row(
                "doc-line-endings-of-description-and-title-normalized",
                "説明と表題の改行（CRLF・U+2028）は LF にそろう（表題は 1 行の見出しに畳み、説明は段落の改行を保つ）",
                [(MockOutputSut.DesignDoc, "doc-line-endings")],
                context =>
                {
                    var crlf = ((char)13).ToString() + ((char)10).ToString();
                    var lineSeparator = ((char)0x2028).ToString();
                    var lf = ((char)10).ToString();

                    var doc = ExportDoc(
                        DocOneScreen(
                            context,
                            "A.html",
                            "A",
                            "一行目" + crlf + "二行目" + lineSeparator + "三行目",
                            "<h1>a</h1>",
                            title: "受注" + crlf + "管理"
                        )
                    );

                    doc.Should().StartWith("# 受注 管理" + lf);
                    doc.Should().Contain(lf + "一行目" + lf + "二行目" + lf + "三行目" + lf);
                }
            ),
        ];
}
