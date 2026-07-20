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
/// テスト1（生成コード）は、<c>quicker generate --provider sqlite --config quicker.json --generate-api-docs</c> と<b>同一の生成経路</b>を
/// 厳密に模倣する（CLI の <c>LoadOptions</c> / <c>RunGenerate</c> と等価）。図 JSON を
/// <see cref="JsonStorageService.Load"/> で読み、<c>quicker.json</c> を CLI と同じ流儀で読み（<see cref="JsonNode"/> →
/// <c>RepositoryDialects=["sqlite"]</c> を補完 ＋ <c>GenerateApiDocs=true</c> を上書き → <see cref="CodeGenerationOptions"/> へデシリアライズ）、
/// <see cref="DiagramCodeGenerator.Generate"/>（SQLite プロバイダ）で生成した結果を照合する。これにより
/// 「チェックイン済み <c>EcOrder.g.cs</c> は実 CLI が生成したものと同一」がテンプレート変更後も守られる。
/// </para>
/// <para>
/// テスト1b（API リファレンス Markdown・英語正本）は、同じ生成経路が <c>--generate-api-docs</c> で追加出力する
/// <c>EcOrder.g.md</c>（<c>.g.cs</c> と同じベース名）を照合する。出力は決定的（生成日時を含まない）のため
/// <c>.g.cs</c> と同じくバイト一致で検証する（<see cref="FixtureDriftHarness.VerifyOrRegeneratePackageSource"/>）。
/// </para>
/// <para>
/// テスト2（DDL）は同じ図から <see cref="SqliteDdlGenerator"/> の出力を照合する。DDL 先頭の
/// <c>-- Generated at:</c> 行は <see cref="DateTime.Now"/> 由来で非決定的なため、両者を正規化してから比較する
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

    /// <summary>チェックイン済み API リファレンス Markdown（英語正本）のリポジトリ相対パス（<c>--generate-api-docs</c> 相当の同梱出力）</summary>
    private const string ApiDocsRepoPath = SampleDir + "/EcOrderSample/Generated/EcOrder.g.md";

    /// <summary>チェックイン済み DDL のリポジトリ相対パス</summary>
    private const string DdlRepoPath = SampleDir + "/EcOrder.sql";

    /// <summary>DDL 先頭の非決定的な生成日時コメント行（正規化して比較から除外する）</summary>
    private static readonly Regex GeneratedAtCommentLine = new(
        @"^-- Generated at: .*$",
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
        var rendered = GenerateSampleFileContent("EcOrder.g.cs");

        FixtureDriftHarness.VerifyOrRegeneratePackageSource(
            rendered,
            GeneratedCodeRepoPath,
            "サンプル生成コード EcOrder.g.cs が現在のテンプレート出力（CLI と同一経路）と乖離しています。"
                + "samples/ec-order の図・quicker.json から再生成が必要です。"
        );
    }

    /// <summary>
    /// テスト1b: チェックイン済み <c>EcOrder.g.md</c>（<c>--generate-api-docs</c> 同梱出力）が、CLI と同一経路で
    /// 再生成した API リファレンス Markdown と完全一致する。
    /// </summary>
    [Fact(
        DisplayName = "サンプル API ドキュメント EcOrder.g.md が CLI と同一経路の再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedSampleApiDocs_MatchesRegeneratedOutput()
    {
        var rendered = GenerateSampleFileContent("EcOrder.g.md");

        FixtureDriftHarness.VerifyOrRegeneratePackageSource(
            rendered,
            ApiDocsRepoPath,
            "サンプル API ドキュメント EcOrder.g.md が現在のテンプレート出力（CLI と同一経路・--generate-api-docs）と乖離しています。"
                + "samples/ec-order の図・quicker.json から再生成が必要です。"
        );
    }

    /// <summary>
    /// テスト2: チェックイン済み <c>EcOrder.sql</c> が、<see cref="SqliteDdlGenerator"/> の再生成出力と一致する
    /// （非決定的な生成日時コメント行のみ正規化して除外）。
    /// </summary>
    /// <remarks>
    /// DDL 先頭の <c>-- Generated at:</c> 行は <see cref="DateTime.Now"/> 由来で毎回変わるため、
    /// <see cref="FixtureDriftHarness.VerifyOrRegeneratePackageSource"/>（厳密文字列一致）はそのまま使えない。
    /// 検証モードでは生成日時行だけを固定文言へ正規化して比較する
    /// （既存フィクスチャと同じ環境変数 <see cref="FixtureDriftHarness.RegenEnvVar"/> に従う）。
    /// 再生成モードでは<b>実質差分（生成日時行以外）があるときだけ</b>書き込む。無条件に書き込むと、
    /// テンプレート・スキーマを何も変えていなくても再生成のたびに生成日時行だけが変わり、
    /// 無意味な 1 行差分がコミットへ紛れ込むため（検証側が無視する行で作業ツリーを汚さない）。
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
            RegenerateSampleDdl(ddlPath, regenerated);
            return;
        }

        // 検証: 生成日時行のみ固定文言へ正規化して突き合わせる（それ以外は完全一致を要求）
        var committed = File.ReadAllText(ddlPath);
        Assert.Equal(NormalizeGeneratedAt(regenerated), NormalizeGeneratedAt(committed));
    }

    /// <summary>
    /// 再生成モードで DDL を書き出す（実質差分がなければ書き込まず、既存の生成日時行を保持する）。
    /// </summary>
    /// <remarks>
    /// 実質差分の有無は、検証側と同じ <see cref="NormalizeGeneratedAt"/> による正規化で判定する。
    /// これにより「検証が無視する差分は再生成でも作らない」という対称性が保たれる
    /// </remarks>
    private static void RegenerateSampleDdl(string ddlPath, string regenerated)
    {
        if (
            File.Exists(ddlPath)
            && NormalizeGeneratedAt(File.ReadAllText(ddlPath)) == NormalizeGeneratedAt(regenerated)
        )
        {
            return;
        }

        // 実質差分あり: 実際の生成日時入り DDL をそのまま書き込む（UTF-8 BOM なし・改行は生成器由来を保持）
        File.WriteAllText(ddlPath, regenerated);
    }

    /// <summary>
    /// CLI（<c>quicker generate --provider sqlite --config quicker.json --generate-api-docs</c>）と同一経路で
    /// サンプルを再生成し、指定ファイル名（<c>EcOrder.g.cs</c> / <c>EcOrder.g.md</c>）の内容を返す。
    /// </summary>
    /// <remarks>
    /// <c>--generate-api-docs</c> 相当（<c>GenerateApiDocs=true</c>）で生成すると
    /// <c>.g.cs</c>・<c>.g.md</c>（英語正本）の 2 ファイルが返る。
    /// 呼び出し側が照合したいファイルを<b>ファイル名の完全一致</b>で取り出す。
    /// </remarks>
    private static string GenerateSampleFileContent(string fileName)
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

        // --generate-api-docs 相当のため .g.cs（コード）＋ .g.md（英語正本）の 2 ファイルが返る
        Assert.Equal(2, result.Files.Count);

        var file = result.Files.Single(f =>
            string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase)
        );
        return file.Content;
    }

    /// <summary>
    /// <c>quicker.json</c> を CLI の <c>LoadOptions</c> と同じ流儀で読み、
    /// <c>RepositoryDialects</c> をプロバイダ名で補い、<c>--generate-api-docs</c> 相当の
    /// <c>GenerateApiDocs=true</c> を立ててオプションを構築する。
    /// </summary>
    private static CodeGenerationOptions LoadSampleOptions(SqliteProvider provider)
    {
        var configPath = ResolveRepoRelativePath(SampleDir + "/quicker.json");
        var node = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject() ?? new JsonObject();

        // CLI は --repository-dialects 未指定時、設定ファイルに RepositoryDialects（非空）が無ければ
        // provider.Name を単一要素の RepositoryDialects として設定する
        if (node["RepositoryDialects"] is not JsonArray { Count: > 0 })
        {
            node["RepositoryDialects"] = new JsonArray(JsonValue.Create(provider.Name));
        }

        // CLI の --generate-api-docs 相当（設定ファイル値を上書きして API リファレンス Markdown も同梱出力する）
        node["GenerateApiDocs"] = true;

        // CLI の LoadOptions と同じく OutputPath（ファイル名）→ OutputFileName を導出する
        // （quicker.json は OutputPath="EcOrder.g.cs" のみを持ち、OutputFileName は持たない）
        if (
            node["OutputFileName"] is null
            && node["OutputPath"] is JsonValue outputPathValue
            && outputPathValue.TryGetValue(out string? outputPath)
            && !string.IsNullOrWhiteSpace(Path.GetFileName(outputPath))
        )
        {
            node["OutputFileName"] = Path.GetFileName(outputPath);
        }

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
        GeneratedAtCommentLine.Replace(ddl, "-- Generated at: (normalized)");

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
