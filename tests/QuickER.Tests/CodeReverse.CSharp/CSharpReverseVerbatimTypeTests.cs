using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using CodeGenStrings = QuickER.CodeGen.CSharp.Resources.Strings;
using ReverseStrings = QuickER.CodeReverse.CSharp.Resources.Strings;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// 型往復の非可逆（<c>datetime</c> → <c>datetime2</c> 等）を、生成側の verbatim 出力で閉じることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 方言中立トークンは「意味」だけを運ぶため、同じ意味を持つ複数の表記（<c>numeric</c> と <c>decimal</c>・
/// <c>ntext</c> と <c>nvarchar(max)</c>・<c>datetime</c> と <c>datetime2</c>）は 1 つの代表表記へ畳まれる。
/// 畳まれた列をリバースすると図の型表記が黙って変わり、次の DB 同期が全該当列へ ALTER COLUMN を出す。
/// </para>
/// <para>
/// これを避けるため、生成側は「トークン経由で書き戻すと綴りが変わる列だけ」元の型表記を
/// <c>[DbColumnMeta(..., NativeType = "...")]</c> として追加で刻み、リバース側は
/// 「その表記を対象方言で解析した正規型がトークンの正規型と一致する」ときだけ採用する。
/// </para>
/// </remarks>
public class CSharpReverseVerbatimTypeTests
{
    /// <summary>非可逆な型表記（左）と、トークン経由で書き戻したときの表記（右）</summary>
    public static TheoryData<string, string> NonRoundTrippableTypes =>
        new()
        {
            { "datetime", "datetime2" },
            { "smalldatetime", "datetime2" },
            { "numeric(10,2)", "decimal(10,2)" },
            { "ntext", "nvarchar(max)" },
            { "text", "varchar(max)" },
            { "image", "varbinary(max)" },
            { "smallmoney", "money" },
        };

    /// <summary>型表記 1 つを持つ 1 テーブルの図を作る</summary>
    private static ErDiagram SingleColumnDiagram(string dataType) =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    TableName = "samples",
                    Columns =
                    {
                        new Column
                        {
                            Name = "sample_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "probe",
                            DataType = dataType,
                            IsNullable = true,
                        },
                    },
                },
            },
        };

    private static CodeGenerationOptions Options =>
        new()
        {
            RootNamespace = "QuickER.Tests.ReverseVerbatim",
            GenerateValueObjects = false,
            SplitFilesByCategory = false,
        };

    private static CodeGenerationResult Generate(ErDiagram diagram)
    {
        var provider = new SqlServerProvider();

        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            Options
        );
    }

    /// <summary>非可逆な型表記は、生成 → リバースの往復で元の綴りのまま戻る</summary>
    [Theory(DisplayName = "非可逆な型表記は往復で元の綴りのまま戻る")]
    [MemberData(nameof(NonRoundTrippableTypes))]
    public void RoundTrip_NonRoundTrippableSpelling_IsPreserved(string dataType, string collapsedTo)
    {
        var generation = Generate(SingleColumnDiagram(dataType));
        generation.HasErrors.Should().BeFalse();

        var source = generation.Files.Single().Content;
        // 生成物は「元の表記」を verbatim 引数として持つ（トークンだけでは綴りを復元できないため）
        source.Should().Contain($"NativeType = \"{dataType}\"");

        var reversed = new CSharpReverseParser().Parse(source, new SqlServerTypeCatalog());
        var column = reversed
            .Entities.Single()
            .Columns.Single(candidate => candidate.Name == "probe");

        column.DataType.Should().Be(dataType, $"verbatim が無ければ '{collapsedTo}' へ化ける");
        reversed.Warnings.Should().BeEmpty();
    }

    /// <summary>綴りが変わらない型（代表表記そのもの）には verbatim 引数を出さない＝既存生成物はバイト不変</summary>
    [Fact(DisplayName = "綴りが変わらない型には verbatim 引数を出さない")]
    public void Generate_RoundTrippableSpelling_EmitsNoVerbatimArgument()
    {
        var generation = Generate(SingleColumnDiagram("datetime2"));

        generation.Files.Single().Content.Should().NotContain("NativeType =");
        generation
            .Diagnostics.Should()
            .NotContain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Info
                && diagnostic.Message.StartsWith(
                    CodeGenStrings.CodeGen_Info_NonCanonicalTypeSpellingColumns.Split('{')[0],
                    StringComparison.Ordinal
                )
            );
    }

    /// <summary>
    /// 大小・前後空白だけの違いは「綴りが変わった」と見なさない（同期の型比較＝Trim ＋ 大小無視と同じ規則）。
    /// SQLite 方言は <c>TryFormat</c> が大文字で返すため、ここを厳密一致にすると SQLite 図の全列へ
    /// verbatim 引数が出て既存生成物が総入れ替えになる。
    /// </summary>
    [Fact(DisplayName = "大小差だけの型表記には verbatim 引数を出さない")]
    public void Generate_CaseOnlyDifference_EmitsNoVerbatimArgument()
    {
        var provider = new QuickER.Provider.Sqlite.SqliteProvider();
        var generation = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            SingleColumnDiagram("nvarchar(50)"),
            Options
        );

        generation.Files.Single().Content.Should().NotContain("NativeType =");
    }

    /// <summary>綴りが変わる列は、生成時に Info 診断で名指しされる</summary>
    [Fact(DisplayName = "綴りが変わる列は生成時 Info 診断で名指しされる")]
    public void Generate_NonRoundTrippableSpelling_ReportsInfoDiagnostic()
    {
        var generation = Generate(SingleColumnDiagram("datetime"));

        generation
            .Diagnostics.Should()
            .ContainSingle(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Info
                && diagnostic.Message.Contains("samples.probe", StringComparison.Ordinal)
                && diagnostic.Message.Contains("datetime2", StringComparison.Ordinal)
            );
    }

    /// <summary>
    /// 手編集などで verbatim とトークンの意味が食い違ったら、トークン側を採って警告する
    /// （verbatim を無条件に信じると、型トークンが語る意味と違う型の図ができる）
    /// </summary>
    [Fact(DisplayName = "verbatim とトークンが食い違うときはトークンを採り警告する")]
    public void Parse_VerbatimContradictsToken_PrefersTokenWithWarning()
    {
        const string source = """
            namespace Sample;

            [Table("samples")]
            public partial class SampleEntity
            {
                [Key]
                [Column("sample_id")]
                [DbColumnMeta("int32", NativeType = "nvarchar(10)")]
                public int SampleId { get; set; }
            }
            """;

        var result = new CSharpReverseParser().Parse(source, new SqlServerTypeCatalog());

        result.Entities.Single().Columns.Single().DataType.Should().Be("int");
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                string.Format(
                    ReverseStrings.Reverse_VerbatimTypeMismatch,
                    "nvarchar(10)",
                    "samples",
                    "sample_id",
                    "int32"
                )
            );
    }

    /// <summary>
    /// 対象方言が verbatim を解析できないとき（別方言の図から生成したコードをリバースした場合）も、
    /// トークン側へ倒して警告する＝方言中立トークンによる可搬性は保たれる
    /// </summary>
    [Fact(DisplayName = "対象方言が解析できない verbatim はトークンへ倒して警告する")]
    public void Parse_VerbatimUnknownToTargetDialect_PrefersTokenWithWarning()
    {
        var generation = Generate(SingleColumnDiagram("datetime"));
        var source = generation.Files.Single().Content;

        // SQL Server の 'datetime' を PostgreSQL 方言でリバースする（PostgreSQL に datetime は無い）
        var result = new CSharpReverseParser().Parse(
            source,
            new QuickER.Provider.PostgreSql.PostgreSqlTypeCatalog()
        );

        result
            .Entities.Single()
            .Columns.Single(column => column.Name == "probe")
            .DataType.Should()
            .Be("timestamp");
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                string.Format(
                    ReverseStrings.Reverse_VerbatimTypeMismatch,
                    "datetime",
                    "samples",
                    "probe",
                    "datetime"
                )
            );
    }

    /// <summary>
    /// 行バージョン列は従来どおりトークンを刻まない（＝<c>[DbColumnMeta]</c> ごと出ない）。
    /// verbatim の追加でこの規則を崩すと、リバースが版ガードをただのバイナリ列として復元してしまう。
    /// </summary>
    [Fact(DisplayName = "行バージョン列には型メタ属性を出さない（verbatim も出さない）")]
    public void Generate_RowVersionColumn_EmitsNoDbColumnMeta()
    {
        var diagram = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "samples",
                    Columns =
                    {
                        new Column
                        {
                            Name = "sample_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        // timestamp は rowversion の非推奨別名＝トークン経由では綴りが変わる型だが、
                        // 行バージョン列には元からトークンを刻まないため verbatim も出ない
                        new Column
                        {
                            Name = "row_ver",
                            DataType = "timestamp",
                            IsNullable = false,
                        },
                    },
                },
            },
        };

        var content = Generate(diagram).Files.Single().Content;

        content.Should().Contain("[StoreGeneratedColumn]");
        content.Should().NotContain("NativeType =");
        content.Should().NotContain("[DbColumnMeta(\"rowversion\"");
    }
}
