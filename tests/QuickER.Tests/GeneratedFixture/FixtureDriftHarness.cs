using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using QuickER.Generator;
using QuickER.Model;
using QuickER.SqlServer;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// コミット済み固定フィクスチャの「ドリフト検知」と「再生成」を 1 経路に集約する共通ハーネス。
/// </summary>
/// <remarks>
/// <para>
/// 通常はコミット済みファイルと、単一ソース定義から現在のテンプレートで再生成した内容の
/// 完全一致を検証する。環境変数 <see cref="RegenEnvVar"/> が真のときは検証の代わりに
/// コミット済みファイルを上書き（再生成）する。これにより「ドリフト検知の期待値を作る経路」と
/// 「再生成の経路」が同一になり、両者がずれないことが構造上保証される。
/// </para>
/// <para>
/// テンプレート（<c>CSharpRuntime.scriban</c> 等）を変更したら、次の 1 コマンドで
/// 両フィクスチャ（<c>GeneratedFixture.g.cs</c> / <c>PortableFixture.g.cs</c>）をまとめて再生成する
/// （リポジトリ直下・PowerShell）:
/// <code>
/// $env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~Drift"; $env:QUICKER_REGEN_FIXTURES=$null
/// </code>
/// 再生成後は環境変数なしで同じテストを流し、緑（ドリフトなし）になることを確認する。
/// </para>
/// </remarks>
internal static class FixtureDriftHarness
{
    /// <summary>真のとき、ドリフト検知の代わりにコミット済みフィクスチャを上書き再生成する環境変数名</summary>
    public const string RegenEnvVar = "QUICKER_REGEN_FIXTURES";

    /// <summary>ドリフト失敗メッセージに埋め込む再生成コマンドの案内</summary>
    private const string RegenCommandHint =
        "再生成（リポジトリ直下・PowerShell）: "
        + "$env:QUICKER_REGEN_FIXTURES=1; "
        + "dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter \"FullyQualifiedName~Drift\"; "
        + "$env:QUICKER_REGEN_FIXTURES=$null";

    /// <summary>
    /// 指定図・オプションから生成したコードを、コミット済みフィクスチャと照合する。
    /// 再生成モード（<see cref="RegenEnvVar"/> 指定時）では検証せずファイルを上書きする。
    /// </summary>
    /// <param name="diagram">単一ソース定義が返す決定的な ER 図</param>
    /// <param name="options">フィクスチャ生成に用いる決定的なオプション</param>
    /// <param name="outputFileName">コミット済みフィクスチャのファイル名（GeneratedFixture フォルダ内）</param>
    /// <param name="driftReason">ドリフト時に表示する理由（末尾に再生成コマンドが自動付与される）</param>
    public static void VerifyOrRegenerate(
        ErDiagram diagram,
        CodeGenerationOptions options,
        string outputFileName,
        string driftReason
    )
    {
        var columnTypes = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var result = new CSharpCodeGenerationService().Generate(diagram, columnTypes, options);

        result.HasErrors.Should().BeFalse("フィクスチャ図の生成でエラーが出てはならない");
        result.Files.Should().ContainSingle("Split 無効のため 1 ファイルで生成される");

        var regenerated = result.Files[0].Content;
        var fixturePath = ResolveFixturePath(outputFileName);

        if (IsRegenerationRequested())
        {
            // File.WriteAllText は UTF-8（BOM なし）で書き出し、改行はそのまま（テンプレート由来の CRLF を保持）
            File.WriteAllText(fixturePath, regenerated);

            return;
        }

        var committed = File.ReadAllText(fixturePath);

        committed
            .Should()
            .Be(regenerated, $"{driftReason} 再生成先: {fixturePath}。{RegenCommandHint}");
    }

    /// <summary>再生成モードが要求されているか（<see cref="RegenEnvVar"/> が 1 / true）</summary>
    private static bool IsRegenerationRequested()
    {
        var value = Environment.GetEnvironmentVariable(RegenEnvVar);

        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>コミット済みフィクスチャの絶対パスを、テストアセンブリの位置から遡って解決する</summary>
    private static string ResolveFixturePath(string outputFileName)
    {
        // テスト実行ディレクトリ（bin/Debug/netX.Y-windows）から、ソースの GeneratedFixture フォルダを探す。
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!
        );

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "GeneratedFixture", outputFileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"コミット済みフィクスチャ {outputFileName} が見つかりませんでした。"
        );
    }
}
