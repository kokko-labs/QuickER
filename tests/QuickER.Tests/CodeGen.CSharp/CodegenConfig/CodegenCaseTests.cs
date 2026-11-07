using AwesomeAssertions;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行（<see cref="CodegenCases"/>）をいくつかのクラスへ分ける（xUnit は同じクラスの Theory の行を並列に流さないため。
/// <see cref="CodegenMatrixParts"/> と同じやり方）
/// </summary>
/// <remarks>
/// 行は表の並びの位置を分ける数で割った余りで振り分ける。1 つ目の分は <see cref="CodegenCaseTests"/>（CLAUDE.md が「<c>CodegenCaseTests</c> の行 …」と
/// 名指す入口の名前）で、2 つ目以降は <c>CodegenCaseTestsPart{n}</c>。分ける数とクラスの数が食い違うと余りの組の行が黙って流れなくなるので、
/// <see cref="CodegenCaseTests.Parts_CoverEveryRowOnce"/> が各クラスの MemberData の和が表の行とちょうど一致することを確かめる。
/// </remarks>
internal static class CodegenCaseParts
{
    /// <summary>分ける数</summary>
    public const int Parts = 8;

    /// <summary>各クラスの Theory の表示名</summary>
    public const string DisplayName =
        "[生成の構成/決まった行] 元のテストの図・オプションで、元と同じ強さの期待どおり";

    /// <summary>表の行のうち <paramref name="part"/> 番目のクラスが受け持つものの Id</summary>
    /// <param name="part">クラスの番号（0 から）</param>
    public static TheoryData<string> Ids(int part)
    {
        var data = new TheoryData<string>();

        foreach (var row in CodegenCases.Table.Where((_, index) => index % Parts == part))
        {
            data.Add(row.Id);
        }

        return data;
    }
}

/// <summary>生成の構成のエンジンの決まった行（<see cref="CodegenCases"/>）の 1 つ目の分と、分け方のガード</summary>
public sealed class CodegenCaseTests
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> Ids() => CodegenCaseParts.Ids(0);

    [Theory(DisplayName = CodegenCaseParts.DisplayName)]
    [MemberData(nameof(Ids))]
    public Task Case_MatchesExpectation(string id) => CodegenCases.RunAsync(id);

    [Fact(
        DisplayName = "[生成の構成/決まった行] 決まった行を分けたクラスの和は、表の行とちょうど一致する（重ならず欠けない）"
    )]
    public void Parts_CoverEveryRowOnce()
    {
        Type[] classes =
        [
            typeof(CodegenCaseTests),
            typeof(CodegenCaseTestsPart2),
            typeof(CodegenCaseTestsPart3),
            typeof(CodegenCaseTestsPart4),
            typeof(CodegenCaseTestsPart5),
            typeof(CodegenCaseTestsPart6),
            typeof(CodegenCaseTestsPart7),
            typeof(CodegenCaseTestsPart8),
        ];

        classes.Should().HaveCount(CodegenCaseParts.Parts, "分ける数とクラスの数をそろえる");

        var ids = classes
            .SelectMany(type =>
                ((TheoryData<string>)type.GetMethod(nameof(Ids))!.Invoke(null, null)!).Select(row =>
                    row.Data
                )
            )
            .ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().BeEquivalentTo(CodegenCases.Table.Select(row => row.Id));
    }

    /// <summary>
    /// 表の不変条件：主キーの列に NULL 許容の印がある図を持つのは、関係 <see cref="CodegenRelation.PrimaryKeyNotNull"/> を掛けた行だけ
    /// </summary>
    /// <remarks>
    /// 理由：<c>Column.IsNullable</c> の既定は true なので、図を組むときに主キーの列へ書き忘れると、NULL 許容の主キーを
    /// 意図しない行が生成の入口の読み替え（主キーを NOT NULL とみなす）の経路を黙って通る（修正 L で 6 行が紛れていた）。
    /// 読み替えを見る行は関係 PrimaryKeyNotNull で主キーを NOT NULL にした図との一致まで確かめるので、印のある図はその行に限る。
    /// </remarks>
    [Fact(
        DisplayName = "[生成の構成/決まった行] NULL 許容の印のある主キーの図は、関係 PrimaryKeyNotNull を掛けた行だけが持つ"
    )]
    public void NullablePrimaryKeyDiagrams_AreOnlyInPrimaryKeyNotNullRows()
    {
        var mismatched = CodegenCases
            .Table.Where(row =>
                row.Input.Diagram()
                    .Entities.SelectMany(entity => entity.Columns)
                    .Any(column => column is { IsPrimaryKey: true, IsNullable: true })
                != (row.Input.Relations?.Contains(CodegenRelation.PrimaryKeyNotNull) ?? false)
            )
            .Select(row => row.Id)
            .ToList();

        mismatched
            .Should()
            .BeEmpty(
                "NULL 許容の主キーを意図しない行は主キーの列に IsNullable = false を書き、意図する行は関係 PrimaryKeyNotNull を掛ける"
            );
    }
}

/// <summary>決まった行の 2 つ目の分（<see cref="CodegenCaseParts"/>）</summary>
public sealed class CodegenCaseTestsPart2
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> Ids() => CodegenCaseParts.Ids(1);

    [Theory(DisplayName = CodegenCaseParts.DisplayName)]
    [MemberData(nameof(Ids))]
    public Task Case_MatchesExpectation(string id) => CodegenCases.RunAsync(id);
}

/// <summary>決まった行の 3 つ目の分（<see cref="CodegenCaseParts"/>）</summary>
public sealed class CodegenCaseTestsPart3
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> Ids() => CodegenCaseParts.Ids(2);

    [Theory(DisplayName = CodegenCaseParts.DisplayName)]
    [MemberData(nameof(Ids))]
    public Task Case_MatchesExpectation(string id) => CodegenCases.RunAsync(id);
}

/// <summary>決まった行の 4 つ目の分（<see cref="CodegenCaseParts"/>）</summary>
public sealed class CodegenCaseTestsPart4
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> Ids() => CodegenCaseParts.Ids(3);

    [Theory(DisplayName = CodegenCaseParts.DisplayName)]
    [MemberData(nameof(Ids))]
    public Task Case_MatchesExpectation(string id) => CodegenCases.RunAsync(id);
}

/// <summary>決まった行の 5 つ目の分（<see cref="CodegenCaseParts"/>）</summary>
public sealed class CodegenCaseTestsPart5
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> Ids() => CodegenCaseParts.Ids(4);

    [Theory(DisplayName = CodegenCaseParts.DisplayName)]
    [MemberData(nameof(Ids))]
    public Task Case_MatchesExpectation(string id) => CodegenCases.RunAsync(id);
}

/// <summary>決まった行の 6 つ目の分（<see cref="CodegenCaseParts"/>）</summary>
public sealed class CodegenCaseTestsPart6
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> Ids() => CodegenCaseParts.Ids(5);

    [Theory(DisplayName = CodegenCaseParts.DisplayName)]
    [MemberData(nameof(Ids))]
    public Task Case_MatchesExpectation(string id) => CodegenCases.RunAsync(id);
}

/// <summary>決まった行の 7 つ目の分（<see cref="CodegenCaseParts"/>）</summary>
public sealed class CodegenCaseTestsPart7
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> Ids() => CodegenCaseParts.Ids(6);

    [Theory(DisplayName = CodegenCaseParts.DisplayName)]
    [MemberData(nameof(Ids))]
    public Task Case_MatchesExpectation(string id) => CodegenCases.RunAsync(id);
}

/// <summary>決まった行の 8 つ目の分（<see cref="CodegenCaseParts"/>）</summary>
public sealed class CodegenCaseTestsPart8
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> Ids() => CodegenCaseParts.Ids(7);

    [Theory(DisplayName = CodegenCaseParts.DisplayName)]
    [MemberData(nameof(Ids))]
    public Task Case_MatchesExpectation(string id) => CodegenCases.RunAsync(id);
}
