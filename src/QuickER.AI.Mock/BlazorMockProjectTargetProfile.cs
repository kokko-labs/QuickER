namespace QuickER.AI.Mock;

/// <summary>Blazor Web App (.NET) ターゲットのプロファイル。csproj／README とプロンプトの Blazor 固有文面を提供する。</summary>
/// <remarks>
/// 技術構成は「Blazor Web App（.NET の統合テンプレート）・グローバル InteractiveServer レンダーモード・単一プロジェクト・
/// net10.0・追加パッケージ不要」（DI・ASP.NET Core ランタイムは共有フレームワークに同梱されるため、方言 ADO 以外の
/// PackageReference は要らない）。WPF 版（<see cref="WpfMockProjectTargetProfile"/>）と対称構造で、各フラグメントは
/// WPF 版の対応する一文と 1:1 に対応する。
/// </remarks>
internal sealed class BlazorMockProjectTargetProfile : MockProjectTargetProfile
{
    /// <inheritdoc />
    internal override MockProjectTarget Target => MockProjectTarget.Blazor;

    /// <inheritdoc />
    internal override string UiFileSearchPattern => "*.razor";

    // ── スキャフォールド差分 ──

    /// <inheritdoc />
    /// <remarks>Blazor Web App・net10.0（Microsoft.NET.Sdk.Web）。PackageReference は方言 ADO のみ（無ければ ItemGroup ごと省略）。</remarks>
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

        // 方言 ADO パッケージが無ければ ItemGroup ごと省略する（空要素を出さない）。
        // DI・ASP.NET Core ランタイムは Microsoft.NET.Sdk.Web の共有フレームワークに含まれるため追加参照は不要。
        var packageItemGroup =
            adoPackage.Length == 0
                ? string.Empty
                : $"\n  <ItemGroup>\n{adoPackage}  </ItemGroup>\n";

        return $@"<Project Sdk=""Microsoft.NET.Sdk.Web"">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>{rootNamespace}</RootNamespace>
  </PropertyGroup>
{packageItemGroup}</Project>
";
    }

    /// <inheritdoc />
    /// <remarks>AI・人間の双方向けの規約ドキュメント（Blazor 実装規約・起動時 DI・実 DB 切り替え手順）。</remarks>
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

QuickER がモックフォルダから生成した Blazor Web App のモックプロジェクトです。
データ層（`Generated/` 配下）は QuickER が決定的に生成しており、UI 層（Program.cs / Components 配下）は
`design/mock/` のデザイン仕様（複数画面）に沿って実装します。

## プロジェクト構成

このフォルダは Visual Studio 標準構成です。出力フォルダ直下に `{projectName}.sln`（ソリューション）があり、
プロジェクト一式は `{projectName}/` フォルダ配下にあります。

- `{projectName}.sln` … ソリューションファイル（出力フォルダ直下）。`dotnet build` はこの場所で実行すれば sln を拾います。
- `{projectName}/{projectName}.csproj` … Blazor Web App（net10.0・Microsoft.NET.Sdk.Web）のプロジェクトファイル。
- `{projectName}/Generated/` … QuickER が生成したデータ層（Entity / EditModel / Mapper / Repository 契約・実装 / インメモリ実装）。
  **このフォルダは自動生成コードのため、手で編集・削除しないでください（再生成で上書きされます）。**
- `{projectName}/design/mock/` … 再現すべき画面のデザイン仕様（モックフォルダ）。
  - `design/mock/mock.json` … 画面一覧・画面遷移・改訂履歴のマニフェスト。まずこれを読んで全体像を把握します。
  - `design/mock/*.html` … 1 ファイル＝1 画面のデザイン仕様（画面構成・項目）。
  - `design/mock/style.css` … 全画面が共有するデザインシステム（共有 CSS）。
- Program.cs / Components 配下（App.razor / Routes.razor / レイアウト / 画面ページ）等の UI 層は `{projectName}/` フォルダ配下に追加してください。

## 実装の規約

- 各画面は `@page` ルート付きの Razor コンポーネント（`Components/Pages/` 配下）として実装します。
- UI は **グローバル InteractiveServer レンダーモード**で動かします（`App.razor` で `<Routes @rendermode=""InteractiveServer"" />` を指定し、
  ボタン・フォーム等の対話的な画面がサーバー側レンダリングで動くようにします）。
- データアクセスは `Generated/` の **`I{{Entity}}Repository`** を DI 経由で受け取って使います
  （リポジトリの具象を直接 `new` しないでください）。
- まず `design/mock/mock.json` で画面一覧と画面遷移（transitions）を把握し、各 `*.html` の構成・項目を Razor コンポーネントで
  忠実に再現します（デザイン仕様の HTML 構造とクラス名は可能な限りそのまま移植し、`design/mock/style.css` は `wwwroot/style.css` へ
  コピーして共有デザインを再利用します）。遷移は `NavLink` / `NavigationManager` によるページ遷移として実装します。

## 起動時の DI 登録

`Program.cs` で Blazor Web App のサービスと `{rootNamespace}.Generated` の **`AddGeneratedInMemoryRepositories()`** を登録し、
InteractiveServer レンダーモードでコンポーネントをマップします（サンプルデータ入りのインメモリ実装が登録され、実 DB なしで動作します）。

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddGeneratedInMemoryRepositories(seedSampleData: true);

var app = builder.Build();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
```

起動は `dotnet run`（表示された localhost の URL をブラウザで開きます）。

## 実 DB への切り替え手順

1. QuickER の DDL 生成機能で対象 DB のスキーマ（DDL）を出力し、DB に適用します。
2. 接続文字列を用意します。
{dialectSwitchGuide}
";
    }

    // ── エージェント型 system プロンプトのターゲット差分 ──

    /// <inheritdoc />
    internal override string SystemScaffoldNoun =>
        "Blazor Web App プロジェクトの雛形とデータ層のコード";

    /// <inheritdoc />
    internal override string SystemScreenReproductionRule =>
        "各画面の画面構成・項目を Blazor の Razor コンポーネントで忠実に再現し、マニフェストの遷移をページ遷移（ルーティング）として実装します（デザイン仕様の HTML 構造とクラス名は可能な限りそのまま移植し、style.css は wwwroot/style.css へコピーして共有デザインを再利用します）。";

    /// <inheritdoc />
    internal override string SystemUiFrameworkRules =>
        "- UI は Blazor Web App（グローバル InteractiveServer レンダーモード）で実装してください。App.razor で `<Routes @rendermode=\"InteractiveServer\" />` を指定し、対話的な画面（ボタン・フォーム）がサーバー側レンダリングで動くようにします。\r\n"
        + "- 各画面は必ず `@page` ルート付きの Razor コンポーネント（.razor）として定義してください。C# コードだけで RenderTreeBuilder / RenderFragment を手組みする実装は禁止です。";

    /// <inheritdoc />
    internal override string SystemWorkflowSteps(string projectName) =>
        $"- UI 層のソース（Program.cs / Components/ / wwwroot/）は {projectName}/ フォルダ配下（csproj と同じ場所）に追加します。\r\n"
        + "- Program.cs で DI（AddRazorComponents().AddInteractiveServerComponents() と AddGeneratedInMemoryRepositories）を構成し、Components/App.razor・Routes.razor・レイアウトを実装します。App.razor は `<!DOCTYPE html>` を含むルート文書として、wwwroot/style.css の参照と `_framework/blazor.web.js` の script を含めます（`app.UseAntiforgery()` も忘れないでください）。\r\n"
        + $"- {projectName}/{MockProjectScaffoldService.DesignFolderRelativePath}/mock.json の各画面（一覧・登録／編集等）を `@page` ルート付きの Razor コンポーネント（Components/Pages/）として再現し、transitions で宣言された画面遷移を NavLink / NavigationManager のページ遷移として実装します。";

    // ── エージェント型 初回プロンプトのターゲット差分 ──

    /// <inheritdoc />
    internal override string UiLayerName => "Blazor UI 層";

    /// <inheritdoc />
    internal override string PromptImplementStep =>
        "Program.cs・Components/App.razor・Routes.razor・レイアウト・各画面の Razor コンポーネントを実装する。各画面を `@page` ルート付きのページ（Components/Pages/）として再現し、mock.json の遷移をページ遷移として実装する。デザイン仕様の HTML 構造と style.css（wwwroot へコピー）は可能な限りそのまま移植する。";

    /// <inheritdoc />
    internal override string PromptViewCriterion =>
        "各画面が `@page` ルート付きの Razor コンポーネント（.razor）として存在し、グローバル InteractiveServer レンダーモードで対話的に動作する。";

    // ── API キー型プロンプトのターゲット差分 ──

    /// <inheritdoc />
    internal override string ApiKeyUiFrameworkRules =>
        "- UI は Blazor Web App（グローバル InteractiveServer レンダーモード）で実装してください。App.razor で `<Routes @rendermode=\"InteractiveServer\" />` を指定し、対話的な画面（ボタン・フォーム）がサーバー側レンダリングで動くようにします。\r\n"
        + "- 各画面は必ず `@page` ルート付きの Razor コンポーネント（.razor）として定義してください。C# コードだけで RenderTreeBuilder / RenderFragment を手組みする実装は禁止です。";

    /// <inheritdoc />
    internal override string ApiKeyScreenReproductionRule =>
        "各画面はデザイン仕様（HTML）の構造・クラス名を可能な限りそのまま Razor コンポーネントへ移植し、宣言された画面遷移をページ遷移（ルーティング）として実装してください（共有 style.css は wwwroot/style.css として参照します）。";

    /// <inheritdoc />
    internal override string ApiKeyCommonEmitInstructions(string projectName) =>
        $"- {projectName}/Program.cs（AddRazorComponents().AddInteractiveServerComponents() と AddGeneratedInMemoryRepositories(seedSampleData: true) を登録し、app.UseAntiforgery() の後に MapRazorComponents<App>().AddInteractiveServerRenderMode() を呼ぶ）\r\n"
        + $"- {projectName}/Components/App.razor（`<!DOCTYPE html>` を含むルート文書。wwwroot/style.css を参照し、`<Routes @rendermode=\"InteractiveServer\" />` と `_framework/blazor.web.js` の script を含める）\r\n"
        + $"- {projectName}/Components/Routes.razor・レイアウト（{projectName}/Components/Layout/MainLayout.razor）・{projectName}/Components/_Imports.razor\r\n"
        + $"- {projectName}/wwwroot/style.css（共有デザインシステムをそのまま収載）\r\n"
        + "\r\n"
        + "個別画面のページは次のターン以降で 1 画面ずつ実装します。ここではまず起動して画面を切り替えられる土台を作ってください。";

    /// <inheritdoc />
    internal override string ApiKeyScreenInstruction(string projectName) =>
        $"この画面のデザイン仕様（HTML）の構造・クラス名を可能な限りそのまま Razor コンポーネントへ移植し、`@page` ルート付きのページとして emit_file で提出してください（{projectName}/Components/Pages/ 配下）。宣言された遷移はページ遷移として実装します。";

    /// <inheritdoc />
    internal override string ApiKeyEmitPathExamples(string projectName) =>
        $"{projectName}/Components/Pages/OrderList.razor・{projectName}/wwwroot/style.css";
}
