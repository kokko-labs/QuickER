using AwesomeAssertions;
using QuickER.Provider.MySql;

namespace QuickER.Tests.Provider.MySql;

/// <summary>
/// <see cref="MySqlTypeNotation.Normalize"/>（型表記を取込が返す <c>COLUMN_TYPE</c> の表記へ寄せる）の規則と、
/// MySQL の同期ケーパビリティの配線を検証するテストクラス
/// </summary>
/// <remarks>
/// 実 DB で「選択肢と TryFormat の全表記が取込と一致する」ことは
/// <c>MySqlTypeNotationIntegrationTests</c>（QuickER.Tests.Integration）が固定する。
/// </remarks>
public class MySqlTypeNotationTests
{
    /// <summary>decimal の既定値の補いと numeric の別名だけを行い、それ以外は前後の空白を落とすだけで変えないことを検証する</summary>
    [Theory(DisplayName = "Normalize: 取込が返す表記へ寄せる")]
    [InlineData("decimal", "decimal(10,0)")]
    [InlineData("DECIMAL", "decimal(10,0)")]
    [InlineData("decimal(12)", "decimal(12,0)")]
    [InlineData("decimal( 12 , 3 )", "decimal(12,3)")]
    [InlineData("numeric(18,2)", "decimal(18,2)")]
    [InlineData("numeric", "decimal(10,0)")]
    [InlineData("decimal(10,2) unsigned", "decimal(10,2) unsigned")]
    [InlineData("decimal(10,0)", "decimal(10,0)")]
    [InlineData("tinyint(1)", "tinyint(1)")]
    [InlineData(" varchar(255) ", "varchar(255)")]
    [InlineData("datetime(6)", "datetime(6)")]
    public void Normalize_Cases(string input, string expected)
    {
        MySqlTypeNotation.Normalize(input).Should().Be(expected);
    }

    /// <summary>正規化は冪等で、取込の出力（COLUMN_TYPE の実際の形）は不動点であることを検証する</summary>
    [Theory(DisplayName = "Normalize: 冪等で、取込の出力は不動点")]
    [InlineData("decimal(10,0)")]
    [InlineData("decimal(12,3) unsigned")]
    [InlineData("int")]
    [InlineData("varchar(50)")]
    [InlineData("enum('a','b')")]
    public void Normalize_IsIdempotentAndImportIsFixedPoint(string columnType)
    {
        MySqlTypeNotation.Normalize(columnType).Should().Be(columnType);
    }

    /// <summary>MySQL の同期ケーパビリティがこの正規化を差分の比較へ配線していることを検証する</summary>
    [Fact(DisplayName = "MySQL の capabilities は正規化を配線している")]
    public void Provider_WiresNormalization()
    {
        var capabilities = new MySqlProvider().SyncCapabilities;

        capabilities.AreSameType("decimal", "decimal(10,0)").Should().BeTrue();
        capabilities.AreSameType("decimal(12)", "decimal(12,0)").Should().BeTrue();
        capabilities.AreSameType("decimal(12)", "decimal(12,2)").Should().BeFalse();
    }
}
