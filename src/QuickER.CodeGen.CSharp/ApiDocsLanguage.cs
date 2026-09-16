using System.Text.Json.Serialization;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// API リファレンス Markdown（<see cref="CodeGenerationOptions.GenerateApiDocs"/>）を出力する言語
/// </summary>
/// <remarks>
/// 設定 JSON（quicker.json・GUI の codegen-settings.json）・CLI の <c>--api-docs-lang</c> は、どれもメンバー名
/// （<c>English</c> / <c>Japanese</c> / <c>Both</c>）で指定する。大文字小文字を区別しないのは設定 JSON だけで、
/// CLI フラグは候補との序数比較のため正確な綴りが要る。型に付けた変換器が設定 JSON の読み書きの両方を名前で行うため、
/// 呼び出し側ごとに変換器を登録する必要はない。
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ApiDocsLanguage>))]
public enum ApiDocsLanguage
{
    /// <summary>英語版 <c>{ベース名}.g.md</c> だけを出力する（既定）</summary>
    English,

    /// <summary>日本語版 <c>{ベース名}.ja.g.md</c> だけを出力する</summary>
    Japanese,

    /// <summary>英語版 <c>{ベース名}.g.md</c> と日本語版 <c>{ベース名}.ja.g.md</c> の両方を出力する</summary>
    Both,
}
