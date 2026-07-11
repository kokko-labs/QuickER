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
/// samples/ec-order-remote のチェックイン済み生成物（<c>EcOrderRemote.g.cs</c> / <c>EcOrderRemote.RemoteServer.g.cs</c> /
/// <c>EcOrderRemote.sql</c>）が、現在のテンプレート・DDL 生成器から再生成した内容と一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// <para>
/// テスト1（生成コード）は、<c>quicker generate --provider sqlite --config quicker.json</c> と<b>同一の生成経路</b>を
/// 厳密に模倣する（CLI の <c>LoadOptions</c> / <c>RunGenerate</c> と等価）。図 JSON を
/// <see cref="JsonStorageService.Load"/> で読み、<c>quicker.json</c> を CLI と同じ流儀で読み（<see cref="JsonNode"/> →
/// <c>RepositoryDialect="sqlite"</c> を上書き → <see cref="CodeGenerationOptions"/> へデシリアライズ。
/// <c>GenerateRemoteServices=true</c> は quicker.json 由来）、<see cref="DiagramCodeGenerator.Generate"/>
/// （SQLite プロバイダ）で生成した結果を照合する。リモートサービス生成は本体＋サーバーの 2 ファイル出力のため、
/// <b>出力ファイル構成（ファイル名一覧・順序込み）そのものもドリフト対象</b>とする
/// （RemoteServiceFixtureDriftTests と同じ思想。サーバーファイルの出力が意図せず消える回帰を検知する）。
/// </para>
/// <para>
/// テスト2（DDL）は同じ図から <see cref="SqliteDdlGenerator"/> の出力を照合する。DDL 先頭の
/// <c>-- 生成日時:</c> 行は <see cref="DateTime.Now"/> 由来で非決定的なため、両者を正規化してから比較する
/// （この 1 行のみ除外。それ以外は完全一致）。名前付きクエリ定義は DDL に影響しない。
/// </para>
/// <para>
/// 検証・再生成の実処理は既存フィクスチャと同じ <see cref="FixtureDriftHarness"/> の
/// <c>VerifyOrRegeneratePackageSource</c>（リポジトリ相対パスで任意ファイルを照合/再生成）に集約する。
/// テンプレート変更後の再生成手順は既存フィクスチャと同一の 1 コマンド
/// （<c>QUICKER_REGEN_FIXTURES=1</c> ＋ <c>--filter FullyQualifiedName~Drift</c>）に自然に乗る。
/// </para>
/// </remarks>
public sealed class EcOrderRemoteSampleDriftTests
{
    /// <summary>サンプルディレクトリ（リポジトリ相対）</summary>
    private const string SampleDir = "samples/ec-order-remote";

    /// <summary>CLI と同一経路の再生成が出力すべきファイル名一覧（順序込み。構成の乖離もドリフトとして検知する）</summary>
    private static readonly string[] ExpectedFileNames =
    [
        "EcOrderRemote.g.cs",
        "EcOrderRemote.RemoteServer.g.cs",
    ];

    /// <summary>チェックイン済み DDL のリポジトリ相対パス</summary>
    private const string DdlRepoPath = SampleDir + "/EcOrderRemote.sql";

    /// <summary>DDL 先頭の非決定的な生成日時コメント行（正規化して比較から除外する）</summary>
    private static readonly Regex GeneratedAtCommentLine = new(
        @"^-- 生成日時: .*$",
        RegexOptions.Multiline
    );

    /// <summary>
    /// テスト1: チェックイン済み生成コード 2 ファイル（本体＋リモートサーバー）が、CLI と同一経路で
    /// 再生成した内容とファイル構成・内容ともに完全一致する。
    /// </summary>
    [Fact(
        DisplayName = "サンプル生成コード（本体＋RemoteServer）が CLI と同一経路の再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedSampleCode_MatchesRegeneratedOutput()
    {
        var result = GenerateSample();

        // 出力ファイル構成そのものもドリフト対象（サーバーファイルの出力が意図せず消える回帰を検知する）
        Assert.Equal(ExpectedFileNames, result.Files.Select(f => f.FileName).ToArray());

        foreach (var file in result.Files)
        {
            FixtureDriftHarness.VerifyOrRegeneratePackageSource(
                file.Content,
                SampleDir + "/Generated/" + file.FileName,
                $"サンプル生成コード {file.FileName} が現在のテンプレート出力（CLI と同一経路）と乖離しています。"
                    + "samples/ec-order-remote の図・quicker.json から再生成が必要です。"
            );
        }
    }

    /// <summary>
    /// テスト2: チェックイン済み <c>EcOrderRemote.sql</c> が、<see cref="SqliteDdlGenerator"/> の再生成出力と一致する
    /// （非決定的な生成日時コメント行のみ正規化して除外）。
    /// </summary>
    /// <remarks>
    /// DDL 先頭の <c>-- 生成日時:</c> 行は <see cref="DateTime.Now"/> 由来で毎回変わるため、
    /// <see cref="FixtureDriftHarness.VerifyOrRegeneratePackageSource"/>（厳密文字列一致）はそのまま使えない。
    /// 検証モードでは生成日時行だけを固定文言へ正規化して比較し、再生成モードでは<b>実質差分
    /// （生成日時行以外）があるときだけ</b>書き込む（EcOrderSampleDriftTests と同じ対称設計。
    /// 検証が無視する差分は再生成でも作らない）。
    /// </remarks>
    [Fact(
        DisplayName = "サンプル DDL EcOrderRemote.sql が SqliteDdlGenerator の再生成と一致する（ドリフト検知）"
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
    /// CLI（<c>quicker generate --provider sqlite --config quicker.json</c>）と同一経路でサンプルを再生成する。
    /// </summary>
    private static CodeGenerationResult GenerateSample()
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
        return result;
    }

    /// <summary>
    /// <c>quicker.json</c> を CLI の <c>LoadOptions</c> と同じ流儀で読み、
    /// <c>RepositoryDialect</c> をプロバイダ名で上書きしてオプションを構築する
    /// （<c>GenerateRemoteServices=true</c> は quicker.json 側の値をそのまま使う）。
    /// </summary>
    private static CodeGenerationOptions LoadSampleOptions(SqliteProvider provider)
    {
        var configPath = ResolveRepoRelativePath(SampleDir + "/quicker.json");
        var node = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject() ?? new JsonObject();

        // CLI は --repository-dialects 未指定時に provider.Name を単一 RepositoryDialect として設定する
        node["RepositoryDialect"] = provider.Name;

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return node.Deserialize<CodeGenerationOptions>(jsonOptions)
            ?? throw new InvalidOperationException("quicker.json のデシリアライズに失敗しました。");
    }

    /// <summary>サンプル図 JSON を読み込む（入力もリポジトリ相対で解決する）</summary>
    private static DiagramDocument LoadSampleDocument()
    {
        var jsonPath = ResolveRepoRelativePath(SampleDir + "/EcOrderRemote.json");
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
