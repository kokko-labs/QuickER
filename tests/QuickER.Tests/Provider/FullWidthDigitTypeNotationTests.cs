using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Services;

namespace QuickER.Tests.Provider;

/// <summary>
/// 型の表記などの数字に全角などの Unicode の数字（<c>５０</c>）が入った入力を、どの入口も例外にせず「数字ではない」として扱うことを固定する
/// </summary>
/// <remarks>
/// 正規表現の数字は <c>[0-9]</c> で書く（<see cref="QuickER.Tests.Docs.RegexCultureGuardTests"/>）。<c>\d</c> のままだと、
/// Unicode の数字に一致したのにインバリアントの数値の解析が読めず、SQL Server の型カタログ・型マッパー・型の表記の正規化が
/// <see cref="FormatException"/> で落ち、<see cref="SqlTypeText.IsSafe"/> は DB が断る表記を安全とみなして DDL へ通していた。
/// </remarks>
public sealed class FullWidthDigitTypeNotationTests
{
    private const string Fifty = "５０";
    private const string Ten = "１０";
    private const string Two = "２";
    private const string Three = "３";

    public static TheoryData<string, string> TypesByDialect() =>
        new()
        {
            { "sqlserver", $"nvarchar({Fifty})" },
            { "sqlserver", $"decimal({Ten},{Two})" },
            { "sqlserver", $"datetime2({Three})" },
            { "sqlserver", $"float({Two}4)" },
            { "mysql", $"varchar({Fifty})" },
            { "mysql", $"decimal({Ten},{Two})" },
            { "mysql", $"decimal({Ten})" },
            { "oracle", $"VARCHAR2({Fifty} CHAR)" },
            { "oracle", $"NUMBER({Ten},{Two})" },
            { "oracle", $"TIMESTAMP({Three})" },
            { "postgresql", $"character varying({Fifty})" },
            { "postgresql", $"numeric({Ten},0)" },
            { "postgresql", $"time({Three}) with time zone" },
            { "sqlite", $"VARCHAR({Fifty})" },
            { "sqlite", $"DECIMAL({Ten},{Two})" },
        };

    [Theory(
        DisplayName = "全角の数字を含む型の表記は、型カタログが解析せず・型マッパーが長さと精度を読まず・正規化が書き換えず・安全な表記とみなさない"
    )]
    [MemberData(nameof(TypesByDialect))]
    public void FullWidthDigits_AreNotDigits(string dialect, string type)
    {
        var (catalog, map, normalize) = Dialect(dialect);

        catalog.TryParse(type, out _).Should().BeFalse();
        var info = map(type);
        info.MaxLength.Should().BeNull();
        info.Precision.Should().BeNull();
        info.Scale.Should().BeNull();
        normalize(type).Should().Be(type);
        SqlTypeText.IsSafe(type).Should().BeFalse("DB は全角の数字の長さ・精度を読めない");
    }

    [Fact(
        DisplayName = "全角の数字は、型トークン・長さだけの違いの判定・Mermaid の型の復元・Excel の UQ 番号でも数字として読まない"
    )]
    public void FullWidthDigits_AreNotDigits_InOtherParsers()
    {
        CanonicalTypeToken.TryParse($"string({Fifty})", out _).Should().BeFalse();
        SyncDialectCapabilities
            .DiffersOnlyInStringLength($"varchar({Fifty})", "varchar(10)")
            .Should()
            .BeFalse();
        MermaidImporter.DenormalizeDataType($"varchar_{Fifty}").Should().Be($"varchar_{Fifty}");
        TableDefinitionDocumentImporter.ParseUniqueConstraintNumbers("UQ１").Should().BeEmpty();
        TableDefinitionDocumentImporter
            .ParseUniqueConstraintNumbers($"UQ1{Two}")
            .Should()
            .BeEmpty("全角の数字が続くラベルを途中までの番号として読まない");
        TableDefinitionDocumentImporter
            .ParseUniqueConstraintNumbers("PK/UQ1,UQ12")
            .Should()
            .Equal(1, 12);
    }

    private static (
        ITypeCatalog Catalog,
        Func<string, CSharpTypeInfo> Map,
        Func<string, string> Normalize
    ) Dialect(string dialect) =>
        dialect switch
        {
            "sqlserver" => (
                new SqlServerProvider().TypeCatalog,
                new SqlServerCSharpTypeMapper().Map,
                SqlServerTypeNotation.Normalize
            ),
            "mysql" => (
                new MySqlProvider().TypeCatalog,
                new MySqlCSharpTypeMapper().Map,
                MySqlTypeNotation.Normalize
            ),
            "oracle" => (
                new OracleProvider().TypeCatalog,
                new OracleCSharpTypeMapper().Map,
                OracleTypeNotation.Normalize
            ),
            "postgresql" => (
                new PostgreSqlProvider().TypeCatalog,
                new PostgreSqlCSharpTypeMapper().Map,
                PostgreSqlTypeNotation.Normalize
            ),
            "sqlite" => (
                new SqliteProvider().TypeCatalog,
                new SqliteCSharpTypeMapper().Map,
                type => type
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null),
        };
}
