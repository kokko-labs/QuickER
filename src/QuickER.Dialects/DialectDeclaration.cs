namespace QuickER.Dialects;

/// <summary>その方言にその種類があり得るか（向き 2 では、方言がその定義を表せるか）</summary>
public enum DialectOccurrence
{
    /// <summary>あり得る（向き 2：表せる）</summary>
    Possible,

    /// <summary>一部の形だけあり得る（向き 2：一部だけ表せる）</summary>
    Partial,

    /// <summary>あり得ない（向き 2：表せない）</summary>
    Impossible,
}

/// <summary>取込でその種類を検出するか</summary>
public enum ImportDetection
{
    /// <summary>検出する</summary>
    Detected,

    /// <summary>検出しない（理由を <see cref="DialectKindDeclaration.NotDetectedReason"/> に書く）</summary>
    NotDetected,

    /// <summary>該当しない（あり得ない種類・向き 2 の種類）</summary>
    NotApplicable,
}

/// <summary>同期の列の変更・表の作り直しが、その定義を落とすか</summary>
public enum SyncAlterEffect
{
    /// <summary>落とす</summary>
    Drops,

    /// <summary>保つ</summary>
    Keeps,

    /// <summary>該当しない（同期の操作が触らない・あり得ない種類・向き 2 の種類）</summary>
    NotApplicable,
}

/// <summary>方言 × 種類の宣言</summary>
/// <param name="Occurrence">あり得るか（向き 2 では表せるか）</param>
/// <param name="Detection">取込で検出するか</param>
/// <param name="SyncEffect">同期の操作が落とすか</param>
/// <param name="NotDetectedReason">検出しない理由（<see cref="ImportDetection.NotDetected"/> のときだけ書く）</param>
/// <remarks>
/// 既定値を持たない（全方言 × 全種類を <see cref="DialectDeclarations"/> が書き、網羅テストが欠けを赤にする）。
/// 組み合わせの決まり（あり得ないなら検出も同期も該当しない、検出しないなら理由がある、向き 2 は検出も同期も該当しない）は
/// 網羅テストが確かめる。
/// </remarks>
public sealed record DialectKindDeclaration(
    DialectOccurrence Occurrence,
    ImportDetection Detection,
    SyncAlterEffect SyncEffect,
    string? NotDetectedReason
);
