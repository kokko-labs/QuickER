using System;

namespace QuickER.Tests.Integration;

/// <summary>
/// コンテナフィクスチャの Docker 要求ポリシー（環境変数 <c>QUICKER_REQUIRE_DOCKER</c>）。
/// </summary>
/// <remarks>
/// <para>
/// コンテナ起動失敗のスキップ変換は「Docker の無い環境でスイートを赤くしない」ための仕掛けだが、
/// 例外を全捕捉するため「Docker はあるのに構成が壊れた」（イメージ pull 失敗・デーモン更新・
/// フィクスチャ自身のバグ）もスキップ緑に化ける。壊れた構成と Docker 不在は例外からは区別できない
/// （CI の windows-latest は「Docker はあるが Linux コンテナが動かない」環境）ため、
/// 「この環境には動く Docker があるはず」という<b>環境側の知識</b>を環境変数で宣言させる。
/// </para>
/// <para>
/// 厳格モード（<c>QUICKER_REQUIRE_DOCKER=1</c>）ではフィクスチャは起動失敗を握らずそのまま投げ、
/// コレクションの全テストが失敗する。CI の ubuntu ジョブと Docker 稼働の開発機で設定する。
/// 未設定の環境（windows-latest・Docker の無い開発機）は従来どおりスキップになる。
/// </para>
/// </remarks>
internal static class DockerRequirement
{
    /// <summary>厳格モードを宣言する環境変数名</summary>
    public const string VariableName = "QUICKER_REQUIRE_DOCKER";

    /// <summary>厳格モード（Docker があるはずの環境＝起動失敗をスキップへ変換しない）かどうか</summary>
    public static bool IsStrict => Environment.GetEnvironmentVariable(VariableName) == "1";

    /// <summary>コンテナを起動せずに飛ばすことを宣言する環境変数名</summary>
    /// <remarks>
    /// 変異テストの起動スクリプト（<c>scripts/run-mutation.ps1</c>）が、Integration を含む範囲でだけ <c>1</c> にする。
    /// 環境に Docker があるかに依らず、コンテナのフィクスチャは起動を試みずに <c>IsAvailable</c> を <c>false</c> にする
    /// （Docker の要らないテストだけを決まって流すため。DOCKER_HOST を届かない値にしても Testcontainers は次の候補へ落ちて
    /// 本物の Docker を見つける）。普段の <c>dotnet test</c>・CI・夜間では立てない。
    /// <para>
    /// 厳格モード（<see cref="VariableName"/>）と両方立っていたら断る（<see cref="SkipReason"/> の例外）。これは手で両方を立てたときの守りで、
    /// ワークフローと起動スクリプトが同時に立てないことは QuickER.Tests のガードが固定する（どのワークフローにもこの変数を書かない・
    /// 起動スクリプトはこの変数を立てる範囲で厳格モードの宣言を外す・Testcontainers を使うどのフィクスチャも <see cref="SkipReason"/> を呼ぶ。
    /// <c>tests/QuickER.Tests/TestSupport/CLAUDE.md</c> の「変異テスト（Stryker.NET）」）。
    /// </para>
    /// </remarks>
    public const string SkipVariableName = "QUICKER_SKIP_DOCKER";

    /// <summary>コンテナを起動せずに飛ばすなら、その理由（飛ばさないなら null）</summary>
    /// <param name="fixtureName">フィクスチャの名前（理由の文に入れる）</param>
    /// <param name="environment">環境変数の読み取り（null ならプロセスの環境変数）</param>
    /// <exception cref="InvalidOperationException">厳格モードと同時に立っている（矛盾する宣言）</exception>
    public static string? SkipReason(string fixtureName, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;

        if (environment(SkipVariableName) != "1")
        {
            return null;
        }

        if (environment(VariableName) == "1")
        {
            throw new InvalidOperationException(
                $"{SkipVariableName}=1 と {VariableName}=1 が同時に立っている（Docker を飛ばす宣言と、Docker があるはずという宣言は両立しない）"
            );
        }

        return $"{fixtureName} を起動しない（{SkipVariableName}=1）";
    }
}
