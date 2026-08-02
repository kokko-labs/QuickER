using System.Collections;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 固定フィクスチャの VO に対しテストプロジェクト側の partial 拡張で <c>CustomizeDisplayName</c> を実装し、
/// 上書きした表示名が EditModel の必須エラーメッセージへ自動反映されることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 対象 VO には <see cref="NameValue"/> を選んだ。フィクスチャ図に <c>name</c> 列の Description は無く、
/// この VO の既定表示名（"Name"）や <c>CustomerEditModel</c> の <c>BindingName</c> 必須メッセージ文言を
/// アサートする他テストが存在しないため、partial での上書きが他テストへ干渉しない。
/// </para>
/// <para>
/// メッセージは <c>NameValue.DisplayName</c> を参照して構築されるため、ここでの上書きがそのまま反映される。
/// </para>
/// </remarks>
public class DisplayNameCustomizeHookTests
{
    /// <summary>Customize フックで上書きした表示名が BindingName の必須エラーメッセージへ反映される</summary>
    [Fact(DisplayName = "CustomizeDisplayName の上書きが EditModel の必須メッセージへ反映される")]
    public void CustomizeDisplayName_Override_IsReflectedInRequiredMessage()
    {
        // 上書きされた表示名が静的プロパティから取得できる
        NameValue.DisplayName.Should().Be(DisplayNameCustomizeHookConstants.OverriddenName);

        // Name（VO・必須）を未設定のまま検証すると、上書き表示名を使った必須メッセージが登録される
        var model = new CustomerEditModel();

        model.Validate(includeChildren: false).Should().BeFalse();

        var errors = ((IEnumerable)model.GetErrors(nameof(CustomerEditModel.BindingName)))
            .Cast<string>()
            .ToList();

        errors
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be($"'{DisplayNameCustomizeHookConstants.OverriddenName}' is required.");
    }
}

/// <summary>テストで期待する上書き表示名を 1 か所に定義する（partial 実装とアサートで共有）</summary>
internal static class DisplayNameCustomizeHookConstants
{
    /// <summary>partial 拡張で NameValue.DisplayName へ差し替える表示名</summary>
    public const string OverriddenName = "顧客名";
}

/// <summary>
/// 固定フィクスチャの <see cref="NameValue"/> へテスト側で表示名上書きを注入する partial 実装。
/// </summary>
/// <remarks>再生成でフィクスチャ本体（.g.cs）が上書きされてもこの partial は残る（拡張ポイントの意図どおり）。</remarks>
public sealed partial class NameValue
{
    /// <summary>表示名を固定の日本語ラベルへ差し替える</summary>
    static partial void CustomizeDisplayName(ref string displayName) =>
        displayName = DisplayNameCustomizeHookConstants.OverriddenName;
}
