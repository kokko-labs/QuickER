using System.IO;
using AwesomeAssertions;
using QuickER.Provider;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Provider;

/// <summary>5 方言の取込が共有する取込警告の作り手（<see cref="SchemaImportWarnings"/>）を検証する</summary>
public sealed class SchemaImportWarningsHelperTests
{
    /// <summary>
    /// 値が NULL の既定値は方言ごとのカタログの表記の完全一致で見分け、文字列の 'NULL' を NULL と取り違えない
    /// </summary>
    /// <remarks>
    /// 表記は各方言の取込が持つ一覧（私有の <c>NullDefaultForms</c>）を反射で読む。表記は実機で確かめたもの
    /// （SQL Server の <c>(NULL)</c>・PostgreSQL の <c>NULL::character varying</c>〔text・integer は既定値の行を作らない〕・
    /// 文字列の <c>'NULL'::text</c>）。MySQL は <c>DEFAULT NULL</c> を DB の NULL で返すので、文字列の表記は持たない。
    /// </remarks>
    [Theory(
        DisplayName = "値が NULL の既定値は方言の表記の完全一致で見分け、文字列の 'NULL' と取り違えない"
    )]
    [InlineData(typeof(QuickER.Provider.SqlServer.SqlServerSchemaImporter), false, "(NULL)", true)]
    [InlineData(
        typeof(QuickER.Provider.SqlServer.SqlServerSchemaImporter),
        false,
        "((NULL))",
        true
    )]
    [InlineData(
        typeof(QuickER.Provider.SqlServer.SqlServerSchemaImporter),
        false,
        "('NULL')",
        false
    )]
    [InlineData(
        typeof(QuickER.Provider.SqlServer.SqlServerSchemaImporter),
        false,
        "(N'NULL')",
        false
    )]
    [InlineData(typeof(QuickER.Provider.SqlServer.SqlServerSchemaImporter), false, "NULL", false)]
    [InlineData(
        typeof(QuickER.Provider.PostgreSql.PostgreSqlSchemaImporter),
        true,
        "NULL::character varying",
        true
    )]
    [InlineData(
        typeof(QuickER.Provider.PostgreSql.PostgreSqlSchemaImporter),
        true,
        "'NULL'::text",
        false
    )]
    [InlineData(
        typeof(QuickER.Provider.PostgreSql.PostgreSqlSchemaImporter),
        true,
        "'NULL'::character varying",
        false
    )]
    [InlineData(typeof(QuickER.Provider.Oracle.OracleSchemaImporter), false, "NULL", true)]
    [InlineData(typeof(QuickER.Provider.Oracle.OracleSchemaImporter), false, "null ", true)]
    [InlineData(typeof(QuickER.Provider.Oracle.OracleSchemaImporter), false, "'NULL'", false)]
    [InlineData(typeof(QuickER.Provider.Sqlite.SqliteSchemaImporter), false, "NULL", true)]
    [InlineData(typeof(QuickER.Provider.Sqlite.SqliteSchemaImporter), false, "'NULL'", false)]
    [InlineData(null, false, "NULL", false)]
    [InlineData(null, false, "", false)]
    public void IsNullDefault_DistinguishesNullFromStringNull(
        Type? importer,
        bool typedNullCast,
        string expression,
        bool expected
    )
    {
        // importer が null は MySQL（文字列の表記で NULL とみなすものは無い）
        var forms = importer is null
            ? []
            : (string[])
                importer
                    .GetField(
                        "NullDefaultForms",
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Static
                    )!
                    .GetValue(null)!;

        SchemaImportWarnings.IsNullDefault(expression, forms, typedNullCast).Should().Be(expected);
        SchemaImportWarnings
            .IsNullDefault(null, forms, typedNullCast)
            .Should()
            .BeTrue("DB の NULL は既定値が無い");
    }

    [Fact(
        DisplayName = "前後に空白のある名前は、テーブル・列・制約ごとに DB の綴りと図での名前を告げる"
    )]
    public void DetectTrimmedNames_ReportsTableColumnAndConstraint()
    {
        var entry = new SchemaTableEntry
        {
            Key = " orders",
            Entity = new QuickER.Model.Entity { TableName = " orders" },
        };
        entry.AddColumn(new QuickER.Model.Column { Name = "id " }, "id ");
        entry.AddColumn(new QuickER.Model.Column { Name = "plain" }, "plain");
        entry.PaddedConstraintNames.Add(" fk_x");
        var clean = new SchemaTableEntry
        {
            Key = "lines",
            Entity = new QuickER.Model.Entity { TableName = "lines" },
        };

        SchemaImportWarnings
            .DetectTrimmedNames([entry, clean])
            .Select(w => (w.Kind, w.TableName, w.Subject, w.Detail))
            .Should()
            .Equal(
                (SchemaImportWarningKind.NameTrimmed, "orders", " orders", "orders"),
                (SchemaImportWarningKind.NameTrimmed, "orders", "id ", "id"),
                (SchemaImportWarningKind.NameTrimmed, "orders", " fk_x", "fk_x")
            );
    }

    [Fact(
        DisplayName = "5 方言の取込は、前後に空白のある名前の検出（DetectTrimmedNames）を取込の最後に呼ぶ"
    )]
    public void AllImporters_CallDetectTrimmedNames()
    {
        var src = Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src");
        string[] importers =
        [
            "QuickER.Provider.SqlServer/SqlServerSchemaImporter.cs",
            "QuickER.Provider.PostgreSql/PostgreSqlSchemaImporter.cs",
            "QuickER.Provider.MySql/MySqlSchemaImporter.cs",
            "QuickER.Provider.Oracle/OracleSchemaImporter.cs",
            "QuickER.Provider.Sqlite/SqliteSchemaImporter.cs",
        ];

        importers
            .Where(path =>
                !File.ReadAllText(Path.Combine(src, path))
                    .Contains("SchemaImportWarnings.DetectTrimmedNames(", StringComparison.Ordinal)
            )
            .Should()
            .BeEmpty();
    }
}
