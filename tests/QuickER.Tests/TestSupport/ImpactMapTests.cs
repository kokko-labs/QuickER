using System.IO;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 影響の地図の照合（<see cref="ImpactMapGuard"/>）と単位の列挙（<see cref="ImpactUnits"/>）の自己検査：架空の守り・宣言で、
/// 問題の形ごとに意図どおり赤になり、整った宣言が緑になることを行の表で固定する
/// </summary>
public sealed class ImpactMapTests
{
    private const string Model = "src/QuickER.Model";
    private const string Gui = "src/QuickER.Gui";
    private const string Behaviors = "src/QuickER.Gui/Behaviors";
    private const string Services = "src/QuickER.Gui/Services";
    private const string Reason = "自己検査のための十分に具体的な理由";

    private static readonly string Root = EngineRepository.Root();

    private static readonly string[] Units = [Model, Gui, Behaviors, Services];

    private static Protection P(string name, params string[] covers) =>
        new(name, ProtectionKind.Engine, "表示の名前", [], covers, "");

    private static UnprotectedArea A(
        string unit,
        UnprotectedKind kind = UnprotectedKind.Pending,
        string reason = Reason
    ) => new(unit, kind, reason);

    /// <summary>整った守り（Model は全体・Gui は Services だけで一部・Behaviors は守りの無い領域）</summary>
    private static Protection[] Valid => [P("E/Model", Model), P("E/Services", Services)];

    /// <summary>自己検査の行（行の Id → 地図を作る関数〔一時フォルダの根を受ける〕・照合に使う根・期待する問題の断片。空なら問題なし）</summary>
    private static readonly Dictionary<
        string,
        (Func<string, (ImpactMap Map, string Root)> Build, string Expected)
    > Cases = new(StringComparer.Ordinal)
    {
        ["valid"] = (_ => (new(Units, Valid, [A(Behaviors)]), Root), ""),
        ["unit-without-declaration-is-red"] = (
            _ => (new(Units, Valid, []), Root),
            $"{Behaviors}: どの守りの範囲にも入らず、守りの無い領域にも無い"
        ),
        ["unit-both-protected-and-unprotected-is-red"] = (
            _ => (new(Units, Valid, [A(Behaviors), A(Model)]), Root),
            $"{Model}: 守り（E/Model）があるのに、守りの無い領域にも載っている"
        ),
        ["project-unprotected-while-folder-protected-is-red"] = (
            _ => (new(Units, [P("E/Model", Model), P("E/Services", Services)], [A(Gui)]), Root),
            $"{Gui}: 守り（E/Services）があるのに、守りの無い領域にも載っている"
        ),
        ["missing-path-is-red"] = (
            _ => (new(Units, Valid, [A(Behaviors), A("src/QuickER.NoSuchProject")]), Root),
            "src/QuickER.NoSuchProject: 守りの無い領域のパスが存在しない"
        ),
        ["path-that-is-not-a-unit-is-red"] = (
            _ =>
                (
                    new(Units, Valid, [A(Behaviors), A("src/QuickER.Model/ConstraintNames.cs")]),
                    Root
                ),
            "src/QuickER.Model/ConstraintNames.cs: 守りの無い領域が単位（プロジェクトか、その直下のフォルダ）でない"
        ),
        ["duplicate-area-is-red"] = (
            _ => (new(Units, Valid, [A(Behaviors), A(Behaviors)]), Root),
            $"{Behaviors}: 守りの無い領域が重複している"
        ),
        ["folder-under-unprotected-project-is-red"] = (
            _ => (new(Units, [P("E/Model", Model)], [A(Gui), A(Behaviors)]), Root),
            $"{Behaviors}: プロジェクト {Gui} ごと守りの無い領域に載っているのに、その下のフォルダを重ねて書いている"
        ),
        ["empty-reason-is-red"] = (
            _ => (new(Units, Valid, [A(Behaviors, reason: " ")]), Root),
            $"{Behaviors}: 守りの無い領域の理由が空"
        ),
        ["placeholder-reason-is-red"] = (
            _ => (new(Units, Valid, [A(Behaviors, reason: "TODO")]), Root),
            $"{Behaviors}: 守りの無い領域の理由「TODO」が具体的でない"
        ),
        ["unknown-kind-is-red"] = (
            _ => (new(Units, Valid, [A(Behaviors, (UnprotectedKind)9)]), Root),
            $"{Behaviors}: 守りの無い領域の分類 9 が Pending / Accepted でない"
        ),
        ["duplicate-protection-name-is-red"] = (
            _ => (new(Units, [.. Valid, P("E/Model", Model)], [A(Behaviors)]), Root),
            "E/Model: 守りの名前が重複している"
        ),
        ["missing-cover-is-red"] = (
            _ =>
                (
                    new(
                        Units,
                        [.. Valid, P("E/Missing", "src/QuickER.NoSuchProject")],
                        [A(Behaviors)]
                    ),
                    Root
                ),
            "E/Missing: 範囲 src/QuickER.NoSuchProject が存在しない"
        ),
        ["cover-outside-projects-is-red"] = (
            _ =>
                (
                    new(Units, [.. Valid, P("E/Outside", "tests/QuickER.Tests")], [A(Behaviors)]),
                    Root
                ),
            "E/Outside: 範囲 tests/QuickER.Tests が src のプロジェクトの中を指さない"
        ),
        ["new-project-without-declaration-is-red"] = (
            temp =>
            {
                MakeRepository(temp);
                return (new(ImpactUnits.Enumerate(temp), [P("E/A", "src/A")], []), temp);
            },
            "src/B: どの守りの範囲にも入らず、守りの無い領域にも無い"
        ),
    };

    /// <summary>自己検査の行の Id</summary>
    public static TheoryData<string> Rows => [.. Cases.Keys];

    [Theory(
        DisplayName = "[地図] 照合は問題の形ごとに名指しして赤になり、整った宣言には問題が無い"
    )]
    [MemberData(nameof(Rows))]
    public void Guard_NamesEachProblem(string row)
    {
        using var temp = new TestTempFolder();
        var (build, expected) = Cases[row];
        var (map, root) = build(temp.Path);
        var problems = ImpactMapGuard.Problems(map, root);

        if (expected.Length == 0)
        {
            problems.Should().BeEmpty();
            return;
        }

        problems.Should().ContainSingle().Which.Should().StartWith(expected);
        FluentActions
            .Invoking(() => ImpactMapGuard.Verify(map, root))
            .Should()
            .Throw<XunitException>()
            .WithMessage("*" + expected + "*");
    }

    /// <summary>決まった形の行の表の型を使うかの判定の行（行の Id → ソース・使うと数えるか）</summary>
    private static readonly Dictionary<string, (string Source, bool Uses)> TableUsageCases = new(
        StringComparer.Ordinal
    )
    {
        ["type-in-member-is-counted"] = (
            "class C { static ScenarioTable<int> Table => null; }",
            true
        ),
        ["type-in-inactive-branch-is-counted"] = (
            "class C {\n#if QUICKER_NEVER\n    static ScenarioTable<int> Table => null;\n#endif\n}",
            true
        ),
        ["only-in-comments-is-not-counted"] = (
            "// ScenarioTable<Row> を使う\n/// <see cref=\"ScenarioTable{T}\"/>\n/* ScenarioTable<Row> */\nclass C { }",
            false
        ),
        ["only-in-string-is-not-counted"] = (
            "class C { const string S = \"ScenarioTable<Row>\"; }",
            false
        ),
        ["non-generic-name-is-not-counted"] = ("class C { int ScenarioTable; }", false),
    };

    /// <summary>判定の行の Id</summary>
    public static TheoryData<string> TableUsageRows => [.. TableUsageCases.Keys];

    [Theory(
        DisplayName = "[地図] 決まった形の行の表の型を使うかは、総称の型の名前の節で数え、コメント・文字列の中の綴りは数えない（条件コンパイルの外れる分岐も読む）"
    )]
    [MemberData(nameof(TableUsageRows))]
    public void TableUsage_IsReadFromSyntax(string row)
    {
        var (source, uses) = TableUsageCases[row];

        ImpactMapSources.UsesScenarioTable(source).Should().Be(uses);
    }

    [Fact(
        DisplayName = "[地図] 単位はプロジェクトと、手で書いたソースを持つ直下のフォルダ（生成物・XAML だけのフォルダと bin・obj を除く）"
    )]
    public void Units_AreProjectsAndHandwrittenFolders()
    {
        using var temp = new TestTempFolder();
        MakeRepository(temp.Path);

        ImpactUnits
            .Enumerate(temp.Path)
            .Should()
            .Equal("src/A", "src/A/Sub", "src/A/Templates", "src/B");
    }

    /// <summary>
    /// 架空のリポジトリ：プロジェクト A（直下のフォルダ Sub・Templates〔.scriban〕・Resources〔Designer.cs だけ〕・Themes〔XAML だけ〕・
    /// obj）とプロジェクト B、csproj の無いフォルダ C
    /// </summary>
    private static void MakeRepository(string root)
    {
        Write(root, "src/A/A.csproj");
        Write(root, "src/A/a.cs");
        Write(root, "src/A/Sub/x.cs");
        Write(root, "src/A/Templates/t.scriban");
        Write(root, "src/A/Resources/Strings.Designer.cs");
        Write(root, "src/A/Themes/Theme.xaml");
        Write(root, "src/A/obj/o.cs");
        Write(root, "src/B/B.csproj");
        Write(root, "src/B/b.g.cs");
        Write(root, "src/C/c.cs");
    }

    private static void Write(string root, string path)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "");
    }
}
