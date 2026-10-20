using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
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
/// docs の方言の表が、方言の宣言（<see cref="SyncDialectCapabilities"/>・<see cref="IdentifierLengthLimit"/>・
/// <see cref="UnsupportedTreatments"/>・<see cref="SchemaImportWarningKinds"/>）と型の表記の正規化に一致することを固定する
/// </summary>
/// <remarks>
/// <para>
/// 表は直前の HTML コメントの目印（<c>&lt;!-- dialect-table: 名前 --&gt;</c>）で探し、次の Markdown 表を読む。
/// 英語の文書（<c>docs/*.md</c>）と日本語の文書（<c>docs/*.ja.md</c>）の同じ目印の表は、行数・列数と記号のセル
/// （✅ と —）が一致しなければならない。語のセルは英日で比べない。記号のセルは記号だけか、記号の後ろに括弧を 1 つ
/// 置いた形に限る（括弧は記号の判定に影響しない）。
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

        /// <summary>方言の列も行も持たない（行＝話題・列＝機能）</summary>
        NoDialects,
    }

    /// <summary>登録された表（目印の名前 → 置き場所の英語の文書と向き）</summary>
    private static readonly IReadOnlyDictionary<string, (string File, Orientation Shape)> Tables =
        new Dictionary<string, (string, Orientation)>
        {
            ["sync-capabilities"] = ("docs/database.md", Orientation.DialectColumns),
            ["foreign-key-rebuilds"] = ("docs/database.md", Orientation.DialectColumns),
            ["identifier-length-limits"] = ("docs/database.md", Orientation.DialectRows),
            ["import-warnings"] = ("docs/database.md", Orientation.DialectColumns),
            ["imported-type-spelling"] = ("docs/database.md", Orientation.DialectRows),
            ["modeling-fidelity"] = ("docs/database.md", Orientation.NoDialects),
            ["same-type-spellings"] = ("docs/database.md", Orientation.DialectRows),
            ["referential-actions"] = ("docs/database.md", Orientation.DialectRows),
            ["column-alteration"] = ("docs/database.md", Orientation.DialectColumns),
            ["descriptions"] = ("docs/database.md", Orientation.DialectRows),
        };

    /// <summary>T-E：取込の警告（英語の行見出し → 行が表す警告の種別）</summary>
    /// <remarks>
    /// 種別は <see cref="SchemaImportWarningKinds.DefinitionKind"/> で図で表せない定義の種類へ写し、取込の扱い
    /// （<see cref="UnsupportedTreatments"/> の Import）が None でない方言に ✅ を付ける。種類を持たない種別は
    /// <see cref="WarningsWithoutDefinitionKind"/> が方言ごとの有無を持つ。
    /// </remarks>
    private static readonly IReadOnlyDictionary<
        string,
        SchemaImportWarningKind[]
    > ImportWarningRows = new Dictionary<string, SchemaImportWarningKind[]>
    {
        ["Domain or alias type"] =
        [
            SchemaImportWarningKind.DomainTypeFlattened,
            SchemaImportWarningKind.AliasTypeFlattened,
        ],
        ["Column that declares no type"] = [SchemaImportWarningKind.UndeclaredColumnType],
        ["Default value"] = [SchemaImportWarningKind.ColumnDefaultLost],
        ["Automatic numbering that accepts given values"] =
        [
            SchemaImportWarningKind.AutoIncrementLost,
        ],
        ["Collation, `INVISIBLE`, `ON UPDATE`, `SRID`"] =
        [
            SchemaImportWarningKind.ColumnCollationLost,
            SchemaImportWarningKind.InvisibleColumnLost,
            SchemaImportWarningKind.ColumnOnUpdateLost,
            SchemaImportWarningKind.SpatialReferenceIdLost,
        ],
        ["Name with spaces around it"] = [SchemaImportWarningKind.NameTrimmed],
        ["Foreign key outside the import scope"] = [SchemaImportWarningKind.ForeignKeyOutsideScope],
        ["Disabled constraint"] = [SchemaImportWarningKind.DisabledConstraintExcluded],
        ["Table with no readable column"] = [SchemaImportWarningKind.TableColumnsUnavailable],
        ["Table names differing only in case or surrounding spaces"] =
        [
            SchemaImportWarningKind.TableNameCollision,
        ],
        ["Column names differing only in case or surrounding spaces"] =
        [
            SchemaImportWarningKind.ColumnNameCollision,
        ],
        ["Table name containing a dot"] = [SchemaImportWarningKind.DottedTableNameExcluded],
        ["Computed or generated column"] = [SchemaImportWarningKind.ComputedColumnExpressionLost],
        ["Automatic numbering that refuses given values"] =
        [
            SchemaImportWarningKind.IdentityColumnMarked,
        ],
        ["View"] = [SchemaImportWarningKind.ViewExcluded],
        ["Materialized view"] = [SchemaImportWarningKind.MaterializedViewExcluded],
        ["External table"] = [SchemaImportWarningKind.ExternalTableExcluded],
        ["Child partition"] = [SchemaImportWarningKind.ChildPartitionExcluded],
        ["Temporary table"] = [SchemaImportWarningKind.TemporaryTableExcluded],
        ["Virtual table"] = [SchemaImportWarningKind.VirtualTableExcluded],
        ["Temporal history table"] = [SchemaImportWarningKind.TemporalHistoryTableExcluded],
        ["Foreign key to a table the import left out"] =
        [
            SchemaImportWarningKind.ForeignKeyToExcludedTable,
        ],
        ["Partitioned table"] = [SchemaImportWarningKind.PartitionDefinitionLost],
        ["Data type that cannot be written into SQL safely"] =
        [
            SchemaImportWarningKind.ColumnTypeNotEmittable,
        ],
        ["Primary key that allows NULL"] = [SchemaImportWarningKind.PrimaryKeyNullabilityAdjusted],
    };

    /// <summary>
    /// 図で表せない定義の種類を持たない取込警告の種別と、それを出す方言（<see cref="DialectNames.All"/> の順）
    /// </summary>
    /// <remarks>
    /// 宣言が無いので、取込のコードが警告を出す箇所から決める。範囲外の外部キーは、全スキーマを取り込み、別の DB を
    /// 参照する外部キーを作れない SQL Server だけが出さない。表名・列名の衝突（<c>SchemaTableEntry</c>）とドットを含む
    /// 表名（<c>SchemaImportTableNames</c>）は 5 方言の取込が共有の部品で判定する。
    /// </remarks>
    private static readonly IReadOnlyDictionary<
        SchemaImportWarningKind,
        bool[]
    > WarningsWithoutDefinitionKind = new Dictionary<SchemaImportWarningKind, bool[]>
    {
        [SchemaImportWarningKind.ForeignKeyOutsideScope] = [false, true, true, true, true],
        [SchemaImportWarningKind.TableNameCollision] = [true, true, true, true, true],
        [SchemaImportWarningKind.ColumnNameCollision] = [true, true, true, true, true],
        [SchemaImportWarningKind.DottedTableNameExcluded] = [true, true, true, true, true],
    };

    /// <summary>T-G：図がモデル化する範囲（英語の行見出し → 図で表せない定義の種類。種類の無い行は null）</summary>
    private static readonly IReadOnlyDictionary<
        string,
        UnsupportedDefinitionKind?
    > ModelingFidelityRows = new Dictionary<string, UnsupportedDefinitionKind?>
    {
        ["`DEFAULT`"] = UnsupportedDefinitionKind.DefaultValue,
        ["`CHECK` constraint"] = UnsupportedDefinitionKind.CheckConstraint,
        ["`COLLATE`"] = UnsupportedDefinitionKind.Collation,
        ["Generated / computed column"] = UnsupportedDefinitionKind.ComputedColumn,
        ["Automatic numbering that refuses given values"] =
            UnsupportedDefinitionKind.IdentityRejectingValues,
        ["Automatic numbering that accepts given values"] =
            UnsupportedDefinitionKind.IdentityAcceptingValues,
        ["Non-unique index"] = UnsupportedDefinitionKind.NonUniqueIndex,
        ["Trigger"] = UnsupportedDefinitionKind.Trigger,
        ["Table options after the column list (`WITHOUT ROWID`, `STRICT`)"] =
            UnsupportedDefinitionKind.TableOption,
        ["Constraint clauses (`ON CONFLICT`, `DEFERRABLE INITIALLY DEFERRED`)"] =
            UnsupportedDefinitionKind.ConstraintClause,
        ["View"] = UnsupportedDefinitionKind.View,
        ["Stored procedure, function, sequence"] = null,
    };

    /// <summary>T-J：列の変更（英語の行見出し → 宣言）</summary>
    private static readonly IReadOnlyDictionary<
        string,
        Func<IDatabaseProvider, bool>
    > ColumnAlterationRows = new Dictionary<string, Func<IDatabaseProvider, bool>>
    {
        ["Altering a column reads the collation back and writes it again"] = p =>
            UnsupportedTreatments.For(
                UnsupportedDefinitionKind.Collation,
                UnsupportedFeature.Sync,
                p.Name
            ) == UnsupportedTreatment.Preserve,
        ["Altering a column keeps the `DEFAULT`"] = p =>
            p.SyncCapabilities.ChecksDefaultOnColumnTypeChange,
        ["Changing the kind of type of a column with a default is confirmed"] = p =>
            p.SyncCapabilities.ChecksDefaultOnColumnTypeChange,
    };

    private static readonly Regex CodeSpan = new(@"`([^`]+)`", RegexOptions.CultureInvariant);

    /// <summary>記号のセルの後ろに置ける括弧（英語は半角・日本語は全角）</summary>
    private static readonly Regex SymbolCell = new(
        @"^(?:✅|—)(?: \(.+\)|（.+）)?$",
        RegexOptions.CultureInvariant
    );

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
        ["Altering a key column drops and re-adds the primary key and unique constraints"] = c =>
            c.AlterColumnRequiresKeyConstraintRebuild,
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

    public static TheoryData<string> DialectMarkers =>
        [.. Tables.Where(t => t.Value.Shape != Orientation.NoDialects).Select(t => t.Key)];

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

    [Theory(DisplayName = "記号: 記号のセルは記号だけか、記号の後ろに括弧を 1 つ置いた形")]
    [MemberData(nameof(Markers))]
    public void SymbolCells_AreBareOrFollowedByParentheses(string marker)
    {
        foreach (var file in new[] { Tables[marker].File, JapaneseFile(Tables[marker].File) })
        {
            foreach (var cell in ReadTable(file, marker).Rows.SelectMany(row => row))
            {
                if (
                    cell.StartsWith(Yes, StringComparison.Ordinal)
                    || cell.StartsWith(No, StringComparison.Ordinal)
                )
                {
                    SymbolCell
                        .IsMatch(cell)
                        .Should()
                        .BeTrue(
                            $"{file} の {marker} のセル「{cell}」は記号の判定を壊さない形にする"
                        );
                }
            }
        }
    }

    [Theory(DisplayName = "方言: 表の方言の並びは DialectNames.All の順の表示名と一致する")]
    [MemberData(nameof(DialectMarkers))]
    public void DialectsFollowDialectNamesOrder(string marker)
    {
        var expected = Providers.Select(p => p.DisplayName).ToArray();

        foreach (var file in new[] { Tables[marker].File, JapaneseFile(Tables[marker].File) })
        {
            var table = ReadTable(file, marker);
            var actual =
                Tables[marker].Shape == Orientation.DialectColumns
                    ? table.Header.Skip(1).Take(Providers.Length).ToArray()
                    : table.Rows.Select(row => row[0]).ToArray();

            actual.Should().Equal(expected, $"{file} の {marker}");
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

    [Fact(
        DisplayName = "宣言: 取込の警告の表は SchemaImportWarningKinds と UnsupportedTreatments の取込の扱いと一致する"
    )]
    public void ImportWarningTable_MatchesDeclarations()
    {
        const string marker = "import-warnings";
        var english = ReadTable(Tables[marker].File, marker);
        var japanese = ReadTable(JapaneseFile(Tables[marker].File), marker);

        english
            .Rows.Select(row => row[0])
            .Should()
            .BeEquivalentTo(ImportWarningRows.Keys, $"{marker} の行見出しは対応表のキーと一致する");

        for (var r = 0; r < english.Rows.Count; r++)
        {
            var row = english.Rows[r];
            var kinds = ImportWarningRows[row[0]];

            row.Should().HaveCount(Providers.Length + 2, row[0]);

            for (var i = 0; i < Providers.Length; i++)
            {
                var occurs = kinds.Select(kind => ImportReports(kind, i)).Distinct().ToArray();

                occurs
                    .Should()
                    .ContainSingle($"「{row[0]}」の種別は {Providers[i].DisplayName} で値がそろう");
                Symbol(row[i + 1])
                    .Should()
                    .Be(
                        occurs[0] ? Yes : No,
                        $"{marker} の「{row[0]}」の {Providers[i].DisplayName}（セル: {row[i + 1]}）"
                    );
            }

            // 図での扱い：まとめて告げる種別（取り込まず名前を並べる）だけがその語
            var aggregated = kinds
                .Select(SchemaImportWarningKinds.IsAggregated)
                .Distinct()
                .ToArray();

            aggregated.Should().ContainSingle($"「{row[0]}」の種別はまとめて告げるかがそろう");
            (row[^1] == "Skipped, listed by name")
                .Should()
                .Be(aggregated[0], $"「{row[0]}」の図での扱い（英: {row[^1]}）");

            if (Aligned(english, japanese))
            {
                (japanese.Rows[r][^1] == "取り込まず名前を並べる")
                    .Should()
                    .Be(aggregated[0], $"「{row[0]}」の図での扱い（日: {japanese.Rows[r][^1]}）");
            }
        }
    }

    [Fact(
        DisplayName = "網羅: 取込の警告の表は SchemaImportWarningKind の全種別を 1 回ずつ受け持つ"
    )]
    public void ImportWarningTable_CoversEveryWarningKind()
    {
        var mapped = ImportWarningRows.Values.SelectMany(kinds => kinds).ToArray();

        mapped.Should().OnlyHaveUniqueItems("1 つの種別は 1 つの行だけが受け持つ");
        mapped
            .Should()
            .BeEquivalentTo(
                Enum.GetValues<SchemaImportWarningKind>(),
                "種別を足したら表の行も足す"
            );
        WarningsWithoutDefinitionKind
            .Keys.Should()
            .BeEquivalentTo(
                Enum.GetValues<SchemaImportWarningKind>()
                    .Where(kind => SchemaImportWarningKinds.DefinitionKind(kind) is null),
                "種類を持たない種別は、出す方言を定数で持つ"
            );
    }

    [Fact(
        DisplayName = "宣言: 図がモデル化する範囲の表は UnsupportedTreatments の取込・DDL・同期の扱いと一致する"
    )]
    public void ModelingFidelityTable_MatchesTreatments()
    {
        const string marker = "modeling-fidelity";
        var english = ReadTable(Tables[marker].File, marker);
        var japanese = ReadTable(JapaneseFile(Tables[marker].File), marker);

        english
            .Rows.Select(row => row[0])
            .Should()
            .BeEquivalentTo(
                ModelingFidelityRows.Keys,
                $"{marker} の行見出しは対応表のキーと一致する"
            );

        for (var r = 0; r < english.Rows.Count; r++)
        {
            var kind = ModelingFidelityRows[english.Rows[r][0]];

            var languages = Aligned(english, japanese)
                ? new[] { (english.Rows[r], false), (japanese.Rows[r], true) }
                : new[] { (english.Rows[r], false) };

            foreach (var (row, ja) in languages)
            {
                var where = $"{marker} の「{english.Rows[r][0]}」（{(ja ? "日" : "英")}）";

                row.Should().HaveCount(4, where);
                AssertImportCell(row[1], kind, ja, where + "の取込");
                AssertDdlCell(row[2], kind, ja, where + "の DDL");
                AssertRebuildCell(row[3], kind, ja, where + "の SQLite の再構築");
            }
        }
    }

    [Fact(
        DisplayName = "振る舞い: 取り込んだ型の表記の表は各方言の NormalizeTypeNotation と一致する"
    )]
    public void ImportedTypeSpellingTable_MatchesNormalization()
    {
        const string marker = "imported-type-spelling";
        var english = ReadTable(Tables[marker].File, marker);
        var japanese = ReadTable(JapaneseFile(Tables[marker].File), marker);

        english.Rows.Should().HaveCount(Providers.Length);

        for (var i = 0; i < Providers.Length; i++)
        {
            var normalize = Providers[i].SyncCapabilities.NormalizeTypeNotation;
            var declared = CodeSpans(english.Rows[i][1]);
            var imported = CodeSpans(english.Rows[i][2]);
            var where = $"{marker} の {Providers[i].DisplayName} の行";

            declared.Should().NotBeEmpty(where);
            imported.Should().NotBeEmpty(where);

            if (imported.Length == 1)
            {
                declared
                    .Select(normalize)
                    .Should()
                    .AllBe(imported[0], where + "（宣言はすべて同じ表記になる）");
            }
            else
            {
                declared
                    .Select(normalize)
                    .Should()
                    .Equal(imported, where + "（宣言と取込は順に対応する）");
            }

            if (Aligned(english, japanese))
            {
                CodeSpans(japanese.Rows[i][1]).Should().Equal(declared, where + "の宣言（日）");
                CodeSpans(japanese.Rows[i][2])
                    .Should()
                    .Equal(imported, where + "の取込の表記（日）");
            }
        }
    }

    [Fact(DisplayName = "振る舞い: 同じとみなす型の綴りの表は各方言の AreSameType と一致する")]
    public void SameTypeSpellingsTable_MatchesAreSameType()
    {
        const string marker = "same-type-spellings";
        var english = ReadTable(Tables[marker].File, marker);
        var japanese = ReadTable(JapaneseFile(Tables[marker].File), marker);

        english.Rows.Should().HaveCount(Providers.Length);

        for (var i = 0; i < Providers.Length; i++)
        {
            var capabilities = Providers[i].SyncCapabilities;
            var where = $"{marker} の {Providers[i].DisplayName} の行";

            foreach (var (column, same) in new[] { (1, true), (2, false) })
            {
                var spans = CodeSpans(english.Rows[i][column]);

                (spans.Length % 2).Should().Be(0, where + "の綴りは 2 つずつ組になる");

                for (var k = 0; k < spans.Length; k += 2)
                {
                    capabilities
                        .AreSameType(spans[k], spans[k + 1])
                        .Should()
                        .Be(same, $"{where}：`{spans[k]}` と `{spans[k + 1]}`");
                }

                if (Aligned(english, japanese))
                {
                    CodeSpans(japanese.Rows[i][column]).Should().Equal(spans, where + "（日）");
                }
            }
        }
    }

    [Fact(
        DisplayName = "宣言: 参照アクションの表は SyncDialectCapabilities と UnsupportedTreatments と一致する"
    )]
    public void ReferentialActionsTable_MatchesDeclarations()
    {
        const string marker = "referential-actions";
        var english = ReadTable(Tables[marker].File, marker);
        var japanese = ReadTable(JapaneseFile(Tables[marker].File), marker);
        var languages = Aligned(english, japanese)
            ? new[] { (english, false), (japanese, true) }
            : new[] { (english, false) };

        foreach (var (table, ja) in languages)
        {
            table.Rows.Should().HaveCount(Providers.Length);

            for (var i = 0; i < Providers.Length; i++)
            {
                var provider = Providers[i];
                var capabilities = provider.SyncCapabilities;
                var row = table.Rows[i];
                var where = $"{marker} の {provider.DisplayName} の行（{(ja ? "日" : "英")}）";

                row.Should().HaveCount(4, where);

                // ON UPDATE：持たない方言は句を省いて注記する
                Symbol(row[1])
                    .Should()
                    .Be(capabilities.SupportsOnUpdateAction ? Yes : No, where + "の ON UPDATE");
                AssertOmittedWithNote(
                    row[1],
                    UnsupportedDefinitionKind.OnUpdateReferentialAction,
                    provider,
                    ja,
                    where + "の ON UPDATE"
                );

                // SET DEFAULT：効く方言でも、DEFAULT 制約を要する方言は括弧で告げる
                Symbol(row[2])
                    .Should()
                    .Be(capabilities.SupportsSetDefaultAction ? Yes : No, where + "の SET DEFAULT");
                AssertOmittedWithNote(
                    row[2],
                    UnsupportedDefinitionKind.SetDefaultReferentialAction,
                    provider,
                    ja,
                    where + "の SET DEFAULT"
                );

                if (capabilities.SupportsSetDefaultAction)
                {
                    (row[2] != Yes)
                        .Should()
                        .Be(
                            capabilities.SetDefaultRequiresColumnDefault,
                            where + "の SET DEFAULT の括弧"
                        );

                    if (capabilities.SetDefaultRequiresColumnDefault)
                    {
                        row[2].Should().Contain(ja ? "DEFAULT 制約" : "DEFAULT constraint", where);
                    }
                }

                // 差分：比較で畳むアクションだけを名指しする
                var foldsOnUpdate =
                    UnsupportedTreatments.For(
                        UnsupportedDefinitionKind.OnUpdateReferentialAction,
                        UnsupportedFeature.Diff,
                        provider.Name
                    ) == UnsupportedTreatment.Fold;
                var foldsSetDefault =
                    UnsupportedTreatments.For(
                        UnsupportedDefinitionKind.SetDefaultReferentialAction,
                        UnsupportedFeature.Diff,
                        provider.Name
                    ) == UnsupportedTreatment.Fold;

                row[3]
                    .Contains("`ON UPDATE`", StringComparison.Ordinal)
                    .Should()
                    .Be(foldsOnUpdate, where + "の差分");
                row[3]
                    .Contains("`SET DEFAULT`", StringComparison.Ordinal)
                    .Should()
                    .Be(foldsSetDefault, where + "の差分");

                if (!foldsOnUpdate && !foldsSetDefault)
                {
                    row[3].Should().Be(ja ? "宣言のまま" : "As declared", where + "の差分");
                }
            }
        }
    }

    [Fact(
        DisplayName = "宣言: 列の変更の表は UnsupportedTreatments と ChecksDefaultOnColumnTypeChange と一致する"
    )]
    public void ColumnAlterationTable_MatchesDeclarations()
    {
        const string marker = "column-alteration";
        var english = ReadTable(Tables[marker].File, marker);
        var japanese = ReadTable(JapaneseFile(Tables[marker].File), marker);

        english
            .Rows.Select(row => row[0])
            .Should()
            .BeEquivalentTo(
                ColumnAlterationRows.Keys,
                $"{marker} の行見出しは対応表のキーと一致する"
            );

        for (var r = 0; r < english.Rows.Count; r++)
        {
            var heading = english.Rows[r][0];

            english.Rows[r].Should().HaveCount(Providers.Length + 1, heading);

            for (var i = 0; i < Providers.Length; i++)
            {
                var expected = ColumnAlterationRows[heading](Providers[i]) ? Yes : No;

                Symbol(english.Rows[r][i + 1])
                    .Should()
                    .Be(
                        expected,
                        $"{marker} の「{heading}」の {Providers[i].DisplayName}（セル: {english.Rows[r][i + 1]}）"
                    );
            }
        }

        // DEFAULT を保つ行：DB 自身が保つ方言（同期の扱いが型の種類の変更の確認＝Warning）と、QuickER が付け直す方言
        // （Preserve）を括弧の語で分ける
        var keepsDefault = english
            .Rows.Select(row => row[0])
            .ToList()
            .IndexOf("Altering a column keeps the `DEFAULT`");
        var languages = Aligned(english, japanese)
            ? new[] { (english.Rows[keepsDefault], false), (japanese.Rows[keepsDefault], true) }
            : new[] { (english.Rows[keepsDefault], false) };

        foreach (var (row, ja) in languages)
        {
            for (var i = 0; i < Providers.Length; i++)
            {
                if (Symbol(row[i + 1]) != Yes)
                {
                    continue;
                }

                var treatment = UnsupportedTreatments.For(
                    UnsupportedDefinitionKind.DefaultValue,
                    UnsupportedFeature.Sync,
                    Providers[i].Name
                );

                row[i + 1]
                    .Contains(ja ? "DB が保つ" : "kept by the database", StringComparison.Ordinal)
                    .Should()
                    .Be(
                        treatment == UnsupportedTreatment.Warning,
                        $"{marker} の DEFAULT の行の {Providers[i].DisplayName}（{(ja ? "日" : "英")}・扱い {treatment}）"
                    );
            }
        }
    }

    [Fact(
        DisplayName = "宣言: 説明の表は SupportsDescriptions・扱いの表・識別子の宣言と、取込と DDL のコードに一致する"
    )]
    public void DescriptionsTable_MatchesDeclarationsAndCode()
    {
        const string marker = "descriptions";
        var english = ReadTable(Tables[marker].File, marker);
        var japanese = ReadTable(JapaneseFile(Tables[marker].File), marker);
        var languages = Aligned(english, japanese)
            ? new[] { (english, false), (japanese, true) }
            : new[] { (english, false) };

        foreach (var (table, ja) in languages)
        {
            table.Rows.Should().HaveCount(Providers.Length);

            for (var i = 0; i < Providers.Length; i++)
            {
                var provider = Providers[i];
                var row = table.Rows[i];
                var where = $"{marker} の {provider.DisplayName} の行（{(ja ? "日" : "英")}）";

                row.Should().HaveCount(4, where);

                if (!provider.SyncCapabilities.SupportsDescriptions)
                {
                    // 説明の機構が無い方言だけが、取込元と書き方を — にし、BMP の列を空にする
                    Symbol(row[1]).Should().Be(No, where + "の取込元");
                    Symbol(row[2]).Should().Be(No, where + "の書き方");
                    row[3].Should().BeEmpty(where + "の BMP の外の文字");

                    if (
                        UnsupportedTreatments.For(
                            UnsupportedDefinitionKind.Description,
                            UnsupportedFeature.Ddl,
                            provider.Name
                        ) == UnsupportedTreatment.Annotation
                    )
                    {
                        row[2]
                            .Should()
                            .Contain(ja ? "コメント行" : "comment lines", where + "の書き方");
                    }

                    continue;
                }

                Symbol(row[3])
                    .Should()
                    .Be(
                        provider
                            .SyncCapabilities
                            .IdentifierLengthLimit
                            .RepresentsSupplementaryCharacters
                            ? Yes
                            : No,
                        where + "の BMP の外の文字"
                    );

                // 日本語の語のセルは、コードスパンが英語の同じ行と一致すること（コードとの照合は英語の行が受け持つ）
                if (ja)
                {
                    CodeSpans(row[1])
                        .Should()
                        .Equal(
                            CodeSpans(english.Rows[i][1]),
                            where + "の取込元（英と同じコードスパン）"
                        );
                    CodeSpans(row[2])
                        .Should()
                        .Equal(
                            CodeSpans(english.Rows[i][2]),
                            where + "の書き方（英と同じコードスパン）"
                        );

                    continue;
                }

                // 語のセルはコードと照合する：取込元は取込のソース、書き方は説明つきの図の DDL
                var importer = ImporterSource(provider);
                var ddl = DescriptionDdl(provider);
                var imported = CodeSpans(row[1]);
                var written = CodeSpans(row[2]);

                imported.Should().NotBeEmpty(where + "の取込元");
                written.Should().NotBeEmpty(where + "の書き方");
                imported
                    .Should()
                    .OnlyContain(
                        span => importer.Contains(span, StringComparison.Ordinal),
                        where + "の取込元は取込のコードにある"
                    );
                written
                    .Should()
                    .OnlyContain(
                        span => ddl.Contains(span, StringComparison.Ordinal),
                        where + "の書き方は DDL に現れる"
                    );
            }
        }
    }

    /// <summary>— のセルは、その方言で句を省いて注記する扱い（OmitWithAnnotation）のときだけ注記を名乗る</summary>
    private static void AssertOmittedWithNote(
        string cell,
        UnsupportedDefinitionKind kind,
        IDatabaseProvider provider,
        bool ja,
        string where
    )
    {
        if (Symbol(cell) != No)
        {
            return;
        }

        UnsupportedTreatments
            .For(kind, UnsupportedFeature.Ddl, provider.Name)
            .Should()
            .Be(UnsupportedTreatment.OmitWithAnnotation, where);
        cell.Should().Contain(ja ? "注記" : "note", where);
    }

    /// <summary>方言の取込のソース（<c>src/QuickER.Provider.{方言}/{方言}SchemaImporter.cs</c>）</summary>
    private static string ImporterSource(IDatabaseProvider provider)
    {
        var name = provider.GetType().Name[..^"Provider".Length];

        return File.ReadAllText(
            Path.Combine(Root, "src", $"QuickER.Provider.{name}", $"{name}SchemaImporter.cs")
        );
    }

    /// <summary>テーブルと列に説明を持つ 1 表の図から、その方言の DDL を作る</summary>
    private static string DescriptionDdl(IDatabaseProvider provider)
    {
        var diagram = new ErDiagram { TargetDbms = provider.Name };
        diagram.Entities.Add(
            new Entity
            {
                TableName = "t",
                Description = "table note",
                Columns =
                {
                    new Column
                    {
                        Name = "id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                        Description = "column note",
                    },
                },
            }
        );

        return provider.DdlGenerator.Build(diagram);
    }

    /// <summary>種別が方言 <paramref name="dialect"/>（<see cref="Providers"/> の位置）で告げられるか</summary>
    private static bool ImportReports(SchemaImportWarningKind kind, int dialect) =>
        SchemaImportWarningKinds.DefinitionKind(kind) is { } definition
            ? UnsupportedTreatments.For(
                definition,
                UnsupportedFeature.Import,
                Providers[dialect].Name
            ) != UnsupportedTreatment.None
            : WarningsWithoutDefinitionKind[kind][dialect];

    /// <summary>種類 × 機能の扱いを、<see cref="Providers"/> の順に並べる（種類の無い行はすべて None）</summary>
    private static UnsupportedTreatment[] TreatmentsOf(
        UnsupportedDefinitionKind? kind,
        UnsupportedFeature feature
    ) =>
        Providers
            .Select(p =>
                kind is { } k
                    ? UnsupportedTreatments.For(k, feature, p.Name)
                    : UnsupportedTreatment.None
            )
            .ToArray();

    private static void AssertImportCell(
        string cell,
        UnsupportedDefinitionKind? kind,
        bool ja,
        string where
    )
    {
        var treatments = TreatmentsOf(kind, UnsupportedFeature.Import);

        if (treatments.All(t => t == UnsupportedTreatment.None))
        {
            cell.Should().Be(ja ? "取り込まない" : "Not imported", where);
        }
        else if (treatments.Contains(UnsupportedTreatment.MarkWithNotice))
        {
            if (ja)
            {
                cell.Should().Contain("印を付けて取り込み", where);
            }
            else
            {
                cell.Should().StartWith("Imported as", where);
            }
        }
        else
        {
            treatments
                .Should()
                .OnlyContain(
                    t =>
                        t == UnsupportedTreatment.None
                        || t == UnsupportedTreatment.Notice
                        || t == UnsupportedTreatment.ExcludeWithAggregatedNotice,
                    where + "の扱いは表の語が受け持つもの"
                );

            if (ja)
            {
                cell.Should().StartWith("取り込まない（", where).And.Contain("告げる", where);
            }
            else
            {
                cell.Should().StartWith("Not imported (named", where);
            }
        }

        AssertNamedDialects(cell, treatments, where);
    }

    private static void AssertDdlCell(
        string cell,
        UnsupportedDefinitionKind? kind,
        bool ja,
        string where
    )
    {
        var treatments = TreatmentsOf(kind, UnsupportedFeature.Ddl);

        if (treatments.All(t => t == UnsupportedTreatment.None))
        {
            cell.Should().Be(ja ? "出力しない" : "Not emitted", where);
        }
        else
        {
            treatments
                .Should()
                .OnlyContain(
                    t => t == UnsupportedTreatment.None || t == UnsupportedTreatment.Annotation,
                    where + "の扱いは表の語が受け持つもの"
                );
            cell.Should()
                .StartWith(
                    ja
                        ? "普通の列として出力（注意コメント付き）"
                        : "Emitted as an ordinary column, with a note",
                    where
                );
        }

        AssertNamedDialects(cell, treatments, where);
    }

    private static void AssertRebuildCell(
        string cell,
        UnsupportedDefinitionKind? kind,
        bool ja,
        string where
    )
    {
        var sqlite = Array.FindIndex(Providers, p => p.Name == DialectNames.Sqlite);
        var treatment = TreatmentsOf(kind, UnsupportedFeature.Sync)[sqlite];

        switch (treatment)
        {
            case UnsupportedTreatment.Warning:
                cell.Should().StartWith(ja ? "失われる" : "Lost", where);
                break;
            case UnsupportedTreatment.Annotation:
                cell.Should()
                    .StartWith(ja ? "失われる" : "Lost", where)
                    .And.Contain(ja ? "注意コメント" : "note", where);
                break;
            case UnsupportedTreatment.Preserve:
                cell.Should().StartWith(ja ? "温存" : "Preserved", where);
                break;
            case UnsupportedTreatment.None:
                (kind is null || !DialectDeclarations.CanOccur(DialectNames.Sqlite, kind.Value))
                    .Should()
                    .BeTrue(where + "の扱いが None になるのは、SQLite にあり得ない種類だけ");
                cell.Should().StartWith(ja ? "対象外" : "Not applicable", where);
                break;
            default:
                throw new InvalidOperationException(
                    $"{where}: 表の語が受け持たない扱い {treatment}"
                );
        }
    }

    /// <summary>セルが方言名を挙げるなら、扱いが None でない方言とちょうど一致する</summary>
    private static void AssertNamedDialects(
        string cell,
        UnsupportedTreatment[] treatments,
        string where
    )
    {
        var named = Providers
            .Where(p => cell.Contains(p.DisplayName, StringComparison.Ordinal))
            .ToArray();

        if (named.Length > 0)
        {
            named
                .Should()
                .Equal(
                    Providers.Where((_, i) => treatments[i] != UnsupportedTreatment.None),
                    where + "のセルが挙げる方言"
                );
        }
    }

    /// <summary>
    /// 日本語の表の行が英語の表の行と位置で対応するか（行数が違うときは英日の一致のテストが報告するので、宣言との照合は
    /// 日本語の行を見ない）
    /// </summary>
    private static bool Aligned(
        (IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows) english,
        (IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows) japanese
    ) => english.Rows.Count == japanese.Rows.Count;

    private static string[] CodeSpans(string cell) =>
        CodeSpan.Matches(cell).Select(m => m.Groups[1].Value).ToArray();

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
