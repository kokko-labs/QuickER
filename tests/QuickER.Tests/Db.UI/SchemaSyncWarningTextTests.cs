using System.Reflection;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;
using DbStrings = QuickER.Db.UI.Resources.Strings;

namespace QuickER.Tests.Db.UI;

/// <summary>同期計画の警告が、種別ごとに実行確認の文言へ整形されることを検証するテストクラス</summary>
/// <remarks>
/// 実行確認の文言化（<c>SchemaSyncDialogViewModel.Describe</c>）は未知の種別を例外にする。
/// <see cref="SyncPlanWarningKind"/> へ種別を足して文言化を忘れると、その警告を出す同期の実行確認が例外で開けなくなる
/// （そもそも計画がその警告を出す形を作るテストが無いと気づけない）ので、全種別を反射で列挙して確かめる。
/// </remarks>
public class SchemaSyncWarningTextTests
{
    /// <summary>警告の文言化（private static）を呼ぶ</summary>
    private static string Describe(SyncPlanWarningKind kind, SyncPlanWarning warning) =>
        Describe(kind, warning, new SqlServerProvider().SyncCapabilities);

    /// <summary>警告の文言化（private static）を、方言のケーパビリティを指定して呼ぶ</summary>
    private static string Describe(
        SyncPlanWarningKind kind,
        SyncPlanWarning warning,
        SyncDialectCapabilities capabilities
    )
    {
        var method = typeof(SchemaSyncDialogViewModel).GetMethod(
            "Describe",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method
            .Should()
            .NotBeNull("the warning text is built by SchemaSyncDialogViewModel.Describe");

        return (string)
            method!.Invoke(null, [kind, new List<SyncPlanWarning> { warning }, capabilities])!;
    }

    /// <summary>すべての警告種別に、実行確認の文言がある</summary>
    [Fact(DisplayName = "同期計画の警告: すべての種別が実行確認の文言へ整形される")]
    public void EveryWarningKind_HasConfirmText()
    {
        foreach (var kind in Enum.GetValues<SyncPlanWarningKind>())
        {
            var text = Describe(kind, new SyncPlanWarning(kind, "orders", "detail"));

            text.Should().NotBeNullOrWhiteSpace($"{kind} needs confirm text");
        }
    }

    /// <summary>
    /// SET NULL の外部キーの NOT NULL の子の列は、作れない方言（計画から外す）と作れる方言（親の削除・更新で失敗する）で
    /// 文言を出し分ける。選ぶのはケーパビリティの宣言で、方言名ではない
    /// </summary>
    [Theory(
        DisplayName = "同期計画の警告: SET NULL の NOT NULL の子の列は、作れるかの宣言で文言を出し分ける"
    )]
    [InlineData(true)]
    [InlineData(false)]
    public void SetNullOnNotNullColumn_TextFollowsCapability(bool requiresNullable)
    {
        var capabilities = TestDialectCapabilities.Neutral with
        {
            SetNullRequiresNullableChildColumn = requiresNullable,
        };
        var warning = new SyncPlanWarning(
            SyncPlanWarningKind.SetNullOnNotNullColumn,
            "orders",
            "FK_orders_customer (customer_id)"
        );

        Describe(SyncPlanWarningKind.SetNullOnNotNullColumn, warning, capabilities)
            .Should()
            .Be(
                string.Format(
                    requiresNullable
                        ? DbStrings.SchemaSync_ExecuteConfirmSetNullOnNotNullColumnExcluded
                        : DbStrings.SchemaSync_ExecuteConfirmSetNullOnNotNullColumn,
                    "  • orders / FK_orders_customer (customer_id)"
                )
            );
    }

    /// <summary>選んでいない変更に依存して外した項目は、表と「対象 (足りないもの)」を箇条書きで名指しする</summary>
    [Fact(
        DisplayName = "同期計画の警告: 選んでいない変更に依存して外した項目を表と対象で名指しする"
    )]
    public void SelectedItemRequiresUnselected_ListsTableAndDetail()
    {
        var text = Describe(
            SyncPlanWarningKind.SelectedItemRequiresUnselectedItem,
            new SyncPlanWarning(
                SyncPlanWarningKind.SelectedItemRequiresUnselectedItem,
                "orders",
                "FK_orders_customer (orders.customer_ref)"
            )
        );

        text.Should()
            .Be(
                string.Format(
                    DbStrings.SchemaSync_ExecuteConfirmSelectedItemRequiresUnselected,
                    "  • orders / FK_orders_customer (orders.customer_ref)"
                )
            );
    }
}
