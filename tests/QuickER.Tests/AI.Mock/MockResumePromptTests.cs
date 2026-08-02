using System.Globalization;
using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockResumePrompt"/> の再開プロンプト組立とスキーマ差異判定を検証するテストクラス。
/// </summary>
/// <remarks>
/// 再開プロンプトの見出し・注記は resx 対訳（表示言語追従）になったため、日本語文言の含有を検証する
/// テストは <see cref="WithCulture{T}"/> で <c>ja</c> カルチャに一時固定して実行する
/// （静的 <c>Strings.Culture</c> は変更せず <see cref="CultureInfo.CurrentUICulture"/> を try/finally で復元）。
/// </remarks>
public class MockResumePromptTests
{
    /// <summary>指定カルチャを CurrentUICulture に設定して関数を評価し、必ず元へ復元する</summary>
    private static T WithCulture<T>(string culture, Func<T> body)
    {
        var previousUi = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            return body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private static MockManifest SampleManifest(string schema = "# schema") =>
        new()
        {
            Title = "受注管理",
            SourceSchema = schema,
            Screens =
            {
                new MockScreen
                {
                    File = "OrderList.html",
                    Name = "注文一覧",
                    Description = "注文の一覧",
                },
                new MockScreen
                {
                    File = "OrderDetail.html",
                    Name = "注文詳細",
                    Description = "1 注文の詳細",
                },
            },
            Transitions =
            {
                new MockTransition
                {
                    From = "OrderList.html",
                    To = "OrderDetail.html",
                    Trigger = "行クリック",
                },
            },
            Revisions =
            {
                new MockRevision
                {
                    Timestamp = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero),
                    Note = "初版作成",
                },
            },
        };

    [Fact(DisplayName = "画面・遷移・履歴・スキーマを含む")]
    public void Build_IncludesScreensTransitionsRevisionsSchema()
    {
        var prompt = WithCulture(
            "ja",
            () => MockResumePrompt.Build("# 現在スキーマ", SampleManifest(), schemaChanged: false)
        );

        prompt.Should().Contain("OrderList.html");
        prompt.Should().Contain("注文一覧");
        prompt.Should().Contain("OrderList.html → OrderDetail.html");
        prompt.Should().Contain("行クリック");
        prompt.Should().Contain("初版作成");
        prompt.Should().Contain("# 現在スキーマ");
        prompt.Should().Contain("get_screen");
    }

    [Fact(DisplayName = "宣言済み画面はエンティティ＋CRUD・未宣言は未宣言と注入する")]
    public void Build_InjectsEntityDeclarationState()
    {
        var manifest = SampleManifest();
        // OrderList は宣言あり・OrderDetail は宣言なし（未宣言）
        manifest.Screens[0].Entities = new()
        {
            new MockScreenEntity { Name = "Order", Operations = "CRU" },
            new MockScreenEntity { Name = "Customer", Operations = "R" },
        };

        var prompt = WithCulture(
            "ja",
            () => MockResumePrompt.Build("# 現在スキーマ", manifest, schemaChanged: false)
        );

        // 宣言済み: エンティティ＋CRUD が Name(OPS) 形式で入る
        prompt.Should().Contain("エンティティ: Order(CRU), Customer(R)");
        // 未宣言画面には未宣言の注記が入る
        prompt.Should().Contain("（未宣言）");
    }

    [Fact(DisplayName = "schemaChanged=true で差異注記を含む")]
    public void Build_WithSchemaChanged_IncludesNote()
    {
        var prompt = WithCulture(
            "ja",
            () => MockResumePrompt.Build("# 現在スキーマ", SampleManifest(), schemaChanged: true)
        );

        prompt.Should().Contain("スキーマが変更されています");
    }

    [Fact(DisplayName = "schemaChanged=false で差異注記を含まない")]
    public void Build_WithoutSchemaChange_OmitsNote()
    {
        var prompt = WithCulture(
            "ja",
            () => MockResumePrompt.Build("# 現在スキーマ", SampleManifest(), schemaChanged: false)
        );

        prompt.Should().NotContain("スキーマが変更されています");
    }

    [Fact(DisplayName = "IsSchemaChanged は同一内容を変更なしと判定する")]
    public void IsSchemaChanged_SameContent_False()
    {
        var manifest = SampleManifest("# データベーススキーマ\n本文");

        MockResumePrompt
            .IsSchemaChanged("# データベーススキーマ\n本文", manifest)
            .Should()
            .BeFalse();
    }

    [Fact(DisplayName = "IsSchemaChanged は改行差（CRLF/LF）を吸収する")]
    public void IsSchemaChanged_NewlineDifference_False()
    {
        var manifest = SampleManifest("line1\r\nline2");

        MockResumePrompt.IsSchemaChanged("line1\nline2", manifest).Should().BeFalse();
    }

    [Fact(DisplayName = "IsSchemaChanged は実質的な差異を変更ありと判定する")]
    public void IsSchemaChanged_RealDifference_True()
    {
        var manifest = SampleManifest("# 旧スキーマ");

        MockResumePrompt.IsSchemaChanged("# 新スキーマ", manifest).Should().BeTrue();
    }
}
