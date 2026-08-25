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
}
