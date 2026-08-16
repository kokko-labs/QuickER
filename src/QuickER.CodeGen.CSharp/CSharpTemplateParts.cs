namespace QuickER.CodeGen.CSharp;

/// <summary>
/// C# 生成テンプレート（<c>Templates/CSharpRuntime/*.scriban</c>）の部品リソース名と、その固定連結順の唯一の正本。
/// </summary>
/// <remarks>
/// <para>
/// テンプレート本文は保守性のため機能単位の部品ファイルへ物理分割されており、<c>ScribanCSharpRenderer</c> が
/// <see cref="OrderedResourceNames"/> の順で単純連結（セパレータなし）して 1 本のテンプレート本文へ復元する。
/// 各部品は分割前テンプレートの連続した行範囲そのもの（1 バイトも編集・並べ替えをしない）で、連結結果が分割前と
/// バイト完全一致することが「生成コードのバイト一致」という不変条件の前提になる。
/// </para>
/// <para>
/// 公開しているのはガードテスト（<c>TemplatePartConcatenationTests</c>）が参照するため
/// （このアセンブリに <c>InternalsVisibleTo</c> は無く、テストは公開 API 越しに検証する方針＝
/// <see cref="GeneratedFixedMemberNames"/> と同じ流儀）。順序・集合・改行コードはビルドでも型検査でも守られず、
/// 崩すと 19 万行のドリフト差分としてしか現れないため、名指しで表明する層を 1 枚置く。
/// </para>
/// </remarks>
public static class CSharpTemplateParts
{
    /// <summary>部品リソース名の共通接頭辞（埋め込みリソースの論理名）</summary>
    public const string ResourceNamePrefix = "QuickER.CodeGen.CSharp.Templates.CSharpRuntime.";

    /// <summary>部品リソース名の共通拡張子</summary>
    public const string ResourceNameSuffix = ".scriban";

    /// <summary>
    /// テンプレート部品リソース名の固定連結順。番号順（＝元テンプレートの行順）で連結すると
    /// 分割前の本文へバイト完全一致で復元される。この配列の順序を崩すと生成コードが壊れるため、並べ替えてはならない。
    /// </summary>
    public static IReadOnlyList<string> OrderedResourceNames { get; } =
    [
        ResourceNamePrefix + "_00_HeaderAttributes" + ResourceNameSuffix,
        ResourceNamePrefix + "_01_ValueObjects" + ResourceNameSuffix,
        ResourceNamePrefix + "_02_Entities" + ResourceNameSuffix,
        ResourceNamePrefix + "_03_EditModelsAndMappers" + ResourceNameSuffix,
        ResourceNamePrefix + "_04_RepositoryContractCore" + ResourceNameSuffix,
        ResourceNamePrefix + "_05_QueryPipeline" + ResourceNameSuffix,
        ResourceNamePrefix + "_06_RemoteSaveInfraAndDI" + ResourceNameSuffix,
        ResourceNamePrefix + "_07_InMemory" + ResourceNameSuffix,
        ResourceNamePrefix + "_08_EfCore" + ResourceNameSuffix,
        ResourceNamePrefix + "_09_RemoteServer" + ResourceNameSuffix,
    ];
}
