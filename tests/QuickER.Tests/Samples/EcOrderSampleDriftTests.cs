using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using QuickER.Cli;
using QuickER.Documents;
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
/// テスト1・1b（生成コード・API ドキュメント）は <b>実 CLI</b> を呼ぶ
/// （<see cref="CliApp.InvokeAsync(string[], TextWriter, TextWriter)"/> で
/// <c>quicker generate --provider sqlite --config quicker.json --generate-api-docs</c> 相当を実行し、一時フォルダへ
/// 書き出された生成物とチェックイン済みファイルをバイト比較する）。設定ローダーの挙動（RepositoryDialects の補完・
/// <c>OutputPath</c> → <c>OutputFileName</c> の導出）を鏡実装で写すと、ローダーを変えても本テストは緑のままになり
/// 「チェックイン済み生成物は実 CLI が生成したものと同一」という主張が空文になるため、経路そのものを共有する。
/// </para>
/// <para>
/// テスト2（DDL）は同じ図から <see cref="SqliteDdlGenerator"/> の出力を照合する（DDL は CLI の generate では
/// 出力されない別経路のため）。DDL 先頭の <c>-- Generated at:</c> 行は <see cref="DateTime.Now"/> 由来で
/// 非決定的なため、両者を正規化してから比較する（この 1 行のみ除外。それ以外は完全一致）。
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

    /// <summary>チェックイン済み生成物の置き場（リポジトリ相対）</summary>
    private const string GeneratedDir = SampleDir + "/EcOrderSample/Generated";

    /// <summary>CLI が出力すべきファイル名一覧（<c>--generate-api-docs</c> 込み＝コード＋英語正本ドキュメント）</summary>
    private static readonly string[] ExpectedFileNames = ["EcOrder.g.cs", "EcOrder.g.md"];

    /// <summary>チェックイン済み DDL のリポジトリ相対パス</summary>
    private const string DdlRepoPath = SampleDir + "/EcOrder.sql";

    /// <summary>DDL 先頭の非決定的な生成日時コメント行（正規化して比較から除外する）</summary>
    private static readonly Regex GeneratedAtCommentLine = new(
        @"^-- Generated at: .*$",
        RegexOptions.Multiline
    );

    /// <summary>
    /// テスト1: チェックイン済み <c>EcOrder.g.cs</c> が、実 CLI で再生成したコードと完全一致する。
    /// </summary>
    [Fact(
        DisplayName = "サンプル生成コード EcOrder.g.cs が実 CLI の再生成と完全一致する（ドリフト検知）"
    )]
    public async Task CommittedSampleCode_MatchesRegeneratedOutput()
    {
        var generated = await GenerateSampleViaCliAsync();

        VerifyOrRegenerate(generated, "EcOrder.g.cs");
    }

    /// <summary>
    /// テスト1b: チェックイン済み <c>EcOrder.g.md</c>（<c>--generate-api-docs</c> 同梱出力）が、実 CLI で
    /// 再生成した API リファレンス Markdown と完全一致する。
    /// </summary>
    [Fact(
        DisplayName = "サンプル API ドキュメント EcOrder.g.md が実 CLI の再生成と完全一致する（ドリフト検知）"
    )]
    public async Task CommittedSampleApiDocs_MatchesRegeneratedOutput()
    {
        var generated = await GenerateSampleViaCliAsync();

        VerifyOrRegenerate(generated, "EcOrder.g.md");
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
        var document = JsonStorageService.Load(
            SampleCliRunner.ResolveRepoRelativePath(SampleDir + "/EcOrder.json")
        );
        var regenerated = new SqliteDdlGenerator().Build(document.Schema);
        var ddlPath = SampleCliRunner.ResolveRepoRelativePath(DdlRepoPath);

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
    /// 実 CLI（<c>quicker generate --provider sqlite --config quicker.json --generate-api-docs</c>）で
    /// サンプルを再生成し、書き出された全ファイル（書き出し順）を返す。
    /// </summary>
    private static Task<IReadOnlyList<GeneratedSampleFile>> GenerateSampleViaCliAsync() =>
        SampleCliRunner.GenerateAsync(
            SampleDir,
            "EcOrder.json",
            ExpectedFileNames,
            orderSensitive: false,
            // API リファレンス Markdown の同梱出力は quicker.json ではなく CLI フラグで指定する（サンプルの生成手順どおり）
            "--generate-api-docs"
        );

    /// <summary>指定ファイルをチェックイン済み生成物と照合（再生成モードでは上書き）する</summary>
    private static void VerifyOrRegenerate(
        IReadOnlyList<GeneratedSampleFile> generated,
        string fileName
    ) =>
        FixtureDriftHarness.VerifyOrRegeneratePackageSource(
            generated.Single(file => file.FileName == fileName).Content,
            GeneratedDir + "/" + fileName,
            $"サンプル生成物 {fileName} が現在の実 CLI 出力と乖離しています。"
                + "samples/ec-order の図・quicker.json から再生成が必要です。"
        );

    /// <summary>DDL の非決定的な生成日時行を固定文言へ正規化する（比較から実質除外する）</summary>
    private static string NormalizeGeneratedAt(string ddl) =>
        GeneratedAtCommentLine.Replace(ddl, "-- Generated at: (normalized)");
}
