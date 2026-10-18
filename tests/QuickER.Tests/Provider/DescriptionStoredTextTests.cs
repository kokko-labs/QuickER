using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 説明の BMP の外の文字（絵文字など）を保存できない方言（MySQL）で、差分が保存される形で比べ、同期の計画が警告し、
/// DDL が注記することを、方言の宣言（<see cref="IdentifierLengthLimit.RepresentsSupplementaryCharacters"/>）と照合する
/// </summary>
/// <remarks>
/// 判定は同期のケーパビリティが指す識別子の上限（<see cref="SyncDialectCapabilities.IdentifierLengthLimit"/>）と、
/// DDL の生成器の方言の上限の宣言だけで行い、方言名で分岐しない。実 DB の往復は
/// <c>DescriptionSupplementaryCharacterIntegrationTests</c>。
/// </remarks>
public class DescriptionStoredTextTests
{
    /// <summary>絵文字（サロゲート対）を 2 つ含む説明</summary>
    private const string Emoji = "a😀b😀c";

    /// <summary>MySQL が保存する形</summary>
    private const string EmojiStored = "a?b?c";

    /// <summary>5 方言のプロバイダ</summary>
    public static TheoryData<string> Dialects() =>
        new() { "sqlserver", "postgresql", "mysql", "oracle", "sqlite" };

    /// <summary>保存する形の規則（MySQL の実測）: サロゲート対は 1 つの ?、対になっていないサロゲートは U+FFFD</summary>
    [Theory(DisplayName = "説明の BMP の外の文字: 保存できない方言の保存する形の規則")]
    [InlineData("abc", "abc")]
    [InlineData(Emoji, EmojiStored)]
    [InlineData("😀😀", "??")]
    [InlineData("?", "?")]
    public void StoredText_FollowsMySqlRule(string text, string expected)
    {
        IdentifierLengthLimits.MySql.StoredText(text).Should().Be(expected);
        IdentifierLengthLimits.SqlServer.StoredText(text).Should().Be(text);
        IdentifierLengthLimit.Unlimited.StoredText(text).Should().Be(text);
    }

    /// <summary>
    /// 対になっていないサロゲートは U+FFFD になる（xUnit の引数は UTF-8 を経て対になっていないサロゲートを壊すので Fact で書く）
    /// </summary>
    [Fact(DisplayName = "説明の BMP の外の文字: 対になっていないサロゲートは U+FFFD になる")]
    public void StoredText_LoneSurrogate_BecomesReplacementCharacter()
    {
        var limit = IdentifierLengthLimits.MySql;

        limit.StoredText("x\uD800y").Should().Be("x�y");
        limit.StoredText("x\uDC00").Should().Be("x�");
        limit.StoredText("\uDE00\uD83D").Should().Be("��");
        IdentifierLengthLimits.PostgreSql.StoredText("x\uD800y").Should().Be("x\uD800y");
    }

    /// <summary>
    /// 差分は、BMP の外の文字を保存できない方言でだけ、図の説明を保存する形にして DB の説明と比べることを検証する
    /// （DB の <c>?</c> と図の絵文字は差分にならない）。保存できる方言では差分になる。
    /// </summary>
    [Theory(DisplayName = "説明の BMP の外の文字: 差分は保存できない方言でだけ保存する形で比べる")]
    [MemberData(nameof(Dialects))]
    public void Diff_ComparesStoredForm_OnlyWhereDeclared(string dialect)
    {
        var capabilities = ProviderFor(dialect).SyncCapabilities;
        var folds = !capabilities.IdentifierLengthLimit.RepresentsSupplementaryCharacters;

        var items = DescriptionItems(Diff(Table(EmojiStored), Table(Emoji), capabilities));

        if (!capabilities.SupportsDescriptions)
        {
            items.Should().BeEmpty();
        }
        else if (folds)
        {
            items.Should().BeEmpty();
        }
        else
        {
            items.Should().HaveCount(2);
        }

        DescriptionItems(Diff(Table("a?b"), Table("a😀c"), capabilities))
            .Should()
            .HaveCount(
                capabilities.SupportsDescriptions ? 2 : 0,
                "保存する形でも違う説明は差分になる"
            );
    }

    /// <summary>
    /// 同期の計画は、保存できない方言で、BMP の外の文字を書く説明の項目を表・列ごとに名指しすることを検証する
    /// （BMP の中だけの説明では名指ししない）。
    /// </summary>
    [Theory(
        DisplayName = "説明の BMP の外の文字: 同期の計画は保存できない方言でだけ説明の項目を名指しする"
    )]
    [MemberData(nameof(Dialects))]
    public void Plan_WarnsDescriptionCharactersReplaced_OnlyWhereDeclared(string dialect)
    {
        var capabilities = ProviderFor(dialect).SyncCapabilities;
        var folds = !capabilities.IdentifierLengthLimit.RepresentsSupplementaryCharacters;

        Warnings(Emoji, capabilities).Should().Equal(folds ? ["t", "t.v"] : Array.Empty<string>());
        Warnings("note", capabilities).Should().BeEmpty("BMP の中だけの説明は名指ししない");
        Warnings("x\uD800y", capabilities)
            .Should()
            .Equal(
                folds ? ["t", "t.v"] : Array.Empty<string>(),
                "対になっていないサロゲートも置き換わる"
            );
    }

    /// <summary>
    /// DDL は、保存できない方言で、BMP の外の文字を含む表・列の説明の前に注記を出すことを検証する
    /// （BMP の中だけの説明では出さない）。
    /// </summary>
    [Theory(DisplayName = "説明の BMP の外の文字: DDL は保存できない方言でだけ説明の前に注記する")]
    [MemberData(nameof(Dialects))]
    public void Ddl_NotesReplacedCharacters_OnlyWhereDeclared(string dialect)
    {
        var provider = ProviderFor(dialect);
        var folds = !IdentifierLengthLimits
            .ForDbms(provider.Name)
            .RepresentsSupplementaryCharacters;

        var notes = Notes(Ddl(provider, Table(Emoji)));

        notes.Should().HaveCount(folds ? 2 : 0);

        if (folds)
        {
            notes[0].Should().Contain("table t");
            notes[1].Should().Contain("column t.v");
        }

        Notes(Ddl(provider, Table("note"))).Should().BeEmpty("BMP の中だけの説明は注記しない");
    }

    /// <summary>
    /// 注記は MySQL の CREATE TABLE で、表の注記が文の前・列の注記がその列の定義行の直前に来ることを検証する
    /// </summary>
    [Fact(DisplayName = "説明の BMP の外の文字: MySQL の注記は表の文の前と列の定義行の直前に来る")]
    public void Ddl_MySqlNotes_PrecedeTableAndColumn()
    {
        var lines = Ddl(new MySqlProvider(), Table(Emoji))
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .ToList();
        var tableNote = lines.FindIndex(l => l.StartsWith("-- Note:", StringComparison.Ordinal));
        var columnNote = lines.FindLastIndex(l =>
            l.Contains("column t.v", StringComparison.Ordinal)
        );

        lines[tableNote + 1].Should().StartWith("CREATE TABLE");
        lines[columnNote + 1].TrimStart().Should().StartWith("`v`");
    }

    private static Entity Table(string description) =>
        new()
        {
            TableName = "t",
            Description = description,
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "v",
                    DataType = "int",
                    IsNullable = true,
                    Description = description,
                },
            },
        };

    private static SchemaDiff Diff(
        Entity live,
        Entity target,
        SyncDialectCapabilities capabilities
    ) => new SchemaDiffService().Compute([live], [], [target], [], capabilities);

    private static List<SchemaDiffItem> DescriptionItems(SchemaDiff diff) =>
        [
            .. diff.Items.Where(i =>
                i.Kind is SchemaDiffKind.SetTableDescription or SchemaDiffKind.SetColumnDescription
            ),
        ];

    /// <summary>説明のない DB から、説明 <paramref name="description"/> の図へ同期する計画の警告の Detail</summary>
    private static string[] Warnings(string description, SyncDialectCapabilities capabilities)
    {
        var diff = Diff(Table(string.Empty), Table(description), capabilities);

        foreach (var item in diff.Items)
        {
            item.IsSelected = item.IsSelectable;
        }

        return
        [
            .. new SyncPlanner()
                .BuildPlan(diff.Items, capabilities, Context(Table(string.Empty)))
                .Warnings.Where(w => w.Kind == SyncPlanWarningKind.DescriptionCharactersReplaced)
                .Select(w => w.Detail),
        ];
    }

    private static SyncPlanContext Context(Entity live) =>
        new() { LiveEntities = [live], LiveRelationships = [] };

    private static string Ddl(IDatabaseProvider provider, Entity table)
    {
        var diagram = new ErDiagram { TargetDbms = provider.Name };
        diagram.Entities.Add(table);
        return provider.DdlGenerator.Build(diagram);
    }

    private static List<string> Notes(string ddl) =>
        [
            .. ddl.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Contains("Basic Multilingual Plane", StringComparison.Ordinal)),
        ];

    private static IDatabaseProvider ProviderFor(string dialect) =>
        dialect switch
        {
            "postgresql" => new PostgreSqlProvider(),
            "mysql" => new MySqlProvider(),
            "oracle" => new OracleProvider(),
            "sqlite" => new SqliteProvider(),
            _ => new SqlServerProvider(),
        };
}
