using System.Globalization;
using System.IO;
using FluentAssertions;
using QuickER.AI.Mock;
using QuickER.AI.Mock.Resources;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockDesignDocExporter"/> の決定的な設計書 Markdown 生成
/// （タイトル・画面一覧・mermaid 遷移図・画面ごとの項目抽出）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 期待文字列のうち見出し・種別語などのローカライズ部分は <see cref="Strings"/> の
/// <c>MockDoc_*</c> プロパティ参照で組み立て、resx の実値へハードコード依存しない。
/// カルチャを切り替えるグローバル静的（<c>Strings.Culture</c> 等）は変更しない。
/// </remarks>
public class MockDesignDocExporterTests : IDisposable
{
    private readonly string _folder;

    public MockDesignDocExporterTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>style.css を参照する最小の完全 HTML を組み立てる</summary>
    private static string Screen(string body) =>
        "<!DOCTYPE html><html><head><link rel=\"stylesheet\" href=\"style.css\"></head><body>"
        + body
        + "</body></html>";

    /// <summary>2 画面＋遷移ありの標準的なモックを作る</summary>
    private MockFolderStore BuildTwoScreenMock()
    {
        var store = MockFolderStore.CreateNew(_folder, "受注管理", "schema");

        store.SaveStylesheet("body { color: #222; }", "css");
        store.SaveScreen(
            "OrderList.html",
            "注文一覧",
            "注文の一覧を表示する画面",
            Screen("<h1>注文一覧</h1><a href=\"OrderDetail.html\">詳細へ</a>"),
            new[]
            {
                new MockTransition
                {
                    From = "OrderList.html",
                    To = "OrderDetail.html",
                    Trigger = "行クリック",
                },
            },
            "v1"
        );
        store.SaveScreen(
            "OrderDetail.html",
            "注文詳細",
            "",
            Screen("<h1>注文詳細</h1><a href=\"OrderList.html\">戻る</a>"),
            new[]
            {
                new MockTransition { From = "OrderDetail.html", To = "OrderList.html" },
            },
            "v2"
        );

        return store;
    }

    [Fact(DisplayName = "タイトル・画面一覧・遷移図・画面セクションが出力される")]
    public void Export_ProducesCoreStructure()
    {
        var store = BuildTwoScreenMock();

        var doc = MockDesignDocExporter.Export(store);

        // タイトル
        doc.Should().Contain("# 受注管理");

        // 画面一覧（見出し＋リンク付きの表）
        doc.Should().Contain("## " + Strings.MockDoc_ScreenListHeading);
        doc.Should().Contain($"| {Strings.MockDoc_ColScreen} | {Strings.MockDoc_ColDescription} |");
        doc.Should().Contain("| [注文一覧](OrderList.html) | 注文の一覧を表示する画面 |");
        doc.Should().Contain("| [注文詳細](OrderDetail.html) |  |");

        // 遷移図（mermaid・全画面ノード＋トリガー付きエッジ）
        doc.Should().Contain("## " + Strings.MockDoc_TransitionDiagramHeading);
        doc.Should().Contain("```mermaid");
        doc.Should().Contain("flowchart LR");
        doc.Should().Contain("OrderList[\"注文一覧\"]");
        doc.Should().Contain("OrderDetail[\"注文詳細\"]");
        doc.Should().Contain("OrderList -->|行クリック| OrderDetail");
        doc.Should().Contain("OrderDetail --> OrderList");

        // 画面セクション（説明・遷移先／元）
        doc.Should().Contain("## 注文一覧");
        doc.Should().Contain("注文の一覧を表示する画面");
        // 括弧書式は UI 言語追従（resx）なので、期待値も同じ書式で組み立てる
        var trigger = string.Format(Strings.MockDoc_TriggerFormat, "行クリック");
        doc.Should()
            .Contain($"- {Strings.MockDoc_TransitionTo}: [注文詳細](OrderDetail.html){trigger}");
        doc.Should()
            .Contain($"- {Strings.MockDoc_TransitionFrom}: [注文一覧](OrderList.html){trigger}");
    }

    [Fact(DisplayName = "同じ入力からは 2 回とも同一の Markdown を返す（決定的）")]
    public void Export_IsDeterministic()
    {
        var store = BuildTwoScreenMock();

        var first = MockDesignDocExporter.Export(store);
        var second = MockDesignDocExporter.Export(store);

        second.Should().Be(first);
    }

    [Fact(DisplayName = "タイトルが空なら既定タイトルを使う")]
    public void Export_UsesDefaultTitleWhenBlank()
    {
        var store = MockFolderStore.CreateNew(_folder, "", "schema");
        store.SaveStylesheet("body{}", "css");
        store.SaveScreen(
            "A.html",
            "A",
            "",
            Screen("<h1>a</h1>"),
            Array.Empty<MockTransition>(),
            "v1"
        );

        var doc = MockDesignDocExporter.Export(store);

        doc.Should().Contain("# " + Strings.MockDoc_DefaultTitle);
    }

    [Fact(DisplayName = "mermaid ラベルの引用符をエスケープし、ID 衝突は連番で一意化する")]
    public void Export_EscapesMermaidLabelAndDisambiguatesIds()
    {
        var store = MockFolderStore.CreateNew(_folder, "t", "s");
        store.SaveStylesheet("body{}", "css");

        // ファイル名 "Order-Detail.html" と "Order Detail.html" はどちらも ID 素が "Order_Detail" になり衝突する
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

        var doc = MockDesignDocExporter.Export(store);

        // mermaid ラベル内の引用符は &quot; へエスケープされる（生の引用符では出ない）
        doc.Should().Contain("Order_Detail[\"詳細&quot;A&quot;\"]");
        doc.Should().NotContain("[\"詳細\"A\"\"]");
        // 衝突した 2 つ目は連番付き ID になる
        doc.Should().Contain("Order_Detail_2[\"詳細B\"]");
    }

    [Fact(DisplayName = "実在しない画面への遷移はエッジに現れない")]
    public void Export_SkipsTransitionsToUnknownScreens()
    {
        var store = MockFolderStore.CreateNew(_folder, "t", "s");
        store.SaveStylesheet("body{}", "css");
        store.SaveScreen(
            "A.html",
            "A",
            "",
            Screen("<h1>a</h1>"),
            // To が実在しない画面（Ghost.html）を指す遷移
            new[]
            {
                new MockTransition { From = "A.html", To = "Ghost.html" },
            },
            "v1"
        );

        var doc = MockDesignDocExporter.Export(store);

        doc.Should().NotContain("Ghost");
        // A のノード宣言はあるがエッジ（-->）は無い
        doc.Should().Contain("A[\"A\"]");
        doc.Should().NotContain("-->");
    }

    [Fact(DisplayName = "代表的な HTML から画面項目を抽出する")]
    public void Export_ExtractsScreenItems()
    {
        var store = MockFolderStore.CreateNew(_folder, "t", "s");
        store.SaveStylesheet("body{}", "css");

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
            + "<input type=\"submit\" value=\"検索\">"
            + "<a class=\"btn btn-primary\" href=\"Next.html\">次へ</a>"
            + "<a href=\"Plain.html\">素のリンク</a>"
            + "<table><thead><tr><th>ID</th><th>名前</th><th>状態</th></tr></thead>"
            + "<tbody><tr><td>1</td><td>a</td><td>x</td></tr></tbody></table>"
            + "<script>console.log('<input type=\"text\" name=\"ghost\">');</script>"
            + "</form>";

        store.SaveScreen(
            "Form.html",
            "入力フォーム",
            "",
            Screen(body),
            Array.Empty<MockTransition>(),
            "v1"
        );

        var doc = MockDesignDocExporter.Export(store);

        // 項目表ヘッダ
        doc.Should()
            .Contain(
                $"| {Strings.MockDoc_ColKind} | {Strings.MockDoc_ColItem} | {Strings.MockDoc_ColNote} |"
            );

        // 入力欄（label for / 包含 label / placeholder / textarea）
        doc.Should()
            .Contain(
                $"| {Strings.MockDoc_KindInput} | 氏名 | text / {Strings.MockDoc_NoteRequired} |"
            );
        doc.Should().Contain($"| {Strings.MockDoc_KindInput} | メール | email / you@example.com |");
        doc.Should().Contain($"| {Strings.MockDoc_KindInput} | キーワード検索 | text |");
        doc.Should()
            .Contain(
                $"| {Strings.MockDoc_KindInput} | 備考を入力 | {Strings.MockDoc_NoteMultiline} |"
            );

        // 選択肢（select は先頭 3 件＋…、radio グループは 1 行）
        doc.Should().Contain($"| {Strings.MockDoc_KindChoice} | pref | 東京 / 大阪 / 京都 … |");

        var radioNote =
            Strings.MockDoc_NoteRadio
            + " / "
            + string.Format(CultureInfo.InvariantCulture, Strings.MockDoc_NoteOptionCountFormat, 2);
        doc.Should().Contain($"| {Strings.MockDoc_KindChoice} | gender | {radioNote} |");

        // ボタン（<button> / submit / a.btn）
        doc.Should().Contain($"| {Strings.MockDoc_KindButton} | 登録 |  |");
        doc.Should().Contain($"| {Strings.MockDoc_KindButton} | 検索 |  |");
        doc.Should().Contain($"| {Strings.MockDoc_KindButton} | 次へ |  |");

        // テーブル列
        var tableItem = string.Format(
            CultureInfo.InvariantCulture,
            Strings.MockDoc_ItemTableFormat,
            1
        );
        doc.Should()
            .Contain($"| {Strings.MockDoc_KindTableColumn} | {tableItem} | ID / 名前 / 状態 |");

        // 拾わないもの: hidden・script 内の擬似 input・素のリンク
        doc.Should().NotContain("token");
        doc.Should().NotContain("ghost");
        doc.Should().NotContain("console.log");
        doc.Should().NotContain("素のリンク");
    }

    [Fact(
        DisplayName = "必須マーカー span は項目名から除いて備考へ移し、同一ボタンは 1 行へ集約する"
    )]
    public void Export_RequiredMarkerAndButtonDedup()
    {
        var store = MockFolderStore.CreateNew(_folder, "t", "s");
        store.SaveStylesheet("body{}", "css");

        // モック CSS の定番パターン: <label>名前<span class="required">必須</span></label>（' 引用の class も対象）
        var body =
            "<form>"
            + "<label for=\"sku\">SKU<span class=\"required\">必須</span></label><input id=\"sku\" type=\"text\">"
            + "<label for=\"state\">販売状態<span class='required'>必須</span></label>"
            + "<select id=\"state\" name=\"state\"><option>販売中</option><option>停止中</option></select>"
            + "</form>"
            + "<button>編集</button><button>編集</button><button>編集</button><button>削除</button>";

        store.SaveScreen(
            "Edit.html",
            "編集",
            "",
            Screen(body),
            Array.Empty<MockTransition>(),
            "v1"
        );

        var doc = MockDesignDocExporter.Export(store);

        // 必須マーカーは項目名に連結されず（「SKU必須」にならない）、備考の必須語へ移る
        doc.Should()
            .Contain(
                $"| {Strings.MockDoc_KindInput} | SKU | text / {Strings.MockDoc_NoteRequired} |"
            );
        doc.Should().NotContain("SKU必須");
        doc.Should()
            .Contain(
                $"| {Strings.MockDoc_KindChoice} | 販売状態 | 販売中 / 停止中 / {Strings.MockDoc_NoteRequired} |"
            );

        // 行ごとの同一テキストのボタンは 1 行へ集約され、異なるテキストのボタンは残る
        var editRow = $"| {Strings.MockDoc_KindButton} | 編集 |  |";
        doc.Should().Contain(editRow);
        doc.IndexOf(editRow, StringComparison.Ordinal)
            .Should()
            .Be(doc.LastIndexOf(editRow, StringComparison.Ordinal));
        doc.Should().Contain($"| {Strings.MockDoc_KindButton} | 削除 |  |");
    }

    [Fact(DisplayName = "抽出項目がゼロの画面は項目表を出さない")]
    public void Export_OmitsItemTableWhenNoItems()
    {
        var store = MockFolderStore.CreateNew(_folder, "t", "s");
        store.SaveStylesheet("body{}", "css");
        store.SaveScreen(
            "Plain.html",
            "案内",
            "テキストだけの画面",
            Screen("<h1>ようこそ</h1><p>本文</p>"),
            Array.Empty<MockTransition>(),
            "v1"
        );

        var doc = MockDesignDocExporter.Export(store);

        // 項目表ヘッダ（種別／項目／備考の 3 列）は出力されない
        doc.Should()
            .NotContain(
                $"| {Strings.MockDoc_ColKind} | {Strings.MockDoc_ColItem} | {Strings.MockDoc_ColNote} |"
            );
    }

    [Fact(DisplayName = "説明に含まれるパイプは表セルでエスケープされる")]
    public void Export_EscapesPipeInDescriptionCell()
    {
        var store = MockFolderStore.CreateNew(_folder, "t", "s");
        store.SaveStylesheet("body{}", "css");
        store.SaveScreen(
            "A.html",
            "A",
            "一覧 | 詳細",
            Screen("<h1>a</h1>"),
            Array.Empty<MockTransition>(),
            "v1"
        );

        var doc = MockDesignDocExporter.Export(store);

        // 画面一覧の説明セルでパイプがエスケープされる
        doc.Should().Contain("| [A](A.html) | 一覧 \\| 詳細 |");
    }
}
