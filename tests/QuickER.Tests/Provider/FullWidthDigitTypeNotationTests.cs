using AwesomeAssertions;
using QuickER.Provider;
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
    private const string Two = "２";

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
}
