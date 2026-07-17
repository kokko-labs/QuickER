using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FluentAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
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
/// テンプレート（<c>CSharpRuntime/*.scriban</c> 等）を変更したら、次の 1 コマンドで
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
        // 実生成経路（DiagramCodeGenerator）と同じく、図の方言の型カタログ由来の DB 定義メタトークンを付加する
        columnTypes = CanonicalTypeTokenAttacher.Attach(
            columnTypes,
            diagram,
            new SqlServerTypeCatalog()
        );
        // 名前付きクエリの型トークンも実生成経路と同じく解決する（クエリなしの図では空辞書＝出力不変）
        var provider = new SqlServerProvider();
        var queryParameterTypes = QueryParameterTypeResolver.Resolve(
            diagram,
            provider.TypeMapper,
            provider.TypeCatalog
        );
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            columnTypes,
            options,
            queryParameterTypes
        );

        VerifyOrRegenerate(result, outputFileName, driftReason);
    }

    /// <summary>
    /// マルチ辞書（方言ごとに解決した列型辞書）を使うフィクスチャ向けのオーバーロード。
    /// 主辞書（図の方言）と方言辞書を渡し、マルチターゲット構成のコードを生成して照合する。
    /// </summary>
    /// <param name="diagram">単一ソース定義が返す決定的な ER 図</param>
    /// <param name="primaryColumnTypes">共有バケット（Entity/EditModel/Mapper/VO）に使う主辞書</param>
    /// <param name="columnTypesByDialect">方言名 → その方言で解決した列型辞書（各方言実装バケット用）</param>
    /// <param name="options">フィクスチャ生成に用いる決定的なオプション</param>
    /// <param name="outputFileName">コミット済みフィクスチャのファイル名（GeneratedFixture フォルダ内）</param>
    /// <param name="driftReason">ドリフト時に表示する理由（末尾に再生成コマンドが自動付与される）</param>
    public static void VerifyOrRegenerate(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> primaryColumnTypes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> columnTypesByDialect,
        CodeGenerationOptions options,
        string outputFileName,
        string driftReason
    )
    {
        // 実生成経路（DiagramCodeGenerator）と同じく、図の方言（本フィクスチャは SQL Server 型表記）の
        // 型カタログ由来の DB 定義メタトークンを主辞書へ付加する。共有 Entity にのみ影響し、canonical 由来で方言に依らない
        var primaryWithToken = CanonicalTypeTokenAttacher.Attach(
            primaryColumnTypes,
            diagram,
            new SqlServerTypeCatalog()
        );
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primaryWithToken,
            columnTypesByDialect,
            options
        );

        VerifyOrRegenerate(result, outputFileName, driftReason);
    }

    /// <summary>
    /// ランタイムパッケージ用のチェックイン済みソース（<see cref="RuntimePackageSourceRenderer"/> 出力）を照合、
    /// または再生成する。既存フィクスチャと同一経路（<see cref="RegenEnvVar"/>）で上書き再生成される。
    /// </summary>
    /// <param name="rendered">レンダラーが生成した現在のソース文字列（CRLF・バイト一致の基準）</param>
    /// <param name="repoRelativePath">リポジトリ直下からの相対パス（例 <c>src/QuickER.Runtime/QuickERRuntime.g.cs</c>）</param>
    /// <param name="driftReason">ドリフト時に表示する理由（末尾に再生成コマンドが自動付与される）</param>
    public static void VerifyOrRegeneratePackageSource(
        string rendered,
        string repoRelativePath,
        string driftReason
    )
    {
        var checkedInPath = ResolveRepoRelativePath(repoRelativePath);

        if (IsRegenerationRequested())
        {
            // File.WriteAllText は UTF-8（BOM なし）で書き出し、改行はそのまま（レンダラー由来の CRLF を保持）
            File.WriteAllText(checkedInPath, rendered);

            return;
        }

        var committed = File.ReadAllText(checkedInPath);

        committed
            .Should()
            .Be(rendered, $"{driftReason} 再生成先: {checkedInPath}。{RegenCommandHint}");
    }

    /// <summary>生成結果をコミット済みフィクスチャと照合、または再生成する共通処理。</summary>
    private static void VerifyOrRegenerate(
        CodeGenerationResult result,
        string outputFileName,
        string driftReason
    )
    {
        result.HasErrors.Should().BeFalse("フィクスチャ図の生成でエラーが出てはならない");
        result.Files.Should().ContainSingle("Split 無効のため 1 ファイルで生成される");

        VerifyOrRegenerateFile(result.Files[0].Content, outputFileName, driftReason);
    }

    /// <summary>
    /// 複数ファイルを出力するフィクスチャ（例: リモートサービス＝本体＋RemoteServer）向けのオーバーロード。
    /// 生成結果の各ファイルを、同名のコミット済みフィクスチャと照合（または再生成）する。
    /// </summary>
    /// <param name="diagram">単一ソース定義が返す決定的な ER 図</param>
    /// <param name="options">フィクスチャ生成に用いる決定的なオプション</param>
    /// <param name="expectedFileNames">期待する出力ファイル名の一覧（生成結果と完全一致していること）</param>
    /// <param name="driftReason">ドリフト時に表示する理由（末尾に再生成コマンドが自動付与される）</param>
    public static void VerifyOrRegenerate(
        ErDiagram diagram,
        CodeGenerationOptions options,
        IReadOnlyList<string> expectedFileNames,
        string driftReason
    )
    {
        var columnTypes = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        columnTypes = CanonicalTypeTokenAttacher.Attach(
            columnTypes,
            diagram,
            new SqlServerTypeCatalog()
        );
        var provider = new SqlServerProvider();
        var queryParameterTypes = QueryParameterTypeResolver.Resolve(
            diagram,
            provider.TypeMapper,
            provider.TypeCatalog
        );
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            columnTypes,
            options,
            queryParameterTypes
        );

        result.HasErrors.Should().BeFalse("フィクスチャ図の生成でエラーが出てはならない");
        result
            .Files.Select(file => file.FileName)
            .Should()
            .Equal(expectedFileNames, "出力ファイル構成そのものもドリフト検知の対象とする");

        foreach (var file in result.Files)
        {
            VerifyOrRegenerateFile(file.Content, file.FileName, driftReason);
        }
    }

    /// <summary>1 ファイル分の照合（または再生成）を行う。</summary>
    private static void VerifyOrRegenerateFile(
        string regenerated,
        string outputFileName,
        string driftReason
    )
    {
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

    /// <summary>
    /// リポジトリ直下からの相対パス（例 <c>src/QuickER.Runtime/QuickERRuntime.g.cs</c>）を、
    /// テストアセンブリの位置から <c>QuickER.slnx</c> を目印にリポジトリ直下を遡って解決する。
    /// </summary>
    private static string ResolveRepoRelativePath(string repoRelativePath)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!
        );

        while (dir is not null)
        {
            // リポジトリ直下は QuickER.slnx が存在する位置で判定する。
            if (File.Exists(Path.Combine(dir.FullName, "QuickER.slnx")))
            {
                return Path.Combine(
                    dir.FullName,
                    repoRelativePath.Replace('/', Path.DirectorySeparatorChar)
                );
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"リポジトリ直下（QuickER.slnx）が見つからず {repoRelativePath} を解決できませんでした。"
        );
    }
}
