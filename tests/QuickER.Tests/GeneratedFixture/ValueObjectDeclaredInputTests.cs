using System;
using System.ComponentModel.DataAnnotations;
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
/// 決まった形の表明（生成・引き当て・拒否・呼び形・表示など）は <see cref="ValueObjectScenarioTests"/> の表にある。ここに残すのは割り当ての測定だけ。
/// </remarks>
public sealed class ValueObjectDeclaredInputTests
{
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

        long delta = long.MaxValue;

        // tier-up の完了時期はプロセス全体の負荷（テスト件数）に左右されるため、固定ラウンド数の
        // 「最後のラウンド」では tier-0 のまま測ってしまうことがある。ゼロのラウンドが観測できるまで
        // 最大 10 ラウンド測る＝本当に確保する実装は全ラウンドで正になるので、網の強さは変わらない
        for (var round = 0; round < 10 && delta != 0; round++)
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
