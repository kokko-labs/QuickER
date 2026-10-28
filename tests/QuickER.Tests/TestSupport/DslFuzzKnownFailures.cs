using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>簡易 DSL の条件の総当たりの、既知の失敗の一覧</summary>
/// <remarks>
/// <para>
/// キーの入口は <c>dsl-generate:{単位}</c>・<c>dsl-compile:{単位}</c>・<c>dsl-exec:{実装先}:{単位}</c>
/// （SQL Server の派生は <c>dsl-sqlserver-…</c>）。一覧に無い失敗も、載っているのに起きない失敗も赤になる。
/// </para>
/// <para>
/// 現在は空（区切り 23 で、載せていた指摘 29・30・31・A8・B4・B5・NA6・DF1・DF2 をすべて直した）。
/// 直せない失敗を受け入れるときだけ、<see cref="FuzzFailureKey.Known"/>（キー・指摘の番号・直す区切り）で作った項目をここへ足す。
/// </para>
/// </remarks>
public static class DslFuzzKnownFailures
{
    /// <summary>既知の失敗の一覧</summary>
    public static KnownFailureSet Set { get; } = new("Dsl", []);
}
