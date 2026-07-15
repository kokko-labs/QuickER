using QuickER.CodeGen.CSharp;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// ランタイム NuGet パッケージのチェックイン済みソース（<c>src/QuickER.Runtime*/*.g.cs</c>）が、
/// 現在のテンプレート・<see cref="RuntimePackageSourceRenderer"/> の出力と文字列完全一致することを検証する
/// ドリフト検知テスト。
/// </summary>
/// <remarks>
/// <para>
/// パッケージソースの正本は <c>Templates/CSharpRuntime.scriban</c>（既存フィクスチャと同一テンプレート）で、
/// レンダラーが「空図＋全機能 ON＋固定名前空間＋public 化」で書き出す。テンプレートを変更すると
/// チェックイン済みソースが古くなり得るため、このテストで乖離を検出する。
/// </para>
/// <para>
/// 対象は 4 パッケージのソース（Core / SqlServer / Sqlite / EntityFrameworkCore）。
/// 検証・再生成の実処理は既存フィクスチャと同じ <see cref="FixtureDriftHarness"/> に集約しており、
/// <c>QUICKER_REGEN_FIXTURES=1</c> のとき既存フィクスチャと同一経路でこの 4 ソースも上書き再生成される。
/// テンプレート変更後の再生成手順は同ハーネスの docstring と失敗メッセージを参照。
/// </para>
/// </remarks>
public sealed class RuntimePackageSourceDriftTests
{
    private static readonly RuntimePackageSourceRenderer Renderer = new();

    /// <summary>コア（<c>QuickER.Runtime</c>・BCL のみ）のチェックイン済みソースが現在の出力と一致する</summary>
    [Fact(
        DisplayName = "コアパッケージのチェックイン済みソースが現在のレンダラー出力と完全一致する（ドリフト検知）"
    )]
    public void CoreSource_MatchesRenderedOutput()
    {
        FixtureDriftHarness.VerifyOrRegeneratePackageSource(
            Renderer.RenderCore(),
            "src/QuickER.Runtime/QuickERRuntime.g.cs",
            "コアパッケージのチェックイン済みソースが現在のテンプレート出力と乖離しています。"
                + "テンプレート（QuickER.CodeGen.CSharp/Templates/CSharpRuntime.scriban 等）を変更した場合は再生成が必要です。"
        );
    }

    /// <summary>QuickER の SQL Server エンジン（<c>QuickER.Runtime.SqlServer</c>）のチェックイン済みソースが一致する</summary>
    [Fact(
        DisplayName = "SqlServer パッケージのチェックイン済みソースが現在のレンダラー出力と完全一致する（ドリフト検知）"
    )]
    public void SqlServerSource_MatchesRenderedOutput()
    {
        FixtureDriftHarness.VerifyOrRegeneratePackageSource(
            Renderer.RenderSqlServer(),
            "src/QuickER.Runtime.SqlServer/QuickERRuntimeSqlServer.g.cs",
            "SqlServer パッケージのチェックイン済みソースが現在のテンプレート出力と乖離しています。"
                + "テンプレート（QuickER.CodeGen.CSharp/Templates/CSharpRuntime.scriban 等）を変更した場合は再生成が必要です。"
        );
    }

    /// <summary>QuickER の SQLite エンジン（<c>QuickER.Runtime.Sqlite</c>）のチェックイン済みソースが一致する</summary>
    [Fact(
        DisplayName = "Sqlite パッケージのチェックイン済みソースが現在のレンダラー出力と完全一致する（ドリフト検知）"
    )]
    public void SqliteSource_MatchesRenderedOutput()
    {
        FixtureDriftHarness.VerifyOrRegeneratePackageSource(
            Renderer.RenderSqlite(),
            "src/QuickER.Runtime.Sqlite/QuickERRuntimeSqlite.g.cs",
            "Sqlite パッケージのチェックイン済みソースが現在のテンプレート出力と乖離しています。"
                + "テンプレート（QuickER.CodeGen.CSharp/Templates/CSharpRuntime.scriban 等）を変更した場合は再生成が必要です。"
        );
    }

    /// <summary>EF Core 共通部品（<c>QuickER.Runtime.EntityFrameworkCore</c>）のチェックイン済みソースが一致する</summary>
    [Fact(
        DisplayName = "EntityFrameworkCore パッケージのチェックイン済みソースが現在のレンダラー出力と完全一致する（ドリフト検知）"
    )]
    public void EntityFrameworkCoreSource_MatchesRenderedOutput()
    {
        FixtureDriftHarness.VerifyOrRegeneratePackageSource(
            Renderer.RenderEfCore(),
            "src/QuickER.Runtime.EntityFrameworkCore/QuickERRuntimeEntityFrameworkCore.g.cs",
            "EntityFrameworkCore パッケージのチェックイン済みソースが現在のテンプレート出力と乖離しています。"
                + "テンプレート（QuickER.CodeGen.CSharp/Templates/CSharpRuntime.scriban 等）を変更した場合は再生成が必要です。"
        );
    }
}
