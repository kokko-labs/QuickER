using AwesomeAssertions;
using QuickER.Provider.Oracle;
using Xunit;

namespace QuickER.Tests.Provider.Oracle;

/// <summary>
/// <see cref="OracleSchemaImporter.FormatDataType"/>（<c>user_tab_columns</c> の列から型表記を組み立てる）の単体テスト。
/// </summary>
public class OracleSchemaImporterTests
{
    [Theory(DisplayName = "FormatDataType: NUMBER の精度・スケールを宣言どおりに書き出す")]
    [InlineData(10, 2, "NUMBER(10,2)")]
    // スケール 0 と未指定は user_tab_columns 上で区別できないため、どちらも精度のみ
    [InlineData(10, 0, "NUMBER(10)")]
    [InlineData(10, null, "NUMBER(10)")]
    // 負のスケール（100 の倍数へ丸める）は落とさない。落とすと NUMBER(10) ＝ Int32 に化ける
    [InlineData(10, -2, "NUMBER(10,-2)")]
    [InlineData(5, -1, "NUMBER(5,-1)")]
    public void FormatDataType_Number(int? precision, int? scale, string expected)
    {
        OracleSchemaImporter.FormatDataType("NUMBER", precision, scale, 0).Should().Be(expected);
    }

    [Fact(DisplayName = "FormatDataType: 精度もスケールも無い NUMBER は型名のみ")]
    public void FormatDataType_NumberWithoutPrecision()
    {
        OracleSchemaImporter.FormatDataType("NUMBER", null, null, 0).Should().Be("NUMBER");
    }

    [Theory(DisplayName = "FormatDataType: NUMBER(*,s) はスケールを落とさない")]
    // data_precision が null・data_scale が非 null ＝ NUMBER(*,s) の宣言。
    // 精度が無いからと早期 return するとスケールが消えて素の NUMBER に化ける
    [InlineData(2, "NUMBER(*,2)")]
    [InlineData(-2, "NUMBER(*,-2)")]
    // スケール 0 は「未指定」と区別できないため従来どおり型名のみ
    [InlineData(0, "NUMBER")]
    public void FormatDataType_NumberWithScaleOnly(int scale, string expected)
    {
        OracleSchemaImporter.FormatDataType("NUMBER", null, scale, 0).Should().Be(expected);
    }

    [Theory(DisplayName = "FormatDataType: VARCHAR2 / CHAR は文字単位のときだけ単位語を付ける")]
    // 既定のバイト単位（char_used = 'B'）は従来どおり単位語なし
    [InlineData("VARCHAR2", "B", "VARCHAR2(50)")]
    [InlineData("CHAR", "B", "CHAR(50)")]
    // 文字単位（char_used = 'C'）はマルチバイト文字セットで意味が変わるため単位語まで持ち帰る
    [InlineData("VARCHAR2", "C", "VARCHAR2(50 CHAR)")]
    [InlineData("CHAR", "C", "CHAR(50 CHAR)")]
    // char_used が取れない場合は従来どおり
    [InlineData("VARCHAR2", null, "VARCHAR2(50)")]
    public void FormatDataType_CharUnit(string dataType, string? charUsed, string expected)
    {
        OracleSchemaImporter
            .FormatDataType(dataType, null, null, 50, 50, charUsed)
            .Should()
            .Be(expected);
    }

    [Theory(
        DisplayName = "FormatDataType: N 系は常に文字単位だが Oracle の構文が単位語を受けないため付けない"
    )]
    [InlineData("NVARCHAR2")]
    [InlineData("NCHAR")]
    public void FormatDataType_NationalCharTypes_NeverCarryUnit(string dataType)
    {
        OracleSchemaImporter
            .FormatDataType(dataType, null, null, 20, 40, "C")
            .Should()
            .Be($"{dataType}(20)");
    }

    [Theory(
        DisplayName = "FormatDataType: TIMESTAMP 系は data_type が既に精度・修飾を持つためそのまま"
    )]
    [InlineData("TIMESTAMP(3)")]
    [InlineData("TIMESTAMP(6) WITH TIME ZONE")]
    [InlineData("TIMESTAMP(6) WITH LOCAL TIME ZONE")]
    public void FormatDataType_Timestamp_PassesThrough(string dataType)
    {
        OracleSchemaImporter.FormatDataType(dataType, null, 6, 0).Should().Be(dataType);
    }

    [Fact(
        DisplayName = "FormatDataType: RAW はバイト長 data_length を使う（char_length は常に 0）"
    )]
    public void FormatDataType_Raw_UsesDataLength()
    {
        OracleSchemaImporter.FormatDataType("RAW", null, null, 0, 16).Should().Be("RAW(16)");
    }
}
