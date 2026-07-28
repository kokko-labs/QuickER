using System.Globalization;
using QuickER.CodeGen.CSharp.Resources;

namespace QuickER.CodeGen.CSharp.Queries;

/// <summary>
/// クエリ検証の診断文言を「資源キー＋書式引数」で保持し、文字列化（描画）を遅延させる値。
/// </summary>
/// <remarks>
/// <para>
/// 同じ診断でも面によって言語が異なる（GUI・内蔵チャット＝UI 言語追従／外部 AI エージェント向け MCP サーバ＝
/// 英語固定）。生成時点で文字列へ焼き付けると後から言語を選べないため、キーと引数のまま持ち回り、
/// フォーマッタが <see cref="Format(CultureInfo?)"/> にカルチャを明示して描画する。
/// </para>
/// <para>
/// 解決はプロセス共有のグローバル静的（<c>Strings.Culture</c> / <see cref="CultureInfo.CurrentUICulture"/>）を
/// 書き換えず、<see cref="System.Resources.ResourceManager.GetString(string, CultureInfo)"/> の明示カルチャ指定で
/// 行う（テストの並列実行でフレークさせないため。<c>ApiReferenceDocRenderer</c> と同流儀）。
/// </para>
/// </remarks>
/// <param name="ResourceKey">
/// 中立 resx（英語）のキー。呼び出し側は <c>nameof(Strings.Xxx)</c> で与える（綴りをコンパイル時に検査するため）。
/// </param>
/// <param name="Arguments">
/// 書式引数。要素が <see cref="QueryDiagnosticText"/> の場合は同じカルチャで再帰的に描画してから埋め込む
/// （「入力の終端」のような文言そのものを引数に取る診断のため）。
/// </param>
public sealed record QueryDiagnosticText(string ResourceKey, params object?[] Arguments)
{
    /// <summary>
    /// 指定カルチャで描画する。<paramref name="culture"/> が <c>null</c> のときは現在の UI 言語
    /// （<c>Strings.Culture</c> が設定されていればそれ）で描画する。
    /// </summary>
    public string Format(CultureInfo? culture)
    {
        var effective = culture ?? Strings.Culture ?? CultureInfo.CurrentUICulture;
        var format = Strings.ResourceManager.GetString(ResourceKey, effective) ?? ResourceKey;

        if (Arguments.Length == 0)
        {
            return format;
        }

        // 入れ子の診断文言も同じカルチャで描画してから引数として埋め込む
        var arguments = Arguments
            .Select(argument =>
                argument is QueryDiagnosticText nested ? nested.Format(culture) : argument
            )
            .ToArray();

        return string.Format(effective, format, arguments);
    }

    /// <summary>中立言語（英語）で描画する。ヘッドレス実行（外部 MCP サーバ）向けの固定言語</summary>
    public string FormatEnglish() => Format(CultureInfo.InvariantCulture);
}
