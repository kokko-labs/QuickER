using AwesomeAssertions;
using QuickER.Provider;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 図の名前（テーブル名・列名・制約名）が SQL へ出せる形かの判定（<see cref="SqlNameText.IsSafe"/>）の公開 API の契約を検証するテストクラス
/// </summary>
/// <remarks>
/// <para>
/// 止めるのは改行・制御文字だけで、<c>"</c> や <c>]</c> や <c>'</c> は対象にしない（DB 取込で実在し得る名前で、
/// 出力側のエスケープで正しく扱える）。C# コード生成側の同じ線引きは生成前診断の Error として実装されている
/// （<c>CodegenCaseTests</c> の行 <c>diag-name-control-character-*</c>）。
/// </para>
/// <para>
/// 改行入りの名前が DDL・同期スクリプトへ 1 行も出ずに名指しで断られることは、方言の DDL の表の行
/// <c>ddl-{table,column,unique-constraint,foreign-key}-name-with-newline-is-rejected</c>・
/// <c>sync-add-table-table-name-with-newline-is-rejected</c>・<c>sync-rebuild-table-name-with-newline-is-rejected</c>・
/// <c>sync-other-alter-column-name-with-newline-is-rejected</c> が、引用符を含むだけの名前を通すことは行
/// <c>ddl-quote-apostrophe-table-and-double-quote-column-accepted</c> が固定する。
/// </para>
/// </remarks>
public class SqlNameTextTests
{
    [Theory(DisplayName = "改行・制御文字を含む名前は安全でない")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [InlineData("a\tb")]
    [InlineData("a\u0000b")]
    [InlineData("a\u007Fb")]
    // C# / 表示系が改行として扱う Unicode の行区切り（C0 の外側なので明示的に含める）
    [InlineData("a\u0085b")]
    [InlineData("a\u2028b")]
    [InlineData("a\u2029b")]
    public void IsSafe_ControlCharacterInName_Rejected(string name) =>
        SqlNameText.IsSafe(name).Should().BeFalse();

    [Theory(DisplayName = "クォート・記号を含むだけの名前は安全（DB 取込で実在し得る）")]
    [InlineData("orders")]
    [InlineData("o'brien")]
    [InlineData("we\"ird")]
    [InlineData("a]b")]
    [InlineData("sales.orders")]
    [InlineData("注文")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSafe_QuotesAndSymbols_Pass(string? name) =>
        SqlNameText.IsSafe(name).Should().BeTrue();
}
