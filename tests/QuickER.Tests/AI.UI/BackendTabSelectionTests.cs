using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.UI;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// <see cref="BackendTabSelection"/> の暫定値パターン（選択を受け入れてから確定・断られたら実変更として戻す）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 表示の巻き戻しは束縛が通知を受けて転送することで成り立つため、ここでは通知の発行事実と値の並びを表明する
/// （実タブでの見た目の巻き戻しは AiChatDialogBackendTabsTests が実ダイアログで固定する）。
/// </remarks>
public class BackendTabSelectionTests
{
    /// <summary>設定ファイルへ触れない接続方式の状態部品を作る</summary>
    private static ChatConnectionSettingsViewModel CreateConnection() =>
        new(
            AiDialogKind.AiChat,
            settingsStore: new AiSettingsStore(
                Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"))
            ),
            codexConfigReader: () => new CodexConfigToml(),
            apiKeyLoader: _ => string.Empty,
            apiKeySaver: (_, _) => { }
        );

    /// <summary>通知のたびに <see cref="BackendTabSelection.Selected"/> の値を記録する</summary>
    private static List<ErChatBackendKind> RecordSelected(BackendTabSelection selection)
    {
        var values = new List<ErChatBackendKind>();
        selection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BackendTabSelection.Selected))
            {
                values.Add(selection.Selected);
            }
        };
        return values;
    }

    /// <summary>切替を認める試行（親 VM の TryChangeBackend の代わり）</summary>
    private static Func<ErChatBackendKind, bool> Accepting(
        ChatConnectionSettingsViewModel connection
    ) =>
        backend =>
        {
            connection.SelectedBackend = backend;
            return true;
        };

    /// <summary>選択は暫定値として受け入れて通知し、確定は投函の後で行うことを検証する</summary>
    [Fact(DisplayName = "選択は暫定値として受け入れ、確定は投函の後に行う")]
    public void Select_IsPendingUntilPostedCommitRuns()
    {
        var connection = CreateConnection();
        var selection = new BackendTabSelection(connection, Accepting(connection));
        var posted = new List<Action>();
        selection.SetUiPost(posted.Add);
        var values = RecordSelected(selection);

        selection.Selected = ErChatBackendKind.Codex;

        selection.Selected.Should().Be(ErChatBackendKind.Codex, "タブは切替先へ動かしておく");
        connection.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey, "確定はまだ");
        values.Should().Equal(ErChatBackendKind.Codex);
        posted.Should().ContainSingle();

        posted[0]();

        connection.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
        selection.Selected.Should().Be(ErChatBackendKind.Codex);
    }

    /// <summary>
    /// 確定を断られたら、暫定値を捨てて元の接続方式を通知し直すことを検証する
    /// （束縛が「切替先 → 元の方式」の実変更として転送するので、タブの内部の選択状態ごと戻る）。
    /// </summary>
    [Fact(DisplayName = "確定を断られたら元の接続方式を通知し直す")]
    public void Rejected_RevertsWithChangeNotification()
    {
        var connection = CreateConnection();
        var attempts = new List<ErChatBackendKind>();
        var selection = new BackendTabSelection(
            connection,
            backend =>
            {
                attempts.Add(backend);
                return false;
            }
        );
        var values = RecordSelected(selection);

        selection.Selected = ErChatBackendKind.Copilot;

        attempts.Should().Equal(ErChatBackendKind.Copilot);
        connection.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey);
        selection.Selected.Should().Be(ErChatBackendKind.ApiKey);
        values.Should().Equal(ErChatBackendKind.Copilot, ErChatBackendKind.ApiKey);
    }

    /// <summary>確定を待つ間に別のタブが選ばれたら、最後の選択だけを確定することを検証する</summary>
    [Fact(DisplayName = "確定を待つ間に選び直したら最後の選択だけを確定する")]
    public void OnlyLatestPendingSelection_IsCommitted()
    {
        var connection = CreateConnection();
        var attempts = new List<ErChatBackendKind>();
        var selection = new BackendTabSelection(
            connection,
            backend =>
            {
                attempts.Add(backend);
                connection.SelectedBackend = backend;
                return true;
            }
        );
        var posted = new List<Action>();
        selection.SetUiPost(posted.Add);

        selection.Selected = ErChatBackendKind.Codex;
        selection.Selected = ErChatBackendKind.ClaudeCode;
        posted.ForEach(commit => commit());

        attempts.Should().Equal(ErChatBackendKind.ClaudeCode);
        connection.SelectedBackend.Should().Be(ErChatBackendKind.ClaudeCode);
        selection.Selected.Should().Be(ErChatBackendKind.ClaudeCode);
    }

    /// <summary>確定した接続方式が VM 側で変わったとき（初期タブの復元など）、タブの表示へ中継することを検証する</summary>
    [Fact(DisplayName = "確定した接続方式の変化をタブの表示へ中継する")]
    public void ConnectionChange_IsRelayedToSelected()
    {
        var connection = CreateConnection();
        var selection = new BackendTabSelection(connection, Accepting(connection));
        var values = RecordSelected(selection);

        connection.SelectedBackend = ErChatBackendKind.Copilot;

        values.Should().Equal(ErChatBackendKind.Copilot);
    }

    /// <summary>今と同じ接続方式の選択は何もしない（投函も通知もしない）ことを検証する</summary>
    [Fact(DisplayName = "同じ接続方式の選択は何もしない")]
    public void SelectingCurrent_DoesNothing()
    {
        var connection = CreateConnection();
        var attempts = 0;
        var selection = new BackendTabSelection(
            connection,
            _ =>
            {
                attempts++;
                return true;
            }
        );
        var values = RecordSelected(selection);

        selection.Selected = ErChatBackendKind.ApiKey;

        attempts.Should().Be(0);
        values.Should().BeEmpty();
    }
}
