using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Db.UI;
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

    /// <summary>方言ごとの C# 型の表の、すべての方言に当てはまる行の行見出し（英）</summary>
    private const string AnyDialectEnglish = "Any";

    /// <summary>方言ごとの C# 型の表の、すべての方言に当てはまる行の行見出し（日）</summary>
    private const string AnyDialectJapanese = "共通";

    private static readonly string Root = NeutralResxFiles.FindRepositoryRoot();

    /// <summary>変換できない型の表の、桁の上限を付けて変換したセルの注意の語（英）</summary>
    private static readonly string CappedNoteEnglish = string.Create(
        CultureInfo.InvariantCulture,
        $"up to {DiagramTypeConverter.CappedPrecision} digits"
    );

    /// <summary>変換できない型の表の、桁の上限を付けて変換したセルの注意の語（日）</summary>
    private static readonly string CappedNoteJapanese = string.Create(
        CultureInfo.InvariantCulture,
        $"{DiagramTypeConverter.CappedPrecision} 桁まで"
    );

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

        /// <summary>行＝一部の方言（先頭の列は方言の名前で始まる・並びは <see cref="DialectNames.All"/> の順の一部分）</summary>
        DialectRowSubset,

        /// <summary>方言の列も行も持たない（行＝話題・列＝機能）</summary>
        NoDialects,

        /// <summary>
        /// 行＝（方言・話題）。先頭の列は方言の表示名で、同じ方言の行は続けて並び、方言は <see cref="DialectNames.All"/>
        /// の順に 1 つ残らず現れる。最後に、すべての方言に当てはまる行（<see cref="AnyDialectEnglish"/>）を 1 つ置ける
        /// </summary>
        DialectRowGroups,
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
            ["unconvertible-types"] = ("docs/database.md", Orientation.DialectColumns),
            ["target-limits"] = ("docs/database.md", Orientation.DialectRowSubset),
            ["mysql-integers"] = ("docs/database.md", Orientation.NoDialects),
            ["connection-settings"] = ("docs/database.md", Orientation.DialectRows),
            ["csharp-types"] = ("docs/code-generation.md", Orientation.DialectRowGroups),
            ["unbounded-binary"] = ("docs/code-generation.md", Orientation.DialectRows),
            ["database-numbering"] = ("docs/code-generation.md", Orientation.DialectRows),
        };

    /// <summary>
    /// T-B：接続の表の語（英語・日本語・列の位置）→ 接続ダイアログがその項目を出すか（取込の画面と同期の画面）。
    /// 列の位置は 1 が接続先、3 が方言固有の項目
    /// </summary>
    private static readonly (
        string English,
        string Japanese,
        int Column,
        Func<DbConnectionDialogViewModel, DbConnectionDialogViewModel, bool> Shown
    )[] ConnectionSettingWords =
    [
        ("Host", "ホスト", 1, (import, _) => import.ShowServerFields),
        ("File path", "ファイルパス", 1, (import, _) => import.ShowFilePath),
        ("Create new", "新規作成", 1, (_, sync) => sync.ShowCreateNewFile),
        ("Authentication", "認証方式", 3, (import, _) => import.ShowAuthMode),
        (
            "TrustServerCertificate",
            "TrustServerCertificate",
            3,
            (import, _) => import.ShowTrustServerCertificate
        ),
        ("SSL Mode", "SSL Mode", 3, (import, _) => import.ShowSslMode),
        ("not encrypted", "暗号化されない", 3, (import, _) => import.ShowOracleEncryptionNote),
    ];

    /// <summary>T-B：認証方式の括弧の名前（英語・日本語）→ 認証方式の選択肢</summary>
    private static readonly (string English, string Japanese, DbAuthMode Mode)[] AuthModeNames =
    [
        ("Windows", "Windows 認証", DbAuthMode.Windows),
        ("SQL Server", "SQL Server 認証", DbAuthMode.UsernamePassword),
        ("Azure AD", "Azure AD 認証", DbAuthMode.AzureAd),
    ];

    /// <summary>T-P：中立の型の語（英語・日本語）。Decimal は語でなく精度・スケールのコードスパンで書く</summary>
    private static readonly IReadOnlyDictionary<
        CanonicalTypeKind,
        (string English, string Japanese)
    > IntegerKindWords = new Dictionary<CanonicalTypeKind, (string, string)>
    {
        [CanonicalTypeKind.TinyInt] = ("8-bit integer (unsigned)", "8 ビット整数（符号なし）"),
        [CanonicalTypeKind.SmallInt] = ("16-bit integer", "16 ビット整数"),
        [CanonicalTypeKind.Int32] = ("32-bit integer", "32 ビット整数"),
        [CanonicalTypeKind.Int64] = ("64-bit integer", "64 ビット整数"),
        [CanonicalTypeKind.Boolean] = ("Boolean", "真偽値"),
    };

    /// <summary>T-O：上限の数（桁区切りのカンマを許す・符号つき）</summary>
    private static readonly Regex LimitNumber = new(
        @"-?[0-9]+(?:,[0-9]{3})*",
        RegexOptions.CultureInvariant
    );

    /// <summary>T-O：文字列・バイナリの上限の組（`名前` / `名前` の並びと、その後ろの数）</summary>
    private static readonly Regex LimitGroup = new(
        @"(?<names>(?:`[^`]+`\s*/\s*)*`[^`]+`)\s*(?<n>[0-9]+(?:,[0-9]{3})*)",
        RegexOptions.CultureInvariant
    );

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

    /// <summary>方言ごとの C# 型の表の型の範囲（<c>`float(1)` to `float(24)`</c> / <c>`float(1)`〜`float(24)`</c>）</summary>
    private static readonly Regex TypeRange = new(
        @"`([a-z_]+)\(([0-9]+)\)`(?: to |〜)`\1\(([0-9]+)\)`",
        RegexOptions.CultureInvariant
    );

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
        ["Changes to an existing table run as statements"] = c =>
            c.SupportsAlterColumn && c.SupportsForeignKeyAlter,
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
            if (Tables[marker].Shape == Orientation.DialectRowSubset)
            {
                var indexes = table
                    .Rows.Select(row => Array.FindIndex(Providers, p => RowNames(row[0], p)))
                    .ToArray();

                indexes.Should().NotContain(-1, $"{file} の {marker} の行は方言の名前で始まる");
                indexes.Should().BeInAscendingOrder($"{file} の {marker}");
                indexes.Should().OnlyHaveUniqueItems($"{file} の {marker}");

                continue;
            }

            if (Tables[marker].Shape == Orientation.DialectRowGroups)
            {
                var any = file.EndsWith(".ja.md", StringComparison.Ordinal)
                    ? AnyDialectJapanese
                    : AnyDialectEnglish;
                var names = table.Rows.Select(row => row[0]).ToList();

                if (names.Count > 0 && names[^1] == any)
                {
                    names.RemoveAt(names.Count - 1);
                }

                var groups = names
                    .Select(name => Array.FindIndex(Providers, p => p.DisplayName == name))
                    .ToArray();

                groups.Should().NotContain(-1, $"{file} の {marker} の行は方言の表示名で始まる");
                groups.Should().BeInAscendingOrder($"{file} の {marker}");
                groups
                    .Distinct()
                    .Should()
                    .Equal(Enumerable.Range(0, Providers.Length), $"{file} の {marker} の方言");

                continue;
            }

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

    [Fact(
        DisplayName = "振る舞い: 方言切替で変換できない型の表は DiagramTypeConverter.CreatePlan と一致する"
    )]
    public void UnconvertibleTypesTable_MatchesTypeConversion()
    {
        const string marker = "unconvertible-types";
        var english = ReadTable(Tables[marker].File, marker);
        var japanese = ReadTable(JapaneseFile(Tables[marker].File), marker);

        english.Rows.Should().NotBeEmpty();

        for (var r = 0; r < english.Rows.Count; r++)
        {
            var row = english.Rows[r];
            var samples = Samples(row[0]);
            var where = $"{marker} の「{row[0]}」";

            row.Should().HaveCount(Providers.Length + 1, where);
            samples.Should().NotBeEmpty(where + "の見本（方言の名前とコードスパン）");

            if (Aligned(english, japanese))
            {
                var japaneseRow = japanese.Rows[r];

                Samples(japaneseRow[0]).Should().Equal(samples, where + "の見本（日）");

                for (var c = 1; c < row.Count; c++)
                {
                    CodeSpans(japaneseRow[c])
                        .Should()
                        .Equal(CodeSpans(row[c]), $"{where} の {c + 1} 列目のコードスパン（日）");
                    japaneseRow[c]
                        .Contains("NOT NULL を解除", StringComparison.Ordinal)
                        .Should()
                        .Be(
                            row[c].Contains("NOT NULL lifted", StringComparison.Ordinal),
                            $"{where} の {c + 1} 列目の NOT NULL の解除（日）"
                        );
                    japaneseRow[c]
                        .Contains(CappedNoteJapanese, StringComparison.Ordinal)
                        .Should()
                        .Be(
                            row[c].Contains(CappedNoteEnglish, StringComparison.Ordinal),
                            $"{where} の {c + 1} 列目の桁の注意（日）"
                        );
                }
            }

            for (var i = 0; i < Providers.Length; i++)
            {
                var cell = row[i + 1];
                var cellWhere = $"{where} の {Providers[i].DisplayName}（セル: {cell}）";
                var results = samples
                    .Select(s => Convert(s.Source, s.Type, Providers[i]))
                    .ToArray();

                results
                    .Select(x => x.Converted)
                    .Distinct()
                    .Should()
                    .ContainSingle(cellWhere + "：同じ行の見本は変換の結果がそろう");
                (Symbol(cell) == Yes).Should().Be(results[0].Converted, cellWhere);

                if (CodeSpans(cell) is [var written])
                {
                    results
                        .Select(x => x.NewType)
                        .Should()
                        .AllBe(written, cellWhere + "の変換後の型");
                }

                var lifted = cell.Contains("NOT NULL lifted", StringComparison.Ordinal);

                results
                    .Should()
                    .OnlyContain(x => x.MakeNullable == lifted, cellWhere + "の NOT NULL の解除");

                var capped = cell.Contains(CappedNoteEnglish, StringComparison.Ordinal);

                results
                    .Should()
                    .OnlyContain(x => x.PrecisionCapped == capped, cellWhere + "の桁の注意");
            }
        }
    }

    [Fact(
        DisplayName = "振る舞い: 接続の指定の表は接続ダイアログの表示・既定のポート・接続文字列の組み立てと一致する"
    )]
    public void ConnectionSettingsTable_MatchesDialogAndFactories()
    {
        const string marker = "connection-settings";
        // 接続プロファイルの保存先は一時フォルダ（AppData を使わない）
        var folder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests_" + Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(folder);

        try
        {
            foreach (var japanese in new[] { false, true })
            {
                var file = japanese ? JapaneseFile(Tables[marker].File) : Tables[marker].File;
                var table = ReadTable(file, marker);

                table.Rows.Should().HaveCount(Providers.Length, file);

                for (var r = 0; r < table.Rows.Count; r++)
                {
                    AssertConnectionRow(table.Rows[r], Providers[r], japanese, file, folder);
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // 後始末はベストエフォート
            }
        }
    }

    /// <summary>接続の表の 1 行を、接続ダイアログの表示と接続文字列の組み立てに突き合わせる</summary>
    private static void AssertConnectionRow(
        IReadOnlyList<string> row,
        IDatabaseProvider provider,
        bool japanese,
        string file,
        string folder
    )
    {
        var where = $"{file} の {provider.DisplayName} の行";
        var registry = new DatabaseProviderRegistry(Providers);
        var store = new SqlConnectionProfileStore(folder, useDpapi: false);
        var import = new DbConnectionDialogViewModel(
            registry,
            DbConnectionDialogMode.Import,
            fixedProvider: provider,
            store
        );
        var sync = new DbConnectionDialogViewModel(
            registry,
            DbConnectionDialogMode.Sync,
            fixedProvider: provider,
            store,
            allowSqliteFileCreation: true
        );

        row.Should().HaveCount(4, where);
        RowNames(row[0], provider).Should().BeTrue(where);

        // 接続先・方言固有の項目：語がある ⇔ ダイアログがその項目を出す
        foreach (var word in ConnectionSettingWords)
        {
            var term = japanese ? word.Japanese : word.English;

            row[word.Column]
                .Contains(term, StringComparison.Ordinal)
                .Should()
                .Be(word.Shown(import, sync), $"{where}の「{term}」");
        }

        // サービス名：「データベース名」欄の値を EZConnect のサービス名に使う組み立てだけが名乗る
        var settings = new DbConnectionSettings
        {
            Host = "db.example",
            Database = "svc",
            AuthMode = DbAuthMode.UsernamePassword,
            UserId = "u",
            Password = "p",
            FilePath = "quicker.db",
        };
        var connectionString = provider.BuildConnectionString(settings);

        row[1]
            .Contains(japanese ? "サービス名" : "service name", StringComparison.Ordinal)
            .Should()
            .Be(
                DataSourceOf(connectionString)?.EndsWith("/svc", StringComparison.Ordinal) == true,
                where + "のサービス名"
            );

        // 認証方式の括弧：名前の並び ⇔ 認証方式の選択肢（列挙値のすべて）
        var opening = japanese ? "認証方式（" : "Authentication (";
        var closing = japanese ? "）" : ")";
        var at = row[3].IndexOf(opening, StringComparison.Ordinal);

        if (at >= 0)
        {
            var start = at + opening.Length;
            var end = row[3].IndexOf(closing, start, StringComparison.Ordinal);

            row[3]
                [start..end]
                .Split(" / ")
                .Should()
                .Equal(
                    AuthModeNames.Select(n => japanese ? n.Japanese : n.English),
                    where + "の認証方式"
                );
            AuthModeNames
                .Select(n => n.Mode)
                .Should()
                .Equal(Enum.GetValues<DbAuthMode>(), where + "の認証方式の選択肢");
        }

        // 既定のポート：セル ＝ DefaultPort ＝ ポートを空けたときの接続文字列の組み立て
        DefaultPortOf(connectionString)
            .Should()
            .Be(provider.DefaultPort, where + "の組み立ての既定のポート");

        if (provider.DefaultPort is int port)
        {
            ParseLimit(LimitNumber.Matches(row[2]).Single().Value)
                .Should()
                .Be(port, where + "の既定のポート");
        }
        else
        {
            Symbol(row[2]).Should().Be(No, where + "の既定のポート");
        }
    }

    /// <summary>接続文字列の Data Source（無ければ null）</summary>
    private static string? DataSourceOf(string connectionString)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

        return builder.TryGetValue("Data Source", out var value) ? value as string : null;
    }

    /// <summary>
    /// ポートを空けて組み立てた接続文字列が使う既定のポート（Port のキー、または Data Source の
    /// <c>host:port</c> / <c>host,port</c>。どちらも無ければ null）
    /// </summary>
    private static int? DefaultPortOf(string connectionString)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

        if (builder.TryGetValue("Port", out var port))
        {
            return int.Parse(
                System.Convert.ToString(port, CultureInfo.InvariantCulture)!,
                CultureInfo.InvariantCulture
            );
        }

        var match = Regex.Match(
            DataSourceOf(connectionString) ?? "",
            @"^db\.example[:,]([0-9]+)",
            RegexOptions.CultureInvariant
        );

        return match.Success
            ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            : null;
    }

    [Fact(DisplayName = "振る舞い: 方言ごとの C# 型の表は各方言の型マッパーの写し方と一致する")]
    public void CSharpTypesTable_MatchesTypeMappers()
    {
        const string marker = "csharp-types";
        var english = ReadTable(Tables[marker].File, marker);
        var japanese = ReadTable(JapaneseFile(Tables[marker].File), marker);

        for (var r = 0; r < english.Rows.Count; r++)
        {
            var row = english.Rows[r];
            var where = $"{marker} の {r + 1} 行目（{string.Join(" | ", row)}）";

            row.Should().HaveCount(4, where);

            var any = row[0] == AnyDialectEnglish;
            var providers = any ? Providers : [Providers.Single(p => p.DisplayName == row[0])];
            var dbTypes = CodeSpans(row[1]);
            var csharpTypes = CodeSpans(row[2]);
            var leftOut = row[3] switch
            {
                "Left out" => true,
                "As usual" => false,
                _ => throw new InvalidOperationException(
                    $"{where}: EditModel の列の語「{row[3]}」"
                ),
            };

            dbTypes.Should().NotBeEmpty(where + "の DB の型");
            csharpTypes.Should().NotBeEmpty(where + "の C# の型");

            // C# の型が 1 つなら DB の型はその別名（すべて同じ型へ写る）。複数なら位置で組にする
            if (csharpTypes.Length > 1)
            {
                dbTypes.Should().HaveCount(csharpTypes.Length, where + "の型の組");
            }

            foreach (var provider in providers)
            {
                for (var i = 0; i < dbTypes.Length; i++)
                {
                    var expected = csharpTypes.Length == 1 ? csharpTypes[0] : csharpTypes[i];
                    var mapped = MapType(provider, dbTypes[i]);
                    var at = $"{where}の {provider.DisplayName} の `{dbTypes[i]}`";

                    mapped.TypeName.Should().Be(expected, at);
                    mapped.IsExcludedFromEditModel.Should().Be(leftOut, at + "の EditModel");
                    mapped.IsFallbackType.Should().Be(any, at + "の代替の型");
                }
            }

            // 範囲（`float(1)` to `float(24)`）は、範囲のすぐ外の型が同じ C# 型へ写らないことも確かめる
            foreach (Match range in TypeRange.Matches(row[1]))
            {
                var name = range.Groups[1].Value;
                var low = int.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture);
                var high = int.Parse(range.Groups[3].Value, CultureInfo.InvariantCulture);
                var outside = new[] { low - 1, high + 1 }.Where(n => n >= 1);

                csharpTypes.Should().ContainSingle(where + "の範囲は別名の行に書く");

                foreach (var provider in providers)
                {
                    foreach (var n in outside)
                    {
                        var type = string.Create(CultureInfo.InvariantCulture, $"{name}({n})");

                        MapType(provider, type)
                            .TypeName.Should()
                            .NotBe(csharpTypes[0], $"{where}の範囲の外の `{type}`");
                    }
                }
            }
        }

        if (!Aligned(english, japanese))
        {
            return;
        }

        for (var r = 0; r < english.Rows.Count; r++)
        {
            var en = english.Rows[r];
            var ja = japanese.Rows[r];
            var where = $"{marker} の {r + 1} 行目（日）";

            ja.Should().HaveCount(4, where);
            ja[0]
                .Should()
                .Be(en[0] == AnyDialectEnglish ? AnyDialectJapanese : en[0], where + "の方言");
            CodeSpans(ja[1]).Should().Equal(CodeSpans(en[1]), where + "の DB の型（英と同じ）");
            CodeSpans(ja[2]).Should().Equal(CodeSpans(en[2]), where + "の C# の型（英と同じ）");
            TypeRange
                .Matches(ja[1])
                .Count.Should()
                .Be(TypeRange.Matches(en[1]).Count, where + "の範囲");
            ja[3].Should().Be(en[3] == "Left out" ? "外す" : "通常どおり", where + "の EditModel");
        }
    }

    [Fact(
        DisplayName = "振る舞い: 無制限バイナリ列の表は各方言の型マッパーの IsUnboundedBinary と一致する"
    )]
    public void UnboundedBinaryTable_MatchesTypeMappers()
    {
        const string marker = "unbounded-binary";
        var english = ReadTable(Tables[marker].File, marker);
        var japanese = ReadTable(JapaneseFile(Tables[marker].File), marker);

        english.Rows.Should().HaveCount(Providers.Length, marker);

        for (var i = 0; i < Providers.Length; i++)
        {
            var provider = Providers[i];
            var row = english.Rows[i];
            var where = $"{marker} の {provider.DisplayName} の行";

            row.Should().HaveCount(3, where);

            var excluded = BinaryTypes(row[1]);

            excluded.Should().NotBeEmpty(where + "の除外する型");

            foreach (var type in excluded)
            {
                var mapped = MapType(provider, type);

                mapped.TypeName.Should().Be("byte[]", $"{where}の `{type}`");
                mapped.IsUnboundedBinary.Should().BeTrue($"{where}の除外する `{type}`");
            }

            if (Symbol(row[2]) == No)
            {
                CodeSpans(row[2]).Should().BeEmpty(where + "の除外しない型");
            }

            foreach (var type in BinaryTypes(row[2]))
            {
                var mapped = MapType(provider, type);

                mapped.TypeName.Should().Be("byte[]", $"{where}の `{type}`");
                mapped.IsUnboundedBinary.Should().BeFalse($"{where}の除外しない `{type}`");
            }
        }

        if (!Aligned(english, japanese))
        {
            return;
        }

        for (var i = 0; i < Providers.Length; i++)
        {
            var where = $"{marker} の {Providers[i].DisplayName} の行（日）";

            for (var c = 1; c < 3; c++)
            {
                CodeSpans(japanese.Rows[i][c])
                    .Should()
                    .BeEquivalentTo(
                        CodeSpans(english.Rows[i][c]),
                        $"{where}の {c + 1} 列目（英と同じ）"
                    );
            }
        }
    }

    [Fact(
        DisplayName = "宣言: DB が採番する列の表は方言の宣言・扱いの表・方言切替の判定と、取込のコードに一致する"
    )]
    public void DatabaseNumberingTable_MatchesDeclarations()
    {
        const string marker = "database-numbering";
        var english = ReadTable(Tables[marker].File, marker);
        var japanese = ReadTable(JapaneseFile(Tables[marker].File), marker);
        var languages = Aligned(english, japanese)
            ? new[] { (english, false), (japanese, true) }
            : new[] { (english, false) };

        foreach (var (table, ja) in languages)
        {
            table.Rows.Should().HaveCount(Providers.Length, marker);

            for (var i = 0; i < Providers.Length; i++)
            {
                var provider = Providers[i];
                var row = table.Rows[i];
                var where = $"{marker} の {provider.DisplayName} の行（{(ja ? "日" : "英")}）";

                row.Should().HaveCount(4, where);

                // 値を拒む採番：持てるか（宣言と方言切替の判定）・取込が印を付けるか
                var refuses = DialectDeclarations.CanOccur(
                    provider.Name,
                    UnsupportedDefinitionKind.IdentityRejectingValues
                );

                IdentityColumnMarks
                    .CanBeHeldBy(provider.Name)
                    .Should()
                    .Be(refuses, where + "の方言切替の判定");
                Symbol(row[1]).Should().Be(refuses ? Yes : No, where + "の値を拒む採番");

                if (refuses)
                {
                    UnsupportedTreatments
                        .For(
                            UnsupportedDefinitionKind.IdentityRejectingValues,
                            UnsupportedFeature.Import,
                            provider.Name
                        )
                        .Should()
                        .Be(UnsupportedTreatment.MarkWithNotice, where + "の取込");
                    row[1].Should().Contain(ja ? "印を付ける" : "marked", where);
                }
                else
                {
                    row[1].Should().Contain(ja ? "持てない" : "cannot exist", where);
                    row[1].Should().Contain(ja ? "切替" : "switch", where);
                }

                // 値も受け付ける採番：あり得るか・取込が告げて普通の列にするか
                var accepts = DialectDeclarations.CanOccur(
                    provider.Name,
                    UnsupportedDefinitionKind.IdentityAcceptingValues
                );

                Symbol(row[2]).Should().Be(accepts ? Yes : No, where + "の値も受け付ける採番");

                if (accepts)
                {
                    UnsupportedTreatments
                        .For(
                            UnsupportedDefinitionKind.IdentityAcceptingValues,
                            UnsupportedFeature.Import,
                            provider.Name
                        )
                        .Should()
                        .Be(UnsupportedTreatment.Notice, where + "の取込");
                    row[2].Should().Contain(ja ? "普通の列" : "ordinary column", where);
                    row[2].Should().Contain(ja ? "告げる" : "warned", where);
                }
                else
                {
                    row[2].Should().Contain(ja ? "持てない" : "cannot exist", where);
                }

                // 印の列の DDL の注記
                Symbol(row[3])
                    .Should()
                    .Be(
                        UnsupportedTreatments.For(
                            UnsupportedDefinitionKind.IdentityRejectingValues,
                            UnsupportedFeature.Ddl,
                            provider.Name
                        ) == UnsupportedTreatment.Annotation
                            ? Yes
                            : No,
                        where + "の DDL の注記"
                    );

                // 括弧の綴りは取込のコードが告げる綴り（日本語は英語の同じ行と一致）
                for (var c = 1; c < 3; c++)
                {
                    var spans = CodeSpans(row[c]);

                    spans
                        .Should()
                        .HaveCount(
                            Symbol(row[c]) == Yes ? spans.Length : 0,
                            where + "の綴りは ✅ のセルだけに書く"
                        );

                    if (Symbol(row[c]) == Yes)
                    {
                        spans.Should().NotBeEmpty($"{where}の {c + 1} 列目の綴り");
                    }

                    if (ja)
                    {
                        spans
                            .Should()
                            .Equal(
                                CodeSpans(english.Rows[i][c]),
                                $"{where}の {c + 1} 列目（英と同じ）"
                            );
                    }
                    else
                    {
                        var importer = ImporterSource(provider);

                        spans
                            .Should()
                            .OnlyContain(
                                span => importer.Contains(span, StringComparison.Ordinal),
                                $"{where}の {c + 1} 列目の綴りは取込のコードにある"
                            );
                    }
                }
            }
        }
    }

    /// <summary>無制限バイナリの表のセルの型（<c>(n)</c> は数へ置き換え、<c>(MAX)</c> のように括弧だけのコードスパンは同じセルの各型の後ろに付けて試す）</summary>
    private static string[] BinaryTypes(string cell)
    {
        var spans = CodeSpans(cell);
        var suffixes = spans.Where(s => s.StartsWith('(')).ToArray();
        var types = spans
            .Where(s => !s.StartsWith('('))
            .Select(s => s.Replace("(n)", "(16)", StringComparison.Ordinal))
            .ToArray();

        return
        [
            .. types,
            .. types
                .Where(t => !t.Contains('(', StringComparison.Ordinal))
                .SelectMany(t => suffixes.Select(suffix => t + suffix)),
        ];
    }

    /// <summary>1 列の図を作り、方言の型マッパーで写した C# の型を返す</summary>
    private static global::QuickER.CodeGen.CSharp.CSharpTypeInfo MapType(
        IDatabaseProvider provider,
        string dataType
    )
    {
        var column = new Column
        {
            Name = "c",
            DataType = dataType,
            IsNullable = true,
        };
        var diagram = new ErDiagram { TargetDbms = provider.Name };

        diagram.Entities.Add(new Entity { TableName = "t", Columns = { column } });

        return provider.TypeMapper.ResolveColumnTypes(diagram)[column.Id];
    }

    [Fact(DisplayName = "振る舞い: 移行先の上限の表は各方言の TryFormat の境界と一致する")]
    public void TargetLimitsTable_MatchesTryFormatBoundaries()
    {
        const string marker = "target-limits";
        CanonicalTypeKind[] timeKinds =
        [
            CanonicalTypeKind.DateTime,
            CanonicalTypeKind.Time,
            CanonicalTypeKind.DateTimeOffset,
        ];

        foreach (var japanese in new[] { false, true })
        {
            var file = japanese ? JapaneseFile(Tables[marker].File) : Tables[marker].File;
            var table = ReadTable(file, marker);

            table.Rows.Should().NotBeEmpty(file);

            foreach (var row in table.Rows)
            {
                var provider = Providers.Single(p => RowNames(row[0], p));
                var catalog = provider.TypeCatalog;
                var where = $"{file} の {provider.DisplayName} の行";

                row.Should().HaveCount(5, where);

                // 文字列・バイナリ：上限ちょうどは書き出し、1 つ超えると書き出さない
                var groups = LimitGroup.Matches(row[1]);

                groups.Should().NotBeEmpty(where + "の文字列・バイナリ");

                foreach (Match group in groups)
                {
                    var limit = ParseLimit(group.Groups["n"].Value);

                    foreach (var name in CodeSpans(group.Groups["names"].Value))
                    {
                        catalog
                            .TryParse($"{name}({limit})", out var atLimit)
                            .Should()
                            .BeTrue($"{where}：`{name}({limit})` を読める");
                        AssertFormats(catalog, atLimit, true, $"{where}の `{name}` の長さ {limit}");
                        AssertFormats(
                            catalog,
                            atLimit with
                            {
                                Length = limit + 1,
                            },
                            false,
                            $"{where}の `{name}` の長さ {limit + 1}"
                        );
                    }
                }

                // decimal の精度
                var precision = ParseLimit(LimitNumber.Matches(row[2]).Single().Value);

                AssertFormats(catalog, Decimal(precision, 0), true, where + "の精度");
                AssertFormats(catalog, Decimal(precision + 1, 0), false, where + "の精度 + 1");

                // decimal のスケール：下限と上限（上限が精度のときは、精度を超えるスケールを書き出さない）
                var scales = LimitNumber.Matches(row[3]).Select(m => ParseLimit(m.Value)).ToArray();
                var toPrecision = row[3]
                    .Contains(japanese ? "精度" : "precision", StringComparison.Ordinal);

                scales.Should().HaveCount(toPrecision ? 1 : 2, where + "のスケール");

                var lower = scales[0];
                var upper = toPrecision ? precision : scales[1];

                AssertFormats(catalog, Decimal(precision, lower), true, where + "のスケールの下限");
                AssertFormats(
                    catalog,
                    Decimal(precision, lower - 1),
                    false,
                    where + "のスケールの下限 - 1"
                );
                AssertFormats(catalog, Decimal(precision, upper), true, where + "のスケールの上限");
                AssertFormats(
                    catalog,
                    toPrecision ? Decimal(precision - 1, upper) : Decimal(precision, upper + 1),
                    false,
                    where + "のスケールの上限を超える"
                );

                // 小数秒：その方言が書き出せる時刻系の種類ごと
                var fraction = ParseLimit(LimitNumber.Matches(row[4]).Single().Value);
                var formatted = timeKinds
                    .Where(kind => catalog.TryFormat(new CanonicalType(kind), out _))
                    .ToArray();

                formatted.Should().NotBeEmpty(where + "の時刻系の型");

                foreach (var kind in formatted)
                {
                    AssertFormats(
                        catalog,
                        new CanonicalType(kind, Precision: fraction),
                        true,
                        $"{where}の {kind} の小数秒"
                    );
                    AssertFormats(
                        catalog,
                        new CanonicalType(kind, Precision: fraction + 1),
                        false,
                        $"{where}の {kind} の小数秒 + 1"
                    );
                }
            }
        }
    }

    [Fact(
        DisplayName = "振る舞い: MySQL の整数の表は MySQL の TryParse と SQL Server への変換と一致する"
    )]
    public void MySqlIntegersTable_MatchesTypeConversion()
    {
        const string marker = "mysql-integers";
        var mySql = Providers.Single(p => p.Name == DialectNames.MySql);
        var sqlServer = Providers.Single(p => p.Name == DialectNames.SqlServer);
        var english = ReadTable(Tables[marker].File, marker);
        var japanese = ReadTable(JapaneseFile(Tables[marker].File), marker);

        foreach (var (table, ja) in new[] { (english, false), (japanese, true) })
        {
            table.Header.Should().HaveCount(3);
            table.Header[0].Should().Be(mySql.DisplayName, marker + "の 1 列目の見出し");
            table.Header[2].Should().Be(sqlServer.DisplayName, marker + "の 3 列目の見出し");
            table.Rows.Should().NotBeEmpty(marker);

            foreach (var row in table.Rows)
            {
                var where = $"{marker} の「{row[0]}」{(ja ? "（日）" : "")}";
                var types = CodeSpans(row[0]);
                var target = CodeSpans(row[2]);

                row.Should().HaveCount(3, where);
                types.Should().NotBeEmpty(where);
                target.Should().ContainSingle(where + "の SQL Server の型");

                foreach (var type in types)
                {
                    mySql
                        .TypeCatalog.TryParse(type, out var canonical)
                        .Should()
                        .BeTrue($"{where}：`{type}` を読める");

                    if (canonical.Kind == CanonicalTypeKind.Decimal)
                    {
                        CodeSpans(row[1])
                            .Should()
                            .Equal(
                                [$"decimal({canonical.Precision},{canonical.Scale})"],
                                $"{where}：`{type}` の中立の型"
                            );
                    }
                    else
                    {
                        IntegerKindWords.Should().ContainKey(canonical.Kind, where);
                        row[1]
                            .Should()
                            .Be(
                                ja
                                    ? IntegerKindWords[canonical.Kind].Japanese
                                    : IntegerKindWords[canonical.Kind].English,
                                $"{where}：`{type}` の中立の型"
                            );
                    }

                    Convert(mySql, type, sqlServer)
                        .NewType.Should()
                        .Be(target[0], $"{where}：`{type}` の SQL Server の型");
                }
            }
        }

        japanese.Rows.Should().HaveCount(english.Rows.Count, marker + "の行数");

        for (var r = 0; r < english.Rows.Count; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                CodeSpans(japanese.Rows[r][c])
                    .Should()
                    .Equal(
                        CodeSpans(english.Rows[r][c]),
                        $"{marker} の {r + 1} 行目 {c + 1} 列目（日）"
                    );
            }
        }
    }

    /// <summary>行見出し（方言の表示名で始まる）がその方言を指すか</summary>
    private static bool RowNames(string cell, IDatabaseProvider provider) =>
        cell.StartsWith(provider.DisplayName, StringComparison.Ordinal);

    /// <summary>T-N の行見出しの見本（方言の表示名の後ろのコードスパンが、その方言の型）</summary>
    private static IReadOnlyList<(IDatabaseProvider Source, string Type)> Samples(string label)
    {
        var names = Providers
            .SelectMany(p =>
                Regex
                    .Matches(label, Regex.Escape(p.DisplayName), RegexOptions.CultureInvariant)
                    .Select(m => (Provider: p, m.Index, End: m.Index + m.Length))
            )
            .OrderBy(x => x.Index)
            .ToArray();
        var samples = new List<(IDatabaseProvider, string)>();

        for (var k = 0; k < names.Length; k++)
        {
            var end = k + 1 < names.Length ? names[k + 1].Index : label.Length;
            var segment = label[names[k].End..end];

            samples.AddRange(CodeSpans(segment).Select(type => (names[k].Provider, type)));
        }

        return samples;
    }

    /// <summary>
    /// 1 列の図を <see cref="DiagramTypeConverter.CreatePlan"/> へ通す（GUI の方言切替と同じ）。同じ方言はそのまま残る。
    /// 列は NOT NULL で主キーでない（行バージョンの NOT NULL の解除を見るため）
    /// </summary>
    private static (
        bool Converted,
        string? NewType,
        bool MakeNullable,
        bool PrecisionCapped
    ) Convert(IDatabaseProvider source, string type, IDatabaseProvider target)
    {
        if (ReferenceEquals(source, target))
        {
            return (true, type, false, false);
        }

        var entity = new Entity { TableName = "t" };

        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        entity.Columns.Add(
            new Column
            {
                Name = "c",
                DataType = type,
                IsNullable = false,
            }
        );

        var diagram = new ErDiagram { TargetDbms = source.Name };

        diagram.Entities.Add(entity);

        var plan = DiagramTypeConverter.CreatePlan(diagram, source.TypeCatalog, target.TypeCatalog);
        var converted = plan.Converted.SingleOrDefault(x => x.ColumnName == "c");

        return converted is null
            ? (false, null, false, false)
            : (true, converted.NewType, converted.MakeNullable, converted.PrecisionCapped);
    }

    private static CanonicalType Decimal(int precision, int scale) =>
        new(CanonicalTypeKind.Decimal, Precision: precision, Scale: scale);

    /// <summary>上限の数を読む（桁区切りのカンマを外す・符号つき）</summary>
    private static int ParseLimit(string text) =>
        int.Parse(
            text.Replace(",", "", StringComparison.Ordinal),
            System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture
        );

    private static void AssertFormats(
        ITypeCatalog catalog,
        CanonicalType canonical,
        bool expected,
        string because
    ) => catalog.TryFormat(canonical, out _).Should().Be(expected, $"{because}（{canonical}）");

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
