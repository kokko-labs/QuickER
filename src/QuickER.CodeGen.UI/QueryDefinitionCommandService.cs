using QuickER.Extensibility;

namespace QuickER.CodeGen.UI;

/// <summary>
/// 名前付きクエリ定義エディタを開き、確定結果で現在の図のクエリを置き換えるコマンドサービス。
/// </summary>
/// <remarks>
/// アプリ本体 <c>MainViewModel</c> の <c>OpenQueryDefinitions</c> から移設したフィーチャーモジュール本体。
/// 編集はダイアログ側の複製に対して行われ、OK 確定時のみ結果が返る（キャンセルは無影響）。
/// 置換後の自動保存は <see cref="IErDiagramHost.ReplaceQueries"/> のホスト実装が担う。
/// </remarks>
public sealed class QueryDefinitionCommandService
{
    /// <summary>ER 図の取得・クエリ差し替えを提供するホスト契約</summary>
    private readonly IErDiagramHost _host;

    /// <summary>名前付きクエリ定義エディタの提示シーム</summary>
    private readonly IQueryDefinitionDialogPresenter _presenter;

    /// <summary>依存を注入して生成する</summary>
    public QueryDefinitionCommandService(
        IErDiagramHost host,
        IQueryDefinitionDialogPresenter presenter
    )
    {
        _host = host;
        _presenter = presenter;
    }

    /// <summary>名前付きクエリ定義エディタを開き、確定結果で現在の図のクエリを置き換える（ツールバーボタンから実行）</summary>
    public void Run()
    {
        var result = _presenter.Show(_host.GetDiagram());

        if (result is null)
        {
            return;
        }

        _host.ReplaceQueries(result);
    }
}
