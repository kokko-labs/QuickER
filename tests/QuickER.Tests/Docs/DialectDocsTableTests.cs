using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.Resources;
using Xunit;

namespace QuickER.Tests.Docs;

/// <summary>
/// docs の方言の表が、方言の宣言（<see cref="SyncDialectCapabilities"/>・<see cref="IdentifierLengthLimit"/>）と一致することを固定する
/// </summary>
/// <remarks>
/// <para>
/// 表は直前の HTML コメントの目印（<c>&lt;!-- dialect-table: 名前 --&gt;</c>）で探し、次の Markdown 表を読む。
/// 英語の文書（<c>docs/*.md</c>）と日本語の文書（<c>docs/*.ja.md</c>）の同じ目印の表は、行数・列数と記号のセル
/// （✅ と —）が一致しなければならない。語のセルは英日で比べない。
/// </para>
/// <para>
/// 宣言との照合は、英語の行見出し（行＝方言の表は方言の列の見出し）から宣言への対応表で行う。行の集合は対応表の
/// キーと完全に一致しなければならず、表に行を足すときも消すときも対応表を合わせて直す。目印の付いた表はすべて
/// <see cref="Tables"/> に登録しなければならない（登録の無い表は照合されずに静かに嘘になるため）。
/// </para>
/// </remarks>
public sealed class DialectDocsTableTests
{
    private const string Yes = "✅";

    private const string No = "—";

    private static readonly string Root = NeutralResxFiles.FindRepositoryRoot();

    private static readonly Regex MarkerPattern = new(
        @"^<!-- dialect-table: ([a-z0-9-]+) -->$",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex SeparatorCell = new(@"^:?-+:?$", RegexOptions.CultureInvariant);

    private static readonly Regex Digits = new(@"[0-9]+", RegexOptions.CultureInvariant);

    /// <summary>方言の宣言の持ち主（<see cref="DialectNames.All"/> の順）</summary>
    private static readonly IDatabaseProvider[] Providers = DialectNames
        .All.Select(name =>
            new IDatabaseProvider[]
            {
                new SqlServerProvider(),
                new PostgreSqlProvider(),
                new MySqlProvider(),
                new OracleProvider(),
                new SqliteProvider(),
            }.Single(p => p.Name == name)
        )
        .ToArray();

    /// <summary>表の向き</summary>
    private enum Orientation
    {
        /// <summary>行＝話題・列＝5 方言（先頭の列は行見出し）</summary>
        DialectColumns,

        /// <summary>行＝方言（先頭の列は方言の名前）</summary>
        DialectRows,
    }

    /// <summary>登録された表（目印の名前 → 置き場所の英語の文書と向き）</summary>
    private static readonly IReadOnlyDictionary<string, (string File, Orientation Shape)> Tables =
        new Dictionary<string, (string, Orientation)>
        {
            ["sync-capabilities"] = ("docs/database.md", Orientation.DialectColumns),
            ["foreign-key-rebuilds"] = ("docs/database.md", Orientation.DialectColumns),
            ["identifier-length-limits"] = ("docs/database.md", Orientation.DialectRows),
        };

    /// <summary>T-L：同期の能力（英語の行見出し → 宣言）</summary>
    private static readonly IReadOnlyDictionary<
        string,
        Func<SyncDialectCapabilities, bool>
    > SyncCapabilityRows = new Dictionary<string, Func<SyncDialectCapabilities, bool>>
    {
        ["Column order can be synced"] = c => c.ColumnReorder != ColumnReorderMode.None,
        ["A computed column's type change can be selected"] = c =>
            c.SupportsComputedColumnTypeChange,
        ["A unique constraint on exactly the primary key's columns is kept"] = c =>
            c.SupportsUniqueConstraintOnPrimaryKey,
        ["A table name with a dot is a schema-qualified name"] = c =>
            c.SupportsSchemaQualifiedTableNames,
        ["Foreign key names are stored"] = c => c.PersistsForeignKeyConstraintNames,
        ["Descriptions are synced"] = c => c.SupportsDescriptions,
        ["A failed script rolls back"] = c => c.TransactionalDdl,
    };

    /// <summary>T-M：外部キーの外し直しと付け直し（英語の行見出し → 宣言）</summary>
    private static readonly IReadOnlyDictionary<
        string,
        Func<SyncDialectCapabilities, bool>
    > ForeignKeyRebuildRows = new Dictionary<string, Func<SyncDialectCapabilities, bool>>
    {
        ["Altering a column drops and re-adds its foreign keys"] = c =>
            c.AlterColumnRequiresForeignKeyRebuild,
        [
            "A foreign key resting on the index of a dropped primary key or unique constraint is re-added"
        ] = c => c.ForeignKeyRequiresLeadingIndex,
        ["Referencing columns must follow the order of the referenced key"] = c =>
            c.ReferencedKeyRequiresSameColumnOrder,
        ["String types differing only in length are allowed on the two ends"] = c =>
            c.ForeignKeyAllowsStringLengthDifference,
        ["A failed re-add can leave the foreign key dropped"] = c => !c.TransactionalDdl,
    };

    /// <summary>T-Q の「数える単位」の語（英語・日本語）</summary>
    private static readonly IReadOnlyDictionary<
        IdentifierLengthUnit,
        (string English, string Japanese)
    > UnitWords = new Dictionary<IdentifierLengthUnit, (string, string)>
    {
        [IdentifierLengthUnit.Unlimited] = ("", ""),
        [IdentifierLengthUnit.Utf16CodeUnits] = ("UTF-16 code units", "UTF-16 コード単位"),
        [IdentifierLengthUnit.Utf8Bytes] = ("UTF-8 bytes", "UTF-8 バイト"),
        [IdentifierLengthUnit.Characters] = ("characters", "文字"),
    };

    public static TheoryData<string> Markers => [.. Tables.Keys];

    [Fact(
        DisplayName = "目印: docs の方言の表の目印は英日それぞれに 1 回ずつ現れ、すべて登録されている"
    )]
    public void Markers_AppearOnceInEachLanguageAndAreRegistered()
    {
        var docs = Path.Combine(Root, "docs");
        var english = Directory
            .EnumerateFiles(docs, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".ja.md", StringComparison.Ordinal))
            .ToArray();
        var japanese = Directory
            .EnumerateFiles(docs, "*.ja.md", SearchOption.AllDirectories)
            .ToArray();

        var englishMarkers = english.SelectMany(f => FindMarkers(f).Select(m => (m, f))).ToList();
        var japaneseMarkers = japanese.SelectMany(f => FindMarkers(f).Select(m => (m, f))).ToList();

        englishMarkers
            .GroupBy(x => x.m)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Should()
            .BeEmpty("目印は英語の文書の中で 1 回だけ使う");
        japaneseMarkers
            .GroupBy(x => x.m)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Should()
            .BeEmpty("目印は日本語の文書の中で 1 回だけ使う");

        englishMarkers
            .Select(x => x.m)
            .Should()
            .BeEquivalentTo(Tables.Keys, "目印の付いた表は、すべて照合の対応表に登録する");
        japaneseMarkers
            .Select(x => x.m)
            .Should()
            .BeEquivalentTo(Tables.Keys, "日本語の文書も同じ目印の表を持つ");

        foreach (var (marker, file) in englishMarkers)
        {
            Path.GetRelativePath(Root, file)
                .Replace('\\', '/')
                .Should()
                .Be(Tables[marker].File, $"目印 {marker} の置き場所");
        }

        foreach (var (marker, file) in japaneseMarkers)
        {
            Path.GetRelativePath(Root, file)
                .Replace('\\', '/')
                .Should()
                .Be(JapaneseFile(Tables[marker].File), $"目印 {marker} の置き場所（日本語）");
        }
    }

    [Theory(DisplayName = "英日: 同じ目印の表は行数・列数と記号のセルが一致する")]
    [MemberData(nameof(Markers))]
    public void EnglishAndJapaneseTables_AgreeInShapeAndSymbols(string marker)
    {
        var english = ReadTable(Tables[marker].File, marker);
        var japanese = ReadTable(JapaneseFile(Tables[marker].File), marker);

        japanese.Header.Count.Should().Be(english.Header.Count, "列数");
        japanese.Rows.Count.Should().Be(english.Rows.Count, "行数");

        for (var r = 0; r < english.Rows.Count; r++)
        {
            japanese.Rows[r].Count.Should().Be(english.Rows[r].Count, $"{r + 1} 行目の列数");

            for (var c = 0; c < english.Rows[r].Count; c++)
            {
                Symbol(japanese.Rows[r][c])
                    .Should()
                    .Be(
                        Symbol(english.Rows[r][c]),
                        $"{r + 1} 行目 {c + 1} 列目（英: {english.Rows[r][c]} / 日: {japanese.Rows[r][c]}）"
                    );
            }
        }
    }

    [Theory(DisplayName = "方言: 表の方言の並びは DialectNames.All の順の表示名と一致する")]
    [MemberData(nameof(Markers))]
    public void DialectsFollowDialectNamesOrder(string marker)
    {
        var expected = Providers.Select(p => p.DisplayName).ToArray();

        foreach (var file in new[] { Tables[marker].File, JapaneseFile(Tables[marker].File) })
        {
            var table = ReadTable(file, marker);
            var actual =
                Tables[marker].Shape == Orientation.DialectColumns
                    ? table.Header.Skip(1).ToArray()
                    : table.Rows.Select(row => row[0]).ToArray();

            actual.Should().Equal(expected, $"{file} の {marker}");

            if (Tables[marker].Shape == Orientation.DialectColumns)
            {
                table.Header[0].Should().BeEmpty($"{file} の {marker} の左上の見出し");
            }
        }
    }

    [Fact(DisplayName = "宣言: 同期の能力の表は SyncDialectCapabilities と一致する")]
    public void SyncCapabilitiesTable_MatchesDeclarations() =>
        AssertMatchesCapabilities("sync-capabilities", SyncCapabilityRows);

    [Fact(DisplayName = "宣言: 外部キーの外し直しの表は SyncDialectCapabilities と一致する")]
    public void ForeignKeyRebuildTable_MatchesDeclarations() =>
        AssertMatchesCapabilities("foreign-key-rebuilds", ForeignKeyRebuildRows);

    [Fact(
        DisplayName = "宣言: 識別子の長さの上限の表は IdentifierLengthLimit と TruncatesLongIdentifiers と一致する"
    )]
    public void IdentifierLengthLimitTable_MatchesDeclarations()
    {
        const string marker = "identifier-length-limits";

        foreach (var japanese in new[] { false, true })
        {
            var file = japanese ? JapaneseFile(Tables[marker].File) : Tables[marker].File;
            var table = ReadTable(file, marker);

            table.Rows.Should().HaveCount(Providers.Length, file);

            for (var i = 0; i < Providers.Length; i++)
            {
                var capabilities = Providers[i].SyncCapabilities;
                var limit = capabilities.IdentifierLengthLimit;
                var row = table.Rows[i];
                var where = $"{file} の {Providers[i].DisplayName} の行";

                row.Should().HaveCount(5, where);

                // 上限：数（ファイル名の上限があればその数も）。上限なしは語
                if (limit.IsUnlimited)
                {
                    row[1].Should().Be(japanese ? "なし" : "none", where + "の上限");
                }
                else
                {
                    var numbers = Digits
                        .Matches(row[1])
                        .Select(m =>
                            int.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)
                        )
                        .ToArray();

                    numbers.Should().NotBeEmpty(where + "の上限");
                    numbers[0].Should().Be(limit.Max, where + "の上限");

                    if (limit.TableFileNameMax is { } fileMax)
                    {
                        numbers.Should().Contain(fileMax, where + "のファイル名の上限");
                    }
                }

                // 数える単位
                var unit = UnitWords[limit.Unit];
                row[2].Should().Be(japanese ? unit.Japanese : unit.English, where + "の単位");

                // 保存できない文字：— ⇔ BMP の外の文字を保存できる
                (Symbol(row[3]) == No)
                    .Should()
                    .Be(limit.RepresentsSupplementaryCharacters, where + "の保存できない文字");

                // 長すぎる名前
                var longer =
                    limit.IsUnlimited ? ""
                    : capabilities.TruncatesLongIdentifiers
                        ? (japanese ? "黙って切り詰める" : "Silently cut")
                    : (japanese ? "断る" : "Rejected");
                row[4].Should().Be(longer, where + "の長すぎる名前");
            }
        }
    }

    /// <summary>行＝話題・列＝5 方言の表を、行見出しから宣言への対応表で照合する（英語の文書）</summary>
    private static void AssertMatchesCapabilities(
        string marker,
        IReadOnlyDictionary<string, Func<SyncDialectCapabilities, bool>> rows
    )
    {
        var table = ReadTable(Tables[marker].File, marker);

        table
            .Rows.Select(row => row[0])
            .Should()
            .BeEquivalentTo(rows.Keys, $"{marker} の行見出しは対応表のキーと一致する");

        foreach (var row in table.Rows)
        {
            row.Should().HaveCount(Providers.Length + 1, row[0]);

            for (var i = 0; i < Providers.Length; i++)
            {
                var expected = rows[row[0]](Providers[i].SyncCapabilities) ? Yes : No;

                Symbol(row[i + 1])
                    .Should()
                    .Be(
                        expected,
                        $"{marker} の「{row[0]}」の {Providers[i].DisplayName}（セル: {row[i + 1]}）"
                    );
            }
        }
    }

    /// <summary>セルの記号（✅ / — で始まればその記号、空なら空、それ以外は語）</summary>
    private static string Symbol(string cell) =>
        cell.StartsWith(Yes, StringComparison.Ordinal) ? Yes
        : cell.StartsWith(No, StringComparison.Ordinal) ? No
        : cell.Length == 0 ? ""
        : "(word)";

    private static string JapaneseFile(string englishFile) =>
        englishFile[..^".md".Length] + ".ja.md";

    private static string[] ReadLines(string file) =>
        File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static IEnumerable<string> FindMarkers(string file) =>
        ReadLines(file)
            .Select(line => MarkerPattern.Match(line))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value);

    /// <summary>目印の次の Markdown 表（見出しの行と、区切りの行を除いた行の配列）を読む</summary>
    private static (
        IReadOnlyList<string> Header,
        IReadOnlyList<IReadOnlyList<string>> Rows
    ) ReadTable(string relativeFile, string marker)
    {
        var lines = ReadLines(Path.Combine(Root, relativeFile));
        var at = Array.FindIndex(
            lines,
            line => MarkerPattern.Match(line) is { Success: true } m && m.Groups[1].Value == marker
        );

        at.Should().BeGreaterThanOrEqualTo(0, $"{relativeFile} に目印 {marker} がある");

        var i = at + 1;

        while (i < lines.Length && lines[i].Trim().Length == 0)
        {
            i++;
        }

        var tableLines = lines.Skip(i).TakeWhile(line => line.StartsWith('|')).ToArray();

        tableLines
            .Length.Should()
            .BeGreaterThanOrEqualTo(2, $"{relativeFile} の目印 {marker} の直後は表");

        var header = SplitRow(tableLines[0]);
        var separator = SplitRow(tableLines[1]);

        separator
            .Should()
            .OnlyContain(
                cell => SeparatorCell.IsMatch(cell),
                $"{relativeFile} の {marker} の 2 行目は区切りの行"
            );
        separator
            .Should()
            .HaveCount(header.Count, $"{relativeFile} の {marker} の区切りの行の列数");

        return (header, tableLines.Skip(2).Select(SplitRow).ToArray());
    }

    /// <summary>表の 1 行をセルへ分ける（<c>\|</c> は区切りにしない・前後の空白は除く）</summary>
    private static IReadOnlyList<string> SplitRow(string line)
    {
        var body = line.Trim();

        if (body.StartsWith('|'))
        {
            body = body[1..];
        }

        if (body.EndsWith('|') && !body.EndsWith("\\|", StringComparison.Ordinal))
        {
            body = body[..^1];
        }

        return Regex
            .Split(body, @"(?<!\\)\|", RegexOptions.CultureInvariant)
            .Select(cell => cell.Trim())
            .ToArray();
    }
}
