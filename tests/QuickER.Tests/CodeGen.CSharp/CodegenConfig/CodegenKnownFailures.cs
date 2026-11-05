using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 生成の構成のエンジンの既知の失敗の一覧（範囲＝行の Id・署名＝段の名前・理由の断片＝失敗の文の言語に依らない部分）
/// </summary>
/// <remarks>
/// <para>
/// 行は決定的なので項目は種を持たない。照合は行ごとに、その行の項目だけを受け持って行う（<see cref="CodegenRunner.RunAsync"/>）。
/// 流していない行（普段の実行の夜間の行の残り）の項目は「起きない」と数えない。載っていない失敗・載っているのに起きない項目・
/// 理由の断片の食い違いは、どれも赤になる。
/// </para>
/// <para>
/// 修正 G（インメモリのシーダーの複合主キー）：インメモリの <c>InMemorySampleData.Seed</c> は全エンティティをシードし、
/// <c>InMemoryDataStore</c> の主キーは <c>EntitySaveMetadata.For</c>（単一主キーだけ）から取るので、複合主キーの表がある図では
/// 既定の <c>AddGeneratedInMemoryRepositories()</c>（<c>seedSampleData: true</c>）が起動時に例外を投げる。
/// 図の形の主の表のキーが複合（Key=composite）でインメモリを生成する、生成が通る行のすべてで起きる。
/// </para>
/// </remarks>
internal static class CodegenKnownFailures
{
    /// <summary>修正 G の直す区切り</summary>
    public const string FixG = "修正 G（インメモリのシーダーの複合主キー）";

    /// <summary>修正 G の失敗の理由の断片（生成物の例外の文の固定部分）</summary>
    public const string FixGReason = "may have only one property with a [Key] attribute";

    /// <summary>修正 G の失敗が起きる行（普段の Success・夜間の Errors・夜間の Success）</summary>
    public static IReadOnlyList<string> FixGRows { get; } =
    [
        // 普段の Success の表
        "s002",
        "s004",
        "s013",
        "s014",
        "s019",
        "s030",
        "s038",
        // 夜間の Errors の表（生成が通る行）
        "n058",
        "n061",
        // 夜間の Success の表
        "t002",
        "t011",
        "t021",
        "t022",
        "t027",
        "t032",
        "t039",
        "t052",
        "t054",
        "t061",
        "t062",
        "t064",
    ];

    /// <summary>一覧</summary>
    public static KnownFailureSet Set { get; } =
        new(
            EngineIds.CodegenConfig,
            FixGRows
                .Select(id => new KnownEntry(
                    "P3-4a フェーズ 3 の探針 InMemorySeed",
                    KnownKind.Pending,
                    FixG,
                    null,
                    id,
                    KnownMatch.ExactSignature,
                    Signature: CodegenRunner.ProbeStage(CodegenProbe.InMemorySeed),
                    ReasonContains: FixGReason
                ))
                .ToList()
        );
}
