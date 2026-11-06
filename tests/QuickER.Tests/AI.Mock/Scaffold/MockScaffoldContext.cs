using System.IO;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.AI.Mock.Agents;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock.Scaffold;

/// <summary>スキャフォールドの前の出力フォルダの状態（次元表の因子 Existing の水準）</summary>
internal enum ScaffoldExisting
{
    /// <summary>出力フォルダが無い（初めてのスキャフォールド）</summary>
    None,

    /// <summary>前回のスキャフォールドの出力がある（作り直し）</summary>
    PreviousOutput,

    /// <summary>出力先の Generated/ が外を指すジャンクション</summary>
    GeneratedJunction,

    /// <summary>コピー元のモックフォルダが、空にして書き直すフォルダ（Generated/・design/mock/）と重なる</summary>
    MockOverlapsOutput,
}

/// <summary>スキャフォールドの表の 1 行の入力（ターゲット・方言・既存の状態・本体）</summary>
/// <param name="Target">生成のターゲット</param>
/// <param name="Dialect">図の方言（<c>ErDiagram.TargetDbms</c>）</param>
/// <param name="Existing">スキャフォールドの前の出力フォルダの状態</param>
/// <param name="Body">流して表明する本体</param>
internal sealed record MockScaffoldScenario(
    MockAgentTarget Target,
    string Dialect,
    ScaffoldExisting Existing,
    Func<MockScaffoldContext, Task> Body
);

/// <summary>スキャフォールドの表の 1 行を流す文脈（行ごとの一時フォルダ・モックフォルダの種・図・スキャフォールド）</summary>
/// <remarks>
/// 出力フォルダ・モックフォルダは、行ごとの <see cref="TestTempFolder"/> 1 つの下に作る（出力フォルダはスキャフォールドが作るので、
/// <see cref="NewPath"/> はパスだけを返す）。破棄では <see cref="ReparsePointTestHelper.DeleteTree"/> でジャンクション・読み取り専用を外しながら
/// 消してから、<see cref="TestTempFolder.Dispose"/> を呼ぶ。
/// </remarks>
internal sealed class MockScaffoldContext : IDisposable
{
    /// <summary>プロジェクト名</summary>
    public const string ProjectName = "AcmeMock";

    /// <summary>日本語の見出しの画面 HTML（共有 style.css を読む）</summary>
    public const string JapaneseScreenHtml =
        "<!DOCTYPE html><html lang=\"ja\"><head><link rel=\"stylesheet\" href=\"style.css\"></head>"
        + "<body><h1>顧客一覧</h1></body></html>";

    /// <summary>英語の見出しの画面 HTML（共有 style.css を読む）</summary>
    public const string EnglishScreenHtml =
        "<!DOCTYPE html><html><head><link rel=\"stylesheet\" href=\"style.css\"></head>"
        + "<body><h1>Customers</h1></body></html>";

    private readonly TestTempFolder _root = new();

    /// <summary>行のスキャフォールドのサービス（SQL Server と SQLite のプロバイダ）</summary>
    public MockProjectScaffoldService Service { get; } =
        new(new DatabaseProviderRegistry([new SqlServerProvider(), new SqliteProvider()]));

    /// <summary>行の一時フォルダの下の、まだ作っていないパスを返す</summary>
    /// <param name="name">名前（行の中で一意）</param>
    public string NewPath(string name) => Path.Combine(_root.Path, name);

    /// <summary>行の一時フォルダの下にフォルダを作って返す</summary>
    /// <param name="name">名前（行の中で一意）</param>
    public string NewFolder(string name) => Directory.CreateDirectory(NewPath(name)).FullName;

    /// <summary>顧客テーブル 1 つの図を作る</summary>
    /// <param name="dialect">方言</param>
    /// <param name="withNameColumn">主キーのほかに name 列（nvarchar(100)）を持つか</param>
    public static ErDiagram Diagram(string dialect, bool withNameColumn = false)
    {
        var columns = new List<Column>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "customer_id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            },
        };

        if (withNameColumn)
        {
            columns.Add(
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "name",
                    DataType = "nvarchar(100)",
                    IsNullable = false,
                }
            );
        }

        return new ErDiagram
        {
            TargetDbms = dialect,
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns = columns,
                },
            ],
        };
    }

    /// <summary>mock.json＋共有 style.css＋日本語の画面 2 枚（CustomerList・CustomerEdit）のモックフォルダを作って返す</summary>
    /// <param name="name">フォルダの名前</param>
    public string SeedJapaneseMock(string name = "mock")
    {
        var folder = NewFolder(name);
        var store = MockFolderStore.CreateNew(folder, ProjectName, "# schema");
        store.SaveStylesheet("body { font-family: sans-serif; }", "初版");
        store.SaveScreen(
            "CustomerList.html",
            "顧客一覧",
            "顧客の一覧",
            JapaneseScreenHtml,
            Array.Empty<MockTransition>(),
            "初版"
        );
        store.SaveScreen(
            "CustomerEdit.html",
            "顧客編集",
            "顧客の編集",
            JapaneseScreenHtml,
            Array.Empty<MockTransition>(),
            "初版"
        );
        return folder;
    }

    /// <summary>mock.json＋共有 style.css＋指定の画面（英語の見出し）のモックフォルダを作って返す</summary>
    /// <param name="name">フォルダの名前</param>
    /// <param name="screenFiles">画面のファイル名</param>
    public string SeedMock(string name, params string[] screenFiles)
    {
        var folder = NewFolder(name);
        var store = MockFolderStore.CreateNew(folder, ProjectName, "# schema");
        store.SaveStylesheet("body { font-family: sans-serif; }", "first");

        foreach (var file in screenFiles)
        {
            store.SaveScreen(
                file,
                file,
                "screen",
                EnglishScreenHtml,
                Array.Empty<MockTransition>(),
                "first"
            );
        }

        return folder;
    }

    /// <summary>mock.json だけのモックフォルダを作って返す</summary>
    /// <param name="name">フォルダの名前</param>
    public string SeedBareMock(string name = "mock")
    {
        var folder = NewFolder(name);
        MockFolderStore.CreateNew(folder, ProjectName, "# schema");
        return folder;
    }

    /// <summary>行のターゲット・方言でスキャフォールドする</summary>
    /// <param name="scenario">行の入力</param>
    /// <param name="output">出力フォルダ</param>
    /// <param name="mockFolder">コピー元のモックフォルダ</param>
    /// <param name="withNameColumn">図に name 列を持たせるか</param>
    /// <param name="dialect">方言（省略時は行の方言）</param>
    public MockProjectScaffoldResult Scaffold(
        MockScaffoldScenario scenario,
        string output,
        string mockFolder,
        bool withNameColumn = false,
        string? dialect = null
    ) =>
        Service.Scaffold(
            Diagram(dialect ?? scenario.Dialect, withNameColumn),
            output,
            ProjectName,
            mockFolder,
            scenario.Target.Profile
        );

    /// <summary>Generated/ 配下の *.g.cs をすべて連結した本文</summary>
    /// <param name="generatedDirectory">Generated/ のパス</param>
    public static string ReadAllGenerated(string generatedDirectory) =>
        string.Concat(
            Directory
                .GetFiles(generatedDirectory, "*.g.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText)
        );

    /// <summary>行の一時フォルダを消す（ジャンクション・読み取り専用を外しながら）</summary>
    public void Dispose()
    {
        try
        {
            ReparsePointTestHelper.DeleteTree(_root.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 後始末はベストエフォート（残りは TestTempFolder の削除に任せる）
        }

        _root.Dispose();
    }
}
