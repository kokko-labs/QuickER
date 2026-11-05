using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickER.Cli;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.UI;
using QuickER.Documents;
using QuickER.Gui.Abstractions;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>生成の入口（行列の軸の正本）</summary>
internal enum CodegenEntrance
{
    /// <summary>本番と同じ <c>DiagramCodeGenerator.Generate</c> を直接呼ぶ</summary>
    Direct,

    /// <summary>生成ダイアログの ViewModel（画面のプロパティ）</summary>
    Gui,

    /// <summary><c>quicker generate --config</c>（プロセス内）</summary>
    Cli,

    /// <summary>MCP の <c>generate_csharp</c></summary>
    Mcp,
}

/// <summary>入口を通して生成したもの</summary>
/// <param name="EffectiveOptions">入口が実際に生成へ渡したオプション（判定はこれで期待を作る）</param>
/// <param name="Result">本番と同じ経路で生成した結果（判定はこれに掛ける）</param>
/// <param name="Problems">入口の段で見つけた問題</param>
/// <param name="Stages">入口が走らせた段</param>
internal sealed record EntranceOutcome(
    CodeGenerationOptions EffectiveOptions,
    CodeGenerationResult Result,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Stages
);

/// <summary>入口のアダプタ（入口の値ごとに 1 つ。値による違いはこのメンバーだけで表す）</summary>
internal interface ICodegenEntrance : IMatrixAdapter
{
    /// <summary>入口が走らせる段（判定の前に期待する段）</summary>
    IReadOnlyList<string> Stages { get; }

    /// <summary>入口を通して生成する</summary>
    /// <param name="input">入力</param>
    Task<EntranceOutcome> RunAsync(CodegenInput input);
}

/// <summary>入口の軸（正本の値・名前・アダプタ）</summary>
internal static class CodegenEntrances
{
    /// <summary>GUI の入口がオプションを確かめた段</summary>
    public const string GuiOptionsStage = "gui-options";

    /// <summary>CLI / MCP の入口が直接の生成と同じファイルを書いたことを確かめた段</summary>
    public const string EntranceRelationStage = "relation:entrance";

    /// <summary>正本の値の名前（宣言の順）</summary>
    public static IReadOnlyList<string> Names { get; } =
        Enum.GetValues<CodegenEntrance>().Select(NameOf).ToList();

    /// <summary>入口の軸（値ごとにちょうど 1 つのアダプタ）</summary>
    public static MatrixAxis<ICodegenEntrance> Axis { get; } =
        new(
            nameof(CodegenEntrance),
            Names,
            [new DirectEntrance(), new GuiEntrance(), new CliEntrance(), new McpEntrance()]
        );

    /// <summary>値の名前（次元表の水準）</summary>
    public static string NameOf(CodegenEntrance entrance) => entrance.ToString().ToLowerInvariant();

    /// <summary>名前から値を引く</summary>
    public static CodegenEntrance Parse(string name) =>
        Enum.GetValues<CodegenEntrance>().Single(value => NameOf(value) == name);

    /// <summary>オプションを全プロパティで比べ、違うプロパティを名指しする（RepositoryDialects は並びと値で比べる）</summary>
    /// <param name="what">比べたもの</param>
    /// <param name="expected">期待</param>
    /// <param name="actual">実際</param>
    public static IReadOnlyList<string> CompareOptions(
        string what,
        CodeGenerationOptions expected,
        CodeGenerationOptions actual
    )
    {
        var problems = new List<string>();

        foreach (
            var property in typeof(CodeGenerationOptions).GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
            )
        )
        {
            var left = property.GetValue(expected);
            var right = property.GetValue(actual);
            var same =
                left is IEnumerable<string> leftList && right is IEnumerable<string> rightList
                    ? leftList.SequenceEqual(rightList, StringComparer.Ordinal)
                    : Equals(left, right);

            if (!same)
            {
                problems.Add(
                    $"{what}: {property.Name} が期待 {Format(left)} と違う {Format(right)}"
                );
            }
        }

        return problems;
    }

    private static string Format(object? value) =>
        value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            IEnumerable<string> list => "[" + string.Join(", ", list) + "]",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
        };

    /// <summary>テストが使う一時フォルダを作る</summary>
    public static string NewTemporaryFolder()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "QuickERCodegenConfig",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>一時フォルダを消す（消せなくても失敗にしない）</summary>
    public static void DeleteQuietly(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>direct：本番と同じ <c>DiagramCodeGenerator.Generate</c>（<c>GenerationExecutor</c> と同じプロバイダ・方言のマッパ）</summary>
internal sealed class DirectEntrance : ICodegenEntrance
{
    /// <inheritdoc/>
    public string AxisValue => CodegenEntrances.NameOf(CodegenEntrance.Direct);

    /// <inheritdoc/>
    public IReadOnlyList<string> Stages => [];

    /// <inheritdoc/>
    public Task<EntranceOutcome> RunAsync(CodegenInput input) =>
        Task.FromResult(
            new EntranceOutcome(
                input.Options,
                CodegenRunner.Generate(input.Diagram, input.TypeDialect, input.Options),
                [],
                []
            )
        );
}

/// <summary>
/// gui：生成ダイアログの ViewModel へ、行のオプションを画面のプロパティ（ラジオ・チェック・欄）から入れて <c>ToOptions()</c> を取る
/// </summary>
/// <remarks>
/// <para>
/// 期待するオプションは「画面に出ていないオプションを結果に持ち越さない」規則（<see cref="GuiOptionRules"/>）で行のオプションから作り、
/// <c>ToOptions()</c> と全プロパティで比べる。続けて確定（<c>OkCommand</c>）で保存した設定を新しい ViewModel で復元し、
/// <c>ToOptions()</c> が同じであることを見る。生成はその <c>ToOptions()</c> の結果で direct と同じ経路を通す。
/// </para>
/// <para>
/// あわせて次を表明する（src/QuickER.CodeGen.UI/CLAUDE.md「画面に出ていないオプション」）：
/// 画面に入れ終えたときの表示の条件（<c>ShowRemoteContracts</c> / <c>ShowSyncSupport</c> / <c>ShowExcludeUnboundedBinary</c>）が規則の表示の条件と一致する、
/// 確定の後も ViewModel は画面に入れた値を保持する（隠れた欄のチェックは結果から落とすだけ）、復元した ViewModel の
/// 同じ 4 プロパティは結果と同じ値（隠れた値を設定へ持ち越さない）、確定は出力先のフォルダを結果に入れてダイアログを閉じる、
/// 復元した ViewModel の出力先の欄は確定したときと同じ。
/// </para>
/// <para>
/// 画面では隠れた欄にチェックを入れられないので、チェックは全部の欄が出る構成（QuickER 版 Repository・SQL Server と SQLite）で入れてから、
/// 行の DB アクセスと対象 DB へ切り替える（隠れた値は「出ている間に入れて、構成を変えて隠す」でだけできる）。
/// 欄（層フォルダ・サブフォルダ・API リファレンスのファイル名・名前空間）は、行のオプションが指定するものだけを最後に入れる
/// （既定の追従の後に入れる。既定と同じ名前空間は導出として null で渡るので、行には既定と違う値だけを書く）。
/// サブフォルダ・API リファレンスの出力先とファイル名の欄は前後に空白を付けて入れる（結果へはトリムして渡る規則も見る）。
/// </para>
/// <para>
/// データアノテーションと親参照ナビゲーションの [JsonIgnore] は画面に出ない（読み込んだ設定の値を保持して書き戻す）ので、
/// 設定ファイル（ViewModel が構築時に読むもの）から入れる。ほかは既定の設定から始めて画面のプロパティで入れる。
/// </para>
/// </remarks>
internal sealed class GuiEntrance : ICodegenEntrance
{
    /// <inheritdoc/>
    public string AxisValue => CodegenEntrances.NameOf(CodegenEntrance.Gui);

    /// <inheritdoc/>
    public IReadOnlyList<string> Stages => [CodegenEntrances.GuiOptionsStage];

    /// <inheritdoc/>
    public Task<EntranceOutcome> RunAsync(CodegenInput input)
    {
        var problems = new List<string>();
        var expected = GuiOptionRules.Expected(input.Options);
        var folder = CodegenEntrances.NewTemporaryFolder();
        CodeGenerationOptions actual;

        try
        {
            var store = new CSharpGenerationSettingsStore(folder);
            var seed = CSharpGenerationSettings.CreateDefault();
            seed.IncludeDataAnnotations = input.Options.IncludeDataAnnotations;
            seed.IncludeJsonIgnoreOnParentNavigation = input
                .Options
                .IncludeJsonIgnoreOnParentNavigation;

            if (!store.Save(seed))
            {
                problems.Add("GUI: 画面に出ない属性の設定を保存できない");
            }

            var provider = CodegenRunner.Providers.Get(input.TypeDialect);
            var dialogs = new RecordingDialogService();
            var viewModel = new CSharpGenerationDialogViewModel(store, null, provider, dialogs);
            var outputFolder = Path.Combine(folder, "out");
            bool? closed = null;
            viewModel.CloseAction = result => closed = result;
            ApplyScreen(viewModel, input.Options, outputFolder);
            problems.AddRange(
                CompareGates(
                    "GUI の表示の条件",
                    GuiOptionRules.Visibility(input.Options),
                    new GuiGates(
                        viewModel.ShowRemoteContracts,
                        viewModel.ShowRemoteContracts,
                        viewModel.ShowSyncSupport,
                        viewModel.ShowExcludeUnboundedBinary
                    )
                )
            );
            actual = viewModel.ToOptions();
            problems.AddRange(
                CodegenEntrances.CompareOptions("GUI の ToOptions", expected, actual)
            );

            var enteredOutputPath = viewModel.OutputPath;
            viewModel.OkCommand.Execute(null);

            if (viewModel.Result is null)
            {
                problems.Add($"GUI: 確定できない（{viewModel.StatusMessage}）");
            }
            else
            {
                problems.AddRange(
                    CodegenEntrances.CompareOptions(
                        "GUI の確定の結果",
                        actual,
                        viewModel.Result.Options
                    )
                );

                if (viewModel.Result.OutputDirectory != outputFolder)
                {
                    problems.Add(
                        $"GUI: 確定の結果の出力先フォルダ {viewModel.Result.OutputDirectory}（期待 {outputFolder}）"
                    );
                }

                if (closed != true)
                {
                    problems.Add(
                        $"GUI: 確定でダイアログを閉じない（{closed?.ToString() ?? "null"}）"
                    );
                }
            }

            problems.AddRange(
                CompareGates(
                    "GUI の確定の後に ViewModel が保持する値",
                    GuiOptionRules.Entered(input.Options),
                    GuiGates.Of(viewModel)
                )
            );
            problems.AddRange(dialogs.Errors.Select(error => $"GUI: エラーの表示 {error}"));

            var restoredViewModel = new CSharpGenerationDialogViewModel(
                store,
                null,
                provider,
                new RecordingDialogService()
            );
            problems.AddRange(
                CodegenEntrances.CompareOptions(
                    "GUI の保存した設定の復元",
                    actual,
                    restoredViewModel.ToOptions()
                )
            );
            problems.AddRange(
                CompareGates(
                    "GUI の保存した設定の復元（ViewModel の値）",
                    GuiGates.Of(expected),
                    GuiGates.Of(restoredViewModel)
                )
            );

            if (restoredViewModel.OutputPath != enteredOutputPath)
            {
                problems.Add(
                    $"GUI: 復元した出力先 {restoredViewModel.OutputPath}（期待 {enteredOutputPath}）"
                );
            }
        }
        finally
        {
            CodegenEntrances.DeleteQuietly(folder);
        }

        return Task.FromResult(
            new EntranceOutcome(
                actual,
                CodegenRunner.Generate(input.Diagram, input.TypeDialect, actual),
                problems,
                Stages
            )
        );
    }

    /// <summary>行のオプションを画面のプロパティから入れる</summary>
    private static void ApplyScreen(
        CSharpGenerationDialogViewModel viewModel,
        CodeGenerationOptions options,
        string outputFolder
    )
    {
        viewModel.RootNamespace = options.RootNamespace;

        // 出力モード（ラジオ・層別のチェック）を先に決め、出力先はモードに合わせて入れる（分割はフォルダ・非分割はファイル）
        if (options.LayeredOutput)
        {
            viewModel.LayeredOutput = true;
        }
        else if (options.SplitFilesByCategory)
        {
            viewModel.SplitFilesByCategory = true;
        }
        else
        {
            viewModel.MergeIntoSingleFile = true;
        }

        viewModel.OutputPath = viewModel.SplitFilesByCategory
            ? outputFolder
            : Path.Combine(outputFolder, options.OutputFileName);

        // DB アクセスのラジオ（両方は画面で表せない＝次元表の isValid が外す）
        if (options.GenerateRepositories && options.GenerateEfCoreRepositories)
        {
            throw new InvalidOperationException(
                "GUI の DB アクセスはラジオの排他で、両方を選べない（次元表の isValid が外す組）"
            );
        }

        // チェックは全部の欄が出る構成で入れる（隠れた欄にはチェックを入れられない）
        viewModel.DbAccessRepository = true;
        viewModel.TargetSqlServer = true;
        viewModel.TargetSqlite = true;

        viewModel.GenerateEditModels = options.GenerateEditModels;
        viewModel.GenerateMappers = options.GenerateMappers;
        viewModel.GenerateInMemoryRepositories = options.GenerateInMemoryRepositories;

        // HTTP 実装のチェックはリモート対応を含意する（親が ON になる）。親を OFF にすると子も OFF に戻る
        if (options.GenerateRemoteServices)
        {
            viewModel.GenerateRemoteServices = true;
        }
        else
        {
            viewModel.GenerateRemoteContracts = options.GenerateRemoteContracts;
        }

        viewModel.GenerateSyncSupport = options.GenerateSyncSupport;
        viewModel.GenerateValueObjects = options.GenerateValueObjects;
        viewModel.UseGuidKeyForStringPrimaryKey = options.UseGuidKeyForStringPrimaryKey;
        viewModel.UseRuntimePackages = options.UseRuntimePackages;
        viewModel.GenerateApiDocs = options.GenerateApiDocs;

        switch (options.ApiDocsLanguage)
        {
            case ApiDocsLanguage.Japanese:
                viewModel.ApiDocsJapanese = true;
                break;

            case ApiDocsLanguage.Both:
                viewModel.ApiDocsBoth = true;
                break;

            default:
                viewModel.ApiDocsEnglish = true;
                break;
        }

        viewModel.ExcludeUnboundedBinaryColumns = options.ExcludeUnboundedBinaryColumns;

        // 行の DB アクセスと対象 DB へ切り替える（ここで隠れる欄のチェックは、画面の値としては残る）
        if (options.GenerateRepositories)
        {
            viewModel.DbAccessRepository = true;
        }
        else if (options.GenerateEfCoreRepositories)
        {
            viewModel.DbAccessEfCore = true;
        }
        else
        {
            viewModel.DbAccessNone = true;
        }

        var dialects = CodegenOptionFacts.Dialects(options);
        viewModel.TargetSqlServer = dialects.Contains(SqlServerProvider.ProviderName);
        viewModel.TargetSqlite = dialects.Contains(SqliteProvider.ProviderName);

        // 欄は行が指定するものだけを、層フォルダ → サブフォルダ・ファイル名 → 名前空間の順に入れる
        // （層フォルダの変更は既定のままの名前空間を追従させるので、名前空間は最後）
        SetIfSpecified(
            options.DomainLayerDirectory,
            value => viewModel.DomainLayerDirectory = value
        );
        SetIfSpecified(
            options.PresentationLayerDirectory,
            value => viewModel.PresentationLayerDirectory = value
        );
        SetIfSpecified(
            options.InfrastructureLayerDirectory,
            value => viewModel.InfrastructureLayerDirectory = value
        );
        SetIfSpecified(
            options.ServerLayerDirectory,
            value => viewModel.ServerLayerDirectory = value
        );
        SetIfSpecified(
            options.CodeSubdirectory,
            value => viewModel.CodeSubdirectory = Padded(value)
        );
        SetIfSpecified(
            options.ApiDocsSubdirectory,
            value => viewModel.ApiDocsSubdirectory = Padded(value)
        );
        SetIfSpecified(options.ApiDocsFileName, value => viewModel.ApiDocsFileName = Padded(value));
        SetIfSpecified(options.RuntimeNamespace, value => viewModel.RuntimeNamespace = value);
        SetIfSpecified(options.EntityNamespace, value => viewModel.EntityNamespace = value);
        SetIfSpecified(options.EditModelNamespace, value => viewModel.EditModelNamespace = value);
        SetIfSpecified(options.MapperNamespace, value => viewModel.MapperNamespace = value);
        SetIfSpecified(options.RepositoryNamespace, value => viewModel.RepositoryNamespace = value);
        SetIfSpecified(
            options.ValueObjectNamespace,
            value => viewModel.ValueObjectNamespace = value
        );
    }

    /// <summary>行のオプションが指定する欄だけを入れる（null は「既定のまま」＝欄に触らない）</summary>
    private static void SetIfSpecified(string? value, Action<string> set)
    {
        if (value is not null)
        {
            set(value);
        }
    }

    /// <summary>欄へ入れる文字列の前後に空白を付ける（結果へはトリムして渡る）</summary>
    private static string Padded(string value) => " " + value + " ";

    /// <summary>画面に出ていないと持ち越さない 4 プロパティの値を比べ、違うものを名指しする</summary>
    private static IEnumerable<string> CompareGates(string what, GuiGates expected, GuiGates actual)
    {
        if (expected.RemoteContracts != actual.RemoteContracts)
        {
            yield return $"{what}: リモート対応（GenerateRemoteContracts）が {actual.RemoteContracts}（期待 {expected.RemoteContracts}）";
        }

        if (expected.RemoteServices != actual.RemoteServices)
        {
            yield return $"{what}: HTTP 実装（GenerateRemoteServices）が {actual.RemoteServices}（期待 {expected.RemoteServices}）";
        }

        if (expected.SyncSupport != actual.SyncSupport)
        {
            yield return $"{what}: 同期支援（GenerateSyncSupport）が {actual.SyncSupport}（期待 {expected.SyncSupport}）";
        }

        if (expected.ExcludeUnboundedBinary != actual.ExcludeUnboundedBinary)
        {
            yield return $"{what}: 無制限バイナリ列の除外（ExcludeUnboundedBinaryColumns）が {actual.ExcludeUnboundedBinary}（期待 {expected.ExcludeUnboundedBinary}）";
        }
    }

    /// <summary>エラーの表示だけを記録するダイアログ（確認は断る）</summary>
    private sealed class RecordingDialogService : IDialogService
    {
        public List<string> Errors { get; } = [];

        public bool Confirm(string message, string title) => false;

        public bool ConfirmWarning(string message, string title) => false;

        public bool ConfirmWarningDetails(string message, string details, string title) => false;

        public void ShowInformation(string message, string title) { }

        public void ShowError(string message, string title) => Errors.Add(message);

        public void ShowInformationDetails(string message, string details, string title) { }

        public void ShowErrorDetails(string message, string details, string title) =>
            Errors.Add(message + " " + details);
    }
}

/// <summary>
/// GUI の入口が結果に持ち越すオプションの規則（宣言の表）
/// </summary>
/// <remarks>
/// <para>
/// 出どころは src/QuickER.CodeGen.UI/CLAUDE.md「画面に出ていないオプション」と、画面の連動（src/QuickER.CodeGen.CSharp/CLAUDE.md
/// 「層別フォルダ出力」：層別 ON は分割を含意し、画面では分割のラジオも立つ）。
/// </para>
/// <list type="table">
///   <listheader><term>オプション</term><description>画面に出る条件（出ていなければ false で持ち越す）</description></listheader>
///   <item><term>GenerateRemoteContracts / GenerateRemoteServices</term><description>ShowRemoteContracts＝QuickER 版 Repository か EF Core</description></item>
///   <item><term>GenerateSyncSupport</term><description>ShowSyncSupport＝QuickER 版 Repository かつ SQL Server と SQLite の両方</description></item>
///   <item><term>ExcludeUnboundedBinaryColumns</term><description>ShowExcludeUnboundedBinary＝QuickER 版 Repository</description></item>
///   <item><term>SplitFilesByCategory</term><description>層別 ON なら true（画面の含意）</description></item>
/// </list>
/// </remarks>
internal static class GuiOptionRules
{
    /// <summary>行のオプションの構成で、4 プロパティの欄が画面に出るか（表の「画面に出る条件」）</summary>
    /// <param name="options">行のオプション</param>
    public static GuiGates Visibility(CodeGenerationOptions options)
    {
        var dialects = CodegenOptionFacts.Dialects(options);
        var showRemote = options.GenerateRepositories || options.GenerateEfCoreRepositories;
        var showSync =
            options.GenerateRepositories
            && dialects.Contains(SqlServerProvider.ProviderName)
            && dialects.Contains(SqliteProvider.ProviderName);

        return new GuiGates(showRemote, showRemote, showSync, options.GenerateRepositories);
    }

    /// <summary>画面に入れた後に ViewModel が持つ 4 プロパティの値（隠れても保持する。HTTP 実装はリモート対応を含意する）</summary>
    /// <param name="options">行のオプション</param>
    public static GuiGates Entered(CodeGenerationOptions options) =>
        new(
            options.GenerateRemoteContracts || options.GenerateRemoteServices,
            options.GenerateRemoteServices,
            options.GenerateSyncSupport,
            options.ExcludeUnboundedBinaryColumns
        );

    /// <summary>行のオプションから、GUI の <c>ToOptions()</c> が返すべきオプションを作る</summary>
    /// <param name="options">行のオプション</param>
    public static CodeGenerationOptions Expected(CodeGenerationOptions options)
    {
        var shown = Visibility(options);

        return options with
        {
            GenerateRemoteContracts = shown.RemoteContracts && options.GenerateRemoteContracts,
            GenerateRemoteServices = shown.RemoteServices && options.GenerateRemoteServices,
            GenerateSyncSupport = shown.SyncSupport && options.GenerateSyncSupport,
            ExcludeUnboundedBinaryColumns =
                shown.ExcludeUnboundedBinary && options.ExcludeUnboundedBinaryColumns,
            SplitFilesByCategory = options.SplitFilesByCategory || options.LayeredOutput,
        };
    }
}

/// <summary>画面に出ていないと持ち越さない 4 プロパティ（リモート対応・HTTP 実装・同期支援・無制限バイナリ列の除外）の組</summary>
/// <remarks>表示の条件・ViewModel が保持する値・結果の値を同じ形で比べる</remarks>
internal sealed record GuiGates(
    bool RemoteContracts,
    bool RemoteServices,
    bool SyncSupport,
    bool ExcludeUnboundedBinary
)
{
    /// <summary>ViewModel が持つ値</summary>
    public static GuiGates Of(CSharpGenerationDialogViewModel viewModel) =>
        new(
            viewModel.GenerateRemoteContracts,
            viewModel.GenerateRemoteServices,
            viewModel.GenerateSyncSupport,
            viewModel.ExcludeUnboundedBinaryColumns
        );

    /// <summary>オプションの値</summary>
    public static GuiGates Of(CodeGenerationOptions options) =>
        new(
            options.GenerateRemoteContracts,
            options.GenerateRemoteServices,
            options.GenerateSyncSupport,
            options.ExcludeUnboundedBinaryColumns
        );
}

/// <summary>CLI と MCP の入口が共有する、設定 JSON と書き出したファイルの読み戻し・直接の生成との照合</summary>
internal static class FileEntranceSupport
{
    /// <summary>行のオプションを設定 JSON（<c>quicker generate --config</c> が読む形・キーは <see cref="GenerationConfigSchema"/>）にする</summary>
    /// <param name="options">生成オプション</param>
    public static string ConfigJson(CodeGenerationOptions options)
    {
        var node = new JsonObject();

        foreach (var property in OptionKeys.Writable(typeof(CodeGenerationOptions)))
        {
            node[property.Name] = property.GetValue(options) switch
            {
                null => null,
                bool flag => JsonValue.Create(flag),
                string text => JsonValue.Create(text),
                Enum value => JsonValue.Create(value.ToString()),
                IEnumerable<string> list => new JsonArray(
                    list.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()
                ),
                var other => throw new InvalidOperationException(
                    $"設定 JSON に書けない型のキー {property.Name}: {other.GetType().Name}"
                ),
            };
        }

        return node.ToJsonString();
    }

    /// <summary>図を図ファイルへ保存する</summary>
    public static void SaveDiagram(string path, QuickER.Model.ErDiagram diagram) =>
        JsonStorageService.SaveAtomic(path, new DiagramDocument { Schema = diagram });

    /// <summary>出力フォルダの全ファイルを（相対フォルダ・ファイル名 → 内容）で読み戻す</summary>
    public static Dictionary<string, string> ReadTree(string outputFolder)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!Directory.Exists(outputFolder))
        {
            return files;
        }

        foreach (
            var path in Directory.EnumerateFiles(outputFolder, "*", SearchOption.AllDirectories)
        )
        {
            var relative = Path.GetRelativePath(outputFolder, path).Replace('\\', '/');
            files[relative] = File.ReadAllText(path);
        }

        return files;
    }

    /// <summary>生成結果のファイルを（相対フォルダ・ファイル名 → 内容）にする</summary>
    public static Dictionary<string, string> ToTree(CodeGenerationResult result) =>
        result.Files.ToDictionary(
            file =>
                string.IsNullOrEmpty(file.RelativeDirectory)
                    ? file.FileName
                    : file.RelativeDirectory.Replace('\\', '/').TrimEnd('/') + "/" + file.FileName,
            file => file.Content,
            StringComparer.Ordinal
        );

    /// <summary>入口の書き出しを直接の生成と照合する（関係＝入口どうしの一致）</summary>
    /// <param name="entrance">入口の名前</param>
    /// <param name="direct">直接の生成の結果</param>
    /// <param name="succeeded">入口が成功を返したか</param>
    /// <param name="written">入口が書き出したファイル</param>
    /// <param name="log">入口の出力（失敗の文に添える）</param>
    public static IReadOnlyList<string> CompareWithDirect(
        string entrance,
        CodeGenerationResult direct,
        bool succeeded,
        IReadOnlyDictionary<string, string> written,
        string log
    )
    {
        var problems = new List<string>();

        if (direct.HasErrors)
        {
            if (succeeded)
            {
                problems.Add($"{entrance}: 直接の生成はエラーなのに入口は成功を返した");
            }

            if (written.Count > 0)
            {
                problems.Add(
                    $"{entrance}: 直接の生成はエラーなのに入口はファイルを書いた（{string.Join(", ", written.Keys)}）"
                );
            }

            return problems;
        }

        if (!succeeded)
        {
            problems.Add($"{entrance}: 直接の生成は成功するのに入口は失敗した: {log}");
            return problems;
        }

        var expected = ToTree(direct);

        foreach (var missing in expected.Keys.Except(written.Keys).Order(StringComparer.Ordinal))
        {
            problems.Add($"{entrance}: 直接の生成にあって入口が書かなかったファイル {missing}");
        }

        foreach (var extra in written.Keys.Except(expected.Keys).Order(StringComparer.Ordinal))
        {
            problems.Add($"{entrance}: 入口だけが書いたファイル {extra}");
        }

        foreach (var key in expected.Keys.Intersect(written.Keys).Order(StringComparer.Ordinal))
        {
            if (!string.Equals(expected[key], written[key], StringComparison.Ordinal))
            {
                problems.Add($"{entrance}: {key} の内容が直接の生成と違う");
            }
        }

        return problems;
    }
}

/// <summary>cli：設定 JSON と図ファイルを一時フォルダへ書き、<c>quicker generate</c> をプロセス内で呼ぶ（<c>SampleCliRunner</c> の流儀）</summary>
internal sealed class CliEntrance : ICodegenEntrance
{
    /// <inheritdoc/>
    public string AxisValue => CodegenEntrances.NameOf(CodegenEntrance.Cli);

    /// <inheritdoc/>
    public IReadOnlyList<string> Stages => [CodegenEntrances.EntranceRelationStage];

    /// <inheritdoc/>
    public async Task<EntranceOutcome> RunAsync(CodegenInput input)
    {
        var direct = CodegenRunner.Generate(input.Diagram, input.TypeDialect, input.Options);
        var folder = CodegenEntrances.NewTemporaryFolder();

        try
        {
            var schema = Path.Combine(folder, "diagram.json");
            var config = Path.Combine(folder, "quicker.json");
            var output = Path.Combine(folder, "out");
            FileEntranceSupport.SaveDiagram(schema, input.Diagram);
            File.WriteAllText(config, FileEntranceSupport.ConfigJson(input.Options));

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            // コンソールの出力エンコーディングを触らない注入版を使う（並列実行中の他テストと競合しない）
            var exitCode = await CliApp.InvokeAsync(
                [
                    "generate",
                    "--schema",
                    schema,
                    "--out",
                    output,
                    "--provider",
                    input.TypeDialect,
                    "--config",
                    config,
                ],
                stdout,
                stderr
            );

            var problems = FileEntranceSupport.CompareWithDirect(
                "CLI",
                direct,
                exitCode == 0,
                FileEntranceSupport.ReadTree(output),
                $"exit={exitCode.ToString(CultureInfo.InvariantCulture)} stderr={stderr}"
            );

            return new EntranceOutcome(input.Options, direct, problems, Stages);
        }
        finally
        {
            CodegenEntrances.DeleteQuietly(folder);
        }
    }
}

/// <summary>mcp：同じ設定 JSON で <c>CodeGenToolSet.Create().Execute("generate_csharp", …)</c> を呼ぶ（<c>CodeGenToolSetTests</c> の流儀）</summary>
internal sealed class McpEntrance : ICodegenEntrance
{
    private static readonly Lazy<QuickER.Mcp.McpToolSet> ToolSet = new(CodeGenToolSet.Create);

    /// <inheritdoc/>
    public string AxisValue => CodegenEntrances.NameOf(CodegenEntrance.Mcp);

    /// <inheritdoc/>
    public IReadOnlyList<string> Stages => [CodegenEntrances.EntranceRelationStage];

    /// <inheritdoc/>
    public Task<EntranceOutcome> RunAsync(CodegenInput input)
    {
        var direct = CodegenRunner.Generate(input.Diagram, input.TypeDialect, input.Options);
        var folder = CodegenEntrances.NewTemporaryFolder();

        try
        {
            var schema = Path.Combine(folder, "diagram.json");
            var config = Path.Combine(folder, "quicker.json");
            var output = Path.Combine(folder, "out");
            FileEntranceSupport.SaveDiagram(schema, input.Diagram);
            File.WriteAllText(config, FileEntranceSupport.ConfigJson(input.Options));

            var (text, success) = ToolSet.Value.Execute(
                "generate_csharp",
                JsonSerializer.Serialize(
                    new
                    {
                        file = schema,
                        out_dir = output,
                        config,
                        provider = input.TypeDialect,
                    }
                )
            );

            var problems = FileEntranceSupport.CompareWithDirect(
                "MCP",
                direct,
                success,
                FileEntranceSupport.ReadTree(output),
                text
            );

            return Task.FromResult(new EntranceOutcome(input.Options, direct, problems, Stages));
        }
        finally
        {
            CodegenEntrances.DeleteQuietly(folder);
        }
    }
}
