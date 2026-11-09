using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using QuickER.AI.Chat;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
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
/// <see cref="Open"/> の GUI のセッションは方言を指定しない（<see cref="MainViewModel"/> の既定のまま）。MCP のセッションは
/// 呼び出し側が渡す方言で図ファイルを作る（<see cref="DiagramFiles.Create"/>）。2 つのホストの方言をそろえるかは、テストの
/// 意味が変わるので呼び出し側が選ぶ：そろえるときは <see cref="OpenAligned"/>（GUI も同じ方言の空の図で開く）を使う。
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

    /// <summary>ホストを選んで、両ホストとも同じ方言の新しい図のセッションを開く</summary>
    /// <param name="kind">ホスト</param>
    /// <param name="targetDbms">図の方言（GUI 側も同じ方言の空の図で開く）</param>
    public static ToolHostSession OpenAligned(ToolHostKind kind, string targetDbms) =>
        Axis.Get(kind.ToString()).OpenAligned(targetDbms);

    /// <summary>GUI 側の新しい図のセッションを開く</summary>
    public static GuiToolHostSession OpenGui() => new(new MainViewModel());

    /// <summary>GUI 側の、指定した方言の空の図のセッションを開く</summary>
    /// <param name="targetDbms">図の方言</param>
    /// <remarks>
    /// <para>
    /// 5 方言のプロバイダを登録したレジストリと、画面を出さないダイアログで <see cref="MainViewModel"/> を作る
    /// （既定のレジストリは SQL Server だけで、ほかの方言の図を入れると本物のメッセージボックスを出して SQL Server へ落ちる）。
    /// </para>
    /// <para>
    /// 既定の方言（<see cref="MainViewModel"/> を作った直後の方言）なら何もしない。違えば空の図をモジュールの置換の入口で入れる
    /// （方言の採用と Undo の履歴の消去まで製品の入口が行う）。開いた方言が指定と違えば例外にする（黙って別の方言で流さない）。
    /// </para>
    /// </remarks>
    public static GuiToolHostSession OpenGui(string targetDbms)
    {
        var viewModel = new MainViewModel(
            dialogService: new StubDialogService(),
            providers: new DatabaseProviderRegistry([
                new SqlServerProvider(),
                new PostgreSqlProvider(),
                new MySqlProvider(),
                new OracleProvider(),
                new SqliteProvider(),
            ])
        );

        if (!string.Equals(viewModel.CurrentProvider.Name, targetDbms, StringComparison.Ordinal))
        {
            viewModel.ReplaceDiagramFromModule(new ErDiagram { TargetDbms = targetDbms });
        }

        if (!string.Equals(viewModel.CurrentProvider.Name, targetDbms, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"GUI のセッションを方言 '{targetDbms}' で開けなかった（実際は '{viewModel.CurrentProvider.Name}'）"
            );
        }

        return new GuiToolHostSession(viewModel);
    }

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

        public ToolHostSession OpenAligned(string targetDbms) => OpenGui(targetDbms);
    }

    /// <summary>MCP 側のアダプタ</summary>
    private sealed class FileAdapter : IToolHostAdapter
    {
        public string AxisValue => nameof(ToolHostKind.File);

        public ToolHostSession Open(string fileTargetDbms) => OpenFile(fileTargetDbms);

        public ToolHostSession OpenAligned(string targetDbms) => OpenFile(targetDbms);
    }
}

/// <summary>2 つのツールホストの軸のアダプタ（ホストごとにセッションの開き方を持つ）</summary>
internal interface IToolHostAdapter : IMatrixAdapter
{
    /// <summary>新しい図のセッションを開く</summary>
    /// <param name="fileTargetDbms">MCP 側の図ファイルの方言（GUI 側は使わない）</param>
    ToolHostSession Open(string fileTargetDbms);

    /// <summary>両ホストとも同じ方言の新しい図のセッションを開く</summary>
    /// <param name="targetDbms">図の方言</param>
    ToolHostSession OpenAligned(string targetDbms);
}

/// <summary>1 つのホストの 1 つの図に対するツールの実行（ホストごとに違う部分は抽象メンバー）</summary>
internal abstract class ToolHostSession : IDisposable
{
    /// <summary>ツールを実行する（引数は JSON へ直列化して渡す）</summary>
    public (string Result, bool Success) Exec(string tool, object args) =>
        Execute(tool, JsonSerializer.SerializeToElement(args));

    /// <summary>ツールを実行し、その呼び出しが図を書いたかを <see cref="WroteDiagram"/> に残す</summary>
    public (string Result, bool Success) Execute(string tool, JsonElement args)
    {
        PrepareWriteObservation();
        var before = WriteMark();
        var outcome = Run(tool, args);
        WroteDiagram = !string.Equals(before, WriteMark(), StringComparison.Ordinal);

        return outcome;
    }

    /// <summary>
    /// 直前の <see cref="Execute"/> が図を書いたか（GUI＝Undo の履歴の世代か未保存の状態が変わった・
    /// MCP＝図ファイルのバイトか更新日時が変わった）
    /// </summary>
    /// <remarks>
    /// 「何も変えない呼び出し」をホストごとの観測で確かめるための面。図の意味が同じでも、GUI が空の履歴を積む・
    /// MCP が同じ内容を書き直すと真になる（MCP は呼び出しの前に更新日時を過去の固定の値へ戻しておくので、
    /// 同じバイトの書き直しも更新日時の変化で見える）。
    /// </remarks>
    public bool WroteDiagram { get; private set; }

    /// <summary>ツールを実行する（ホストごとの実体）</summary>
    protected abstract (string Result, bool Success) Run(string tool, JsonElement args);

    /// <summary>図を書いたかを比べるための、いまの状態の印</summary>
    protected abstract string WriteMark();

    /// <summary>呼び出しの前に、書いたことが印に残るよう状態を整える（既定は何もしない）</summary>
    protected virtual void PrepareWriteObservation() { }

    /// <summary>現在の図</summary>
    public abstract ErDiagram Schema { get; }

    /// <summary>図の名前付きクエリの名前（図の中の並び順）</summary>
    public abstract List<string> QueryNames { get; }

    /// <summary>図へ名前付きクエリを直接足す（ツールの対象外の経路で作られるもの）</summary>
    public abstract void AddQuery(QueryDefinition query);

    /// <summary>図のリレーションの制約名をすべて消す（ツールでは作れない形を直接作る）</summary>
    public abstract void ClearConstraintNames();

    /// <summary>列の外部キーの印を直接付ける・外す（リレーションと無関係に手で付けた印を作る）</summary>
    /// <param name="tableName">テーブル名（大文字小文字まで一致）</param>
    /// <param name="columnName">カラム名（大文字小文字まで一致）</param>
    /// <param name="isForeignKey">付けるなら真</param>
    public abstract void SetForeignKeyMark(string tableName, string columnName, bool isForeignKey);

    /// <summary>
    /// 図の意味モデルを直接書き換える（手で編集した図のファイルにだけ現れる形＝主キーの列の NULL 許容の印・
    /// 同じ名前の一意制約 2 つなど、ツールでも画面でも作れない形を作る）
    /// </summary>
    /// <param name="edit">いまの図の写しへ当てる書き換え</param>
    /// <remarks>
    /// GUI は書き換えた図を機能モジュールと同じ入口（<see cref="MainViewModel.ReplaceDiagramFromModule"/>）で読み込むので、
    /// 読込が実効値へそろえる面（主キーの列の NULL 許容）は GUI の図に残らない。File は図のファイルへそのまま書く。
    /// </remarks>
    public abstract void EditSchema(Action<ErDiagram> edit);

    /// <summary>後始末（既定は何もしない）</summary>
    public virtual void Dispose() { }
}

/// <summary>GUI 側のセッション（<see cref="ErDiagramDynamicTools"/>＋<see cref="MainViewModel"/>）</summary>
/// <remarks>
/// 名前付きクエリの 3 ツールは、製品の配線と同じく AI チャットのアダプタ（<see cref="ErDiagramHostChatAdapter"/>＋
/// <see cref="MainViewModelErDiagramHost"/>）を通す（<see cref="ErDiagramDynamicTools"/> はクエリのツールを持たない）。
/// ほかのツールは <see cref="ErDiagramDynamicTools"/> を直接呼ぶ。
/// </remarks>
internal sealed class GuiToolHostSession(MainViewModel viewModel) : ToolHostSession
{
    /// <summary>名前付きクエリのツール（チャットのアダプタが捕まえるもの）</summary>
    private static readonly HashSet<string> QueryTools = new(StringComparer.Ordinal)
    {
        QueryToolCore.SetQueryToolName,
        QueryToolCore.ListQueriesToolName,
        QueryToolCore.RemoveQueryToolName,
    };

    /// <summary>図を持つ ViewModel</summary>
    public MainViewModel ViewModel { get; } = viewModel;

    protected override (string Result, bool Success) Run(string tool, JsonElement args) =>
        QueryTools.Contains(tool)
            ? new ErDiagramHostChatAdapter(
                new MainViewModelErDiagramHost(ViewModel)
            ).ToolHost.Execute(tool, args.GetRawText())
            : ErDiagramDynamicTools.Execute(tool, args, ViewModel);

    protected override string WriteMark() =>
        $"{ViewModel.UndoRedo.ChangeGeneration}/{ViewModel.IsDirty}";

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

    public override void SetForeignKeyMark(
        string tableName,
        string columnName,
        bool isForeignKey
    ) =>
        ViewModel
            .Entities.Single(entity => entity.TableName == tableName)
            .Columns.Single(column => column.Name == columnName)
            .IsForeignKey = isForeignKey;

    public override void EditSchema(Action<ErDiagram> edit)
    {
        var diagram = ViewModel.ToDiagramModel();
        edit(diagram);
        ViewModel.ReplaceDiagramFromModule(diagram);
    }
}

/// <summary>MCP 側のセッション（<see cref="DocumentErDiagramToolHost"/>＋一時ディレクトリの図ファイル）</summary>
internal sealed class FileToolHostSession(string directory, string filePath) : ToolHostSession
{
    /// <summary>図ファイルのパス</summary>
    public string FilePath { get; } = filePath;

    protected override (string Result, bool Success) Run(string tool, JsonElement args) =>
        DocumentErDiagramToolHost.Execute(tool, FilePath, args);

    /// <summary>呼び出しの前に図ファイルへ置く更新日時（書けば必ず変わる過去の固定の値）</summary>
    private static readonly DateTime UnwrittenStamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    protected override void PrepareWriteObservation()
    {
        if (File.Exists(FilePath))
        {
            File.SetLastWriteTimeUtc(FilePath, UnwrittenStamp);
        }
    }

    protected override string WriteMark() =>
        File.Exists(FilePath)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FilePath)))
                + "/"
                + File.GetLastWriteTimeUtc(FilePath).Ticks.ToString(CultureInfo.InvariantCulture)
            : "(none)";

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

    public override void SetForeignKeyMark(string tableName, string columnName, bool isForeignKey)
    {
        var document = JsonStorageService.Load(FilePath);
        document
            .Schema.Entities.Single(entity => entity.TableName == tableName)
            .Columns.Single(column => column.Name == columnName)
            .IsForeignKey = isForeignKey;
        JsonStorageService.SaveAtomic(FilePath, document);
    }

    public override void EditSchema(Action<ErDiagram> edit)
    {
        var document = JsonStorageService.Load(FilePath);
        edit(document.Schema);
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
