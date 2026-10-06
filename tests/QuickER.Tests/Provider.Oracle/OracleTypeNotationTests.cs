using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider.Oracle;

namespace QuickER.Tests.Provider.Oracle;

/// <summary>
/// <see cref="OracleTypeNotation.Normalize"/>（型表記を取込が返す表記へ寄せる）の規則と、Oracle の同期
/// ケーパビリティの配線を検証するテストクラス
/// </summary>
/// <remarks>
/// 実 DB で「選択肢と TryFormat の全表記が取込と一致する」ことは
/// <c>OracleTypeNotationIntegrationTests</c>（QuickER.Tests.Integration）が固定する。
/// </remarks>
public class OracleTypeNotationTests
{
    /// <summary>既定値の補いと別名の置き換えだけを行い、それ以外は前後の空白を落とすだけで変えないことを検証する</summary>
    [Theory(DisplayName = "Normalize: 取込が返す表記へ寄せる")]
    [InlineData("TIMESTAMP", "TIMESTAMP(6)")]
    [InlineData("timestamp", "TIMESTAMP(6)")]
    [InlineData("TIMESTAMP WITH TIME ZONE", "TIMESTAMP(6) WITH TIME ZONE")]
    [InlineData("TIMESTAMP  WITH  LOCAL  TIME  ZONE", "TIMESTAMP(6) WITH LOCAL TIME ZONE")]
    [InlineData("TIMESTAMP(3)", "TIMESTAMP(3)")]
    [InlineData("TIMESTAMP (9) WITH TIME ZONE", "TIMESTAMP(9) WITH TIME ZONE")]
    [InlineData("NUMBER(10,0)", "NUMBER(10)")]
    [InlineData("number(10, 2)", "NUMBER(10,2)")]
    [InlineData("NUMBER(10,-2)", "NUMBER(10,-2)")]
    [InlineData("NUMBER(*)", "NUMBER")]
    [InlineData("NUMBER(*,0)", "NUMBER(*,0)")]
    [InlineData("NUMBER(*,2)", "NUMBER(*,2)")]
    [InlineData("NUMBER", "NUMBER")]
    [InlineData("INTEGER", "NUMBER(*,0)")]
    [InlineData("int", "NUMBER(*,0)")]
    [InlineData("SMALLINT", "NUMBER(*,0)")]
    [InlineData("FLOAT", "FLOAT(126)")]
    [InlineData("FLOAT(63)", "FLOAT(63)")]
    [InlineData(" VARCHAR2(50) ", "VARCHAR2(50)")]
    [InlineData("DATE", "DATE")]
    public void Normalize_Cases(string input, string expected)
    {
        OracleTypeNotation.Normalize(input).Should().Be(expected);
    }

    /// <summary>正規化は冪等（2 回通しても 1 回と同じ）であることを検証する</summary>
    [Theory(DisplayName = "Normalize: 冪等")]
    [InlineData("TIMESTAMP WITH TIME ZONE")]
    [InlineData("NUMBER(10,0)")]
    [InlineData("INTEGER")]
    [InlineData("NUMBER(*)")]
    [InlineData("FLOAT")]
    [InlineData("XMLTYPE")]
    public void Normalize_IsIdempotent(string input)
    {
        var once = OracleTypeNotation.Normalize(input);

        OracleTypeNotation.Normalize(once).Should().Be(once);
    }

    /// <summary>取込（<see cref="OracleSchemaImporter.FormatDataType"/>）の出力は正規化しても変わらない（不動点）ことを検証する</summary>
    [Theory(DisplayName = "Normalize: 取込の出力は不動点")]
    [InlineData("NUMBER", 10, 2)]
    [InlineData("NUMBER", 10, 0)]
    [InlineData("NUMBER", null, 0)]
    [InlineData("NUMBER", null, 2)]
    [InlineData("NUMBER", null, null)]
    [InlineData("FLOAT", 126, null)]
    [InlineData("TIMESTAMP(6) WITH TIME ZONE", null, 6)]
    [InlineData("TIMESTAMP(3) WITH LOCAL TIME ZONE", null, 3)]
    public void Normalize_ImporterOutputIsFixedPoint(string type, int? precision, int? scale)
    {
        var imported = OracleSchemaImporter.FormatDataType(type, precision, scale, 0);

        OracleTypeNotation.Normalize(imported).Should().Be(imported);
    }

    /// <summary>
    /// Oracle の同期ケーパビリティが、型表記の正規化と、Oracle が持たない参照アクション（ON UPDATE・
    /// ON DELETE SET DEFAULT）の畳み込みを配線していることを検証する。
    /// </summary>
    [Fact(DisplayName = "Oracle の capabilities は正規化と参照アクションの畳み込みを配線している")]
    public void Provider_WiresNormalizationAndActionFolding()
    {
        var capabilities = new OracleProvider().SyncCapabilities;

        capabilities.AreSameType("TIMESTAMP", "TIMESTAMP(6)").Should().BeTrue();
        capabilities
            .EffectiveOnUpdate(ForeignKeyReferentialAction.Cascade)
            .Should()
            .Be(ForeignKeyReferentialAction.NoAction);
        capabilities
            .EffectiveOnDelete(ForeignKeyReferentialAction.SetDefault)
            .Should()
            .Be(ForeignKeyReferentialAction.NoAction);
        capabilities
            .EffectiveOnDelete(ForeignKeyReferentialAction.Cascade)
            .Should()
            .Be(ForeignKeyReferentialAction.Cascade);
    }
}
