using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Platform;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Platform;

/// <summary>
/// ファイルへの書き込みは <see cref="AtomicFile"/> を通し、素の書き込み・削除は理由つきの一覧にあるものだけであることを固定する
/// </summary>
/// <remarks>
/// <para>
/// 素の書き込みは既存のファイルを切り詰めてから書くので、途中で落ちる・ディスクが満杯になると、利用者のファイル
/// （エクスポート・生成物・モックの出力・設定・秘密）が中途半端な内容で残る。原子的な書き込みの正本があっても、
/// 新しい書き込みを素で書けばそこだけが取り残されるので、全数で数える。
/// </para>
/// <para>
/// 拾う基準：<c>src/</c> の <c>QuickER.Platform</c> 以外のプロジェクトを <see cref="SourceSymbolScan"/> で組み、呼び出し・生成が指す
/// メソッドのシンボルで数える（目的型の <c>new()</c>・<c>using static</c>・別名・<c>FileInfo</c> も同じ API として拾う）。
/// 数えるのは <see cref="FileWriteCalls.Describe"/> の API（パスへ書く・作る・写す・移す・置き換える・削除する）で、ストリームを
/// 受ける多重定義（<c>new StreamWriter(stream)</c>・<c>SaveAs(stream)</c>）はファイルの書き込みではないので数えない。
/// そのため <c>AtomicFile.Write</c> へ渡すラムダの中も特別扱いしない（ストリームへ書く呼び出しは数えず、パスへの素の書き込みは数える）。
/// 対象の名前の呼び出しでシンボルが解決しなかったものは「未解決」として同じ一覧で照合する（数えない側へ倒さない）。
/// </para>
/// <para>
/// 正当な出現は理由つきで <see cref="Known"/> に載せる。照合は厳密で、一覧に無い出現も、一覧にあるのに無い出現（件数の違いを含む）も赤にする。
/// </para>
/// </remarks>
public sealed class RawFileWriteGuardTests(ITestOutputHelper output)
{
    /// <summary>一時フォルダの一意の名前のファイル（使い終わったら消す）</summary>
    private const string TemporaryFile =
        "%TEMP% の一意の名前の一時ファイル（子プロセスへ渡すために書き、使い終わったら消す）";

    /// <summary>読めなかった設定の退避</summary>
    private const string CorruptBackup =
        "読めなかった設定ファイルを脇へ写す（失敗しても元のファイルは残る。写し先は退避専用の名前）";

    /// <summary>依存ゼロのランタイムのパッケージ</summary>
    private const string RuntimePackage =
        "依存ゼロのランタイムのパッケージは QuickER.Platform を参照できない（自前で一時ファイル→置き換えをする・同期の一時ファイル）";

    /// <summary>自分が作った一時物の後片付け</summary>
    private const string OwnTemporaryCleanup =
        "自分が作った一時物（一意の名前の一時ファイル・作業フォルダ）を使い終わって消す";

    /// <summary>空の値の保存＝保存済みの秘密を消す</summary>
    private const string SecretRemoval =
        "利用者が空の値を保存した（保存をやめた）ので、保存済みの暗号ファイルを消す";

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
        ("src/QuickER.Db.UI/SqlConnectionProfileStore.cs", "File.Copy", 1, CorruptBackup),
        ("src/QuickER.Settings/JsonSettingsStore.cs", "File.Copy", 1, CorruptBackup),
        ("src/QuickER.Runtime/Runtime.g.cs", "new FileStream", 1, RuntimePackage),
        ("src/QuickER.Runtime/Runtime.g.cs", "File.Move", 2, RuntimePackage),
        ("src/QuickER.Runtime/Runtime.g.cs", "File.Replace", 1, RuntimePackage),
        ("src/QuickER.Runtime.Sync/Runtime.Sync.g.cs", "new FileStream", 1, RuntimePackage),
        ("src/QuickER.Runtime/Runtime.g.cs", "File.Delete", 1, RuntimePackage),
        ("src/QuickER.Runtime.Sync/Runtime.Sync.g.cs", "File.Delete", 1, RuntimePackage),
        ("src/QuickER.AI/BackendWorkingDirectory.cs", "Directory.Delete", 1, OwnTemporaryCleanup),
        ("src/QuickER.AI/ClaudeCodeClient.cs", "File.Delete", 1, OwnTemporaryCleanup),
        (
            "src/QuickER.AI.Mock/MockFolderStore.cs",
            "File.Delete",
            1,
            "remove_screen が、ツールの名指しした画面の HTML をモックフォルダから消す（名前は ValidateScreenFileName を通る）"
        ),
        (
            "src/QuickER.AI.Mock/MockProjectFiles.cs",
            "DirectoryInfo.Delete",
            2,
            "最終ビルドの前に、プロジェクトの obj / bin を消す（リンクはリンクだけを外す。ビルド前の変更検知の範囲と揃える）"
        ),
        ("src/QuickER.AI.UI/ApiKeyStore.cs", "File.Delete", 1, SecretRemoval),
        ("src/QuickER.Db.UI/SqlConnectionProfileStore.cs", "File.Delete", 1, SecretRemoval),
    ];

    [Fact(
        DisplayName = "ファイルへの素の書き込み・削除は、QuickER.Platform の外では理由つきの一覧にあるものだけ"
    )]
    public void RawFileWrites_OutsidePlatform_AreOnlyKnown()
    {
        var found = SourceSymbolScan
            .SrcProjects.Where(project => project.Name != "QuickER.Platform")
            .SelectMany(project =>
                project
                    .ScanNodes(TestContext.Current.CancellationToken)
                    .Select(item =>
                        (
                            File: item.File.RelativePath,
                            Call: FileWriteCalls.Describe(item.Model, item.Node)
                        )
                    )
                    .Where(hit => hit.Call is not null)
                    .Select(hit => (hit.File, Call: hit.Call!))
            )
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

        string.Join(Environment.NewLine, unexpected)
            .Should()
            .BeEmpty("ファイルへの書き込みは AtomicFile を通し、例外は理由つきで一覧に載せる");
        string.Join(Environment.NewLine, stale).Should().BeEmpty("無くなった出現は一覧から消す");
    }

    [Fact(DisplayName = "走査の土台は、どのプロジェクトでも BCL の呼び出しを解決できている")]
    public void SymbolScan_ResolvesBclCallsInEveryProject()
    {
        SourceSymbolScan
            .SrcProjects.Where(project => project.ResolvedBclInvocationCount == 0)
            .Select(project => project.Name)
            .Should()
            .BeEmpty("参照の組み立てが壊れると、すべての呼び出しが未解決になって走査が空振りする");
        SourceSymbolScan
            .SrcProjects.SelectMany(project =>
                project
                    .Variants.Where(variant => variant.ResolvedBclInvocationCount == 0)
                    .Select(variant => $"{project.Name} [{variant.Symbols}]")
            )
            .Should()
            .BeEmpty(
                "条件コンパイルの変種のコンパイルも、差し替えた構文木の BCL の呼び出しを解決できる"
            );
        SourceSymbolScan.SrcProjects.Should().HaveCountGreaterThan(30);
        output.WriteLine(
            $"projects={SourceSymbolScan.SrcProjects.Count} load={SourceSymbolScan.LoadElapsed.TotalSeconds:F1}s"
        );
    }

    /// <summary>ガードが拾うべき書き方（目的型の new・using static・別名・FileInfo・変数の FileMode・OpenHandle・削除・ラムダの中）</summary>
    private const string ProbeSource = """
        using System;
        using System.IO;
        using QuickER.Platform;
        using static System.IO.File;
        using F = System.IO.File;

        internal static class Probe
        {
            public static void Write(string path, string other, Stream stream)
            {
                FileStream a = new(path, FileMode.Create);
                StreamWriter b = new(path);
                new FileInfo(path).OpenWrite().Dispose();
                new FileInfo(path).CreateText().Dispose();
                WriteAllText(path, "x");
                F.AppendAllText(path, "x");
                var mode = FileMode.Open;
                using var c = new FileStream(path, mode);
                using var d = File.OpenHandle(path, FileMode.Create, FileAccess.Write);
                File.Delete(path);
                Directory.Delete(path, true);
                new FileInfo(path).CopyTo(other);
                AtomicFile.Write(path, s => File.WriteAllText(other, "x"));
            }

            public static void Read(string path, Stream stream)
            {
                using var a = new FileStream(path, FileMode.Open, FileAccess.Read);
                using var b = File.OpenHandle(path);
                using var c = new StreamWriter(stream);
                using var d = File.Open(path, FileMode.Open, FileAccess.Read);
                File.ReadAllText(path);
                AtomicFile.Write(path, s => s.WriteByte(1));
            }
        }
        """;

    [Fact(DisplayName = "走査はシンボルで判定し、構文の別の書き方も同じ API として拾う")]
    public void Describe_FindsEveryWritingForm()
    {
        var (compilation, tree) = SourceSymbolScan.CompileProbe(ProbeSource);
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot(TestContext.Current.CancellationToken);

        string[] Hits(string method) =>
            root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(declaration => declaration.Identifier.ValueText == method)
                .DescendantNodes()
                .Select(node => FileWriteCalls.Describe(model, node))
                .OfType<string>()
                .ToArray();

        Hits("Write")
            .Should()
            .Equal(
                "new FileStream",
                "new StreamWriter",
                "FileInfo.OpenWrite",
                "FileInfo.CreateText",
                "File.WriteAllText",
                "File.AppendAllText",
                "new FileStream",
                "File.OpenHandle",
                "File.Delete",
                "Directory.Delete",
                "FileInfo.CopyTo",
                "File.WriteAllText"
            );
        Hits("Read")
            .Should()
            .BeEmpty("読み取り・ストリームへの書き込みはファイルの書き込みではない");
    }
}

/// <summary>ファイルの書き込み・削除を表す呼び出しの判定（<see cref="RawFileWriteGuardTests"/> が使う）</summary>
internal static class FileWriteCalls
{
    /// <summary>File / FileInfo / Directory / DirectoryInfo の、書き込み・作成・写し・移動・置き換え・削除のメソッド</summary>
    private static readonly Dictionary<string, HashSet<string>> WriteMethods = new(
        StringComparer.Ordinal
    )
    {
        ["System.IO.File"] =
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
            nameof(File.AppendAllBytes),
            nameof(File.AppendAllBytesAsync),
            nameof(File.Create),
            nameof(File.CreateText),
            nameof(File.AppendText),
            nameof(File.OpenWrite),
            nameof(File.Copy),
            nameof(File.Move),
            nameof(File.Replace),
            nameof(File.Delete),
            nameof(File.CreateSymbolicLink),
        ],
        ["System.IO.FileInfo"] =
        [
            nameof(FileInfo.Create),
            nameof(FileInfo.CreateText),
            nameof(FileInfo.AppendText),
            nameof(FileInfo.OpenWrite),
            nameof(FileInfo.CopyTo),
            nameof(FileInfo.MoveTo),
            nameof(FileInfo.Replace),
            nameof(FileInfo.Delete),
        ],
        ["System.IO.Directory"] = [nameof(Directory.Delete), nameof(Directory.Move)],
        ["System.IO.DirectoryInfo"] = [nameof(DirectoryInfo.Delete), nameof(DirectoryInfo.MoveTo)],
    };

    /// <summary>引数の FileMode / FileAccess しだいで書き込みになるメソッド</summary>
    private static readonly HashSet<(string Type, string Method)> ModeDependentMethods =
    [
        ("System.IO.File", nameof(File.Open)),
        ("System.IO.File", nameof(File.OpenHandle)),
        ("System.IO.FileInfo", nameof(FileInfo.Open)),
    ];

    /// <summary>シンボルが解決しなかったときに「未解決」として数える名前</summary>
    private static readonly HashSet<string> TargetNames = new(
        WriteMethods.Values.SelectMany(names => names).Concat(["Open", "OpenHandle", "SaveAs"]),
        StringComparer.Ordinal
    );

    /// <summary>数える書き込みなら、その表記（例 <c>File.WriteAllText</c>・<c>new FileStream</c>・<c>未解決 OpenWrite</c>）を返す</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node) =>
        node switch
        {
            InvocationExpressionSyntax invocation => DescribeInvocation(model, invocation),
            BaseObjectCreationExpressionSyntax creation => DescribeCreation(model, creation),
            _ => null,
        };

    private static string? DescribeInvocation(
        SemanticModel model,
        InvocationExpressionSyntax invocation
    )
    {
        var method = SourceSymbolScan.ResolveMethod(model, invocation);
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };

        if (method is null)
        {
            return name is not null && TargetNames.Contains(name) ? $"未解決 {name}" : null;
        }

        var type = SourceSymbolScan.ContainingTypeName(method);

        if (type is null)
        {
            return null;
        }

        var shortType = type[(type.LastIndexOf('.') + 1)..];

        if (WriteMethods.TryGetValue(type, out var methods) && methods.Contains(method.Name))
        {
            return $"{shortType}.{method.Name}";
        }

        if (ModeDependentMethods.Contains((type, method.Name)))
        {
            return WritesByMode(model, method, invocation.ArgumentList)
                ? $"{shortType}.{method.Name}"
                : null;
        }

        // ClosedXML のブックなど、パスを受ける SaveAs（ストリームを受ける多重定義は数えない）
        return method.Name == "SaveAs" && FirstParameterIsString(method) ? "SaveAs" : null;
    }

    private static string? DescribeCreation(
        SemanticModel model,
        BaseObjectCreationExpressionSyntax creation
    )
    {
        var constructor = SourceSymbolScan.ResolveMethod(model, creation);

        if (constructor is null)
        {
            var written = creation is ObjectCreationExpressionSyntax explicitCreation
                ? explicitCreation.Type.ToString().Split('.')[^1]
                : null;

            return written is nameof(FileStream) or nameof(StreamWriter)
                ? $"未解決 new {written}"
                : null;
        }

        return SourceSymbolScan.ContainingTypeName(constructor) switch
        {
            "System.IO.FileStream" => WritesByMode(model, constructor, creation.ArgumentList)
                ? "new FileStream"
                : null,
            "System.IO.StreamWriter" => FirstParameterIsString(constructor)
                ? "new StreamWriter"
                : null,
            _ => null,
        };
    }

    /// <summary>
    /// FileMode / FileAccess の引数から書き込みかを決める。読み取りと言えるのは、FileAccess が定数の Read で、FileMode が
    /// 定数の Open のとき（省略は既定値で判定する）だけ。変数・FileStreamOptions・ハンドルは書き込みとみなす
    /// </summary>
    private static bool WritesByMode(
        SemanticModel model,
        IMethodSymbol method,
        ArgumentListSyntax? arguments
    )
    {
        if (
            method.Parameters.Any(parameter =>
                parameter.Type.Name is nameof(FileStreamOptions) or "SafeFileHandle"
            )
        )
        {
            return true;
        }

        object? Constant(string parameterName)
        {
            var parameter = method.Parameters.FirstOrDefault(p => p.Name == parameterName);

            if (parameter is null)
            {
                return null;
            }

            var argument = arguments
                ?.Arguments.Select((argument, index) => (argument, index))
                .FirstOrDefault(pair =>
                    pair.argument.NameColon?.Name.Identifier.ValueText == parameterName
                    || (pair.argument.NameColon is null && pair.index == parameter.Ordinal)
                )
                .argument;

            if (argument is null)
            {
                return parameter.HasExplicitDefaultValue
                    ? parameter.ExplicitDefaultValue
                    : "omitted";
            }

            var constant = model.GetConstantValue(argument.Expression);
            return constant.HasValue ? constant.Value : "variable";
        }

        var mode = Constant("mode");
        var access = Constant("access");

        // FileStream(path, mode) と File.Open(path, mode) の FileAccess の省略は ReadWrite
        var readAccess = access is int accessValue && accessValue == (int)FileAccess.Read;
        var openMode = mode is int modeValue && modeValue == (int)FileMode.Open;

        return !(readAccess && openMode);
    }

    private static bool FirstParameterIsString(IMethodSymbol method) =>
        method.Parameters.FirstOrDefault()?.Type.SpecialType == SpecialType.System_String;
}
