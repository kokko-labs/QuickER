using QuickER.AI;

namespace QuickER.AI.Chat;

/// <summary>
/// AI チャット ViewModel が操作対象のダイアグラムに対して必要とする最小限の能力を抽象化するインターフェース。
/// </summary>
/// <remarks>
/// <see cref="AiChatDialogViewModel"/> を巨大なアプリ本体の MainViewModel 具象から切り離し、
/// スタブ注入による単体テストを可能にする。AI のツール実行は <see cref="IErDiagramToolHost"/> が担う。
/// 具象アダプタ（MainViewModelChatHost）はアプリ本体（QuickER.Gui）に置く。
/// </remarks>
public interface IErDiagramChatHost
{
    /// <summary>AI のツール呼び出しをダイアグラム操作へ橋渡しするホスト</summary>
    IErDiagramToolHost ToolHost { get; }

    /// <summary>ダイアグラムにエンティティが 1 つも無いかどうか（ターン開始時の空判定に使用）</summary>
    bool IsEmpty { get; }

    /// <summary>新規生成されたダイアグラムを自動整列する</summary>
    void AutoArrangeNewDiagram();
}
