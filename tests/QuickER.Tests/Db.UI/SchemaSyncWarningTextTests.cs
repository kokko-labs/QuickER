using System.Reflection;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Provider;
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
    private static string Describe(SyncPlanWarningKind kind, SyncPlanWarning warning)
    {
        var method = typeof(SchemaSyncDialogViewModel).GetMethod(
            "Describe",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method
            .Should()
            .NotBeNull("the warning text is built by SchemaSyncDialogViewModel.Describe");

        return (string)method!.Invoke(null, [kind, new List<SyncPlanWarning> { warning }])!;
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
