namespace QuickER.AI.Mock;

/// <summary>WPF (.NET) ターゲットのプロファイル。csproj／README とプロンプトの WPF 固有文面を提供する。</summary>
/// <remarks>
/// ここで返すフラグメントは、リファクタリング前に <see cref="MockProjectScaffoldService"/> と
/// <see cref="MockProjectPromptBuilder"/> に直書きされていた WPF 固有文面をそのまま切り出したもの（挙動不変）。
/// </remarks>
internal sealed class WpfMockProjectTargetProfile : MockProjectTargetProfile
{
    /// <inheritdoc />
    internal override MockProjectTarget Target => MockProjectTarget.Wpf;

    /// <inheritdoc />
    internal override string UiFileSearchPattern => "*.xaml";

    // ── スキャフォールド差分 ──

    /// <inheritdoc />
    /// <remarks>WPF・net10.0-windows・必要な PackageReference。</remarks>
    internal override string BuildCsproj(string rootNamespace, string? repositoryDialect)
    {
        var adoPackage = repositoryDialect switch
        {
            "sqlserver" =>
                "    <PackageReference Include=\"Microsoft.Data.SqlClient\" Version=\"7.0.1\" />\n",
            "sqlite" =>
                "    <PackageReference Include=\"Microsoft.Data.Sqlite\" Version=\"10.0.0\" />\n",
            _ => string.Empty,
        };

        return $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <RootNamespace>{rootNamespace}</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""CommunityToolkit.Mvvm"" Version=""8.4.2"" />
    <PackageReference Include=""Microsoft.Extensions.DependencyInjection"" Version=""10.0.0"" />
{adoPackage}  </ItemGroup>

</Project>
";
    }

    /// <inheritdoc />
    /// <remarks>AI・人間の双方向けの規約ドキュメント（WPF 実装規約・起動時 DI・実 DB 切り替え手順）。</remarks>
    internal override string BuildReadme(
        string projectName,
        string rootNamespace,
        string? repositoryDialect
    )
    {
        // DI 登録はエンジン別（AddGenerated{方言}Repositories）で統一されているため、方言別名で案内する
        var dialectSwitchGuide = repositoryDialect switch
        {
            "sqlserver" =>
                "3. 実 DB（SQL Server）へ切り替えるには、`AddGeneratedInMemoryRepositories()` を "
                    + "`AddGeneratedSqlServerRepositories(接続文字列)` に差し替えます。",
            "sqlite" =>
                "3. 実 DB（SQLite）へ切り替えるには、`AddGeneratedInMemoryRepositories()` を "
                    + "`AddGeneratedSqliteRepositories(接続文字列)` に差し替えます。",
            _ =>
                "3. 実 DB へ切り替える場合は、QuickER で対応方言（SQL Server / SQLite）のQuickER 版 Repository を"
                    + "生成し直し、`AddGeneratedInMemoryRepositories()` を対応する DI 登録へ差し替えます。",
        };

        return $@"# {projectName}

QuickER がモックフォルダから生成した WPF モックプロジェクトです。
データ層（`Generated/` 配下）は QuickER が決定的に生成しており、UI 層（App / MainWindow / ビュー・ビューモデル）は
`design/mock/` のデザイン仕様（複数画面）に沿って実装します。

## プロジェクト構成

このフォルダは Visual Studio 標準構成です。出力フォルダ直下に `{projectName}.sln`（ソリューション）があり、
プロジェクト一式は `{projectName}/` フォルダ配下にあります。

- `{projectName}.sln` … ソリューションファイル（出力フォルダ直下）。`dotnet build` はこの場所で実行すれば sln を拾います。
- `{projectName}/{projectName}.csproj` … WPF（net10.0-windows）のプロジェクトファイル。
- `{projectName}/Generated/` … QuickER が生成したデータ層（Entity / EditModel / Mapper / Repository 契約・実装 / インメモリ実装）。
  **このフォルダは自動生成コードのため、手で編集・削除しないでください（再生成で上書きされます）。**
- `{projectName}/design/mock/` … 再現すべき画面のデザイン仕様（モックフォルダ）。
  - `design/mock/mock.json` … 画面一覧・画面遷移・改訂履歴のマニフェスト。まずこれを読んで全体像を把握します。
  - `design/mock/*.html` … 1 ファイル＝1 画面のデザイン仕様（画面構成・項目）。
  - `design/mock/style.css` … 全画面が共有するデザインシステム（共有 CSS）。
- App / MainWindow / ビュー・ビューモデル等の UI 層は `{projectName}/` フォルダ配下に追加してください。

## 実装の規約

- UI は **CommunityToolkit.Mvvm** を用いた MVVM（`ObservableObject` / `RelayCommand` / `[ObservableProperty]`）で実装します。
- データアクセスは `Generated/` の **`I{{Entity}}Repository`** を DI 経由で受け取って使います
  （リポジトリの具象を直接 `new` しないでください）。
- まず `design/mock/mock.json` で画面一覧と画面遷移（transitions）を把握し、各 `*.html` の構成・項目を WPF の
  ネイティブ UI で忠実に再現します（HTML をそのまま埋め込むのではなく、WPF のウィンドウ／ページ／ユーザーコントロール
  へ作り直し、マニフェストの遷移をナビゲーションとして実装します）。

## 起動時の DI 登録

`App` の起動で `Microsoft.Extensions.DependencyInjection` のコンテナを構成し、`{rootNamespace}.Generated` の
**`AddGeneratedInMemoryRepositories()`** を呼びます（サンプルデータ入りのインメモリ実装が登録され、実 DB なしで動作します）。

```csharp
var services = new ServiceCollection();
services.AddGeneratedInMemoryRepositories(seedSampleData: true);
// ビュー・ビューモデルを登録
var provider = services.BuildServiceProvider();
```

## 実 DB への切り替え手順

1. QuickER の DDL 生成機能で対象 DB のスキーマ（DDL）を出力し、DB に適用します。
2. 接続文字列を用意します。
{dialectSwitchGuide}
";
    }

    // ── エージェント型 system プロンプトのターゲット差分 ──

    /// <inheritdoc />
    internal override string SystemScaffoldNoun => "WPF プロジェクトの雛形とデータ層のコード";

    /// <inheritdoc />
    internal override string SystemScreenReproductionRule =>
        "各画面の画面構成・項目を WPF で忠実に再現し、マニフェストの遷移をナビゲーションとして実装します（HTML をそのまま埋め込むのではなく、WPF のネイティブ UI で作り直します）。";

    /// <inheritdoc />
    internal override string SystemUiFrameworkRules =>
        "- UI は CommunityToolkit.Mvvm を用いた MVVM（ObservableObject / RelayCommand / ObservableProperty）で実装してください。\r\n"
        + "- 各画面は必ず XAML の View（Window / Page / UserControl の .xaml ＋コードビハインド）として定義してください。C# コードだけでコントロールツリーを組み立てる実装（new したコントロールをコードで並べる方式）は禁止です。";

    /// <inheritdoc />
    internal override string SystemWorkflowSteps(string projectName) =>
        $"- App.xaml / App.xaml.cs 等の UI 層のソースは {projectName}/ フォルダ配下（csproj と同じ場所）に追加します。\r\n"
        + "- App.xaml / App.xaml.cs で DI を構成し、MainWindow とビュー・ビューモデルを実装します。\r\n"
        + $"- {projectName}/{MockProjectScaffoldService.DesignFolderRelativePath}/mock.json の各画面（一覧・登録／編集等）を WPF のウィンドウ／ページ／ユーザーコントロールとして再現し、transitions で宣言された画面遷移をナビゲーションとして実装します。";

    // ── エージェント型 初回プロンプトのターゲット差分 ──

    /// <inheritdoc />
    internal override string UiLayerName => "WPF UI 層";

    /// <inheritdoc />
    internal override string PromptImplementStep =>
        "App.xaml(.cs)・MainWindow・各ビュー／ビューモデルを CommunityToolkit.Mvvm の MVVM で実装する。各画面を WPF のウィンドウ／ページ／ユーザーコントロールとして再現し、mock.json の遷移をナビゲーションとして実装する。";

    /// <inheritdoc />
    internal override string PromptViewCriterion =>
        "各画面が XAML の View（.xaml）として存在し、対応する ViewModel（CommunityToolkit.Mvvm）と組になっている。";

    // ── API キー型プロンプトのターゲット差分 ──

    /// <inheritdoc />
    internal override string ApiKeyUiFrameworkRules =>
        "- UI は CommunityToolkit.Mvvm を用いた MVVM（ObservableObject / RelayCommand / [ObservableProperty]）で実装してください。\r\n"
        + "- 各画面は必ず XAML の View（Window / Page / UserControl の .xaml ＋コードビハインド）として定義してください。C# コードだけでコントロールツリーを組み立てる実装（new したコントロールをコードで並べる方式）は禁止です。";

    /// <inheritdoc />
    internal override string ApiKeyScreenReproductionRule =>
        "各画面はデザイン仕様（HTML）を WPF のネイティブ UI で忠実に再現し、宣言された画面遷移をナビゲーションとして実装してください（HTML をそのまま埋め込まない）。";

    /// <inheritdoc />
    internal override string ApiKeyCommonEmitInstructions(string projectName) =>
        $"- {projectName}/App.xaml / {projectName}/App.xaml.cs（Microsoft.Extensions.DependencyInjection で DI を構成し、AddGeneratedInMemoryRepositories(seedSampleData: true) を登録・起動ウィンドウを表示）\r\n"
        + $"- {projectName}/MainWindow.xaml / {projectName}/MainWindow.xaml.cs（画面を切り替えるシェル）とナビゲーションの骨格\r\n"
        + "- 画面共通のインフラ（ナビゲーションサービス・ViewModelBase 等・必要なら）\r\n"
        + "\r\n"
        + "個別画面のビュー／ビューモデルは次のターン以降で 1 画面ずつ実装します。ここではまず起動して画面を切り替えられる土台を作ってください。";

    /// <inheritdoc />
    internal override string ApiKeyScreenInstruction(string projectName) =>
        $"この画面のデザイン仕様（HTML）を WPF のネイティブ UI で忠実に再現し、View と ViewModel を emit_file で提出してください（{projectName}/ フォルダ配下）。宣言された遷移はナビゲーションとして実装します。";

    /// <inheritdoc />
    internal override string ApiKeyEmitPathExamples(string projectName) =>
        $"{projectName}/App.xaml・{projectName}/Views/OrderListView.xaml";
}
