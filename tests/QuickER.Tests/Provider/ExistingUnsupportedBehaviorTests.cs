using System.IO;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider;

/// <summary>
/// 扱いの表のうち、今の挙動なのにテストが無かったマス（D2b の取込・B7c の DDL・N1 の差分）を固定する
/// </summary>
public sealed class ExistingUnsupportedBehaviorTests
{
    [Fact(
        DisplayName = "D2b 取込：DDL へ出せない型表記の列を、表・列・表記つきで告げる（方言に依らない検査）"
    )]
    public void DetectUnemittableColumnTypes_NamesUnsafeTypes()
    {
        var entity = new Entity
        {
            TableName = "t",
            Columns =
            {
                new Column { Name = "ok", DataType = "int" },
                new Column { Name = "bad", DataType = "weird;type" },
            },
        };

        var warnings = SchemaImportWarnings.DetectUnemittableColumnTypes([entity]).ToList();

        warnings
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new SchemaImportWarning(
                    SchemaImportWarningKind.ColumnTypeNotEmittable,
                    "t",
                    "bad",
                    "weird;type"
                )
            );
    }

    [Fact(
        DisplayName = "D2b 取込：5 方言の取込が、取込の最後に DDL へ出せない型表記の検査を掛ける"
    )]
    public void AllImporters_CallDetectUnemittableColumnTypes()
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
            .Where(importer =>
                !File.ReadAllText(Path.Combine(src, importer))
                    .Contains(
                        "SchemaImportWarnings.DetectUnemittableColumnTypes(",
                        StringComparison.Ordinal
                    )
            )
            .Should()
            .BeEmpty(
                "どれか 1 方言で掛け忘れると、その方言だけ DDL・同期を叩いたときに遅れて露見する"
            );
    }
}
