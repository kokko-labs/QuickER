namespace QuickER.Tests.Provider;

/// <summary>横断（制約名の一致とエスケープ・コメントの畳み込み・固定文の英語・外部キーを作る関係の判定・カルチャ）</summary>
internal static partial class DialectDdlRows
{
    private static IEnumerable<DialectDdlRow> CrossCuttingRows() =>
        [.. ConstraintNameRows(), .. EnglishRows(), .. CultureRows()];
}
