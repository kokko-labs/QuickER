using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の表：テストの土台（tests のソースの書き方）</summary>
/// <remarks>
/// tests を文字列で読む規則（プロバイダの指定・フォーカス依存の目印・MainWindow の生成）が、このファイル自身の文字列を拾わないよう、
/// 目印の綴りはすべて割って書く（範囲は狭めない）。
/// </remarks>
internal static partial class SourceRules
{
    /// <summary>
    /// フォーカス・キー入力・アクティブ状態への依存を表す目印（このファイル自身が写り込まないよう分割して書く）
    /// </summary>
    private static readonly string[] WindowActivationMarkers =
    [
        "IsKeyboard" + "Focus",
        "Keyboard." + "PrimaryDevice",
        "Keyboard." + "Focus(",
        "Move" + "Focus(",
        "Deac" + "tivated",
    ];

    /// <summary>WindowActivation のコレクションへの所属を表す属性</summary>
    private static readonly Regex WindowActivationCollectionAttribute = new(
        @"\[Collection\((TestSupport\.)?WindowActivationTestCollection\.Name\)\]"
    );

    /// <summary>DbContext のプロバイダを指定する呼び出し</summary>
    private static readonly Regex EfCoreProviderCall = new(
        @"\.Use(Sqlite|SqlServer|Npgsql|Oracle|MySQL|MySql)\("
    );

    /// <summary>プロバイダの指定の直後に続けるべき共通の設定</summary>
    private const string EfCoreSharedOptions = ".IgnoreServiceProviderCacheLimit()";

    /// <summary>生成器・レンダラーの出力（メモリ上の文字列）</summary>
    private const string DirectParseGeneratedOutput =
        "生成器・レンダラーの出力（メモリ上の文字列）を 1 つの構成でコンパイル・読み取りする（生成物は条件コンパイルを出さない）";

    /// <summary>生成物をコンパイルする共有の部品</summary>
    private const string DirectParseGeneratedCompiler =
        "生成物（ディスクのフィクスチャ・ランタイムのパッケージ用ソースを含む）とテストの追加ソースを 1 つの構成でコンパイルする共有の部品"
        + "（生成物は条件コンパイルを出さない。走査ではなくコンパイル。生成物が #if を持つようになったら ConditionalSourceParse へ移す）";

    /// <summary>テストが組み立てたソース</summary>
    private const string DirectParseComposedSource =
        "テストが組み立てたソースの文字列をコンパイルする（ディスクのソースではない）";

    private static IEnumerable<SourceRule> Tests() =>
        [
            // テストで DbContext のプロバイダを指定する呼び出しの直後には、必ず共通の設定（EfCoreTestOptions）を続ける。
            // EF Core は構成が違うたびに内部のサービスプロバイダを作り、20 を超えると関係の無い DbContext が警告（既定でエラー）で
            // 失敗する。tests の全体（QuickER.Tests と Integration）の呼び出しを正規表現で見つけ、括弧の対応を読んで（文字列・文字の
            // リテラルの中の括弧は数えない）閉じ括弧の直後を見る。文字列で読むので、コメント・条件コンパイルの外れる分岐の中も数える。
            // 空振りの検知：範囲の中のプロバイダの指定が 41 以上（元のガードの「40 件超」）
            SourceRule.Forbid(
                "ef-core-provider-cache-limit",
                "プロバイダの指定の直後に .IgnoreServiceProviderCacheLimit() を続ける（内部のサービスプロバイダが 20 を超えると関係の無いテストが落ちる）",
                ExceptSuffix(
                    SourceScope.Under("tests"),
                    ".g.cs",
                    "生成物（固定フィクスチャの *.g.cs）は製品のテンプレートの出力で、テストの構成ではない"
                ),
                GuardFinder.File(GuardReader.Text, FindProviderCallsWithoutSharedOptions)
            ) with
            {
                Floor = new(
                    GuardFinder.File(
                        GuardReader.Text,
                        file =>
                            EfCoreProviderCall
                                .Matches(file.Text)
                                .Select(match => new GuardFinding(
                                    "プロバイダの指定",
                                    file.LineAt(match.Index)
                                ))
                    ),
                    41,
                    "プロバイダを指定する呼び出し"
                ),
            },
            // キーボードフォーカス・流したキー入力・ウィンドウのアクティブ状態に結果が左右されるテストのクラスは、WindowActivation の
            // コレクションに入れる（並列で流すと、ほかのテストのウィンドウにアクティブを奪われて全件実行でだけ時々落ちる）。
            // 目印の API を含むのに Collection 属性の無いファイルを 1 件と数える（文字列で読むので、コメントでの言及も数える。
            // 並列から外して失うのは実行時間だけなので、取りこぼさない側へ倒す）
            SourceRule.Forbid(
                "window-activation-collection",
                "フォーカス・キー入力・アクティブ状態に頼るテストのクラスには [Collection(TestSupport.WindowActivationTestCollection.Name)] を付ける",
                SourceScope
                    .Under("tests/QuickER.Tests")
                    .Except(
                        "TestSupport/WindowActivationTestCollection.cs",
                        "コレクションの定義そのもの（目印を説明に書くが、テストを持たない）"
                    )
                    .Except(
                        "TestSupport/GuiFailureDiagnostics.cs",
                        "失敗した瞬間の状態の記録（アクティブ・フォーカスのイベントを受け身で購読して記録するだけで、テストを持たず結果も変えない）"
                    ),
                GuardFinder.File(GuardReader.Text, FindWindowActivationOffenders)
            ),
            // 目印の綴りが崩れて何も拾わなくなると、上の規則は黙って緑になる。既知のフォーカス依存のテスト 3 つを目印が拾えること
            // （文字列で読むので、コメント・条件コンパイルの外れる分岐の中の目印でも満たされる）
            SourceRule.Require(
                "window-activation-markers",
                "目印の API は既知のフォーカス依存のテストを拾える（拾えないと上の規則が空振りする）",
                SourceScope.Files(
                    "tests/QuickER.Tests",
                    "tests/QuickER.Tests/AI.Chat/AiChatDialogApiKeyBoxTests.cs",
                    "tests/QuickER.Tests/Gui/Behaviors/CanvasViewportBehaviorSpaceKeyTests.cs",
                    "tests/QuickER.Tests/Gui/Views/PropertyPanelMemoEditTests.cs"
                ),
                GuardFinder.File(GuardReader.Text, FindWindowActivationMarker),
                new(
                    "tests/QuickER.Tests/AI.Chat/AiChatDialogApiKeyBoxTests.cs",
                    "フォーカス依存の目印",
                    1,
                    "並列の全件実行でだけ落ちた実例（API キーの欄のフォーカス）"
                ),
                new(
                    "tests/QuickER.Tests/Gui/Behaviors/CanvasViewportBehaviorSpaceKeyTests.cs",
                    "フォーカス依存の目印",
                    1,
                    "並列の全件実行でだけ落ちた実例（Space のパンとフォーカス）"
                ),
                new(
                    "tests/QuickER.Tests/Gui/Views/PropertyPanelMemoEditTests.cs",
                    "フォーカス依存の目印",
                    1,
                    "並列の全件実行でだけ落ちた実例（メモの編集のフォーカス）"
                )
            ) with
            {
                MinimumFiles = 3,
            },
            // テストの MainWindow の生成は共有の窓口（WpfApplicationTestSupport.CreateMainWindow / RunInIsolatedWindow）を通す。
            // MainWindow の生成は BAML のロードを伴い、複数の STA テストのクラスが同時に読むと XamlParseException で散発的に落ちる
            // （直列化は窓口が担う）。文字列で読むので、コメントの中の表記も数える
            SourceRule.Forbid(
                "wpf-main-window-creations",
                "MainWindow の生成は WpfApplicationTestSupport.CreateMainWindow / RunInIsolatedWindow を通す（BAML のロードの直列化を外すと --filter の実行で散発的に落ちる）",
                SourceScope.Under("tests/QuickER.Tests"),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindOccurrences(file, "new MainWindow" + "(")
                ),
                new GuardKnown(
                    "tests/QuickER.Tests/TestSupport/WpfApplicationTestSupport.cs",
                    "new MainWindow" + "(",
                    2,
                    "直列化の窓口そのもの（生成 1 か所と、窓口を通せという説明 1 か所）"
                )
            ),
            // テストが src・tests のソースをパースするときは ConditionalSourceParse を通す（1 通りの記号でパースすると、外れる分岐の
            // 禁止の呼び出しを見落とす）。Roslyn のパースの直接の呼び出し（メソッド名が ParseText / ParseSyntaxTree。受け手の書き方・
            // 完全修飾・using static を問わない）を数え、生成器・レンダラーの出力やテストが組み立てた文字列を 1 つの構成でコンパイル・
            // 読み取りするもの・入口そのもの・自己検査の対照とプローブだけを理由つきで許す
            SourceRule.Forbid(
                "direct-roslyn-parses",
                "ソースファイルの走査は ConditionalSourceParse を通し（#if のどの分岐も読む）、例外は理由つきで一覧に載せる",
                SourceScope.Under("tests"),
                GuardFinder.Nodes(DescribeDirectParse),
                new(
                    "tests/QuickER.Tests/CodeGen.CSharp/GeneratedCodeCompiler.cs",
                    "ParseText",
                    1,
                    DirectParseGeneratedCompiler
                ),
                new(
                    "tests/QuickER.Tests/CodeGen.CSharp/GeneratedFixedMemberDriftTests.cs",
                    "ParseText",
                    1,
                    DirectParseGeneratedOutput
                ),
                new(
                    "tests/QuickER.Tests/CodeGen.CSharp/QueryCodegenDiagnosticsTests.cs",
                    "ParseText",
                    1,
                    DirectParseComposedSource
                ),
                new(
                    "tests/QuickER.Tests/CodeGen.CSharp/QueryLiteralEscapeScanTests.cs",
                    "ParseText",
                    1,
                    DirectParseGeneratedOutput
                ),
                new(
                    "tests/QuickER.Tests/TestSupport/ConditionalSourceParse.cs",
                    "ParseText",
                    2,
                    "src・tests のソースをパースする唯一の入口（基準の構文木と、記号の組み合わせごとの構文木）"
                ),
                new(
                    "tests/QuickER.Tests/TestSupport/ConditionalSourceParseTests.cs",
                    "ParseText",
                    2,
                    "自己検査の対照（1 通りのパースが分岐の中を見落とすことを示す）"
                ),
                new(
                    "tests/QuickER.Tests/TestSupport/SourceSymbolScan.cs",
                    "ParseText",
                    1,
                    "自己検査のプローブの文字列をコンパイルする（CompileProbe）"
                )
            ),
            // テストホストはサーバー GC で動かす。並列の全件実行では生成コードのコンパイルと総当たりが大量に割り当て、既定の
            // ワークステーション GC だと CPU を余したまま GC の待ちで全件の時間が倍になる（消しても赤になるテストは無く、遅くなるだけ）。
            // csproj を XML として読む（コメントの中の表記・条件つきの設定・true でない値は数えない）
            SourceRule.Require(
                "test-host-server-gc",
                "テストのプロジェクトはサーバー GC を有効にする（外すと全件実行が GC の待ちで倍に伸びる）",
                SourceScope.Files("tests/QuickER.Tests", TestProjectFile),
                GuardFinder.File(GuardReader.Xml, FindServerGcSettings),
                new GuardKnown(
                    TestProjectFile,
                    ServerGcSetting,
                    1,
                    "テストホストの GC の設定（runtimeconfig へ載る）"
                )
            ),
        ];

    /// <summary>QuickER.Tests のプロジェクトファイル</summary>
    private const string TestProjectFile = "tests/QuickER.Tests/QuickER.Tests.csproj";

    /// <summary>サーバー GC を有効にする設定の表記</summary>
    private const string ServerGcSetting = "ServerGarbageCollection=true";

    /// <summary>
    /// 条件の無い PropertyGroup の中で、条件の無い <c>ServerGarbageCollection</c> の値が true のもの
    /// （MSBuild の真偽値は大文字小文字を区別しないので、前後の空白を除いて大小を無視して比べる）
    /// </summary>
    private static IEnumerable<GuardFinding> FindServerGcSettings(GuardFile file) =>
        XDocument
            .Parse(file.Text, LoadOptions.SetLineInfo)
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "ServerGarbageCollection"
                && element.Parent?.Name.LocalName == "PropertyGroup"
                && element.Attribute("Condition") is null
                && element.Parent.Attribute("Condition") is null
                && string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            )
            .Select(element => new GuardFinding(
                ServerGcSetting,
                ((IXmlLineInfo)element).LineNumber
            ));

    /// <summary>範囲から、根の下の名前の末尾が同じファイルを 1 つずつ除く（いまあるファイルを列挙して除外にする）</summary>
    private static SourceScope ExceptSuffix(SourceScope scope, string suffix, string reason) =>
        SourceFiles
            .Enumerate(scope.Root, "*" + scope.Extension)
            .Select(file => file.AreaRelativePath)
            .Where(path => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Aggregate(scope, (current, path) => current.Except(path, reason));

    /// <summary>直後に共通の設定が続かないプロバイダの指定（何かは呼び出しの表記・行は呼び出しの行）</summary>
    private static IEnumerable<GuardFinding> FindProviderCallsWithoutSharedOptions(GuardFile file)
    {
        foreach (Match match in EfCoreProviderCall.Matches(file.Text))
        {
            var close = MatchingParenthesis(file.Text, match.Index + match.Length - 1);
            var rest = file.Text[(close + 1)..].TrimStart();

            if (!rest.StartsWith(EfCoreSharedOptions, StringComparison.Ordinal))
            {
                yield return new GuardFinding(match.Value, file.LineAt(match.Index));
            }
        }
    }

    /// <summary>開き括弧の位置から、対応する閉じ括弧の位置を返す（文字列・文字のリテラルの中の括弧は数えない）</summary>
    private static int MatchingParenthesis(string text, int open)
    {
        var depth = 0;

        for (var i = open; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '"':
                    i = SkipLiteral(text, i, '"');
                    break;
                case '\'':
                    i = SkipLiteral(text, i, '\'');
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;

                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        throw new InvalidOperationException($"閉じ括弧が見つからない（位置 {open}）");
    }

    /// <summary>リテラルの終わりの位置を返す（逐語的文字列の "" と、通常のリテラルの \ のエスケープを扱う）</summary>
    private static int SkipLiteral(string text, int start, char quote)
    {
        var verbatim = quote == '"' && start > 0 && text[start - 1] == '@';

        for (var i = start + 1; i < text.Length; i++)
        {
            if (!verbatim && text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == quote)
            {
                if (verbatim && i + 1 < text.Length && text[i + 1] == quote)
                {
                    i++;
                }
                else
                {
                    return i;
                }
            }
        }

        return text.Length - 1;
    }

    /// <summary>本文でいちばん前にある目印の位置（目印が無ければ null）</summary>
    private static int? FirstWindowActivationMarker(string text) =>
        WindowActivationMarkers
            .Select(marker => text.IndexOf(marker, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .Select(index => (int?)index)
            .Min();

    /// <summary>目印を含むのに Collection 属性の無いファイル（1 ファイル 1 件・行は最初の目印の行）</summary>
    private static IEnumerable<GuardFinding> FindWindowActivationOffenders(GuardFile file) =>
        FirstWindowActivationMarker(file.Text) is { } index
        && !WindowActivationCollectionAttribute.IsMatch(file.Text)
            ? [new GuardFinding("Collection 属性の無いフォーカス依存のソース", file.LineAt(index))]
            : [];

    /// <summary>目印を含むファイル（1 ファイル 1 件・行は最初の目印の行）</summary>
    private static IEnumerable<GuardFinding> FindWindowActivationMarker(GuardFile file) =>
        FirstWindowActivationMarker(file.Text) is { } index
            ? [new GuardFinding("フォーカス依存の目印", file.LineAt(index))]
            : [];

    /// <summary>節が Roslyn のパースの直接の呼び出しなら、そのメソッド名（メソッド名で判定する）</summary>
    private static string? DescribeDirectParse(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };

        return name is "ParseText" or "ParseSyntaxTree" ? name : null;
    }
}
