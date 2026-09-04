using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 固定 infra の検証ルールクラス（<c>ValueObjectRules</c> / <c>ValueObjectStringRules</c> /
/// <c>ValueObjectNumberRules</c> / <c>ValueObjectDecimalRules</c>）を、コミット済みフィクスチャ
/// （<c>GeneratedFixture.g.cs</c>）が持つ実体に対して直接検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// 生成 VO の <c>ValidateCore</c> が呼ぶ 3 規則（必須・最大長・decimal 桁数）と、利用者が <c>OnValidate</c> から
/// 呼ぶ汎用ヘルパ 4 種（整数桁数・範囲・ASCII 英数字・メールアドレス）を同じ流儀で押さえる。VO 経由の観測は
/// <see cref="ValueObjectValidationTests"/> が持つため、ここでは規則そのものの境界値を見る。
/// </para>
/// <para>
/// エラーリストは全ルールが「参照渡し・未確保」で受け取り、違反があるときだけ確保する契約なので、
/// 各テストは「通ったときは <c>null</c> のまま」も併せて表明する（成功パスの割り当てゼロの構造的な裏取り）。
/// </para>
/// </remarks>
public sealed class ValueObjectRuleHelperTests
{
    /// <summary>テスト用の表示名（既定メッセージは表示名を文面へ入れないため、素通し確認にだけ使う）</summary>
    private const string Label = "TestValue";

    // ===== ValueObjectRules.ValidateRequired =====

    [Fact(DisplayName = "[必須] null は false＋必須エラー、非 null は true＋エラーなし")]
    public void 必須チェックはnullだけを弾く()
    {
        List<string>? errors = null;

        ValueObjectRules.ValidateRequired<string>(null, Label, ref errors).Should().BeFalse();
        errors.Should().ContainSingle().Which.Should().Be("A value is required.");

        List<string>? passing = null;

        ValueObjectRules.ValidateRequired("value", Label, ref passing).Should().BeTrue();
        passing.Should().BeNull();

        // 空文字は「値がある」＝必須違反ではない（空かどうかは列側の判断）
        List<string>? empty = null;

        ValueObjectRules.ValidateRequired(string.Empty, Label, ref empty).Should().BeTrue();
        empty.Should().BeNull();
    }

    // ===== ValueObjectStringRules.ValidateMaxLength =====

    [Theory(DisplayName = "[最大長] 境界ちょうどは通り、1 文字超過で弾く")]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void 最大長は境界ちょうどまで通す(int length, bool expected)
    {
        List<string>? errors = null;

        ValueObjectStringRules.ValidateMaxLength(new string('a', length), 5, Label, ref errors);

        if (expected)
        {
            errors.Should().BeNull();
        }
        else
        {
            errors
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be("Enter at most 5 characters. (currently 6 characters)");
        }
    }

    // ===== ValueObjectNumberRules.ValidateMaxDigits =====

    [Theory(DisplayName = "[整数桁数] 符号は数えず 0 は 1 桁・境界ちょうどは通る")]
    [InlineData(0L, 1, true)]
    [InlineData(9L, 1, true)]
    [InlineData(10L, 1, false)]
    [InlineData(-9L, 1, true)]
    [InlineData(-10L, 1, false)]
    [InlineData(-12345L, 5, true)]
    [InlineData(99999L, 5, true)]
    [InlineData(100000L, 5, false)]
    // long.MinValue は正の対応値を持たない（符号反転が overflow する）ため桁数を定数で返す経路
    [InlineData(long.MinValue, 19, true)]
    [InlineData(long.MinValue, 18, false)]
    public void 整数桁数の判定(long value, int maxDigits, bool expected)
    {
        List<string>? errors = null;

        ValueObjectNumberRules.ValidateMaxDigits(value, maxDigits, Label, ref errors);

        if (expected)
        {
            errors.Should().BeNull();
        }
        else
        {
            errors.Should().ContainSingle().Which.Should().Be($"Enter at most {maxDigits} digits.");
        }
    }

    // ===== ValueObjectNumberRules.ValidateRange =====

    [Theory(DisplayName = "[範囲] 閉区間（上下端を含む）で判定する")]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void 範囲は閉区間で判定する(int value, bool expected)
    {
        List<string>? errors = null;

        ValueObjectNumberRules.ValidateRange(value, 1, 10, Label, ref errors);

        if (expected)
        {
            errors.Should().BeNull();
        }
        else
        {
            errors.Should().ContainSingle().Which.Should().Be("Enter a value between 1 and 10.");
        }
    }

    [Fact(DisplayName = "[範囲] IComparable<T> なら数値以外（日時）でも同じ判定になる")]
    public void 範囲は日時にも効く()
    {
        var min = new DateTime(2026, 1, 1);
        var max = new DateTime(2026, 12, 31);

        List<string>? passing = null;

        ValueObjectNumberRules.ValidateRange(
            new DateTime(2026, 6, 1),
            min,
            max,
            Label,
            ref passing
        );
        passing.Should().BeNull();

        List<string>? failing = null;

        ValueObjectNumberRules.ValidateRange(
            new DateTime(2027, 1, 1),
            min,
            max,
            Label,
            ref failing
        );
        failing.Should().ContainSingle();
    }

    // ===== ValueObjectStringRules.ValidateAsciiAlphanumeric =====

    [Theory(DisplayName = "[ASCII 英数字] 全角は弾き、許可記号だけを追加で通す")]
    [InlineData("", "", true)]
    [InlineData("abcXYZ012", "", true)]
    [InlineData("abc-123", "", false)]
    [InlineData("abc-123", "-", true)]
    [InlineData("abc-12_3", "-_", true)]
    [InlineData("abc 123", "", false)]
    // 全角英数字は「見た目が同じ別の文字」なので弾く（コード列・識別子列の用途）
    [InlineData("ａbc", "", false)]
    [InlineData("abc１23", "", false)]
    [InlineData("日本語", "", false)]
    public void ASCII英数字の判定(string value, string allowedSymbols, bool expected)
    {
        List<string>? errors = null;

        ValueObjectStringRules.ValidateAsciiAlphanumeric(value, allowedSymbols, Label, ref errors);

        if (expected)
        {
            errors.Should().BeNull();
        }
        else
        {
            errors.Should().ContainSingle();
        }
    }

    [Fact(DisplayName = "[ASCII 英数字] 違反文字が複数あってもエラーは 1 件（文言は規則を述べる）")]
    public void ASCII英数字の違反は1件に畳む()
    {
        List<string>? errors = null;

        ValueObjectStringRules.ValidateAsciiAlphanumeric(
            "a-b-c-d",
            string.Empty,
            Label,
            ref errors
        );

        errors.Should().ContainSingle().Which.Should().Be("Enter ASCII letters and digits only.");
    }

    [Fact(DisplayName = "[ASCII 英数字] 許可記号があると既定文言はその記号を列挙する")]
    public void ASCII英数字の文言は許可記号を含む()
    {
        List<string>? errors = null;

        ValueObjectStringRules.ValidateAsciiAlphanumeric("a b", "-_", Label, ref errors);

        errors
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("Enter ASCII letters, digits, and any of \"-_\" only.");
    }

    // ===== ValueObjectStringRules.ValidateEmailAddress =====

    [Theory(DisplayName = "[メールアドレス] @ ちょうど 1 つ・前後が非空・空白なしの実用最小判定")]
    [InlineData("a@b", true)]
    [InlineData("user.name+tag@example.co.jp", true)]
    [InlineData("user@example", true)]
    [InlineData("", false)]
    [InlineData("example.com", false)]
    [InlineData("@example.com", false)]
    [InlineData("user@", false)]
    [InlineData("a@b@c", false)]
    [InlineData("user name@example.com", false)]
    [InlineData("user@exa mple.com", false)]
    [InlineData(" user@example.com", false)]
    [InlineData("user@example.com ", false)]
    // 表示名付き形式（RFC のヘッダ表記）は通さない＝アドレス列に header 1 行が入るのを防ぐ
    [InlineData("Name <user@example.com>", false)]
    public void メールアドレスの判定(string value, bool expected)
    {
        List<string>? errors = null;

        ValueObjectStringRules.ValidateEmailAddress(value, Label, ref errors);

        if (expected)
        {
            errors.Should().BeNull();
        }
        else
        {
            errors.Should().ContainSingle().Which.Should().Be("Enter a valid email address.");
        }
    }

    // ===== ValueObjectDecimalRules.Validate =====

    [Fact(DisplayName = "[decimal] 小数部は宣言スケール基準（末尾ゼロを数える）で、丸めずに弾く")]
    public void decimal規則はスケールを丸めない()
    {
        List<string>? errors = null;

        ValueObjectDecimalRules.Validate(1.230m, 10, 2, Label, ref errors);

        errors
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("Enter at most 2 digits after the decimal point.");

        List<string>? passing = null;

        ValueObjectDecimalRules.Validate(1.20m, 10, 2, Label, ref passing);
        passing.Should().BeNull();
    }

    [Theory(
        DisplayName = "[decimal] 整数部は precision - scale 桁ちょうどまで通る（符号は数えない）"
    )]
    [InlineData("0", true)]
    [InlineData("99999999", true)]
    [InlineData("-99999999", true)]
    [InlineData("100000000", false)]
    [InlineData("-100000000", false)]
    public void decimal規則の整数部境界(string literal, bool expected)
    {
        List<string>? errors = null;

        ValueObjectDecimalRules.Validate(decimal.Parse(literal), 10, 2, Label, ref errors);

        if (expected)
        {
            errors.Should().BeNull();
        }
        else
        {
            errors
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be("Enter at most 8 digits in the integer part.");
        }
    }

    [Fact(DisplayName = "[decimal] スケール超過と整数部超過は同時に 2 件積む")]
    public void decimal規則は複数違反を積む()
    {
        List<string>? errors = null;

        ValueObjectDecimalRules.Validate(123456789.123m, 10, 2, Label, ref errors);

        errors.Should().HaveCount(2);
    }

    [Fact(DisplayName = "[decimal] decimal が表せる桁数を超える上限は整数部の違反を出さない")]
    public void decimal規則は表現上限より広い制限で常に通す()
    {
        // decimal の有効桁は 29 桁までなので、整数部 38 桁の宣言（DB 側の最大 precision）は
        // どんな値でも超えられない＝比較表を引かずに素通しする経路
        List<string>? errors = null;

        ValueObjectDecimalRules.Validate(decimal.MaxValue, 38, 0, Label, ref errors);

        errors.Should().BeNull();
    }
}
