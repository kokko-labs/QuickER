using System.Globalization;
using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Platform;
using QuickER.Provider.SqlServer;
using CodeGenStrings = QuickER.CodeGen.CSharp.Resources.Strings;

namespace QuickER.Tests.Platform;

/// <summary>
/// 利用者の設定・AI の出力から受け取った名前を出力先の下で使う入口が、すべて同じ規則（<see cref="PathSegments"/>）で断り、
/// 当たった規則を名指しすることを、断るべき形の総当たりで確かめる
/// </summary>
/// <remarks>
/// <para>
/// 入口は 9 つ：層フォルダ（<see cref="CodeGenerationOptions.DomainLayerDirectory"/>）・生成コードのサブフォルダ・
/// API リファレンスのサブフォルダとファイル名・非分割の出力ファイル名（<see cref="CodeGenerationOptions.OutputFileName"/>）・
/// 生成物のファイル名（<see cref="GeneratedFileWriter"/>）・モックのプロジェクト名・
/// 画面のファイル名（<see cref="MockFolderStore.SaveScreen"/>）・API キー方式の提出のパス（<see cref="MockProjectEmitTools.ResolveEmitPath"/>）。
/// 入口ごとに判定を持つと、断る形が入口ごとに食い違う（ある入口だけ <c>..</c> や 8.3 の別名を通す）。
/// </para>
/// <para>
/// 各入口は、断るべき形のうち、その入口で成り立つもの（拡張子を付けると消える形・区切りが正当な相対パスの区切りは除く）を
/// すべて断り、メッセージに当たった規則の文言（<see cref="PathSegments.Describe(PathSegmentViolation, CultureInfo)"/>）を含める。
/// 正当な名前の表も流して、断りすぎないことも確かめる（250 文字の名前は、書き込む入口では実際に書けることまで確かめる）。
/// </para>
/// <para>
/// 設定の値と提出のパスは、値全体の前後の空白を取り除いてから判定し、提出のパスはさらに空の区間と <c>.</c> の区間を
/// 落としてから判定する（どちらも決まった形への正規化で、意図と違う場所へは書かない）。その入口では、これらの形を
/// 断らずに受け付けることを確かめる（区間の内側の前後の空白は、どの入口でも断る）。
/// </para>
/// </remarks>
public sealed class PathSegmentEntranceFuzzTests : IDisposable
{
    /// <summary>1 区間の名前として断るべき形</summary>
    private static readonly (string Form, PathSegmentViolation Violation)[] SegmentForms =
    [
        (".", PathSegmentViolation.DotSegment),
        ("..", PathSegmentViolation.DotSegment),
        ("a/b", PathSegmentViolation.Separator),
        ("a\\b", PathSegmentViolation.Separator),
        // 2 文字目の ":" はドライブ指定（相対パスの入口では Rooted）になるので、3 文字目に置く
        ("ab:c", PathSegmentViolation.InvalidCharacter),
        ("a*b", PathSegmentViolation.InvalidCharacter),
        ("a?b", PathSegmentViolation.InvalidCharacter),
        ("a<b", PathSegmentViolation.InvalidCharacter),
        ("a>b", PathSegmentViolation.InvalidCharacter),
        ("a|b", PathSegmentViolation.InvalidCharacter),
        ("a\"b", PathSegmentViolation.InvalidCharacter),
        ("a\u0001b", PathSegmentViolation.InvalidCharacter),
        (" a", PathSegmentViolation.LeadingWhitespace),
        ("\u3000a", PathSegmentViolation.LeadingWhitespace),
        ("a.", PathSegmentViolation.TrailingDotOrWhitespace),
        ("a ", PathSegmentViolation.TrailingDotOrWhitespace),
        ("GENERA~1", PathSegmentViolation.Tilde),
        ("a~b", PathSegmentViolation.Tilde),
        ("CON", PathSegmentViolation.ReservedDeviceName),
        ("nul", PathSegmentViolation.ReservedDeviceName),
        ("COM1", PathSegmentViolation.ReservedDeviceName),
        ("LPT9", PathSegmentViolation.ReservedDeviceName),
        ("CONIN$", PathSegmentViolation.ReservedDeviceName),
        ("COM¹", PathSegmentViolation.ReservedDeviceName),
        ("AUX.txt", PathSegmentViolation.ReservedDeviceName),
        (new string('a', PathSegments.MaxLength + 1), PathSegmentViolation.TooLong),
    ];

    /// <summary>相対パスとしてだけ断るべき形（先頭・区間のつながり）</summary>
    private static readonly (string Form, PathSegmentViolation Violation)[] RelativeForms =
    [
        ("/a", PathSegmentViolation.Rooted),
        ("\\a", PathSegmentViolation.Rooted),
        ("C:\\a", PathSegmentViolation.Rooted),
        ("C:a", PathSegmentViolation.Rooted),
        ("a//b", PathSegmentViolation.Empty),
        ("a/./b", PathSegmentViolation.DotSegment),
        ("a/../b", PathSegmentViolation.DotSegment),
        ("a/ b", PathSegmentViolation.LeadingWhitespace),
        ("a /b", PathSegmentViolation.TrailingDotOrWhitespace),
        ("a/CON/b", PathSegmentViolation.ReservedDeviceName),
        ("a/x~1", PathSegmentViolation.Tilde),
    ];

    /// <summary>断らない名前（1 区間）</summary>
    private static readonly string[] AcceptedNames =
    [
        "Domain",
        "MyApp.Domain",
        "a-b_c",
        "日本語",
        "a..b",
        "x.y.z",
        "COM10",
        "CONSOLE",
        // 拡張子を付けても 1 区間の上限（255 文字）に収まる長い名前
        new string('a', 250),
    ];

    /// <summary>テスト用の一時フォルダ</summary>
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "QuickERTests_" + Guid.NewGuid().ToString("N")
    );

    public PathSegmentEntranceFuzzTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 後始末はベストエフォート
        }
    }

    /// <summary>入口（名前・入力の組み立て・判定）。判定は「断ったときのメッセージ」を返し、受け付けたら null</summary>
    /// <param name="TrimsWholeValue">値全体の前後の空白を取り除いてから判定する入口（設定の値と提出のパス）</param>
    /// <param name="DropsDotAndEmptySegments">空の区間と <c>.</c> の区間を落としてから判定する入口（提出のパス）</param>
    private sealed record Entrance(
        string Name,
        bool Relative,
        Func<PathSegmentViolation, string, bool> Applies,
        Func<string, string> Build,
        Func<string, string?> Reject,
        CultureInfo MessageCulture,
        bool TrimsWholeValue = false,
        bool DropsDotAndEmptySegments = false
    );

    /// <summary>入口がこの形を受け付けるはずか（値全体の前後の空白・落とす区間）</summary>
    private static bool IsAcceptedByPreprocessing(
        Entrance entrance,
        string input,
        string form,
        PathSegmentViolation violation
    ) =>
        (
            entrance.TrimsWholeValue
            && input.Length > 0
            && (char.IsWhiteSpace(input[0]) || char.IsWhiteSpace(input[^1]))
        )
        || (
            entrance.DropsDotAndEmptySegments
            && (
                violation == PathSegmentViolation.Empty
                || (violation == PathSegmentViolation.DotSegment && !form.Contains(".."))
            )
        );

    /// <summary>9 つの入口</summary>
    private IEnumerable<Entrance> Entrances()
    {
        var ui = CultureInfo.CurrentUICulture;
        var english = CultureInfo.InvariantCulture;

        yield return new Entrance(
            "DomainLayerDirectory",
            Relative: true,
            Applies: (v, _) => v != PathSegmentViolation.Separator,
            Build: form => form,
            Reject: value =>
                GenerationError(
                    new CodeGenerationOptions
                    {
                        LayeredOutput = true,
                        DomainLayerDirectory = value,
                    },
                    SubdirectoryMessagePrefix(
                        nameof(CodeGenerationOptions.DomainLayerDirectory),
                        value
                    )
                ),
            ui,
            TrimsWholeValue: true
        );
        yield return new Entrance(
            "CodeSubdirectory",
            Relative: true,
            Applies: (v, _) => v != PathSegmentViolation.Separator,
            Build: form => form,
            Reject: value =>
                GenerationError(
                    new CodeGenerationOptions { CodeSubdirectory = value },
                    SubdirectoryMessagePrefix(nameof(CodeGenerationOptions.CodeSubdirectory), value)
                ),
            ui,
            TrimsWholeValue: true
        );
        yield return new Entrance(
            "ApiDocsSubdirectory",
            Relative: true,
            Applies: (v, _) => v != PathSegmentViolation.Separator,
            Build: form => form,
            Reject: value =>
                GenerationError(
                    new CodeGenerationOptions
                    {
                        GenerateApiDocs = true,
                        ApiDocsSubdirectory = value,
                    },
                    SubdirectoryMessagePrefix(
                        nameof(CodeGenerationOptions.ApiDocsSubdirectory),
                        value
                    )
                ),
            ui,
            TrimsWholeValue: true
        );
        yield return new Entrance(
            "ApiDocsFileName",
            Relative: false,
            Applies: (_, _) => true,
            Build: form => form,
            Reject: value =>
                GenerationError(
                    new CodeGenerationOptions { GenerateApiDocs = true, ApiDocsFileName = value },
                    MessagePrefix(CodeGenStrings.CodeGen_Error_InvalidApiDocsFileName, value.Trim())
                ),
            ui,
            TrimsWholeValue: true
        );
        yield return new Entrance(
            "OutputFileName",
            Relative: false,
            Applies: (_, _) => true,
            Build: form => form,
            Reject: value =>
                GenerationError(
                    new CodeGenerationOptions { OutputFileName = value },
                    MessagePrefix(CodeGenStrings.CodeGen_Error_InvalidOutputFileName, value.Trim())
                ),
            ui,
            TrimsWholeValue: true
        );
        // 生成物のファイル名は「.g.cs」が付き、ディレクトリ要素（区切り・ドライブ）は Path.GetFileName が先に落とす
        yield return new Entrance(
            "GeneratedFileName",
            Relative: false,
            Applies: (v, form) =>
                v
                    is not (
                        PathSegmentViolation.DotSegment
                        or PathSegmentViolation.Separator
                        or PathSegmentViolation.TrailingDotOrWhitespace
                    )
                && !form.Contains(':'),
            Build: form => form + ".g.cs",
            Reject: WriteGeneratedFile,
            ui
        );
        yield return new Entrance(
            "ProjectName",
            Relative: false,
            Applies: (_, _) => true,
            Build: form => form,
            Reject: value => MockProjectScaffoldService.CheckProjectName(value, ui),
            ui
        );
        // 画面のファイル名は「.html」が付くので、末尾の形と '.'・'..' は成り立たない
        yield return new Entrance(
            "ScreenFileName",
            Relative: false,
            Applies: (v, _) =>
                v
                    is not (
                        PathSegmentViolation.DotSegment
                        or PathSegmentViolation.TrailingDotOrWhitespace
                    ),
            Build: form => form + ".html",
            Reject: SaveScreen,
            english
        );
        yield return new Entrance(
            "EmitPath",
            Relative: true,
            Applies: (v, _) => v != PathSegmentViolation.Separator,
            Build: form => "MockApp/" + form + "/OrderList.xaml",
            Reject: value =>
                MockProjectEmitTools.ResolveEmitPath(_folder, MockProjectTargetProfile.Wpf, value)
                    is { Ok: false } result
                    ? result.Error
                    : null,
            english,
            TrimsWholeValue: true,
            DropsDotAndEmptySegments: true
        );
    }

    [Fact(
        DisplayName = "パスの名前を受け取る 9 つの入口は、断るべき形をすべて断り、当たった規則を名指しする"
    )]
    public void Entrances_RejectEveryHostileForm_NamingTheRule()
    {
        var failures = new List<string>();

        foreach (var entrance in Entrances())
        {
            var cases = SegmentForms
                .Where(item => entrance.Applies(item.Violation, item.Form))
                .Select(item => (Input: entrance.Build(item.Form), item.Form, item.Violation));

            if (entrance.Relative)
            {
                // 相対パスの入口は、先頭・区間のつながりの形をそのまま（提出のパスは末尾にファイル名を付けて）流す
                cases = cases.Concat(
                    RelativeForms.Select(item =>
                        (
                            Input: entrance.Name == "EmitPath"
                                ? item.Form + "/OrderList.xaml"
                                : item.Form,
                            item.Form,
                            item.Violation
                        )
                    )
                );
            }

            foreach (var (input, form, violation) in cases)
            {
                var message = entrance.Reject(input);

                // 値全体の前後の空白・落とす区間は、断らずに正規化して受け付ける（断るのは区間の内側の形だけ）
                if (IsAcceptedByPreprocessing(entrance, input, form, violation))
                {
                    if (message is not null)
                    {
                        failures.Add(
                            $"{entrance.Name}: '{input}' を断った（前処理で受け付けるはず）: {message}"
                        );
                    }

                    continue;
                }

                var reason = PathSegments.Describe(violation, entrance.MessageCulture);

                if (message is null)
                {
                    failures.Add(
                        $"{entrance.Name}: '{input}' を受け付けた（{violation} で断るはず）"
                    );
                }
                else if (!message.Contains(reason, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{entrance.Name}: '{input}' の理由が {violation} でない: {message}"
                    );
                }
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "パスの名前を受け取る 9 つの入口は、正当な名前を断らない（長い名前も書ける）"
    )]
    public void Entrances_AcceptOrdinaryNames()
    {
        var failures = new List<string>();

        foreach (var entrance in Entrances())
        {
            var inputs = AcceptedNames.Select(entrance.Build).ToList();

            if (entrance.Relative)
            {
                inputs.Add(entrance.Build("MyApp.Domain/Views"));
            }

            if (entrance.TrimsWholeValue)
            {
                inputs.Add(" " + entrance.Build("Domain") + "　");
            }

            if (entrance.DropsDotAndEmptySegments)
            {
                inputs.Add("./" + entrance.Build("Domain"));
                inputs.Add(entrance.Build("Domain").Replace("/", "//", StringComparison.Ordinal));
            }

            foreach (var input in inputs)
            {
                if (entrance.Reject(input) is { } message)
                {
                    failures.Add($"{entrance.Name}: '{input}' を断った: {message}");
                }
            }
        }

        failures.Should().BeEmpty();
    }

    [Theory(
        DisplayName = "プロジェクト名の入口は、生成するプロジェクトが自分用に使うフォルダの名前を大文字小文字を問わず断る"
    )]
    [InlineData("Generated")]
    [InlineData("generated")]
    [InlineData("design")]
    [InlineData("Design")]
    [InlineData("obj")]
    [InlineData("Obj")]
    [InlineData("bin")]
    [InlineData("BIN")]
    public void ProjectName_RejectsProtectedFolderNames(string name)
    {
        var message = MockProjectScaffoldService.CheckProjectName(
            name,
            CultureInfo.CurrentUICulture
        );

        message.Should().NotBeNull();
        message.Should().Contain(name);
        MockProjectEmitTools
            .ProtectedFolderNames.Should()
            .Contain(folder => string.Equals(folder, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 出力サブフォルダのエラーの、理由の手前までの文（オプション名と値まで。エラーは前後の空白を取り除いた値を出す）
    /// </summary>
    private static string SubdirectoryMessagePrefix(string optionName, string value) =>
        MessagePrefix(
            CodeGenStrings.CodeGen_Error_InvalidOutputSubdirectory,
            optionName,
            value.Trim()
        );

    /// <summary>
    /// 書式の最後の引数（理由）の手前までを返す。同じオプションの別の検証（層フォルダの名前空間など）のエラーと取り違えないため、
    /// 入口の判定のエラーかどうかをこの接頭辞で見分ける
    /// </summary>
    private static string MessagePrefix(string format, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, [.. arguments, "\u0000"]).Split('\u0000')[
            0
        ];

    /// <summary>生成の診断から、接頭辞で始まるエラーのメッセージを返す（無ければ null）</summary>
    private static string? GenerationError(CodeGenerationOptions options, string prefix)
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            options
        );

        return result
            .Diagnostics.Where(d => d.Severity == GenerationDiagnosticSeverity.Error)
            .Select(d => d.Message)
            .FirstOrDefault(message => message.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>生成物 1 つを書き出し、断ったら例外のメッセージを返す</summary>
    private string? WriteGeneratedFile(string fileName)
    {
        var output = Path.Combine(_folder, "out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var result = new CodeGenerationResult
        {
            Files = [new GeneratedFile { FileName = fileName, Content = "// x" }],
        };

        try
        {
            new GeneratedFileWriter().WriteFiles(output, result);

            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>画面を 1 つ保存し、断ったら例外のメッセージを返す</summary>
    private string? SaveScreen(string file)
    {
        var folder = Path.Combine(_folder, "mock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var store = MockFolderStore.CreateNew(folder, "title", "schema");

        try
        {
            store.SaveScreen(
                file,
                "Screen",
                "description",
                "<html><body>x</body></html>",
                [],
                "note"
            );

            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
