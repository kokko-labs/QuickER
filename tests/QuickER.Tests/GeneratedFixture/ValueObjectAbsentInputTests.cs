using System;
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
/// 決まった形の表明（生成・引き当て・拒否・呼び形・表示など）は <see cref="ValueObjectScenarioTests"/> の表にある。ここに残すのは割り当ての測定だけ。
/// </remarks>
public sealed class ValueObjectAbsentInputTests
{
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

        long delta = long.MaxValue;

        // tier-up の完了時期はプロセス全体の負荷（テスト件数）に左右されるため、固定ラウンド数の
        // 「最後のラウンド」では tier-0 のまま測ってしまうことがある。ゼロのラウンドが観測できるまで
        // 最大 10 ラウンド測る＝本当に確保する実装は全ラウンドで正になるので、網の強さは変わらない
        for (var round = 0; round < 10 && delta != 0; round++)
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
