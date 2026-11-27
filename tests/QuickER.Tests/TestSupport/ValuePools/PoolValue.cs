namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>値のプールの 1 つの値（名前・値・その値をプールに置く理由）</summary>
/// <typeparam name="T">値の型</typeparam>
/// <param name="Name">名前（プールの中で一意。生成の表の値のプールの水準の名前にそのまま使う）</param>
/// <param name="Value">値</param>
/// <param name="Why">その値で答えが変わりうる理由（どの端か）</param>
/// <remarks>
/// 値のプールは BCL だけに依存する（Integration へ Compile のリンクで入り、QuickER.Tests のエンジンからも引ける）。
/// 置き場と使い方は tests/QuickER.Tests/TestSupport/CLAUDE.md「値のプール（ValuePools/）」。
/// </remarks>
public sealed record PoolValue<T>(string Name, T Value, string Why);
