using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// API リファレンステンプレートの対訳ペア（英語＝正本 <c>ApiReferenceDoc.scriban</c> と
/// 日本語 <c>ApiReferenceDoc.ja.scriban</c>）が、Scriban 構造（<c>{{ ... }}</c> トークン列）で完全一致することを守る
/// 構造パリティテスト。
/// </summary>
/// <remarks>
/// 文言（見出し・本文・表ヘッダ・コード例のコメント）は言語ごとに自由に違えられるが、Scriban のトークン
/// （変数・ループ・条件）の並びは両言語で同一でなければならない。片側だけトークンを追加・削除・並べ替えると
/// レンダリング結果が言語間で構造的に食い違うため、それを CI で検知する。
/// </remarks>
public sealed class ApiReferenceDocTemplateParityTests
{
    /// <summary>英語（正本）テンプレートの埋め込みリソース名</summary>
    private const string EnglishResourceName =
        "QuickER.CodeGen.CSharp.Templates.ApiReferenceDoc.scriban";

    /// <summary>日本語（併産）テンプレートの埋め込みリソース名</summary>
    private const string JapaneseResourceName =
        "QuickER.CodeGen.CSharp.Templates.ApiReferenceDoc.ja.scriban";

    /// <summary>Scriban トークン（<c>{{ ... }}</c>）を抽出する正規表現（非貪欲・1 トークン単位）</summary>
    private static readonly Regex ScribanTokenPattern = new(@"\{\{.*?\}\}", RegexOptions.Compiled);

    [Fact(
        DisplayName = "英語（正本）と日本語テンプレートの Scriban 構造（{{...}} トークン列）が完全一致する"
    )]
    public void EnglishAndJapaneseTemplates_HaveIdenticalScribanStructure()
    {
        var englishTokens = ExtractScribanTokens(LoadTemplate(EnglishResourceName));
        var japaneseTokens = ExtractScribanTokens(LoadTemplate(JapaneseResourceName));

        // 少なくとも数個の Scriban トークンがあるはず（抽出失敗で空同士が一致し「検証していないのに合格」を防ぐ）
        englishTokens
            .Should()
            .NotBeEmpty("英語テンプレートから Scriban トークンが 1 つも抽出できない");

        japaneseTokens
            .Should()
            .Equal(
                englishTokens,
                "対訳テンプレート（ApiReferenceDoc.scriban / ApiReferenceDoc.ja.scriban）の Scriban 構造は "
                    + "完全一致する必要があります。片側だけ {{...}} トークンを追加・削除・並べ替えると検知されます。"
                    + "テンプレートを編集したら英語・日本語の両方を同時に更新してください（文言は自由・構造は同一）"
            );
    }

    /// <summary>テンプレート本文から Scriban トークン（<c>{{ ... }}</c>）の並びを順序どおり抽出する</summary>
    private static IReadOnlyList<string> ExtractScribanTokens(string template) =>
        ScribanTokenPattern.Matches(template).Select(match => match.Value).ToList();

    /// <summary>埋め込みリソースからテンプレート本文を読み込む</summary>
    private static string LoadTemplate(string resourceName)
    {
        var assembly = typeof(CSharpCodeGenerationService).Assembly;
        using var stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"埋め込みリソース '{resourceName}' が見つかりません。"
            );
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
