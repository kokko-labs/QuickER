using System.IO;
using AwesomeAssertions;
using QuickER.Gui.Abstractions;
using QuickER.Resources;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 図を開けなかったときの案内（<see cref="MainViewModel.OpenCommand"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 手で編集した図でしか起こらない 2 つの壊れ方（Id の重複・必須の名前や型の明示 <c>null</c>）は、
/// 復旧手段が JSON の手編集しかないため、一般の失敗（読めない・無関係な JSON）とは別の文言で案内する。
/// どの文言を選ぶかは失敗の種別で分かれるので、種別ごとに 1 ケースずつ固定する。
/// </remarks>
public sealed class MainViewModelOpenFailureTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-openfail-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelOpenFailureTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch
        {
            // 後始末失敗はテスト結果に影響させない
        }
    }

    /// <summary>指定の JSON を書き出し、それを「開く」で選んだときのエラーメッセージとパスを返す</summary>
    private (string Path, string Message) OpenAndCaptureError(string json)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);

        var dialogs = new StubDialogService();
        var vm = new MainViewModel(
            dialogs,
            files: new StubFileDialogService { OpenResult = new FileDialogResult(path, 1) }
        );

        vm.OpenCommand.Execute(null);

        return (path, dialogs.ErrorMessages.Should().ContainSingle().Subject);
    }

    [Fact(DisplayName = "開く: Id が重複する図は、手で直すよう案内する専用の文言で断る")]
    public void Open_DuplicateId_UsesDedicatedMessage()
    {
        var duplicated = Guid.NewGuid();

        var (path, message) = OpenAndCaptureError(
            $$"""
            {
              "Version": 1,
              "Schema": {
                "TargetDbms": "sqlserver",
                "Entities": [
                  { "Id": "{{duplicated}}", "TableName": "Orders", "Columns": [] },
                  { "Id": "{{duplicated}}", "TableName": "OrderLines", "Columns": [] }
                ]
              }
            }
            """
        );

        message.Should().StartWith(string.Format(Strings.Open_DuplicateId, path));
        message.Should().Contain(duplicated.ToString(), "手で直すには場所が要る");
    }

    [Fact(DisplayName = "開く: 必須の名前が null の図は、手で直すよう案内する専用の文言で断る")]
    public void Open_MissingRequiredText_UsesDedicatedMessage()
    {
        var (path, message) = OpenAndCaptureError(
            """
            {
              "Version": 1,
              "Schema": {
                "TargetDbms": "sqlserver",
                "Entities": [
                  { "TableName": null, "Columns": [] }
                ]
              }
            }
            """
        );

        message.Should().StartWith(string.Format(Strings.Open_MissingRequiredText, path));
        message.Should().Contain("TableName", "手で直すには場所が要る");
    }

    [Fact(DisplayName = "開く: 無関係な JSON は一般の失敗として断る")]
    public void Open_NotDiagramDocument_UsesGenericMessage()
    {
        var (path, message) = OpenAndCaptureError("""{ "hello": "world" }""");

        message.Should().StartWith(string.Format(Strings.Open_Failed, path));
    }
}
