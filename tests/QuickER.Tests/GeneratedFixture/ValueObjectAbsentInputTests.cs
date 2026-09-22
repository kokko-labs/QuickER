using System;
using System.Collections.Generic;
using System.Globalization;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// <see cref="ScreenMode"/> の生成形ミラーへ、テンプレートが出力する ConvertAbsentInput の
/// ブリッジ＋partial フック宣言を足した部分（per-VO 出力のミラー＝<c>_01_ValueObjects.scriban</c> と同形）。
/// </summary>
public sealed partial class ScreenMode
{
    static bool IValueObject<ScreenMode>.TryConvertAbsentInput(
        IFormatProvider? provider,
        out ScreenMode? result
    )
    {
        ScreenMode? absent = null;
        ConvertAbsentInput(provider, ref absent);
        result = absent;
        return absent is not null;
    }

    static partial void ConvertAbsentInput(IFormatProvider? provider, ref ScreenMode? result);

    // 利用者側: 空欄は List として扱う（記法「空欄＝既定画面」の検証用）
    static partial void ConvertAbsentInput(IFormatProvider? provider, ref ScreenMode? result) =>
        result = List;
}

/// <summary>実生成 VO（<see cref="BioValue"/>）へ空欄フックを実装した側＝生成された実ブリッジの配線検証用。</summary>
/// <remarks>
/// 空欄（null / DBNull / 空文字列）だけを専用インスタンスへ引き取る。空欄以外は素通しなので、
/// BioValue を使う他のテストへは影響しない（<c>CreateFrom("plain")</c> は従来どおり）。
/// </remarks>
public sealed partial class BioValue
{
    /// <summary>空欄フックが返す専用インスタンス（素の生成経路で作って保持する）。</summary>
    public static readonly BioValue AbsentBio = Create("#absent");

    static partial void ConvertAbsentInput(IFormatProvider? provider, ref BioValue? result) =>
        result = AbsentBio;
}

/// <summary>
/// 手書き VO が interface の <c>TryConvertAbsentInput</c> を直接実装する側（docs のレシピと同形）。
/// 空欄セルを False（未マーク）として取り込むフラグ＝「○ か空欄か」の記法の空欄側を型が引き取る。
/// </summary>
public sealed class InspectionMark
    : ValueObjectBooleanBase<InspectionMark>,
        IValueObject<InspectionMark, bool>
{
    public static readonly InspectionMark Marked = new(true);
    public static readonly InspectionMark Unmarked = new(false);

    private InspectionMark(bool value)
        : base(value) { }

    static InspectionMark IValueObject<InspectionMark, bool>.New(bool value) =>
        value ? Marked : Unmarked;

    // 記法の「書かれる側」（○）は変換フックで受ける
    static bool IValueObject<InspectionMark>.TryConvertCustomInput(
        object raw,
        IFormatProvider? provider,
        out InspectionMark? result
    )
    {
        result = raw is "○" ? Marked : null;

        return result is not null;
    }

    // 記法の「空欄側」はこちらで受ける（フックの中から Create 系を呼ばない＝再帰する）
    static bool IValueObject<InspectionMark>.TryConvertAbsentInput(
        IFormatProvider? provider,
        out InspectionMark? result
    )
    {
        result = Unmarked;

        return true;
    }
}

/// <summary>
/// 空欄入力（null / DBNull / 空文字列）を型ごとの既定インスタンスへ引き取る
/// <c>ConvertAbsentInput</c> フック（ブリッジ <c>TryConvertAbsentInput</c>）の検証。
/// </summary>
/// <remarks>
/// 基底の <c>TryCreateFrom</c> が空欄早期リターンの内側で <c>TSelf.TryConvertAbsentInput</c> を
/// 照会することを全呼び形で固定する（<c>TryConvertCustomInput</c> と同じ「TSelf. 経由」の不変条件）。
/// フック未実装の型は従来どおり「成功＋null」で、割り当てもゼロのまま。
/// </remarks>
public sealed class ValueObjectAbsentInputTests
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

    // 基底がどの呼び形でも TSelf.TryConvertAbsentInput を照会することを 4 形すべてで固定する
    // （TryConvertCustomInput の「全呼び形で効く」と同じ網＝基底の照会を外すとここだけが赤くなる）。
    [Fact(DisplayName = "[空欄フック] 空欄フックはどの呼び形でも効く（4 引数の型名呼びを含む）")]
    public void 空欄フックは全呼び形で効く()
    {
        // 4 引数・具象型名
        ScreenMode
            .TryCreateFrom("", CultureInfo.InvariantCulture, out var viaFourArgs, out var errors1)
            .Should()
            .BeTrue();
        viaFourArgs.Should().BeSameAs(ScreenMode.List);
        errors1.Should().BeEmpty();

        // 3 引数・具象型名（null 入力）
        ScreenMode.TryCreateFrom(null, out var viaThreeArgs, out _).Should().BeTrue();
        viaThreeArgs.Should().BeSameAs(ScreenMode.List);

        // CreateFrom（例外版・DBNull 入力）
        ScreenMode
            .CreateFrom(DBNull.Value, CultureInfo.InvariantCulture)
            .Should()
            .BeSameAs(ScreenMode.List);

        // 型引数経由（ジェネリック取り込み経路）
        var errors = new List<string>();
        ReadCell<ScreenMode>("", errors).Should().BeSameAs(ScreenMode.List);
        errors.Should().BeEmpty();

        // 空欄でない入力は空欄フックを通らない（変換フック・通常変換は従来どおり）
        ScreenMode.CreateFrom("Edit").Should().BeSameAs(ScreenMode.Edit);
        ScreenMode.CreateFrom(2, CultureInfo.InvariantCulture).Should().BeSameAs(ScreenMode.Edit);
    }

    // 実際に生成されたブリッジ（GeneratedFixture.g.cs の BioValue）が partial フックへ配線されていることの検証。
    // ScreenMode はテスト内のミラーなので、テンプレート出力そのものはこちらで固定する。
    [Fact(DisplayName = "[空欄フック] 実生成 VO のブリッジが partial フックへ配線されている")]
    public void 実生成VOのブリッジが配線されている()
    {
        BioValue
            .TryCreateFrom("", CultureInfo.InvariantCulture, out var fromEmpty, out _)
            .Should()
            .BeTrue();
        fromEmpty.Should().BeSameAs(BioValue.AbsentBio);

        BioValue.CreateFrom(DBNull.Value).Should().BeSameAs(BioValue.AbsentBio);

        var errors = new List<string>();
        ReadCell<BioValue>(null, errors).Should().BeSameAs(BioValue.AbsentBio);
        errors.Should().BeEmpty();

        // 空白のみの文字列は「空欄」ではない＝フックを通らず通常の生成になる（空欄述語の境界の固定）
        BioValue.CreateFrom(" ")!.Value.Should().Be(" ");
        BioValue.CreateFrom(" ").Should().NotBeSameAs(BioValue.AbsentBio);

        // 空欄以外は素通し（他テストへの不干渉の裏取り）
        BioValue.CreateFrom("plain")!.Value.Should().Be("plain");
    }

    [Fact(DisplayName = "[空欄フック] 手書き VO は interface の直接実装でも空欄を引き取れる")]
    public void 手書きVOの直接実装でも効く()
    {
        // 空欄側（4 引数の型名呼び＝基底へ静的束縛される形でもフックが効く）
        InspectionMark
            .TryCreateFrom("", CultureInfo.InvariantCulture, out var blank, out _)
            .Should()
            .BeTrue();
        blank.Should().BeSameAs(InspectionMark.Unmarked);
        InspectionMark.CreateFrom(null).Should().BeSameAs(InspectionMark.Unmarked);

        // 書かれる側（○）は従来の変換フックのまま＝2 つのフックで 1 つの記法が閉じる
        InspectionMark.CreateFrom("○").Should().BeSameAs(InspectionMark.Marked);

        var errors = new List<string>();
        ReadCell<InspectionMark>(DBNull.Value, errors).Should().BeSameAs(InspectionMark.Unmarked);
        errors.Should().BeEmpty();
    }

    // フックを書かない型の空欄挙動が変わっていないこと（既定は「成功＋null・エラーなし」のまま）。
    [Fact(DisplayName = "[空欄フック] フックを書かない型の空欄入力は従来どおり成功＋null")]
    public void フック未実装型の空欄挙動は不変()
    {
        // 生成 VO（ブリッジはあるが partial 未実装＝呼び出しごと消える）
        CustomerIdValue.TryCreateFrom("", out var empty, out var errors1).Should().BeTrue();
        empty.Should().BeNull();
        errors1.Should().BeEmpty();

        CustomerIdValue
            .TryCreateFrom(null, CultureInfo.InvariantCulture, out var fromNull, out _)
            .Should()
            .BeTrue();
        fromNull.Should().BeNull();

        CustomerIdValue.CreateFrom(DBNull.Value).Should().BeNull();

        // 手書き VO（interface の既定実装が効く＝実装なしで挙動不変）
        DataAccessMode.TryCreateFrom("", out var handwritten, out var errors2).Should().BeTrue();
        handwritten.Should().BeNull();
        errors2.Should().BeEmpty();
    }

    // フック未実装の型では、空欄入力の経路が現行どおり割り当てゼロであること
    // （ブリッジ＋既定実装の照会が挟まっても、確保は増えない）。
    [Fact(DisplayName = "[空欄フック] フック未実装型の空欄入力は割り当てゼロのまま")]
    public void フック未実装型の空欄経路は割り当てゼロ()
    {
        // ウォームアップ（JIT・tier-up 由来の割り当てを測定から追い出す）
        for (var i = 0; i < 50_000; i++)
        {
            CustomerIdValue.TryCreateFrom("", out _, out _);
        }

        long delta = 0;

        // 測定中に tier-up が走った回を拾わないよう 3 ラウンド測り、最後のラウンドで表明する
        for (var round = 0; round < 3; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < 10_000; i++)
            {
                CustomerIdValue.TryCreateFrom("", out _, out _);
            }

            delta = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        delta.Should().Be(0, "空欄の早期リターンはフック照会を挟んでも何も確保しない");
    }
}
