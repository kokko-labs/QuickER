using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 「○ か空欄か」で書かれる bool フラグの宣言的定義: InputText が書かれる側・ClaimsAbsent が空欄側を
/// 引き取り、表示（[Display]）とあわせて記法が宣言 2 行で閉じる。
/// </summary>
public sealed class CheckMark : ValueObjectBooleanBase<CheckMark>, IValueObject<CheckMark, bool>
{
    [DeclaredInstance(InputText = "○"), Display(Name = "○")]
    public static readonly CheckMark Marked = new(true);

    [DeclaredInstance(ClaimsAbsent = true), Display(Name = "")]
    public static readonly CheckMark Unmarked = new(false);

    private CheckMark(bool value)
        : base(value) { }

    static CheckMark IValueObject<CheckMark, bool>.New(bool value) => new(value);

    public override string DisplayValue => DeclaredDisplayName ?? base.DisplayValue;
}

/// <summary>int のコード列挙 × 入力表記（「済」→ Done）。表記なしの宣言（Open）は通常変換だけで引ける。</summary>
public sealed class WorkState : ValueObjectOrderedBase<WorkState, int>, IValueObject<WorkState, int>
{
    [DeclaredInstance(InputText = "済"), Display(Name = "済")]
    public static readonly WorkState Done = new(9);

    [DeclaredInstance]
    public static readonly WorkState Open = new(1);

    private WorkState(int value)
        : base(value) { }

    static WorkState IValueObject<WorkState, int>.New(int value) => new(value);
}

/// <summary>
/// フック（ConvertCustomInput / ConvertAbsentInput）と宣言表記（InputText / ClaimsAbsent）を併用する
/// 生成形ミラー＝フック優先の検証用。フックは同じ記法を別インスタンスへ引き当てる。
/// </summary>
public sealed partial class OverrideMark
    : ValueObjectBooleanBase<OverrideMark>,
        IValueObject<OverrideMark, bool>
{
    private OverrideMark(bool value)
        : base(value) { }

    static OverrideMark IValueObject<OverrideMark, bool>.New(bool value) => new(value);

    static bool IValueObject<OverrideMark>.TryConvertCustomInput(
        object raw,
        IFormatProvider? provider,
        out OverrideMark? result
    )
    {
        OverrideMark? custom = null;
        ConvertCustomInput(raw, provider, ref custom);
        result = custom;
        return custom is not null;
    }

    static partial void ConvertCustomInput(
        object raw,
        IFormatProvider? provider,
        ref OverrideMark? result
    );

    static bool IValueObject<OverrideMark>.TryConvertAbsentInput(
        IFormatProvider? provider,
        out OverrideMark? result
    )
    {
        OverrideMark? absent = null;
        ConvertAbsentInput(provider, ref absent);
        result = absent;
        return absent is not null;
    }

    static partial void ConvertAbsentInput(IFormatProvider? provider, ref OverrideMark? result);
}

/// <summary>利用者側: 宣言表記と同じ「○」「空欄」をフックが別インスタンスへ引き当てる（フック優先の観測用）。</summary>
public sealed partial class OverrideMark
{
    [DeclaredInstance(InputText = "○"), Display(Name = "○")]
    public static readonly OverrideMark Marked = new(true);

    [DeclaredInstance(ClaimsAbsent = true)]
    public static readonly OverrideMark Unmarked = new(false);

    /// <summary>フックが「○」に対して返す別インスタンス（属性なし＝レジストリには載らない）。</summary>
    public static readonly OverrideMark HookMarked = new(true);

    /// <summary>フックが空欄に対して返す別インスタンス（属性なし）。</summary>
    public static readonly OverrideMark HookAbsent = new(false);

    static partial void ConvertCustomInput(
        object raw,
        IFormatProvider? provider,
        ref OverrideMark? result
    )
    {
        if (raw is "○")
        {
            result = HookMarked;
        }
    }

    static partial void ConvertAbsentInput(IFormatProvider? provider, ref OverrideMark? result) =>
        result = HookAbsent;
}

/// <summary>同じ InputText を 2 フィールドが宣言＝初回使用時 fail-fast の検証用。</summary>
public sealed class DupInputState
    : ValueObjectOrderedBase<DupInputState, int>,
        IValueObject<DupInputState, int>
{
    [DeclaredInstance(InputText = "×")]
    public static readonly DupInputState A = new(1);

    [DeclaredInstance(InputText = "×")]
    public static readonly DupInputState B = new(2);

    private DupInputState(int value)
        : base(value) { }

    static DupInputState IValueObject<DupInputState, int>.New(int value) => new(value);
}

/// <summary>ClaimsAbsent を 2 フィールドが宣言＝初回使用時 fail-fast の検証用。</summary>
public sealed class DoubleAbsentState
    : ValueObjectOrderedBase<DoubleAbsentState, int>,
        IValueObject<DoubleAbsentState, int>
{
    [DeclaredInstance(ClaimsAbsent = true)]
    public static readonly DoubleAbsentState A = new(1);

    [DeclaredInstance(ClaimsAbsent = true)]
    public static readonly DoubleAbsentState B = new(2);

    private DoubleAbsentState(int value)
        : base(value) { }

    static DoubleAbsentState IValueObject<DoubleAbsentState, int>.New(int value) => new(value);
}

/// <summary>InputText = ""（空欄述語に先取りされ絶対に一致しない設定）＝初回使用時 fail-fast の検証用。</summary>
public sealed class EmptyInputState
    : ValueObjectOrderedBase<EmptyInputState, int>,
        IValueObject<EmptyInputState, int>
{
    [DeclaredInstance(InputText = "")]
    public static readonly EmptyInputState A = new(1);

    private EmptyInputState(int value)
        : base(value) { }

    static EmptyInputState IValueObject<EmptyInputState, int>.New(int value) => new(value);
}

/// <summary>
/// [DeclaredInstance] の入力表記（InputText / ClaimsAbsent）の検証。
/// </summary>
/// <remarks>
/// 基底 TryCreateFrom の 2 駅（空欄: フック → ClaimsAbsent → null／文字列入力: フック → InputText →
/// 通常変換）を、成功・不一致・フック優先・fail-fast で固定する。照合は Ordinal 完全一致・Trim なし・
/// string 入力のみで、[Display] の名前は入力として受けない。
/// </remarks>
public sealed class ValueObjectDeclaredInputTests
{
    private static T? ReadCell<T>(object? cell, List<string> errors)
        where T : class, IValueObject<T>
    {
        if (T.TryCreateFrom(cell, null, out var value, out var messages))
        {
            return value;
        }

        errors.AddRange(messages);

        return null;
    }

    [Fact(DisplayName = "[入力表記] InputText はどの呼び形でも効く（4 引数の型名呼びを含む）")]
    public void InputTextは全呼び形で効く()
    {
        // 4 引数・具象型名
        CheckMark
            .TryCreateFrom("○", CultureInfo.InvariantCulture, out var viaFourArgs, out var errors1)
            .Should()
            .BeTrue();
        viaFourArgs.Should().BeSameAs(CheckMark.Marked);
        errors1.Should().BeEmpty();

        // 3 引数・具象型名
        CheckMark.TryCreateFrom("○", out var viaThreeArgs, out _).Should().BeTrue();
        viaThreeArgs.Should().BeSameAs(CheckMark.Marked);

        // CreateFrom（例外版）
        CheckMark.CreateFrom("○").Should().BeSameAs(CheckMark.Marked);

        // 型引数経由（ジェネリック取り込み経路）
        var errors = new List<string>();
        ReadCell<CheckMark>("○", errors).Should().BeSameAs(CheckMark.Marked);
        errors.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "[入力表記] ClaimsAbsent は空欄 3 形（null / DBNull / 空文字）すべてで効く"
    )]
    public void ClaimsAbsentは空欄3形で効く()
    {
        CheckMark.TryCreateFrom("", out var fromEmpty, out var errors).Should().BeTrue();
        fromEmpty.Should().BeSameAs(CheckMark.Unmarked);
        errors.Should().BeEmpty();

        CheckMark.CreateFrom(null).Should().BeSameAs(CheckMark.Unmarked);
        CheckMark
            .CreateFrom(DBNull.Value, CultureInfo.InvariantCulture)
            .Should()
            .BeSameAs(CheckMark.Unmarked);
    }

    // 照合規則の固定: Ordinal 完全一致・Trim なし。空白や末尾スペースの揺れは受けない（受けたければフックの領分）
    [Fact(DisplayName = "[入力表記] 空白のみ・表記ゆれの文字列は一致せず通常変換のまま")]
    public void 不一致の文字列は通常変換のまま()
    {
        // 空白のみは「空欄」でも「○」でもない → bool への通常変換に失敗して検証エラー
        CheckMark.TryCreateFrom(" ", out var whitespace, out var errors1).Should().BeFalse();
        whitespace.Should().BeNull();
        errors1.Should().ContainSingle();

        // 末尾スペース付きは Trim されない＝不一致
        CheckMark.TryCreateFrom("○ ", out _, out var errors2).Should().BeFalse();
        errors2.Should().ContainSingle();
    }

    [Fact(
        DisplayName = "[入力表記] 非文字列と通常変換可能な文字列は従来どおり変換され、同じ宣言済みインスタンスに解決される"
    )]
    public void 非文字列は通常変換経由で同じインスタンスに解決される()
    {
        // bool そのもの → 通常変換 → Materialize のレジストリが宣言済みへ引き当てる
        CheckMark.CreateFrom(true).Should().BeSameAs(CheckMark.Marked);

        // bool へ変換可能な文字列も従来どおり（表記の追加は通常変換を奪わない）
        CheckMark.CreateFrom("True").Should().BeSameAs(CheckMark.Marked);
    }

    [Fact(DisplayName = "[入力表記] int のコード列挙でも表記と通常変換が同居する")]
    public void intコード列挙でも効く()
    {
        WorkState.CreateFrom("済").Should().BeSameAs(WorkState.Done);

        // 数字の文字列は通常変換 → 宣言済みへ解決（表記なしの Open も同様）
        WorkState.CreateFrom("9").Should().BeSameAs(WorkState.Done);
        WorkState.CreateFrom("1").Should().BeSameAs(WorkState.Open);

        // 閉じた集合の拒否（既存機能）はそのまま効いている
        WorkState.TryCreate(5, out _, out var errors).Should().BeFalse();
        errors.Should().ContainSingle();
    }

    // [Display] の名前は表示専用で、入力としては受けない（InputText の明示オプトインだけが入力になる）。
    // 将来「Display 名の自動パース」が紛れ込むとここが赤くなる。
    [Fact(DisplayName = "[入力表記] Display の名前は入力として受けない")]
    public void Display名は入力にならない()
    {
        MeasureStatus.TryCreateFrom("拾い出し", out var value, out var errors).Should().BeFalse();
        value.Should().BeNull();
        errors.Should().ContainSingle();
    }

    [Fact(DisplayName = "[入力表記] フックは宣言表記より優先される（書かれる側・空欄側とも）")]
    public void フックが宣言表記より優先される()
    {
        // 「○」: フックが HookMarked を返す → InputText の Marked ではない
        OverrideMark.CreateFrom("○").Should().BeSameAs(OverrideMark.HookMarked);
        OverrideMark.CreateFrom("○").Should().NotBeSameAs(OverrideMark.Marked);

        // 空欄: フックが HookAbsent を返す → ClaimsAbsent の Unmarked ではない
        OverrideMark.CreateFrom(null).Should().BeSameAs(OverrideMark.HookAbsent);
        OverrideMark.CreateFrom("").Should().NotBeSameAs(OverrideMark.Unmarked);
    }

    [Fact(
        DisplayName = "[入力表記] InputText の重複・ClaimsAbsent の複数・空の InputText は初回使用時に fail-fast する"
    )]
    public void 不正な宣言はfailfastする()
    {
        var dup = () => DupInputState.Create(1);
        dup.Should().Throw<InvalidOperationException>().WithMessage("*InputText '×'*");

        var doubleAbsent = () => DoubleAbsentState.Create(1);
        doubleAbsent
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*claim the absent input more than once*");

        var empty = () => EmptyInputState.Create(1);
        empty.Should().Throw<InvalidOperationException>().WithMessage("*empty InputText*");
    }

    [Fact(DisplayName = "[入力表記] 表示側は Display 名がそのまま出る（空文字の Display も含む）")]
    public void 表示はDisplay名がそのまま出る()
    {
        CheckMark.Marked.DisplayValue.Should().Be("○");

        // Display(Name = "") は「空欄として表示する」の宣言＝旧 DisplaySymbol の置き換え
        CheckMark.Unmarked.DisplayValue.Should().Be("");
    }

    // InputText 一致パス（空欄述語 → フック照会 → レジストリの線形走査 → 既存インスタンス返却）が
    // 何も確保しないこと。表記の照合を LINQ 化・正規化付きへ書き換えると赤くなる。
    [Fact(DisplayName = "[入力表記] InputText 一致パスは割り当てゼロ")]
    public void InputText一致パスは割り当てゼロ()
    {
        // ウォームアップ（JIT・tier-up・初回走査の確保を測定から追い出す）
        for (var i = 0; i < 50_000; i++)
        {
            CheckMark.TryCreateFrom("○", out _, out _);
        }

        long delta = 0;

        // 測定中に tier-up が走った回を拾わないよう 3 ラウンド測り、最後のラウンドで表明する
        for (var round = 0; round < 3; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < 10_000; i++)
            {
                CheckMark.TryCreateFrom("○", out _, out _);
            }

            delta = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        delta.Should().Be(0, "宣言表記の照合と引き当ては何も確保しない");
    }
}
