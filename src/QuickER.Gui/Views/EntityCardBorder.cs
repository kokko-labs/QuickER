using System.Collections.Generic;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using QuickER.ViewModels;

namespace QuickER.Views;

/// <summary>
/// エンティティカードのルート要素。UI オートメーションへは「テーブル名を持つ単一の Group」として
/// 露出し、内部のカラム行（TextBlock 群）をオートメーションツリーから隠す
/// </summary>
/// <remarks>
/// 大きい図（50 テーブル×数十カラム）ではカラム行だけで UIA 要素が 1 万を超え、
/// スクリーンリーダーや PowerToys 等の常駐 UIA クライアントが最初のポップアップ表示を契機に
/// ツリーを走査すると、その応答（UI スレッドで処理される）に数秒かかり操作が固まる。
/// カード内部を単一ピアに畳むことで走査対象を約 95% 削減する。
/// カラム内容へのアクセシビリティはプロパティパネル（DataGrid）側が引き続き提供する。
/// </remarks>
public class EntityCardBorder : Border
{
    /// <summary>このカードを単一ピアとして露出するオートメーションピアを生成する</summary>
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new EntityCardAutomationPeer(this);

    /// <summary>子要素を持たない（内部を隠す）エンティティカード用のオートメーションピア</summary>
    private sealed class EntityCardAutomationPeer : FrameworkElementAutomationPeer
    {
        public EntityCardAutomationPeer(EntityCardBorder owner)
            : base(owner) { }

        /// <summary>内部のカラム行等を UIA ツリーへ露出しない（走査コスト削減の本体）</summary>
        protected override List<AutomationPeer>? GetChildrenCore() => null;

        /// <summary>カードの読み上げ名としてテーブル名を返す</summary>
        protected override string GetNameCore() =>
            Owner is EntityCardBorder { DataContext: EntityViewModel vm }
                ? vm.TableName
                : base.GetNameCore();

        /// <summary>カードはグループ要素として扱う</summary>
        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.Group;
    }
}
