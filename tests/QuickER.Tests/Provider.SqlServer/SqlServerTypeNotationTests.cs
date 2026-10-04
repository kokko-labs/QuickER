using AwesomeAssertions;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Provider.SqlServer;

/// <summary>
/// <see cref="SqlServerTypeNotation.Normalize"/>（型表記を取込が返す表記へ寄せる）の規則を検証するテストクラス
/// </summary>
/// <remarks>
/// 実 DB で「選択肢と TryFormat の全表記が取込と一致する」ことは
/// <c>SqlServerTypeNotationIntegrationTests</c>（QuickER.Tests.Integration）が固定する。ここは規則の表と、
/// 取込の出力が不動点であること・冪等であることを DB なしで固定する。
/// </remarks>
public class SqlServerTypeNotationTests
{
    /// <summary>既定値の省略と別名の置き換えだけを行い、それ以外は前後の空白を落とすだけで変えないことを検証する</summary>
    [Theory(DisplayName = "Normalize: 取込が返す表記へ寄せる")]
    [InlineData("datetime2(7)", "datetime2")]
    [InlineData("DATETIME2 ( 7 )", "datetime2")]
    [InlineData("time(7)", "time")]
    [InlineData("datetimeoffset(7)", "datetimeoffset")]
    [InlineData("datetime2(3)", "datetime2(3)")]
    [InlineData("time(0)", "time(0)")]
    [InlineData("decimal", "decimal(18)")]
    [InlineData("numeric", "numeric(18)")]
    [InlineData("numeric(18,0)", "numeric(18)")]
    [InlineData("decimal(10, 0)", "decimal(10)")]
    [InlineData("decimal(10)", "decimal(10)")]
    [InlineData("decimal(10,2)", "decimal(10,2)")]
    [InlineData("rowversion", "timestamp")]
    [InlineData("timestamp", "timestamp")]
    [InlineData("datetime2", "datetime2")]
    [InlineData(" nvarchar(50) ", "nvarchar(50)")]
    [InlineData("int", "int")]
    [InlineData("", "")]
    public void Normalize_Cases(string input, string expected)
    {
        SqlServerTypeNotation.Normalize(input).Should().Be(expected);
    }

    /// <summary>正規化は冪等（2 回通しても 1 回と同じ）であることを検証する</summary>
    [Theory(DisplayName = "Normalize: 冪等")]
    [InlineData("numeric(18,0)")]
    [InlineData("decimal")]
    [InlineData("datetime2(7)")]
    [InlineData("datetimeoffset(3)")]
    [InlineData("rowversion")]
    [InlineData("geography")]
    public void Normalize_IsIdempotent(string input)
    {
        var once = SqlServerTypeNotation.Normalize(input);

        SqlServerTypeNotation.Normalize(once).Should().Be(once);
    }

    /// <summary>取込（<see cref="SqlServerSchemaImporter.FormatDataType"/>）の出力は正規化しても変わらない（不動点）ことを検証する</summary>
    [Theory(DisplayName = "Normalize: 取込の出力は不動点")]
    [InlineData("decimal", null, 18, 0, null)]
    [InlineData("numeric", null, 10, 2, null)]
    [InlineData("datetime2", null, null, null, 3)]
    [InlineData("datetime2", null, null, null, 7)]
    [InlineData("time", null, null, null, 0)]
    [InlineData("nvarchar", -1, null, null, null)]
    [InlineData("timestamp", null, null, null, null)]
    public void Normalize_ImporterOutputIsFixedPoint(
        string type,
        int? maxLen,
        int? precision,
        int? scale,
        int? datetimePrecision
    )
    {
        var imported = SqlServerSchemaImporter.FormatDataType(
            type,
            maxLen,
            precision,
            scale,
            datetimePrecision
        );

        SqlServerTypeNotation.Normalize(imported).Should().Be(imported);
    }

    /// <summary>SQL Server の同期ケーパビリティがこの正規化を差分の比較へ配線していることを検証する</summary>
    [Fact(DisplayName = "SQL Server の capabilities は正規化を配線している")]
    public void Provider_WiresNormalization()
    {
        new SqlServerProvider()
            .SyncCapabilities.AreSameType("numeric(18,0)", "numeric(18)")
            .Should()
            .BeTrue();
    }
}
