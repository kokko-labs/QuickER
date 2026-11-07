namespace QuickER.Tests.TestSupport.Guards;

/// <summary>規則の自己検査の表：テストの土台（tests のソースの書き方）</summary>
/// <remarks>
/// このファイルも tests を文字列で読む規則（プロバイダの指定・フォーカス依存の目印・MainWindow の生成）の範囲に入る。
/// 拾わせたい綴りは割った定数から補間で組み立て、このファイルの本文には現れないようにする。
/// </remarks>
internal static partial class SourceRuleProbes
{
    /// <summary>プロバイダを指定する呼び出しの前半（綴りを割る）</summary>
    private const string ProviderUse = ".Use";

    /// <summary>フォーカス依存の目印（綴りを割る）</summary>
    private const string KeyboardFocusMarker = "IsKeyboard" + "Focus";

    private const string PrimaryDeviceMarker = "Keyboard." + "PrimaryDevice";

    private const string KeyboardFocusCallMarker = "Keyboard." + "Focus(";

    private const string MoveFocusMarker = "Move" + "Focus(";

    private const string DeactivatedMarker = "Deac" + "tivated";

    /// <summary>MainWindow の生成（綴りを割る）</summary>
    private const string MainWindowCreation = "new MainWindow" + "(";

    /// <summary>テストの一時フォルダの親の名前（綴りを割る）</summary>
    private const string TestTempRoot = "QuickER" + "Tests";

    /// <summary>TEMP の直下に一時フォルダを作る BCL の呼び出し（綴りを割る）</summary>
    private const string CreateTempSubdirectory = "CreateTemp" + "Subdirectory";

    private static IEnumerable<SourceRuleProbe> Tests() =>
        [
            // 閉じ括弧の直後に共通の設定が無い呼び出しを拾う（引数の中の文字列・文字のリテラルの括弧は数えない・コメントを挟むのも
            // 直後ではない）。直後に続くもの（改行を挟むもの・逐語的文字列の "" を含むもの）と、別の Use の呼び出しは拾わない
            new SourceRuleProbe(
                "ef-core-provider-cache-limit-forms",
                "ef-core-provider-cache-limit",
                [
                    new ProbeFile(
                        "Probe.cs",
                        $$"""
                        internal static class Probe
                        {
                            public static void Hits(DbContextOptionsBuilder b)
                            {
                                b{{ProviderUse}}Sqlite("Data Source=:memory:").EnableSensitiveDataLogging();
                                _ = b{{ProviderUse}}SqlServer(Connection("(", ')')).Options;
                                b{{ProviderUse}}Npgsql(x)
                                    // shared options must follow the call directly
                                    .IgnoreServiceProviderCacheLimit();
                            }

                            public static void Misses(DbContextOptionsBuilder b)
                            {
                                b{{ProviderUse}}Sqlite("a)b").IgnoreServiceProviderCacheLimit();
                                b{{ProviderUse}}Oracle(@"x"")y")
                                    .IgnoreServiceProviderCacheLimit();
                                b{{ProviderUse}}MySql(Pick(')', "(")).IgnoreServiceProviderCacheLimit();
                                b.UseInMemoryDatabase("x");
                            }
                        }
                        """,
                        ProviderUse + "Sqlite(",
                        ProviderUse + "SqlServer(",
                        ProviderUse + "Npgsql("
                    ),
                ]
            ),
            // 文字列で読む規則は、条件コンパイルの外れる分岐の中の呼び出しも拾う
            new SourceRuleProbe(
                "ef-core-provider-cache-limit-inactive-branch",
                "ef-core-provider-cache-limit",
                [
                    new ProbeFile(
                        "Probe.cs",
                        $$"""
                        internal static class Probe
                        {
                            public static object Build(DbContextOptionsBuilder b)
                            {
                        #if QUICKER_PROBE_BRANCH
                                return b{{ProviderUse}}MySQL("x").Options;
                        #else
                                return b;
                        #endif
                            }
                        }
                        """,
                        ProviderUse + "MySQL("
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 目印を含むのに Collection 属性の無いファイルを 1 件拾う（コメントの中の目印・別のコレクションの属性も）。属性
            // （TestSupport. の有無を問わない）のあるファイルと、目印の無いファイルは拾わない
            new SourceRuleProbe(
                "window-activation-collection-forms",
                "window-activation-collection",
                [
                    new ProbeFile(
                        "Hit.cs",
                        $$"""
                        public class FocusTests
                        {
                            public void Check(UIElement element) => _ = element.{{KeyboardFocusMarker}}ed;
                        }
                        """,
                        "Collection 属性の無いフォーカス依存のソース"
                    ),
                    new ProbeFile(
                        "Comment.cs",
                        $$"""
                        // calls {{MoveFocusMarker}}) indirectly
                        public class CommentTests { }
                        """,
                        "Collection 属性の無いフォーカス依存のソース"
                    ),
                    new ProbeFile(
                        "OtherCollection.cs",
                        $$"""
                        [Collection("Other")]
                        public class OtherTests
                        {
                            public void Check() => {{KeyboardFocusCallMarker}}null);
                        }
                        """,
                        "Collection 属性の無いフォーカス依存のソース"
                    ),
                    new ProbeFile(
                        "Marked.cs",
                        $$"""
                        [Collection(TestSupport.WindowActivationTestCollection.Name)]
                        public class MarkedTests
                        {
                            public void Check() => _ = {{PrimaryDeviceMarker}};
                        }
                        """
                    ),
                    new ProbeFile(
                        "MarkedShort.cs",
                        $$"""
                        [Collection(WindowActivationTestCollection.Name)]
                        public class MarkedShortTests
                        {
                            public void Check(Window window) => window.{{DeactivatedMarker}} += (_, _) => { };
                        }
                        """
                    ),
                    new ProbeFile("Plain.cs", "public class PlainTests { }\n"),
                ]
            ),
            // 5 つの目印のどれでも 1 ファイル 1 件拾い（2 つ含んでも 1 件）、目印の無いファイルは拾わない
            new SourceRuleProbe(
                "window-activation-markers-forms",
                "window-activation-markers",
                [
                    new ProbeFile(
                        "A.cs",
                        $"_ = e.{KeyboardFocusMarker}ed;\n",
                        "フォーカス依存の目印"
                    ),
                    new ProbeFile("B.cs", $"_ = {PrimaryDeviceMarker};\n", "フォーカス依存の目印"),
                    new ProbeFile(
                        "C.cs",
                        $"{KeyboardFocusCallMarker}e);\n",
                        "フォーカス依存の目印"
                    ),
                    new ProbeFile("D.cs", $"e.{MoveFocusMarker}r);\n", "フォーカス依存の目印"),
                    new ProbeFile(
                        "E.cs",
                        $"w.{DeactivatedMarker} += h;\nw.{MoveFocusMarker}r);\n",
                        "フォーカス依存の目印"
                    ),
                    new ProbeFile("Plain.cs", "_ = e.IsFocused;\n"),
                ]
            ),
            // 文字列で読む必須の規則は、条件コンパイルの外れる分岐の中の目印でも満たされる
            new SourceRuleProbe(
                "window-activation-markers-inactive-branch",
                "window-activation-markers",
                [
                    new ProbeFile(
                        "Probe.cs",
                        $$"""
                        public class FocusTests
                        {
                        #if QUICKER_PROBE_BRANCH
                            public void Check(Window window) => window.{{DeactivatedMarker}} += (_, _) => { };
                        #endif
                        }
                        """,
                        "フォーカス依存の目印"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // MainWindow の生成をコメントの中も含めて 1 出現 1 件拾い、名前が MainWindow で始まる別の型の生成は拾わない
            new SourceRuleProbe(
                "wpf-main-window-creations-forms",
                "wpf-main-window-creations",
                [
                    new ProbeFile(
                        "Probe.cs",
                        $$"""
                        internal static class Probe
                        {
                            // do not write {{MainWindowCreation}}...) directly
                            public static object Create(object vm) => {{MainWindowCreation}}vm);

                            public static object Misses() => new MainWindowViewModel();
                        }
                        """,
                        MainWindowCreation,
                        MainWindowCreation
                    ),
                ]
            ),
            // 完全修飾・using static・別名の呼び出しも拾い、入口・文字列は拾わない
            new SourceRuleProbe(
                "direct-roslyn-parses-forms",
                "direct-roslyn-parses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using Microsoft.CodeAnalysis.CSharp;
                        using QuickER.Tests.TestSupport;
                        using static Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree;
                        using T = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree;

                        internal static class Probe
                        {
                            public static void Hits(string text)
                            {
                                CSharpSyntaxTree.ParseText(text);
                                Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(text);
                                SyntaxFactory.ParseSyntaxTree(text);
                                ParseText(text);
                                T.ParseText(text);
                            }

                            public static void Misses(string text)
                            {
                                ConditionalSourceParse.Parse(text);
                                Microsoft.CodeAnalysis.Text.SourceText.From(text);
                                _ = "CSharpSyntaxTree.ParseText(text)";
                            }
                        }
                        """,
                        "ParseText",
                        "ParseText",
                        "ParseSyntaxTree",
                        "ParseText",
                        "ParseText"
                    ),
                ]
            ),
            // 構文の規則は、条件コンパイルの外れる分岐の中の呼び出しも拾う
            new SourceRuleProbe(
                "direct-roslyn-parses-inactive-branch",
                "direct-roslyn-parses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using Microsoft.CodeAnalysis.CSharp;

                        internal static class Probe
                        {
                            public static void Parse(string text)
                            {
                        #if QUICKER_PROBE_BRANCH
                                CSharpSyntaxTree.ParseText(text);
                        #endif
                            }
                        }
                        """,
                        "ParseText"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 親の名前を含む文字列リテラル（そのもの・前置きの連結・逐語的・生の文字列）と補間の文字の部分、CreateTempSubdirectory の
            // 呼び出し（完全修飾・using static）を拾う。コメント・TestTempFolder の使い方・別の名前・別の呼び出しは拾わない
            new SourceRuleProbe(
                "test-temp-folder-root-copies-forms",
                "test-temp-folder-root-copies",
                [
                    new ProbeFile(
                        "Probe.cs",
                        $$""""
                        using System.IO;
                        using static System.IO.Directory;

                        internal static class Probe
                        {
                            public static void Hits(string id)
                            {
                                _ = Path.Combine(Path.GetTempPath(), "{{TestTempRoot}}", id);
                                _ = Path.Combine(Path.GetTempPath(), "{{TestTempRoot}}_" + id);
                                _ = $"{{TestTempRoot}}-blocked-{id}";
                                _ = @"C:\{{TestTempRoot}}\x";
                                _ = """{{TestTempRoot}}""";
                                Directory.{{CreateTempSubdirectory}}("quicker-");
                                System.IO.Directory.{{CreateTempSubdirectory}}();
                                {{CreateTempSubdirectory}}("x");
                            }

                            public static void Misses(string id)
                            {
                                // %TEMP%\{{TestTempRoot}}\<guid> is made by TestTempFolder
                                using var temp = new TestTempFolder();
                                _ = Path.Combine(TestTempFolder.Root, "copilot-bin");
                                _ = Path.Combine(temp.Path, "QuickERTest", id);
                                Directory.CreateDirectory(temp.Path);
                                _ = Path.GetTempFileName();
                            }
                        }
                        """",
                        $"\"{TestTempRoot}\"",
                        $"\"{TestTempRoot}\"",
                        $"\"{TestTempRoot}\"",
                        $"\"{TestTempRoot}\"",
                        $"\"{TestTempRoot}\"",
                        CreateTempSubdirectory,
                        CreateTempSubdirectory,
                        CreateTempSubdirectory
                    ),
                ]
            ),
            // 構文の規則は、条件コンパイルの外れる分岐の中のリテラル・呼び出しも拾う
            new SourceRuleProbe(
                "test-temp-folder-root-copies-inactive-branch",
                "test-temp-folder-root-copies",
                [
                    new ProbeFile(
                        "Probe.cs",
                        $$"""
                        using System.IO;

                        internal static class Probe
                        {
                            public static string Folder()
                            {
                        #if QUICKER_PROBE_BRANCH
                                Directory.{{CreateTempSubdirectory}}("quicker-");
                                return Path.Combine(Path.GetTempPath(), "{{TestTempRoot}}");
                        #else
                                return "";
                        #endif
                            }
                        }
                        """,
                        CreateTempSubdirectory,
                        $"\"{TestTempRoot}\""
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
        ];
}
