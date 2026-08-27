using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using QuickER.Documents;
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
/// テスト1（生成コード）は <b>実 CLI</b> を呼ぶ（<c>quicker generate --provider sqlite --config quicker.json</c> 相当を
/// 一時フォルダへ実行し、チェックイン済みファイルとバイト比較する。設定ローダーの挙動を鏡実装で写すと、
/// ローダーを変えても本テストは緑のままになるため経路そのものを共有する）。リモートサービス生成は本体＋サーバーの
/// 2 ファイル出力のため、<b>出力ファイル構成（ファイル名一覧・順序込み）そのものもドリフト対象</b>とする
/// （RemoteServiceFixtureDriftTests と同じ思想。サーバーファイルの出力が意図せず消える回帰を検知する）。
/// </para>
/// <para>
/// テスト2（DDL）は同じ図から <see cref="SqliteDdlGenerator"/> の出力を照合する。DDL 先頭の
/// <c>-- Generated at:</c> 行は <see cref="DateTime.Now"/> 由来で非決定的なため、両者を正規化してから比較する
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

    /// <summary>実 CLI が出力すべきファイル名一覧（順序込み。構成の乖離もドリフトとして検知する）</summary>
    private static readonly string[] ExpectedFileNames =
    [
        "EcOrderRemote.g.cs",
        "EcOrderRemote.RemoteServer.g.cs",
    ];

    /// <summary>チェックイン済み DDL のリポジトリ相対パス</summary>
    private const string DdlRepoPath = SampleDir + "/EcOrderRemote.sql";

    /// <summary>DDL 先頭の非決定的な生成日時コメント行（正規化して比較から除外する）</summary>
    private static readonly Regex GeneratedAtCommentLine = new(
        @"^-- Generated at: .*$",
        RegexOptions.Multiline
    );

    /// <summary>
    /// テスト1: チェックイン済み生成コード 2 ファイル（本体＋リモートサーバー）が、実 CLI で
    /// 再生成した内容とファイル構成・内容ともに完全一致する。
    /// </summary>
    [Fact(
        DisplayName = "サンプル生成コード（本体＋RemoteServer）が実 CLI の再生成と完全一致する（ドリフト検知）"
    )]
    public async Task CommittedSampleCode_MatchesRegeneratedOutput()
    {
        var generated = await SampleCliRunner.GenerateAsync(
            SampleDir,
            "EcOrderRemote.json",
            ExpectedFileNames,
            orderSensitive: true
        );

        foreach (var file in generated)
        {
            FixtureDriftHarness.VerifyOrRegeneratePackageSource(
                file.Content,
                SampleDir + "/Generated/" + file.FileName,
                $"サンプル生成コード {file.FileName} が現在の実 CLI 出力と乖離しています。"
                    + "samples/ec-order-remote の図・quicker.json から再生成が必要です。"
            );
        }
    }

    /// <summary>
    /// テスト2: チェックイン済み <c>EcOrderRemote.sql</c> が、<see cref="SqliteDdlGenerator"/> の再生成出力と一致する
    /// （非決定的な生成日時コメント行のみ正規化して除外）。
    /// </summary>
    /// <remarks>
    /// DDL 先頭の <c>-- Generated at:</c> 行は <see cref="DateTime.Now"/> 由来で毎回変わるため、
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
        var document = JsonStorageService.Load(
            SampleCliRunner.ResolveRepoRelativePath(SampleDir + "/EcOrderRemote.json")
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

    /// <summary>DDL の非決定的な生成日時行を固定文言へ正規化する（比較から実質除外する）</summary>
    private static string NormalizeGeneratedAt(string ddl) =>
        GeneratedAtCommentLine.Replace(ddl, "-- Generated at: (normalized)");
}
