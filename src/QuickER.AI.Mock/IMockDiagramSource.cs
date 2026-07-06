using QuickER.Model;
using QuickER.Provider;

namespace QuickER.AI.Mock;

/// <summary>
/// モック生成 ViewModel が必要とする「現在の ER 図」の供給元を抽象化するインターフェース。
/// </summary>
/// <remarks>
/// <see cref="MockGenerationDialogViewModel"/> をアプリ本体の巨大な MainViewModel 具象から切り離し、
/// スタブ注入による単体テストを可能にする（AI チャットの IErDiagramChatHost と同じ発想）。
/// 具象アダプタ（MainViewModelMockDiagramSource）はアプリ本体（QuickER.Gui）に置く。
/// </remarks>
public interface IMockDiagramSource
{
    /// <summary>ダイアグラムにエンティティが 1 つも無いか（生成開始の空判定に使用）</summary>
    bool IsEmpty { get; }

    /// <summary>現在の ER 図を意味モデル（<see cref="ErDiagram"/>・視覚情報なし）として取得する</summary>
    ErDiagram GetDiagram();

    /// <summary>WPF モック生成の型解決に使う DB プロバイダレジストリ</summary>
    DatabaseProviderRegistry Providers { get; }
}
