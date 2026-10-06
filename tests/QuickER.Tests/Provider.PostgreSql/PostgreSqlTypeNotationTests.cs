using AwesomeAssertions;
using QuickER.Provider.PostgreSql;

namespace QuickER.Tests.Provider.PostgreSql;

/// <summary>
/// <see cref="PostgreSqlTypeNotation.Normalize"/> が、図・型の選択肢・<c>TryFormat</c> が書く短縮語彙も
/// 取込（<c>format_type()</c> の正規化）と同じ表記へ寄せることを検証するテストクラス
/// </summary>
/// <remarks>
/// <c>format_type()</c> の正準表記の規則は <c>PostgreSqlSchemaImporterTests</c>（取込の入口）が固定する。
/// 実 DB で「選択肢と TryFormat の全表記が取込と一致する」ことは
/// <c>PostgreSqlTypeNotationIntegrationTests</c>（QuickER.Tests.Integration）が固定する。
/// </remarks>
public class PostgreSqlTypeNotationTests
{
    /// <summary>短縮語彙の既定精度の補いとスケール 0 の畳み込みを検証する（取込はどちらも補った形で返す）</summary>
    [Theory(DisplayName = "Normalize: 図の短縮語彙を取込が返す表記へ寄せる")]
    [InlineData("timestamp", "timestamp(6)")]
    [InlineData("TIMESTAMP", "timestamp(6)")]
    [InlineData("timestamptz", "timestamptz(6)")]
    [InlineData("time", "time(6)")]
    [InlineData("timestamp(3)", "timestamp(3)")]
    [InlineData("timestamptz ( 0 )", "timestamptz(0)")]
    [InlineData("timestamp[]", "timestamp(6)[]")]
    [InlineData("numeric(18,0)", "numeric(18)")]
    [InlineData("NUMERIC(18, 0)", "numeric(18)")]
    [InlineData("numeric(10,2)", "numeric(10,2)")]
    [InlineData("CHARACTER VARYING(50)", "varchar(50)")]
    [InlineData("time with time zone", "time with time zone")]
    [InlineData(" integer ", "integer")]
    public void Normalize_ShortVocabulary(string input, string expected)
    {
        PostgreSqlTypeNotation.Normalize(input).Should().Be(expected);
    }

    /// <summary>正規化は冪等（2 回通しても 1 回と同じ）であることを検証する</summary>
    [Theory(DisplayName = "Normalize: 冪等")]
    [InlineData("timestamp")]
    [InlineData("timestamp without time zone")]
    [InlineData("numeric(18,0)")]
    [InlineData("character varying(20)[]")]
    [InlineData("time")]
    public void Normalize_IsIdempotent(string input)
    {
        var once = PostgreSqlTypeNotation.Normalize(input);

        PostgreSqlTypeNotation.Normalize(once).Should().Be(once);
    }

    /// <summary>取込（<see cref="PostgreSqlSchemaImporter.NormalizeFormatType"/>）がこの関数へ委ねていることを検証する</summary>
    [Theory(DisplayName = "取込の正規化は同じ関数を通す")]
    [InlineData("timestamp without time zone")]
    [InlineData("numeric(18,0)")]
    [InlineData("character varying(50)")]
    public void Importer_DelegatesToNotation(string formatType)
    {
        PostgreSqlSchemaImporter
            .NormalizeFormatType(formatType)
            .Should()
            .Be(PostgreSqlTypeNotation.Normalize(formatType));
    }

    /// <summary>PostgreSQL の同期ケーパビリティがこの正規化を差分の比較へ配線していることを検証する</summary>
    [Fact(DisplayName = "PostgreSQL の capabilities は正規化を配線している")]
    public void Provider_WiresNormalization()
    {
        var capabilities = new PostgreSqlProvider().SyncCapabilities;

        capabilities.AreSameType("timestamp", "timestamp(6)").Should().BeTrue();
        capabilities.AreSameType("numeric(18,0)", "numeric(18)").Should().BeTrue();
        capabilities.AreSameType("timestamp", "timestamp(3)").Should().BeFalse();
    }
}
