using System.ComponentModel;
using QuickER.AI;

namespace QuickER.AI.UI;

/// <summary>
/// 接続方式が切り替わったときに、その方式の接続状態の確認（接続・初期化）を起動する共通部品。
/// 「再確認」の実行も同じ失敗の受け止め方で通す。
/// </summary>
/// <remarks>
/// <para>
/// 起動の契機は確定した接続方式の変化（<see cref="ChatConnectionSettingsViewModel.SelectedBackend"/>）で、
/// タブの操作と初期タブの復元のどちらからでも同じ経路で走る。API キー接続は確認するものが無いので何もしない。
/// </para>
/// <para>
/// 確認は完了を待たずに起動する（タブの切り替えを止めない）。失敗はすべて <c>reportFailure</c> へ渡し、
/// 例外をウィンドウの外へ漏らさない（受け止めないと、投げっぱなしの起動では例外が観測されずに消え、
/// 「再確認」のコマンドではディスパッチャへ投げ直されてアプリが落ちる）。
/// 素早い往復で確認が重なっても、エンジンの接続・初期化は実行中の処理を共有するので二重に走らない。
/// </para>
/// </remarks>
public sealed class BackendConnectionChecks
{
    private readonly ChatConnectionSettingsViewModel _connection;
    private readonly Func<Task> _codex;
    private readonly Func<Task> _claudeCode;
    private readonly Func<Task> _copilot;
    private readonly Action<Exception> _reportFailure;

    /// <summary>接続方式ごとの確認を結び付け、接続方式の変化の購読を始める</summary>
    /// <param name="connection">確定した接続方式を持つ状態部品</param>
    /// <param name="codex">Codex へ切り替えたときの確認</param>
    /// <param name="claudeCode">Claude Code へ切り替えたときの確認</param>
    /// <param name="copilot">GitHub Copilot へ切り替えたときの確認</param>
    /// <param name="reportFailure">確認の失敗の知らせ先（ダイアログの文言でステータスへ出す）</param>
    public BackendConnectionChecks(
        ChatConnectionSettingsViewModel connection,
        Func<Task> codex,
        Func<Task> claudeCode,
        Func<Task> copilot,
        Action<Exception> reportFailure
    )
    {
        _connection = connection;
        _codex = codex;
        _claudeCode = claudeCode;
        _copilot = copilot;
        _reportFailure = reportFailure;
        _connection.PropertyChanged += OnConnectionPropertyChanged;
    }

    /// <summary>確認を実行し、失敗は知らせ先へ渡す（例外を外へ漏らさない）</summary>
    /// <param name="check">実行する確認</param>
    public async Task RunAsync(Func<Task> check)
    {
        try
        {
            await check().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _reportFailure(ex);
        }
    }

    /// <summary>接続方式が変わったら、その方式の確認を完了を待たずに起動する</summary>
    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChatConnectionSettingsViewModel.SelectedBackend))
        {
            return;
        }

        Func<Task>? check = _connection.SelectedBackend switch
        {
            ErChatBackendKind.Codex => _codex,
            ErChatBackendKind.ClaudeCode => _claudeCode,
            ErChatBackendKind.Copilot => _copilot,
            _ => null,
        };

        if (check is not null)
        {
            _ = RunAsync(check);
        }
    }
}
