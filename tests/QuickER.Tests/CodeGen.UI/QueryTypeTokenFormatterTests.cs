using FluentAssertions;
using QuickER.CodeGen.UI;

namespace QuickER.Tests.CodeGen.UI;

/// <summary>
/// <see cref="QueryTypeTokenFormatter" />（シグネチャプレビュー用の簡易フォーマッタ）の
/// 方言中立トークン → C# 型名変換・テーブル名 → Entity クラス名変換を検証するテストクラス
/// </summary>
/// <remarks>
/// 純粋な整形ロジックのため、境界値（null・空・空白・max=-1・未知トークン・括弧付き）を中心に網羅する。
/// </remarks>
public class QueryTypeTokenFormatterTests
{
    /// <summary>既知の型トークンが対応する C# 型名へ変換されることを検証する（別名・大文字小文字を含む）</summary>
    [Theory(DisplayName = "既知トークンが C# 型名へ変換される")]
    [InlineData("int32", "int")]
    [InlineData("int", "int")]
    [InlineData("INT32", "int")] // 大文字小文字は区別しない
    [InlineData("int64", "long")]
    [InlineData("long", "long")]
    [InlineData("int16", "short")]
    [InlineData("short", "short")]
    [InlineData("byte", "byte")]
    [InlineData("string", "string")]
    [InlineData("text", "string")]
    [InlineData("decimal", "decimal")]
    [InlineData("double", "double")]
    [InlineData("float", "float")]
    [InlineData("single", "float")]
    [InlineData("boolean", "bool")]
    [InlineData("bool", "bool")]
    [InlineData("datetime", "DateTime")]
    [InlineData("datetimeoffset", "DateTimeOffset")]
    [InlineData("date", "DateOnly")]
    [InlineData("time", "TimeOnly")]
    [InlineData("guid", "Guid")]
    public void ToCSharpType_KnownToken_MapsToClrType(string token, string expected)
    {
        QueryTypeTokenFormatter.ToCSharpType(token).Should().Be(expected);
    }

    /// <summary>null・空・空白のみのトークンは <c>object</c> になることを検証する</summary>
    [Theory(DisplayName = "null・空・空白トークンは object になる")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToCSharpType_NullOrBlank_ReturnsObject(string? token)
    {
        QueryTypeTokenFormatter.ToCSharpType(token).Should().Be("object");
    }

    /// <summary>括弧付きの長さ・精度・max(-1) 指定はベーストークンで判定され、C# 型名へ変換されることを検証する</summary>
    [Theory(DisplayName = "括弧付き（長さ・精度・max=-1）はベーストークンで判定される")]
    [InlineData("string(50)", "string")]
    [InlineData("decimal(10,2)", "decimal")]
    [InlineData("decimal(10,-1)", "decimal")] // 精度 -1（max）でもベースは decimal
    [InlineData("string(-1)", "string")] // 長さ -1（max）でも string
    public void ToCSharpType_ParenthesizedToken_UsesBaseToken(string token, string expected)
    {
        QueryTypeTokenFormatter.ToCSharpType(token).Should().Be(expected);
    }

    /// <summary>未知トークンは（トリムした上で）そのまま返されることを検証する（括弧付きも保持する）</summary>
    [Theory(DisplayName = "未知トークンはトリムしてそのまま返す")]
    [InlineData("MyCustomType", "MyCustomType")]
    [InlineData("  int32  ", "int")] // 既知トークンは前後空白をトリムして解決
    [InlineData("widget(5)", "widget(5)")] // 未知トークンは括弧ごと保持
    public void ToCSharpType_UnknownToken_ReturnedTrimmedAsIs(string token, string expected)
    {
        QueryTypeTokenFormatter.ToCSharpType(token).Should().Be(expected);
    }

    /// <summary>テーブル名が PascalCase 化され Entity サフィックスが付くことを検証する（区切り文字で単語分割）</summary>
    [Theory(DisplayName = "テーブル名から Entity クラス名を生成する")]
    [InlineData("Order", "OrderEntity")]
    [InlineData("order", "OrderEntity")] // 先頭大文字化
    [InlineData("order_items", "OrderItemsEntity")] // アンダースコア区切りで単語分割
    [InlineData("customer profile", "CustomerProfileEntity")] // 空白区切り
    [InlineData("order-detail", "OrderDetailEntity")] // ハイフン区切り
    public void ToEntityClassName_PascalCasesAndAppendsSuffix(string tableName, string expected)
    {
        QueryTypeTokenFormatter.ToEntityClassName(tableName).Should().Be(expected);
    }

    /// <summary>既に Entity で終わる名前には重ねて付けないことを検証する（二重サフィックス防止）</summary>
    [Fact(DisplayName = "既に Entity で終わる名前は二重サフィックスにしない")]
    public void ToEntityClassName_AlreadyEndsWithEntity_NotDoubled()
    {
        QueryTypeTokenFormatter.ToEntityClassName("OrderEntity").Should().Be("OrderEntity");
    }

    /// <summary>空・空白のみのテーブル名は既定の <c>Entity</c> になることを検証する（分割語ゼロのフォールバック）</summary>
    [Theory(DisplayName = "空・空白・記号のみのテーブル名は Entity になる")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("___")] // 英数字を含まないと分割語ゼロ → 既定 Entity
    public void ToEntityClassName_EmptyOrSymbolOnly_ReturnsEntity(string tableName)
    {
        QueryTypeTokenFormatter.ToEntityClassName(tableName).Should().Be("Entity");
    }
}
