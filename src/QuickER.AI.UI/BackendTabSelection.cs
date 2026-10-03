using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickER.AI;

namespace QuickER.AI.UI;

/// <summary>
/// 接続方式タブの選択（TwoWay 束縛の相手）と、接続方式の確定（<see cref="ChatConnectionSettingsViewModel.SelectedBackend"/>）を仲介する共通 VM 部品。
/// </summary>
/// <remarks>
/// <para>
/// 切り替えは押した後に断られることがある（会話を消してよいかの確認をキャンセルしたとき）。
/// そのときタブの表示を元へ戻すため、選択はいったん暫定値として受け入れてタブを実際に切替先へ動かし、
/// 確定できなかったら暫定値を捨てて再通知する（束縛が「切替先 → 元の方式」の実変更として転送するので、
/// タブの内部の選択状態ごと戻る）。選択を受け取った時点で値を捨てると、ソースは元のままなのにタブだけが
/// 切替先へ進んで食い違い、後から何を通知しても変化なしと見なされて戻らない。
/// </para>
/// <para>
/// 確定（確認モーダルを含む）は <see cref="SetUiPost"/> で渡した投函で後置する。束縛のソース更新は
/// タブの選択変更処理の内側で走るため、そこで直接モーダルを開くと、確認中に回るネストしたメッセージループが
/// 選択変更処理と交錯する。投函の既定は同期実行（ダイアログが配線する）。
/// </para>
/// <para>
/// 押す前から切り替えられない状況（ターン実行中など）は、親 VM の可否でタブ自体を無効化する。
/// ここで扱う「断られる」は、押した後に利用者が決める確認のキャンセルと、二重の守りとしての親 VM の拒否だけ。
/// </para>
/// </remarks>
public sealed class BackendTabSelection : ObservableObject
{
    private readonly ChatConnectionSettingsViewModel _connection;
    private readonly Func<ErChatBackendKind, bool> _tryChange;
    private Action<Action> _uiPost = action => action();

    /// <summary>確定を待っている暫定の選択（無ければ null）</summary>
    private ErChatBackendKind? _pending;

    /// <summary>接続方式の状態部品と、切替の試行を結び付けて生成する</summary>
    /// <param name="connection">確定した接続方式を持つ状態部品</param>
    /// <param name="tryChange">
    /// 切替を試みる処理（親 VM の TryChangeBackend）。切り替えた（または既に同じ）なら true、断ったなら false
    /// </param>
    public BackendTabSelection(
        ChatConnectionSettingsViewModel connection,
        Func<ErChatBackendKind, bool> tryChange
    )
    {
        _connection = connection;
        _tryChange = tryChange;
        // 確定した接続方式の変化（初期タブの復元・確定の結果）をタブへ中継する
        _connection.PropertyChanged += OnConnectionPropertyChanged;
    }

    /// <summary>タブに表示する接続方式（確定待ちの暫定値があればそれ、無ければ確定した接続方式）</summary>
    public ErChatBackendKind Selected
    {
        get => _pending ?? _connection.SelectedBackend;
        set
        {
            if (value == Selected)
            {
                return;
            }

            // 選択を暫定的に受け入れる（タブの内部状態と束縛のソースを一致させておく）
            _pending = value;
            OnPropertyChanged();
            _uiPost(() => Commit(value));
        }
    }

    /// <summary>確定を後置する投函の仕組みを差し替える（ダイアログが UI スレッドのディスパッチャを配線する）</summary>
    /// <param name="uiPost">渡した処理を、いま処理中のイベントが終わった後で UI スレッド上に実行する投函</param>
    public void SetUiPost(Action<Action> uiPost) => _uiPost = uiPost;

    /// <summary>暫定の選択を確定する。断られたら暫定値を捨ててタブを確定した接続方式へ戻す</summary>
    /// <param name="value">確定する接続方式</param>
    private void Commit(ErChatBackendKind value)
    {
        // 確定を待つ間に別のタブが選ばれていたら、最後の選択だけを確定する
        if (_pending != value)
        {
            return;
        }

        try
        {
            _tryChange(value);
        }
        finally
        {
            // 成否に関わらず暫定値を捨てる。断られたときは、この通知でタブが元の接続方式へ戻る
            _pending = null;
            OnPropertyChanged(nameof(Selected));
        }
    }

    /// <summary>確定した接続方式が変わったら、タブの表示へ中継する</summary>
    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConnectionSettingsViewModel.SelectedBackend))
        {
            OnPropertyChanged(nameof(Selected));
        }
    }
}
