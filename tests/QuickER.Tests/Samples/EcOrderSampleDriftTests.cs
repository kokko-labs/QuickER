using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QuickER.CodeGen.CSharp;
using QuickER.Documents;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedFixture;
using Xunit;

namespace QuickER.Tests.Samples;

/// <summary>
/// samples/ec-order のチェックイン済み生成物（<c>EcOrder.g.cs</c> / <c>EcOrder.g.md</c> / <c>EcOrder.sql</c>）が、
/// 現在のテンプレート・DDL 生成器から再生成した内容と一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// <para>
/// テスト1（生成コード）は、<c>quicker generate --provider sqlite --config quicker.json --api-docs</c> と<b>同一の生成経路</b>を
/// 厳密に模倣する（CLI の <c>LoadOptions</c> / <c>RunGenerate</c> と等価）。図 JSON を
/// <see cref="JsonStorageService.Load"/> で読み、<c>quicker.json</c> を CLI と同じ流儀で読み（<see cref="JsonNode"/> →
/// <c>RepositoryDialect="sqlite"</c> ＋ <c>GenerateApiDocs=true</c> を上書き → <see cref="CodeGenerationOptions"/> へデシリアライズ）、
/// <see cref="DiagramCodeGenerator.Generate"/>（SQLite プロバイダ）で生成した結果を照合する。これにより
/// 「チェックイン済み <c>EcOrder.g.cs</c> は実 CLI が生成したものと同一」がテンプレート変更後も守られる。
/// </para>
/// <para>
/// テスト1b（API リファレンス Markdown）は、同じ生成経路が <c>--api-docs</c> で追加出力する
/// <c>EcOrder.g.md</c>（<c>.g.cs</c> と同じベース名）を照合する。出力は決定的（生成日時を含まない）のため
/// <c>.g.cs</c> と同じくバイト一致で検証する（<see cref="FixtureDriftHarness.VerifyOrRegeneratePackageSource"/>）。
/// </para>
/// <para>
/// テスト2（DDL）は同じ図から <see cref="SqliteDdlGenerator"/> の出力を照合する。DDL 先頭の
/// <c>-- 生成日時:</c> 行は <see cref="DateTime.Now"/> 由来で非決定的なため、両者を正規化してから比較する
/// （この 1 行のみ除外。それ以外は完全一致）。
/// </para>
/// <para>
/// 検証・再生成の実処理は既存フィクスチャと同じ <see cref="FixtureDriftHarness"/> の
/// <c>VerifyOrRegeneratePackageSource</c>（リポジトリ相対パスで任意ファイルを照合/再生成）に集約する。
/// テンプレート変更後の再生成手順は既存フィクスチャと同一の 1 コマンド
/// （<c>QUICKER_REGEN_FIXTURES=1</c> ＋ <c>--filter FullyQualifiedName~Drift</c>）に自然に乗る。
/// </para>
/// </remarks>
public sealed class EcOrderSampleDriftTests
{
    /// <summary>サンプルディレクトリ（リポジトリ相対）</summary>
    private const string SampleDir = "samples/ec-order";

    /// <summary>チェックイン済み生成コードのリポジトリ相対パス</summary>
    private const string GeneratedCodeRepoPath =
        SampleDir + "/EcOrderSample/Generated/EcOrder.g.cs";

    /// <summary>チェックイン済み API リファレンス Markdown のリポジトリ相対パス（<c>--api-docs</c> 相当の同梱出力）</summary>
    private const string ApiDocsRepoPath = SampleDir + "/EcOrderSample/Generated/EcOrder.g.md";

    /// <summary>チェックイン済み DDL のリポジトリ相対パス</summary>
    private const string DdlRepoPath = SampleDir + "/EcOrder.sql";

    /// <summary>DDL 先頭の非決定的な生成日時コメント行（正規化して比較から除外する）</summary>
    private static readonly Regex GeneratedAtCommentLine = new(
        @"^-- 生成日時: .*$",
        RegexOptions.Multiline
    );

    /// <summary>
    /// テスト1: チェックイン済み <c>EcOrder.g.cs</c> が、CLI と同一経路で再生成したコードと完全一致する。
    /// </summary>
    [Fact(
        DisplayName = "サンプル生成コード EcOrder.g.cs が CLI と同一経路の再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedSampleCode_MatchesRegeneratedOutput()
    {
        var rendered = GenerateSampleFileContent(".g.cs");

        FixtureDriftHarness.VerifyOrRegeneratePackageSource(
            rendered,
            GeneratedCodeRepoPath,
            "サンプル生成コード EcOrder.g.cs が現在のテンプレート出力（CLI と同一経路）と乖離しています。"
                + "samples/ec-order の図・quicker.json から再生成が必要です。"
        );
    }

    /// <summary>
    /// テスト1b: チェックイン済み <c>EcOrder.g.md</c>（<c>--api-docs</c> 同梱出力）が、CLI と同一経路で
    /// 再生成した API リファレンス Markdown と完全一致する。
    /// </summary>
    [Fact(
        DisplayName = "サンプル API ドキュメント EcOrder.g.md が CLI と同一経路の再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedSampleApiDocs_MatchesRegeneratedOutput()
    {
        var rendered = GenerateSampleFileContent(".g.md");

        FixtureDriftHarness.VerifyOrRegeneratePackageSource(
            rendered,
            ApiDocsRepoPath,
            "サンプル API ドキュメント EcOrder.g.md が現在のテンプレート出力（CLI と同一経路・--api-docs）と乖離しています。"
                + "samples/ec-order の図・quicker.json から再生成が必要です。"
        );
    }

    /// <summary>
    /// テスト2: チェックイン済み <c>EcOrder.sql</c> が、<see cref="SqliteDdlGenerator"/> の再生成出力と一致する
    /// （非決定的な生成日時コメント行のみ正規化して除外）。
    /// </summary>
    /// <remarks>
    /// DDL 先頭の <c>-- 生成日時:</c> 行は <see cref="DateTime.Now"/> 由来で毎回変わるため、
    /// <see cref="FixtureDriftHarness.VerifyOrRegeneratePackageSource"/>（厳密文字列一致）はそのまま使えない。
    /// 再生成モードでは実際の DDL（生成日時入り）をそのまま書き込み、検証モードでは生成日時行だけを固定文言へ
    /// 正規化して比較する（既存フィクスチャと同じ環境変数 <see cref="FixtureDriftHarness.RegenEnvVar"/> に従う）。
    /// </remarks>
    [Fact(
        DisplayName = "サンプル DDL EcOrder.sql が SqliteDdlGenerator の再生成と一致する（ドリフト検知）"
    )]
    public void CommittedSampleDdl_MatchesRegeneratedOutput()
    {
        var document = LoadSampleDocument();
        var regenerated = new SqliteDdlGenerator().Build(document.Schema);
        var ddlPath = ResolveRepoRelativePath(DdlRepoPath);

        var regenRequested =
            Environment.GetEnvironmentVariable(FixtureDriftHarness.RegenEnvVar) is "1" or "true";

        if (regenRequested)
        {
            // 再生成: 実際の生成日時入り DDL をそのまま書き込む（UTF-8 BOM なし・改行は生成器由来を保持）
            File.WriteAllText(ddlPath, regenerated);
            return;
        }

        // 検証: 生成日時行のみ固定文言へ正規化して突き合わせる（それ以外は完全一致を要求）
        var committed = File.ReadAllText(ddlPath);
        Assert.Equal(NormalizeGeneratedAt(regenerated), NormalizeGeneratedAt(committed));
    }

    /// <summary>
    /// CLI（<c>quicker generate --provider sqlite --config quicker.json --api-docs</c>）と同一経路で
    /// サンプルを再生成し、指定拡張子（<c>.g.cs</c> / <c>.g.md</c>）のファイル内容を返す。
    /// </summary>
    /// <remarks>
    /// <c>--api-docs</c> 相当（<c>GenerateApiDocs=true</c>）で生成すると <c>.g.cs</c> と <c>.g.md</c> の
    /// 2 ファイルが返る。呼び出し側が照合したい方の拡張子で 1 ファイルを取り出す。
    /// </remarks>
    private static string GenerateSampleFileContent(string extension)
    {
        var document = LoadSampleDocument();
        var provider = new SqliteProvider();
        var options = LoadSampleOptions(provider);

        // CLI の ResolveDialectTypeMappers 相当（実効方言 → 方言別マッパ。SQLite 単一のため sqlite のみ）
        var dialectMappers = new Dictionary<string, IColumnTypeMapper>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var dialect in options.EffectiveRepositoryDialects)
        {
            if (string.Equals(dialect, provider.Name, StringComparison.OrdinalIgnoreCase))
            {
                dialectMappers[dialect] = provider.TypeMapper;
            }
        }

        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            dialectMappers,
            document.Schema,
            options
        );

        Assert.False(result.HasErrors, "サンプル図の生成でエラーが出てはならない");

        // --api-docs 相当のため .g.cs（コード 1 本）＋ .g.md（API ドキュメント 1 本）の 2 ファイルが返る
        Assert.Equal(2, result.Files.Count);

        var file = result.Files.Single(f =>
            f.FileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
        );
        return file.Content;
    }

    /// <summary>
    /// <c>quicker.json</c> を CLI の <c>LoadOptions</c> と同じ流儀で読み、
    /// <c>RepositoryDialect</c> をプロバイダ名で上書きし、<c>--api-docs</c> 相当の
    /// <c>GenerateApiDocs=true</c> を立ててオプションを構築する。
    /// </summary>
    private static CodeGenerationOptions LoadSampleOptions(SqliteProvider provider)
    {
        var configPath = ResolveRepoRelativePath(SampleDir + "/quicker.json");
        var node = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject() ?? new JsonObject();

        // CLI は --repository-dialects 未指定時に provider.Name を単一 RepositoryDialect として設定する
        node["RepositoryDialect"] = provider.Name;

        // CLI の --api-docs 相当（設定ファイル値を上書きして API リファレンス Markdown も同梱出力する）
        node["GenerateApiDocs"] = true;

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return node.Deserialize<CodeGenerationOptions>(jsonOptions)
            ?? throw new InvalidOperationException("quicker.json のデシリアライズに失敗しました。");
    }

    /// <summary>サンプル図 JSON を読み込む（入力もリポジトリ相対で解決する）</summary>
    private static DiagramDocument LoadSampleDocument()
    {
        var jsonPath = ResolveRepoRelativePath(SampleDir + "/EcOrder.json");
        return JsonStorageService.Load(jsonPath);
    }

    /// <summary>DDL の非決定的な生成日時行を固定文言へ正規化する（比較から実質除外する）</summary>
    private static string NormalizeGeneratedAt(string ddl) =>
        GeneratedAtCommentLine.Replace(ddl, "-- 生成日時: (正規化)");

    /// <summary>
    /// リポジトリ直下（<c>QuickER.slnx</c> を目印）からの相対パスを絶対パスへ解決する。
    /// </summary>
    private static string ResolveRepoRelativePath(string repoRelativePath)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!
        );

        while (dir is not null)
        {
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
