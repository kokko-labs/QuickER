namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>値の池の 1 つの値（名前・値・その値を池に置く理由）</summary>
/// <typeparam name="T">値の型</typeparam>
/// <param name="Name">名前（池の中で一意。生成の表の値の池の水準の名前にそのまま使う）</param>
/// <param name="Value">値</param>
/// <param name="Why">その値で答えが変わりうる理由（どの端か）</param>
/// <remarks>
/// 値の池は BCL だけに依存する（Integration へ Compile のリンクで入り、QuickER.Tests のエンジンからも引ける）。
/// 置き場と使い方は tests/QuickER.Tests/TestSupport/CLAUDE.md「値の池（ValuePools/）」。
/// </remarks>
public sealed record PoolValue<T>(string Name, T Value, string Why);
