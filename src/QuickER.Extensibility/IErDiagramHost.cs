using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Extensibility;

/// <summary>
/// ホストアプリ（QuickER.Gui）が着脱可能な機能モジュールへ提供する、ER 図に対する操作能力の契約。
/// </summary>
/// <remarks>
/// <para>
/// この契約は依存方向を「プラグイン → 契約 ← ホスト」に逆転させるための切断面である。
/// <b>実装はホスト側</b>（QuickER.Gui の MainViewModel を包むアダプタ）に置き、
/// <b>消費はフィーチャーモジュール側</b>（QuickER.AI.Chat / QuickER.AI.Mock）が行う。
/// これにより機能モジュールは巨大なアプリ本体の具象（MainViewModel）を知らずに ER 図を操作できる。
/// </para>
/// <para>
/// <see cref="ExecuteTool"/> は <see cref="Model.Entity"/> 等の可変状態を書き換えるため、
/// 必ず UI スレッド上で呼び出すこと。
/// </para>
/// </remarks>
public interface IErDiagramHost
{
    /// <summary>ダイアグラムにエンティティが 1 つも無いかどうか（ターン開始時の空判定に使用）</summary>
    bool IsEmpty { get; }

    /// <summary>図に未保存の変更があるかどうか（図の内容を失う操作の確認で警告水準の選択に使用）</summary>
    bool IsDirty { get; }

    /// <summary>現在の ER 図を意味モデル（<see cref="ErDiagram"/>・視覚情報なし）として取得する</summary>
    ErDiagram GetDiagram();

    /// <summary>型解決などに使う DB プロバイダレジストリ</summary>
    DatabaseProviderRegistry Providers { get; }

    /// <summary>新規生成されたダイアグラムを自動整列する</summary>
    void AutoArrangeNewDiagram();

    /// <summary>
    /// ER 図操作ツールを名前と引数 JSON で実行し、結果テキストと成否を返す。
    /// </summary>
    /// <remarks>ER 図の可変状態を変更するため、必ず UI スレッドで呼び出すこと。</remarks>
    /// <param name="toolName">実行するツールの名前</param>
    /// <param name="argumentsJson">ツールへ渡す引数の JSON 文字列</param>
    /// <returns>結果テキストと、成功したかどうかのタプル</returns>
    (string Result, bool Success) ExecuteTool(string toolName, string argumentsJson);

    /// <summary>カラム名がユーザー編集で変更されたときに発火する（名前付きクエリの条件式追従などに使用）</summary>
    event EventHandler<ColumnRenamedEventArgs>? ColumnRenamed;

    /// <summary>図の名前付きクエリ定義を丸ごと差し替える。差し替え後の自動保存はホスト実装の責務</summary>
    /// <param name="queries">差し替える名前付きクエリ定義の一覧</param>
    void ReplaceQueries(IReadOnlyList<QueryDefinition> queries);

    /// <summary>図を丸ごと差し替える（DB 取込などの用途）。</summary>
    /// <remarks>
    /// <see cref="ErDiagram.TargetDbms"/> の方言採用・Undo 履歴のクリア・自動整列・画面フィット要求まで含めてホスト実装の責務。
    /// </remarks>
    /// <param name="diagram">差し替える図の意味モデル</param>
    void ReplaceDiagram(ErDiagram diagram);

    /// <summary>現在の対象 DBMS 識別子（<see cref="GetDiagram"/> の全材料化を避けた軽量読み取り）</summary>
    string TargetDbms { get; }

    /// <summary>対象 DBMS が切り替わったときに発火する（ツールバーボタンの活性・ツールチップ再評価などに使用）</summary>
    event EventHandler? TargetDbmsChanged;
}
