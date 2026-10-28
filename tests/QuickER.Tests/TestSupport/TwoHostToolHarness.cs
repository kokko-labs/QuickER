using System.IO;
using System.Text.Json;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;

namespace QuickER.Tests.TestSupport;

/// <summary>ER 図のツールを実行するホストの種類（2 つのツールホストの軸の正本）</summary>
public enum ToolHostKind
{
    /// <summary>GUI 側（<see cref="ErDiagramDynamicTools"/>＋<see cref="MainViewModel"/>）</summary>
    Gui,

    /// <summary>MCP 側（<see cref="DocumentErDiagramToolHost"/>＋一時ファイル）</summary>
    File,
}

/// <summary>
/// 同じツール呼び出しを、GUI 側（<see cref="ErDiagramDynamicTools"/>＋<see cref="MainViewModel"/>）と MCP 側
/// （<see cref="DocumentErDiagramToolHost"/>＋一時ファイル）の 2 つのホストへ流すテストの土台
/// </summary>
/// <remarks>
/// <para>
/// ホストは行列の軸（<see cref="Axis"/>）として登録する。正本の列挙は <see cref="ToolHostKind"/> の 1 つで、値ごとにアダプタが
/// ちょうど 1 つある（無ければ軸を作る時点で赤）。ホストごとに違う部分（ツールの実行・現在の図・図への直接の書き込み）は
/// <see cref="ToolHostSession"/> の抽象メンバーにする。ホストを引数に取るテストが全ホストの行を持つことは
/// <c>ToolHostAxisGuardTests</c> が確かめる。
/// </para>
/// <para>
/// GUI のセッションは方言を指定しない（<see cref="MainViewModel"/> の既定のまま）。MCP のセッションは呼び出し側が渡す方言で
/// 図ファイルを作る（<see cref="DiagramFiles.Create"/>）。2 つのホストの方言をそろえるかは、テストの意味が変わるので
/// この土台では決めない。
/// </para>
/// </remarks>
internal static class TwoHostToolHarness
{
    /// <summary>2 つのツールホストの軸（値＝<see cref="ToolHostKind"/> の名前）</summary>
    public static MatrixAxis<IToolHostAdapter> Axis { get; } =
        new("ToolHost", Enum.GetNames<ToolHostKind>(), [new GuiAdapter(), new FileAdapter()]);

    /// <summary>ホストを選んで、新しい図のセッションを開く</summary>
    /// <param name="kind">ホスト</param>
    /// <param name="fileTargetDbms">MCP 側の図ファイルの方言（GUI 側は使わない）</param>
    public static ToolHostSession Open(ToolHostKind kind, string fileTargetDbms) =>
        Axis.Get(kind.ToString()).Open(fileTargetDbms);

    /// <summary>GUI 側の新しい図のセッションを開く</summary>
    public static GuiToolHostSession OpenGui() => new(new MainViewModel());

    /// <summary>MCP 側の新しい図ファイル（一時ディレクトリの中）のセッションを開く</summary>
    /// <param name="targetDbms">図ファイルの方言</param>
    public static FileToolHostSession OpenFile(string targetDbms)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "quicker-tool-host-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "diagram.json");
        DiagramFiles.Create(file, targetDbms);

        return new FileToolHostSession(directory, file);
    }

    /// <summary>GUI 側のアダプタ</summary>
    private sealed class GuiAdapter : IToolHostAdapter
    {
        public string AxisValue => nameof(ToolHostKind.Gui);

        public ToolHostSession Open(string fileTargetDbms) => OpenGui();
    }

    /// <summary>MCP 側のアダプタ</summary>
    private sealed class FileAdapter : IToolHostAdapter
    {
        public string AxisValue => nameof(ToolHostKind.File);

        public ToolHostSession Open(string fileTargetDbms) => OpenFile(fileTargetDbms);
    }
}

/// <summary>2 つのツールホストの軸のアダプタ（ホストごとにセッションの開き方を持つ）</summary>
internal interface IToolHostAdapter : IMatrixAdapter
{
    /// <summary>新しい図のセッションを開く</summary>
    /// <param name="fileTargetDbms">MCP 側の図ファイルの方言（GUI 側は使わない）</param>
    ToolHostSession Open(string fileTargetDbms);
}

/// <summary>1 つのホストの 1 つの図に対するツールの実行（ホストごとに違う部分は抽象メンバー）</summary>
internal abstract class ToolHostSession : IDisposable
{
    /// <summary>ツールを実行する（引数は JSON へ直列化して渡す）</summary>
    public (string Result, bool Success) Exec(string tool, object args) =>
        Execute(tool, JsonSerializer.SerializeToElement(args));

    /// <summary>ツールを実行する</summary>
    public abstract (string Result, bool Success) Execute(string tool, JsonElement args);

    /// <summary>現在の図</summary>
    public abstract ErDiagram Schema { get; }

    /// <summary>図の名前付きクエリの名前（図の中の並び順）</summary>
    public abstract List<string> QueryNames { get; }

    /// <summary>図へ名前付きクエリを直接足す（ツールの対象外の経路で作られるもの）</summary>
    public abstract void AddQuery(QueryDefinition query);

    /// <summary>図のリレーションの制約名をすべて消す（ツールでは作れない形を直接作る）</summary>
    public abstract void ClearConstraintNames();

    /// <summary>後始末（既定は何もしない）</summary>
    public virtual void Dispose() { }
}

/// <summary>GUI 側のセッション（<see cref="ErDiagramDynamicTools"/>＋<see cref="MainViewModel"/>）</summary>
internal sealed class GuiToolHostSession(MainViewModel viewModel) : ToolHostSession
{
    /// <summary>図を持つ ViewModel</summary>
    public MainViewModel ViewModel { get; } = viewModel;

    public override (string Result, bool Success) Execute(string tool, JsonElement args) =>
        ErDiagramDynamicTools.Execute(tool, args, ViewModel);

    public override ErDiagram Schema => ViewModel.ToDiagramModel();

    public override List<string> QueryNames =>
        ViewModel.Queries.Select(query => query.Name).ToList();

    public override void AddQuery(QueryDefinition query) => ViewModel.Queries.Add(query);

    public override void ClearConstraintNames()
    {
        foreach (var relationship in ViewModel.Relationships)
        {
            relationship.ConstraintName = null;
        }
    }
}

/// <summary>MCP 側のセッション（<see cref="DocumentErDiagramToolHost"/>＋一時ディレクトリの図ファイル）</summary>
internal sealed class FileToolHostSession(string directory, string filePath) : ToolHostSession
{
    /// <summary>図ファイルのパス</summary>
    public string FilePath { get; } = filePath;

    public override (string Result, bool Success) Execute(string tool, JsonElement args) =>
        DocumentErDiagramToolHost.Execute(tool, FilePath, args);

    public override ErDiagram Schema => JsonStorageService.Load(FilePath).Schema;

    public override List<string> QueryNames => Schema.Queries.Select(query => query.Name).ToList();

    public override void AddQuery(QueryDefinition query)
    {
        var document = JsonStorageService.Load(FilePath);
        document.Schema.Queries.Add(query);
        JsonStorageService.SaveAtomic(FilePath, document);
    }

    public override void ClearConstraintNames()
    {
        var document = JsonStorageService.Load(FilePath);

        foreach (var relationship in document.Schema.Relationships)
        {
            relationship.ConstraintName = null;
        }

        JsonStorageService.SaveAtomic(FilePath, document);
    }

    public override void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // 後始末の失敗はテストの結果に影響させない
        }
    }
}
