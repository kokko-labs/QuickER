using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Platform;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Platform;

/// <summary>
/// ファイルへの書き込みは <see cref="AtomicFile"/> を通し、素の書き込みは理由つきの一覧にあるものだけであることを固定する
/// </summary>
/// <remarks>
/// <para>
/// 素の書き込みは既存のファイルを切り詰めてから書くので、途中で落ちる・ディスクが満杯になると、利用者のファイル
/// （エクスポート・生成物・モックの出力・設定・秘密）が中途半端な内容で残る。原子的な書き込みの正本があっても、
/// 新しい書き込みを素で書けばそこだけが取り残されるので、全数で数える。
/// </para>
/// <para>
/// 拾う基準：<c>src/</c> の <c>QuickER.Platform</c> 以外の <c>.cs</c>（<c>bin</c> / <c>obj</c> を除く）を構文木で走査し、
/// 次を数える。コメント・XmlDoc・文字列の中の言及は数えない。
/// </para>
/// <list type="bullet">
/// <item><c>File.</c> の書き込みのメソッド（<see cref="WriteMethods"/>）の呼び出し</item>
/// <item><c>File.Open</c> と <c>new FileStream(</c> のうち、引数に書き込みの FileMode / FileAccess（<see cref="WriteModes"/>）を含むもの</item>
/// <item><c>new StreamWriter(</c>（パスを受ける形とストリームを包む形を構文では見分けられないので、すべて数える）</item>
/// <item><c>.SaveAs(</c>（ClosedXML のブックの保存）</item>
/// </list>
/// <para>
/// <c>AtomicFile.Write</c> へ渡すラムダの中は数えない（書き先は原子的な書き込みの一時ファイルのストリーム）。
/// ライブラリ固有の「パスへ保存する」メソッドは <c>SaveAs</c> 以外を認識しない。そうした API を使うときは、ストリームを受ける形を
/// <c>AtomicFile.Write</c> の中で呼ぶ。
/// </para>
/// <para>
/// 正当な出現は理由つきで <see cref="Known"/> に載せる。照合は厳密で、一覧に無い出現も、一覧にあるのに無い出現（件数の違いを含む）も赤にする。
/// </para>
/// </remarks>
public sealed class RawFileWriteGuardTests
{
    /// <summary>一時フォルダの一意の名前のファイル（使い終わったら消す）</summary>
    private const string TemporaryFile =
        "%TEMP% の一意の名前の一時ファイル（子プロセスへ渡すために書き、使い終わったら消す）";

    /// <summary>読めなかった設定の退避</summary>
    private const string CorruptBackup =
        "読めなかった設定ファイルを脇へ写す（失敗しても元のファイルは残る。写し先は退避専用の名前）";

    /// <summary>依存ゼロのランタイムのパッケージ</summary>
    private const string RuntimePackage =
        "依存ゼロのランタイムのパッケージは QuickER.Platform を参照できない（自前で一時ファイル→Move をする・同期の一時ファイル）";

    /// <summary>既知の出現（ファイル・呼び出し・件数・理由）</summary>
    private static readonly (string File, string Call, int Count, string Reason)[] Known =
    [
        ("src/QuickER.AI/ClaudeCodeChatEngine.cs", "File.WriteAllBytes", 1, TemporaryFile),
        ("src/QuickER.AI/ClaudeCodeChatEngine.cs", "File.WriteAllText", 1, TemporaryFile),
        ("src/QuickER.AI/ClaudeCodeClient.cs", "File.WriteAllText", 1, TemporaryFile),
        (
            "src/QuickER.Gui/Services/CrashHandlingService.cs",
            "new FileStream",
            1,
            "クラッシュログは一意の名前を CreateNew で作る（既存を上書きしない＝壊す相手が無い）"
        ),
        (
            "src/QuickER.Gui/Services/CrashHandlingService.cs",
            "new StreamWriter",
            1,
            "上の CreateNew のストリームを包む"
        ),
        ("src/QuickER.Db.UI/SqlConnectionProfileStore.cs", "File.Copy", 1, CorruptBackup),
        ("src/QuickER.Settings/JsonSettingsStore.cs", "File.Copy", 1, CorruptBackup),
        ("src/QuickER.Runtime/Runtime.g.cs", "new FileStream", 1, RuntimePackage),
        ("src/QuickER.Runtime/Runtime.g.cs", "File.Move", 1, RuntimePackage),
        ("src/QuickER.Runtime.Sync/Runtime.Sync.g.cs", "new FileStream", 1, RuntimePackage),
    ];

    /// <summary>数える <c>File.</c> のメソッド（書き込み・作成・写し・移動・置き換え）</summary>
    private static readonly HashSet<string> WriteMethods =
    [
        nameof(File.WriteAllText),
        nameof(File.WriteAllTextAsync),
        nameof(File.WriteAllBytes),
        nameof(File.WriteAllBytesAsync),
        nameof(File.WriteAllLines),
        nameof(File.WriteAllLinesAsync),
        nameof(File.AppendAllText),
        nameof(File.AppendAllTextAsync),
        nameof(File.AppendAllLines),
        nameof(File.AppendAllLinesAsync),
        nameof(File.Create),
        nameof(File.CreateText),
        nameof(File.AppendText),
        nameof(File.OpenWrite),
        nameof(File.Copy),
        nameof(File.Move),
        nameof(File.Replace),
    ];

    /// <summary>書き込みを表す FileMode / FileAccess の値</summary>
    private static readonly HashSet<string> WriteModes =
    [
        nameof(FileMode.Create),
        nameof(FileMode.CreateNew),
        nameof(FileMode.OpenOrCreate),
        nameof(FileMode.Append),
        nameof(FileMode.Truncate),
        nameof(FileAccess.Write),
        nameof(FileAccess.ReadWrite),
    ];

    [Fact(
        DisplayName = "ファイルへの素の書き込みは、QuickER.Platform の外では理由つきの一覧にあるものだけ"
    )]
    public void RawFileWrites_OutsidePlatform_AreOnlyKnown()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();
        var src = Path.Combine(root, "src");
        var platform = Path.Combine(src, "QuickER.Platform") + Path.DirectorySeparatorChar;
        var found = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(platform, StringComparison.OrdinalIgnoreCase))
            .Where(path =>
                !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj")
            )
            .SelectMany(path =>
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var tree = CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path),
                    cancellationToken: TestContext.Current.CancellationToken
                );

                return tree.GetRoot(TestContext.Current.CancellationToken)
                    .DescendantNodes()
                    .Where(node => !IsInsideAtomicWrite(node))
                    .Select(Describe)
                    .OfType<string>()
                    .Select(call => (File: relative, Call: call));
            })
            .GroupBy(hit => hit)
            .ToDictionary(group => group.Key, group => group.Count());

        var expected = Known.ToDictionary(known => (known.File, known.Call), known => known.Count);

        var unexpected = found
            .Where(pair => !expected.TryGetValue(pair.Key, out var count) || count != pair.Value)
            .Select(pair => $"{pair.Key.File}: {pair.Key.Call} x{pair.Value}")
            .ToList();
        var stale = expected
            .Where(pair => !found.TryGetValue(pair.Key, out var count) || count != pair.Value)
            .Select(pair => $"{pair.Key.File}: {pair.Key.Call} x{pair.Value}")
            .ToList();

        unexpected
            .Should()
            .BeEmpty("ファイルへの書き込みは AtomicFile を通し、例外は理由つきで一覧に載せる");
        stale.Should().BeEmpty("無くなった出現は一覧から消す");
    }

    /// <summary>数える書き込みなら、その表記（例 <c>File.WriteAllText</c>・<c>new FileStream</c>）を返す</summary>
    private static string? Describe(SyntaxNode node)
    {
        switch (node)
        {
            case InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax member,
            } invocation:
            {
                var name = member.Name.Identifier.ValueText;

                if (name == "SaveAs")
                {
                    return "SaveAs";
                }

                if (!IsFileClass(member.Expression))
                {
                    return null;
                }

                if (WriteMethods.Contains(name))
                {
                    return $"File.{name}";
                }

                return name == nameof(File.Open) && HasWriteMode(invocation.ArgumentList)
                    ? "File.Open"
                    : null;
            }

            case ObjectCreationExpressionSyntax creation:
            {
                var type = LastIdentifier(creation.Type);

                if (type == nameof(StreamWriter))
                {
                    return "new StreamWriter";
                }

                return type == nameof(FileStream) && HasWriteMode(creation.ArgumentList)
                    ? "new FileStream"
                    : null;
            }

            default:
                return null;
        }
    }

    /// <summary>式が <c>File</c> クラス（<c>File</c>・<c>System.IO.File</c>）を指すか</summary>
    private static bool IsFileClass(ExpressionSyntax expression) =>
        expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == nameof(File),
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText == nameof(File),
            _ => false,
        };

    /// <summary>型の名前の最後の識別子（<c>System.IO.FileStream</c> なら <c>FileStream</c>）</summary>
    private static string? LastIdentifier(TypeSyntax type) =>
        type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            _ => null,
        };

    /// <summary>引数に書き込みの FileMode / FileAccess の値を含むか</summary>
    private static bool HasWriteMode(ArgumentListSyntax? arguments) =>
        arguments is not null
        && arguments
            .DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(access =>
                access.Expression is IdentifierNameSyntax or MemberAccessExpressionSyntax
                && access.Expression.ToString().Split('.')[^1]
                    is nameof(FileMode)
                        or nameof(FileAccess)
                && WriteModes.Contains(access.Name.Identifier.ValueText)
            );

    /// <summary>節が <c>AtomicFile.Write</c> の引数（一時ファイルのストリームへ書くラムダ）の中にあるか</summary>
    private static bool IsInsideAtomicWrite(SyntaxNode node) =>
        node.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax member
                && member.Name.Identifier.ValueText == nameof(AtomicFile.Write)
                && member.Expression.ToString().Split('.')[^1] == nameof(AtomicFile)
            );
}
